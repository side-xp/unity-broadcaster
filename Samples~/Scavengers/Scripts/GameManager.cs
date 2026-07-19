#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using System;

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

        [Tooltip("The delay (in seconds) between each player turn.")]
        public float turnDelay = 0.1f;

        /// <summary>Flag enabled if it's currently the player's turn. Owned here and exposed as provided <see cref="PlayerTurn"/>
        /// state.</summary>
        private bool playersTurn = true;

        private BoardManager boardScript;

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

            boardScript = GetComponent<BoardManager>();
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Become the single authority that ends the run. Anything can end it by ordering EndRun, without a reference here.
            Broadcaster.Obey<EndRun>(this, _ =>
            {
                Broadcaster.Emit(new RunEnded { level = level });
                enabled = false;
            });

            // Own the food total and the turn flag as state: accept the orders that change them, and provide their current value.
            // AdjustFood keeps a named handler because it's the one with real logic; the rest are inline.
            Broadcaster.Obey<AdjustFood>(this, OnAdjustFood);
            Broadcaster.Obey<EndPlayerTurn>(this, _ => SetPlayersTurn(false));
            Broadcaster.Provide<FoodChanged>(this, () => new FoodChanged { current = playerFoodPoints, source = FoodChangeSource.Move });
            Broadcaster.Provide<PlayerTurn>(this, () => new PlayerTurn { active = playersTurn });
        }

        private void Update()
        {
            // Cancel if it's the player's turn, if enemies are already moving, or if the board is still being set up
            if (playersTurn || enemiesMoving || doingSetup)
                return;

            RunEnemyTurn();
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
        private async void InitGame()
        {
            // Enemies stay put while the intro plays; cleared once the intro cue actually finishes (below)
            doingSetup = true;

            // Establish this level's starting state
            SetPlayersTurn(true);
            Broadcaster.Emit(new FoodChanged { current = playerFoodPoints, source = FoodChangeSource.Move });

            // Announce the new level; the UI shows the level card
            Broadcaster.Emit(new LevelStarted { level = level });

            // Reset board
            boardScript.SetupScene(level);

            // Wait for the UI's intro to actually finish, rather than run a parallel stopwatch of our own
            try
            {
                await Broadcaster.Cue(new LevelIntro(), destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Play mode exited, or this manager was destroyed during the intro
                return;
            }

            doingSetup = false;
        }

        /// <summary>
        /// Handles the <see cref="AdjustFood"/> command: applies the change, announces it, and ends the run on starvation.
        /// </summary>
        private void OnAdjustFood(AdjustFood command)
        {
            playerFoodPoints += command.delta;
            Broadcaster.Emit(new FoodChanged { current = playerFoodPoints, delta = command.delta, source = command.source });

            if (playerFoodPoints <= 0)
            {
                Broadcaster.Emit(new PlayerDied());
                Broadcaster.Order(new EndRun());
            }
        }

        /// <summary>
        /// Sets the turn flag and announces the new value, so subscribers are pushed the change instead of polling.
        /// </summary>
        private void SetPlayersTurn(bool active)
        {
            playersTurn = active;
            Broadcaster.Emit(new PlayerTurn { active = active });
        }

        /// <summary>
        /// Runs the enemies' turn: sends the <see cref="EnemyTurn"/> cue and waits for every enemy to finish (when-all), then hands the
        /// turn back to the player. It waits for the performers' real completion, not a guessed per-enemy duration.
        /// </summary>
        private async void RunEnemyTurn()
        {
            // Set synchronously (before the first await) so Update doesn't start a second enemy turn
            enemiesMoving = true;

            try
            {
                // A short beat before the enemies act
                await Awaitable.WaitForSecondsAsync(turnDelay, destroyCancellationToken);
                // Wait for every enemy performer to finish its move or attack
                await Broadcaster.Cue(new EnemyTurn(), destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Play mode exited, or this manager was destroyed mid-turn
                return;
            }

            SetPlayersTurn(true);
            enemiesMoving = false;
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
