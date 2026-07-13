using System;
using System.Collections.Generic;
using System.Reflection;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// A mutable, editable instance of an event type, used by the Events window's emit box: it holds a live payload the window edits
    /// field-by-field, then hands to the bus. It reads the same members the monitor snapshots (public instance fields), so what you edit is
    /// what a listener would receive. The instance is kept boxed as <see cref="object"/> so a struct payload is mutated in place through
    /// reflection.
    /// </summary>
    public sealed class EventDraft
    {

        private readonly FieldInfo[] _fields;

        /// <summary>The event type this draft builds an instance of.</summary>
        public Type EventType { get; }

        /// <summary>
        /// The current payload, boxed. <c>null</c> only when the type couldn't be instantiated (see <see cref="CanInstantiate"/>). Editing a
        /// field mutates this instance in place.
        /// </summary>
        public object Instance { get; private set; }

        /// <summary>Whether an instance of the type could be created (a struct always can; a class needs a public parameterless constructor).</summary>
        public bool CanInstantiate => Instance != null;

        /// <summary>The public instance fields the emit box exposes for editing, in declaration order.</summary>
        public IReadOnlyList<FieldInfo> Fields => _fields;

        /// <inheritdoc cref="EventDraft"/>
        /// <param name="eventType">The concrete event type to draft.</param>
        public EventDraft(Type eventType)
        {
            EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
            _fields = eventType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Instance = TryCreate(eventType);
        }

        /// <summary>
        /// Resets the payload to a fresh default instance, discarding any edits.
        /// </summary>
        public void Reset()
        {
            Instance = TryCreate(EventType);
        }

        /// <summary>
        /// Reads a field's current value off the payload.
        /// </summary>
        public object GetValue(FieldInfo field)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            return Instance == null ? null : field.GetValue(Instance);
        }

        /// <summary>
        /// Writes a field's value on the payload (mutating the boxed instance in place, so it works on a struct payload too).
        /// </summary>
        public void SetValue(FieldInfo field, object value)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (Instance == null)
                return;

            // A boxed struct is mutated in place by SetValue, so the edited value survives on this same Instance.
            object boxed = Instance;
            field.SetValue(boxed, value);
            Instance = boxed;
        }

        /// <summary>
        /// Creates a default instance of a type, or returns <c>null</c> when it has no accessible parameterless construction path (a class
        /// without a public parameterless constructor).
        /// </summary>
        private static object TryCreate(Type type)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch (Exception)
            {
                return null;
            }
        }

    }

}
