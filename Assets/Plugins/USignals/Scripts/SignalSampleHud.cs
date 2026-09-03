using UnityEngine;

namespace USignals.Samples
{
    /// <summary>
    /// Receiver. Usually you connect these from the Signals window instead of code —
    /// this file just shows the code-side API.
    /// </summary>
    public class SignalSampleHud : MonoBehaviour
    {
        [SerializeField] SignalSampleHealth m_Health;

        void OnEnable()
        {
            if (m_Health == null) return;
            m_Health.HealthChanged.Connect(OnHealthChanged);
            m_Health.HealthDepleted += OnDied;                 // += works as well
        }

        void OnDisable()
        {
            if (m_Health == null) return;
            m_Health.HealthChanged.Disconnect(OnHealthChanged);
            m_Health.HealthDepleted -= OnDied;
        }

        void OnHealthChanged(int amount, int max)
        {
            Debug.Log(string.Format("HP {0}/{1}", amount, max), this);
        }

        void OnDied()
        {
            Debug.Log("Died", this);
        }
    }
}
