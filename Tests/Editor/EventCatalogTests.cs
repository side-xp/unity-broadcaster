using System;
using System.Collections.Generic;

using NUnit.Framework;

using SideXP.Broadcaster.EditorOnly;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Events window catalog tests: kind classification for each of the four kinds, rejection of non-catalogable types, and the search and
    /// grouping the window renders. Pure logic, no bus or window involved.
    /// </summary>
    public class EventCatalogTests
    {

        #region Classification

        [Test]
        public void Classify_Signal_IsSignalWithNoResult()
        {
            Assert.IsTrue(EventCatalog.TryClassify(typeof(PingSignal), out EventEntry entry));
            Assert.AreEqual(EventKind.Signal, entry.Kind);
            Assert.IsFalse(entry.HasResult);
            Assert.IsNull(entry.ResultType);
        }

        [Test]
        public void Classify_Cue_IsCue()
        {
            Assert.IsTrue(EventCatalog.TryClassify(typeof(FlashCue), out EventEntry entry));
            Assert.AreEqual(EventKind.Cue, entry.Kind);
            Assert.IsFalse(entry.HasResult);
        }

        [Test]
        public void Classify_VoidCommand_IsCommandWithNoResult()
        {
            Assert.IsTrue(EventCatalog.TryClassify(typeof(MoveCommand), out EventEntry entry));
            Assert.AreEqual(EventKind.Command, entry.Kind);
            Assert.IsFalse(entry.HasResult);
        }

        [Test]
        public void Classify_ValuedCommand_IsCommandCarryingResultType()
        {
            Assert.IsTrue(EventCatalog.TryClassify(typeof(DoubleCommand), out EventEntry entry));
            Assert.AreEqual(EventKind.Command, entry.Kind);
            Assert.IsTrue(entry.HasResult);
            Assert.AreEqual(typeof(int), entry.ResultType);
        }

        [Test]
        public void Classify_Request_IsRequestCarryingResultType()
        {
            Assert.IsTrue(EventCatalog.TryClassify(typeof(SumRequest), out EventEntry entry));
            Assert.AreEqual(EventKind.Request, entry.Kind);
            Assert.IsTrue(entry.HasResult);
            Assert.AreEqual(typeof(int), entry.ResultType);
        }

        [Test]
        public void Classify_ReadsBroadcastAttribute()
        {
            Assert.IsTrue(EventCatalog.TryClassify(typeof(DescribedSignal), out EventEntry entry));
            Assert.AreEqual("A described signal.", entry.Description);
            Assert.IsTrue(entry.OmitSnapshot);
            Assert.IsTrue(entry.Hidden);
        }

        // EmptySignal is only left un-hidden (a valid "no metadata" fixture) in a demos-enabled project; without SIDEXP_DEMOS it's marked
        // hidden so package consumers don't see it, and this assertion wouldn't hold. See EventsWindowTestEvents.
#if SIDEXP_DEMOS
        [Test]
        public void Classify_TypeWithoutAttribute_HasNoMetadata()
        {
            Assert.IsTrue(EventCatalog.TryClassify(typeof(EmptySignal), out EventEntry entry));
            Assert.IsNull(entry.Description);
            Assert.IsFalse(entry.OmitSnapshot);
            Assert.IsFalse(entry.Hidden);
        }
#endif

        #endregion


        #region Rejection

        [Test]
        public void Classify_Interface_IsRejected()
        {
            Assert.IsFalse(EventCatalog.TryClassify(typeof(ISignal), out _));
        }

        [Test]
        public void Classify_OpenGeneric_IsRejected()
        {
            Assert.IsFalse(EventCatalog.TryClassify(typeof(OpenSignal<>), out _));
        }

        [Test]
        public void Classify_NonEventType_IsRejected()
        {
            Assert.IsFalse(EventCatalog.TryClassify(typeof(object), out _));
        }

        [Test]
        public void Classify_Null_IsRejected()
        {
            Assert.IsFalse(EventCatalog.TryClassify(null, out _));
        }

        #endregion


        #region Build

        [Test]
        public void BuildFrom_SkipsNonCatalogableTypes()
        {
            Type[] types = { typeof(PingSignal), typeof(ISignal), typeof(OpenSignal<>), typeof(object), typeof(SumRequest) };
            List<EventEntry> entries = EventCatalog.BuildFrom(types);

            Assert.AreEqual(2, entries.Count);
            CollectionAssert.AreEquivalent(new[] { typeof(PingSignal), typeof(SumRequest) }, entries.ConvertAll(e => e.EventType));
        }

        [Test]
        public void BuildFrom_SortsByKind()
        {
            // Given out of kind order, the build orders Signal < Cue < Command < Request.
            Type[] types = { typeof(SumRequest), typeof(MoveCommand), typeof(FlashCue), typeof(PingSignal) };
            List<EventEntry> entries = EventCatalog.BuildFrom(types);

            EventKind[] kinds = entries.ConvertAll(e => e.Kind).ToArray();
            CollectionAssert.AreEqual(new[] { EventKind.Signal, EventKind.Cue, EventKind.Command, EventKind.Request }, kinds);
        }

        #endregion


        #region Search & grouping

        [Test]
        public void Filter_MatchesNameCaseInsensitively()
        {
            List<EventEntry> entries = EventCatalog.BuildFrom(new[] { typeof(PingSignal), typeof(PongSignal), typeof(FlashCue) });
            List<EventEntry> filtered = EventCatalog.Filter(entries, "ping", includeHidden: true);

            Assert.AreEqual(1, filtered.Count);
            Assert.AreEqual(typeof(PingSignal), filtered[0].EventType);
        }

        [Test]
        public void Filter_MatchesDescription()
        {
            List<EventEntry> entries = EventCatalog.BuildFrom(new[] { typeof(DescribedSignal), typeof(PingSignal) });
            List<EventEntry> filtered = EventCatalog.Filter(entries, "described", includeHidden: true);

            Assert.AreEqual(1, filtered.Count);
            Assert.AreEqual(typeof(DescribedSignal), filtered[0].EventType);
        }

        [Test]
        public void Filter_EmptySearch_ReturnsAll()
        {
            List<EventEntry> entries = EventCatalog.BuildFrom(new[] { typeof(PingSignal), typeof(FlashCue) });
            Assert.AreEqual(2, EventCatalog.Filter(entries, "", includeHidden: true).Count);
            Assert.AreEqual(2, EventCatalog.Filter(entries, "   ", includeHidden: true).Count);
        }

        // Relies on EmptySignal being visible, which only holds with SIDEXP_DEMOS (it's hidden otherwise). See EventsWindowTestEvents.
#if SIDEXP_DEMOS
        [Test]
        public void Filter_ExcludesHiddenEntries_UnlessIncluded()
        {
            // DescribedSignal is [Event(Hidden = true)]; EmptySignal carries no attribute here (demos enabled), so it stays visible.
            List<EventEntry> entries = EventCatalog.BuildFrom(new[] { typeof(DescribedSignal), typeof(EmptySignal) });

            // The eye off (includeHidden: false) drops the hidden entry regardless of the search matching it.
            List<EventEntry> visible = EventCatalog.Filter(entries, "", includeHidden: false);
            Assert.AreEqual(1, visible.Count);
            Assert.AreEqual(typeof(EmptySignal), visible[0].EventType);

            // The eye on (the default) keeps hidden entries like any other.
            Assert.AreEqual(2, EventCatalog.Filter(entries, "", includeHidden: true).Count);
        }
#endif

        [Test]
        public void GroupByKind_OrdersGroupsAndOmitsEmptyKinds()
        {
            List<EventEntry> entries = EventCatalog.BuildFrom(new[] { typeof(SumRequest), typeof(PingSignal), typeof(PongSignal) });
            List<KeyValuePair<EventKind, List<EventEntry>>> groups = EventCatalog.GroupByKind(entries);

            // Only Signal and Request are present, in kind order; Cue and Command are omitted.
            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual(EventKind.Signal, groups[0].Key);
            Assert.AreEqual(2, groups[0].Value.Count);
            Assert.AreEqual(EventKind.Request, groups[1].Key);
            Assert.AreEqual(1, groups[1].Value.Count);
        }

        #endregion

    }

}
