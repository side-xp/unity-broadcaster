using System;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Handled-event tests (synchronous): commands (<see cref="EventBus.Obey{T}"/> / <see cref="EventBus.Order{T}"/>) and
    /// requests (<see cref="EventBus.Answer{T, TResult}"/> / <see cref="EventBus.Ask{TResult}"/> /
    /// <see cref="EventBus.TryAsk{TResult}"/>), the exactly-one-handler rule, the cardinality diagnostics, and handler
    /// cleanup. Every test uses a fresh <see cref="EventBus"/>.
    /// </summary>
    public class EventBusHandledTests
    {

        #region Command round-trips

        [Test]
        public void Order_VoidCommand_InvokesHandlerAndAcknowledges()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int received = -1;

            bus.Obey<MoveCommand>(owner, command => received = command.Steps);
            bool handled = bus.Order(new MoveCommand { Steps = 3 });

            Assert.IsTrue(handled, "A registered handler acknowledges the command.");
            Assert.AreEqual(3, received);
        }

        [Test]
        public void Order_ValuedCommand_ReturnsHandlerOutcome()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Obey<DoubleCommand, int>(owner, command => command.Value * 2);
            int outcome = bus.Order(new DoubleCommand { Value = 21 });

            Assert.AreEqual(42, outcome);
        }

        [Test]
        public void Order_OverloadResolution_PicksAckForVoidAndResultForValued()
        {
            // Compile-time proof: the void command binds the bool-returning overload and the valued command binds the
            // TResult-returning one. If sibling interfaces (D5-12) ever regressed to inheritance, this would stop compiling
            // or bind the wrong overload.
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Obey<MoveCommand>(owner, _ => { });
            bus.Obey<DoubleCommand, int>(owner, command => command.Value * 2);

            bool ack = bus.Order(new MoveCommand { Steps = 1 });
            int result = bus.Order(new DoubleCommand { Value = 5 });

            Assert.IsTrue(ack);
            Assert.AreEqual(10, result);
        }

        #endregion


        #region Request round-trips

        [Test]
        public void Ask_ReturnsHandlerAnswer()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Answer<SumRequest, int>(owner, request => request.A + request.B);
            int answer = bus.Ask(new SumRequest { A = 2, B = 5 });

            Assert.AreEqual(7, answer);
        }

        [Test]
        public void TryAsk_WithHandler_ReturnsTrueAndAnswer()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Answer<SumRequest, int>(owner, request => request.A + request.B);

            Assert.IsTrue(bus.TryAsk(new SumRequest { A = 4, B = 6 }, out int answer));
            Assert.AreEqual(10, answer);
        }

        [Test]
        public void TryAsk_WithoutHandler_ReturnsFalseAndDefault()
        {
            EventBus bus = new EventBus();

            Assert.IsFalse(bus.TryAsk(new SumRequest { A = 1, B = 1 }, out int answer));
            Assert.AreEqual(0, answer);
        }

        #endregion


        #region Cardinality diagnostics

        [Test]
        public void Order_VoidCommandWithoutHandler_LogsErrorAndReturnsFalse()
        {
            EventBus bus = new EventBus();

            LogAssert.Expect(LogType.Error, new Regex("No handler is registered for command"));
            bool handled = bus.Order(new MoveCommand { Steps = 1 });

            Assert.IsFalse(handled);
        }

        [Test]
        public void Order_ValuedCommandWithoutHandler_Throws()
        {
            EventBus bus = new EventBus();

            // A valued order has no outcome to return without a handler, so it throws (in release too), never returns default.
            Assert.Throws<InvalidOperationException>(() => bus.Order(new DoubleCommand { Value = 1 }));
        }

        [Test]
        public void Ask_WithoutHandler_Throws()
        {
            EventBus bus = new EventBus();

            Assert.Throws<InvalidOperationException>(() => bus.Ask(new SumRequest { A = 1, B = 1 }));
        }

        [Test]
        public void Obey_SecondHandler_LogsDiagnosticAndKeepsFirst()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Obey<DoubleCommand, int>(owner, command => command.Value * 2);

            LogAssert.Expect(LogType.Error, new Regex("already registered"));
            SubscriptionHandle second = bus.Obey<DoubleCommand, int>(owner, command => command.Value * 10);

            Assert.IsFalse(second.IsActive, "The rejected duplicate returns an inactive handle.");
            Assert.AreEqual(4, bus.Order(new DoubleCommand { Value = 2 }), "The first handler stays authoritative.");
        }

        [Test]
        public void Answer_SecondHandler_LogsDiagnosticAndKeepsFirst()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Answer<SumRequest, int>(owner, request => request.A + request.B);

            LogAssert.Expect(LogType.Error, new Regex("already registered"));
            bus.Answer<SumRequest, int>(owner, request => 999);

            Assert.AreEqual(3, bus.Ask(new SumRequest { A = 1, B = 2 }), "The first answerer stays authoritative.");
        }

        #endregion


        #region Handler replacement (hand-off)

        [Test]
        public void Obey_Replace_SupersedesExistingWithoutError()
        {
            EventBus bus = new EventBus();
            object first = new object();
            object second = new object();

            bus.Obey<DoubleCommand, int>(first, command => command.Value * 2);
            // No LogAssert.Expect: replace must NOT log the duplicate error (an unexpected log would fail the test).
            SubscriptionHandle handle = bus.Obey<DoubleCommand, int>(second, command => command.Value * 10, replace: true);

            Assert.IsTrue(handle.IsActive);
            Assert.AreEqual(30, bus.Order(new DoubleCommand { Value = 3 }), "The replacing handler is now authoritative.");
        }

        [Test]
        public void Answer_Replace_SupersedesExistingWithoutError()
        {
            EventBus bus = new EventBus();
            object first = new object();
            object second = new object();

            bus.Answer<SumRequest, int>(first, request => request.A + request.B);
            SubscriptionHandle handle = bus.Answer<SumRequest, int>(second, request => (request.A + request.B) * 100, replace: true);

            Assert.IsTrue(handle.IsActive);
            Assert.AreEqual(300, bus.Ask(new SumRequest { A = 1, B = 2 }), "The replacing answerer is now authoritative.");
        }

        [Test]
        public void Obey_ReplaceWhenNoneExists_RegistersNormally()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            SubscriptionHandle handle = bus.Obey<DoubleCommand, int>(owner, command => command.Value * 2, replace: true);

            Assert.IsTrue(handle.IsActive);
            Assert.AreEqual(8, bus.Order(new DoubleCommand { Value = 4 }));
        }

        [Test]
        public void Obey_Replace_OldOwnerCleanupDoesNotRemoveNewHandler()
        {
            // The additive-scene hand-off: the incoming handler replaces the outgoing one while both owners are alive,
            // then the outgoing owner is cleaned up. The new handler must survive.
            EventBus bus = new EventBus();
            object outgoing = new object();
            object incoming = new object();

            bus.Obey<DoubleCommand, int>(outgoing, command => command.Value * 2);
            bus.Obey<DoubleCommand, int>(incoming, command => command.Value * 10, replace: true);

            bus.UnsubscribeAll(outgoing); // outgoing scene unloads after the hand-off

            Assert.AreEqual(30, bus.Order(new DoubleCommand { Value = 3 }), "The incoming handler must remain.");
        }

        [Test]
        public void Obey_Replace_OldHandleBecomesInactive()
        {
            EventBus bus = new EventBus();
            object first = new object();
            object second = new object();

            SubscriptionHandle firstHandle = bus.Obey<DoubleCommand, int>(first, command => command.Value * 2);
            bus.Obey<DoubleCommand, int>(second, command => command.Value * 10, replace: true);

            Assert.IsFalse(firstHandle.IsActive, "The superseded handler's handle reports inactive.");

            // Disposing the stale handle must not disturb the current handler.
            firstHandle.Dispose();
            Assert.AreEqual(50, bus.Order(new DoubleCommand { Value = 5 }));
        }

        #endregion


        #region Exception propagation

        [Test]
        public void Order_HandlerThrows_PropagatesToCaller()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Obey<DoubleCommand, int>(owner, _ => throw new InvalidOperationException("boom"));

            // A single handler has no "others" to isolate from, and a valued order must return something — so the fault
            // surfaces to the caller rather than being swallowed like a signal listener's.
            Assert.Throws<InvalidOperationException>(() => bus.Order(new DoubleCommand { Value = 1 }));
        }

        #endregion


        #region Cleanup

        [Test]
        public void UnsubscribeAll_RemovesHandlers()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Obey<MoveCommand>(owner, _ => { });
            bus.Answer<SumRequest, int>(owner, request => request.A + request.B);

            int removed = bus.UnsubscribeAll(owner);

            Assert.AreEqual(2, removed, "Both the command handler and the request answerer are counted.");
            LogAssert.Expect(LogType.Error, new Regex("No handler is registered for command"));
            Assert.IsFalse(bus.Order(new MoveCommand()));
            Assert.IsFalse(bus.TryAsk(new SumRequest(), out _));
        }

        [Test]
        public void HandleDispose_RemovesHandler()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            SubscriptionHandle handle = bus.Obey<DoubleCommand, int>(owner, command => command.Value * 2);
            handle.Dispose();

            Assert.Throws<InvalidOperationException>(() => bus.Order(new DoubleCommand { Value = 1 }));
        }

        [Test]
        public void ClearOfType_RemovesHandler()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Answer<SumRequest, int>(owner, request => request.A + request.B);
            bus.Clear<SumRequest>();

            Assert.IsFalse(bus.TryAsk(new SumRequest(), out _));
        }

        [Test]
        public void Clear_RemovesHandlers()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Obey<DoubleCommand, int>(owner, command => command.Value * 2);
            bus.Clear();

            Assert.Throws<InvalidOperationException>(() => bus.Order(new DoubleCommand { Value = 1 }));
        }

        [Test]
        public void ObeyAfterHandlerRemoved_Succeeds()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            SubscriptionHandle first = bus.Obey<DoubleCommand, int>(owner, command => command.Value * 2);
            first.Dispose();

            SubscriptionHandle second = bus.Obey<DoubleCommand, int>(owner, command => command.Value * 3);

            Assert.IsTrue(second.IsActive);
            Assert.AreEqual(9, bus.Order(new DoubleCommand { Value = 3 }));
        }

        #endregion


        #region Isolation

        [Test]
        public void TwoBuses_DoNotShareHandlers()
        {
            EventBus first = new EventBus();
            EventBus second = new EventBus();
            object owner = new object();

            first.Obey<DoubleCommand, int>(owner, command => command.Value * 2);

            Assert.AreEqual(4, first.Order(new DoubleCommand { Value = 2 }));
            Assert.Throws<InvalidOperationException>(() => second.Order(new DoubleCommand { Value = 2 }),
                "A handler on one bus must not answer orders on another.");
        }

        #endregion


        #region Argument validation

        [Test]
        public void Obey_NullArguments_Throw()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            Assert.Throws<ArgumentNullException>(() => bus.Obey<MoveCommand>(null, _ => { }));
            Assert.Throws<ArgumentNullException>(() => bus.Obey<MoveCommand>(owner, null));
            Assert.Throws<ArgumentNullException>(() => bus.Obey<DoubleCommand, int>(null, command => command.Value));
            Assert.Throws<ArgumentNullException>(() => bus.Obey<DoubleCommand, int>(owner, null));
        }

        [Test]
        public void Answer_NullArguments_Throw()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            Assert.Throws<ArgumentNullException>(() => bus.Answer<SumRequest, int>(null, request => request.A));
            Assert.Throws<ArgumentNullException>(() => bus.Answer<SumRequest, int>(owner, null));
        }

        [Test]
        public void ValuedOrderAndAsk_NullPayload_Throw()
        {
            EventBus bus = new EventBus();

            Assert.Throws<ArgumentNullException>(() => bus.Order<int>(null));
            Assert.Throws<ArgumentNullException>(() => bus.Ask<int>(null));
            Assert.Throws<ArgumentNullException>(() => bus.TryAsk<int>(null, out _));
        }

        #endregion

    }

}
