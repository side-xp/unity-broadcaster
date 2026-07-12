#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !BROADCASTER_MONITOR_OFF
#define BROADCASTER_MONITOR
#endif

#if BROADCASTER_MONITOR
using System;
using System.Collections.Generic;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// The monitor's record of one dispatch (an emit, a cue, an order or an ask): its identity, what was sent, when it began and ended,
    /// how it finished, the callbacks it invoked, and the dispatch it happened inside of. A synchronous dispatch opens and closes within a
    /// single stack; a cue or async order/ask stays open across frames until its work finishes.
    /// </summary>
    /// <remarks>
    /// The same instance is handed to <c>SpanBegan</c> and later <c>SpanEnded</c>: a recorder can hold it and read the end fields once
    /// <see cref="IsComplete"/> is set. Only produced in the editor and development builds; stripped from release.
    /// </remarks>
    public sealed class DispatchSpan
    {

        private readonly List<ListenerSpan> _listeners = new List<ListenerSpan>();

        /// <summary>A bus-unique, increasing id for this dispatch.</summary>
        public long Id { get; }

        /// <summary>The kind of event dispatched.</summary>
        public EventKind Kind { get; }

        /// <summary>The exact event type dispatched.</summary>
        public Type EventType { get; }

        /// <summary>A stringified snapshot of the payload, taken at send time. Never a live reference.</summary>
        public PayloadSnapshot Payload { get; }

        /// <summary>
        /// The dispatch this one happened inside of, or <c>null</c> for a top-level dispatch. Guaranteed for synchronous cascades (a
        /// listener that emits); best-effort across async continuations, where temporal containment still holds even without a hard link.
        /// </summary>
        public DispatchSpan Parent { get; }

        /// <summary>The frame the dispatch began on.</summary>
        public int BeginFrame { get; }

        /// <summary>The realtime (seconds since startup) the dispatch began at.</summary>
        public double BeginTime { get; }

        /// <summary>The frame the dispatch ended on. Meaningful once <see cref="IsComplete"/> is true.</summary>
        public int EndFrame { get; internal set; }

        /// <summary>The realtime (seconds since startup) the dispatch ended at. Meaningful once <see cref="IsComplete"/> is true.</summary>
        public double EndTime { get; internal set; }

        /// <summary>How the dispatch finished. Meaningful once <see cref="IsComplete"/> is true.</summary>
        public DispatchOutcome Outcome { get; internal set; }

        /// <summary>Whether the dispatch has finished (its end fields and <see cref="Outcome"/> are set).</summary>
        public bool IsComplete { get; internal set; }

        /// <summary>The callbacks invoked by this dispatch, one sub-span each, in invocation order.</summary>
        public IReadOnlyList<ListenerSpan> Listeners => _listeners;

        internal DispatchSpan(long id, EventKind kind, Type eventType, PayloadSnapshot payload, DispatchSpan parent, int beginFrame, double beginTime)
        {
            Id = id;
            Kind = kind;
            EventType = eventType;
            Payload = payload;
            Parent = parent;
            BeginFrame = beginFrame;
            BeginTime = beginTime;
        }

        /// <summary>Adds a sub-span for an invoked callback (used by the bus as it starts each one).</summary>
        internal void Add(ListenerSpan listener)
        {
            _listeners.Add(listener);
        }
    }

    /// <summary>
    /// The monitor's record of one callback invoked by a <see cref="DispatchSpan"/>: who owned it, what role it played, when it ran, and
    /// how it finished. "Listener" is the generic word for any invoked callback (a signal listener, a cue performer, or a handler).
    /// </summary>
    /// <remarks>
    /// The same instance is handed to <c>ListenerBegan</c> and later <c>ListenerEnded</c>. A durative cue performer stays open until its
    /// work finishes, so its sub-span can end well after the dispatch's synchronous start. Only produced in the editor and development
    /// builds; stripped from release.
    /// </remarks>
    public sealed class ListenerSpan
    {

        /// <summary>The dispatch this callback was invoked by.</summary>
        public DispatchSpan Span { get; }

        /// <summary>The owner that registered the callback.</summary>
        public object Owner { get; }

        /// <summary>What the callback was (listener, performer, or handler).</summary>
        public RegistrationRole Role { get; }

        /// <summary>The frame the callback started on.</summary>
        public int BeginFrame { get; }

        /// <summary>The realtime (seconds since startup) the callback started at.</summary>
        public double BeginTime { get; }

        /// <summary>The frame the callback finished on. Meaningful once <see cref="IsComplete"/> is true.</summary>
        public int EndFrame { get; internal set; }

        /// <summary>The realtime (seconds since startup) the callback finished at. Meaningful once <see cref="IsComplete"/> is true.</summary>
        public double EndTime { get; internal set; }

        /// <summary>How the callback finished. Meaningful once <see cref="IsComplete"/> is true.</summary>
        public DispatchOutcome Outcome { get; internal set; }

        /// <summary>The exception the callback threw, when <see cref="Outcome"/> is <see cref="DispatchOutcome.Faulted"/>; otherwise <c>null</c>.</summary>
        public Exception Exception { get; internal set; }

        /// <summary>Whether the callback has finished (its end fields and <see cref="Outcome"/> are set).</summary>
        public bool IsComplete { get; internal set; }

        internal ListenerSpan(DispatchSpan span, object owner, RegistrationRole role, int beginFrame, double beginTime)
        {
            Span = span;
            Owner = owner;
            Role = role;
            BeginFrame = beginFrame;
            BeginTime = beginTime;
        }

    }

}
#endif
