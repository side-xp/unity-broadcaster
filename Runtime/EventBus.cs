using System;
using System.Collections.Generic;
using System.Threading;

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
        /// This value is a counter rather than a boolean flag because dispatches nest (a listener may emit from inside its callback).
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

        /// <summary>
        /// Command and request handlers, at most one per event type. Both kinds share this single-handler store (a type is only ever a
        /// command <i>or</i> a request, never both), keyed by exact event type.
        /// </summary>
        private readonly Dictionary<Type, Registration> _handlers = new Dictionary<Type, Registration>();

        /// <summary>
        /// Cue performers, keyed by exact cue type. Like signal listeners, a cue has 0..N performers held in a per-type list and invoked
        /// in registration order; unlike signals, a cue's send awaits every performer's completion (when-all).
        /// </summary>
        private readonly Dictionary<Type, List<Registration>> _cuePerformers = new Dictionary<Type, List<Registration>>();

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


        #region Commands

        /// <summary>
        /// Registers the single handler that performs a command. A command has <b>exactly one</b> handler: a second registration for the
        /// same type is ignored.
        /// </summary>
        /// <typeparam name="T">The exact command type to handle.</typeparam>
        /// <param name="owner">The owner of this registration.</param>
        /// <param name="handler">Performs the action when the command is ordered.</param>
        /// <param name="replace">By default, if you try to add a handler while another one already exists, this call is ignored. If
        /// enabled, this handler supersedes it (for an intentional hand-off, eg. across an additive scene load).</param>
        /// <returns>A handle that unregisters this handler when disposed. When a handler already exists and <paramref name="replace"/> is
        /// <c>false</c>, returns an inactive handle instead.</returns>
        public SubscriptionHandle Obey<T>(object owner, Action<T> handler, bool replace = false) where T : ICommand
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            MainThreadGuard.Assert();
            return RegisterHandler(typeof(T), RegistrationRole.CommandHandler, owner, handler, replace, async: false);
        }

        /// <summary>
        /// Registers the single <b>async</b> handler that performs a command. A command has <b>exactly one</b> handler: a second
        /// registration for the same type is ignored. Reachable only through <see cref="OrderAsync{T}(T, CancellationToken)"/> (a
        /// synchronous <see cref="Order{T}(T)"/> can't wait for it).
        /// </summary>
        /// <param name="handler">Performs the action and returns an <see cref="Awaitable"/> that completes when it's done.</param>
        /// <inheritdoc cref="Obey{T}(object, Action{T}, bool)"/>
        public SubscriptionHandle Obey<T>(object owner, Func<T, Awaitable> handler, bool replace = false) where T : ICommand
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            MainThreadGuard.Assert();
            return RegisterHandler(typeof(T), RegistrationRole.CommandHandler, owner, handler, replace, async: true);
        }

        /// <summary>
        /// Registers the single handler that performs a command and reports its outcome. A command has <b>exactly one</b> handler: a second
        /// registration for the same type is ignored.
        /// </summary>
        /// <typeparam name="T">The exact command type to handle.</typeparam>
        /// <typeparam name="TResult">The outcome the action produces.</typeparam>
        /// <param name="owner">The owner of this registration.</param>
        /// <param name="handler">Performs the action and returns its outcome.</param>
        /// <param name="replace">By default, if you try to add a handler while another one already exists, this call is ignored. If
        /// enabled, this handler supersedes it (for an intentional hand-off, eg. across an additive scene load).</param>
        /// <returns>A handle that unregisters this handler when disposed. When a handler already exists and <paramref name="replace"/> is
        /// <c>false</c>, returns an inactive handle instead.</returns>
        public SubscriptionHandle Obey<T, TResult>(object owner, Func<T, TResult> handler, bool replace = false) where T : ICommand<TResult>
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            MainThreadGuard.Assert();
            // Store an invoker typed on the interface so the interface-typed Order can call it back without knowing the concrete command
            // type (the cast unboxes the command the caller passed as ICommand<TResult>).
            Func<ICommand<TResult>, TResult> invoker = command => handler((T)command);
            return RegisterHandler(typeof(T), RegistrationRole.CommandHandler, owner, invoker, replace, async: false);
        }

        /// <summary>
        /// Registers the single <b>async</b> handler that performs a command and reports its outcome. A command has <b>exactly one</b>
        /// handler: a second registration for the same type is ignored. Reachable only through
        /// <see cref="OrderAsync{TResult}(ICommand{TResult}, CancellationToken)"/> (a synchronous
        /// <see cref="Order{TResult}(ICommand{TResult})"/> can't wait for it).
        /// </summary>
        /// <param name="handler">Performs the action and returns an <see cref="Awaitable{TResult}"/> carrying its outcome.</param>
        /// <inheritdoc cref="Obey{T, TResult}(object, Func{T, TResult}, bool)"/>
        public SubscriptionHandle Obey<T, TResult>(object owner, Func<T, Awaitable<TResult>> handler, bool replace = false) where T : ICommand<TResult>
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            MainThreadGuard.Assert();
            Func<ICommand<TResult>, Awaitable<TResult>> invoker = command => handler((T)command);
            return RegisterHandler(typeof(T), RegistrationRole.CommandHandler, owner, invoker, replace, async: true);
        }

        /// <summary>
        /// Orders a command, invoking its single handler synchronously. Returns whether a handler performed it. With no handler
        /// registered, logs a dev-build error and returns <c>false</c>.
        /// </summary>
        /// <typeparam name="T">The exact command type.</typeparam>
        /// <param name="command">The command instance (its fields are the payload).</param>
        /// <returns>True if a handler performed the command.</returns>
        public bool Order<T>(T command) where T : ICommand
        {
            MainThreadGuard.Assert();

            if (_handlers.TryGetValue(typeof(T), out Registration registration) && registration.Active)
            {
                if (registration.Async)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogError($"[Broadcaster] The handler for command '{typeof(T).Name}' is asynchronous and can't be run synchronously. Use OrderAsync.");
#endif
                    return false;
                }

                ((Action<T>)registration.Callback).Invoke(command);
                return true;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError($"[Broadcaster] No handler is registered for command '{typeof(T).Name}'. The command was not performed.");
#endif
            return false;
        }

        /// <summary>
        /// Orders a command and returns the outcome its handler produced, invoked synchronously. A handler is mandatory: with none
        /// registered this throws (a valued order has no outcome to report without one).
        /// </summary>
        /// <typeparam name="TResult">The outcome the action produces.</typeparam>
        /// <param name="command">The command instance, typed as its interface so <typeparamref name="TResult"/> is inferred at the call
        /// site.</param>
        /// <returns>The outcome the handler produced.</returns>
        /// <exception cref="InvalidOperationException">No handler is registered for the command type.</exception>
        public TResult Order<TResult>(ICommand<TResult> command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            MainThreadGuard.Assert();

            Type type = command.GetType();
            if (_handlers.TryGetValue(type, out Registration registration) && registration.Active)
            {
                if (registration.Async)
                    throw new InvalidOperationException($"The handler for command '{type.Name}' is asynchronous and can't be run synchronously. Use OrderAsync.");

                return ((Func<ICommand<TResult>, TResult>)registration.Callback).Invoke(command);
            }

            throw new InvalidOperationException($"No handler is registered for command '{type.Name}', which must report a {typeof(TResult).Name}.");
        }

        /// <summary>
        /// Orders a command and awaits its completion. Works on both sync and async handlers (a sync handler completes immediately). The
        /// awaitable resolves as cancelled if <paramref name="cancellation"/> fires or if the handler is unregistered before it finishes,
        /// and faulted if the handler throws (it never hangs).
        /// </summary>
        /// <param name="cancellation">Cancels the caller's wait (the handler itself manages its own cancellation).</param>
        /// <returns>An awaitable that completes when the handler finishes. Completes immediately (dev-build error) when no handler is
        /// registered. There is no outcome to report for a void command.</returns>
        /// <inheritdoc cref="Order{T}(T)"/>
        public Awaitable OrderAsync<T>(T command, CancellationToken cancellation = default) where T : ICommand
        {
            MainThreadGuard.Assert();

            if (cancellation.IsCancellationRequested)
                return CanceledAwaitable();

            if (_handlers.TryGetValue(typeof(T), out Registration registration) && registration.Active)
            {
                if (registration.Async)
                    return BridgeAwaitable(() => ((Func<T, Awaitable>)registration.Callback).Invoke(command), registration, cancellation);

                // A sync handler through the async verb: run it now and hand back an already-completed (or faulted) awaitable.
                try
                {
                    ((Action<T>)registration.Callback).Invoke(command);
                    return CompletedAwaitable();
                }
                catch (Exception exception)
                {
                    return FaultedAwaitable(exception);
                }
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError($"[Broadcaster] No handler is registered for command '{typeof(T).Name}'. The command was not performed.");
#endif
            return CompletedAwaitable();
        }

        /// <summary>
        /// Orders a command and awaits the outcome its handler produces. Works on both sync and async handlers (a sync handler completes
        /// immediately). The awaitable resolves as cancelled if <paramref name="cancellation"/> fires or if the handler is unregistered
        /// before it finishes, and faulted if the handler throws or no handler is registered (it never hangs).
        /// </summary>
        /// <param name="cancellation">Cancels the caller's wait (the handler itself manages its own cancellation).</param>
        /// <returns>An awaitable carrying the handler's outcome.</returns>
        /// <inheritdoc cref="Order{TResult}(ICommand{TResult})"/>
        public Awaitable<TResult> OrderAsync<TResult>(ICommand<TResult> command, CancellationToken cancellation = default)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            MainThreadGuard.Assert();

            if (cancellation.IsCancellationRequested)
                return CanceledAwaitable<TResult>();

            Type type = command.GetType();
            if (_handlers.TryGetValue(type, out Registration registration) && registration.Active)
            {
                if (registration.Async)
                    return BridgeAwaitable(() => ((Func<ICommand<TResult>, Awaitable<TResult>>)registration.Callback).Invoke(command), registration, cancellation);

                try
                {
                    TResult result = ((Func<ICommand<TResult>, TResult>)registration.Callback).Invoke(command);
                    return CompletedAwaitable(result);
                }
                catch (Exception exception)
                {
                    return FaultedAwaitable<TResult>(exception);
                }
            }

            return FaultedAwaitable<TResult>(new InvalidOperationException($"No handler is registered for command '{type.Name}', which must report a {typeof(TResult).Name}."));
        }

        #endregion


        #region Requests

        /// <summary>
        /// Registers the single handler that answers a request. A request has <b>exactly one</b> handler: a second registration for the
        /// same type is ignored.
        /// </summary>
        /// <typeparam name="T">The exact request type to answer.</typeparam>
        /// <typeparam name="TResult">The type of the answer.</typeparam>
        /// <param name="owner">The owner of this registration.</param>
        /// <param name="handler">Produces the answer. By convention it must not mutate state (asking is always safe).</param>
        /// <param name="replace">By default, if you try to add a handler while another one already exists, this call is ignored. If
        /// enabled, this handler supersedes it (for an intentional hand-off, eg. across an additive scene load).</param>
        /// <returns>A handle that unregisters this handler when disposed. When a handler already exists and <paramref name="replace"/> is
        /// <c>false</c>, returns an inactive handle instead.</returns>
        public SubscriptionHandle Answer<T, TResult>(object owner, Func<T, TResult> handler, bool replace = false) where T : IRequest<TResult>
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            MainThreadGuard.Assert();
            // Same interface-typed invoker trick as commands, so the interface-typed Ask can call back without the concrete request type.
            Func<IRequest<TResult>, TResult> invoker = request => handler((T)request);
            return RegisterHandler(typeof(T), RegistrationRole.RequestHandler, owner, invoker, replace, async: false);
        }

        /// <summary>
        /// Registers the single <b>async</b> handler that answers a request. A request has <b>exactly one</b> handler: a second
        /// registration for the same type is ignored. Reachable only through
        /// <see cref="AskAsync{TResult}(IRequest{TResult}, CancellationToken)"/> (a synchronous
        /// <see cref="Ask{TResult}(IRequest{TResult})"/> can't wait for it).
        /// </summary>
        /// <param name="handler">Produces the answer as an <see cref="Awaitable{TResult}"/>. By convention it must not mutate state.</param>
        /// <inheritdoc cref="Answer{T, TResult}(object, Func{T, TResult}, bool)"/>
        public SubscriptionHandle Answer<T, TResult>(object owner, Func<T, Awaitable<TResult>> handler, bool replace = false) where T : IRequest<TResult>
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            MainThreadGuard.Assert();
            Func<IRequest<TResult>, Awaitable<TResult>> invoker = request => handler((T)request);
            return RegisterHandler(typeof(T), RegistrationRole.RequestHandler, owner, invoker, replace, async: true);
        }

        /// <summary>
        /// Asks a request and returns its handler's answer, invoked synchronously. A handler is mandatory: with none registered this
        /// throws (a question with no answer has no value to return). Use <see cref="TryAsk{TResult}(IRequest{TResult}, out TResult)"/> to
        /// tolerate an unanswered request.
        /// </summary>
        /// <typeparam name="TResult">The type of the answer.</typeparam>
        /// <param name="request">The request instance, typed as its interface so <typeparamref name="TResult"/> is inferred at the call
        /// site.</param>
        /// <returns>The handler's answer.</returns>
        /// <exception cref="InvalidOperationException">No handler is registered for the request type.</exception>
        public TResult Ask<TResult>(IRequest<TResult> request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            MainThreadGuard.Assert();

            Type type = request.GetType();
            if (_handlers.TryGetValue(type, out Registration registration) && registration.Active)
            {
                if (registration.Async)
                    throw new InvalidOperationException($"The handler for request '{type.Name}' is asynchronous and can't be run synchronously. Use AskAsync.");

                return ((Func<IRequest<TResult>, TResult>)registration.Callback).Invoke(request);
            }

            throw new InvalidOperationException($"No handler is registered to answer request '{type.Name}'.");
        }

        /// <summary>
        /// Asks a request without requiring an answer. Returns whether a handler answered. <paramref name="result"/> is the answer, or
        /// <c>default</c> if none.
        /// </summary>
        /// <typeparam name="TResult">The type of the answer.</typeparam>
        /// <param name="request">The request instance.</param>
        /// <param name="result">The handler's answer, or <c>default</c> if no handler is registered.</param>
        /// <returns>True if a handler answered.</returns>
        public bool TryAsk<TResult>(IRequest<TResult> request, out TResult result)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            MainThreadGuard.Assert();

            Type type = request.GetType();
            if (_handlers.TryGetValue(type, out Registration registration) && registration.Active)
            {
                if (registration.Async)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogError($"[Broadcaster] The handler for request '{type.Name}' is asynchronous and can't be answered synchronously. Use AskAsync.");
#endif
                    result = default;
                    return false;
                }

                result = ((Func<IRequest<TResult>, TResult>)registration.Callback).Invoke(request);
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// Asks a request and awaits its handler's answer. Works on both sync and async handlers (a sync handler completes immediately).
        /// The awaitable resolves as cancelled if <paramref name="cancellation"/> fires or if the handler is unregistered before it
        /// answers, and faulted if the handler throws or no handler is registered (it never hangs).
        /// </summary>
        /// <param name="cancellation">Cancels the caller's wait (the handler itself manages its own cancellation).</param>
        /// <returns>An awaitable carrying the handler's answer.</returns>
        /// <inheritdoc cref="Ask{TResult}(IRequest{TResult})"/>
        public Awaitable<TResult> AskAsync<TResult>(IRequest<TResult> request, CancellationToken cancellation = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            MainThreadGuard.Assert();

            if (cancellation.IsCancellationRequested)
                return CanceledAwaitable<TResult>();

            Type type = request.GetType();
            if (_handlers.TryGetValue(type, out Registration registration) && registration.Active)
            {
                if (registration.Async)
                    return BridgeAwaitable(() => ((Func<IRequest<TResult>, Awaitable<TResult>>)registration.Callback).Invoke(request), registration, cancellation);

                try
                {
                    TResult result = ((Func<IRequest<TResult>, TResult>)registration.Callback).Invoke(request);
                    return CompletedAwaitable(result);
                }
                catch (Exception exception)
                {
                    return FaultedAwaitable<TResult>(exception);
                }
            }

            return FaultedAwaitable<TResult>(new InvalidOperationException($"No handler is registered to answer request '{type.Name}'."));
        }

        #endregion


        #region Cues

        /// <summary>
        /// Registers an <b>instant</b> performer for a cue type. It runs synchronously when the cue is sent and completes immediately.
        /// </summary>
        /// <typeparam name="T">The exact cue type to perform.</typeparam>
        /// <param name="owner">The owner of this registration.</param>
        /// <param name="performer">The reaction, run synchronously on each cue.</param>
        /// <returns>A handle that unregisters this performer when disposed.</returns>
        public SubscriptionHandle Perform<T>(object owner, Action<T> performer) where T : ICue
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (performer == null)
                throw new ArgumentNullException(nameof(performer));

            MainThreadGuard.Assert();
            return AddPerformer(typeof(T), owner, performer);
        }

        /// <summary>
        /// Registers a <b>durative</b> performer for a cue type. It starts synchronously when the cue is sent and returns an
        /// <see cref="Awaitable"/> that completes when its reaction finishes; the cue's completion waits for it.
        /// </summary>
        /// <param name="performer">The reaction; its first synchronous stretch runs on send, and the returned awaitable marks
        /// completion.</param>
        /// <returns>A handle that unregisters this performer when disposed.</returns>
        /// <inheritdoc cref="Perform{T}(object, Action{T})"/>
        public SubscriptionHandle Perform<T>(object owner, Func<T, Awaitable> performer) where T : ICue
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (performer == null)
                throw new ArgumentNullException(nameof(performer));

            MainThreadGuard.Assert();
            return AddPerformer(typeof(T), owner, performer);
        }

        /// <summary>
        /// Registers a <b>callback-style</b> performer for a cue type, for coroutine/callback code that doesn't want to author an
        /// <see cref="Awaitable"/>. It starts synchronously and receives a <c>done</c> callback it must invoke when its reaction finishes;
        /// the cue's completion waits for that call.
        /// </summary>
        /// <param name="performer">The reaction, receiving the cue and a <c>done</c> callback. It must eventually call <c>done</c> or the
        /// cue never completes for this performer (until the performer is unregistered).</param>
        /// <returns>A handle that unregisters this performer when disposed.</returns>
        /// <inheritdoc cref="Perform{T}(object, Action{T})"/>
        public SubscriptionHandle Perform<T>(object owner, CuePerformerDelegate<T> performer) where T : ICue
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (performer == null)
                throw new ArgumentNullException(nameof(performer));

            MainThreadGuard.Assert();
            return AddPerformer(typeof(T), owner, performer);
        }

        /// <summary>
        /// Sends a cue: every current performer <b>starts synchronously in registration order</b> (an instant performer runs fully, a
        /// durative/callback one runs its synchronous stretch), then the returned awaitable resolves when the <b>last</b> performer
        /// finishes (when-all). Zero performers resolves instantly.<br/>
        /// A throwing performer is isolated (logged with its owner as context) and neither holds up nor kills the others. The awaitable
        /// resolves as cancelled if <paramref name="cancellation"/> fires (the cancellation fans out to every in-flight performer). A
        /// performer unregistered mid-cue resolves rather than hanging; the cue still completes on the rest.
        /// </summary>
        /// <typeparam name="T">The exact cue type.</typeparam>
        /// <param name="cue">The cue instance (its fields are the payload).</param>
        /// <param name="cancellation">Cancels the cue: resolves the awaitable as cancelled and fans out to every in-flight performer.</param>
        /// <returns>An awaitable that completes when every performer has finished.</returns>
        public Awaitable Cue<T>(T cue, CancellationToken cancellation = default) where T : ICue
        {
            MainThreadGuard.Assert();

            if (cancellation.IsCancellationRequested)
                return CanceledAwaitable();

            if (!_cuePerformers.TryGetValue(typeof(T), out List<Registration> list) || list.Count == 0)
                return CompletedAwaitable();

            AwaitableCompletionSource completion = new AwaitableCompletionSource();
            bool resolved = false;

            // When-all accounting. A "starting" guard (+1) keeps the count above zero while performers are still being started, so an
            // instant performer completing synchronously mid-loop can't resolve the cue before the later performers have even begun.
            // The cancellation fan-out is what each performer registers below; when the token fires it drains every in-flight slot, so the
            // cue resolves here. The last slot to drain decides completed vs cancelled by inspecting the token (a cancelled token means the
            // drain was the fan-out, not natural completion — this also avoids depending on the order the token invokes its callbacks).
            int outstanding = 1;
            void OnPerformerDone()
            {
                outstanding--;
                if (outstanding == 0 && !resolved)
                {
                    resolved = true;
                    if (cancellation.IsCancellationRequested)
                        completion.TrySetCanceled();
                    else
                        completion.TrySetResult();
                }
            }

            // Guard the enumeration: an instant performer that unregisters another during its synchronous run defers the structural edit.
            _dispatchDepth++;
            try
            {
                int count = list.Count;
                for (int i = 0; i < count; i++)
                {
                    Registration registration = list[i];
                    if (!registration.Active)
                        continue;

                    outstanding++;
                    StartCuePerformer(registration, cue, cancellation, OnPerformerDone);
                }
            }
            finally
            {
                _dispatchDepth--;
                if (_dispatchDepth == 0)
                    RemovePendingRemovals();
            }

            // Release the starting guard: if every performer already finished (all instant, or none active), the cue resolves now.
            OnPerformerDone();
            return completion.Awaitable;
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

                foreach (List<Registration> list in _cuePerformers.Values)
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

                foreach (Registration handler in _handlers.Values)
                {
                    if (handler.Active && ReferenceEquals(handler.Owner, owner))
                    {
                        Remove(handler);
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
            // Cue performers may be mid-cue: resolve their in-flight slots so any awaiting cue completes rather than hanging.
            foreach (List<Registration> list in _cuePerformers.Values)
            {
                foreach (Registration registration in list)
                {
                    registration.Active = false;
                    CancelInFlight(registration);
                }
            }
            foreach (Registration provider in _providers.Values)
                provider.Active = false;
            foreach (Registration handler in _handlers.Values)
            {
                handler.Active = false;
                CancelInFlight(handler);
            }

            _signalRegistrations.Clear();
            _cuePerformers.Clear();
            _providers.Clear();
            _handlers.Clear();
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

            if (_cuePerformers.TryGetValue(type, out List<Registration> performers))
            {
                // Deactivate and resolve any in-flight performer so an awaiting cue completes instead of hanging.
                foreach (Registration registration in performers)
                {
                    registration.Active = false;
                    CancelInFlight(registration);
                }
                _cuePerformers.Remove(type);
            }

            if (_providers.TryGetValue(type, out Registration provider))
            {
                provider.Active = false;
                _providers.Remove(type);
            }

            if (_handlers.TryGetValue(type, out Registration handler))
            {
                handler.Active = false;
                CancelInFlight(handler);
                _handlers.Remove(type);
            }
        }

        #endregion


        #region Internal

        /// <summary>
        /// Stores a command or request handler in the single-handler slot for its type, enforcing the exactly-one rule. If an active
        /// handler already occupies the slot and <paramref name="replace"/> is <c>false</c>, logs a dev-build diagnostic and returns an
        /// inactive handle (the first stays authoritative); with <paramref name="replace"/> <c>true</c>, the incumbent is superseded.
        /// </summary>
        private SubscriptionHandle RegisterHandler(Type type, RegistrationRole role, object owner, Delegate callback, bool replace, bool async)
        {
            if (_handlers.TryGetValue(type, out Registration existing) && existing.Active)
            {
                if (!replace)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogError($"[Broadcaster] A handler for '{type.Name}' is already registered. The existing one stays authoritative and this registration is ignored. Pass replace: true for an intentional hand-off.", owner as UnityEngine.Object);
#endif
                    return default;
                }

                // Supersede the incumbent: deactivate it so its handle and any pending removal become no-ops, then let the slot be
                // overwritten below. The outgoing owner's later cleanup won't find it in the slot anymore.
                existing.Active = false;
                // The superseded handler is gone, so resolve anyone still awaiting it rather than leaving them hung.
                CancelInFlight(existing);
            }

            Registration registration = new Registration
            {
                Bus = this,
                Role = role,
                EventType = type,
                Owner = owner,
                Callback = callback,
                Active = true,
                Async = async,
            };
            _handlers[type] = registration;
            return new SubscriptionHandle(registration);
        }

        /// <summary>
        /// Appends a cue performer to the per-type performer list (creating the list on first use). The <paramref name="callback"/> is one
        /// of the three performer shapes (<see cref="Action{T}"/>, <see cref="Func{T, Awaitable}"/>, or
        /// <c>Action&lt;T, Action&gt;</c>); <see cref="StartCuePerformer{T}"/> dispatches on its concrete type.
        /// </summary>
        private SubscriptionHandle AddPerformer(Type type, object owner, Delegate callback)
        {
            if (!_cuePerformers.TryGetValue(type, out List<Registration> list))
            {
                list = new List<Registration>();
                _cuePerformers[type] = list;
            }

            Registration registration = new Registration
            {
                Bus = this,
                Role = RegistrationRole.CuePerformer,
                EventType = type,
                Owner = owner,
                Callback = callback,
                Active = true,
            };
            list.Add(registration);
            return new SubscriptionHandle(registration);
        }

        /// <summary>
        /// Starts one cue performer synchronously and arranges for <paramref name="onDone"/> to be called exactly once when it finishes —
        /// whether it completes, faults (isolated: logged, still counts as done), is cancelled by <paramref name="cancellation"/>, or is
        /// unregistered mid-cue. Dispatches on the performer's concrete delegate type.
        /// </summary>
        private void StartCuePerformer<T>(Registration registration, T cue, CancellationToken cancellation, Action onDone) where T : ICue
        {
            bool done = false;
            CancellationTokenRegistration tokenRegistration = default;

            // Called on every terminal path (completion, fault, cancel, unregister); idempotent. Unhooks this performer's cancellation and
            // disposes its token registration, so every path cleans up exactly once.
            Action resolve = null;
            resolve = () =>
            {
                if (done)
                    return;
                done = true;
                registration.PendingCancellations?.Remove(resolve);
                tokenRegistration.Dispose();
                onDone();
            };

            // Never-hangs: unregistering this performer mid-cue fires resolve, so the cue's when-all doesn't wait on a gone performer.
            (registration.PendingCancellations ??= new List<Action>()).Add(resolve);
            // Cancellation fan-out: the cue's token resolves this performer's slot too (the cue as a whole is cancelled at the send level).
            tokenRegistration = cancellation.CanBeCanceled ? cancellation.Register(resolve) : default;

            // Registering the token may have fired resolve synchronously (already-cancelled token), before the assignment above captured the
            // registration — so dispose it here and don't start the performer.
            if (done)
            {
                tokenRegistration.Dispose();
                return;
            }

            switch (registration.Callback)
            {
                // Instant: runs fully synchronously, then completes.
                case Action<T> instant:
                    try
                    {
                        instant.Invoke(cue);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, registration.Owner as UnityEngine.Object);
                    }
                    resolve();
                    break;

                // Durative: starts synchronously and returns an awaitable; the cue waits for it.
                case Func<T, Awaitable> durative:
                    Awaitable inner;
                    try
                    {
                        inner = durative.Invoke(cue);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, registration.Owner as UnityEngine.Object);
                        resolve();
                        break;
                    }
                    Pump(inner);
                    break;

                // Callback-style: runs synchronously and is handed a `done` callback that resolves this performer when invoked.
                case CuePerformerDelegate<T> callback:
                    try
                    {
                        callback.Invoke(cue, resolve);
                    }
                    catch (Exception exception)
                    {
                        // A throw during the synchronous stretch is isolated and completes the performer, exactly like the other shapes.
                        Debug.LogException(exception, registration.Owner as UnityEngine.Object);
                        resolve();
                    }
                    // No resolve() on the happy path: completion is when the performer calls `done` (or is cancelled/unregistered).
                    break;
            }

            // Awaits a durative performer's awaitable off the bus, isolating faults and resolving the performer's slot when it settles.
            async void Pump(Awaitable awaitable)
            {
                try
                {
                    await awaitable;
                }
                catch (OperationCanceledException)
                {
                    // The performer honoured a cancellation; its slot still resolves so the cue's when-all proceeds.
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, registration.Owner as UnityEngine.Object);
                }
                finally
                {
                    resolve();
                }
            }
        }

        /// <summary>
        /// Marks a registration inactive and removes it (immediately when no dispatch is in progress), otherwise deferred until the
        /// outermost dispatch ends.
        /// </summary>
        internal void Remove(Registration registration)
        {
            if (registration == null || !registration.Active)
                return;

            registration.Active = false;
            // Never-hangs: resolve anyone still awaiting this handler now that it's gone.
            CancelInFlight(registration);
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

            if (registration.Role == RegistrationRole.CommandHandler || registration.Role == RegistrationRole.RequestHandler)
            {
                // Same slot guard as providers: only drop it if this exact handler still occupies it.
                if (_handlers.TryGetValue(registration.EventType, out Registration current) && current == registration)
                    _handlers.Remove(registration.EventType);
                return;
            }

            if (registration.Role == RegistrationRole.CuePerformer)
            {
                if (_cuePerformers.TryGetValue(registration.EventType, out List<Registration> performers))
                {
                    performers.Remove(registration);
                    if (performers.Count == 0)
                        _cuePerformers.Remove(registration.EventType);
                }
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

        /// <summary>
        /// Resolves (as cancelled) every async dispatch still awaiting this handler, called when the handler is unregistered mid-flight so
        /// those callers don't hang. A no-op for handlers with nothing in flight and for non-handler roles.
        /// </summary>
        private static void CancelInFlight(Registration registration)
        {
            List<Action> pending = registration.PendingCancellations;
            if (pending == null || pending.Count == 0)
                return;

            // Snapshot then clear: each callback also removes itself, so it must not mutate the list we're walking.
            Action[] callbacks = pending.ToArray();
            pending.Clear();
            foreach (Action cancel in callbacks)
                cancel();
        }

        /// <summary>
        /// Bridges a durative command handler's <see cref="Awaitable"/> into one the bus controls, so the caller's wait resolves on
        /// completion, on cancellation, on a handler fault, or on the handler being unregistered mid-flight — never hanging.
        /// </summary>
        private Awaitable BridgeAwaitable(Func<Awaitable> invoke, Registration registration, CancellationToken cancellation)
        {
            AwaitableCompletionSource source = new AwaitableCompletionSource();
            bool resolved = false;

            Action cancel = null;
            cancel = () =>
            {
                if (resolved)
                    return;
                resolved = true;
                source.TrySetCanceled();
                registration.PendingCancellations?.Remove(cancel);
            };
            (registration.PendingCancellations ??= new List<Action>()).Add(cancel);

            CancellationTokenRegistration tokenRegistration = cancellation.CanBeCanceled ? cancellation.Register(cancel) : default;

            // Registering may have fired the callback synchronously (token already cancelled) — don't invoke the handler if so.
            if (resolved)
            {
                tokenRegistration.Dispose();
                return source.Awaitable;
            }

            Awaitable inner;
            try
            {
                inner = invoke();
            }
            catch (Exception exception)
            {
                if (!resolved)
                {
                    resolved = true;
                    source.TrySetException(exception);
                }
                registration.PendingCancellations?.Remove(cancel);
                tokenRegistration.Dispose();
                return source.Awaitable;
            }

            Pump();
            return source.Awaitable;

            async void Pump()
            {
                try
                {
                    await inner;
                    if (!resolved)
                    {
                        resolved = true;
                        source.TrySetResult();
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!resolved)
                    {
                        resolved = true;
                        source.TrySetCanceled();
                    }
                }
                catch (Exception exception)
                {
                    if (!resolved)
                    {
                        resolved = true;
                        source.TrySetException(exception);
                    }
                }
                finally
                {
                    registration.PendingCancellations?.Remove(cancel);
                    tokenRegistration.Dispose();
                }
            }
        }

        /// <summary>
        /// Bridges a durative command/request handler's <see cref="Awaitable{TResult}"/> into one the bus controls (see
        /// <see cref="BridgeAwaitable(Func{Awaitable}, Registration, CancellationToken)"/>), carrying the handler's result.
        /// </summary>
        private Awaitable<TResult> BridgeAwaitable<TResult>(Func<Awaitable<TResult>> invoke, Registration registration, CancellationToken cancellation)
        {
            AwaitableCompletionSource<TResult> source = new AwaitableCompletionSource<TResult>();
            bool resolved = false;

            Action cancel = null;
            cancel = () =>
            {
                if (resolved)
                    return;
                resolved = true;
                source.TrySetCanceled();
                registration.PendingCancellations?.Remove(cancel);
            };
            (registration.PendingCancellations ??= new List<Action>()).Add(cancel);

            CancellationTokenRegistration tokenRegistration = cancellation.CanBeCanceled ? cancellation.Register(cancel) : default;

            if (resolved)
            {
                tokenRegistration.Dispose();
                return source.Awaitable;
            }

            Awaitable<TResult> inner;
            try
            {
                inner = invoke();
            }
            catch (Exception exception)
            {
                if (!resolved)
                {
                    resolved = true;
                    source.TrySetException(exception);
                }
                registration.PendingCancellations?.Remove(cancel);
                tokenRegistration.Dispose();
                return source.Awaitable;
            }

            Pump();
            return source.Awaitable;

            async void Pump()
            {
                try
                {
                    TResult result = await inner;
                    if (!resolved)
                    {
                        resolved = true;
                        source.TrySetResult(result);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!resolved)
                    {
                        resolved = true;
                        source.TrySetCanceled();
                    }
                }
                catch (Exception exception)
                {
                    if (!resolved)
                    {
                        resolved = true;
                        source.TrySetException(exception);
                    }
                }
                finally
                {
                    registration.PendingCancellations?.Remove(cancel);
                    tokenRegistration.Dispose();
                }
            }
        }

        /// <summary>An already-completed void awaitable (a sync handler run through an async verb, or a silent unhandled void order).</summary>
        private static Awaitable CompletedAwaitable()
        {
            AwaitableCompletionSource source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }

        /// <summary>An already-faulted void awaitable, carrying <paramref name="exception"/> to the awaiter.</summary>
        private static Awaitable FaultedAwaitable(Exception exception)
        {
            AwaitableCompletionSource source = new AwaitableCompletionSource();
            source.SetException(exception);
            return source.Awaitable;
        }

        /// <summary>An already-cancelled void awaitable (the caller's token was already cancelled at call time).</summary>
        private static Awaitable CanceledAwaitable()
        {
            AwaitableCompletionSource source = new AwaitableCompletionSource();
            source.SetCanceled();
            return source.Awaitable;
        }

        /// <summary>An already-completed awaitable carrying <paramref name="value"/> (a sync handler run through an async verb).</summary>
        private static Awaitable<TResult> CompletedAwaitable<TResult>(TResult value)
        {
            AwaitableCompletionSource<TResult> source = new AwaitableCompletionSource<TResult>();
            source.SetResult(value);
            return source.Awaitable;
        }

        /// <summary>An already-faulted awaitable, carrying <paramref name="exception"/> to the awaiter (thrown handler, or none registered).</summary>
        private static Awaitable<TResult> FaultedAwaitable<TResult>(Exception exception)
        {
            AwaitableCompletionSource<TResult> source = new AwaitableCompletionSource<TResult>();
            source.SetException(exception);
            return source.Awaitable;
        }

        /// <summary>An already-cancelled awaitable (the caller's token was already cancelled at call time).</summary>
        private static Awaitable<TResult> CanceledAwaitable<TResult>()
        {
            AwaitableCompletionSource<TResult> source = new AwaitableCompletionSource<TResult>();
            source.SetCanceled();
            return source.Awaitable;
        }

        #endregion

    }

}
