#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using UnityEngine;

namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// The kind of a <see cref="Collectible"/>, which decides the pickup signal it emits.
    /// </summary>
    public enum CollectibleKind
    {
        Food,
        Soda,
    }

    /// <summary>
    /// Marks a tile the player can pick up to gain food points.
    /// </summary>
    public class Collectible : MonoBehaviour
    {
        [Tooltip("The amount of food granted to the player when picking up this collectible.")]
        public int points = 10;

        [Tooltip("Which pickup feedback is emitted when this collectible is consumed.")]
        public CollectibleKind kind = CollectibleKind.Food;
    }
}
#pragma warning restore IDE1006 // Naming Styles