using System;

using UnityEngine;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// Main entry point of the Broadcast system.<br/>
    /// </summary>
    /// <remarks>Internally, this class just shares a static instance of <see cref="EventBus"/>.</remarks>
    public static class Broadcaster
    {

        #region Fields

        /// <summary>
        /// Backing field for <see cref="Default"/>.
        /// </summary>
        private static EventBus s_default;

        #endregion


        #region Public API

        /// <inheritdoc cref="EventBus.Emit{T}(T)"/>
        public static void Emit<T>(T signal) where T : ISignal => Default.Emit(signal);

        /// <inheritdoc cref="EventBus.Subscribe{T}(object, Action{T})"/>
        public static SubscriptionHandle Subscribe<T>(object owner, Action<T> listener) where T : ISignal => Default.Subscribe(owner, listener);

        /// <inheritdoc cref="EventBus.Unsubscribe{T}(Action{T})"/>
        public static bool Unsubscribe<T>(Action<T> listener) where T : ISignal => Default.Unsubscribe(listener);

        /// <inheritdoc cref="EventBus.UnsubscribeAll(object)"/>
        public static int UnsubscribeAll(object owner) => Default.UnsubscribeAll(owner);

        /// <inheritdoc cref="EventBus.Clear"/>
        public static void Clear() => Default.Clear();

        /// <inheritdoc cref="EventBus.Clear{T}"/>
        public static void Clear<T>() where T : IEvent => Default.Clear<T>();

        #endregion


        #region Lifecycle

        /// <summary>
        /// Recreates <see cref="Default"/> when entering play mode so play sessions never start with state left over from a previous one
        /// (matters when domain reload is disabled).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefault()
        {
            s_default = new EventBus();
        }

        #endregion


        #region Internal API

        /// <summary>
        /// The shared default bus. Created lazily so the façade also works in edit mode, and recreated when entering play mode so a
        /// disabled domain reload can't leak state from a previous play session.
        /// </summary>
        internal static EventBus Default => s_default ??= new EventBus();

        #endregion

    }

}
