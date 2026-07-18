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
}