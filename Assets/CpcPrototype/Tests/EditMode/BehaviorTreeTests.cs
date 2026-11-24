using System.Collections.Generic;
using CpcPrototype.BehaviorTree;
using NUnit.Framework;

namespace CpcPrototype.Tests
{
    public sealed class BehaviorTreeTests
    {
        [Test]
        public void Condition_MapsTrueAndFalseToSuccessAndFailure()
        {
            Assert.AreEqual(NodeStatus.Success, new Condition(() => true).Tick());
            Assert.AreEqual(NodeStatus.Failure, new Condition(() => false).Tick());
        }

        [Test]
        public void ActionNode_ReturnsActionStatus()
        {
            Assert.AreEqual(NodeStatus.Running, new ActionNode(() => NodeStatus.Running).Tick());
        }

        [Test]
        public void Selector_StopsAtFirstNonFailureAndRechecksFromFirstChild()
        {
            var calls = new List<int>();
            var selector = new Selector(
                new ActionNode(() => { calls.Add(1); return NodeStatus.Failure; }),
                new ActionNode(() => { calls.Add(2); return NodeStatus.Running; }),
                new ActionNode(() => { calls.Add(3); return NodeStatus.Success; }));

            Assert.AreEqual(NodeStatus.Running, selector.Tick());
            Assert.AreEqual(NodeStatus.Running, selector.Tick());
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 2 }, calls);
        }

        [Test]
        public void Selector_ReturnsFailureWhenAllChildrenFail()
        {
            var selector = new Selector(
                new ActionNode(() => NodeStatus.Failure),
                new ActionNode(() => NodeStatus.Failure));

            Assert.AreEqual(NodeStatus.Failure, selector.Tick());
        }

        [Test]
        public void Sequence_StopsAtFirstNonSuccess()
        {
            var calls = new List<int>();
            var sequence = new Sequence(
                new ActionNode(() => { calls.Add(1); return NodeStatus.Success; }),
                new ActionNode(() => { calls.Add(2); return NodeStatus.Running; }),
                new ActionNode(() => { calls.Add(3); return NodeStatus.Success; }));

            Assert.AreEqual(NodeStatus.Running, sequence.Tick());
            CollectionAssert.AreEqual(new[] { 1, 2 }, calls);
        }

        [Test]
        public void Sequence_ReturnsSuccessWhenAllChildrenSucceed()
        {
            var sequence = new Sequence(
                new ActionNode(() => NodeStatus.Success),
                new ActionNode(() => NodeStatus.Success));

            Assert.AreEqual(NodeStatus.Success, sequence.Tick());
        }
    }
}
