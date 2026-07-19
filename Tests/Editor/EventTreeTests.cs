using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using SideXP.Broadcaster.EditorOnly;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Tests for the Events window's tree builder: how an event's <c>/</c>-separated display name becomes nested folder/leaf nodes, and how
    /// those nodes are ordered. Pure logic, no TreeView or window involved.
    /// </summary>
    public class EventTreeTests
    {

        // A catalog entry with the given display name; the event type is irrelevant to nesting, so any concrete event serves.
        private static EventEntry Named(string displayName)
        {
            return new EventEntry(typeof(PingSignal), EventKind.Signal, null, displayName, null, false, false);
        }

        [Test]
        public void Build_Null_ReturnsEmptyRoot()
        {
            EventTreeNode root = EventTree.Build(null);
            Assert.IsNotNull(root);
            Assert.AreEqual(0, root.Children.Count);
        }

        [Test]
        public void Build_NoPath_PlacesLeafAtRoot()
        {
            EventTreeNode root = EventTree.Build(new[] { Named("Ping") });

            Assert.AreEqual(1, root.Children.Count);
            EventTreeNode leaf = root.Children[0];
            Assert.AreEqual("Ping", leaf.Name);
            Assert.IsNotNull(leaf.Entry);
            Assert.IsFalse(leaf.HasChildren);
        }

        [Test]
        public void Build_Path_NestsUnderFolders()
        {
            EventTreeNode root = EventTree.Build(new[] { Named("Combat/Damage/Dealt") });

            EventTreeNode combat = root.Children.Single();
            Assert.AreEqual("Combat", combat.Name);
            Assert.IsNull(combat.Entry, "An intermediate path segment is a folder, not an event.");

            EventTreeNode damage = combat.Children.Single();
            Assert.AreEqual("Damage", damage.Name);

            EventTreeNode dealt = damage.Children.Single();
            Assert.AreEqual("Dealt", dealt.Name);
            Assert.IsNotNull(dealt.Entry, "The last segment carries the event.");
        }

        [Test]
        public void Build_SharedPrefix_SharesTheFolder()
        {
            EventTreeNode root = EventTree.Build(new[] { Named("Combat/Hit"), Named("Combat/Miss") });

            EventTreeNode combat = root.Children.Single();
            Assert.AreEqual("Combat", combat.Name);
            CollectionAssert.AreEquivalent(new[] { "Hit", "Miss" }, combat.Children.Select(n => n.Name).ToList());
        }

        [Test]
        public void Build_OrdersFoldersBeforeLeaves_ThenAlphabetically()
        {
            EventTreeNode root = EventTree.Build(new[] { Named("Zeta"), Named("Beta/Child"), Named("Alpha") });

            List<string> order = root.Children.Select(n => n.Name).ToList();
            // "Beta" is a folder so it sorts ahead of the "Alpha"/"Zeta" leaves; leaves then go alphabetically.
            Assert.AreEqual(new List<string> { "Beta", "Alpha", "Zeta" }, order);
        }

        [Test]
        public void Build_TrimsSegments_AndDropsEmptyOnes()
        {
            EventTreeNode root = EventTree.Build(new[] { Named("/ Combat /  Hit ") });

            EventTreeNode combat = root.Children.Single();
            Assert.AreEqual("Combat", combat.Name);
            Assert.AreEqual("Hit", combat.Children.Single().Name);
        }

    }

}
