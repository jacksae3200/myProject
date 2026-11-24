using UnityEngine;
namespace CpcPrototype
{
    public enum ZombieKind { Normal, Spitter, Boomer, Crawler, Burrower, Jumper }
    // 이전 씬의 설정을 종류별 컴포넌트로 옮기기 위한 호환 데이터입니다.
    [AddComponentMenu("" )]
    public sealed class ZombieAbilities : MonoBehaviour
    {
        [Header("공통 근접 감지")]
        [Min(0f)] public float TriggerRange = 2f;
        [Header("토사물 좀비")]
        [Min(0f)] public float SpitRange = 12f;
        [Min(.01f)] public float SpitInterval = 3f;
        [Min(0f)] public float SpitDamage = 20f;
        [Min(.01f)] public float SpitFlightTime = .9f;
        [Min(.01f)] public float SpitLifetime = 6f;
        [Min(.01f)] public float SpitSize = .2f;
        public float SpitSpawnHeight = 1f;
        public float SpitTargetHeight = .7f;
        [Header("부머")]
        [Min(0f)] public float ExplosionDelay = 2f;
        [Min(0f)] public float ExplosionDamage = 50f;
        [Min(0f), Tooltip("본인 캡슐 최대 크기에 곱하는 폭발 반경 배율")]
        public float ExplosionRadiusMultiplier = 2f;
        [Header("버로우 좀비")]
        [Min(0f)] public float BurrowInitialDelay = 3f;
        [Min(.01f)] public float BurrowInterval = 5f;
        [Min(0f)] public float BurrowDamage = 20f;
        [Min(0f)] public float RootDuration = 5f;
        [Header("점프 좀비")]
        [Min(0f)] public float LeapRange = 8f;
        [Min(.01f)] public float LeapInterval = 5f;
        [Min(.01f)] public float LeapDuration = .65f;
        [Min(0f)] public float LeapHeight = 2f;
        [Min(0f)] public float LeapDamage = 30f;
        [Min(0f)] public float FleeDuration = 1.5f;
        [Min(0f)] public float FleeDistance = 5f;
    }
}

