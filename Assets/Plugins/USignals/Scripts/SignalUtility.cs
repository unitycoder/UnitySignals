using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace USignals
{
    /// <summary>Description of one signal field declared by a component type.</summary>
    public sealed class SignalInfo
    {
        public FieldInfo Field;
        public string Name;
        public Type[] ArgumentTypes;
        public string[] ArgumentNames;
        public string Description;
        public Type DeclaringType;

        /// <summary>e.g. "HealthChanged(int amount, int max)"</summary>
        public string Signature
        {
            get
            {
                var sb = new StringBuilder(Name);
                sb.Append('(');
                for (int i = 0; i < ArgumentTypes.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(SignalUtility.PrettyType(ArgumentTypes[i]));
                    sb.Append(' ');
                    sb.Append(ArgumentNames[i]);
                }
                sb.Append(')');
                return sb.ToString();
            }
        }
    }

    public static class SignalUtility
    {
        static readonly Dictionary<Type, SignalInfo[]> s_Cache = new Dictionary<Type, SignalInfo[]>();
        static readonly SignalInfo[] s_None = new SignalInfo[0];

        const BindingFlags kFieldFlags = BindingFlags.Instance | BindingFlags.Public |
                                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public const BindingFlags MethodFlags = BindingFlags.Instance | BindingFlags.Public |
                                                BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // ------------------------------------------------------------ discovery

        /// <summary>All signal fields on a type, including inherited ones. Cached.</summary>
        public static SignalInfo[] GetSignals(Type type)
        {
            if (type == null) return s_None;

            SignalInfo[] cached;
            if (s_Cache.TryGetValue(type, out cached)) return cached;

            var list = new List<SignalInfo>();
            var seen = new HashSet<string>();

            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Component)
                                         && t != typeof(UnityEngine.Object) && t != typeof(object); t = t.BaseType)
            {
                var fields = t.GetFields(kFieldFlags);
                for (int i = 0; i < fields.Length; i++)
                {
                    var f = fields[i];
                    if (!typeof(SignalBase).IsAssignableFrom(f.FieldType)) continue;
                    if (f.FieldType.IsAbstract) continue;
                    if (!seen.Add(f.Name)) continue;
                    list.Add(Describe(f));
                }
            }

            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            cached = list.Count == 0 ? s_None : list.ToArray();
            s_Cache[type] = cached;
            return cached;
        }

        static SignalInfo Describe(FieldInfo field)
        {
            var argTypes = field.FieldType.IsGenericType
                ? field.FieldType.GetGenericArguments()
                : Type.EmptyTypes;

            var attr = (SignalAttribute)Attribute.GetCustomAttribute(field, typeof(SignalAttribute));
            var names = new string[argTypes.Length];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = attr != null && attr.ArgumentNames.Length > i && !string.IsNullOrEmpty(attr.ArgumentNames[i])
                    ? attr.ArgumentNames[i]
                    : DefaultArgumentName(argTypes[i], i, argTypes.Length);
            }

            return new SignalInfo
            {
                Field = field,
                Name = field.Name,
                ArgumentTypes = argTypes,
                ArgumentNames = names,
                Description = attr != null ? attr.Description : null,
                DeclaringType = field.DeclaringType,
            };
        }

        static string DefaultArgumentName(Type t, int index, int count)
        {
            if (count == 1)
            {
                var n = PrettyType(t);
                n = n.Replace("[]", "s");
                return char.ToLowerInvariant(n[0]) + n.Substring(1);
            }
            return "arg" + index;
        }

        public static SignalInfo Find(Type type, string signalName)
        {
            var all = GetSignals(type);
            for (int i = 0; i < all.Length; i++)
                if (all[i].Name == signalName) return all[i];
            return null;
        }

        public static bool HasSignals(Component component)
        {
            return component != null && GetSignals(component.GetType()).Length > 0;
        }

        // ------------------------------------------------------------- instances

        /// <summary>
        /// Reads the signal instance off a component, creating it if the field was left null.
        /// Also stamps Name/Owner so error messages and the editor can identify it.
        /// </summary>
        public static SignalBase GetInstance(object owner, SignalInfo info)
        {
            if (owner == null || info == null) return null;

            var signal = info.Field.GetValue(owner) as SignalBase;
            if (signal == null)
            {
                signal = (SignalBase)Activator.CreateInstance(info.Field.FieldType);
                try { info.Field.SetValue(owner, signal); }
                catch (Exception e)
                {
                    Debug.LogWarning(string.Format(
                        "Could not initialise signal '{0}' on {1} ({2}). Initialise it inline: " +
                        "public {3} {0} = new {3}();", info.Name, owner.GetType().Name, e.Message,
                        PrettyType(info.Field.FieldType)));
                }
            }

            if (string.IsNullOrEmpty(signal.Name)) signal.Name = info.Name;
            if (signal.Owner == null) signal.Owner = owner as UnityEngine.Object;
            return signal;
        }

        public static SignalBase GetInstance(Component owner, string signalName)
        {
            if (owner == null) return null;
            return GetInstance(owner, Find(owner.GetType(), signalName));
        }

        // --------------------------------------------------------------- methods

        /// <summary>
        /// Methods on <paramref name="targetType"/> that can receive the given signal
        /// arguments followed by the bound extra arguments (Godot appends binds after
        /// the signal's own parameters).
        /// </summary>
        public static List<MethodInfo> FindCompatibleMethods(Type targetType, Type[] signalArgs, Type[] bindTypes)
        {
            var result = new List<MethodInfo>();
            if (targetType == null) return result;

            var seen = new HashSet<string>();
            for (var t = targetType; t != null && t != typeof(MonoBehaviour) && t != typeof(Component)
                                                && t != typeof(UnityEngine.Object) && t != typeof(object); t = t.BaseType)
            {
                var methods = t.GetMethods(MethodFlags);
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (!IsUsableReceiver(m)) continue;
                    if (!Matches(m, signalArgs, bindTypes)) continue;
                    if (!seen.Add(MethodKey(m))) continue;
                    result.Add(m);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        public static MethodInfo FindMethod(Type targetType, string methodName, Type[] signalArgs, Type[] bindTypes)
        {
            for (var t = targetType; t != null && t != typeof(object); t = t.BaseType)
            {
                var methods = t.GetMethods(MethodFlags);
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (m.Name != methodName) continue;
                    if (!IsUsableReceiver(m)) continue;
                    if (Matches(m, signalArgs, bindTypes)) return m;
                }
            }
            return null;
        }

        static bool IsUsableReceiver(MethodInfo m)
        {
            if (m.IsSpecialName || m.IsGenericMethod || m.IsAbstract) return false;
            if (m.Name.IndexOf('<') >= 0) return false;        // compiler generated
            if (m.ReturnType != typeof(void) && m.ReturnType.IsByRef) return false;
            var ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].IsOut || ps[i].ParameterType.IsByRef) return false;
            return true;
        }

        static bool Matches(MethodInfo m, Type[] signalArgs, Type[] bindTypes)
        {
            int bindCount = bindTypes == null ? 0 : bindTypes.Length;
            var ps = m.GetParameters();
            if (ps.Length != signalArgs.Length + bindCount) return false;

            for (int i = 0; i < signalArgs.Length; i++)
                if (!ps[i].ParameterType.IsAssignableFrom(signalArgs[i])) return false;

            for (int i = 0; i < bindCount; i++)
                if (bindTypes[i] != null && !ps[signalArgs.Length + i].ParameterType.IsAssignableFrom(bindTypes[i]))
                    return false;

            return true;
        }

        static string MethodKey(MethodInfo m)
        {
            var sb = new StringBuilder(m.Name);
            foreach (var p in m.GetParameters()) sb.Append('|').Append(p.ParameterType.FullName);
            return sb.ToString();
        }

        public static string DescribeMethod(MethodInfo m)
        {
            var sb = new StringBuilder(m.Name);
            sb.Append('(');
            var ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(PrettyType(ps[i].ParameterType)).Append(' ').Append(ps[i].Name);
            }
            sb.Append(')');
            return sb.ToString();
        }

        // ---------------------------------------------------------------- naming

        static readonly Dictionary<Type, string> s_Keywords = new Dictionary<Type, string>
        {
            { typeof(void), "void" }, { typeof(bool), "bool" }, { typeof(int), "int" },
            { typeof(uint), "uint" }, { typeof(long), "long" }, { typeof(float), "float" },
            { typeof(double), "double" }, { typeof(string), "string" }, { typeof(object), "object" },
            { typeof(byte), "byte" }, { typeof(char), "char" }, { typeof(short), "short" },
        };

        /// <summary>Short, C#-looking type name (int, float, Vector3, Signal&lt;int&gt;).</summary>
        public static string PrettyType(Type t)
        {
            if (t == null) return "?";
            string keyword;
            if (s_Keywords.TryGetValue(t, out keyword)) return keyword;
            if (t.IsArray) return PrettyType(t.GetElementType()) + "[]";
            if (!t.IsGenericType) return t.Name;

            var sb = new StringBuilder();
            var name = t.Name;
            int tick = name.IndexOf('`');
            sb.Append(tick > 0 ? name.Substring(0, tick) : name).Append('<');
            var args = t.GetGenericArguments();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(PrettyType(args[i]));
            }
            sb.Append('>');
            return sb.ToString();
        }

        /// <summary>Type name usable inside a generated script file.</summary>
        public static string CodeType(Type t)
        {
            string keyword;
            if (s_Keywords.TryGetValue(t, out keyword)) return keyword;
            if (t.IsArray) return CodeType(t.GetElementType()) + "[]";
            if (t.Namespace == null || t.Namespace == "UnityEngine" || t.Namespace == "System"
                || t.Namespace.StartsWith("UnityEngine.")) return PrettyType(t);
            return (t.FullName ?? t.Name).Replace('+', '.');
        }

        /// <summary>"OnPlayerHealthChanged" — the C# flavour of Godot's _on_player_health_changed.</summary>
        public static string SuggestMethodName(Type sourceType, string signalName)
        {
            var source = sourceType != null ? sourceType.Name : "";
            var name = "On" + source + signalName;
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
                if (char.IsLetterOrDigit(name[i]) || name[i] == '_') sb.Append(name[i]);
            return sb.ToString();
        }
    }
}
