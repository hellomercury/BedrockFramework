/********************************************************           
BEDROCKFRAMEWORK : https://github.com/GainDeveloper/BedrockFramework
Receives all data from the host.
Stores list of connections.
// TODO: Ownership, which allows connections to take control of updating.
********************************************************/
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using BedrockFramework.Pool;
using BedrockFramework.Utilities;
using System;

namespace BedrockFramework.Network
{
    interface INetworkComponent
    {
        int NumNetVars { get; }
        bool[] GetNetVarsToUpdate();
        void WriteUpdatedNetVars(NetworkWriter toWrite, bool forceAll);
        void ReadUpdatedNetVars(NetworkReader reader, bool[] updatedNetVars, int currentPosition, bool forceAll, float sendRate);
    }

    [HideMonoScript, AddComponentMenu("BedrockFramework/Network GameObject")]
    public class NetworkGameObject : MonoBehaviour, IPool
    {
        public byte updatesPerSecond  = 3;
        public NetworkGameObjectTransform networkTransform = new NetworkGameObjectTransform();
        public NetworkGameObjectRigidbody networkRigidbody = new NetworkGameObjectRigidbody();
        public NetworkGameObjectAnimator networkAnimator = new NetworkGameObjectAnimator();

        [ReadOnly, ShowInInspector]
        private PoolDefinition poolDefinition;
        [ReadOnly, ShowInInspector]
        private short networkID = 0;

        private Coroutine activeLoop;
        private List<INetworkComponent> activeNetworkComponents;
        private float lastUpdateTime;

        public short NetworkID { get { return networkID; } }

        // Pool
        PoolDefinition IPool.PoolDefinition { set { poolDefinition = value; } }

        void IPool.OnSpawn()
        {
            activeNetworkComponents = GetComponents<INetworkComponent>().ToList();

            if (networkRigidbody.enabled)
            {
                networkRigidbody.Setup(gameObject.GetComponent<Rigidbody>());
                activeNetworkComponents.Add((INetworkComponent)networkRigidbody);
            }
            if (networkAnimator.enabled)
            {
                networkAnimator.Setup(gameObject.GetComponent<Animator>());
                activeNetworkComponents.Add((INetworkComponent)networkAnimator);
            }

            networkTransform.Setup(gameObject.transform);
            if (networkTransform.enabled)
                activeNetworkComponents.Add((INetworkComponent)networkTransform);

            ServiceLocator.NetworkService.OnBecomeHost += HostGameObject;
            if (ServiceLocator.NetworkService.IsActive && ServiceLocator.NetworkService.IsHost)
                HostGameObject();
        }

        void IPool.OnDeSpawn()
        {
            ServiceLocator.NetworkService.OnBecomeHost -= HostGameObject;
            NetworkService_OnStop();
        }

        private void HostGameObject()
        {
            if (!ServiceLocator.NetworkService.IsHost)
                DevTools.Logger.LogError(NetworkService.NetworkLog, "None host is trying to host a network game object!");

            if (networkID == 0)
                networkID = ServiceLocator.NetworkService.UniqueNetworkID;

            ServiceLocator.NetworkService.AddNetworkGameObject(this);

            // Tell all active connections about our creation.
            if (ServiceLocator.NetworkService.IsActive)
            {
                Host_SendGameObject(ServiceLocator.NetworkService.ActiveSocket.ActiveConnections().Where(x => x.CurrentState == NetworkConnectionState.Ready).ToArray());
            }

            activeLoop = StartCoroutine(UpdateLoop());
            ServiceLocator.NetworkService.OnNetworkConnectionReady += OnNetworkConnectionReady;
            ServiceLocator.NetworkService.OnStop += NetworkService_OnStop;
        }

        private void NetworkService_OnStop()
        {
            if (activeLoop != null)
            {
                StopCoroutine(activeLoop);
                activeLoop = null;
            }

            ServiceLocator.NetworkService.RemoveNetworkGameObject(this);
            ServiceLocator.NetworkService.OnNetworkConnectionReady -= OnNetworkConnectionReady;
            ServiceLocator.NetworkService.OnStop -= NetworkService_OnStop;
        }

        private void OnNetworkConnectionReady(NetworkConnection readyConnection)
        {
            if (!ServiceLocator.NetworkService.IsHost)
                DevTools.Logger.LogError(NetworkService.NetworkLog, "None host is trying to handle a connection ready network game object request!");

            Host_SendGameObject(new NetworkConnection[] { readyConnection });
        }

        //
        // Initialization
        //

        void Host_SendGameObject(NetworkConnection[] receivers)
        {
            NetworkWriter writer = ServiceLocator.NetworkService.ActiveSocket.Writer.Setup(ServiceLocator.NetworkService.ActiveSocket.ReliableChannel, MessageTypes.BRF_Client_Receive_GameObject);
            writer.Write(ServiceLocator.SaveService.SavedObjectReferences.GetSavedObjectID(poolDefinition));
            writer.Write(networkID);

            // Send initial data for active components.
            for (int i = 0; i < activeNetworkComponents.Count; i++)
            {
                activeNetworkComponents[i].WriteUpdatedNetVars(writer, true);
            }

            for (int i = 0; i < receivers.Length; i++)
                ServiceLocator.NetworkService.ActiveSocket.Writer.Send(receivers[i], () => "NetworkGameObject Init");
        }

        public void Client_ReceiveGameObject(NetworkReader reader)
        {
            networkID = reader.ReadInt16();
            lastUpdateTime = Time.time;
            ServiceLocator.NetworkService.AddNetworkGameObject(this);

            int currentPosition = 0;
            for (int i = 0; i < activeNetworkComponents.Count; i++)
            {
                activeNetworkComponents[i].ReadUpdatedNetVars(reader, null, currentPosition, true, 0);
                currentPosition += activeNetworkComponents[i].NumNetVars;
            }
        }

        //
        // Host Update Loop
        //

        int NumNetVars { get {
                int i = 0;
                foreach (INetworkComponent comp in activeNetworkComponents)
                    i += comp.NumNetVars;
                return i;
            } }

        // Collects all NetVars in active network components and send those that have been updated.
        IEnumerator UpdateLoop()
        {
            bool[] updatedNetVars = new bool[NumNetVars];

            while (true)
            {
                yield return new WaitForSecondsRealtime(1f / updatesPerSecond);

                // Get updated netvars
                RefreshUpdatedNetVars(ref updatedNetVars);

                // Check if any NetVars were updated.
                if (updatedNetVars.ArrayContainsValue(true) && ServiceLocator.NetworkService.ActiveSocket.NumActiveConnections > 0)
                {
                    WriteAndSendNetVars(updatedNetVars);
                }
            }
        }

        private void WriteAndSendNetVars(bool[] updatedNetVars)
        {
            // Write updated net vars.
            NetworkWriter writer = ServiceLocator.NetworkService.ActiveSocket.Writer.Setup(ServiceLocator.NetworkService.ActiveSocket.UnreliableChannel, MessageTypes.BRF_Client_Update_GameObject);
            writer.Write(networkID);
            writer.Write(updatedNetVars.ToByteArray(), NumNetVars.BoolArraySizeToByteArraySize());

            for (int i = 0; i < activeNetworkComponents.Count; i++)
            {
                activeNetworkComponents[i].WriteUpdatedNetVars(writer, false);
            }

            // Send NetVars
            foreach (NetworkConnection active in ServiceLocator.NetworkService.ActiveSocket.ActiveConnections())
            {
                if (active.CurrentState != NetworkConnectionState.Ready)
                    continue;

                ServiceLocator.NetworkService.ActiveSocket.Writer.Send(active, () => "NetworkGameObject Update");
            }
        }

        /// <summary>
        /// Updates the list of bools with what vars want to be synced.
        /// </summary>
        /// <param name="updatedNetVars"></param>
        private void RefreshUpdatedNetVars(ref bool[] updatedNetVars)
        {
            int currentPosition = 0;
            for (int i = 0; i < activeNetworkComponents.Count; i++)
            {
                activeNetworkComponents[i].GetNetVarsToUpdate().CopyTo(updatedNetVars, currentPosition);
                currentPosition += activeNetworkComponents[i].NumNetVars;
            }
        }

        //
        // Client Update Loop
        //

        public void Client_ReceiveGameObjectUpdate(NetworkReader reader)
        {
            lastUpdateTime = Time.time;

            // Read the bool array with what has been updated.
            bool[] updatedNetVars = reader.ReadBytes(NumNetVars.BoolArraySizeToByteArraySize()).ToBoolArray();

            //Debug.Log(string.Join(", ", updatedNetVars.Select(x => x.ToString()).ToArray()));

            int currentPosition = 0;
            for (int i = 0; i < activeNetworkComponents.Count; i++)
            {
                activeNetworkComponents[i].ReadUpdatedNetVars(reader, updatedNetVars, currentPosition, false, 1f / updatesPerSecond);
                currentPosition += activeNetworkComponents[i].NumNetVars;
            }
        }
    }
}