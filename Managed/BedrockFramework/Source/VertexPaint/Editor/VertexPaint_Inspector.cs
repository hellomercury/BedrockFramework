﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using BedrockFramework.Utilities;

namespace BedrockFramework.VertexPaint
{
    [CustomEditor(typeof(VertexPaint), true)]
    [CanEditMultipleObjects]
    public class VertexPaint_Inspector : Editor
    {
        [System.Serializable]
        class VertexPaint_InspectorSettings
        {
            private const string editorPrefsKey = "VertexPaintSettings";

            public float brushSize = 1;
            public float brushFalloff = 0.5f;
            public float brushDepth = 0.25f;
            public float brushNormalBias = 0.5f;
            public float brushStrength = 1.0f;

            public void SaveSettings()
            {
                EditorPrefs.SetString(editorPrefsKey, JsonUtility.ToJson(this));
            }

            public void LoadSettings()
            {
                if (!EditorPrefs.HasKey(editorPrefsKey))
                    return;

                JsonUtility.FromJsonOverwrite(EditorPrefs.GetString(editorPrefsKey), this);
            }
        }

        private const float vertexDisplaySize = 0.025f;
        private const float sceneViewWindowWidth = 300;
        private const float sceneViewWindowHeight = 150;
        private const float sceneViewWindowPadding = 10;
        private Color outerBrushColour = new Color(0.6f, 0.8f, 0.5f);
        private Color innerBrushColour = new Color(0.8f, 1f, 0.7f);

        VertexPaint_InspectorSettings vertexPaintSettings = new VertexPaint_InspectorSettings();

        Vector3 m_BrushPos;
        Vector3 m_BrushNorm;
        int m_BrushFace = -1;
        Plane mousePlane;

        struct VertexPaint_EditorInstance
        {
            public Transform transform;
            public VertexPaint vertexPaint;
            public Mesh localMesh;
        }

        VertexPaint_EditorInstance[] activeInstances;

        void OnEnable()
        {
            vertexPaintSettings.LoadSettings();
            activeInstances = new VertexPaint_EditorInstance[targets.Length];

            for (int i = 0; i < targets.Length; i++)
            {
                VertexPaint vertexPaint = (VertexPaint)targets[i];
                activeInstances[i] = new VertexPaint_EditorInstance { vertexPaint = vertexPaint, transform = vertexPaint.transform, localMesh = vertexPaint.GetComponent<MeshFilter>().sharedMesh };
            }
        }

        void OnDisable()
        {
            vertexPaintSettings.SaveSettings();
        }

        void OnSceneGUI()
        {
            if (Event.current.type == EventType.Repaint)
            {
                UpdatePreviewBrush();

                if (m_BrushFace >= 0)
                {
                    DrawBrush();
                    DrawVertices();
                }
            }

            if (m_BrushFace >= 0)
            {
                PaintSceneGUI();
            }

            Handles.BeginGUI();
            GUILayout.Window(0, new Rect(Screen.width - sceneViewWindowWidth - sceneViewWindowPadding, Screen.height - sceneViewWindowHeight - sceneViewWindowPadding - 18, sceneViewWindowWidth, sceneViewWindowHeight), PaintingWindow, "Vertex Painting");
            Handles.EndGUI();
        }

        void PaintSceneGUI()
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            HandleUtility.AddDefaultControl(id);

            EventType type = e.type;
            if (e.control || e.shift || e.alt)
                return;

            if (type == EventType.MouseDown || type == EventType.MouseDrag)
            {
                if (e.button == 0)
                {
                    // Paint stuff
                }
                else if (e.button == 1)
                {
                    vertexPaintSettings.brushSize += e.delta.x * Time.deltaTime;
                }
                else if (e.button == 2)
                {
                    vertexPaintSettings.brushFalloff += e.delta.x * Time.deltaTime;
                }

                e.Use();
            }
            else if (type == EventType.ScrollWheel)
            {
                vertexPaintSettings.brushDepth += e.delta.y * 0.1f;
                e.Use();
            }
        }

        void PaintingWindow(int windowID)
        {
            EditorGUIUtility.labelWidth = 80;


            EditorGUILayout.LabelField("Brush Settings", EditorStyles.boldLabel, GUILayout.Width(sceneViewWindowWidth - sceneViewWindowPadding));
            vertexPaintSettings.brushSize = EditorGUILayout.Slider("Radius", vertexPaintSettings.brushSize, 0.1f, 50, GUILayout.Width(sceneViewWindowWidth - sceneViewWindowPadding));
            vertexPaintSettings.brushStrength = EditorGUILayout.Slider("Strength", vertexPaintSettings.brushStrength, 0, 1, GUILayout.Width(sceneViewWindowWidth - sceneViewWindowPadding));
            vertexPaintSettings.brushFalloff = EditorGUILayout.Slider("Falloff", vertexPaintSettings.brushFalloff, 0f, 1, GUILayout.Width(sceneViewWindowWidth - sceneViewWindowPadding));
            vertexPaintSettings.brushDepth = EditorGUILayout.Slider("Depth", vertexPaintSettings.brushDepth, 0.1f, 1, GUILayout.Width(sceneViewWindowWidth - sceneViewWindowPadding));
        }

        void UpdatePreviewBrush()
        {
            Raycast(out m_BrushPos, out m_BrushNorm, out m_BrushFace);
            mousePlane = new Plane(m_BrushNorm, m_BrushPos);
        }

        public bool Raycast(out Vector3 pos, out Vector3 norm, out int face)
        {
            Ray mouseRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            bool hitSomething = false;

            norm = Vector2.zero;
            pos = Vector3.zero;
            face = -1;

            for (int i = 0; i < activeInstances.Length; i++)
            {
                RaycastHit hit;

                if (EditorHandles_UnityInternal.IntersectRayMesh(mouseRay, activeInstances[i].localMesh, activeInstances[i].transform.localToWorldMatrix, out hit))
                {
                    if (hitSomething)
                    {
                        if (Vector3.Distance(hit.point, mouseRay.origin) > Vector3.Distance(pos, mouseRay.origin))
                            continue;
                    }

                    norm = hit.normal.normalized;
                    pos = hit.point;
                    face = hit.triangleIndex;
                    hitSomething = true;
                }
            }

            return hitSomething;
        }



        void DrawBrush()
        {
            Handles.color = innerBrushColour;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.DrawLine(m_BrushPos - m_BrushNorm * vertexPaintSettings.brushDepth, m_BrushPos + m_BrushNorm * vertexPaintSettings.brushDepth);

            Handles.color = outerBrushColour;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
            Handles.DrawLine(m_BrushPos - m_BrushNorm * vertexPaintSettings.brushDepth, m_BrushPos + m_BrushNorm * vertexPaintSettings.brushDepth);


            Handles.color = innerBrushColour;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.DrawWireDisc(m_BrushPos, m_BrushNorm, vertexPaintSettings.brushSize * vertexPaintSettings.brushFalloff);

            Handles.color = outerBrushColour;
            Handles.DrawWireDisc(m_BrushPos, m_BrushNorm, vertexPaintSettings.brushSize);
        }

        void DrawVertices()
        {
            for (int i = 0; i < activeInstances.Length; i++)
            {
                Vector3[] normals = activeInstances[i].localMesh.normals;
                Vector3[] vertices = activeInstances[i].localMesh.vertices;

                for (int x = 0; x < normals.Length; x++)
                {
                    Vector3 normal = activeInstances[i].transform.TransformVector(normals[x]);
                    Vector3 position = activeInstances[i].transform.TransformPoint(vertices[x]);

                    float vertexStrenth = GetVertexStrength(position, normal);

                    Handles.color = Color.Lerp(outerBrushColour, innerBrushColour, vertexStrenth);
                    if (vertexStrenth > 0)
                        Handles.DotHandleCap(0, position, Quaternion.identity, vertexDisplaySize * HandleUtility.GetHandleSize(position), EventType.Repaint);
                }
            }
        }

        float GetVertexStrength(Vector3 position, Vector3 normal)
        {
            bool forwardFacing = Vector3.Dot(normal, Camera.current.transform.forward) <= 0;
            if (!forwardFacing)
                return 0;


            bool alignsWithBrush = Vector3.Dot(normal, m_BrushNorm) > vertexPaintSettings.brushNormalBias;
            if (!alignsWithBrush)
                return 0;

            float distanceFromBrush = Vector3.Distance(position, m_BrushPos);
            bool withinBrush = distanceFromBrush < vertexPaintSettings.brushSize;
            if (!withinBrush)
                return 0;

            
            if (Mathf.Abs(mousePlane.GetDistanceToPoint(position)) > vertexPaintSettings.brushDepth)
                return 0;

            float distanceFromEdgeOfBrush = Mathf.Max(0, distanceFromBrush - vertexPaintSettings.brushSize * vertexPaintSettings.brushFalloff);
            float fallOffArea = (vertexPaintSettings.brushSize * (1 - vertexPaintSettings.brushFalloff));

            return distanceFromEdgeOfBrush.Remap(0, fallOffArea, 1, 0);
        }
    }
}