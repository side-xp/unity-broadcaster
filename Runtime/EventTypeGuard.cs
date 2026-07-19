using System.Diagnostics;

using Debug = UnityEngine.Debug;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// Asserts that the type argument of a bus verb is a concrete event type. The bus dispatches on exact concrete types only, so a
    /// call keyed on an interface or an abstract class can never match a concrete event: it compiles (an interface satisfies its own
    /// generic constraint) but silently does nothing. The classic trap is a variable declared as the marker interface
    /// (<c>ISignal signal = ...; bus.Emit(signal);</c>) infers the type argument as <c>ISignal</c>. Zero cost in release builds (the
    /// <see cref="Assert{T}"/> call is stripped by <see cref="ConditionalAttribute"/>).
    /// </summary>
    internal static class EventTypeGuard
    {

        /// <summary>
        /// Logs an error if <typeparamref name="T"/> is an interface or an abstract class. Compiled only into editor and development
        /// builds.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Assert<T>() where T : IEvent
        {
            if (Cache<T>.IsConcrete)
                return;

            Debug.LogError($"[Broadcaster] '{typeof(T).Name}' is an interface or an abstract type: the bus dispatches on exact concrete types only, so no event is ever keyed on it and this call can't match anything. This usually means the type argument was inferred from a variable declared as the interface or base type (make the concrete event type flow to this call instead).");
        }

        /// <summary>
        /// Caches the reflection check per closed type, so repeated asserts (the emit hot path) cost a static field read.
        /// </summary>
        private static class Cache<T>
        {
            public static readonly bool IsConcrete = !typeof(T).IsInterface && !typeof(T).IsAbstract;
        }

    }

}
