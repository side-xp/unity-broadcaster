#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using UnityEngine;

namespace SideXP.Broadcaster.Scavengers
{
    [Event("Emitted when the player successfully moved to an adjacent tile.")]
    public struct PlayerMoved : ISignal { }

    [Event("Emitted when the player picked up a food collectible.")]
    public struct PlayerAte : ISignal { }

    [Event("Emitted when the player picked up a soda collectible.")]
    public struct PlayerDrank : ISignal { }

    [Event("Emitted when the player chopped a destructible wall.")]
    public struct WallChopped : ISignal { }

    [Event("Emitted when an enemy attacked the player.")]
    public struct EnemyAttacked : ISignal { }

    [Event("Emitted when the player ran out of food; the run is over.")]
    public struct PlayerDied : ISignal { }

    /// <summary>
    /// Describes why the player's food total changed.
    /// </summary>
    public enum FoodChangeSource
    {
        /// <summary>The player spent food taking a turn.</summary>
        Move,
        /// <summary>The player picked up a collectible (food or soda).</summary>
        Pickup,
        /// <summary>The player took damage (an enemy hit, a trap, ...).</summary>
        Damage,
    }

    [Event("Emitted whenever the player's food total changed. Current is the new total, Delta the signed change, Source why it happened.")]
    public struct FoodChanged : ISignal
    {
        [Tooltip("The player's food total after the change.")]
        public int current;

        [Tooltip("The signed amount the total just changed by (negative for a move or damage, positive for a pickup).")]
        public int delta;

        [Tooltip("What caused the change.")]
        public FoodChangeSource source;
    }

    [Event("Emitted when a new level started.")]
    public struct LevelStarted : ISignal
    {
        [Tooltip("The 1-based number of the level that just started.")]
        public int level;
    }

    [Event("Emitted when the run ended; Level is how far the player got.")]
    public struct RunEnded : ISignal
    {
        [Tooltip("The level the player had reached when the run ended.")]
        public int level;
    }

    [Event("End the current run (starvation, a lethal tile, a debug shortcut, ...).")]
    public struct EndRun : ICommand { }

    [Event("Damage the player, reducing its food by Amount.")]
    public struct DamagePlayer : ICommand
    {
        [Tooltip("How much food the damage costs the player.")]
        public int amount;
    }

    [Event("Change the player's food by Delta, tagged with why (Source).")]
    public struct AdjustFood : ICommand
    {
        [Tooltip("The signed amount to change the food total by.")]
        public int delta;
        [Tooltip("What caused the change.")]
        public FoodChangeSource source;
    }

    [Event("End the player's turn.")]
    public struct EndPlayerTurn : ICommand { }

    [Event("Whether it is currently the player's turn.")]
    public struct PlayerTurn : ISignal
    {
        [Tooltip("True while the player may act.")]
        public bool active;
    }

    [Event("The player's current world position.")]
    public struct PlayerPosition : ISignal
    {
        [Tooltip("The player's position right now.")]
        public Vector3 position;
    }

    [Event("The enemies' turn: every enemy performs, and the game waits for all of them to finish.")]
    public struct EnemyTurn : ICue { }

    [Event("The level intro: the UI shows the level card, and the game waits for it before play begins.")]
    public struct LevelIntro : ICue { }
}
#pragma warning restore IDE1006 // Naming Styles
