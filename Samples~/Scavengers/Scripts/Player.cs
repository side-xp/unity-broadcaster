#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using UnityEngine;
using UnityEngine.UI;
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

        [Header("UI")]

        public Text foodText;

        [Header("Audio")]

        public AudioClip moveSound1;
        public AudioClip moveSound2;
        public AudioClip gameOverSound;

        private Animator animator;
        private int food;

        protected override void Start()
        {
            animator = GetComponent<Animator>();

            food = GameManager.instance.playerFoodPoints;
            foodText.text = "Food: " + food;

            base.Start();
        }

        private void OnEnable()
        {
            moveAction.Enable();
        }

        private void OnDisable()
        {
            moveAction.Disable();
            // Store current amount of food on the GameManager so it can be re-loaded in next level
            GameManager.instance.playerFoodPoints = food;
        }

        private void Update()
        {
            // Cancel if it's not the player's turn
            if (!GameManager.instance.playersTurn)
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
            // Decrease food for each move
            food--;
            foodText.text = "Food: " + food;

            base.AttemptMove<T>(xDir, yDir);

            RaycastHit2D hit;
            // Make the player move, play feedback if successful
            if (Move(xDir, yDir, out hit))
                SoundManager.instance.RandomizeSfx(moveSound1, moveSound2);

            // Check for game over if the player lost its last food point this turn
            CheckIfGameOver();

            // End player turn
            GameManager.instance.playersTurn = false;
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
                food += collectible.points;
                foodText.text = "+" + collectible.points + " Food: " + food;

                SoundManager.instance.RandomizeSfx(collectible.pickupSound1, collectible.pickupSound2);

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
        /// Makes the player lose food.
        /// </summary>
        public void LoseFood(int loss)
        {
            animator.SetTrigger("hit");
            food -= loss;
            foodText.text = "-" + loss + " Food: " + food;
            CheckIfGameOver();
        }

        /// <summary>
        /// Checks if the player has remaining food points. If not, ends the game.
        /// </summary>
        private void CheckIfGameOver()
        {
            if (food <= 0)
            {
                SoundManager.instance.PlaySingle(gameOverSound);
                SoundManager.instance.musicSource.Stop();
                GameManager.instance.GameOver();
            }
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
