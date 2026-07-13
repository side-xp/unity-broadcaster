using System.Collections.Generic;
using System.Text;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// An immutable, stringified picture of an event's payload, taken by the monitor at send time. It never holds a live reference to the
    /// payload: mutable class payloads can't lie after the fact, and no dead object is kept alive by the monitor.
    /// </summary>
    /// <remarks>
    /// Field values are captured one level deep (a field that is itself a complex object is shown by its own <c>ToString()</c>, or just
    /// its type name), never expanded further. Strings, collections and long values are truncated to hard caps. Only produced in the
    /// editor and development builds; release builds never produce one.
    /// </remarks>
    public sealed class PayloadSnapshot
    {

        /// <summary>
        /// The name of the event type this snapshot is of.
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        /// A one-line summary from the event's own <c>ToString()</c> override, or <c>null</c> when the type doesn't override it (the
        /// default <see cref="object"/>/<see cref="System.ValueType"/> implementations are never captured, they're just noise).
        /// </summary>
        public string Summary { get; }

        /// <summary>
        /// The captured public fields and properties, in reflected order. Empty when the payload has no public members, or when the event
        /// opted out of capture with <c>[Broadcast(OmitSnapshot = true)]</c>. Never <c>null</c>.
        /// </summary>
        public IReadOnlyList<PayloadField> Fields { get; }

        /// <inheritdoc cref="PayloadSnapshot"/>
        public PayloadSnapshot(string typeName, string summary, IReadOnlyList<PayloadField> fields)
        {
            TypeName = typeName;
            Summary = summary;
            Fields = fields ?? System.Array.Empty<PayloadField>();
        }

        /// <summary>
        /// A compact one-line rendering of the snapshot, eg. <c>HealthChanged { unit = "Hero (Unit)", value = 42 }</c>. Uses
        /// <see cref="Summary"/> as the body when the type overrides <c>ToString()</c>, otherwise lists the captured fields.
        /// </summary>
        public override string ToString()
        {
            if (Summary != null)
                return $"{TypeName} ({Summary})";

            if (Fields.Count == 0)
                return TypeName;

            StringBuilder builder = new StringBuilder(TypeName);
            builder.Append(" { ");
            for (int i = 0; i < Fields.Count; i++)
            {
                if (i > 0)
                    builder.Append(", ");
                builder.Append(Fields[i].Name).Append(" = ").Append(Fields[i].Value);
            }
            builder.Append(" }");
            return builder.ToString();
        }

    }

    /// <summary>
    /// One captured member of a payload: the name of a public field or property, and its already-stringified, truncated value.
    /// </summary>
    public readonly struct PayloadField
    {

        /// <summary>
        /// The field or property name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The stringified value: quoted for strings, name + type for Unity objects, count + elements for collections, <c>"null"</c> for
        /// nulls, and <c>"&lt;error&gt;"</c> if reading or stringifying it threw. Truncated to a hard cap.
        /// </summary>
        public string Value { get; }

        /// <inheritdoc cref="PayloadField"/>
        public PayloadField(string name, string value)
        {
            Name = name;
            Value = value;
        }

    }

}
