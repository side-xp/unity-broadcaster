using System;
using System.Collections.Generic;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// A live tally of what's registered on a bus, per event type, maintained purely from the bus's registration hooks (the window never
    /// asks the bus to enumerate itself). The Events window reads it to show each row's live columns: how many listeners or performers a
    /// type has, whether a provider is alive, and which owner holds a command's or request's single handler.
    /// </summary>
    /// <remarks>
    /// It reflects changes seen since it attached, so it's a play-mode view (registrations made before attaching aren't counted). Counts are
    /// clamped at zero so a miss can never drive one negative. Editor-only, like the hooks it listens to.
    /// </remarks>
    public sealed class EventBusLiveIndex
    {

        private readonly Dictionary<Type, State> _states = new Dictionary<Type, State>();
        private EventBus _bus;

        /// <summary>Raised whenever a registration change updated the tally, so the window can repaint.</summary>
        public event Action Changed;

        /// <summary>
        /// Starts tracking <paramref name="bus"/>, detaching from any previously tracked one and clearing the tally first (so counts start
        /// from a clean, if partial, view of the new bus).
        /// </summary>
        public void Attach(EventBus bus)
        {
            Detach();
            _states.Clear();

            _bus = bus;
            if (_bus == null)
                return;

            _bus.OnRegistered += HandleRegistered;
            _bus.OnUnregistered += HandleUnregistered;
        }

        /// <summary>
        /// Stops tracking the current bus. Safe to call when not attached.
        /// </summary>
        public void Detach()
        {
            if (_bus == null)
                return;

            _bus.OnRegistered -= HandleRegistered;
            _bus.OnUnregistered -= HandleUnregistered;
            _bus = null;
        }

        /// <summary>
        /// Reads the live tally for an event type. Returns false (and a default state) for a type nothing has registered against since
        /// attaching.
        /// </summary>
        public bool TryGet(Type eventType, out EventLiveState state)
        {
            if (eventType != null && _states.TryGetValue(eventType, out State tracked))
            {
                state = tracked.Snapshot();
                return true;
            }
            state = default;
            return false;
        }

        private void HandleRegistered(RegistrationInfo info)
        {
            State state = GetOrAdd(info.EventType);
            switch (info.Role)
            {
                case RegistrationRole.SignalListener: state.Listeners++; break;
                case RegistrationRole.CuePerformer: state.Performers++; break;
                case RegistrationRole.Provider: state.Providers++; state.ProviderOwner = info.Owner; break;
                case RegistrationRole.CommandHandler:
                case RegistrationRole.RequestHandler: state.Handlers++; state.HandlerOwner = info.Owner; break;
            }
            Changed?.Invoke();
        }

        private void HandleUnregistered(RegistrationInfo info)
        {
            State state = GetOrAdd(info.EventType);
            switch (info.Role)
            {
                case RegistrationRole.SignalListener: state.Listeners = Decrement(state.Listeners); break;
                case RegistrationRole.CuePerformer: state.Performers = Decrement(state.Performers); break;
                case RegistrationRole.Provider:
                    state.Providers = Decrement(state.Providers);
                    if (state.Providers == 0) state.ProviderOwner = null;
                    break;
                case RegistrationRole.CommandHandler:
                case RegistrationRole.RequestHandler:
                    state.Handlers = Decrement(state.Handlers);
                    if (state.Handlers == 0) state.HandlerOwner = null;
                    break;
            }
            Changed?.Invoke();
        }

        private State GetOrAdd(Type eventType)
        {
            if (!_states.TryGetValue(eventType, out State state))
            {
                state = new State();
                _states[eventType] = state;
            }
            return state;
        }

        private static int Decrement(int value) => value > 0 ? value - 1 : 0;

        /// <summary>Mutable per-type tally.</summary>
        private sealed class State
        {
            public int Listeners;
            public int Performers;
            public int Providers;
            public int Handlers;
            public object ProviderOwner;
            public object HandlerOwner;

            public EventLiveState Snapshot() => new EventLiveState(Listeners, Performers, Providers > 0, ProviderOwner, Handlers > 0, HandlerOwner);
        }

    }

    /// <summary>
    /// An immutable read of one event type's live registrations, as the window renders them.
    /// </summary>
    public readonly struct EventLiveState
    {

        /// <summary>Number of signal listeners currently registered.</summary>
        public int Listeners { get; }

        /// <summary>Number of cue performers currently registered.</summary>
        public int Performers { get; }

        /// <summary>Whether a state provider is alive for the type.</summary>
        public bool HasProvider { get; }

        /// <summary>The provider's owner, or <c>null</c> when none is alive.</summary>
        public object ProviderOwner { get; }

        /// <summary>Whether a command or request handler is registered for the type.</summary>
        public bool HasHandler { get; }

        /// <summary>The handler's owner, or <c>null</c> when none is registered.</summary>
        public object HandlerOwner { get; }

        /// <inheritdoc cref="EventLiveState"/>
        public EventLiveState(int listeners, int performers, bool hasProvider, object providerOwner, bool hasHandler, object handlerOwner)
        {
            Listeners = listeners;
            Performers = performers;
            HasProvider = hasProvider;
            ProviderOwner = providerOwner;
            HasHandler = hasHandler;
            HandlerOwner = handlerOwner;
        }

    }

}
