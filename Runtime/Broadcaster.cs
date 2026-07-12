using System;
using System.Threading;

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


        #region Signals

        /// <inheritdoc cref="EventBus.Emit{T}(T)"/>
        public static void Emit<T>(T signal) where T : ISignal
        {
            Default.Emit(signal);
        }

        /// <inheritdoc cref="EventBus.Subscribe{T}(object, Action{T}, bool)"/>
        public static SubscriptionHandle Subscribe<T>(object owner, Action<T> listener, bool init = false) where T : ISignal
        {
            return Default.Subscribe(owner, listener, init);
        }

        /// <inheritdoc cref="EventBus.Unsubscribe{T}(Action{T})"/>
        public static bool Unsubscribe<T>(Action<T> listener) where T : ISignal
        {
            return Default.Unsubscribe(listener);
        }

        /// <inheritdoc cref="EventBus.Provide{T}(object, Func{T}, bool)"/>
        public static SubscriptionHandle Provide<T>(object owner, Func<T> provider, bool replace = false) where T : ISignal
        {
            return Default.Provide(owner, provider, replace);
        }

        /// <inheritdoc cref="EventBus.TryGetCurrent{T}(out T)"/>
        public static bool TryGetCurrent<T>(out T current) where T : ISignal
        {
            return Default.TryGetCurrent(out current);
        }

        #endregion


        #region Cues

        /// <inheritdoc cref="EventBus.Perform{T}(object, Action{T})"/>
        public static SubscriptionHandle Perform<T>(object owner, Action<T> performer) where T : ICue
        {
            return Default.Perform(owner, performer);
        }

        /// <inheritdoc cref="EventBus.Perform{T}(object, Func{T, Awaitable})"/>
        public static SubscriptionHandle Perform<T>(object owner, Func<T, Awaitable> performer) where T : ICue
        {
            return Default.Perform(owner, performer);
        }

        /// <inheritdoc cref="EventBus.Perform{T}(object, Action{T, Action})"/>
        public static SubscriptionHandle Perform<T>(object owner, Action<T, Action> performer) where T : ICue
        {
            return Default.Perform(owner, performer);
        }

        /// <inheritdoc cref="EventBus.Cue{T}(T, CancellationToken)"/>
        public static Awaitable Cue<T>(T cue, CancellationToken cancellation = default) where T : ICue
        {
            return Default.Cue(cue, cancellation);
        }

        #endregion


        #region Commands

        /// <inheritdoc cref="EventBus.Obey{T}(object, Action{T}, bool)"/>
        public static SubscriptionHandle Obey<T>(object owner, Action<T> handler, bool replace = false) where T : ICommand
        {
            return Default.Obey(owner, handler, replace);
        }

        /// <inheritdoc cref="EventBus.Obey{T}(object, Func{T, Awaitable}, bool)"/>
        public static SubscriptionHandle Obey<T>(object owner, Func<T, Awaitable> handler, bool replace = false) where T : ICommand
        {
            return Default.Obey(owner, handler, replace);
        }

        /// <inheritdoc cref="EventBus.Obey{T, TResult}(object, Func{T, TResult}, bool)"/>
        public static SubscriptionHandle Obey<T, TResult>(object owner, Func<T, TResult> handler, bool replace = false) where T : ICommand<TResult>
        {
            return Default.Obey(owner, handler, replace);
        }

        /// <inheritdoc cref="EventBus.Obey{T, TResult}(object, Func{T, Awaitable{TResult}}, bool)"/>
        public static SubscriptionHandle Obey<T, TResult>(object owner, Func<T, Awaitable<TResult>> handler, bool replace = false) where T : ICommand<TResult>
        {
            return Default.Obey(owner, handler, replace);
        }

        /// <inheritdoc cref="EventBus.Order{T}(T)"/>
        public static bool Order<T>(T command) where T : ICommand
        {
            return Default.Order(command);
        }

        /// <inheritdoc cref="EventBus.Order{TResult}(ICommand{TResult})"/>
        public static TResult Order<TResult>(ICommand<TResult> command)
        {
            return Default.Order(command);
        }

        /// <inheritdoc cref="EventBus.OrderAsync{T}(T, CancellationToken)"/>
        public static Awaitable OrderAsync<T>(T command, CancellationToken cancellation = default) where T : ICommand
        {
            return Default.OrderAsync(command, cancellation);
        }

        /// <inheritdoc cref="EventBus.OrderAsync{TResult}(ICommand{TResult}, CancellationToken)"/>
        public static Awaitable<TResult> OrderAsync<TResult>(ICommand<TResult> command, CancellationToken cancellation = default)
        {
            return Default.OrderAsync(command, cancellation);
        }

        #endregion


        #region Requests

        /// <inheritdoc cref="EventBus.Answer{T, TResult}(object, Func{T, TResult}, bool)"/>
        public static SubscriptionHandle Answer<T, TResult>(object owner, Func<T, TResult> handler, bool replace = false) where T : IRequest<TResult>
        {
            return Default.Answer(owner, handler, replace);
        }

        /// <inheritdoc cref="EventBus.Answer{T, TResult}(object, Func{T, Awaitable{TResult}}, bool)"/>
        public static SubscriptionHandle Answer<T, TResult>(object owner, Func<T, Awaitable<TResult>> handler, bool replace = false) where T : IRequest<TResult>
        {
            return Default.Answer(owner, handler, replace);
        }

        /// <inheritdoc cref="EventBus.Ask{TResult}(IRequest{TResult})"/>
        public static TResult Ask<TResult>(IRequest<TResult> request)
        {
            return Default.Ask(request);
        }

        /// <inheritdoc cref="EventBus.TryAsk{TResult}(IRequest{TResult}, out TResult)"/>
        public static bool TryAsk<TResult>(IRequest<TResult> request, out TResult result)
        {
            return Default.TryAsk(request, out result);
        }

        /// <inheritdoc cref="EventBus.AskAsync{TResult}(IRequest{TResult}, CancellationToken)"/>
        public static Awaitable<TResult> AskAsync<TResult>(IRequest<TResult> request, CancellationToken cancellation = default)
        {
            return Default.AskAsync(request, cancellation);
        }

        #endregion


        #region General

        /// <inheritdoc cref="EventBus.UnsubscribeAll(object)"/>
        public static int UnsubscribeAll(object owner)
        {
            return Default.UnsubscribeAll(owner);
        }

        /// <inheritdoc cref="EventBus.Clear"/>
        public static void Clear()
        {
            Default.Clear();
        }

        /// <inheritdoc cref="EventBus.Clear{T}"/>
        public static void Clear<T>() where T : IEvent
        {
            Default.Clear<T>();
        }

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
