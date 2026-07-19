#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// Represents the player.
    /// </summary>
    public class Player : MovingObject
    {
        [Header("Sequence settings")]

        [Tooltip("The delay (in seconds) to restart the level.")]
        public float restartLevelDelay = 1f;

        [Header("Gameplay settings")]

        [Tooltip("Defines the amount of damage the player inflicts on a wall when chopping it.")]
        public int wallDamage = 1;

        [Tooltip("The controls for moving the player.")]
        public InputAction moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");

        private Animator animator;
        /// <summary>Cached turn state, pushed by the GameManager instead of polled every frame.</summary>
        private bool myTurn;

        protected override void Start()
        {
            animator = GetComponent<Animator>();
            base.Start();
        }

        private void OnEnable()
        {
            moveAction.Enable();
            // Become the single authority on damaging the player: anything can hurt it by ordering DamagePlayer
            Broadcaster.Obey<DamagePlayer>(this, OnDamagePlayer);
            // Expose the player's position as state anyone can read (the enemies use it to path toward the player)
            Broadcaster.Provide<PlayerPosition>(this, () => new PlayerPosition { Position = transform.position });
            // Follow the turn flag by push instead of polling every frame; init pulls its current value right now
            Broadcaster.Subscribe<PlayerTurn>(this, OnPlayerTurn, init: true);
        }

        private void OnDisable()
        {
            moveAction.Disable();
            Broadcaster.UnregisterAll(this);
        }

        private void Update()
        {
            // Cancel if it's not the player's turn
            if (!myTurn)
                return;

            Vector2 direction = moveAction.ReadValue<Vector2>();

            // .2f is the deadzone offset
            if (direction.sqrMagnitude < .2f)
                return;

            int horizontal = 0;
            int vertical = 0;

            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
                horizontal = direction.x > 0 ? 1 : -1;
            else
                vertical = direction.y > 0 ? 1 : -1;

            AttemptMove<Wall>(horizontal, vertical);
        }

        /// <inheritdoc cref="MovingObject.AttemptMove{T}(int, int)"/>
        protected override void AttemptMove<T>(int xDir, int yDir)
        {
            // Spend a food point on the move; the food owner applies it and detects starvation
            Broadcaster.Order(new AdjustFood { Delta = -1, Source = FoodChangeSource.Move });

            base.AttemptMove<T>(xDir, yDir);

            RaycastHit2D hit;
            // Make the player move, and emit a signal on success so the audio system can react
            if (Move(xDir, yDir, out hit))
                Broadcaster.Emit(new PlayerMoved());

            // Hand the turn over to the enemies
            Broadcaster.Order(new EndPlayerTurn());
        }

        /// <inheritdoc cref="MovingObject.OnCantMove{T}(T)"/>
        protected override void OnCantMove<T>(T component)
        {
            Wall hitWall = component as Wall;
            hitWall.DamageWall(wallDamage);
            animator.SetTrigger("chop");
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // If the hit object is the Exit, trigger next level
            if (other.GetComponent<Exit>() != null)
            {
                Invoke("Restart", restartLevelDelay);
                enabled = false;
            }
            // Else, if the hit object is a collectible, get its defined amount of food
            else if (other.TryGetComponent(out Collectible collectible))
            {
                // The food owner applies the gain; the audio signals below stay separate feedback
                Broadcaster.Order(new AdjustFood { Delta = collectible.points, Source = FoodChangeSource.Pickup });

                // Emit what happened; the audio system decides how each kind of pickup sounds
                if (collectible.kind == CollectibleKind.Food)
                    Broadcaster.Emit(new PlayerAte());
                else
                    Broadcaster.Emit(new PlayerDrank());

                // Disable the collectible once consumed
                other.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Restarts the scene.
        /// </summary>
        private void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>
        /// Handles the <see cref="DamagePlayer"/> command: plays the hit reaction and orders the food loss.
        /// </summary>
        private void OnDamagePlayer(DamagePlayer command)
        {
            animator.SetTrigger("hit");
            // The food owner applies the loss and decides whether it was fatal
            Broadcaster.Order(new AdjustFood { Delta = -command.Amount, Source = FoodChangeSource.Damage });
        }

        /// <summary>
        /// Caches the turn state pushed by the <see cref="GameManager"/>.
        /// </summary>
        private void OnPlayerTurn(PlayerTurn signal)
        {
            myTurn = signal.Active;
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
