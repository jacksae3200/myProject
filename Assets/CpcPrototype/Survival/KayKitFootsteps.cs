using UnityEngine;
using UnityEngine.AI;

namespace CpcPrototype
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class KayKitFootsteps : MonoBehaviour
    {
        public KayKitCombatEffects Effects;
        public AudioClip[] WalkClips;
        public AudioClip WaterWalk;
        [Min(.05f)] public float StepDistance = .24f;
        [Range(0f, 1f)] public float WalkVolume = .45f;
        [Range(0f, 1f)] public float WetVolume = .055f;
        [Min(1)] public int BloodyStepCount = 6;

        private AudioSource _audio;
        private Transform _root;
        private SurvivalPlayer _player;
        private NavMeshAgent _agent;
        private Health _health;
        private Vector3 _previous;
        private float _distance;
        private bool _left, _wetLastFrame;
        private int _remaining, _previousClip = -1;

        private void Awake()
        {
            _root = transform;
            _previous = _root.position;
            _audio = GetComponent<AudioSource>();
            _player = GetComponent<SurvivalPlayer>();
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
        }

        private void LateUpdate()
        {
            Vector3 position = _root.position;
            Vector3 movement = position - _previous;
            _previous = position;
            movement.y = 0f;
            bool grounded = _player != null ? _player.CanPlayFootstep :
                _agent != null && _agent.isOnNavMesh && !_agent.isOnOffMeshLink && _health.IsAlive;
            if (!grounded || movement.sqrMagnitude > 1f)
            {
                _distance = 0f;
                _wetLastFrame = false;
                return;
            }
            if (movement.sqrMagnitude < 1e-7f) return;

            bool wet = Effects.IsOnBlood(position);
            if (wet) _remaining = BloodyStepCount;
            _distance += movement.magnitude;
            if (_distance >= StepDistance || wet && !_wetLastFrame)
            {
                _distance = 0f;
                Step(position, movement.normalized, wet);
            }
            _wetLastFrame = wet;
        }

        private void Step(Vector3 position, Vector3 direction, bool wet)
        {
            if (WalkClips.Length > 0)
            {
                int index = Random.Range(0, WalkClips.Length);
                if (WalkClips.Length > 1 && _previousClip >= 0)
                {
                    index = Random.Range(0, WalkClips.Length - 1);
                    if (index >= _previousClip) index++;
                }
                _previousClip = index;
                _audio.PlayOneShot(WalkClips[index], WalkVolume);
            }
            if (wet && WaterWalk != null) _audio.PlayOneShot(WaterWalk, WetVolume);
            _left = !_left;
            if (wet || _remaining <= 0) return;

            Vector3 right = Vector3.Cross(Vector3.up, direction);
            Vector3 foot = position + right * (_left ? -.032f : .032f);
            float alpha = Mathf.Lerp(.12f, .95f, (_remaining - 1f) / Mathf.Max(1, BloodyStepCount - 1));
            if (Effects.StampFootprint(foot, direction, _left, alpha)) _remaining--;
        }
    }
}
