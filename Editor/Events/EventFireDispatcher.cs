using System;
using System.Reflection;

using UnityEngine;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// Fires a drafted event onto a bus from the Events window. The bus verbs are generically constrained on the kind markers, so they can't
    /// be called with a runtime <see cref="Type"/> directly; this closes each one over the drafted type through reflection and reports the
    /// outcome as a <see cref="FireResult"/>. It always uses the synchronous verb for a signal, command or request (the window is a manual
    /// probe, not a call site that awaits), and the awaitable <c>Cue</c> for a cue so the window can show its completion.
    /// </summary>
    public static class EventFireDispatcher
    {

        // The four constrained verbs, resolved once. Order has two same-named overloads (void ack vs valued result) told apart by return
        // type; the others are unambiguous by name.
        private static readonly MethodInfo s_emit = typeof(EventBus).GetMethod(nameof(EventBus.Emit));
        private static readonly MethodInfo s_cue = typeof(EventBus).GetMethod(nameof(EventBus.Cue));
        private static readonly MethodInfo s_ask = typeof(EventBus).GetMethod(nameof(EventBus.Ask));
        private static readonly MethodInfo s_orderVoid = ResolveOrderVoid();
        private static readonly MethodInfo s_orderValued = ResolveOrderValued();

        /// <summary>
        /// Fires <paramref name="instance"/> onto <paramref name="bus"/> using the verb for its kind, and returns what happened.
        /// </summary>
        /// <param name="bus">The bus to fire onto (typically the default bus).</param>
        /// <param name="entry">The catalog entry describing the event's kind and result type.</param>
        /// <param name="instance">The drafted payload (its runtime type must be <see cref="EventEntry.EventType"/>).</param>
        /// <returns>The outcome, ready to render inline.</returns>
        public static FireResult Fire(EventBus bus, EventEntry entry, object instance)
        {
            if (bus == null)
                throw new ArgumentNullException(nameof(bus));
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            try
            {
                switch (entry.Kind)
                {
                    case EventKind.Signal:
                        Invoke(s_emit, bus, entry.EventType, instance);
                        return FireResult.Done(EventKind.Signal, "Emitted.");

                    case EventKind.Cue:
                        Awaitable completion = (Awaitable)Invoke(s_cue, bus, entry.EventType, instance, default(System.Threading.CancellationToken));
                        return FireResult.Running(completion, "Performing…");

                    case EventKind.Command when !entry.HasResult:
                        bool acknowledged = (bool)Invoke(s_orderVoid, bus, entry.EventType, instance);
                        return FireResult.Produced(EventKind.Command, acknowledged, acknowledged ? "Performed." : "No handler; not performed.");

                    case EventKind.Command:
                        object outcome = InvokeResult(s_orderValued, bus, entry.ResultType, instance);
                        return FireResult.Produced(EventKind.Command, outcome, Describe(outcome));

                    case EventKind.Request:
                        object answer = InvokeResult(s_ask, bus, entry.ResultType, instance);
                        return FireResult.Produced(EventKind.Request, answer, Describe(answer));

                    default:
                        throw new InvalidOperationException($"Unknown event kind '{entry.Kind}'.");
                }
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException != null)
            {
                // Unwrap the real failure the bus verb threw (an unhandled valued command / request, or a handler that threw) so the window
                // shows its message, not the reflection wrapper's.
                return FireResult.Fault(entry.Kind, wrapped.InnerException);
            }
            catch (Exception exception)
            {
                return FireResult.Fault(entry.Kind, exception);
            }
        }

        /// <summary>
        /// Closes a verb generic on the event type (<c>Emit&lt;T&gt;</c>, <c>Cue&lt;T&gt;</c>, void <c>Order&lt;T&gt;</c>) over that type and
        /// invokes it.
        /// </summary>
        private static object Invoke(MethodInfo verb, EventBus bus, Type eventType, params object[] arguments)
        {
            return verb.MakeGenericMethod(eventType).Invoke(bus, arguments);
        }

        /// <summary>
        /// Closes a result-typed verb (valued <c>Order&lt;TResult&gt;</c>, <c>Ask&lt;TResult&gt;</c>) over its result type and invokes it.
        /// The instance is passed as its marker interface, which its runtime type already satisfies.
        /// </summary>
        private static object InvokeResult(MethodInfo verb, EventBus bus, Type resultType, object instance)
        {
            return verb.MakeGenericMethod(resultType).Invoke(bus, new[] { instance });
        }

        /// <summary>
        /// A one-line rendering of a produced value for the inline result label.
        /// </summary>
        private static string Describe(object value)
        {
            return value == null ? "null" : value.ToString();
        }

        /// <summary>
        /// The void <c>Order&lt;T&gt;(T)</c>: one generic argument, returns a <see cref="bool"/> acknowledgement.
        /// </summary>
        private static MethodInfo ResolveOrderVoid()
        {
            foreach (MethodInfo method in typeof(EventBus).GetMethods())
            {
                if (method.Name == nameof(EventBus.Order) && method.GetGenericArguments().Length == 1 && method.ReturnType == typeof(bool))
                    return method;
            }
            throw new MissingMethodException(nameof(EventBus), "void Order");
        }

        /// <summary>
        /// The valued <c>Order&lt;TResult&gt;(ICommand&lt;TResult&gt;)</c>: one generic argument returned as the result.
        /// </summary>
        private static MethodInfo ResolveOrderValued()
        {
            foreach (MethodInfo method in typeof(EventBus).GetMethods())
            {
                if (method.Name == nameof(EventBus.Order) && method.GetGenericArguments().Length == 1 && method.ReturnType.IsGenericParameter)
                    return method;
            }
            throw new MissingMethodException(nameof(EventBus), "valued Order");
        }

    }

}
