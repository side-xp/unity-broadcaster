# Core Concepts

Broadcaster is a **code-first, type-keyed, intent-driven event bus** for decoupled communication in *Unity* games and packages. Systems talk to each other by exchanging **events** instead of holding references, so a system that announces something never needs to know who, if anyone, reacts.

This page covers the ideas shared by every event kind. For the day-to-day API of each kind, see [Event Kinds](guides/event-kinds.md). For a full worked example, see the [Broadcaster Demo](guides/demo.md).

## An event is a type

There is no separate "event id" and "payload". An event **is** a C# type, and that type is both the identity *and* the data:

```csharp
using SideXP.Broadcaster;

public struct PlayerMoved : ISignal
{
    public Vector2Int From;
    public Vector2Int To;
}
```

You send that event by value, its fields are the payload. Because events are plain types, they cost nothing to declare, they are easy to find (every send and every listener is a compile-time reference to the same type), and the compiler is what keeps senders and receivers in agreement.

Structs are the natural choice for events (no garbage on the hot path), but classes work too.

## The four kinds

Every event implements exactly one **kind marker**. The kind is a deliberate contract: it tells everyone what a receiver is allowed to do with the event, and it decides which bus verbs you use.

| Kind | Marker | Meaning | Receivers | Send verb | Register verb |
|---|---|---|---|---|---|
| **Signal** | `ISignal` | "it happened" | 0..N listeners | `Emit` | `Subscribe` |
| **Cue** | `ICue` | "everyone, do your part" | 0..N performers | `Cue` | `Perform` |
| **Command** | `ICommand` / `ICommand<TResult>` | "do it" | exactly 1 handler | `Order` | `Obey` |
| **Request** | `IRequest<TResult>` | "tell me" | exactly 1 handler | `Ask` | `Answer` |

The marker interfaces are required: the bus entry points are generically constrained on them, so your event vocabulary is explicit and checked at compile time. Pick the kind by the *tense* of the name: *past* (Signal), *imperative-plural* (Cue), *imperative* (Command), *interrogative*
(Request). [Event Kinds](guides/event-kinds.md) explains how to choose and how to use each.

## Two dispatch behaviors

Under the four kinds there are really only **two** behaviors:

- **Broadcast**: signals and cues go to a list of `0..N` receivers.
- **Handled**: commands and requests go to **exactly one** handler.

Everything else (return values, awaiting, cardinality diagnostics) is a variation on those two.

## Exact-type dispatch

Dispatch matches the **exact** type only. Emitting a `PlayerDied` never reaches a listener subscribed to a base `PlayerEvent`, even if `PlayerDied` derives from it. This is deliberate: dispatch is a single dictionary lookup, with no inheritance walking and no ambiguity about who gets called. If you want to observe *everything*, use the monitor and its editor windows (see [Tooling](#tooling)), not a base-type subscription.

## Owners and cleanup

Every registration takes an `object owner` as its first argument:

```csharp
Broadcaster.Subscribe<PlayerMoved>(this, OnPlayerMoved);
```

The owner powers bulk cleanup, diagnostics, and the "who" column in the monitor. It is usually the `MonoBehaviour` that registered, but it can be any object. There are two ways to unregister:

- **Bulk, by owner**: the blessed one-liner. In `OnDisable`, drop everything the component registered, of every kind, in a single call:

```csharp
void OnDisable() => Broadcaster.UnregisterAll(this);
```

- **Fine-grained, by handle**: every registration returns a `SubscriptionHandle`. Dispose it to remove just that one registration. The handle is a struct, is idempotent, and is safe to dispose twice or when `default`.

```csharp
SubscriptionHandle handle = Broadcaster.Subscribe<PlayerMoved>(this, OnPlayerMoved);
// ...later:
handle.Dispose();
```

Signals additionally support delegate-based removal with `Unsubscribe<T>(listener)`. It matches by delegate equality (target + method), so unsubscribing with the *same method group* you subscribed with works (you don't have to cache the delegate).

## The façade and the bus

There are two ways to reach the API:

- **`Broadcaster`**: a static façade over a single shared `EventBus`, exposed as `Broadcaster.Default`. This is what game code uses. The default bus is created lazily (so it also works in edit mode) and is recreated when you enter play mode, so a play session never starts with state left over from the previous one (even when domain reload is disabled).
- **`EventBus`**: the instantiable core that holds all the state and logic. Construct one with `new EventBus()` when you want an isolated or temporary bus: a scoped bus for a subsystem, or a fresh bus per test. The façade and an instance expose the same surface.

```csharp
// Game code (the shared bus):
Broadcaster.Emit(new PlayerMoved { To = cell });

// A test (an isolated bus, no shared state, no teardown):
var bus = new EventBus();
bus.Emit(new PlayerMoved { To = cell });
```

## Synchronous by default, async by returning the work

`Emit` and the synchronous verbs are **synchronous and main-thread only**. When `Emit` returns, every listener has already run, in registration order. There is no queued or deferred delivery (a signal is delivered now or not at all).

When work genuinely takes time, the bus models it by **returning the work**: a cue's send, and the async command/request verbs, hand you an `Awaitable` you can await. A durative receiver signals completion by *returning* an `Awaitable` rather than by calling back. There is no "I'm done" callback to wire up and no polling.

```csharp
Path path = await Broadcaster.OrderAsync(new MoveUnit { To = cell }, destroyCancellationToken);
```

Async verbs take an optional `CancellationToken` (a component's `destroyCancellationToken` pairs naturally). The bus itself is main-thread only. A handler may hop threads internally but resumes on the main thread, following `Awaitable`'s behavior. Crucially, an in-flight await **never hangs**: if the handler or performer you're waiting on is unregistered before it finishes, your await resolves (cancelled or faulted) instead of waiting forever.

## Diagnostics: loud in development, lean in release

The bus reports mistakes where it can, but only in the editor and development builds:

- **A throwing receiver is isolated.** Its exception is logged (with the owner as the Unity context object, so double-clicking the console entry pings it) and the other receivers still run.
- **Ordering a command with no handler** logs a development error, and the ack-returning `Order` returns `false`.
- **Registering a second handler or provider** for a type that already has one logs a development diagnostic and is ignored (the first registration stays authoritative). Pass `replace: true` for an intentional hand-off.
- **Asking a request with no handler is the one hard failure**: `Ask` throws in release too (a question with no answer has nothing to return). Use `TryAsk` when an unanswered request is acceptable.

## Tooling

Because every meaningful action flows through internal monitor hooks, Broadcaster ships with editor windows built entirely on top of them, under `Tools > Sideways Experiments > Broadcaster`:

- **Events**: a catalog of every event type in your project, grouped by kind and searchable, showing the `[Event]` name/description and live columns in play mode (listener count, provider alive, handler owner). Expand a row to edit an event's fields and fire it from the window. Works in edit mode too.
- **Timeline**: a recorder that renders every dispatch as a span across frames. Shows instant dispatches as points, durative ones as blocks, each with its receiver sub-spans, cascade edges, and any violations shown in place. Filter by kind, type, owner, or payload field, and click to ping owners.

The hooks compile only in the editor and development builds and are stripped from release, so there is no runtime cost in a shipped game. See the [Editor Windows](guides/editor-windows.md) guide for how to use them.

## Where to go next

- [Event Kinds](guides/event-kinds.md): the API for each kind, with the rules to follow.
- [Broadcaster Demo](guides/demo.md): the concepts applied end-to-end in a small game.
