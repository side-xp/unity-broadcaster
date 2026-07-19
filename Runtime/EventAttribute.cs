using System;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// Optional metadata for an <see cref="IEvent"/> type, consumed by tooling (the editor windows and the monitor).
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class EventAttribute : Attribute
    {

        /// <summary>
        /// The name the Events window shows for this event instead of the C# type name. Supports <c>/</c>-separated paths.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Human-readable description of the event, surfaced in the Events window and the monitor.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// When true, the monitor captures only this event's type name, not its field/property values. Set it on events whose payload is
        /// sensitive, huge, or expensive to stringify. Off by default (the full payload is captured).
        /// </summary>
        public bool OmitSnapshot { get; set; }

        /// <summary>
        /// When true, the Events window leaves this event out of its catalog by default.
        /// </summary>
        public bool Hidden { get; set; }

        /// <inheritdoc cref="EventAttribute"/>
        public EventAttribute() { }

        /// <inheritdoc cref="EventAttribute"/>
        /// <param name="description"><inheritdoc cref="Description" path="/summary"/></param>
        public EventAttribute(string description)
        {
            Description = description;
        }

    }

}
