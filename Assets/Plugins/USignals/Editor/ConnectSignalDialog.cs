using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace USignals.EditorTools
{
    /// <summary>Godot's "Connect a Signal to a Method" dialog, Unity flavoured.</summary>
    public class ConnectSignalDialog : EditorWindow
    {
        Component m_Source;
        SignalInfo m_Info;
        SignalConnection m_Editing;
        Action m_OnApplied;

        GameObject m_TargetObject;
        Component[] m_Components = new Component[0];
        string[] m_ComponentNames = new string[0];
        int m_ComponentIndex;

        List<MethodInfo> m_Methods = new List<MethodInfo>();
        string[] m_MethodLabels = new string[0];
        int m_MethodIndex;

        bool m_CreateMethod;
        string m_NewMethodName = "";
        bool m_OpenScript = true;

        List<SignalArgument> m_Binds = new List<SignalArgument>();
        bool m_Deferred, m_OneShot;
        bool m_BindsFoldout;
        Vector2 m_Scroll;

        public static void Open(Component source, SignalInfo info, SignalConnection editing, Action onApplied)
        {
            var w = CreateInstance<ConnectSignalDialog>();
            w.titleContent = new GUIContent(editing == null ? "Connect a Signal" : "Edit Connection");
            w.m_Source = source;
            w.m_Info = info;
            w.m_Editing = editing;
            w.m_OnApplied = onApplied;

            if (editing != null)
            {
                w.m_TargetObject = editing.target != null ? editing.target.gameObject : source.gameObject;
                w.m_Deferred = editing.deferred;
                w.m_OneShot = editing.oneShot;
                foreach (var b in editing.binds) w.m_Binds.Add(b);
                w.m_BindsFoldout = w.m_Binds.Count > 0;
            }
            else
            {
                w.m_TargetObject = source.gameObject;
            }

            w.RefreshComponents(editing != null ? editing.target : null);
            w.RefreshMethods(editing != null ? editing.method : null);
            w.m_NewMethodName = SignalUtility.SuggestMethodName(source.GetType(), info.Name);
            w.minSize = new Vector2(420, 330);
            w.ShowUtility();
        }

        // ------------------------------------------------------------------- ui

        void OnGUI()
        {
            if (m_Source == null || m_Info == null) { Close(); return; }

            EditorGUILayout.LabelField("Signal", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(m_Source.GetType().Name + "." + m_Info.Signature, EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(m_Info.Description))
                EditorGUILayout.LabelField(m_Info.Description, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            DrawReceiver();
            EditorGUILayout.Space();
            DrawMethod();
            EditorGUILayout.Space();
            DrawBinds();
            EditorGUILayout.Space();
            DrawOptions();

            EditorGUILayout.EndScrollView();

            DrawButtons();
        }

        void DrawReceiver()
        {
            EditorGUILayout.LabelField("Receiver", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            m_TargetObject = (GameObject)EditorGUILayout.ObjectField("GameObject", m_TargetObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshComponents(null);
                RefreshMethods(null);
            }

            if (m_Components.Length == 0)
            {
                EditorGUILayout.HelpBox("Pick a GameObject that has at least one component.", MessageType.Info);
                return;
            }

            EditorGUI.BeginChangeCheck();
            m_ComponentIndex = EditorGUILayout.Popup("Component", m_ComponentIndex, m_ComponentNames);
            if (EditorGUI.EndChangeCheck()) RefreshMethods(null);
        }

        void DrawMethod()
        {
            EditorGUILayout.LabelField("Method", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            m_CreateMethod = EditorGUILayout.Toggle("Create new method", m_CreateMethod);
            if (EditorGUI.EndChangeCheck()) GUI.FocusControl(null);

            if (m_CreateMethod)
            {
                m_NewMethodName = EditorGUILayout.TextField("Name", m_NewMethodName);
                m_OpenScript = EditorGUILayout.Toggle("Open script after", m_OpenScript);

                var target = SelectedComponent();
                var script = target != null ? SignalScriptWriter.GetScript(target) : null;
                if (script == null)
                {
                    EditorGUILayout.HelpBox("The selected component has no editable script; pick an existing method instead.",
                                            MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField(" ", "will be added to " + script.name + ".cs", EditorStyles.miniLabel);
                }
            }
            else if (m_Methods.Count == 0)
            {
                EditorGUILayout.HelpBox("No method on this component matches " + m_Info.Signature +
                                        (m_Binds.Count > 0 ? " plus " + m_Binds.Count + " bound argument(s)." : "."),
                                        MessageType.Warning);
            }
            else
            {
                m_MethodIndex = EditorGUILayout.Popup("Method", Mathf.Clamp(m_MethodIndex, 0, m_Methods.Count - 1), m_MethodLabels);
            }
        }

        void DrawBinds()
        {
            m_BindsFoldout = EditorGUILayout.Foldout(m_BindsFoldout,
                "Extra Arguments (binds)" + (m_Binds.Count > 0 ? " — " + m_Binds.Count : ""), true);
            if (!m_BindsFoldout) return;

            EditorGUILayout.LabelField("Appended after the signal's own arguments.", EditorStyles.miniLabel);

            for (int i = 0; i < m_Binds.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var bind = m_Binds[i];

                EditorGUI.BeginChangeCheck();
                bind.type = (SignalArgument.ArgType)EditorGUILayout.EnumPopup(bind.type, GUILayout.Width(72));
                if (EditorGUI.EndChangeCheck()) RefreshMethods(CurrentMethodName());

                switch (bind.type)
                {
                    case SignalArgument.ArgType.Int: bind.intValue = EditorGUILayout.IntField(bind.intValue); break;
                    case SignalArgument.ArgType.Float: bind.floatValue = EditorGUILayout.FloatField(bind.floatValue); break;
                    case SignalArgument.ArgType.Bool: bind.boolValue = EditorGUILayout.Toggle(bind.boolValue); break;
                    case SignalArgument.ArgType.String: bind.stringValue = EditorGUILayout.TextField(bind.stringValue); break;
                    case SignalArgument.ArgType.Object:
                        EditorGUI.BeginChangeCheck();
                        bind.objectValue = EditorGUILayout.ObjectField(bind.objectValue, typeof(UnityEngine.Object), true);
                        if (EditorGUI.EndChangeCheck()) RefreshMethods(CurrentMethodName());
                        break;
                    case SignalArgument.ArgType.Vector2:
                        bind.vectorValue = EditorGUILayout.Vector2Field(GUIContent.none, bind.vectorValue);
                        break;
                    case SignalArgument.ArgType.Vector3:
                        bind.vectorValue = EditorGUILayout.Vector3Field(GUIContent.none, bind.vectorValue);
                        break;
                    case SignalArgument.ArgType.Color:
                        bind.colorValue = EditorGUILayout.ColorField(bind.colorValue);
                        break;
                }

                if (GUILayout.Button("-", GUILayout.Width(22)))
                {
                    m_Binds.RemoveAt(i);
                    RefreshMethods(CurrentMethodName());
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Argument", GUILayout.Width(110)))
            {
                m_Binds.Add(new SignalArgument());
                RefreshMethods(CurrentMethodName());
            }
        }

        void DrawOptions()
        {
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            m_Deferred = EditorGUILayout.Toggle(new GUIContent("Deferred",
                "Call at the end of the frame instead of immediately."), m_Deferred);
            m_OneShot = EditorGUILayout.Toggle(new GUIContent("One Shot",
                "Disconnect after the first emission."), m_OneShot);
        }

        void DrawButtons()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(80))) Close();

            using (new EditorGUI.DisabledScope(!CanApply()))
            {
                if (GUILayout.Button(m_Editing == null ? "Connect" : "Apply", GUILayout.Width(90))) Apply();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        // -------------------------------------------------------------- helpers

        Component SelectedComponent()
        {
            if (m_Components.Length == 0) return null;
            return m_Components[Mathf.Clamp(m_ComponentIndex, 0, m_Components.Length - 1)];
        }

        string CurrentMethodName()
        {
            if (m_CreateMethod) return m_NewMethodName;
            if (m_Methods.Count == 0) return null;
            return m_Methods[Mathf.Clamp(m_MethodIndex, 0, m_Methods.Count - 1)].Name;
        }

        void RefreshComponents(Component preferred)
        {
            var list = new List<Component>();
            if (m_TargetObject != null)
                foreach (var c in m_TargetObject.GetComponents<Component>())
                    if (c != null && !(c is SignalConnections)) list.Add(c);

            m_Components = list.ToArray();
            m_ComponentNames = new string[m_Components.Length];
            for (int i = 0; i < m_Components.Length; i++) m_ComponentNames[i] = m_Components[i].GetType().Name;

            m_ComponentIndex = 0;
            if (preferred != null)
            {
                int idx = Array.IndexOf(m_Components, preferred);
                if (idx >= 0) m_ComponentIndex = idx;
            }
            else
            {
                // prefer a user script over Transform/Renderer/etc.
                for (int i = 0; i < m_Components.Length; i++)
                    if (m_Components[i] is MonoBehaviour) { m_ComponentIndex = i; break; }
            }
        }

        void RefreshMethods(string preferredName)
        {
            m_Methods.Clear();
            var target = SelectedComponent();
            if (target != null)
            {
                var bindTypes = new Type[m_Binds.Count];
                for (int i = 0; i < m_Binds.Count; i++) bindTypes[i] = m_Binds[i].RuntimeType;
                m_Methods = SignalUtility.FindCompatibleMethods(target.GetType(), m_Info.ArgumentTypes, bindTypes);
            }

            m_MethodLabels = new string[m_Methods.Count];
            m_MethodIndex = 0;
            for (int i = 0; i < m_Methods.Count; i++)
            {
                m_MethodLabels[i] = SignalUtility.DescribeMethod(m_Methods[i]);
                if (preferredName != null && m_Methods[i].Name == preferredName) m_MethodIndex = i;
            }

            if (preferredName != null && m_Methods.Count == 0)
            {
                m_CreateMethod = true;
                m_NewMethodName = preferredName;
            }
        }

        bool CanApply()
        {
            if (SelectedComponent() == null) return false;
            if (m_CreateMethod)
                return IsValidIdentifier(m_NewMethodName) && SignalScriptWriter.GetScript(SelectedComponent()) != null;
            return m_Methods.Count > 0;
        }

        static bool IsValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!char.IsLetter(name[0]) && name[0] != '_') return false;
            for (int i = 1; i < name.Length; i++)
                if (!char.IsLetterOrDigit(name[i]) && name[i] != '_') return false;
            return true;
        }

        void Apply()
        {
            var target = SelectedComponent();
            string methodName;

            if (m_CreateMethod)
            {
                methodName = m_NewMethodName;
                var paramTypes = new List<Type>(m_Info.ArgumentTypes);
                var paramNames = new List<string>(m_Info.ArgumentNames);
                for (int i = 0; i < m_Binds.Count; i++)
                {
                    paramTypes.Add(m_Binds[i].RuntimeType);
                    paramNames.Add("bound" + i);
                }

                string error;
                if (!SignalScriptWriter.AddMethod(target, methodName, paramTypes.ToArray(), paramNames.ToArray(),
                        m_Source.GetType().Name + "." + m_Info.Name, m_OpenScript, out error))
                {
                    EditorUtility.DisplayDialog("Could not create method", error, "OK");
                    return;
                }
            }
            else
            {
                methodName = m_Methods[Mathf.Clamp(m_MethodIndex, 0, m_Methods.Count - 1)].Name;
            }

            var storage = SignalConnections.GetOrAdd(m_Source.gameObject);
            Undo.RecordObject(storage, m_Editing == null ? "Connect Signal" : "Edit Signal Connection");

            var connection = m_Editing;
            if (connection == null)
            {
                connection = new SignalConnection();
                storage.Connections.Add(connection);
            }
            else
            {
                connection.Unbind();
            }

            connection.source = m_Source;
            connection.signal = m_Info.Name;
            connection.target = target;
            connection.method = methodName;
            connection.binds = new List<SignalArgument>(m_Binds);
            connection.deferred = m_Deferred;
            connection.oneShot = m_OneShot;
            connection.enabled = true;

            SignalsWindow.MarkDirty(storage);

            if (m_OnApplied != null) m_OnApplied();
            Close();
        }
    }
}
