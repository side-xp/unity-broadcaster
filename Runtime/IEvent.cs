namespace SideXP.Broadcaster
{

    /// <summary>
    /// Base marker for every event type handled by an <see cref="EventBus"/>.<br/>
    /// An event <b>is</b> a C# type: the type is both the identity and the payload. Implement one of the four kind markers below — never
    /// this one directly.
    /// </summary>
    /// <remarks>
    /// The bus dispatches on the <b>exact</b> type only: emitting a derived type never reaches a listener registered for a base type.
    /// </remarks>
    public interface IEvent { }

    /// <summary>
    /// A Signal is a simple notification. It can have 0 or more listeners.<br/>
    /// Sent with <see cref="EventBus.Emit{T}(T)"/>.
    /// </summary>
    public interface ISignal : IEvent { }

    /// <summary>
    /// A Cue is a notification that "can take time". Orchestrators can wait for all the listeners to finish their job before it's
    /// considered ended. It can have 0 or more listeners.
    /// </summary>
    public interface ICue : IEvent { }

    /// <summary>
    /// A Command is an actual order. This kind of event must be answered by exactly one handler that performs the action and acknowledges
    /// it.
    /// </summary>
    public interface ICommand : IEvent { }

    /// <summary>
    /// A Command is an actual order. This kind of event must be answered by exactly one handler that performs the action and returns
    /// <typeparamref name="TResult"/> as a "report" of what happened.<br/>
    /// As a general rule, <typeparamref name="TResult"/> should never be data obtainable without performing it (that would be an
    /// <see cref="IRequest{TResult}"/>), it should only be the actual result of the operation, not the final state of the changed objects.
    /// </summary>
    /// <typeparam name="TResult">The type of value the action produces.</typeparam>
    public interface ICommand<TResult> : IEvent { }

    /// <summary>
    /// A Request is a way for any system to ask for information about another. It must be answered by exactly one handler.<br/>
    /// As a general rule, a Request must be side-effect free, and never change the state of an object.
    /// </summary>
    /// <typeparam name="TResult">The type of the answer.</typeparam>
    public interface IRequest<TResult> : IEvent { }

}
