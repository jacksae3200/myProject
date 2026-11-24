using UnityEngine;
using UnityEngine.AI;
namespace CpcPrototype
{
    [DisallowMultipleComponent]
    public sealed class CrawlerZombieAbility : ZombieAbility
    {
        public override ZombieKind Kind => ZombieKind.Crawler;
        [Min(0f)] public float MoveSpeedMultiplier = 1.5f;
        public override float SpeedMultiplier => MoveSpeedMultiplier;
        public override void Tick() { }
    }
}

