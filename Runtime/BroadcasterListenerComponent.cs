using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;
using UnityEngine.Events;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// Reacts to a Broadcaster signal or cue picked in the inspector by invoking a <see cref="UnityEvent"/>. The reaction is
    /// fire-and-forget: it ignores the event's payload and, for a cue, completes instantly (it never holds up the cue's when-all).
    /// </summary>
    /// <remarks>
    /// Only signals (<see cref="ISignal"/>) and cues (<see cref="ICue"/>) are supported: they are the kinds that allow 0..N listeners and
    /// don't have to report a value. Commands and requests demand exactly one handler that returns a result, which doesn't map onto a
    /// parameterless inspector callback, so they stay code-only.
    /// </remarks>
    [AddComponentMenu(Constants.AddComponentMenu + "/Broadcaster Listener")]
    [HelpURL(Constants.BaseHelpUrl + "/api/SideXP.Broadcaster/" + nameof(BroadcasterListenerComponent))]
    public class BroadcasterListenerComponent : MonoBehaviour
    {

        #region Fields

        [SerializeField, EventTypeRef]
        [Tooltip("The signal or cue this component reacts to.")]
        private string _eventType = null;

        [SerializeField]
        [Tooltip("Invoked each time the selected signal is emitted or the selected cue is sent.")]
        private UnityEvent _onEmit = new UnityEvent();

        /// <summary>
        /// The open generic definition of <see cref="SubscribeSignal{T}(object, Action)"/>, resolved once for reflection.
        /// </summary>
        private static readonly MethodInfo s_subscribeSignalDefinition = typeof(BroadcasterListenerComponent)
            .GetMethod(nameof(SubscribeSignal), BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>
        /// The open generic definition of <see cref="PerformCue{T}(object, Action)"/>, resolved once for reflection.
        /// </summary>
        private static readonly MethodInfo s_performCueDefinition = typeof(BroadcasterListenerComponent)
            .GetMethod(nameof(PerformCue), BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>
        /// The closed bridge method per event type, so re-enabling never pays for <see cref="MethodInfo.MakeGenericMethod(Type[])"/> twice.
        /// A <c>null</c> value is cached too, to remember a type that is neither a signal nor a cue.
        /// </summary>
        private static readonly Dictionary<Type, MethodInfo> s_bridgeCache = new Dictionary<Type, MethodInfo>();

        #endregion


        #region Lifecycle

        /// <summary>
        /// Resolves the picked event type and registers the reaction on the shared bus.
        /// </summary>
        private void OnEnable()
        {
            if (string.IsNullOrWhiteSpace(_eventType))
                return;

            Type type = Type.GetType(_eventType);
            if (type == null)
            {
                Debug.LogWarning($"Failed to resolved the event type \"{_eventType}\" from {nameof(BroadcasterListenerComponent)} on \"{name}\". Nothing is registered.", this);
                return;
            }

            MethodInfo bridge = ResolveBridge(type);
            if (bridge == null)
            {
                Debug.LogWarning($"Failed to reference \"{{type.Name\" from {nameof(BroadcasterListenerComponent)} on \"{name}\", since it's neither a signal nor a cue. Nothing is registered.", this);
                return;
            }

            // The bridge closes over this component as the owner, so OnDisable's UnregisterAll(this) tears the registration down.
            bridge.Invoke(null, new object[] { this, (Action)OnFired });
        }

        /// <summary>
        /// Removes the reaction from the shared bus. Safe to call even when nothing was registered.
        /// </summary>
        private void OnDisable()
        {
            Broadcaster.UnregisterAll(this);
        }

        #endregion


        #region Internal

        /// <summary>
        /// Invokes the inspector-authored reaction. Used as the payload-agnostic callback handed to the bus.
        /// </summary>
        private void OnFired()
        {
            _onEmit?.Invoke();
        }

        /// <summary>
        /// Returns the closed bridge method that registers the reaction for the given event type: the signal bridge when it's an
        /// <see cref="ISignal"/>, the cue bridge when it's an <see cref="ICue"/>, or <c>null</c> when it's neither.
        /// </summary>
        private static MethodInfo ResolveBridge(Type type)
        {
            if (s_bridgeCache.TryGetValue(type, out MethodInfo cached))
                return cached;

            MethodInfo bridge = null;
            if (typeof(ISignal).IsAssignableFrom(type))
                bridge = s_subscribeSignalDefinition.MakeGenericMethod(type);
            else if (typeof(ICue).IsAssignableFrom(type))
                bridge = s_performCueDefinition.MakeGenericMethod(type);

            s_bridgeCache[type] = bridge;
            return bridge;
        }

        /// <summary>
        /// Subscribes <paramref name="onFired"/> to the signal type <typeparamref name="T"/>. The stored delegate is a genuine
        /// <see cref="Action{T}"/>, so the bus's exact-type dispatch stays on its fast path.
        /// </summary>
        private static void SubscribeSignal<T>(object owner, Action onFired) where T : ISignal
        {
            Broadcaster.Subscribe<T>(owner, _ => onFired());
        }

        /// <summary>
        /// Registers <paramref name="onFired"/> as an instant performer of the cue type <typeparamref name="T"/>. An instant performer runs
        /// synchronously and completes immediately, so it never delays the cue's completion.
        /// </summary>
        private static void PerformCue<T>(object owner, Action onFired) where T : ICue
        {
            Broadcaster.Perform<T>(owner, (Action<T>)(_ => onFired()));
        }

        #endregion

    }

}
