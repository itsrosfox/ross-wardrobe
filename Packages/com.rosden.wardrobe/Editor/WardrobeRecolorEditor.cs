using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Inspector for a worn garment: its setup, a shortcut to the Ros' Wardrobe window, and the same look editor
    /// the window shows (changes are baked onto the garment as you make them).
    /// </summary>
    [CustomEditor(typeof(WardrobeRecolor))]
    public class WardrobeRecolorEditor : UnityEditor.Editor {
        private static bool _setupOpen;
        private WardrobeLookPanel _panel;

        private void OnEnable() {
            _panel = new WardrobeLookPanel(Repaint);
            _panel.SetTarget((WardrobeRecolor)target);
        }

        private void OnDisable() => _panel.Dispose();

        public override void OnInspectorGUI() {
            var rc = (WardrobeRecolor)target;
            DrawSetup(rc);
            if (GUILayout.Button("Open in Ros' Wardrobe", GUILayout.Height(24))) WardrobeWindow.Open(rc);
            EditorGUILayout.Space();
            _panel.OnGUI(EditorGUIUtility.currentViewWidth - 24);
        }

        private void DrawSetup(WardrobeRecolor rc) {
            serializedObject.Update();
            if (rc.garment == null || rc.target == null) _setupOpen = true;
            _setupOpen = EditorGUILayout.Foldout(_setupOpen, "Setup", true);
            if (_setupOpen) {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("garment"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("target"), new GUIContent("Renderer"));
                var materials = rc.target != null ? rc.target.sharedMaterials : new Material[0];
                if (materials.Length > 1) {
                    var slot = serializedObject.FindProperty("materialSlot");
                    var names = materials.Select((m, i) => $"{i}: {(m != null ? m.name : "None")}").ToArray();
                    slot.intValue = EditorGUILayout.Popup("Material Slot", slot.intValue, names);
                }
                EditorGUI.indentLevel--;
            }
            if (serializedObject.ApplyModifiedProperties()) _panel.TargetChanged();
        }
    }
}
