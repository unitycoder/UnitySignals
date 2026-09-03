using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace USignals
{
    /// <summary>One extra argument appended after the signal's own arguments (Godot "binds").</summary>
    [Serializable]
    public class SignalArgument
    {
        public enum ArgType { Int, Float, Bool, String, Object, Vector2, Vector3, Color }

        public ArgType type = ArgType.Int;
        public int intValue;
        public float floatValue;
        public bool boolValue;
        public string stringValue = "";
        public UnityEngine.Object objectValue;
        public Vector3 vectorValue;
        public Color colorValue = Color.white;

        public object Value
        {
            get
            {
                switch (type)
                {
                    case ArgType.Int: return intValue;
                    case ArgType.Float: return floatValue;
                    case ArgType.Bool: return boolValue;
                    case ArgType.String: return stringValue;
                    case ArgType.Object: return objectValue;
                    case ArgType.Vector2: return (Vector2)vectorValue;
                    case ArgType.Vector3: return vectorValue;
                    case ArgType.Color: return colorValue;
                }
                return null;
            }
        }

        public Type RuntimeType
        {
            get
            {
                switch (type)
                {
                    case ArgType.Int: return typeof(int);
                    case ArgType.Float: return typeof(float);
                    case ArgType.Bool: return typeof(bool);
                    case ArgType.String: return typeof(string);
                    case ArgType.Object: return objectValue != null ? objectValue.GetType() : typeof(UnityEngine.Object);
                    case ArgType.Vector2: return typeof(Vector2);
                    case ArgType.Vector3: return typeof(Vector3);
                    case ArgType.Color: return typeof(Color);
                }
                return typeof(object);
            }
        }

        public string Describe()
        {
            var v = Value;
            if (type == SignalArgument.ArgType.String) return "\"" + stringValue + "\"";
            return v == null ? "null" : v.ToString();
        }
    }

    /// <summary>
    /// A connection authored in the editor. Serialized on the emitter's GameObject
    /// (via <see cref="SignalConnections"/>), the same way Godot stores connections
    /// in the scene file of the emitting node.
    /// </summary>
    [Serializable]
    public class SignalConnection
    {
        public Component source;        // component declaring the signal
        public string signal;           // field name of the signal
        public Component target;        // receiving component
        public string method;           // method name on the target
        public List<SignalArgument> binds = new List<SignalArgument>();
        public bool deferred;
        public bool oneShot;
        public bool enabled = true;

        [NonSerialized] SignalBase m_Bound;
        [NonSerialized] string m_Error;

        public bool IsBound { get { return m_Bound != null; } }
        public string Error { get { return m_Error; } }

        public string Describe()
        {
            var t = target != null ? target.gameObject.name + "." + target.GetType().Name : "<missing>";
            var b = "";
            if (binds != null && binds.Count > 0)
            {
                var parts = new string[binds.Count];
                for (int i = 0; i < binds.Count; i++) parts[i] = binds[i].Describe();
                b = "(" + string.Join(", ", parts) + ")";
            }
            return t + "." + method + b;
        }

        public Type[] BindTypes()
        {
            if (binds == null || binds.Count == 0) return Type.EmptyTypes;
            var types = new Type[binds.Count];
            for (int i = 0; i < binds.Count; i++) types[i] = binds[i].RuntimeType;
            return types;
        }

        /// <summary>Validates without connecting. Returns null when the connection is fine.</summary>
        public string Validate()
        {
            if (source == null) return "Source component is missing";
            if (string.IsNullOrEmpty(signal)) return "No signal selected";

            var info = SignalUtility.Find(source.GetType(), signal);
            if (info == null) return string.Format("'{0}' has no signal named '{1}'", source.GetType().Name, signal);

            if (target == null) return "Target component is missing";
            if (string.IsNullOrEmpty(method)) return "No method selected";

            var mi = SignalUtility.FindMethod(target.GetType(), method, info.ArgumentTypes, BindTypes());
            if (mi == null)
                return string.Format("'{0}' has no method '{1}' matching {2}",
                    target.GetType().Name, method, info.Signature);

            return null;
        }

        public void Bind()
        {
            Unbind();
            m_Error = null;
            if (!enabled) return;

            m_Error = Validate();
            if (m_Error != null)
            {
                Debug.LogWarning(string.Format("Signal connection is broken: {0}", m_Error),
                                 source != null ? source : target);
                return;
            }

            var info = SignalUtility.Find(source.GetType(), signal);
            var signalInstance = SignalUtility.GetInstance(source, info);
            if (signalInstance == null) return;

            var bindTypes = BindTypes();
            var mi = SignalUtility.FindMethod(target.GetType(), method, info.ArgumentTypes, bindTypes);

            var flags = ConnectFlags.None;
            if (deferred) flags |= ConnectFlags.Deferred;
            if (oneShot) flags |= ConnectFlags.OneShot;

            // Fast path: no binds and an exactly matching signature -> real delegate, no reflection at emit time.
            if (bindTypes.Length == 0)
            {
                var del = Delegate.CreateDelegate(signalInstance.DelegateType, target, mi, false);
                if (del != null)
                {
                    signalInstance.ConnectDelegate(del, this, target, flags);
                    m_Bound = signalInstance;
                    return;
                }
            }

            var boundValues = new object[bindTypes.Length];
            for (int i = 0; i < boundValues.Length; i++) boundValues[i] = binds[i].Value;

            var receiver = target;
            var argCount = info.ArgumentTypes.Length;
            Action<object[]> callback = args =>
            {
                var full = new object[argCount + boundValues.Length];
                for (int i = 0; i < argCount; i++) full[i] = args[i];
                for (int i = 0; i < boundValues.Length; i++) full[argCount + i] = boundValues[i];
                mi.Invoke(receiver, full);
            };

            signalInstance.ConnectBoxed(callback, this, target, flags);
            m_Bound = signalInstance;
        }

        public void Unbind()
        {
            if (m_Bound == null) return;
            m_Bound.Disconnect(this);
            m_Bound = null;
        }

        public SignalConnection Clone()
        {
            var copy = new SignalConnection
            {
                source = source,
                signal = signal,
                target = target,
                method = method,
                deferred = deferred,
                oneShot = oneShot,
                enabled = enabled,
                binds = new List<SignalArgument>(),
            };
            if (binds != null)
                foreach (var b in binds)
                    copy.binds.Add(new SignalArgument
                    {
                        type = b.type, intValue = b.intValue, floatValue = b.floatValue,
                        boolValue = b.boolValue, stringValue = b.stringValue,
                        objectValue = b.objectValue, vectorValue = b.vectorValue, colorValue = b.colorValue,
                    });
            return copy;
        }
    }
}
