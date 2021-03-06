/********************************************************           
BEDROCKFRAMEWORK : https://github.com/GainDeveloper/BedrockFramework

Saveable Scriptable Objects store references to themselves in a shared list.
TODO: Need to only store asset paths in the end to ensure we don't have all saveable assets loaded into memory.
********************************************************/
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using System;

namespace BedrockFramework.Saves
{
    [CreateAssetMenu(fileName = "SavedScriptableObjects", menuName = "BedrockFramework/SavedScriptableObjects", order = 0)]
    public class SavedObjectReferences : ScriptableObject, ISerializationCallbackReceiver
    {
        [ReadOnly, ShowInInspector]
        private Map<short, UnityEngine.Object> savedObjects = new Map<short, UnityEngine.Object>();
        [SerializeField, HideInInspector]
        private List<int> savedScriptableObjectKeys;
        [SerializeField, HideInInspector]
        private List<UnityEngine.Object> savedObjectValues;

        public void OnBeforeSerialize()
        {
            savedScriptableObjectKeys = new List<int>();
            savedObjectValues = new List<UnityEngine.Object>();
            IEnumerator enumerator = savedObjects.GetEnumerator();

            while (enumerator.MoveNext())
            {
                KeyValuePair<short, UnityEngine.Object> savedScriptableObject = (KeyValuePair<short, UnityEngine.Object>)enumerator.Current;
                savedScriptableObjectKeys.Add(savedScriptableObject.Key);
                savedObjectValues.Add(savedScriptableObject.Value);
            }
        }

        public void OnAfterDeserialize()
        {
            savedObjects = new Map<short, UnityEngine.Object>();

            for (int i = 0; i != savedScriptableObjectKeys.Count; i++)
                savedObjects.Add((short)savedScriptableObjectKeys[i], savedObjectValues[i]);
        }

        public T GetSavedObject<T>(short instanceID) where T : UnityEngine.Object
        {
            return savedObjects.Forward[instanceID] as T;
        }

        public short GetSavedObjectID(UnityEngine.Object objectInstance, bool logIfNone = true)
        {
            if (!savedObjects.Reverse.Contains(objectInstance))
            {
                if (logIfNone)
                    DevTools.Logger.LogError(SaveService.SaveServiceLog, "Received ID Request for {} but it has not been added to the saved references.", () => new object[] { objectInstance.name });
                return 0;
            }

            return savedObjects.Reverse[objectInstance];
        }

        public T[] GetObjectsOfType<T>() where T : UnityEngine.Object
        {
            return Array.ConvertAll<UnityEngine.Object, T>(savedObjectValues.Where(x => x.GetType() == typeof(T)).ToArray(), delegate (UnityEngine.Object i)
            {
                return (T)i;
            });
        }

        public void AddObject(UnityEngine.Object so)
        {
            savedObjects.Add((short)UnityEngine.Random.Range(1, short.MaxValue), so);
            Cleanup();
        }

        private void Cleanup()
        {
            List<short> toRemove = savedObjects.Where(x => x.Value == null).Select(x => x.Key).ToList();

            for (short i = 0; i != toRemove.Count; i++)
                savedObjects.Remove(toRemove[i]);
        }
    }
}