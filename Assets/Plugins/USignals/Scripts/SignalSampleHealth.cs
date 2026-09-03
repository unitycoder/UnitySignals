using UnityEngine;

namespace USignals.Samples
{
    /// <summary>Emitter. Declare signals as public fields and initialise them inline.</summary>
    public class SignalSampleHealth : MonoBehaviour
    {
        [SerializeField] int m_Max = 100;
        int m_Current;

        // Godot: signal health_depleted
        public Signal HealthDepleted = new Signal();

        // Godot: signal health_changed(amount, max)
        [Signal("amount", "max", Description = "Emitted whenever health changes.")]
        public Signal<int, int> HealthChanged = new Signal<int, int>();

        void Awake()
        {
            m_Current = m_Max;
        }

        [ContextMenu("Take 10 Damage")]
        public void TakeDamage() { TakeDamage(10); }

        public void TakeDamage(int amount)
        {
            m_Current = Mathf.Max(0, m_Current - amount);
            HealthChanged.Emit(m_Current, m_Max);          // emit_signal("health_changed", ...)
            if (m_Current == 0) HealthDepleted.Emit();
        }
    }
}
