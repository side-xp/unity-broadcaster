#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using System.Collections;
using UnityEngine;

namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// Represents an object that can move on the board.
    /// </summary>
    public abstract class MovingObject : MonoBehaviour
    {
        [Tooltip("The time (in seconds) for an object to move.")]
        public float moveTime = 0.1f;

        private BoxCollider2D boxCollider;
        private Rigidbody2D rb2D;

        protected virtual void Start()
        {
            boxCollider = GetComponent<BoxCollider2D>();
            rb2D = GetComponent<Rigidbody2D>();
        }

        /// <summary>
        /// Checks whether this entity can move in a given direction. Outputs the destination tile and whatever blocks the move (if any),
        /// without starting any movement — so a caller can decide to slide there and wait for it, or react to the blocker.
        /// </summary>
        /// <param name="end">The destination tile if the move is possible.</param>
        /// <param name="hit">The object that blocks the movement, if any.</param>
        protected bool CanMove(int xDir, int yDir, out Vector2 end, out RaycastHit2D hit)
        {
            Vector2 start = transform.position;
            end = start + new Vector2(xDir, yDir);

            // Only solid tiles (walls, characters) can block a move; triggers (collectibles, the exit) are ignored.
            ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
            RaycastHit2D[] hits = new RaycastHit2D[1];

            // Temporarily disable the collider to prevent the Linecast from hitting this object
            boxCollider.enabled = false;
            // Detect nearby tile
            int count = Physics2D.Linecast(start, end, filter, hits);
            boxCollider.enabled = true;

            hit = count > 0 ? hits[0] : default;
            return hit.transform == null;
        }

        /// <summary>
        /// Makes this entity move in a given direction, or returns false if the movement is not possible.
        /// </summary>
        /// <param name="hit">The object that blocked the movement.</param>
        protected bool Move(int xDir, int yDir, out RaycastHit2D hit)
        {
            if (!CanMove(xDir, yDir, out Vector2 end, out hit))
                return false;

            StartCoroutine(SmoothMovement(end));
            return true;
        }

        /// <summary>
        /// A coroutine for animating the unit's movement.
        /// </summary>
        protected IEnumerator SmoothMovement(Vector3 end)
        {
            // Calculate the distance to the target
            float sqrRemainingDistance = (transform.position - end).sqrMagnitude;

            // Make the unit move toward the target until it reaches it
            while (sqrRemainingDistance > float.Epsilon)
            {
                Vector3 newPosition = Vector3.MoveTowards(rb2D.position, end, Time.deltaTime / moveTime);
                rb2D.MovePosition(newPosition);
                sqrRemainingDistance = (transform.position - end).sqrMagnitude;
                yield return null;
            }
        }

        /// <summary>
        /// Tries to make this unit move in a given direction, interacting with the unit or object on the target tile, if any.
        /// </summary>
        /// <typeparam name="T">The type of component that's expected for this unit to interact with if blocked (Player for Enemies, Wall
        /// for Player).</typeparam>
        protected virtual void AttemptMove<T>(int xDir, int yDir)
            where T : Component
        {
            RaycastHit2D hit;
            bool canMove = Move(xDir, yDir, out hit);

            // Stop if nothing blocked the movement
            if (hit.transform == null)
                return;

            T hitComponent = hit.transform.GetComponent<T>();
            // If canMove was false and the object on target tile is blocking, notify
            if (!canMove && hitComponent != null)
                OnCantMove(hitComponent);
        }

        /// <summary>
        /// Called when the movement is blocked this turn. The default does nothing; override it to react to the blocker.
        /// </summary>
        /// <param name="component">The unit or object that blocked the movement.</param>
        protected virtual void OnCantMove<T>(T component)
            where T : Component
        { }
    }
}
#pragma warning restore IDE1006 // Naming Styles
