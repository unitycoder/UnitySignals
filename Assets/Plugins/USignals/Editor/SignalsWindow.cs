using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace USignals.EditorTools
{
    /// <summary>
    /// Lists the signals declared by the components of the selected GameObject and the
    /// connections made from them — the Unity counterpart of Godot's Node ▸ Signals dock.
    /// </summary>
    public class SignalsWindow : EditorWindow
    {
        const float kRowHeight = 20f;

        [SerializeField] GameObject m_Locked;
        [SerializeField] Vector2 m_Scroll;

        Component m_SelComponent;
        string m_SelSignal;
        SignalConnection m_SelConnection;

        GUIStyle m_RowStyle, m_HeaderStyle;

        [MenuItem("Tools/Signals")]
        public static SignalsWindow Open()
        {
            var w = GetWindow<SignalsWindow>("Signals");
            w.minSize = new Vector2(280, 160);
            return w;
        }

        [MenuItem("CONTEXT/Component/Signals...")]
        static void OpenFromContext(MenuCommand command)
        {
            var component = command.context as Component;
            var w = Open();
            if (component != null) Selection.activeGameObject = component.gameObject;
            w.Repaint();
        }

        void OnEnable() { Undo.undoRedoPerformed += Repaint; }
        void OnDisable() { Undo.undoRedoPerformed -= Repaint; }
        void OnSelectionChange() { Repaint(); }

        GameObject Current { get { return m_Locked != null ? m_Locked : Selection.activeGameObject; } }

        void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();

            var go = Current;
            if (go == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject to see its signals.", MessageType.Info);
                return;
            }

            var storage = SignalConnections.Find(go);
            var components = go.GetComponents<Component>();
            bool any = false;

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is SignalConnections) continue;

                var signals = SignalUtility.GetSignals(component.GetType());
                if (signals.Length == 0) continue;
                any = true;

                DrawComponentHeader(component);
                for (int s = 0; s < signals.Length; s++)
                    DrawSignal(component, signals[s], storage);
            }
            EditorGUILayout.EndScrollView();

            if (!any) DrawEmptyState(go);

            DrawFooter(go);
        }

        // ------------------------------------------------------------------- ui

        void EnsureStyles()
        {
            if (m_RowStyle != null) return;
            m_RowStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, richText = true };
            m_HeaderStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var go = Current;
            GUILayout.Label(go != null ? go.name : "(nothing selected)", EditorStyles.toolbarButton,
                            GUILayout.MinWidth(60));

            GUILayout.FlexibleSpace();

            if (Application.isPlaying) GUILayout.Label("play mode", EditorStyles.miniLabel);

            bool locked = m_Locked != null;
            bool newLocked = GUILayout.Toggle(locked, locked ? "Locked" : "Lock",
                                              EditorStyles.toolbarButton, GUILayout.Width(52));
            if (newLocked != locked) m_Locked = newLocked ? Selection.activeGameObject : null;

            EditorGUILayout.EndHorizontal();
        }

        void DrawComponentHeader(Component component)
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, m_HeaderStyle, GUILayout.Height(kRowHeight));
            var icon = AssetPreview.GetMiniThumbnail(component);
            var label = new GUIContent(" " + component.GetType().Name, icon);
            GUI.Label(rect, label, m_HeaderStyle);
        }

        void DrawSignal(Component component, SignalInfo info, SignalConnections storage)
        {
            bool selected = m_SelConnection == null && m_SelComponent == component && m_SelSignal == info.Name;

            var rect = DrawRow(16f, selected);
            var content = new GUIContent(info.Signature, SignalIcon(), info.Description);
            GUI.Label(rect, content, m_RowStyle);

            // runtime connection count (includes connections made from code)
            if (Application.isPlaying)
            {
                var live = SignalUtility.GetInstance(component, info);
                if (live != null && live.ConnectionCount > 0)
                {
                    var r = rect; r.xMin = r.xMax - 60f;
                    GUI.Label(r, live.ConnectionCount + " live", EditorStyles.miniLabel);
                }
            }

            HandleRowEvents(rect,
                onClick: () => Select(component, info.Name, null),
                onDoubleClick: () => OpenConnectDialog(component, info, null),
                onContext: () => SignalContextMenu(component, info));

            if (storage == null) return;
            foreach (var connection in storage.ConnectionsFor(component, info.Name))
                DrawConnection(component, info, connection, storage);
        }

        void DrawConnection(Component component, SignalInfo info, SignalConnection connection, SignalConnections storage)
        {
            bool selected = m_SelConnection == connection;
            var rect = DrawRow(34f, selected);

            string error = connection.Validate();
            var text = "→ " + connection.Describe();
            var suffix = new List<string>();
            if (connection.deferred) suffix.Add("deferred");
            if (connection.oneShot) suffix.Add("one shot");
            if (!connection.enabled) suffix.Add("disabled");
            if (suffix.Count > 0) text += "  <i>[" + string.Join(", ", suffix.ToArray()) + "]</i>";

            var style = new GUIStyle(m_RowStyle);
            if (error != null || !connection.enabled)
                style.normal.textColor = error != null ? new Color(0.9f, 0.35f, 0.3f) : Color.gray;

            var icon = error != null
                ? SafeIcon("console.warnicon.sml")
                : AssetPreview.GetMiniThumbnail(connection.target);

            GUI.Label(rect, new GUIContent(" " + text, icon, error ?? "Double click to edit"), style);

            HandleRowEvents(rect,
                onClick: () => Select(component, info.Name, connection),
                onDoubleClick: () => OpenConnectDialog(component, info, connection),
                onContext: () => ConnectionContextMenu(storage, connection, component, info));
        }

        Rect DrawRow(float indent, bool selected)
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, m_RowStyle,
                                                GUILayout.Height(kRowHeight), GUILayout.ExpandWidth(true));
            if (selected)
                EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                    ? new Color(0.24f, 0.37f, 0.59f) : new Color(0.24f, 0.49f, 0.90f, 0.4f));
            rect.xMin += indent;
            return rect;
        }

        void HandleRowEvents(Rect rect, System.Action onClick, System.Action onDoubleClick, System.Action onContext)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                onClick();
                if (e.clickCount == 2) onDoubleClick();
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.ContextClick)
            {
                onClick();
                onContext();
                e.Use();
            }
        }

        void DrawEmptyState(GameObject go)
        {
            EditorGUILayout.HelpBox(
                "No signals declared on '" + go.name + "'.\n\n" +
                "Declare one in any MonoBehaviour on this object:\n\n" +
                "    using USignals;\n\n" +
                "    public Signal Died = new Signal();\n" +
                "    [Signal(\"amount\")]\n" +
                "    public Signal<int> HealthChanged = new Signal<int>();\n\n" +
                "Then emit it: HealthChanged.Emit(50);",
                MessageType.Info);
        }

        void DrawFooter(GameObject go)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            using (new EditorGUI.DisabledScope(m_SelComponent == null || string.IsNullOrEmpty(m_SelSignal)))
            {
                if (GUILayout.Button(m_SelConnection == null ? "Connect..." : "Edit...", EditorStyles.toolbarButton))
                {
                    var info = SignalUtility.Find(m_SelComponent.GetType(), m_SelSignal);
                    OpenConnectDialog(m_SelComponent, info, m_SelConnection);
                }
            }

            using (new EditorGUI.DisabledScope(m_SelConnection == null))
            {
                if (GUILayout.Button("Disconnect", EditorStyles.toolbarButton))
                    Disconnect(SignalConnections.Find(go), m_SelConnection);
            }

            GUILayout.FlexibleSpace();

            var storage = SignalConnections.Find(go);
            using (new EditorGUI.DisabledScope(storage == null || storage.Connections.Count == 0))
            {
                if (GUILayout.Button("Disconnect All", EditorStyles.toolbarButton) &&
                    EditorUtility.DisplayDialog("Disconnect all",
                        "Remove every signal connection stored on '" + go.name + "'?", "Disconnect", "Cancel"))
                {
                    Undo.RecordObject(storage, "Disconnect All Signals");
                    foreach (var c in storage.Connections) c.Unbind();
                    storage.Connections.Clear();
                    MarkDirty(storage);
                    m_SelConnection = null;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // -------------------------------------------------------------- actions

        void Select(Component component, string signal, SignalConnection connection)
        {
            m_SelComponent = component;
            m_SelSignal = signal;
            m_SelConnection = connection;
        }

        void OpenConnectDialog(Component component, SignalInfo info, SignalConnection edit)
        {
            if (info == null) return;
            ConnectSignalDialog.Open(component, info, edit, () =>
            {
                m_SelConnection = null;
                Repaint();
            });
        }

        void SignalContextMenu(Component component, SignalInfo info)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Connect..."), false, () => OpenConnectDialog(component, info, null));
            menu.AddItem(new GUIContent("Copy emit code"), false, () =>
            {
                var args = new string[info.ArgumentNames.Length];
                for (int i = 0; i < args.Length; i++) args[i] = info.ArgumentNames[i];
                EditorGUIUtility.systemCopyBuffer = info.Name + ".Emit(" + string.Join(", ", args) + ");";
            });
            menu.ShowAsContext();
        }

        void ConnectionContextMenu(SignalConnections storage, SignalConnection connection,
                                   Component component, SignalInfo info)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Edit..."), false, () => OpenConnectDialog(component, info, connection));
            menu.AddItem(new GUIContent("Go to Method"), false, () => SignalScriptWriter.OpenMethod(connection));
            menu.AddItem(new GUIContent("Select Target"), false, () =>
            {
                if (connection.target != null) Selection.activeObject = connection.target.gameObject;
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enabled"), connection.enabled, () =>
            {
                Undo.RecordObject(storage, "Toggle Signal Connection");
                connection.enabled = !connection.enabled;
                MarkDirty(storage);
            });
            menu.AddItem(new GUIContent("Disconnect"), false, () => Disconnect(storage, connection));
            menu.ShowAsContext();
        }

        void Disconnect(SignalConnections storage, SignalConnection connection)
        {
            if (storage == null || connection == null) return;
            Undo.RecordObject(storage, "Disconnect Signal");
            connection.Unbind();
            storage.Connections.Remove(connection);
            MarkDirty(storage);
            m_SelConnection = null;
            Repaint();
        }

        internal static void MarkDirty(SignalConnections storage)
        {
            if (storage == null) return;
            EditorUtility.SetDirty(storage);
            PrefabUtility.RecordPrefabInstancePropertyModifications(storage);
            if (Application.isPlaying) storage.Rebind();
            else if (!EditorUtility.IsPersistent(storage))
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(storage.gameObject.scene);
        }

        static Texture s_SignalIcon;
        static bool s_SignalIconResolved;

        static Texture SignalIcon()
        {
            if (s_SignalIconResolved) return s_SignalIcon;
            s_SignalIconResolved = true;
            s_SignalIcon = SafeIcon("Animation.EventMarker") ?? SafeIcon("d_animationevent");
            return s_SignalIcon;
        }

        internal static Texture SafeIcon(string name)
        {
            try
            {
                var content = EditorGUIUtility.IconContent(name);
                return content != null ? content.image : null;
            }
            catch { return null; }
        }
    }
}
