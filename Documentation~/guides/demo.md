# Broadcaster Demo

The package comes with a sample project that illustrates how to use the Broadcaster features in real situations.

You can import it from the *Packages Manager* window:

1. In your *Unity* project, go to `Window > Package Management > Package Manager`
2. Select *SideXP - Broadcaster* in the packages list
3. Click on the *Samples* tab
4. Import the *Scavengers* project

This will install the sample files into your `Assets` folder.

## The game concept

The game is a turn-based tile-based roguelike where the player must survive incoming zombies.

In short:

- The player can move by pressing arrow keys
- Each movement is a turn and 1 Food
- Running out of food is game over
- Enemies can also move after player turn, and attack the player if in range
- Enemy hits decrease food
- Move on a tile with food grants +10 food, soda grants +20 food
- Enemies can't be defeated, and will increase in number as the player progresses
- Reach the *Exit* panel to go to the next level

The concept come from an [old Unity tutorial](https://learn.unity.com/course/intermediate-3d-game-development/unit/2d-roguelike-tutorial-legacy). We reused their scripts and assets, but reworked it with Broadcaster features.

> For reference, and for comparison, the original scripts (before any integration of Broadcaster) are available in the `Original` folder of the sample project. We only reworked them so you don't need to setup anything for running the demo (eg. tags, layers, ...).

## Reworking with Broadcaster

Broadcaster lets a system announce *that something happened*, or *ask another system to do something*, without holding a reference to whoever handles it. Systems no longer wire themselves to each other. Instead, they exchange **events**, and the *kind* of event you pick (a signal, a command, a request, a cue) is a deliberate contract about what a listener is allowed to do with it.

The rework introduces those kinds one at a time, each solving a concrete coupling problem in the original tutorial code. Each section presents a problem, then shows how Broadcaster features solve it.

## Signals - Making the audio system independent

A **signal** (`ISignal`) is a fire-and-forget notification. It can have any number of listeners or none. It's the lightest event kind, and the right tool whenever a system just needs to announce that something happened and doesn't care who, if anyone, reacts.

### The problem

In the original code, audio is a `SoundManager` singleton, and gameplay plays sounds by reaching for it directly:

```csharp
// Original Player, Enemy and Wall all do this:
SoundManager.instance.RandomizeSfx(moveSound1, moveSound2);
```

`Player` calls it in three places, `Enemy` and `Wall` once each. Worse, the game-over path reaches *through* the singleton into one of its fields:

```csharp
// Original Player.CheckIfGameOver
SoundManager.instance.PlaySingle(gameOverSound);
// Player knows SoundManager owns an AudioSource named musicSource
SoundManager.instance.musicSource.Stop();
```

Three things are wrong here:

- **Delete `SoundManager` and three unrelated systems break.** `Player`, `Enemy` and `Wall` all depend on an audio class to do their own job.
- **Gameplay owns the sounds.** Each of those scripts carries `AudioClip` fields (`moveSound1`, `chopSound1`, …) and decides what a move or a chop sounds like. So audio is smeared across the gameplay code.
- **Player has an additional hard dependency.** In the original code, `Player` itself stops the music when it dies (a job that isn't really the player's), which ties it even more tightly to the audio system.

### Broadcaster's solution

Flip the dependency. Gameplay **emits a signal that describes what happened**; the audio system **listens** and plays the appropriate sound. Nothing calls the audio system anymore, so nothing breaks when it's gone.

The core idea behind **signals** is to announce *what happened*, not *what should be done about it*.

In our case specifically, don't write a `PlaySfx` struct that carries an `AudioClip`. That would only solve the problem partially: `Player`, `Enemy` and `Wall` would still have to own the `AudioClip` fields. Instead, implement a `PlayerMoved` signal with no payload. The audio system is then responsible for listening for that signal and playing the appropriate sounds, which it owns. We can then remove the `moveSound` fields from `Player`, making the two systems independent.

Repeat the process for the other signals and sounds, and you'll be able to completely remove the audio system from the scene without breaking anything. Plus, if you later replace the audio system with a third-party solution such as [*FMOD*](https://www.fmod.com), the swap is painless, since audio is already completely decoupled from the rest of the game!

### The implementation

**1. Declare the signals.** Each is a small type implementing `ISignal`. The `[Event(...)]` attribute is optional (it's meant for documentation purposes), so it's worth filling in as you introduce each type:

```csharp
[Event("The player successfully moved to an adjacent tile.")]
public struct PlayerMoved : ISignal { }
```

The audio rework needs six (none of them carrying a payload), since the audio system only needs to know that each one happened:

| Signal | Emitted when… |
| --- | --- |
| `PlayerMoved` | the player moves to a free tile |
| `PlayerAte` | the player picks up food |
| `PlayerDrank` | the player picks up soda |
| `WallChopped` | the player damages a wall |
| `EnemyAttacked` | an enemy hits the player |
| `PlayerDied` | the player runs out of food |

**2. Emit them from gameplay,** at the moment each thing happens, in place of the old `SoundManager.instance.*` calls:

```csharp
// Player.AttemptMove
if (Move(xDir, yDir, out hit))
    Broadcaster.Emit(new PlayerMoved());
```

> Food and soda need different sounds, but both are a single `Collectible` component. So the collectible carries a `kind`, and `Player` emits the matching signal (`PlayerAte` or `PlayerDrank`) without the audio system ever seeing a collectible.

**3. Make `SoundManager` a pure listener.** It subscribes to every signal in `OnEnable`, releases everything in `OnDisable`, owns all the clips, and keeps `RandomizeSfx`/`PlaySingle` private:

```csharp
private void OnEnable()
{
    Broadcaster.Subscribe<PlayerMoved>(this, OnPlayerMoved);
    Broadcaster.Subscribe<PlayerAte>(this, OnPlayerAte);
    // …one Subscribe per signal
}

private void OnDisable()
{
    // The blessed one-liner: drop every registration owned by this object.
    Broadcaster.UnregisterAll(this);
}

private void OnPlayerMoved(PlayerMoved signal) => RandomizeSfx(moveSound1, moveSound2);
```

The `musicSource.Stop()` that used to live in `Player` moves here too, into the `PlayerDied` handler where it belongs — stopping the music is the audio system's business, not the player's.

> **A subtlety worth knowing:** the bus dispatches on the *exact* type you emit. `Broadcaster.Emit(new PlayerMoved())` is fine, but emitting through a variable typed as `ISignal` would reach no listener (and log a development-time error). Always emit the concrete type.

### The payoff

Delete the `SoundManager` from the scene and press *Play*: the game runs exactly as before, with no errors — just silent. That single test *is* the proof that no system depends on the audio system.

And feedback is now purely additive. Want a particle when the player eats? Add a listener on `PlayerAte`; it touches no gameplay code, and the existing sound listener never notices the second one.