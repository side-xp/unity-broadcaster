#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using System;
using System.Collections;

using UnityEngine;
using UnityEngine.UI;

namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// The game's HUD. It listens for gameplay signals and owns how the game is presented: the food counter, the level
    /// card and the game-over message.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("References")]

        [Tooltip("The text displaying the player's current food total.")]
        public Text foodText;
        [Tooltip("The full-screen overlay shown during setup and on game over.")]
        public GameObject levelImage;
        [Tooltip("The text shown on the overlay (level card, then game-over message).")]
        public Text levelText;

        [Header("Sequence")]

        [Tooltip("How long (in seconds) the level card stays up before hiding.")]
        public float levelStartDelay = 2f;

        private void OnEnable()
        {
            // init: true pulls the current food from its provider right away, so the counter shows a value on level start
            Broadcaster.Subscribe<FoodChanged>(this, OnFoodChanged, init: true);
            Broadcaster.Subscribe<LevelStarted>(this, OnLevelStarted);
            Broadcaster.Subscribe<RunEnded>(this, OnRunEnded);
            // Perform the level intro: the game waits on this, so the UI owns how long the card stays up
            Broadcaster.Perform<LevelIntro>(this, PerformLevelIntro);
        }

        private void OnDisable()
        {
            Broadcaster.UnregisterAll(this);
        }

        private void OnFoodChanged(FoodChanged signal)
        {
            // A move is the routine turn cost, so keep it quiet; pickups and damage get an explicit +/- badge.
            if (signal.source == FoodChangeSource.Move)
                foodText.text = "Food: " + signal.current;
            else
                foodText.text = (signal.delta >= 0 ? "+" : "") + signal.delta + " Food: " + signal.current;
        }

        private void OnLevelStarted(LevelStarted signal)
        {
            levelText.text = "Day " + signal.level;
            levelImage.SetActive(true);
        }

        private void OnRunEnded(RunEnded signal)
        {
            // The run is over: raise the overlay and keep it up (no intro performer is running to hide it).
            levelText.text = "After " + signal.level + " days, you starved.";
            levelImage.SetActive(true);
        }

        /// <summary>
        /// Performs the <see cref="LevelIntro"/> cue: holds the level card up for its duration, then hides it. Setup waits for this
        /// to finish, so this component alone decides how long the intro lasts.
        /// </summary>
        private void PerformLevelIntro(LevelIntro cue, Action done)
        {
            StartCoroutine(LevelIntroRoutine(done));
        }

        private IEnumerator LevelIntroRoutine(Action done)
        {
            yield return new WaitForSeconds(levelStartDelay);
            levelImage.SetActive(false);
            done();
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
