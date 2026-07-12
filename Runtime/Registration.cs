using System;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// What a <see cref="Registration"/> represents. Decides which store holds it and how it is removed. More roles
    /// (command/request handler, cue performer) are added as those kinds land.
    /// </summary>
    internal enum RegistrationRole
    {
        /// <summary>A signal listener, held in the per-type listener list.</summary>
        SignalListener,

        /// <summary>A state provider, single per type, answering "what is the current value?".</summary>
        Provider,

        /// <summary>A command handler, single per type, that performs the ordered action.</summary>
        CommandHandler,

        /// <summary>A request handler, single per type, that answers the question.</summary>
        RequestHandler,
    }

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
        /// What this registration is. Decides which store holds it and how it's removed.
        /// </summary>
        public RegistrationRole Role;

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
