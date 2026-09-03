using UnityEngine;

namespace USignals
{
    /// <summary>
    /// Loose, string-based access — the equivalent of Godot's
    /// <c>emit_signal("name", args)</c> / <c>is_connected(...)</c>.
    /// Prefer the typed fields (<c>HealthChanged.Emit(50)</c>) in normal code.
    /// </summary>
    public static class SignalExtensions
    {
        public static SignalBase GetSignal(this Component component, string signalName)
        {
            var signal = SignalUtility.GetInstance(component, signalName);
            if (signal == null)
                Debug.LogWarning(string.Format("'{0}' has no signal named '{1}'.",
                    component != null ? component.GetType().Name : "null", signalName), component);
            return signal;
        }

        public static void EmitSignal(this Component component, string signalName, params object[] args)
        {
            var signal = component.GetSignal(signalName);
            if (signal != null) signal.EmitBoxed(args);
        }

        public static bool HasSignal(this Component component, string signalName)
        {
            return component != null && SignalUtility.Find(component.GetType(), signalName) != null;
        }

        /// <summary>Disconnects everything connected to a signal, editor connections included.</summary>
        public static void DisconnectAll(this Component component, string signalName)
        {
            var signal = component.GetSignal(signalName);
            if (signal != null) signal.DisconnectAll();
        }
    }
}
