#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using UnityEngine;

namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// Represents a destructible wall.
    /// </summary>
    public class Wall : MonoBehaviour
    {
        [Header("Gameplay settings")]

        public int hp = 3;

        [Header("Visuals")]

        public Sprite dmgSprite;

        private SpriteRenderer spriteRenderer;

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>
        /// Inflicts damage on this wall, and disables it if it has no remaining <see cref="hp"/>.
        /// </summary>
        public void DamageWall(int loss)
        {
            Broadcaster.Emit(new WallChopped());

            spriteRenderer.sprite = dmgSprite;
            hp -= loss;

            if (hp <= 0)
                gameObject.SetActive(false);
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
