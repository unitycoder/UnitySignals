using System;

namespace USignals
{
    /// <summary>
    /// Optional metadata for a signal field. Equivalent to Godot's
    /// <c>signal health_changed(amount, max)</c> argument names.
    ///
    ///     [Signal("amount", "max")]
    ///     public Signal&lt;int, int&gt; HealthChanged = new Signal&lt;int, int&gt;();
    ///
    /// A field is treated as a signal purely because its type derives from
    /// <see cref="SignalBase"/>; this attribute only improves what the editor shows
    /// and what generated method stubs name their parameters.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class SignalAttribute : Attribute
    {
        public string[] ArgumentNames { get; private set; }
        public string Description { get; set; }

        public SignalAttribute(params string[] argumentNames)
        {
            ArgumentNames = argumentNames ?? new string[0];
        }
    }
}
