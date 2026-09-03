using USignals;
using UnityEngine;
using System.Collections;

public class Health : MonoBehaviour
{
    [SerializeField] int max = 100;
    int current;

    // Godot: signal health_depleted
    public Signal HealthDepleted = new Signal();

    // Godot: signal health_changed(amount, max)
    [Signal("amount", "max", Description = "Emitted whenever health changes.")]
    public Signal<int, int> HealthChanged = new Signal<int, int>();

    void Awake()
    {
        current = max;
    }

    public void TakeDamage(int amount)
    {
        current = Mathf.Max(0, current - amount);
        HealthChanged.Emit(current, max);   // emit_signal("health_changed", current, max)
        if (current == 0) HealthDepleted.Emit();

        StartCoroutine(FlashRed());
    }

    IEnumerator FlashRed()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            var originalColor = renderer.material.color;
            renderer.material.color = Color.red;
            yield return new WaitForSeconds(0.1f);
            renderer.material.color = originalColor;
        }
    }
}