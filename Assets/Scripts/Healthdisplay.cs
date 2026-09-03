using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Example receiver for the signals declared by Health.
/// Put this on any GameObject (the same one as Health, or a separate HUD object),
/// then connect from Window ▸ Signals with Health selected.
///
/// Note there is no "using USignals" here — a receiver is just a plain component
/// with methods whose parameters match the signal.
/// </summary>
public class HealthDisplay : MonoBehaviour
{
    [SerializeField] Slider bar;          // optional: scaled to show the health fraction
    [SerializeField] GameObject deathEffect; // optional: spawned when health runs out

    // ---- receivers for Health.HealthChanged(int amount, int max) ----------------

    // Connect: Health.HealthChanged  ->  HealthDisplay.OnHealthChanged
    public void OnHealthChanged(int amount, int max)
    {
        float fraction = max > 0 ? (float)amount / max : 0f;
        if (bar != null)
        {
            bar.value = fraction;
        }
        Debug.Log(string.Format("{0}: {1}/{2}", name, amount, max), this);
    }

    // Same signal, but with one bound extra argument (a "bind" in Godot).
    // Add a String argument in the connect dialog and this method shows up.
    public void OnHealthChangedLabelled(int amount, int max, string label)
    {
        Debug.Log(string.Format("{0} {1}/{2}", label, amount, max), this);
    }

    // A receiver may ignore the arguments it does not care about only by declaring
    // fewer parameters is NOT allowed — the parameter count must match. Use this
    // shape if you only want the current value plus a bound constant:
    public void OnHealthChangedFlash(int amount, int max)
    {
        if (amount < max / 4) Debug.LogWarning("Low health!", this);
    }

    // ---- receivers for Health.HealthDepleted() ---------------------------------

    // Connect: Health.HealthDepleted  ->  HealthDisplay.OnHealthDepleted
    // Tick "One Shot" so it only ever fires once.
    public void OnHealthDepleted()
    {
        if (deathEffect != null) Instantiate(deathEffect, transform.position, Quaternion.identity);
        Debug.Log(name + ": died", this);
    }

    // Private methods work too — Godot allows connecting to them, and so does this.
    // Tick "Deferred" for anything that destroys or disables objects, so it runs
    // at the end of the frame instead of in the middle of the emission.
    void OnHealthDepletedDisable()
    {
        gameObject.SetActive(false);
    }

    // With a bound string argument, one method can serve several sources:
    //   Player.HealthDepleted -> OnSomethingDied, bind "Player"
    //   Enemy.HealthDepleted  -> OnSomethingDied, bind "Enemy"
    public void OnSomethingDied(string who)
    {
        Debug.Log(who + " died", this);
    }
}