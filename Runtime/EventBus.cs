using System;
using System.Collections.Generic;

using UnityEngine;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// A code-first, type-keyed event bus for decoupled communication.<br/>
    /// In this system, an event is a C# type, that type is both the identity and the payload.<br/>
    /// This instantiable core holds all state and logic. You should prefer using <see cref="Broadcaster"/> façade at runtime, instead of
    /// using this class directly. But it's useful if you need an isolated or temporary events bus, eg. for testing.
    /// </summary>
    public sealed class EventBus
    {

        #region Fields

        /// <summary>
        /// Active signal registrations, keyed by exact signal type.
        /// </summary>
        /// <remarks>
        /// There's no inheritance dispatch: an event of type <c>MyEvent</c> is mechanically different than <c>MyEventBase</c>.
        /// </remarks>
        private readonly Dictionary<Type, List<Registration>> _signalRegistrations = new Dictionary<Type, List<Registration>>();

        /// <summary>
        /// Number of dispatches (or bulk edits) currently iterating registration lists.<br/>
        /// If greater than zero, that means some loop is walking a list, so structural edits to that list are unsafe (they'd corrupt the
        /// iteration) and are deferred until the outermost operation finishes (which is the role of <see cref="_pendingRemovals"/>).<br/>
        /// This value is a counter rather than a boolean flag because dispatches nest (a listener may emit from inside its callbac)k.
        /// </summary>
        private int _dispatchDepth = 0;

        /// <summary>
        /// Registrations that were requested for removal while a dispatch was running (so their list couldn't be edited yet). See
        /// <see cref="_dispatchDepth"/>.<br/>
        /// They are already marked inactive (dispatch loops skip them) and get physically pulled from their lists once the outermost
        /// dispatch ends.
        /// </summary>
        private readonly List<Registration> _pendingRemovals = new List<Registration>();

        /// <summary>
        /// State providers, at most one per signal type. A provider answers "what is the current value of this signal?" so a listener
        /// subscribing with <c>init</c> can pull it immediately.
        /// </summary>
        private readonly Dictionary<Type, Registration> _providers = new Dictionary<Type, Registration>();

        #endregion


        #region Signals

        /// <summary>
        /// Delivers a signal synchronously to every current listener, in registration order. Returns once all listeners have run.<br/>
        /// Zero listeners is fine (silent). A throwing listener never stops the others (its exception is logged and the rest still run).
        /// </summary>
        /// <typeparam name="T">The exact signal type. Dispatch is exact-type only (a derived signal never reaches a base-type
        /// listener).</typeparam>
        /// <param name="signal">The signal instance (its fields are the payload).</param>
        public void Emit<T>(T signal) where T : ISignal
        {
            MainThreadGuard.Assert();

            if (!_signalRegistrations.TryGetValue(typeof(T), out List<Registration> list))
                return;

            _dispatchDepth++;
            try
            {
                // Snapshot the count so registrations appended during this dispatch are not invoked by it.
                int count = list.Count;
                for (int i = 0; i < count; i++)
                {
                    Registration registration = list[i];
                    // A registration removed during this dispatch is skipped rather than invoked.
                    if (!registration.Active)
                        continue;

                    try
                    {
                        // Exact cast back to the concrete delegate type: the payload is never boxed on the hot path.
                        ((Action<T>)registration.Callback).Invoke(signal);
                    }
                    catch (Exception exception)
                    {
                        // Exception isolation: log with the owner as Unity context object, then carry on.
                        Debug.LogException(exception, registration.Owner as UnityEngine.Object);
                    }
                }
            }
            finally
            {
                _dispatchDepth--;
                if (_dispatchDepth == 0)
                    RemovePendingRemovals();
            }
        }

        /// <summary>
        /// Registers a listener for a signal type.
        /// </summary>
        /// <typeparam name="T">The exact signal type to listen for.</typeparam>
        /// <param name="owner">The owner of this registration (powers <see cref="UnsubscribeAll(object)"/> and diagnostics).</param>
        /// <param name="listener">The callback invoked on each emit.</param>
        /// <param name="init">If true and a provider for <typeparamref name="T"/> is alive, the listener is invoked
        /// immediately with that provider's current value (in addition to future emits). Does nothing if no provider is
        /// alive. Only sees providers registered <i>before</i> this call.</param>
        /// <returns>A handle that unregisters this listener when disposed.</returns>
        public SubscriptionHandle Subscribe<T>(object owner, Action<T> listener, bool init = false) where T : ISignal
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            MainThreadGuard.Assert();

            Type type = typeof(T);
            if (!_signalRegistrations.TryGetValue(type, out List<Registration> list))
            {
                list = new List<Registration>();
                _signalRegistrations[type] = list;
            }

            Registration registration = new Registration
            {
                Bus = this,
                Role = RegistrationRole.SignalListener,
                EventType = type,
                Owner = owner,
                Callback = listener,
                Active = true,
            };
            list.Add(registration);

            // Pull the current value from a live provider, if the caller opted in.
            if (init && TryGetCurrent(out T current))
            {
                try
                {
                    listener.Invoke(current);
                }
                catch (Exception exception)
                {
                    // Isolate the init call exactly like a dispatched one.
                    Debug.LogException(exception, owner as UnityEngine.Object);
                }
            }

            return new SubscriptionHandle(registration);
        }

        /// <summary>
        /// Removes a previously registered signal listener, matched by <see cref="Delegate.Equals(object)"/> (target + method, never
        /// reference equality, so a method group re-created at the call site still matches). Removes the first matching active
        /// registration.
        /// </summary>
        /// <typeparam name="T">The signal type the listener was registered for.</typeparam>
        /// <param name="listener">The same listener (a method group re-created at the call site still matches).</param>
        /// <returns>True if a matching registration was found and removed.</returns>
        public bool Unsubscribe<T>(Action<T> listener) where T : ISignal
        {
            if (listener == null)
                return false;

            if (!_signalRegistrations.TryGetValue(typeof(T), out List<Registration> list))
                return false;

            foreach (Registration registration in list)
            {
                if (registration.Active && registration.Callback.Equals(listener))
                {
                    Remove(registration);
                    return true;
                }
            }
            return false;
        }

        #endregion


        #region State & providers

        /// <summary>
        /// Registers a provider for a signal type.<br/>
        /// The state owner's answer to "what is the current value?". A listener subscribing with <c>init: true</c> pulls this value
        /// immediately, and <see cref="TryGetCurrent{T}(out T)"/> reads it on demand. The provider is invoked lazily (never cached), so
        /// its value is never stale, and it dies with its owner.
        /// </summary>
        /// <typeparam name="T">The exact signal type this provider supplies the current value for.</typeparam>
        /// <param name="owner">The owner of this registration.</param>
        /// <param name="provider">Returns the current value on demand.</param>
        /// <param name="replace">By default, if you try to add a provider while another one already exists, this call is ignored. If
        /// enabled, this provider supersedes it.</param>
        /// <returns>A handle that unregisters this provider when disposed. When a provider already exists and <paramref name="replace"/>
        /// is <c>false</c>, returns an inactive handle instead.</returns>
        public SubscriptionHandle Provide<T>(object owner, Func<T> provider, bool replace = false) where T : ISignal
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            MainThreadGuard.Assert();

            Type type = typeof(T);
            if (_providers.TryGetValue(type, out Registration existing) && existing.Active)
            {
                if (!replace)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogError($"[Broadcaster] A provider for '{type.Name}' is already registered. The existing one stays authoritative and this registration is ignored. Pass replace: true for an intentional hand-off.", owner as UnityEngine.Object);
#endif
                    return default;
                }

                // Supersede the incumbent: deactivate it so its handle and any pending removal become no-ops, then let the slot be
                // overwritten below. The outgoing owner's later cleanup won't find it in the slot anymore.
                existing.Active = false;
            }

            Registration registration = new Registration
            {
                Bus = this,
                Role = RegistrationRole.Provider,
                EventType = type,
                Owner = owner,
                Callback = provider,
                Active = true,
            };
            _providers[type] = registration;
            return new SubscriptionHandle(registration);
        }

        /// <summary>
        /// Reads the current value for a signal type from its provider, without subscribing. Returns false (and <paramref name="current"/>
        /// is <c>default</c>) if no provider is alive for the type.
        /// </summary>
        /// <typeparam name="T">The signal type to read the current value of.</typeparam>
        /// <param name="current">The provider's current value, or <c>default</c> if none.</param>
        /// <returns>True if a provider answered, false otherwise.</returns>
        public bool TryGetCurrent<T>(out T current) where T : ISignal
        {
            MainThreadGuard.Assert();

            if (_providers.TryGetValue(typeof(T), out Registration provider) && provider.Active)
            {
                current = ((Func<T>)provider.Callback).Invoke();
                return true;
            }

            current = default;
            return false;
        }

        #endregion


        #region Cleanup

        /// <summary>
        /// Removes <b>every</b> registration made by the given owner.
        /// </summary>
        /// <param name="owner">The owner whose registrations to remove (compared by reference).</param>
        /// <returns>The number of registrations removed.</returns>
        public int UnsubscribeAll(object owner)
        {
            if (owner == null)
                return 0;

            int removed = 0;
            // Guard the enumeration so removals are deferred until after we finish walking the lists.
            _dispatchDepth++;
            try
            {
                foreach (List<Registration> list in _signalRegistrations.Values)
                {
                    foreach (Registration registration in list)
                    {
                        if (registration.Active && ReferenceEquals(registration.Owner, owner))
                        {
                            Remove(registration);
                            removed++;
                        }
                    }
                }

                foreach (Registration provider in _providers.Values)
                {
                    if (provider.Active && ReferenceEquals(provider.Owner, owner))
                    {
                        Remove(provider);
                        removed++;
                    }
                }
            }
            finally
            {
                _dispatchDepth--;
                if (_dispatchDepth == 0)
                    RemovePendingRemovals();
            }
            return removed;
        }

        /// <summary>
        /// Hard reset: removes every registration of every kind. The bus stores no payloads, so there is nothing else to clear.
        /// </summary>
        public void Clear()
        {
            // Deactivate first so any dispatch in progress skips the rest of its listeners.
            foreach (List<Registration> list in _signalRegistrations.Values)
            {
                foreach (Registration registration in list)
                    registration.Active = false;
            }
            foreach (Registration provider in _providers.Values)
                provider.Active = false;

            _signalRegistrations.Clear();
            _providers.Clear();
            _pendingRemovals.Clear();
        }

        /// <summary>
        /// Removes every registration for a single event type.
        /// </summary>
        /// <typeparam name="T">The event type to clear.</typeparam>
        public void Clear<T>() where T : IEvent
        {
            Type type = typeof(T);
            if (_signalRegistrations.TryGetValue(type, out List<Registration> list))
            {
                // Deactivate first so any dispatch in progress skips these; the list object stays alive for in-flight
                // loops that already captured it.
                foreach (Registration registration in list)
                    registration.Active = false;
                _signalRegistrations.Remove(type);
            }

            if (_providers.TryGetValue(type, out Registration provider))
            {
                provider.Active = false;
                _providers.Remove(type);
            }
        }

        #endregion


        #region Internal

        /// <summary>
        /// Marks a registration inactive and removes it (immediately when no dispatch is in progress), otherwise deferred until the
        /// outermost dispatch ends.
        /// </summary>
        internal void Remove(Registration registration)
        {
            if (registration == null || !registration.Active)
                return;

            registration.Active = false;
            if (_dispatchDepth > 0)
                _pendingRemovals.Add(registration);
            else
                RemoveFromStore(registration);
        }

        /// <summary>
        /// Physically removes a registration from whichever store holds it (listener list or provider slot), dropping an
        /// emptied list entry.
        /// </summary>
        private void RemoveFromStore(Registration registration)
        {
            if (registration.Role == RegistrationRole.Provider)
            {
                // Only drop the slot if it still points at this exact registration — a newer provider may have replaced
                // it while this one was pending removal.
                if (_providers.TryGetValue(registration.EventType, out Registration current) && current == registration)
                    _providers.Remove(registration.EventType);
                return;
            }

            if (_signalRegistrations.TryGetValue(registration.EventType, out List<Registration> list))
            {
                list.Remove(registration);
                if (list.Count == 0)
                    _signalRegistrations.Remove(registration.EventType);
            }
        }

        /// <summary>
        /// Physically removes everything deactivated during the dispatch that just ended.
        /// </summary>
        private void RemovePendingRemovals()
        {
            if (_pendingRemovals.Count == 0)
                return;

            foreach (Registration registration in _pendingRemovals)
                RemoveFromStore(registration);
            _pendingRemovals.Clear();
        }

        #endregion

    }

}
