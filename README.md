# GodotSignals for Unity

A Godot-style signal system for Unity, with an editor dock that works like Godot's
**Node ▸ Signals** panel: select a GameObject, see the signals its components declare,
double-click one, pick a receiver and a method, and the connection is saved in the scene.

## Install

Copy the `GodotSignals` folder into your project, e.g. `Assets/Plugins/GodotSignals`.
Two assembly definitions are included; `Assembly-CSharp` references them automatically,
so your own scripts can use `using GodotSignals;` right away.

Open the dock with **Window ▸ Signals** (or right-click any component header ▸ *Signals...*).
Dock it next to the Inspector.

## Declaring and emitting

```csharp
using USignals;
using UnityEngine;

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
    }
}
```

A field is a signal simply because its type derives from `SignalBase`
(`Signal`, `Signal<T>`, `Signal<T1,T2>`, `Signal<T1,T2,T3>`). Public or private, both are
found. The `[Signal]` attribute is optional and only supplies argument names and a
tooltip for the editor and for generated method stubs.

**Always initialise the field inline** (`= new Signal()`). If you forget, the system
creates the instance on first use, but inline initialisation keeps things predictable.

## Connecting from the editor

1. Select the GameObject that *emits* the signal.
2. In the **Signals** window, double-click the signal (or select it and press *Connect...*).
3. In the dialog choose:
   - **Receiver** — any GameObject in the scene, then one of its components.
   - **Method** — an existing compatible method, or tick *Create new method* to have a
     stub written into the receiver's script (Godot's behaviour). The suggested name is
     `OnPlayerHealthChanged`, the C# spelling of Godot's `_on_player_health_changed`.
   - **Extra arguments (binds)** — constant values appended after the signal's own
     arguments, exactly like Godot's binds.
   - **Deferred** — invoke at the end of the frame instead of immediately.
   - **One Shot** — disconnect after the first emission.
4. Press *Connect*. The connection appears as a child row under the signal.

Connections are stored in a hidden `SignalConnections` component on the emitter's
GameObject (Godot stores them in the emitting node's scene data), so they survive
domain reloads, are part of prefabs and prefab overrides, and support undo.
They are wired up in `Awake`.

Right-click a connection row for *Edit*, *Go to Method*, *Select Target*,
*Enabled* and *Disconnect*. Broken connections (renamed or deleted method) are drawn
in red with the reason as a tooltip, and logged as a warning when they fail to bind.

> After *Create new method*, the row stays red until Unity finishes recompiling —
> the method genuinely does not exist yet at that moment.

## Connecting from code

```csharp
health.HealthChanged.Connect(OnHealthChanged);              // connect(...)
health.HealthDepleted += OnDied;                            // += also works
health.HealthDepleted.Connect(OnDied, ConnectFlags.OneShot | ConnectFlags.Deferred);

health.HealthChanged.Disconnect(OnHealthChanged);
health.HealthDepleted -= OnDied;

bool connected = health.HealthDepleted.IsConnected(OnDied);
```

String-based access, for the rare dynamic case:

```csharp
component.EmitSignal("HealthChanged", 50, 100);
component.HasSignal("HealthChanged");
component.GetSignal("HealthChanged").DisconnectAll();
```

## Behaviour notes

- Connections whose receiver is a destroyed `UnityEngine.Object` are dropped
  automatically on the next emission, so a destroyed listener never causes a
  `MissingReferenceException`.
- Exceptions thrown by one handler are logged and do not stop the other handlers.
- Connecting the same callback twice is ignored with a warning (Godot errors).
- Handlers may connect or disconnect during an emission; the emission iterates a snapshot.
- Editor connections with no binds and an exactly matching signature are bound as real
  delegates, so emitting costs no reflection. Binds fall back to `MethodInfo.Invoke`.
- Deferred calls are flushed in `LateUpdate` by a hidden `[SignalDispatcher]` object
  created on demand. Outside play mode they run immediately.

## Limitations

- Up to three signal arguments out of the box. Adding `Signal<T1,T2,T3,T4>` is a
  copy-paste of the existing class in `Runtime/Signal.cs`.
- Signals live on components, not on `GameObject`s, so the editor lists them per component.
- The emitter GameObject must be active for its connections to be wired (`Awake`).
- Method stub generation matches braces textually; it does not parse braces inside
  comments or strings. It refuses to touch scripts outside `Assets/`.
- Bind values support int, float, bool, string, `UnityEngine.Object`, Vector2, Vector3 and Color.
