namespace SideXP.Broadcaster
{

    /// <summary>
    /// The four kinds of event the bus dispatches, reported on monitor spans and registration changes.
    /// </summary>
    public enum EventKind
    {
        /// <summary>A fire-and-forget notification with 0..N listeners.</summary>
        Signal,

        /// <summary>An awaitable "everyone, do your part" with 0..N performers.</summary>
        Cue,

        /// <summary>An order performed by exactly one handler.</summary>
        Command,

        /// <summary>A question answered by exactly one handler.</summary>
        Request,
    }

    /// <summary>
    /// What a registration does on the bus, reported on the monitor's registration-change hooks and per-callback sub-spans. Derived from
    /// the registration's concrete kind at report time.
    /// </summary>
    public enum RegistrationRole
    {
        /// <summary>A signal listener, invoked on each emit.</summary>
        SignalListener,

        /// <summary>A state provider, answering "what is the current value?".</summary>
        Provider,

        /// <summary>The single handler that performs a command.</summary>
        CommandHandler,

        /// <summary>The single handler that answers a request.</summary>
        RequestHandler,

        /// <summary>A cue performer.</summary>
        CuePerformer,
    }

    /// <summary>
    /// How a dispatch or a single invoked callback finished.
    /// </summary>
    public enum DispatchOutcome
    {
        /// <summary>Ran to completion.</summary>
        Completed,

        /// <summary>Threw; the exception was isolated (and, for a callback, logged).</summary>
        Faulted,

        /// <summary>Was cancelled (a token fired, or the callback was unregistered before it finished).</summary>
        Cancelled,
    }

    /// <summary>
    /// Why a registration was added or removed, reported on the monitor's registration-change hooks.
    /// </summary>
    public enum RegistrationChangeReason
    {
        /// <summary>The registration was just added.</summary>
        Registered,

        /// <summary>A signal listener was removed by an explicit <c>Unsubscribe</c>.</summary>
        Unsubscribed,

        /// <summary>The registration's <see cref="SubscriptionHandle"/> was disposed.</summary>
        Disposed,

        /// <summary>The registration was removed by an <c>UnsubscribeAll</c> on its owner.</summary>
        RemovedByOwner,

        /// <summary>A single-slot registration (provider or handler) was superseded by a replacement.</summary>
        Replaced,

        /// <summary>The registration was removed by a <c>Clear</c> or <c>Clear&lt;T&gt;</c>.</summary>
        Cleared,
    }

}
