using System.Collections.Generic;
using UnityEngine;

namespace USignals
{
    /// <summary>
    /// Holds the signal connections authored on this GameObject and wires them up on Awake.
    /// Added automatically by the Signals window — you never add it by hand.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public class SignalConnections : MonoBehaviour
    {
        [SerializeField] List<SignalConnection> m_Connections = new List<SignalConnection>();

        public List<SignalConnection> Connections { get { return m_Connections; } }

        void Awake()
        {
            Rebind();
        }

        void OnDestroy()
        {
            for (int i = 0; i < m_Connections.Count; i++) m_Connections[i].Unbind();
        }

        /// <summary>Re-applies every connection. Safe to call at runtime after editing.</summary>
        public void Rebind()
        {
            for (int i = 0; i < m_Connections.Count; i++) m_Connections[i].Bind();
        }

        public IEnumerable<SignalConnection> ConnectionsFor(Component source, string signal)
        {
            for (int i = 0; i < m_Connections.Count; i++)
            {
                var c = m_Connections[i];
                if (c.source == source && c.signal == signal) yield return c;
            }
        }

        public int CountFor(Component source, string signal)
        {
            int n = 0;
            for (int i = 0; i < m_Connections.Count; i++)
                if (m_Connections[i].source == source && m_Connections[i].signal == signal) n++;
            return n;
        }

        public static SignalConnections Find(GameObject go)
        {
            return go == null ? null : go.GetComponent<SignalConnections>();
        }

#if UNITY_EDITOR
        /// <summary>Editor-only: gets or adds the storage component, with undo support.</summary>
        public static SignalConnections GetOrAdd(GameObject go)
        {
            var existing = go.GetComponent<SignalConnections>();
            if (existing != null) return existing;
            var added = UnityEditor.Undo.AddComponent<SignalConnections>(go);
            return added;
        }
#endif
    }
}
