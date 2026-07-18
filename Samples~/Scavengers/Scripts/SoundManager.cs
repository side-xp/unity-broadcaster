#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using UnityEngine;

namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// The audio system of the game. It listens for gameplay signals to play the appropriate sounds.
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        [Tooltip("The AudioSource used to play SFX.")]
        public AudioSource efxSource;
        [Tooltip("The AudioSource used to play the music in loop.")]
        public AudioSource musicSource;

        [Space]

        [Tooltip("The lowest a sound effect will be randomly pitched.")]
        public float lowPitchRange = .95f;
        [Tooltip("The highest a sound effect will be randomly pitched.")]
        public float highPitchRange = 1.05f;

        [Header("Clips")]

        public AudioClip moveSound1;
        public AudioClip moveSound2;
        public AudioClip eatSound1;
        public AudioClip eatSound2;
        public AudioClip drinkSound1;
        public AudioClip drinkSound2;
        public AudioClip chopSound1;
        public AudioClip chopSound2;
        public AudioClip enemyAttackSound1;
        public AudioClip enemyAttackSound2;
        public AudioClip gameOverSound;

        /// <summary>Singleton instance. Read by <see cref="Loader"/> to avoid spawning a duplicate on scene reload.</summary>
        public static SoundManager instance = null;

        private void Awake()
        {
            // Persist a single instance across level reloads
            if (instance == null)
            {
                instance = this;
            }
            else if (instance != this)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            // A duplicate awaiting destruction must not register itself
            if (instance != this)
                return;

            Broadcaster.Subscribe<PlayerMoved>(this, OnPlayerMoved);
            Broadcaster.Subscribe<PlayerAte>(this, OnPlayerAte);
            Broadcaster.Subscribe<PlayerDrank>(this, OnPlayerDrank);
            Broadcaster.Subscribe<WallChopped>(this, OnWallChopped);
            Broadcaster.Subscribe<EnemyAttacked>(this, OnEnemyAttacked);
            Broadcaster.Subscribe<PlayerDied>(this, OnPlayerDied);
        }

        private void OnDisable()
        {
            Broadcaster.UnsubscribeAll(this);
        }

        private void OnPlayerMoved(PlayerMoved signal) => RandomizeSfx(moveSound1, moveSound2);
        private void OnPlayerAte(PlayerAte signal) => RandomizeSfx(eatSound1, eatSound2);
        private void OnPlayerDrank(PlayerDrank signal) => RandomizeSfx(drinkSound1, drinkSound2);
        private void OnWallChopped(WallChopped signal) => RandomizeSfx(chopSound1, chopSound2);
        private void OnEnemyAttacked(EnemyAttacked signal) => RandomizeSfx(enemyAttackSound1, enemyAttackSound2);

        private void OnPlayerDied(PlayerDied signal)
        {
            PlaySingle(gameOverSound);
            musicSource.Stop();
        }

        /// <summary>
        /// Plays a given SFX.
        /// </summary>
        private void PlaySingle(AudioClip clip)
        {
            efxSource.clip = clip;
            efxSource.Play();
        }

        /// <summary>
        /// Picks an SFX from the given clips at random and plays it.
        /// </summary>
        private void RandomizeSfx(params AudioClip[] clips)
        {
            int randomIndex = Random.Range(0, clips.Length);
            float randomPitch = Random.Range(lowPitchRange, highPitchRange);
            efxSource.pitch = randomPitch;
            efxSource.clip = clips[randomIndex];
            efxSource.Play();
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
