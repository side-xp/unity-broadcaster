#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !BROADCASTER_MONITOR_OFF
#define BROADCASTER_MONITOR
#endif

#if BROADCASTER_MONITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// Turns an event payload into a <see cref="PayloadSnapshot"/>: a flat, stringified, truncated picture of its public fields and
    /// properties. The per-type plan (which members to read, whether the type opts out, whether it overrides <c>ToString()</c>) is
    /// computed once and cached, so repeated captures on the send path cost a static field read plus the member reads themselves.
    /// </summary>
    /// <remarks>
    /// The reflector never throws: a member whose getter or <c>ToString()</c> throws is rendered as <c>"&lt;error&gt;"</c> and capture
    /// continues. It never keeps a reference to the payload (every value is stringified in place). Capture is one level deep: a member
    /// that is itself a complex object is rendered by its own <c>ToString()</c> (or type name), never expanded field-by-field.
    /// </remarks>
    internal static class PayloadReflector
    {

        #region Caps

        /// <summary>
        /// Maximum length of any single stringified value (and of the summary line) before it's truncated with an ellipsis.
        /// </summary>
        private const int MaxValueLength = 128;

        /// <summary>Maximum number of elements shown for a collection value before the rest are elided.</summary>
        private const int MaxCollectionElements = 8;

        /// <summary>The character appended to mark that a value or collection was truncated.</summary>
        private const string Ellipsis = "…";

        #endregion


        #region Fields

        /// <summary>
        /// Caches, per arbitrary type, whether it declares its own <c>ToString()</c> (as opposed to inheriting <see cref="object"/>'s or
        /// <see cref="ValueType"/>'s). Used both for the summary line and to decide how nested complex values render. Main-thread only, so
        /// a plain dictionary is enough.
        /// </summary>
        private static readonly Dictionary<Type, bool> s_customToString = new Dictionary<Type, bool>();

        #endregion


        #region Public API

        /// <summary>
        /// Captures a snapshot of <paramref name="payload"/>. When the event opted out with <c>[Broadcast(OmitSnapshot = true)]</c>, the
        /// snapshot carries only the type name (no summary, no fields).
        /// </summary>
        /// <typeparam name="T">The exact event type. Members are read off this type, matching the bus's exact-type dispatch.</typeparam>
        /// <param name="payload">The event instance to snapshot.</param>
        /// <returns>An immutable snapshot; never <c>null</c>.</returns>
        public static PayloadSnapshot Capture<T>(T payload)
        {
            if (Plan<T>.Omit)
                return new PayloadSnapshot(Plan<T>.TypeName, null, Array.Empty<PayloadField>());

            // Box once so the cached FieldInfo/PropertyInfo can read a struct payload; happens only in monitor builds.
            object boxed = payload;

            string summary = Plan<T>.HasCustomToString ? Truncate(SafeToString(boxed)) : null;

            FieldInfo[] fields = Plan<T>.Fields;
            PropertyInfo[] properties = Plan<T>.Properties;
            if (fields.Length == 0 && properties.Length == 0)
                return new PayloadSnapshot(Plan<T>.TypeName, summary, Array.Empty<PayloadField>());

            List<PayloadField> captured = new List<PayloadField>(fields.Length + properties.Length);
            foreach (FieldInfo field in fields)
                captured.Add(new PayloadField(field.Name, FormatMember(() => field.GetValue(boxed))));
            foreach (PropertyInfo property in properties)
                captured.Add(new PayloadField(property.Name, FormatMember(() => property.GetValue(boxed))));

            return new PayloadSnapshot(Plan<T>.TypeName, summary, captured);
        }

        #endregion


        #region Formatting

        /// <summary>
        /// Reads a member value through <paramref name="read"/> and formats it, turning any exception from the getter or the formatting
        /// into <c>"&lt;error&gt;"</c> so a single bad member never aborts the whole snapshot.
        /// </summary>
        private static string FormatMember(Func<object> read)
        {
            try
            {
                return FormatValue(read());
            }
            catch
            {
                return "<error>";
            }
        }

        /// <summary>
        /// Formats a top-level member value: null, string (quoted), Unity object (name + type), primitive/enum (invariant), collection
        /// (count + elements), or any other object (its <c>ToString()</c> if overridden, else its type name).
        /// </summary>
        private static string FormatValue(object value)
        {
            if (value == null)
                return "null";
            if (value is string text)
                return Quote(Truncate(text));
            if (value is UnityEngine.Object unityObject)
                return FormatUnityObject(unityObject);

            Type type = value.GetType();
            if (type.IsEnum)
                return value.ToString();
            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            if (type.IsPrimitive)
                return value.ToString();

            // A collection expands to its elements (shallow); a plain object falls back to its ToString/type name.
            if (value is IEnumerable enumerable)
                return FormatCollection(enumerable);
            return FormatComplex(value, type);
        }

        /// <summary>
        /// Formats a value nested one level below the top (a collection element): like <see cref="FormatValue"/> but a nested collection
        /// is <b>not</b> expanded (it renders by its type name) so capture stays one level deep.
        /// </summary>
        private static string FormatElement(object value)
        {
            if (value == null)
                return "null";
            if (value is string text)
                return Quote(Truncate(text));
            if (value is UnityEngine.Object unityObject)
                return FormatUnityObject(unityObject);

            Type type = value.GetType();
            if (type.IsEnum)
                return value.ToString();
            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            if (type.IsPrimitive)
                return value.ToString();

            return FormatComplex(value, type);
        }

        /// <summary>
        /// Formats a Unity object as <c>name (Type)</c>. A destroyed (or otherwise fake-null) object renders as <c>null (Type)</c> without
        /// touching its members, which would throw.
        /// </summary>
        private static string FormatUnityObject(UnityEngine.Object unityObject)
        {
            // Unity's overloaded == treats destroyed objects as null; the managed wrapper is still alive so GetType() is safe.
            if (unityObject == null)
                return $"null ({unityObject.GetType().Name})";
            return $"{Truncate(unityObject.name)} ({unityObject.GetType().Name})";
        }

        /// <summary>
        /// Formats a collection as <c>[e1, e2, …] (count)</c>, showing at most <see cref="MaxCollectionElements"/> elements. The count is
        /// exact for an <see cref="ICollection"/>; otherwise it's the number seen, suffixed with <c>+</c> when more remained.
        /// </summary>
        private static string FormatCollection(IEnumerable enumerable)
        {
            int? knownCount = (enumerable as ICollection)?.Count;

            List<string> elements = new List<string>();
            bool more = false;
            IEnumerator enumerator = enumerable.GetEnumerator();
            try
            {
                while (enumerator.MoveNext())
                {
                    if (elements.Count >= MaxCollectionElements)
                    {
                        more = true;
                        break;
                    }
                    elements.Add(FormatElement(enumerator.Current));
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }

            string body = string.Join(", ", elements);
            if (more)
                body = body.Length == 0 ? Ellipsis : body + ", " + Ellipsis;

            string count = knownCount.HasValue
                ? knownCount.Value.ToString(CultureInfo.InvariantCulture)
                : (more ? elements.Count + "+" : elements.Count.ToString(CultureInfo.InvariantCulture));

            return $"[{body}] ({count})";
        }

        /// <summary>
        /// Formats a complex value that isn't a string, Unity object, primitive or collection: its own <c>ToString()</c> when it overrides
        /// it, otherwise its type name in braces (eg. <c>{Vector3}</c>), avoiding the default <c>ValueType</c>/<see cref="object"/> noise.
        /// </summary>
        private static string FormatComplex(object value, Type type)
        {
            if (HasCustomToString(type))
                return Truncate(SafeToString(value));
            return "{" + type.Name + "}";
        }

        #endregion


        #region Helpers

        /// <summary>
        /// Whether <paramref name="type"/> declares its own <c>ToString()</c> rather than inheriting the base implementations. Cached.
        /// </summary>
        private static bool HasCustomToString(Type type)
        {
            if (s_customToString.TryGetValue(type, out bool cached))
                return cached;

            bool custom = false;
            MethodInfo method = type.GetMethod("ToString", Type.EmptyTypes);
            if (method != null)
            {
                Type declaring = method.DeclaringType;
                custom = declaring != typeof(object) && declaring != typeof(ValueType);
            }

            s_customToString[type] = custom;
            return custom;
        }

        /// <summary>
        /// Calls <c>ToString()</c>, turning a throw or a null return into a safe placeholder so the reflector never propagates a failure.
        /// </summary>
        private static string SafeToString(object value)
        {
            try
            {
                return value.ToString() ?? "null";
            }
            catch
            {
                return "<error>";
            }
        }

        /// <summary>Wraps a string in double quotes.</summary>
        private static string Quote(string text)
        {
            return "\"" + text + "\"";
        }

        /// <summary>Truncates <paramref name="text"/> to <see cref="MaxValueLength"/>, marking the cut with an ellipsis.</summary>
        private static string Truncate(string text)
        {
            if (text == null)
                return "null";
            if (text.Length <= MaxValueLength)
                return text;
            return text.Substring(0, MaxValueLength) + Ellipsis;
        }

        #endregion


        #region Per-type plan

        /// <summary>
        /// The reflection work done once per closed event type: its display name, whether it opted out of capture, its public
        /// instance fields and readable non-indexer properties, and whether it overrides <c>ToString()</c>.
        /// </summary>
        private static class Plan<T>
        {

            public static readonly string TypeName;
            public static readonly bool Omit;
            public static readonly FieldInfo[] Fields;
            public static readonly PropertyInfo[] Properties;
            public static readonly bool HasCustomToString;

            static Plan()
            {
                Type type = typeof(T);
                TypeName = type.Name;

                BroadcastAttribute attribute = type.GetCustomAttribute<BroadcastAttribute>(inherit: false);
                Omit = attribute != null && attribute.OmitSnapshot;
                if (Omit)
                {
                    Fields = Array.Empty<FieldInfo>();
                    Properties = Array.Empty<PropertyInfo>();
                    return;
                }

                Fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

                List<PropertyInfo> readable = new List<PropertyInfo>();
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.CanRead && property.GetIndexParameters().Length == 0)
                        readable.Add(property);
                }
                Properties = readable.ToArray();

                HasCustomToString = PayloadReflector.HasCustomToString(type);
            }

        }

        #endregion

    }

}
#endif
