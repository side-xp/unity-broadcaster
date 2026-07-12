#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !BROADCASTER_MONITOR_OFF
#define BROADCASTER_MONITOR
#endif

#if BROADCASTER_MONITOR
using System;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// A misuse of the bus the monitor reports, matching the diagnostics the bus also logs (or throws) in the editor and development
    /// builds.
    /// </summary>
    public enum ViolationKind
    {
        /// <summary>A command was ordered but no handler is registered for it.</summary>
        UnhandledCommand,

        /// <summary>A request was asked but no handler is registered to answer it.</summary>
        UnansweredRequest,

        /// <summary>A second handler was registered for an event type; the first stays authoritative and the new one is ignored.</summary>
        MultipleHandlers,

        /// <summary>A second provider was registered for a signal type; the first stays authoritative and the new one is ignored.</summary>
        MultipleProviders,

        /// <summary>A synchronous verb was used on a type whose handler is asynchronous (it can't be completed in one call).</summary>
        SyncCallOnAsyncHandler,
    }

    /// <summary>
    /// The monitor's structured report of a bus misuse, handed to the <c>OnViolation</c> hook. Produced in the editor and development
    /// builds only; stripped from release.
    /// </summary>
    public readonly struct Violation
    {

        /// <summary>What was misused.</summary>
        public ViolationKind Kind { get; }

        /// <summary>The event type involved.</summary>
        public Type EventType { get; }

        /// <summary>
        /// For a registration-time violation (a duplicate handler or provider), the owner of the rejected registration; otherwise
        /// <c>null</c>.
        /// </summary>
        public object Owner { get; }

        /// <summary>A human-readable summary.</summary>
        public string Message { get; }

        /// <summary>The frame the violation happened on.</summary>
        public int Frame { get; }

        /// <summary>
        /// For a dispatch-time violation (an unhandled order, an unanswered ask, a sync call on an async handler), the dispatch it
        /// happened in (when a dispatch-span hook is attached); otherwise <c>null</c>. Always <c>null</c> for registration-time violations.
        /// </summary>
        public DispatchSpan Span { get; }

        /// <inheritdoc cref="Violation"/>
        public Violation(ViolationKind kind, Type eventType, object owner, string message, int frame, DispatchSpan span)
        {
            Kind = kind;
            EventType = eventType;
            Owner = owner;
            Message = message;
            Frame = frame;
            Span = span;
        }

    }

}
#endif
