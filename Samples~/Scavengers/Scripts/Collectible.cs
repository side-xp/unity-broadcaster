#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using UnityEngine;

namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// Marks a tile the player can pick up to gain food points.
    /// </summary>
    public class Collectible : MonoBehaviour
    {
        [Tooltip("The amount of food granted to the player when picking up this collectible.")]
        public int points = 10;

        [Header("Audio")]

        public AudioClip pickupSound1;
        public AudioClip pickupSound2;
    }
}
#pragma warning restore IDE1006 // Naming Styles