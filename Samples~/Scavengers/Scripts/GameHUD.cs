#pragma warning disable IDE1006 // Naming Styles, disabled for demo
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
        }

        private void OnDisable()
        {
            Broadcaster.UnregisterAll(this);
        }

        private void OnFoodChanged(FoodChanged signal)
        {
            // A move is the routine turn cost, so keep it quiet; pickups and damage get an explicit +/- badge.
            if (signal.Source == FoodChangeSource.Move)
                foodText.text = "Food: " + signal.Current;
            else
                foodText.text = (signal.Delta >= 0 ? "+" : "") + signal.Delta + " Food: " + signal.Current;
        }

        private void OnLevelStarted(LevelStarted signal)
        {
            levelText.text = "Day " + signal.Level;
            levelImage.SetActive(true);
            Invoke(nameof(HideLevelImage), levelStartDelay);
        }

        private void OnRunEnded(RunEnded signal)
        {
            // The run is over: keep the overlay up, so cancel any pending hide from the level card.
            CancelInvoke(nameof(HideLevelImage));
            levelText.text = "After " + signal.Level + " days, you starved.";
            levelImage.SetActive(true);
        }

        private void HideLevelImage()
        {
            levelImage.SetActive(false);
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
