#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using UnityEngine;

namespace SideXP.Broadcaster.Scavengers.Original
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

        [Header("Audio")]

        public AudioClip chopSound1;
        public AudioClip chopSound2;

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
            SoundManager.instance.RandomizeSfx(chopSound1, chopSound2);

            spriteRenderer.sprite = dmgSprite;
            hp -= loss;

            if (hp <= 0)
                gameObject.SetActive(false);
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
