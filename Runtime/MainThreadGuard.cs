using System.Diagnostics;
using System.Threading;
using UnityEngine;

using Debug = UnityEngine.Debug;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// Captures the Unity main thread and asserts bus access happens on it. Zero cost in release builds (the <see cref="Assert"/> call is
    /// stripped by <see cref="ConditionalAttribute"/>).
    /// </summary>
    internal static class MainThreadGuard
    {

        /// <summary>
        /// Managed id of the Unity main thread, or 0 before it has been captured.
        /// </summary>
        private static int s_mainThreadId;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void CaptureInEditor() => Capture();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Capture()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// Logs an error if the caller is not on the Unity main thread. Compiled only into editor and development builds.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Assert()
        {
            if (s_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId != s_mainThreadId)
                Debug.LogError("[Broadcaster] EventBus was accessed off the Unity main thread. The bus is main-thread only.");
        }

    }

}
