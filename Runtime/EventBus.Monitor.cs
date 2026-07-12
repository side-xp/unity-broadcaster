#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !BROADCASTER_MONITOR_OFF
#define BROADCASTER_MONITOR
#endif

#if BROADCASTER_MONITOR
using System;

using UnityEngine;

namespace SideXP.Broadcaster
{

    // The bus's observability surface: hooks that report every registration change and every dispatch as it happens, plus the helpers the
    // dispatch code calls to raise them. Compiled into the editor and development builds only; stripped from release. Hooks are pure
    // notifications. They store nothing (retention is a consumer's concern), and creating a span costs nothing when no hook is attached.
    public sealed partial class EventBus
    {

        #region Hooks

        /// <summary>
        /// Raised when a registration is added to the bus (a listener, provider, performer or handler). Editor and development builds only.
        /// </summary>
        public event Action<RegistrationInfo> OnRegistered;

        /// <summary>
        /// Raised when a registration is removed from the bus, whatever the cause. Editor and development builds only.
        /// </summary>
        public event Action<RegistrationInfo> OnUnregistered;

        /// <summary>
        /// Raised when a dispatch begins, before any callback runs. The span's end fields aren't set yet. Editor and development builds only.
        /// </summary>
        public event Action<DispatchSpan> OnSpanBegan;

        /// <summary>
        /// Raised when a dispatch finishes, after its last callback. The span is complete. Editor and development builds only.
        /// </summary>
        public event Action<DispatchSpan> OnSpanEnded;

        /// <summary>
        /// Raised when a dispatch is about to invoke one of its callbacks. Editor and development builds only.
        /// </summary>
        public event Action<ListenerSpan> OnListenerBegan;

        /// <summary>
        /// Raised when a callback invoked by a dispatch finishes (completed, faulted or cancelled). Editor and development builds only.
        /// </summary>
        public event Action<ListenerSpan> OnListenerEnded;

        /// <summary>
        /// Raised when the bus is misused in a way it also logs or throws (an unhandled order, an unanswered ask, a duplicate handler or
        /// provider, a synchronous call on an async handler). Editor and development builds only.
        /// </summary>
        public event Action<Violation> OnViolation;

        #endregion


        #region Fields

        /// <summary>
        /// Next id handed to a span. Per-bus and increasing, so a fresh bus produces a predictable sequence.
        /// </summary>
        private long _nextSpanId = 1;

        /// <summary>
        /// The dispatch currently open on this thread, so a nested dispatch (a listener that emits) can record it as its parent. Saved and
        /// restored around each synchronous dispatch; captured at start for async ones.
        /// </summary>
        private DispatchSpan _currentSpan;

        #endregion


        #region Dispatch spans

        /// <summary>
        /// Whether any dispatch-span hook is attached. When none is, spans aren't created at all and the payload isn't snapshotted, so
        /// monitoring a bus nobody is watching costs nothing beyond the checks the dispatch code already makes.
        /// </summary>
        private bool MonitorDispatchActive => OnSpanBegan != null || OnSpanEnded != null || OnListenerBegan != null || OnListenerEnded != null;

        /// <summary>
        /// Opens a span for a dispatch and makes it the current one, snapshotting the payload. Returns <c>null</c> when no hook is attached,
        /// in which case every other monitor call for this dispatch is a no-op.
        /// </summary>
        private DispatchSpan MonitorBeginDispatch<T>(EventKind kind, Type eventType, T payload)
        {
            if (!MonitorDispatchActive)
                return null;

            return MonitorOpenSpan(kind, eventType, PayloadReflector.Capture(payload));
        }

        /// <summary>
        /// Like <see cref="MonitorBeginDispatch{T}"/> but for a payload whose static type is its marker interface (a valued order or an
        /// ask): the snapshot is taken off the runtime type so it reflects the concrete event, not the empty interface.
        /// </summary>
        private DispatchSpan MonitorBeginDispatchBoxed(EventKind kind, Type eventType, object payload)
        {
            if (!MonitorDispatchActive)
                return null;

            return MonitorOpenSpan(kind, eventType, PayloadReflector.CaptureBoxed(payload));
        }

        /// <summary>
        /// Creates the span, makes it the current one, and raises <c>OnSpanBegan</c>. Shared by the two begin-dispatch entry points.
        /// </summary>
        private DispatchSpan MonitorOpenSpan(EventKind kind, Type eventType, PayloadSnapshot snapshot)
        {
            DispatchSpan span = new DispatchSpan(_nextSpanId++, kind, eventType, snapshot, _currentSpan, Time.frameCount, Time.realtimeSinceStartupAsDouble);
            _currentSpan = span;
            Raise(OnSpanBegan, span);
            return span;
        }

        /// <summary>
        /// Opens and immediately closes a span for a dispatch that finished synchronously with nothing to invoke (a cancelled cue, a cue
        /// or order with no receiver). Handy where there is no callback loop to wrap.
        /// </summary>
        private void MonitorSyncDispatch<T>(EventKind kind, Type eventType, T payload, DispatchOutcome outcome)
        {
            DispatchSpan span = MonitorBeginDispatch(kind, eventType, payload);
            MonitorEndDispatch(span, outcome);
        }

        /// <summary>
        /// The runtime-type counterpart of <see cref="MonitorSyncDispatch{T}"/>, for an interface-typed payload (a valued order or an ask
        /// that resolved synchronously with nothing to invoke).
        /// </summary>
        private void MonitorSyncDispatchBoxed(EventKind kind, Type eventType, object payload, DispatchOutcome outcome)
        {
            DispatchSpan span = MonitorBeginDispatchBoxed(kind, eventType, payload);
            MonitorEndDispatch(span, outcome);
        }

        /// <summary>
        /// Closes a synchronous dispatch's span: restores the parent as the current one, then finishes the span.
        /// </summary>
        private void MonitorEndDispatch(DispatchSpan span, DispatchOutcome outcome)
        {
            if (span == null)
                return;

            MonitorRestoreAmbient(span);
            MonitorCompleteDispatch(span, outcome);
        }

        /// <summary>
        /// Restores a dispatch's parent as the current span, without finishing the span. An async dispatch (a cue, an async order/ask)
        /// calls this once its synchronous start returns, so a later unrelated dispatch isn't mis-parented to a span that is still open but
        /// no longer on the stack. Its own completion runs <see cref="MonitorCompleteDispatch"/> later.
        /// </summary>
        private void MonitorRestoreAmbient(DispatchSpan span)
        {
            if (span == null)
                return;

            _currentSpan = span.Parent;
        }

        /// <summary>
        /// Finishes a span (sets its end fields and outcome, raises <c>OnSpanEnded</c>) without touching the current span. Used both by
        /// the synchronous close and by an async dispatch resolving after its start returned.
        /// </summary>
        private void MonitorCompleteDispatch(DispatchSpan span, DispatchOutcome outcome)
        {
            if (span == null)
                return;

            span.EndFrame = Time.frameCount;
            span.EndTime = Time.realtimeSinceStartupAsDouble;
            span.Outcome = outcome;
            span.IsComplete = true;
            Raise(OnSpanEnded, span);
        }

        /// <summary>
        /// Opens a sub-span for a callback a dispatch is about to invoke. A no-op (returns <c>null</c>) when the dispatch isn't being
        /// monitored.
        /// </summary>
        private ListenerSpan MonitorBeginListener(DispatchSpan span, Registration registration)
        {
            if (span == null)
                return null;

            ListenerSpan listener = new ListenerSpan(span, registration.Owner, RoleOf(registration), Time.frameCount, Time.realtimeSinceStartupAsDouble);
            span.Add(listener);
            Raise(OnListenerBegan, listener);
            return listener;
        }

        /// <summary>
        /// Closes a callback's sub-span with how it finished (and the exception, when it faulted).
        /// </summary>
        private void MonitorEndListener(ListenerSpan listener, DispatchOutcome outcome, Exception exception)
        {
            if (listener == null)
                return;

            listener.EndFrame = Time.frameCount;
            listener.EndTime = Time.realtimeSinceStartupAsDouble;
            listener.Outcome = outcome;
            listener.Exception = exception;
            listener.IsComplete = true;
            Raise(OnListenerEnded, listener);
        }

        /// <summary>
        /// Invokes a synchronous handler under a sub-span and closes the dispatch's span: completed on success, faulted (then re-thrown so
        /// the caller still sees the exception) on a throw. Used by the sync handled verbs, whose single handler is a callback like any
        /// other, and which let a handler's exception propagate.
        /// </summary>
        private TResult MonitorInvokeHandler<TResult>(DispatchSpan span, HandlerRegistration registration, Func<TResult> invoke)
        {
            ListenerSpan listener = MonitorBeginListener(span, registration);
            try
            {
                TResult result = invoke();
                MonitorEndListener(listener, DispatchOutcome.Completed, null);
                MonitorEndDispatch(span, DispatchOutcome.Completed);
                return result;
            }
            catch (Exception exception)
            {
                MonitorEndListener(listener, DispatchOutcome.Faulted, exception);
                MonitorEndDispatch(span, DispatchOutcome.Faulted);
                throw;
            }
        }

        /// <summary>
        /// The void counterpart of <see cref="MonitorInvokeHandler{TResult}"/>, for a command handler that reports no outcome.
        /// </summary>
        private void MonitorInvokeHandlerVoid(DispatchSpan span, HandlerRegistration registration, Action invoke)
        {
            ListenerSpan listener = MonitorBeginListener(span, registration);
            try
            {
                invoke();
                MonitorEndListener(listener, DispatchOutcome.Completed, null);
                MonitorEndDispatch(span, DispatchOutcome.Completed);
            }
            catch (Exception exception)
            {
                MonitorEndListener(listener, DispatchOutcome.Faulted, exception);
                MonitorEndDispatch(span, DispatchOutcome.Faulted);
                throw;
            }
        }

        #endregion


        #region Registration changes

        /// <summary>
        /// Reports that a registration was just added.
        /// </summary>
        private void MonitorRegistered(Registration registration)
        {
            if (OnRegistered == null)
                return;

            Raise(OnRegistered, new RegistrationInfo(KindOf(registration), RoleOf(registration), registration.EventType, registration.Owner, Time.frameCount, RegistrationChangeReason.Registered));
        }

        /// <summary>
        /// Reports that a registration was removed, and why.
        /// </summary>
        private void MonitorUnregistered(Registration registration, RegistrationChangeReason reason)
        {
            if (OnUnregistered == null)
                return;

            Raise(OnUnregistered, new RegistrationInfo(KindOf(registration), RoleOf(registration), registration.EventType, registration.Owner, Time.frameCount, reason));
        }

        #endregion


        #region Violations

        /// <summary>
        /// Reports a registration-time violation (a duplicate handler or provider), carrying the owner of the rejected registration.
        /// </summary>
        private void MonitorViolation(ViolationKind kind, Type eventType, object owner)
        {
            if (OnViolation == null)
                return;

            Raise(OnViolation, new Violation(kind, eventType, owner, BuildViolationMessage(kind, eventType), Time.frameCount, null));
        }

        /// <summary>
        /// Reports a dispatch-time violation (an unhandled order, an unanswered ask, a sync call on an async handler), linked to the
        /// dispatch it happened in (which is <c>null</c> when no dispatch-span hook is attached).
        /// </summary>
        private void MonitorDispatchViolation(ViolationKind kind, Type eventType, DispatchSpan span)
        {
            if (OnViolation == null)
                return;

            Raise(OnViolation, new Violation(kind, eventType, null, BuildViolationMessage(kind, eventType), Time.frameCount, span));
        }

        /// <summary>
        /// A human-readable summary for a violation.
        /// </summary>
        private static string BuildViolationMessage(ViolationKind kind, Type eventType)
        {
            switch (kind)
            {
                case ViolationKind.UnhandledCommand:
                    return $"No handler is registered for command '{eventType.Name}'.";
                case ViolationKind.UnansweredRequest:
                    return $"No handler is registered to answer request '{eventType.Name}'.";
                case ViolationKind.MultipleHandlers:
                    return $"A second handler for '{eventType.Name}' was ignored; the first stays authoritative.";
                case ViolationKind.MultipleProviders:
                    return $"A second provider for '{eventType.Name}' was ignored; the first stays authoritative.";
                case ViolationKind.SyncCallOnAsyncHandler:
                    return $"The handler for '{eventType.Name}' is asynchronous and can't be run through a synchronous verb.";
                default:
                    return eventType.Name;
            }
        }

        #endregion


        #region Helpers

        /// <summary>
        /// The role a registration plays, derived from its concrete kind (a request handler and a command handler share a type, split by
        /// <see cref="HandlerRegistration.IsRequest"/>).
        /// </summary>
        private static RegistrationRole RoleOf(Registration registration)
        {
            switch (registration)
            {
                case ProviderRegistration _:
                    return RegistrationRole.Provider;
                case PerformerRegistration _:
                    return RegistrationRole.CuePerformer;
                case HandlerRegistration handler:
                    return handler.IsRequest ? RegistrationRole.RequestHandler : RegistrationRole.CommandHandler;
                default:
                    return RegistrationRole.SignalListener;
            }
        }

        /// <summary>
        /// The event kind a registration is for, derived from its concrete kind. Listeners and providers are both signal-typed.
        /// </summary>
        private static EventKind KindOf(Registration registration)
        {
            switch (registration)
            {
                case PerformerRegistration _:
                    return EventKind.Cue;
                case HandlerRegistration handler:
                    return handler.IsRequest ? EventKind.Request : EventKind.Command;
                default:
                    return EventKind.Signal;
            }
        }

        /// <summary>
        /// Raises a hook, isolating each subscriber: a hook consumer is user code running inside bus internals, so one that throws is
        /// logged and never stops the other consumers, the dispatch that raised it, or the hook stream. A consumer may freely re-enter the
        /// bus — the dispatch loops and bulk-removal paths already tolerate registrations changing mid-flight.
        /// </summary>
        private static void Raise<T>(Action<T> hook, T argument)
        {
            if (hook == null)
                return;

            foreach (Delegate subscriber in hook.GetInvocationList())
            {
                try
                {
                    ((Action<T>)subscriber).Invoke(argument);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        #endregion

    }

}
#endif
