using System;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// Internal record of a single registration on an <see cref="EventBus"/>.
    /// </summary>
    internal sealed class Registration
    {

        /// <summary>
        /// The bus this registration belongs to (so a <see cref="SubscriptionHandle"/> can unregister it).
        /// </summary>
        public EventBus Bus;

        /// <summary>
        /// The exact event type this registration is keyed on.
        /// </summary>
        public Type EventType;

        /// <summary>
        /// The owner passed at registration time, used for bulk cleanup and diagnostics.
        /// </summary>
        public object Owner;

        /// <summary>
        /// The registered callback (for signals, an <see cref="Action{T}"/>). Stored as <see cref="Delegate"/> so the same record serves
        /// every kind. Compared with <see cref="Delegate.Equals(object)"/> on removal (never reference equality).
        /// </summary>
        public Delegate Callback;

        /// <summary>
        /// False once this registration has been removed.<br/>
        /// Inactive registrations are skipped mid-dispatch and physically dropped once no dispatch is in progress.
        /// </summary>
        public bool Active;

    }

}
