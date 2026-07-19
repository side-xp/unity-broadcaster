namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// Emitted when the player successfully moves to an adjacent tile.
    /// </summary>
    [Event("The player successfully moved to an adjacent tile.")]
    public struct PlayerMoved : ISignal { }

    /// <summary>
    /// Emitted when the player picks up a food collectible.
    /// </summary>
    [Event("The player picked up a food collectible.")]
    public struct PlayerAte : ISignal { }

    /// <summary>
    /// Emitted when the player picks up a soda collectible.
    /// </summary>
    [Event("The player picked up a soda collectible.")]
    public struct PlayerDrank : ISignal { }

    /// <summary>
    /// Emitted when the player chops a destructible wall.
    /// </summary>
    [Event("The player chopped a destructible wall.")]
    public struct WallChopped : ISignal { }

    /// <summary>
    /// Emitted when an enemy attacks the player.
    /// </summary>
    [Event("An enemy attacked the player.")]
    public struct EnemyAttacked : ISignal { }

    /// <summary>
    /// Emitted when the player runs out of food and the run ends.
    /// </summary>
    [Event("The player ran out of food; the run is over.")]
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

    /// <summary>
    /// Emitted whenever the player's food total changes.
    /// </summary>
    [Event("The player's food total changed. Current is the new total, Delta the signed change, Source why it happened.")]
    public struct FoodChanged : ISignal
    {
        /// <summary>The player's food total after the change.</summary>
        public int Current;

        /// <summary>The signed amount the total just changed by (negative for a move or damage, positive for a pickup).</summary>
        public int Delta;

        /// <summary>What caused the change.</summary>
        public FoodChangeSource Source;
    }

    /// <summary>
    /// Emitted when a new level begins.
    /// </summary>
    [Event("A new level started.")]
    public struct LevelStarted : ISignal
    {
        /// <summary>The 1-based number of the level that just started.</summary>
        public int Level;
    }

    /// <summary>
    /// Emitted when the run ends because the player starved.
    /// </summary>
    [Event("The run ended; Level is how far the player got.")]
    public struct RunEnded : ISignal
    {
        /// <summary>The level the player had reached when the run ended.</summary>
        public int Level;
    }
}
