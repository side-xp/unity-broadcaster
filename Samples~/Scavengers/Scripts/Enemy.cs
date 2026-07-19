#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using UnityEngine;

namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// Represents an enemy.
    /// </summary>
    public class Enemy : MovingObject
    {
        [Tooltip("The amount of damage dealt to the player when attacking. This is applied to the player's food count.")]
        public int playerDamage;

        private Animator animator;
        private Transform target;
        /// <summary>Defines if the enemy should skip this turn.</summary>
        private bool skipMove;

        /// <inheritdoc cref="MovingObject.Start"/>
        protected override void Start()
        {
            // Register itself to the game instance
            GameManager.instance.AddEnemyToList(this);

            animator = GetComponent<Animator>();
            target = GameObject.FindGameObjectWithTag("Player").transform;

            base.Start();
        }

        /// <inheritdoc cref="MovingObject.AttemptMove{T}(int, int)"/>
        protected override void AttemptMove<T>(int xDir, int yDir)
        {
            // Skip this turn if applicable
            if (skipMove)
            {
                skipMove = false;
                return;
            }

            base.AttemptMove<T>(xDir, yDir);
            // Skip the next turn
            skipMove = true;
        }

        /// <summary>
        /// Makes this enemy move towards the player.
        /// </summary>
        public void MoveEnemy()
        {
            int xDir = 0;
            int yDir = 0;

            // If this entity and the player are on the same column
            if (Mathf.Abs(target.position.x - transform.position.x) < float.Epsilon)
                // Make this entity move vertically
                yDir = target.position.y > transform.position.y ? 1 : -1;
            // Otherwise
            else
                // Make this entity move horizontally
                xDir = target.position.x > transform.position.x ? 1 : -1;

            // Try to move in the computed direction
            AttemptMove<Player>(xDir, yDir);
        }

        /// <inheritdoc cref="MovingObject.OnCantMove{T}(T)"/>
        protected override void OnCantMove<T>(T component)
        {
            // Order the damage instead of reaching into the player's API (the enemy doesn't even need to know Player exists)
            Broadcaster.Order(new DamagePlayer { Amount = playerDamage });
            // Play feedback
            animator.SetTrigger("attack");
            Broadcaster.Emit(new EnemyAttacked());
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
