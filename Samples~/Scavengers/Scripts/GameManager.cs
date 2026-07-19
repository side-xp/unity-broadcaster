#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// Represents a game instance, and handles the game flow.
    /// </summary>
    public class GameManager : MonoBehaviour
    {

        /// <summary>Singleton instance of this manager.</summary>
        public static GameManager instance = null;

        [Header("Gameplay settings")]

        [Tooltip("The initial amount of food on the player.")]
        public int playerFoodPoints = 100;

        [Header("Sequence")]

        [Tooltip("The time (in seconds) before starting a level.")]
        public float levelStartDelay = 2f;

        [Tooltip("The delay (in seconds) between each player turn.")]
        public float turnDelay = 0.1f;

        /// <summary>Flag enabled if it's currently the player's turn.</summary>
        [HideInInspector] public bool playersTurn = true;

        private BoardManager boardScript;
        private List<Enemy> enemies;

        /// <summary>Current level number.</summary>
        private int level = 0;
        /// <summary>Flag enabled if enemies are currently moving.</summary>
        private bool enemiesMoving;
        /// <summary>Flag enabled during board setup.</summary>
        private bool doingSetup = true;

        private void Awake()
        {
            // Set singleton instance if applicable
            if (instance == null)
            {
                instance = this;
            }
            // Destroy this object if it's not the singleton instance
            else if (instance != this)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);

            enemies = new List<Enemy>();
            boardScript = GetComponent<BoardManager>();
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Become the single authority that ends the run. Anything can end it by ordering EndRun, without a reference here.
            Broadcaster.Obey<EndRun>(this, OnEndRun);
        }

        private void Update()
        {
            // Cancel if it's the player's turn, if enemies are already moving, or if the board is still being set up
            if (playersTurn || enemiesMoving || doingSetup)
                return;

            StartCoroutine(MoveEnemies());
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Broadcaster.UnregisterAll(this);
        }

        /// <summary>
        /// Called when a scene is loaded.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            level++;
            InitGame();
        }

        /// <summary>
        /// Initializes the game on each level.
        /// </summary>
        private void InitGame()
        {
            // Mark setup, cleared in EndSetup()
            doingSetup = true;

            // Announce the new level; the UI owns the level card and how long it stays up
            Broadcaster.Emit(new LevelStarted { Level = level });
            Invoke(nameof(EndSetup), levelStartDelay);

            // Reset board
            enemies.Clear();
            boardScript.SetupScene(level);
        }

        /// <summary>
        /// Clears the setup gate once <see cref="levelStartDelay"/> has elapsed, so enemies may start moving.
        /// </summary>
        private void EndSetup()
        {
            doingSetup = false;
        }

        /// <summary>
        /// Adds a given enemy instance to the list.
        /// </summary>
        public void AddEnemyToList(Enemy script)
        {
            enemies.Add(script);
        }


        /// <summary>
        /// Handles the <see cref="EndRun"/> command: announces the run's end and disables this game manager.
        /// </summary>
        private void OnEndRun(EndRun command)
        {
            Broadcaster.Emit(new RunEnded { Level = level });
            enabled = false;
        }

        /// <summary>
        /// Coroutine to move enemies in sequence.
        /// </summary>
        private IEnumerator MoveEnemies()
        {
            // Mark sequence started
            enemiesMoving = true;

            yield return new WaitForSeconds(turnDelay);

            // Apply simple delay if there's no enemy to move
            if (enemies.Count == 0)
                yield return new WaitForSeconds(turnDelay);

            // For each enemy on the board, make it move and wait a short delay
            for (int i = 0; i < enemies.Count; i++)
            {
                enemies[i].MoveEnemy();
                yield return new WaitForSeconds(enemies[i].moveTime);
            }

            // Clear sequence
            playersTurn = true;
            enemiesMoving = false;
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
