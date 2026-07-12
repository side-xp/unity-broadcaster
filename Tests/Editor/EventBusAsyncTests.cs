using System;
using System.Text.RegularExpressions;
using System.Threading;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Async-layer tests: async <see cref="EventBus.Obey{T}(object, Func{T, Awaitable}, bool)"/> /
    /// <see cref="EventBus.Answer{T, TResult}(object, Func{T, Awaitable{TResult}}, bool)"/>, the async send verbs
    /// (<c>OrderAsync</c>/<c>AskAsync</c>), cancellation, the sync-verb-on-async-handler diagnostics, and the never-hangs
    /// guarantee. Every test uses a fresh <see cref="EventBus"/>.
    /// <br/>
    /// These run in EditMode and stay synchronous by driving each handler's completion through a test-owned
    /// <see cref="AwaitableCompletionSource{T}"/> — setting its result resumes the bus's bridge on the same call, and every
    /// resolution the bus itself owns (cancel, unregister, unhandled) completes synchronously too.
    /// </summary>
    public class EventBusAsyncTests
    {

        #region Round-trips

        [Test]
        public void OrderAsync_AsyncValuedHandler_RoundTrips()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>();

            bus.Obey<DoubleCommand, int>(owner, _ => handler.Awaitable);

            Awaitable<int> pending = bus.OrderAsync(new DoubleCommand { Value = 21 });
            Awaitable<int>.Awaiter awaiter = pending.GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted, "The order is still in flight until the handler completes.");

            handler.SetResult(42);
            Assert.IsTrue(awaiter.IsCompleted);
            Assert.AreEqual(42, awaiter.GetResult());
        }

        [Test]
        public void AskAsync_AsyncHandler_RoundTrips()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>();

            bus.Answer<SumRequest, int>(owner, _ => handler.Awaitable);

            Awaitable<int> pending = bus.AskAsync(new SumRequest { A = 4, B = 6 });
            Awaitable<int>.Awaiter awaiter = pending.GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted);

            handler.SetResult(10);
            Assert.AreEqual(10, awaiter.GetResult());
        }

        [Test]
        public void OrderAsync_AsyncVoidHandler_RoundTrips()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource handler = new AwaitableCompletionSource();

            bus.Obey<MoveCommand>(owner, _ => handler.Awaitable);

            Awaitable pending = bus.OrderAsync(new MoveCommand { Steps = 2 });
            Awaitable.Awaiter awaiter = pending.GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted);

            handler.SetResult();
            Assert.IsTrue(awaiter.IsCompleted);
            awaiter.GetResult(); // must not throw
        }

        #endregion


        #region Sync handler through an async verb

        [Test]
        public void OrderAsync_SyncValuedHandler_CompletesImmediately()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Obey<DoubleCommand, int>(owner, command => command.Value * 2);

            Awaitable<int>.Awaiter awaiter = bus.OrderAsync(new DoubleCommand { Value = 21 }).GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted, "A sync handler completes within the OrderAsync call.");
            Assert.AreEqual(42, awaiter.GetResult());
        }

        [Test]
        public void OrderAsync_SyncVoidHandler_CompletesImmediately()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool ran = false;

            bus.Obey<MoveCommand>(owner, _ => ran = true);

            Awaitable.Awaiter awaiter = bus.OrderAsync(new MoveCommand { Steps = 1 }).GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted);
            awaiter.GetResult();
            Assert.IsTrue(ran);
        }

        [Test]
        public void AskAsync_SyncHandler_CompletesImmediately()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Answer<SumRequest, int>(owner, request => request.A + request.B);

            Assert.AreEqual(7, bus.AskAsync(new SumRequest { A = 3, B = 4 }).GetAwaiter().GetResult());
        }

        [Test]
        public void OrderAsync_SyncHandlerThrows_Faults()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            // Typed local so the throw-expression lambda binds to the sync overload, not the async one.
            Func<DoubleCommand, int> throwing = _ => throw new InvalidOperationException("boom");
            bus.Obey<DoubleCommand, int>(owner, throwing);

            Awaitable<int>.Awaiter awaiter = bus.OrderAsync(new DoubleCommand { Value = 1 }).GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
        }

        #endregion


        #region Sync verb on an async handler (dev diagnostics)

        [Test]
        public void Order_VoidOnAsyncHandler_LogsErrorAndReturnsFalse()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource handler = new AwaitableCompletionSource();

            bus.Obey<MoveCommand>(owner, _ => handler.Awaitable);

            LogAssert.Expect(LogType.Error, new Regex("asynchronous"));
            Assert.IsFalse(bus.Order(new MoveCommand { Steps = 1 }));
        }

        [Test]
        public void Order_ValuedOnAsyncHandler_Throws()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>();

            bus.Obey<DoubleCommand, int>(owner, _ => handler.Awaitable);

            Assert.Throws<InvalidOperationException>(() => bus.Order(new DoubleCommand { Value = 1 }));
        }

        [Test]
        public void Ask_OnAsyncHandler_Throws()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>();

            bus.Answer<SumRequest, int>(owner, _ => handler.Awaitable);

            Assert.Throws<InvalidOperationException>(() => bus.Ask(new SumRequest()));
        }

        [Test]
        public void TryAsk_OnAsyncHandler_LogsErrorAndReturnsFalse()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>();

            bus.Answer<SumRequest, int>(owner, _ => handler.Awaitable);

            LogAssert.Expect(LogType.Error, new Regex("asynchronous"));
            Assert.IsFalse(bus.TryAsk(new SumRequest(), out int _));
        }

        [Test]
        public void AsyncLambda_BindsToAsyncOverload()
        {
            // Compile-time + behavioural proof: an async lambda binds to the Func<T, Awaitable<TResult>> overload (not the
            // sync Func<T, TResult>), so the handler lands in the async slot and the sync verb rejects it.
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> gate = new AwaitableCompletionSource<int>();

            bus.Obey<DoubleCommand, int>(owner, async command => (await gate.Awaitable) + command.Value);

            Assert.Throws<InvalidOperationException>(() => bus.Order(new DoubleCommand { Value = 1 }),
                "An async lambda must register as an async handler, which the sync Order rejects.");
        }

        #endregion


        #region Unhandled

        [Test]
        public void OrderAsync_UnhandledValued_Faults()
        {
            EventBus bus = new EventBus();

            Awaitable<int>.Awaiter awaiter = bus.OrderAsync(new DoubleCommand { Value = 1 }).GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
        }

        [Test]
        public void AskAsync_Unhandled_Faults()
        {
            EventBus bus = new EventBus();

            Awaitable<int>.Awaiter awaiter = bus.AskAsync(new SumRequest()).GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
        }

        [Test]
        public void OrderAsync_UnhandledVoid_CompletesWithDevLog()
        {
            EventBus bus = new EventBus();

            LogAssert.Expect(LogType.Error, new Regex("No handler is registered for command"));
            Awaitable.Awaiter awaiter = bus.OrderAsync(new MoveCommand { Steps = 1 }).GetAwaiter();

            Assert.IsTrue(awaiter.IsCompleted, "A void order with no handler completes silently (nothing to report).");
            awaiter.GetResult();
        }

        #endregion


        #region Cancellation

        [Test]
        public void OrderAsync_TokenAlreadyCancelled_ResolvesCancelled()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>();
            bus.Obey<DoubleCommand, int>(owner, _ => handler.Awaitable);

            CancellationToken cancelled = new CancellationToken(canceled: true);
            Awaitable<int>.Awaiter awaiter = bus.OrderAsync(new DoubleCommand { Value = 1 }, cancelled).GetAwaiter();

            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
        }

        [Test]
        public void OrderAsync_TokenCancelledDuringFlight_ResolvesCancelled()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>();
            bus.Obey<DoubleCommand, int>(owner, _ => handler.Awaitable);

            CancellationTokenSource cts = new CancellationTokenSource();
            Awaitable<int>.Awaiter awaiter = bus.OrderAsync(new DoubleCommand { Value = 1 }, cts.Token).GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted);

            cts.Cancel();
            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
        }

        #endregion


        #region Never-hangs (handler unregistered mid-flight)

        [Test]
        public void OrderAsync_HandlerReleasedMidFlight_ResolvesCancelled()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>(); // never completes
            bus.Obey<DoubleCommand, int>(owner, _ => handler.Awaitable);

            Awaitable<int>.Awaiter awaiter = bus.OrderAsync(new DoubleCommand { Value = 1 }).GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted);

            bus.UnsubscribeAll(owner); // the handler vanishes before completing

            Assert.IsTrue(awaiter.IsCompleted, "The caller must resolve rather than hang forever.");
            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
        }

        [Test]
        public void OrderAsync_HandlerDisposedMidFlight_ResolvesCancelled()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>();
            SubscriptionHandle registration = bus.Obey<DoubleCommand, int>(owner, _ => handler.Awaitable);

            Awaitable<int>.Awaiter awaiter = bus.OrderAsync(new DoubleCommand { Value = 1 }).GetAwaiter();
            registration.Dispose();

            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
        }

        [Test]
        public void AskAsync_HandlerClearedMidFlight_ResolvesCancelled()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>();
            bus.Answer<SumRequest, int>(owner, _ => handler.Awaitable);

            Awaitable<int>.Awaiter awaiter = bus.AskAsync(new SumRequest()).GetAwaiter();
            bus.Clear<SumRequest>();

            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
        }

        [Test]
        public void OrderAsync_HandlerReplacedMidFlight_OldCallerResolvesCancelled()
        {
            EventBus bus = new EventBus();
            object first = new object();
            object second = new object();
            AwaitableCompletionSource<int> firstHandler = new AwaitableCompletionSource<int>();
            bus.Obey<DoubleCommand, int>(first, _ => firstHandler.Awaitable);

            Awaitable<int>.Awaiter awaiter = bus.OrderAsync(new DoubleCommand { Value = 1 }).GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted);

            // Hand the handler off to a new owner; the in-flight caller of the old one must resolve, not hang.
            bus.Obey<DoubleCommand, int>(second, command => command.Value * 2, replace: true);

            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
        }

        #endregion


        #region Faults

        [Test]
        public void OrderAsync_AsyncHandlerFaults_ResolvesFaultedAndBusStaysUsable()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>();
            bus.Obey<DoubleCommand, int>(owner, _ => handler.Awaitable);

            Awaitable<int>.Awaiter awaiter = bus.OrderAsync(new DoubleCommand { Value = 1 }).GetAwaiter();
            handler.SetException(new InvalidOperationException("handler failed"));

            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());

            // Isolation: a fault in one dispatch leaves the bus fully usable.
            AwaitableCompletionSource<int> next = new AwaitableCompletionSource<int>();
            bus.Obey<DoubleCommand, int>(owner, _ => next.Awaitable, replace: true);
            Awaitable<int>.Awaiter again = bus.OrderAsync(new DoubleCommand { Value = 2 }).GetAwaiter();
            next.SetResult(99);
            Assert.AreEqual(99, again.GetResult());
        }

        #endregion

    }

}
