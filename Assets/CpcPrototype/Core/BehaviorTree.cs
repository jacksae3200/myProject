using System;

namespace CpcPrototype.BehaviorTree
{
    public enum NodeStatus { Failure, Success, Running }
    public abstract class Node { public abstract NodeStatus Tick(); }

    public sealed class Condition : Node
    {
        private readonly Func<bool> _condition;
        public Condition(Func<bool> condition) { _condition = condition; }
        public override NodeStatus Tick() => _condition() ? NodeStatus.Success : NodeStatus.Failure;
    }

    public sealed class ActionNode : Node
    {
        private readonly Func<NodeStatus> _action;
        public ActionNode(Func<NodeStatus> action) { _action = action; }
        public override NodeStatus Tick() => _action();
    }

    // 매 틱 우선순위를 재평가하는 reactive selector/sequence.
    public sealed class Selector : Node
    {
        private readonly Node[] _children;
        public Selector(params Node[] children) { _children = children; }
        public override NodeStatus Tick()
        {
            for (int i = 0; i < _children.Length; i++)
            {
                NodeStatus result = _children[i].Tick();
                if (result != NodeStatus.Failure) return result;
            }
            return NodeStatus.Failure;
        }
    }

    public sealed class Sequence : Node
    {
        private readonly Node[] _children;
        public Sequence(params Node[] children) { _children = children; }
        public override NodeStatus Tick()
        {
            for (int i = 0; i < _children.Length; i++)
            {
                NodeStatus result = _children[i].Tick();
                if (result != NodeStatus.Success) return result;
            }
            return NodeStatus.Success;
        }
    }
}
