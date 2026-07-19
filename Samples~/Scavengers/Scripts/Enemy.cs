#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using System;
using System.Collections;

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

        [Tooltip("How long (in seconds) the attack animation plays. The enemy turn waits for it, so a longer attack visibly holds the turn.")]
        public float attackTime = 0.4f;

        private Animator animator;
        /// <summary>Defines if the enemy should skip this turn.</summary>
        private bool skipMove;

        /// <inheritdoc cref="MovingObject.Start"/>
        protected override void Start()
        {
            animator = GetComponent<Animator>();
            base.Start();
        }

        private void OnEnable()
        {
            // Join the enemy-turn cue. The set of performers IS the roster of live enemies, there's no separate list to maintain.
            Broadcaster.Perform<EnemyTurn>(this, PerformTurn);
        }

        private void OnDisable()
        {
            Broadcaster.UnregisterAll(this);
        }

        /// <summary>
        /// Performs this enemy's turn as a cue performer. It reports <paramref name="done"/> only once the move or attack has actually
        /// finished, so the turn manager's when-all waits for the real animation instead of a guessed duration.
        /// </summary>
        private void PerformTurn(EnemyTurn cue, Action done)
        {
            StartCoroutine(TurnRoutine(done));
        }

        private IEnumerator TurnRoutine(Action done)
        {
            // This enemy only acts every other turn
            if (skipMove)
            {
                skipMove = false;
                done();
                yield break;
            }
            skipMove = true;

            // Ask where the player is instead of holding a reference to it
            if (!Broadcaster.TryGetCurrent(out PlayerPosition player))
            {
                done();
                yield break;
            }

            int xDir = 0;
            int yDir = 0;

            // If this entity and the player are on the same column, move vertically; otherwise move horizontally
            if (Mathf.Abs(player.position.x - transform.position.x) < float.Epsilon)
                yDir = player.position.y > transform.position.y ? 1 : -1;
            else
                xDir = player.position.x > transform.position.x ? 1 : -1;

            // Move toward the player, waiting for the actual slide to finish; or attack if the player is in the way
            if (CanMove(xDir, yDir, out Vector2 end, out RaycastHit2D hit))
            {
                yield return StartCoroutine(SmoothMovement(end));
            }
            else if (hit.transform != null && hit.transform.GetComponent<Player>() != null)
            {
                // Order the damage instead of reaching into the player's API (the enemy doesn't even need to know Player exists)
                Broadcaster.Order(new DamagePlayer { amount = playerDamage });
                animator.SetTrigger("attack");
                Broadcaster.Emit(new EnemyAttacked());
                yield return new WaitForSeconds(attackTime);
            }

            // The action has genuinely finished now
            done();
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
