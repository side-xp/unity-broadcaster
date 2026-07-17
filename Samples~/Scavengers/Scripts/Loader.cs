#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using UnityEngine;

namespace SideXP.Broadcaster.Scavengers
{
    /// <summary>
    /// Spawns the essential objects in the scene.
    /// </summary>
    public class Loader : MonoBehaviour
    {
        public GameObject gameManager;
        public GameObject soundManager;

        void Awake()
        {
            if (GameManager.instance == null)
                Instantiate(gameManager);

            if (SoundManager.instance == null)
                Instantiate(soundManager);
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
