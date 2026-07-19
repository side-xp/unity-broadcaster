using System;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// Handle to a single registration on an <see cref="EventBus"/>, returned by every registration call. Disposing it unregisters that
    /// one registration.
    /// </summary>
    /// <remarks>
    /// Handles <i>complement</i> owner-based cleanup, they don't replace it: <see cref="EventBus.UnregisterAll(object)"/> in
    /// <c>OnDisable()</c> is the blessed one-liner, while a handle is for fine-grained lifetimes. Disposing is idempotent and safe on
    /// <c>default(SubscriptionHandle)</c>.
    /// </remarks>
    public readonly struct SubscriptionHandle : IDisposable
    {

        /// <summary>
        /// The registration this handle points to, or <c>null</c> for a <c>default</c> handle.
        /// </summary>
        private readonly Registration _registration;

        /// <inheritdoc cref="SubscriptionHandle"/>
        internal SubscriptionHandle(Registration registration)
        {
            _registration = registration;
        }

        /// <summary>
        /// Is the registration this handle points to still active (not yet unregistered)?<br/>
        /// Always false for a
        /// <c>default</c> handle.
        /// </summary>
        public bool IsActive => _registration != null && _registration.Active;

        /// <summary>
        /// Unregisters the registration this handle points to.<br/>
        /// Idempotent. A no-op on a <c>default</c> handle or once the registration has already been removed.
        /// </summary>
        public void Dispose()
        {
            if (_registration != null && _registration.Active)
                _registration.Bus.Remove(_registration, RegistrationChangeReason.Disposed);
        }

    }

}
