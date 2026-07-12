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

            PayloadSnapshot snapshot = PayloadReflector.Capture(payload);
            DispatchSpan span = new DispatchSpan(_nextSpanId++, kind, eventType, snapshot, _currentSpan, Time.frameCount, Time.realtimeSinceStartupAsDouble);
            _currentSpan = span;
            OnSpanBegan?.Invoke(span);
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
            OnSpanEnded?.Invoke(span);
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
            OnListenerBegan?.Invoke(listener);
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
            OnListenerEnded?.Invoke(listener);
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

            OnRegistered.Invoke(new RegistrationInfo(KindOf(registration), RoleOf(registration), registration.EventType, registration.Owner, Time.frameCount, RegistrationChangeReason.Registered));
        }

        /// <summary>
        /// Reports that a registration was removed, and why.
        /// </summary>
        private void MonitorUnregistered(Registration registration, RegistrationChangeReason reason)
        {
            if (OnUnregistered == null)
                return;

            OnUnregistered.Invoke(new RegistrationInfo(KindOf(registration), RoleOf(registration), registration.EventType, registration.Owner, Time.frameCount, reason));
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

        #endregion

    }

}
#endif
