#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using UnityEngine;

namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// Handles audio feedback.
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        /// <summary>The singleton instance of this manager.</summary>
        public static SoundManager instance = null;

        [Tooltip("The AudioSource used to play SFX.")]
        public AudioSource efxSource;
        [Tooltip("The AudioSource used to play the music in loop.")]
        public AudioSource musicSource;

        [Space]

        [Tooltip("The lowest a sound effect will be randomly pitched.")]
        public float lowPitchRange = .95f;
        [Tooltip("The highest a sound effect will be randomly pitched.")]
        public float highPitchRange = 1.05f;

        void Awake()
        {
            // Singleton guard
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

        /// <summary>
        /// Plays a given SFX.
        /// </summary>
        public void PlaySingle(AudioClip clip)
        {
            efxSource.clip = clip;
            efxSource.Play();
        }

        /// <summary>
        /// Picks an SFX from a given array at random and plays it.
        /// </summary>
        public void RandomizeSfx(params AudioClip[] clips)
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
