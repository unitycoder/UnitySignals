using UnityEditor;
using UnityEngine;

namespace USignals.EditorTools
{
    [CustomEditor(typeof(SignalConnections))]
    public class SignalConnectionsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var storage = (SignalConnections)target;

            EditorGUILayout.HelpBox("Signal connections stored on this GameObject. Edit them in the Signals window.",
                                    MessageType.None);

            if (GUILayout.Button("Open Signals Window")) SignalsWindow.Open();

            EditorGUILayout.Space();

            if (storage.Connections.Count == 0)
            {
                EditorGUILayout.LabelField("No connections.", EditorStyles.miniLabel);
                return;
            }

            foreach (var connection in storage.Connections)
            {
                var error = connection.Validate();
                var label = (connection.source != null ? connection.source.GetType().Name : "?") +
                            "." + connection.signal + "  →  " + connection.Describe();

                if (error != null) EditorGUILayout.HelpBox(label + "\n" + error, MessageType.Warning);
                else EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            }

            if (GUILayout.Button("Remove Broken Connections"))
            {
                Undo.RecordObject(storage, "Remove Broken Signal Connections");
                storage.Connections.RemoveAll(c => c.Validate() != null);
                SignalsWindow.MarkDirty(storage);
            }
        }
    }
}
