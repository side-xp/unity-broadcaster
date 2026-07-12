using System;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// Optional metadata for an <see cref="IEvent"/> type, consumed by tooling (the editor windows and the monitor).
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class BroadcastAttribute : Attribute
    {

        /// <summary>
        /// Human-readable description of the event, surfaced in the Events window and the monitor.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// When true, the monitor captures only this event's type name, not its field/property values. Set it on events whose payload is
        /// sensitive, huge, or expensive to stringify. Off by default (the full payload is captured).
        /// </summary>
        public bool OmitSnapshot { get; set; }

    }

}
