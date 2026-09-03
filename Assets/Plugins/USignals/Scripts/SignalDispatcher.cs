using System;
using System.Collections.Generic;
using UnityEngine;

namespace USignals
{
    /// <summary>
    /// Runs deferred connections (ConnectFlags.Deferred) at the end of the frame,
    /// the Unity equivalent of Godot's "call deferred / idle frame" behaviour.
    /// Created on demand, hidden, and never saved to a scene.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(short.MaxValue)]
    public sealed class SignalDispatcher : MonoBehaviour
    {
        static SignalDispatcher s_Instance;
        static readonly Queue<Action> s_Queue = new Queue<Action>();
        static readonly List<Action> s_Flushing = new List<Action>();

        public static void Enqueue(Action action)
        {
            if (action == null) return;

            // Outside play mode there is no frame loop worth waiting for.
            if (!Application.isPlaying)
            {
                action();
                return;
            }

            EnsureInstance();
            s_Queue.Enqueue(action);
        }

        static void EnsureInstance()
        {
            if (s_Instance != null) return;
            var go = new GameObject("[SignalDispatcher]");
            go.hideFlags = HideFlags.HideAndDontSave;
            s_Instance = go.AddComponent<SignalDispatcher>();
        }

        void LateUpdate()
        {
            if (s_Queue.Count == 0) return;

            // Copy first: a deferred handler may enqueue more work for the next frame.
            s_Flushing.Clear();
            while (s_Queue.Count > 0) s_Flushing.Add(s_Queue.Dequeue());

            for (int i = 0; i < s_Flushing.Count; i++)
            {
                try { s_Flushing[i](); }
                catch (Exception e) { Debug.LogException(e); }
            }
            s_Flushing.Clear();
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }
    }
}
