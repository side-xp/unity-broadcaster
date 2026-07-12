#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !BROADCASTER_MONITOR_OFF
#define BROADCASTER_MONITOR
#endif

#if BROADCASTER_MONITOR
using System;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// The monitor's description of a registration being added or removed, handed to the <c>Registered</c> and <c>Unregistered</c> hooks.
    /// </summary>
    /// <remarks>Only produced in the editor and development builds; stripped from release.</remarks>
    public readonly struct RegistrationInfo
    {

        /// <summary>The kind of event the registration is for.</summary>
        public EventKind Kind { get; }

        /// <summary>What the registration does.</summary>
        public RegistrationRole Role { get; }

        /// <summary>The exact event type the registration is keyed on.</summary>
        public Type EventType { get; }

        /// <summary>The owner that made the registration.</summary>
        public object Owner { get; }

        /// <summary>The frame the change happened on.</summary>
        public int Frame { get; }

        /// <summary>Why the change happened.</summary>
        public RegistrationChangeReason Reason { get; }

        /// <inheritdoc cref="RegistrationInfo"/>
        public RegistrationInfo(EventKind kind, RegistrationRole role, Type eventType, object owner, int frame, RegistrationChangeReason reason)
        {
            Kind = kind;
            Role = role;
            EventType = eventType;
            Owner = owner;
            Frame = frame;
            Reason = reason;
        }

    }

}
#endif
