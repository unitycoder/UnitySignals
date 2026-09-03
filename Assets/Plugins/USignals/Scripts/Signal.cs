using System;
using System.Collections.Generic;
using UnityEngine;

namespace USignals
{
    /// <summary>Mirrors Godot's Object.ConnectFlags (the subset that makes sense in Unity).</summary>
    [Flags]
    public enum ConnectFlags
    {
        None = 0,
        /// <summary>Invoke the callback at the end of the frame instead of immediately.</summary>
        Deferred = 1 << 0,
        /// <summary>Disconnect automatically after the first emission.</summary>
        OneShot = 1 << 1,
    }

    /// <summary>
    /// Non-generic base for every signal. The editor tooling and the serialized
    /// connections only ever talk to this type.
    /// </summary>
    public abstract class SignalBase
    {
        protected internal class Handle
        {
            public object Key;                    // used for Disconnect() lookups
            public Delegate Typed;                // fast path
            public Action<object[]> Boxed;        // reflection path (used by editor connections with binds)
            public UnityEngine.Object Lifetime;   // auto-disconnect when this is destroyed
            public bool OneShot;
            public bool Deferred;
            public bool Alive = true;
        }

        protected static readonly object[] NoArgs = new object[0];
        static readonly Stack<List<Handle>> s_Pool = new Stack<List<Handle>>();

        readonly List<Handle> m_Handles = new List<Handle>();

        /// <summary>Field name of the signal, filled in by <see cref="SignalUtility"/>.</summary>
        public string Name { get; internal set; }

        /// <summary>Component that declares this signal, filled in by <see cref="SignalUtility"/>.</summary>
        public UnityEngine.Object Owner { get; internal set; }

        public abstract Type[] ArgumentTypes { get; }

        /// <summary>Action, Action&lt;T1&gt;, ... matching this signal's arguments.</summary>
        public abstract Type DelegateType { get; }

        public abstract void EmitBoxed(params object[] args);

        public int ConnectionCount { get { return m_Handles.Count; } }

        // ---------------------------------------------------------------- connect

        public void ConnectDelegate(Delegate callback, object key = null,
                                    UnityEngine.Object lifetime = null,
                                    ConnectFlags flags = ConnectFlags.None)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            if (!DelegateType.IsInstanceOfType(callback))
                throw new ArgumentException(string.Format(
                    "Signal '{0}' expects a {1}, got {2}.", Name, DelegateType, callback.GetType()));

            Add(key ?? callback, callback, null, lifetime ?? (callback.Target as UnityEngine.Object), flags);
        }

        public void ConnectBoxed(Action<object[]> callback, object key = null,
                                 UnityEngine.Object lifetime = null,
                                 ConnectFlags flags = ConnectFlags.None)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            Add(key ?? callback, null, callback, lifetime, flags);
        }

        void Add(object key, Delegate typed, Action<object[]> boxed, UnityEngine.Object lifetime, ConnectFlags flags)
        {
            if (IndexOf(key) >= 0)
            {
                Debug.LogWarning(string.Format("Signal '{0}' is already connected to that target. Ignoring.", Name), Owner);
                return;
            }

            m_Handles.Add(new Handle
            {
                Key = key,
                Typed = typed,
                Boxed = boxed,
                Lifetime = lifetime,
                Deferred = (flags & ConnectFlags.Deferred) != 0,
                OneShot = (flags & ConnectFlags.OneShot) != 0,
            });
        }

        // ------------------------------------------------------------- disconnect

        public bool IsConnected(object key) { return IndexOf(key) >= 0; }

        public void Disconnect(object key)
        {
            int i = IndexOf(key);
            if (i < 0) return;
            m_Handles[i].Alive = false;
            m_Handles.RemoveAt(i);
        }

        public void DisconnectAll()
        {
            for (int i = 0; i < m_Handles.Count; i++) m_Handles[i].Alive = false;
            m_Handles.Clear();
        }

        int IndexOf(object key)
        {
            if (key == null) return -1;
            for (int i = 0; i < m_Handles.Count; i++)
                if (Equals(m_Handles[i].Key, key)) return i;
            return -1;
        }

        // ------------------------------------------------------------------ emit

        protected List<Handle> BeginEmit()
        {
            var buffer = s_Pool.Count > 0 ? s_Pool.Pop() : new List<Handle>();
            buffer.AddRange(m_Handles);
            return buffer;
        }

        protected void EndEmit(List<Handle> buffer)
        {
            buffer.Clear();
            s_Pool.Push(buffer);
        }

        /// <summary>Drops dead handles, consumes one-shots. Returns false if the handle must be skipped.</summary>
        protected bool Prepare(Handle h)
        {
            if (IsDead(h)) { Remove(h); return false; }
            if (h.OneShot) Remove(h);
            return true;
        }

        protected static bool IsDead(Handle h)
        {
            return !h.Alive || TargetDestroyed(h);
        }

        /// <summary>True when the connection was bound to a UnityEngine.Object that has been destroyed.</summary>
        protected static bool TargetDestroyed(Handle h)
        {
            // ReferenceEquals avoids Unity's overloaded == when no lifetime was set.
            return !ReferenceEquals(h.Lifetime, null) && h.Lifetime == null;
        }

        void Remove(Handle h)
        {
            h.Alive = false;
            m_Handles.Remove(h);
        }

        protected void LogCallbackException(Exception e)
        {
            var inner = e is System.Reflection.TargetInvocationException && e.InnerException != null
                ? e.InnerException : e;
            Debug.LogError(string.Format("Exception in a handler of signal '{0}':", Name), Owner);
            Debug.LogException(inner, Owner);
        }
    }

    // ===================================================================== 0 args

    public sealed class Signal : SignalBase
    {
        public override Type[] ArgumentTypes { get { return Type.EmptyTypes; } }
        public override Type DelegateType { get { return typeof(Action); } }

        public void Connect(Action callback, ConnectFlags flags = ConnectFlags.None)
        {
            ConnectDelegate(callback, callback, callback.Target as UnityEngine.Object, flags);
        }

        public void Disconnect(Action callback) { Disconnect((object)callback); }
        public bool IsConnected(Action callback) { return IsConnected((object)callback); }

        public static Signal operator +(Signal s, Action callback)
        {
            if (s == null) s = new Signal();
            s.Connect(callback);
            return s;
        }

        public static Signal operator -(Signal s, Action callback)
        {
            if (s != null) s.Disconnect(callback);
            return s;
        }

        public void Emit()
        {
            var buffer = BeginEmit();
            for (int i = 0; i < buffer.Count; i++)
            {
                var h = buffer[i];
                if (!Prepare(h)) continue;
                if (h.Deferred) { var hh = h; SignalDispatcher.Enqueue(() => Invoke(hh)); }
                else Invoke(h);
            }
            EndEmit(buffer);
        }

        void Invoke(Handle h)
        {
            if (TargetDestroyed(h)) return;
            try
            {
                var typed = h.Typed as Action;
                if (typed != null) typed();
                else if (h.Boxed != null) h.Boxed(NoArgs);
            }
            catch (Exception e) { LogCallbackException(e); }
        }

        public override void EmitBoxed(params object[] args) { Emit(); }
    }

    // ===================================================================== 1 arg

    public sealed class Signal<T1> : SignalBase
    {
        public override Type[] ArgumentTypes { get { return new[] { typeof(T1) }; } }
        public override Type DelegateType { get { return typeof(Action<T1>); } }

        public void Connect(Action<T1> callback, ConnectFlags flags = ConnectFlags.None)
        {
            ConnectDelegate(callback, callback, callback.Target as UnityEngine.Object, flags);
        }

        public void Disconnect(Action<T1> callback) { Disconnect((object)callback); }
        public bool IsConnected(Action<T1> callback) { return IsConnected((object)callback); }

        public static Signal<T1> operator +(Signal<T1> s, Action<T1> callback)
        {
            if (s == null) s = new Signal<T1>();
            s.Connect(callback);
            return s;
        }

        public static Signal<T1> operator -(Signal<T1> s, Action<T1> callback)
        {
            if (s != null) s.Disconnect(callback);
            return s;
        }

        public void Emit(T1 a1)
        {
            var buffer = BeginEmit();
            for (int i = 0; i < buffer.Count; i++)
            {
                var h = buffer[i];
                if (!Prepare(h)) continue;
                if (h.Deferred) { var hh = h; SignalDispatcher.Enqueue(() => Invoke(hh, a1)); }
                else Invoke(h, a1);
            }
            EndEmit(buffer);
        }

        void Invoke(Handle h, T1 a1)
        {
            if (TargetDestroyed(h)) return;
            try
            {
                var typed = h.Typed as Action<T1>;
                if (typed != null) typed(a1);
                else if (h.Boxed != null) h.Boxed(new object[] { a1 });
            }
            catch (Exception e) { LogCallbackException(e); }
        }

        public override void EmitBoxed(params object[] args) { Emit((T1)args[0]); }
    }

    // ==================================================================== 2 args

    public sealed class Signal<T1, T2> : SignalBase
    {
        public override Type[] ArgumentTypes { get { return new[] { typeof(T1), typeof(T2) }; } }
        public override Type DelegateType { get { return typeof(Action<T1, T2>); } }

        public void Connect(Action<T1, T2> callback, ConnectFlags flags = ConnectFlags.None)
        {
            ConnectDelegate(callback, callback, callback.Target as UnityEngine.Object, flags);
        }

        public void Disconnect(Action<T1, T2> callback) { Disconnect((object)callback); }
        public bool IsConnected(Action<T1, T2> callback) { return IsConnected((object)callback); }

        public static Signal<T1, T2> operator +(Signal<T1, T2> s, Action<T1, T2> callback)
        {
            if (s == null) s = new Signal<T1, T2>();
            s.Connect(callback);
            return s;
        }

        public static Signal<T1, T2> operator -(Signal<T1, T2> s, Action<T1, T2> callback)
        {
            if (s != null) s.Disconnect(callback);
            return s;
        }

        public void Emit(T1 a1, T2 a2)
        {
            var buffer = BeginEmit();
            for (int i = 0; i < buffer.Count; i++)
            {
                var h = buffer[i];
                if (!Prepare(h)) continue;
                if (h.Deferred) { var hh = h; SignalDispatcher.Enqueue(() => Invoke(hh, a1, a2)); }
                else Invoke(h, a1, a2);
            }
            EndEmit(buffer);
        }

        void Invoke(Handle h, T1 a1, T2 a2)
        {
            if (TargetDestroyed(h)) return;
            try
            {
                var typed = h.Typed as Action<T1, T2>;
                if (typed != null) typed(a1, a2);
                else if (h.Boxed != null) h.Boxed(new object[] { a1, a2 });
            }
            catch (Exception e) { LogCallbackException(e); }
        }

        public override void EmitBoxed(params object[] args) { Emit((T1)args[0], (T2)args[1]); }
    }

    // ==================================================================== 3 args

    public sealed class Signal<T1, T2, T3> : SignalBase
    {
        public override Type[] ArgumentTypes { get { return new[] { typeof(T1), typeof(T2), typeof(T3) }; } }
        public override Type DelegateType { get { return typeof(Action<T1, T2, T3>); } }

        public void Connect(Action<T1, T2, T3> callback, ConnectFlags flags = ConnectFlags.None)
        {
            ConnectDelegate(callback, callback, callback.Target as UnityEngine.Object, flags);
        }

        public void Disconnect(Action<T1, T2, T3> callback) { Disconnect((object)callback); }
        public bool IsConnected(Action<T1, T2, T3> callback) { return IsConnected((object)callback); }

        public static Signal<T1, T2, T3> operator +(Signal<T1, T2, T3> s, Action<T1, T2, T3> callback)
        {
            if (s == null) s = new Signal<T1, T2, T3>();
            s.Connect(callback);
            return s;
        }

        public static Signal<T1, T2, T3> operator -(Signal<T1, T2, T3> s, Action<T1, T2, T3> callback)
        {
            if (s != null) s.Disconnect(callback);
            return s;
        }

        public void Emit(T1 a1, T2 a2, T3 a3)
        {
            var buffer = BeginEmit();
            for (int i = 0; i < buffer.Count; i++)
            {
                var h = buffer[i];
                if (!Prepare(h)) continue;
                if (h.Deferred) { var hh = h; SignalDispatcher.Enqueue(() => Invoke(hh, a1, a2, a3)); }
                else Invoke(h, a1, a2, a3);
            }
            EndEmit(buffer);
        }

        void Invoke(Handle h, T1 a1, T2 a2, T3 a3)
        {
            if (TargetDestroyed(h)) return;
            try
            {
                var typed = h.Typed as Action<T1, T2, T3>;
                if (typed != null) typed(a1, a2, a3);
                else if (h.Boxed != null) h.Boxed(new object[] { a1, a2, a3 });
            }
            catch (Exception e) { LogCallbackException(e); }
        }

        public override void EmitBoxed(params object[] args)
        {
            Emit((T1)args[0], (T2)args[1], (T3)args[2]);
        }
    }
}
