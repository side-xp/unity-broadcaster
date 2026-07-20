# Event Kinds

Every Broadcaster event implements exactly one of four **kind markers**. The kind you pick is a contract about what a receiver may do with the event, and it decides which verbs you use to send and to register. This guide covers all four in order (*Signals*, *Commands*, *Requests*, and *Cues*) plus **providers**, the state companion to signals.

If you haven't yet, read [Core Concepts](../core-concepts.md) first: it covers the ideas shared by every kind (owners, cleanup, exact-type dispatch, the façade, async). Everything below assumes them.

All examples use the static `Broadcaster` façade and the `SideXP.Broadcaster` namespace. Every API shown also exists on an `EventBus` instance.

## Quick reference

Not sure which kind an event should be? Pick by what a receiver is allowed to do with it:

| Kind | Choose it when… | Returns | Examples |
|---|---|---|---|
| [**Signal**](#signals) | Something *happened* and you're only announcing it. Any number of listeners, or none. | nothing | `PlayerMoved`, `ScoreChanged`, `EnemyDied` |
| [**Command**](#commands) | Something *must be done*, and exactly one system is responsible for doing it. | an ack, or what the action produced | `ChangeScore`, `MoveUnit → Path`, `SpawnEnemy → Enemy` |
| [**Request**](#requests) | You need to *read* information that exists independently of the asking (answering must not change anything). | the answer, always | `GetPlayerCell → Vector2Int`, `IsTileFree → bool` |
| [**Cue**](#cues) | Timed reactions must *all play out*, and an orchestrator wants to await the whole beat. | an awaitable completion | `HitFeedback`, `LevelIntro`, `DeathSequence` |

Three questions settle almost every choice:

- **Announce or instruct?** Nobody *has* to react → [*Signal*](#signals). Exactly one system must act → [*Command*](#commands).
- **Does answering change the world?** Yes → [*Command*](#commands). No, it's a pure read → [*Request*](#requests).
- **Do I need to wait for the reactions?** No → [*Signal*](#signals). Yes, await them all → [*Cue*](#cues).

## Documenting events with `[Event]`

The `[Event]` attribute is **optional metadata for tooling only**, the bus never requires it. It gives the editor windows a friendly name and description, and lets an event opt out of a few tooling behaviors:

```csharp
[Event("The player successfully moved to an adjacent tile.")]
public struct PlayerMoved : ISignal { public Vector2Int To; }

[Event(Name = "Combat/Damaged", Description = "The player took damage.", OmitSnapshot = true)]
public struct PlayerDamaged : ISignal { public int Amount; }
```

- `Name`: the label the Events window shows instead of the type name. Supports `/`-separated paths for grouping.
- `Description`: a human-readable summary, shown in the Events window and the monitor. The single-argument constructor sets this.
- `OmitSnapshot`: capture only the type name, not the field values (for sensitive, huge, or expensive-to-stringify payloads).
- `Hidden`: leave the type out of the Events window catalog by default.

## Signals

A **signal** (`ISignal`) is a fire-and-forget notification: *"this happened."* It can have any number of listeners, or none. It is the lightest kind and the right tool whenever a system just needs to announce something and doesn't care who, if anyone, reacts.

**Declare** one as a type implementing `ISignal`:

```csharp
[Event("The player successfully moved to an adjacent tile.")]
public struct PlayerMoved : ISignal
{
    public Vector2Int From;
    public Vector2Int To;
}
```

**Subscribe** a listener, giving an owner (see [Owners and cleanup](../core-concepts.md#owners-and-cleanup)):

```csharp
void OnEnable() => Broadcaster.Subscribe<PlayerMoved>(this, OnPlayerMoved);
void OnDisable() => Broadcaster.UnregisterAll(this);

void OnPlayerMoved(PlayerMoved e) => _camera.FollowTo(e.To);
```

**Emit** by constructing the event and sending it. Delivery is synchronous and in registration order. When `Emit` returns, every listener has run:

```csharp
Broadcaster.Emit(new PlayerMoved { From = _cell, To = target });
```

Zero listeners is fine (the emit is silent). A listener that throws is isolated: its exception is logged and the other listeners still run.

To remove a single listener without dropping the rest of the owner's registrations, either keep the returned handle and dispose it, or use `Unsubscribe`, which matches by delegate equality so the same method group works:

```csharp
Broadcaster.Unsubscribe<PlayerMoved>(OnPlayerMoved);
```

### State and providers

The bus **never stores a payload**. So how does a listener that subscribes *after* an event was emitted catch up to the current value? A **provider**: the state owner registers a function that answers *"what is the current value right now?"*, and a listener can pull it on subscribe.

```csharp
// The state owner offers the current value (invoked lazily, never cached):
void Awake() => Broadcaster.Provide<FoodChanged>(this, () => new FoodChanged { Current = _food });

// A listener asks to be initialized with it the moment it subscribes:
void OnEnable() => Broadcaster.Subscribe<FoodChanged>(this, OnFoodChanged, init: true);
```

With `init: true`, if a provider is alive the listener is invoked immediately with its current value, *in addition* to future emits. With no provider alive, nothing happens. You can also read a provider on demand without subscribing:

```csharp
if (Broadcaster.TryGetCurrent<FoodChanged>(out FoodChanged current))
    _label.text = current.Current.ToString();
```

There is **one provider per signal type**. A second `Provide` for the same type is ignored (with a development diagnostic); pass `replace: true` for an intentional hand-off. Because the provider is invoked lazily and dies with its owner, its value is never stale, and stale replay across scene loads is impossible by construction.

> **Provider ordering.** `init` only sees providers registered *before* it runs.
> The reliable convention is: **owners `Provide` in `Awake`, subscribers `init` in `OnEnable` or `Start`.** `Awake` runs before `OnEnable`, so the provider is always in place when a subscriber pulls. If you `Provide` and `init` in the same phase, ordering between objects is undefined and the pull may miss.

## Commands

A **command** is an imperative: *"do it."* It has **exactly one** handler, which performs the action. Use a command when something must happen and exactly one system is responsible for making it happen.

There are two shapes. A **void command** (`ICommand`) is a plain instruction. A **valued command** (`ICommand<TResult>`) also reports *what the action produced*: a spawned instance, a clamped value, a refusal reason. The result must be something only performing the action yields. **If the data exists independently of the action, it's a [*Request*](#requests), not a command.**

**Register** the single handler with `Obey`:

```csharp
// Void command:
[Event("Change the player's food by Delta.")]
public struct ChangeFood : ICommand { public int Delta; }

void OnEnable() => Broadcaster.Obey<ChangeFood>(this, cmd => _food += cmd.Delta);

// Valued command (reports the new total):
[Event("Move a unit; reports the path it took.")]
public struct MoveUnit : ICommand<Path> { public Vector2Int To; }

void OnEnable() => Broadcaster.Obey<MoveUnit, Path>(this, cmd => _mover.MoveTo(cmd.To));
```

**Order** the command to run it. The void form returns a `bool` ack (whether a handler performed it). The valued form returns the result, with `TResult` inferred at the call site:

```csharp
bool handled = Broadcaster.Order(new ChangeFood { Delta = -1 });
Path path = Broadcaster.Order(new MoveUnit { To = cell });
```

Ordering a command with no handler logs a development error; the void `Order` returns `false`, and the valued `Order` throws (it has no result to return without a handler).

A second `Obey` for a type that already has a handler is ignored (with a development diagnostic); pass `replace: true` for an intentional hand-off, such as across an additive scene load.

### Async commands

When the action takes time, register an async handler by returning an `Awaitable`, and order it with `OrderAsync`:

```csharp
void OnEnable() =>
    Broadcaster.Obey<MoveUnit, Path>(this, async cmd => await _pathfinder.FindAsync(cmd.To));

Path path = await Broadcaster.OrderAsync(new MoveUnit { To = cell }, destroyCancellationToken);
```

`OrderAsync` works on **both** sync and async handlers (a sync handler simply completes immediately), so a caller that awaits doesn't need to know which kind it's talking to. The reverse is not allowed: calling the **synchronous** `Order` on an **async** handler is a development error (a synchronous call site can't wait for a deferred answer) (the void `Order` returns `false`, the valued `Order` throws).

The optional `CancellationToken` cancels the caller's wait. And the await never hangs: if the handler is unregistered mid-flight, the awaitable resolves as cancelled instead of waiting forever. If the handler throws, the awaitable faults and the exception surfaces at the await.

For coroutine or callback-based code that can't easily return an `Awaitable`, build one from `UnityEngine.AwaitableCompletionSource` and return its `Awaitable`.

## Requests

A **request** (`IRequest<TResult>`) is a question: *"tell me."* Like a command it has **exactly one** handler, and it always returns a `TResult`. Use a request to read information that exists independently of the asking.

The distinction from a valued command is a **contract of purity**: asking must be *safe*. A request handler must not mutate state, so asking the same question twice must not change anything. This is a convention (not enforced), but it is the whole point of the kind: **if answering would change the world, model it as a [*Command*](#commands)**.

**Register** the handler with `Answer`:

```csharp
[Event("What tile is the player currently standing on?")]
public struct GetPlayerCell : IRequest<Vector2Int> { }

void OnEnable() => Broadcaster.Answer<GetPlayerCell, Vector2Int>(this, _ => _cell);
```

**Ask** to get the answer, with `TResult` inferred at the call site:

```csharp
Vector2Int cell = Broadcaster.Ask(new GetPlayerCell());
```

A request handler is **mandatory**. Asking with no handler registered **throws**, in release too. A question with no answer has nothing to return. This is the one hard failure in the whole API. When an unanswered request is acceptable, use the tolerant `TryAsk`:

```csharp
if (Broadcaster.TryAsk(new GetPlayerCell(), out Vector2Int cell))
    _marker.MoveTo(cell);
```

A second `Answer` for the same type is ignored (development diagnostic); pass `replace: true` for an intentional hand-off.

### Async requests

As with commands, an async handler returns an `Awaitable<TResult>` and is asked with `AskAsync`:

```csharp
void OnEnable() =>
    Broadcaster.Answer<GetSaveSlot, SaveSlot>(this, async r => await _disk.LoadAsync(r.Index));

SaveSlot slot = await Broadcaster.AskAsync(new GetSaveSlot { Index = 0 }, destroyCancellationToken);
```

`AskAsync` works on both sync and async handlers; the synchronous `Ask`/`TryAsk` on an async handler is a development error (there is no synchronous answer to hand back). Cancellation and the never-hangs guarantee behave exactly as for [async commands](#async-commands).

## Cues

A **cue** (`ICue`) is an imperative-plural: *"everyone, do your part."* Like a signal it has `0..N` receivers (here called **performers**) but unlike a signal, sending a cue produces an `Awaitable` that completes only when **every** performer has finished. Cues are how an orchestrator (a sequencer, a director, a turn manager) coordinates presentation-tempo work (sounds, tweens, camera moves, ...) and waits for the whole beat to land before moving on.

The verbs read like a stage direction: the director **cues**, and the actors **perform**.

**Register** a performer with `Perform`. There are three shapes:

```csharp
// Instant, runs synchronously and completes immediately:
Broadcaster.Perform<HitFeedback>(this, cue => _sfx.PlayHit(cue.Target));

// Durative, returns an Awaitable; the cue waits for it:
Broadcaster.Perform<HitFeedback>(this, async cue => await _anim.PlayAsync(cue));

// Callback-style, for coroutine/callback code, hands you a `done` you must call:
Broadcaster.Perform<HitFeedback>(this, (cue, done) => StartCoroutine(Flash(cue, done)));
```

The async lambda binds to the durative overload automatically. A two-parameter lambda binds to the callback-style overload. A callback-style performer **must** eventually call `done`, or the cue never completes for it (until the performer is unregistered). Calling `done` twice is harmless.

**Cue** the event to fire it. Every performer starts synchronously in registration order (the instant sound punches this frame). The returned awaitable resolves when the *last* performer finishes:

```csharp
await Broadcaster.Cue(new HitFeedback { Target = enemy }, _skipToken);
```

Zero performers resolves instantly. A throwing performer is isolated (logged with its owner) and neither holds up nor kills the others. The `CancellationToken` cancels the cue as a whole: it resolves the awaitable as cancelled *and* fans out to every in-flight performer. And, as everywhere, it never hangs (a performer unregistered mid-cue resolves instead of stalling the when-all).

> **Only orchestrators await cues.** A cue is a presentation-tempo event. The systems that *await* a cue are the ones whose job is pacing: a sequence director, a turn manager, the code that must not advance until the beat lands.
> **Game logic should not await cues.** When logic needs to announce that something happened and move on, it emits a [Signal](#signals) (fire-and-forget), and lets performers react on their own schedule. Awaiting a cue from deep inside game logic couples that logic to the frame-rate of its presentation.

## Cleanup, once more

Whatever the kind, unregister when the owner goes away. The one-liner covers every kind at once:

```csharp
void OnDisable() => Broadcaster.UnregisterAll(this);
```

See [Owners and cleanup](../core-concepts.md#owners-and-cleanup) for handle-based removal and the full story.

## See it in context

The [Broadcaster Demo](demo.md) introduces these kinds one at a time, each solving a concrete coupling problem in a small turn-based game, the best next read once the vocabulary here is familiar.
