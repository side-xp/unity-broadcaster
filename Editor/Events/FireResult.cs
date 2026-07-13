using System;

using UnityEngine;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// The outcome of firing an event from the Events window, in a shape the window can render inline: a one-line summary plus whatever the
    /// kind produces (an acknowledgement, a returned value, or a cue's still-running completion).
    /// </summary>
    public sealed class FireResult
    {

        /// <summary>The kind that was fired, so the window knows how to present the rest.</summary>
        public EventKind Kind { get; }

        /// <summary>A short, human-readable line describing what happened, shown next to the fire button.</summary>
        public string Summary { get; set; }

        /// <summary>True when firing threw (an unhandled valued command or request, a synchronous call on an async handler, or a handler that threw).</summary>
        public bool Faulted { get; }

        /// <summary>The exception that faulted the fire, when <see cref="Faulted"/> is true.</summary>
        public Exception Exception { get; }

        /// <summary>Whether <see cref="Value"/> carries a produced value (a void command's ack, or a valued command / request result).</summary>
        public bool HasValue { get; }

        /// <summary>The produced value: a <see cref="bool"/> acknowledgement for a void command, or the result of a valued command / request.</summary>
        public object Value { get; }

        /// <summary>
        /// A cue's completion, still in flight when the window receives the result (null for the synchronous kinds). The window awaits it to
        /// show when the cue's performers have all finished.
        /// </summary>
        public Awaitable Completion { get; }

        private FireResult(EventKind kind, string summary, bool faulted, Exception exception, bool hasValue, object value, Awaitable completion)
        {
            Kind = kind;
            Summary = summary;
            Faulted = faulted;
            Exception = exception;
            HasValue = hasValue;
            Value = value;
            Completion = completion;
        }

        /// <summary>A signal was emitted, or any kind ran with nothing to report but success.</summary>
        public static FireResult Done(EventKind kind, string summary) => new FireResult(kind, summary, false, null, false, null, null);

        /// <summary>A kind produced a value (a void command's ack, or a valued command / request result).</summary>
        public static FireResult Produced(EventKind kind, object value, string summary) => new FireResult(kind, summary, false, null, true, value, null);

        /// <summary>A cue was sent and its performers are still finishing; <paramref name="completion"/> resolves when they're done.</summary>
        public static FireResult Running(Awaitable completion, string summary) => new FireResult(EventKind.Cue, summary, false, null, false, null, completion);

        /// <summary>Firing threw.</summary>
        public static FireResult Fault(EventKind kind, Exception exception) => new FireResult(kind, exception.Message, true, exception, false, null, null);

    }

}
