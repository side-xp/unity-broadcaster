using System;
using System.Collections.Generic;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// Internal record of a single registration on an <see cref="EventBus"/>. Each concrete subclass matches one bus store, so a
    /// registration self-documents what it is and what removing it means.
    /// </summary>
    internal abstract class Registration
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

        /// <summary>
        /// Resolves every async dispatch still awaiting this registration, so removing it mid-flight releases those callers instead of
        /// leaving them hanging forever. A no-op for the kinds that never track in-flight work (see <see cref="InFlightRegistration"/>).
        /// </summary>
        public virtual void CancelInFlight() { }

    }

    /// <summary>
    /// A registration whose invocations can outlive the call that started them (an async handler mid-order/ask, a performer mid-cue), so
    /// it tracks the in-flight waits that must be resolved if it's removed before they complete.
    /// </summary>
    internal abstract class InFlightRegistration : Registration
    {

        /// <summary>
        /// Cancellation callbacks for the async dispatches currently awaiting this registration. Lazily created; each in-flight dispatch
        /// adds its callback here and removes it on completion.
        /// </summary>
        public List<Action> PendingCancellations;

        /// <inheritdoc cref="Registration.CancelInFlight"/>
        public sealed override void CancelInFlight()
        {
            if (PendingCancellations == null || PendingCancellations.Count == 0)
                return;

            // Snapshot then clear: each callback also removes itself, so it must not mutate the list we're walking.
            Action[] callbacks = PendingCancellations.ToArray();
            PendingCancellations.Clear();
            foreach (Action cancel in callbacks)
                cancel();
        }

    }

    /// <summary>
    /// A signal listener, held in the bus's per-type listener list (0..N per signal type) and invoked synchronously on each emit.
    /// </summary>
    internal sealed class ListenerRegistration : Registration { }

    /// <summary>
    /// A state provider, at most one per signal type, answering "what is the current value?" for init pulls and on-demand reads.
    /// </summary>
    internal sealed class ProviderRegistration : Registration { }

    /// <summary>
    /// A command or request handler, at most one per event type. Both kinds share the bus's single-handler store (a type is only ever a
    /// command <i>or</i> a request, never both) and the same machinery.
    /// </summary>
    internal sealed class HandlerRegistration : InFlightRegistration
    {

        /// <summary>
        /// True when this handler answers a request, false when it performs a command. Purely informational (both kinds share the same
        /// store and machinery): used to word diagnostics, and for tooling to report the role.
        /// </summary>
        public bool IsRequest;

        /// <summary>
        /// True when this handler returns an <see cref="UnityEngine.Awaitable"/> (registered through an async <c>Obey</c>/<c>Answer</c>
        /// overload). The synchronous verbs use this to reject a handler they can't complete in one call.
        /// </summary>
        public bool Async;

    }

    /// <summary>
    /// A cue performer, held in the bus's per-type performer list (0..N per cue type, like signal listeners); a cue's send starts every
    /// performer synchronously and awaits their completion (when-all).
    /// </summary>
    internal sealed class PerformerRegistration : InFlightRegistration { }

}
