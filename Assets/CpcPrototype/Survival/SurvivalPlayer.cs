using UnityEngine;
using UnityEngine.InputSystem;
using CpcPrototype.Core;

namespace CpcPrototype
{
    [RequireComponent(typeof(CharacterController), typeof(Health))]
    public sealed class SurvivalPlayer : MonoBehaviour
    {
        public Camera View;
        public PlayerGrappleAction Grapple;
        public GrappleSniperAction GrappleSniper;
        public Animator Body;
        public Transform Muzzle;
        public Transform Rifle;
        public GameObject ViewRifle;
        public Renderer[] BodyRenderers;
        public CpcChatPanel Chat;
        public ShieldCompanion Companion;
        [Min(0f)] public float CompanionBubbleOffset = .12f;
        private Transform _companionHead;
        [Min(1)] public int CompanionDialogueFontSize = 20;
        [Min(.1f)] public float CompanionDialogueDuration = 5f;
        private string _lastCompanionDialogue;
        private float _companionDialogueEnd;
        private GUIStyle _companionBubble;
        private readonly GUIContent _companionBubbleContent = new GUIContent();
        public BoxCollider[] OneWayPlatforms = System.Array.Empty<BoxCollider>();
        public bool DepthView;
        public KayKitAimGuide AimGuide;
        public KayKitCombatEffects CombatEffects;
        public EquipmentGrip RifleGrip;
        [Range(0f, 1f)] public float UpperBodyAimWeight = .8f;
        [Range(0f, 80f)] public float UpperBodyPitchLimit = 55f;
        [Min(0f)] public float UpperBodyAimSharpness = 12f;
        private Transform _aimSpine, _aimChest;
        private float _upperBodyPitch, _upperBodyYaw;
        private bool _depthShotPending;
        private Vector2 _depthShotPosition;
        public bool DirectionalMovement;
        public Vector3 DepthCameraOffset = new Vector3(0, 0, -5.7f);
        [Min(.1f)] public float DepthViewSize = 1.85f;
        public GameObject BulletModel;
        [Min(.01f)] public float BulletModelScale = 1f;
        public Vector3 BulletModelRotation = new Vector3(0, 90, 0);
        [Min(0)] public float MoveSpeed = 3.5f;
        [Min(0)] public float SprintSpeed = 6.5f;
        [Min(0)] public float JumpHeight = 1.25f;
        [Min(1)] public float Gravity = 24f;
        [Min(0)] public float RollSpeed = 8f;
        [Min(.05f)] public float RollDuration = .62f;
        [Min(0)] public float RollCooldown = .9f;
        [Min(0)] public float ShotDamage = 10f; public float HeadDamage = 20f; public float MeleeDamage = 20f; public float MeleeRange = 2f; private float _nextMelee; private CombatStatus _status;
        [Min(.03f)] public float ShotInterval = .24f;
        [Min(1)] public int MagazineCapacity = 30;
        [Min(0)] public int StartingMagazineAmmo = 31;
        [Min(0)] public int StartingReserveAmmo = 90;
        [Min(.1f)] public float AmmoWarningDuration = 2f;
        [Min(1)] public float ShotRange = 70f;
        public LayerMask ShotLayers = ~0;
        [Min(.01f)] public float TracerWidth = .025f;
        [Min(.01f)] public float TracerDuration = .16f;
        [Min(.1f)] public float TracerLength = 2f;
        [ColorUsage(false, true)] public Color TracerColor = new Color(1f, .8f, .25f);
        [Min(.01f)] public float MouseSensitivity = .15f;
        public Vector3 CameraPivot = new Vector3(0, 1.53f, 0);
        public Vector3 ThirdPersonOffset = new Vector3(.62f, .32f, -3.7f);
        public Vector3 FirstPersonOffset = new Vector3(0, .04f, .16f);
        [Range(15, 100)] public float AimFov = 42f;
        [Range(15, 100)] public float NormalFov = 65f;
        [Range(0, 1)] public float AnimationBlend = .1f;
        private CharacterController _controller;
        private Health _health;
        private Transform _root;
        private float _yaw, _pitch, _verticalSpeed, _rollEnd, _nextRoll, _nextShot;
        private AmmoInventory _ammo;
        private float _ammoWarningEnd;
        private GUIStyle _ammoWarningStyle;
        private bool _airborne, _falling, _aiming, _chatOpen;
        private Vector3 _rollDirection;
        private LineRenderer _tracer;
        private Material _tracerMaterial;
        private Transform _bullet;
        private Vector3 _tracerOrigin, _tracerTarget;
        private float _tracerStart, _tracerEnd;
        private readonly RaycastHit[] _hits = new RaycastHit[32];
        private static readonly int SPEED = Animator.StringToHash("Speed");
        public Health Health => _health;
        public bool IsAiming => _aiming;
        public bool CanPlayFootstep => _controller.isGrounded && Time.time >= _rollEnd && !_chatOpen && _health.IsAlive;
        public bool IsChatOpen => _chatOpen;
        public bool GetGuideTarget(Vector2 screen, out Vector3 target) => TryGetDepthAim(screen, out target);

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            _ammo = new AmmoInventory(MagazineCapacity, StartingMagazineAmmo, StartingReserveAmmo);
            _root = transform; _health.Configure(CombatTeam.Player, 100f); _status = CombatStatus.Get(gameObject);
            _yaw = _root.eulerAngles.y;
            _pitch = 8;
            if (DepthView && Body != null && Body.isHuman)
            {
                _aimSpine = Body.GetBoneTransform(HumanBodyBones.Spine);
                _aimChest = Body.GetBoneTransform(HumanBodyBones.Chest);
            }
            if (Companion != null && Companion.Body != null && Companion.Body.isHuman)
                _companionHead = Companion.Body.GetBoneTransform(HumanBodyBones.Head);
            if (Chat != null && Companion != null) Chat.Configure(SetChatInput, Companion.SayToCompanion);
        }

        private void Start()
        {
            SetCursor(!DepthView);
            if (Companion != null && Companion.Llm != null) Companion.Llm.Replied += RefreshCompanionDialogue;
            if (AimGuide != null) Cursor.visible = false;
            if (DepthView && Companion != null && Companion.TryGetComponent(out CapsuleCollider companionCollider))
                Physics.IgnoreCollision(_controller, companionCollider);
            var effect = new GameObject("Bullet Tracer");
            effect.transform.SetParent(_root, false);
            _tracer = effect.AddComponent<LineRenderer>();
            _tracerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _tracer.sharedMaterial = _tracerMaterial;
            _tracer.positionCount = 2;
            _tracer.enabled = false;
            if (BulletModel != null)
            {
                _bullet = Instantiate(BulletModel, effect.transform).transform;
                _bullet.name = "Flying Bullet";
                _bullet.localScale = Vector3.one * BulletModelScale / _root.lossyScale.x;
                _bullet.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (!_chatOpen && keyboard != null && Chat != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) Chat.Open();
            if ((Grapple != null && Grapple.IsControllingMovement) ||
                (GrappleSniper != null && GrappleSniper.IsActive)) return;
            if (_chatOpen || !_health.IsAlive) return;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame) ReloadAmmo();
            if (keyboard == null || mouse == null) return;
            if (!DepthView && keyboard.escapeKey.wasPressedThisFrame) SetCursor(false);
            if (!DepthView && Cursor.lockState != CursorLockMode.Locked)
            {
                if (mouse.leftButton.wasPressedThisFrame) SetCursor(true);
                return;
            }

            if (DepthView)
            {
                if (TryGetDepthAim(mouse.position.ReadValue(), out Vector3 target))
                {
                    Vector3 facing = target - _root.position;
                    facing.y = 0f;
                    if (facing.sqrMagnitude > .0025f) _yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
                }
            }
            else
            {
                Vector2 look = mouse.delta.ReadValue() * MouseSensitivity;
                _yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - look.y, -70f, 75f);
            }
            Quaternion heading = Quaternion.Euler(0, _yaw, 0);
            float x = (keyboard.dKey.isPressed || DepthView && keyboard.rightArrowKey.isPressed ? 1 : 0)
                - (keyboard.aKey.isPressed || DepthView && keyboard.leftArrowKey.isPressed ? 1 : 0);
            float z = (keyboard.wKey.isPressed || DepthView && keyboard.upArrowKey.isPressed ? 1 : 0)
                - (keyboard.sKey.isPressed || DepthView && keyboard.downArrowKey.isPressed ? 1 : 0);
            Vector3 input = Vector3.ClampMagnitude(new Vector3(x, 0, z), 1f);
            Vector3 direction = DepthView ? input : heading * input;
            bool grounded = _controller.isGrounded;
            bool rolling = Time.time < _rollEnd; if (_status.CannotMove) { input = Vector3.zero; direction = Vector3.zero; rolling = false; _rollEnd = 0f; }
            if (!_status.CannotMove && grounded && !rolling && Time.time >= _nextRoll &&
                (keyboard.leftCtrlKey.wasPressedThisFrame || keyboard.rightCtrlKey.wasPressedThisFrame))
            {
                _rollDirection = input.sqrMagnitude > .01f ? direction.normalized : heading * Vector3.forward;
                _rollEnd = Time.time + RollDuration;
                _nextRoll = _rollEnd + RollCooldown;
                _root.rotation = Quaternion.LookRotation(_rollDirection);
                Body.CrossFadeInFixedTime("Roll", AnimationBlend, 0);
                rolling = true;
            }

            _aiming = !DepthView && mouse.middleButton.isPressed && !rolling;
            bool sprinting = !_aiming && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (!rolling) _root.rotation = heading;
            if (grounded && _verticalSpeed < 0f) _verticalSpeed = -2f;
            if (!_status.CannotMove && grounded && !rolling && keyboard.spaceKey.wasPressedThisFrame)
            {
                _verticalSpeed = Mathf.Sqrt(2f * Gravity * JumpHeight);
                Body.CrossFadeInFixedTime("Jump", AnimationBlend, 0);
                _airborne = true;
                _falling = false;
            }
            _verticalSpeed -= Gravity * Time.deltaTime;
            UpdateOneWayPlatforms();
            Vector3 velocity = rolling ? _rollDirection * RollSpeed : direction * (sprinting ? SprintSpeed : MoveSpeed);
            CollisionFlags collision = _controller.Move((velocity + Vector3.up * _verticalSpeed) * Time.deltaTime);
            if ((collision & CollisionFlags.Above) != 0) _verticalSpeed = Mathf.Min(0f, _verticalSpeed);
            if (!rolling)
            {
                if (_controller.isGrounded && _verticalSpeed <= 0)
                {
                    if (_airborne || !Body.GetCurrentAnimatorStateInfo(0).IsName("Locomotion") && !Body.IsInTransition(0))
                        Body.CrossFadeInFixedTime("Locomotion", AnimationBlend, 0);
                    _airborne = _falling = false;
                }
                else if (_verticalSpeed < -.5f && !_falling)
                {
                    Body.CrossFadeInFixedTime("Fall", AnimationBlend, 0);
                    _airborne = _falling = true;
                }
            }
            Body.SetFloat(SPEED, input.magnitude * (sprinting ? 1f : .5f), .1f, Time.deltaTime);
            if (DirectionalMovement) DirectionalLocomotion.Update(Body, _root, velocity, sprinting);
            Body.SetLayerWeight(1, rolling ? 0 : 1);
            if (!rolling && !_status.IsStunned && mouse.leftButton.isPressed && Time.time >= _nextShot)
            {
                _nextShot = Time.time + ShotInterval;
                if (DepthView)
                {
                    _depthShotPending = true;
                    _depthShotPosition = mouse.position.ReadValue();
                }
                else
                {
                    UpdateView();
                    Fire(mouse.position.ReadValue());
                }
            }
        }

        private void UpdateOneWayPlatforms()
        {
            if (OneWayPlatforms.Length == 0) return;
            float feet = _controller.bounds.min.y;
            float tolerance = _controller.skinWidth * _root.lossyScale.y + .012f;
            foreach (BoxCollider platform in OneWayPlatforms)
            {
                if (platform == null) continue;
                bool ignore = _verticalSpeed > 0f || feet < platform.bounds.max.y - tolerance;
                if (Physics.GetIgnoreCollision(_controller, platform) != ignore)
                    Physics.IgnoreCollision(_controller, platform, ignore);
            }
        }

        private void LateUpdate()
        {
            UpdateView();
            if (_depthShotPending)
            {
                _depthShotPending = false;
                Fire(_depthShotPosition);
            }
            if (_tracer == null || !_tracer.enabled) return;
            float progress = Mathf.InverseLerp(_tracerStart, _tracerEnd, Time.time);
            float distance = Vector3.Distance(_tracerOrigin, _tracerTarget);
            float head = distance * progress;
            Vector3 direction = (_tracerTarget - _tracerOrigin).normalized;
            _tracer.SetPosition(0, _tracerOrigin + direction * Mathf.Max(0, head - TracerLength));
            Vector3 tip = _tracerOrigin + direction * head;
            _tracer.SetPosition(1, tip);
            if (_bullet != null) _bullet.position = tip;
            if (progress >= 1f)
            {
                _tracer.enabled = false;
                if (_bullet != null) _bullet.gameObject.SetActive(false);
            }
        }

        private void UpdateView()
        {
            if (GrappleSniper != null && GrappleSniper.ControlsView) return;
            if (DepthView)
            {
                float size = DepthViewSize;
                float height = Mathf.Approximately(DepthCameraOffset.y, 0f) ? Mathf.Max(CameraPivot.y, size - .16f) : CameraPivot.y;
                Vector3 focus = new Vector3(_root.position.x, height, _root.position.z * .35f);
                View.transform.SetPositionAndRotation(focus + DepthCameraOffset, Quaternion.LookRotation(-DepthCameraOffset));
                View.orthographicSize = size;
                if (!_chatOpen && _health.IsAlive && Mouse.current != null &&
                    TryGetDepthAim(Mouse.current.position.ReadValue(), out Vector3 target))
                {
                    // 총 위치를 기준으로 삼으면 그립 보정이 다음 프레임의 조준에 영향을 준다.
                    Vector3 origin = _aimChest != null ? _aimChest.position :
                        _aimSpine != null ? _aimSpine.position : _root.position + CameraPivot;
                    Vector3 direction = target - origin;
                    if (direction.sqrMagnitude > .0004f && Time.time >= _rollEnd)
                    {
                        Quaternion upperBodyRotation = ApplyUpperBodyAim(direction);
                        // 총 자체를 목표점으로 꺾지 않고 상체에 적용한 회전만 함께 적용한다.
                        Rifle.rotation = upperBodyRotation * _root.rotation;
                    }
                }
                if (Time.time < _rollEnd)
                {
                    _upperBodyPitch = _upperBodyYaw = 0f;
                    Rifle.rotation = _root.rotation;
                }
                if (RifleGrip != null) RifleGrip.Align();
                return;
            }
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0);
            Vector3 pivot = _root.position + CameraPivot;
            Vector3 offset = rotation * (_aiming ? FirstPersonOffset : ThirdPersonOffset);
            if (!_aiming && FindHit(pivot, offset.normalized, offset.magnitude, out RaycastHit wall))
                offset = offset.normalized * Mathf.Max(.05f, wall.distance - .12f);
            View.transform.SetPositionAndRotation(pivot + offset, rotation);
            View.fieldOfView = _aiming ? AimFov : NormalFov;
            Rifle.rotation = rotation;
            Rifle.gameObject.SetActive(!_aiming);
            ViewRifle.SetActive(_aiming);
            foreach (Renderer renderer in BodyRenderers) renderer.enabled = !_aiming;
        }

        private Quaternion ApplyUpperBodyAim(Vector3 direction)
        {
            Vector3 local = _root.InverseTransformDirection(direction);
            float pitch = Mathf.Atan2(-local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, -UpperBodyPitchLimit, UpperBodyPitchLimit) * UpperBodyAimWeight;
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -35f, 35f) * UpperBodyAimWeight;
            float blend = 1f - Mathf.Exp(-UpperBodyAimSharpness * Time.deltaTime);
            _upperBodyPitch = Mathf.Lerp(_upperBodyPitch, pitch, blend);
            _upperBodyYaw = Mathf.Lerp(_upperBodyYaw, yaw, blend);
            Transform anchor = _aimChest != null ? _aimChest : _aimSpine;
            if (anchor == null) return Quaternion.identity;
            Quaternion before = anchor.rotation;
            // 한쪽 본만 있는 리그에서도 상체 회전량 전체를 적용한다.
            if (_aimSpine != null) RotateAimBone(_aimSpine,
                _upperBodyPitch * (_aimChest != null ? .45f : 1f),
                _upperBodyYaw * (_aimChest != null ? .45f : 1f));
            if (_aimChest != null) RotateAimBone(_aimChest,
                _upperBodyPitch * (_aimSpine != null ? .55f : 1f),
                _upperBodyYaw * (_aimSpine != null ? .55f : 1f));
            return anchor.rotation * Quaternion.Inverse(before);
        }

        private void RotateAimBone(Transform bone, float pitch, float yaw)
        {
            bone.rotation = Quaternion.AngleAxis(yaw, _root.up) *
                Quaternion.AngleAxis(pitch, _root.right) * bone.rotation;
        }

        private bool TryGetDepthAim(Vector2 screenPosition, out Vector3 target)
        {
            if (AimGuide != null && AimGuide.TryGetTarget(screenPosition, out target)) return true;
            Ray ray = View.ScreenPointToRay(screenPosition);
            if (FindHit(ray.origin, ray.direction, ShotRange, out RaycastHit hit))
            {
                target = hit.point;
                return true;
            }
            var plane = new Plane(Vector3.forward, _root.position);
            if (plane.Raycast(ray, out float distance))
            {
                target = ray.GetPoint(distance);
                return true;
            }
            target = default;
            return false;
        }

        private bool FindHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            bool found = false;
            int count = Physics.RaycastNonAlloc(origin, direction, _hits, distance, ShotLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.transform.IsChildOf(_root) || found && hit.distance >= nearest.distance) continue;
                nearest = hit;
                found = true;
            }
            return found;
        }

        private void Fire(Vector2 screenPosition)
        {
            if (_ammo == null || !_ammo.TryFire())
            {
                if (_ammo != null && _ammo.ReserveAmmo > 0) ReloadAmmo();
                else ShowAmmoWarning();
                return;
            }
            if (_ammo.LoadedAmmo == 0 && _ammo.ReserveAmmo > 0) ReloadAmmo();
            else if (_ammo.IsOutOfAmmo) ShowAmmoWarning();
            Body.CrossFadeInFixedTime("Fire", .03f, 1, 0);
            if (DepthView)
            {
                Vector3 muzzle = Muzzle.position;
                Vector3 direction = Rifle.forward;
                float distance = ShotRange;
                if (TryGetDepthAim(screenPosition, out Vector3 depthTarget))
                {
                    Vector3 offset = depthTarget - muzzle;
                    if (offset.sqrMagnitude > .0025f)
                    {
                        direction = offset.normalized;
                        distance = Mathf.Min(ShotRange, offset.magnitude + .05f);
                    }
                }
                bool blocked = FindHit(muzzle, direction, distance, out RaycastHit depthHit);
                ShowTracer(muzzle, blocked ? depthHit.point : muzzle + direction * distance);
                if (AimGuide != null && AimGuide.TryGetHeadTarget(screenPosition, out SurvivalZombie head) &&
                    Vector3.Distance(muzzle, head.HeadAimPoint) <= ShotRange &&
                    (!blocked || depthHit.collider.GetComponentInParent<SurvivalZombie>() == head))
                    HitMonster(head.HitPoints, head, HeadDamage,
                        blocked ? depthHit.point : head.HeadAimPoint);
                else if (blocked) DamageTarget(depthHit);
                return;
            }
            Ray aim = View.ViewportPointToRay(new Vector3(.5f, .5f));
            bool hitTarget = FindHit(aim.origin, aim.direction, ShotRange, out RaycastHit hit);
            Vector3 target = hitTarget ? hit.point : aim.GetPoint(ShotRange);
            Vector3 origin = _aiming
                ? ViewRifle.transform.TransformPoint(Vector3.forward * .5f)
                : Muzzle.position;
            if (!_aiming)
            {
                Vector3 shot = target - origin;
                hitTarget = FindHit(origin, shot.normalized, shot.magnitude + .02f, out hit);
                if (hitTarget) target = hit.point;
            }
            ShowTracer(origin, target);
            if (hitTarget) DamageTarget(hit);
        }

        private void ReloadAmmo()
        {
            if (_ammo == null) return;
            AmmoReloadResult result = _ammo.TryReload();
            if (result == AmmoReloadResult.NoReserveAmmo) ShowAmmoWarning();
        }

        private void ShowAmmoWarning()
        {
            _ammoWarningEnd = Time.unscaledTime + AmmoWarningDuration;
        }

        private void ShowTracer(Vector3 origin, Vector3 target)
        {
            if (CombatEffects != null) CombatEffects.Flash((target - origin).normalized, Muzzle);
            _tracerOrigin = origin;
            _tracerTarget = target;
            _tracerStart = Time.time;
            _tracerEnd = Time.time + TracerDuration;
            _tracerMaterial.SetColor("_BaseColor", TracerColor);
            _tracer.widthMultiplier = TracerWidth;
            _tracer.SetPosition(0, origin);
            _tracer.SetPosition(1, origin);
            _tracer.enabled = true;
            if (_bullet != null && (target - origin).sqrMagnitude > .000001f)
            {
                _bullet.SetPositionAndRotation(origin, Quaternion.LookRotation(target - origin) * Quaternion.Euler(BulletModelRotation));
                _bullet.gameObject.SetActive(true);
            }
        }

        private bool IsHeadHit(RaycastHit hit)
        {
            SurvivalZombie zombie = hit.collider.GetComponentInParent<SurvivalZombie>();
            return zombie != null && Vector3.Distance(hit.point, zombie.HeadAimPoint) <= zombie.HeadHitRadius;
        }
        public void Melee()
        {
            if (_health == null || !_health.IsAlive || _chatOpen || _status.IsStunned || Time.time < _nextMelee) return;
            _nextMelee = Time.time + .6f;
            foreach (SurvivalZombie zombie in SurvivalZombie.Active)
            {
                if (zombie == null || !zombie.HitPoints.IsAlive) continue;
                Vector3 offset = zombie.transform.position - _root.position;
                if (offset.sqrMagnitude > MeleeRange * MeleeRange || Vector3.Dot(_root.forward, offset.normalized) < .3f) continue;
                if (Physics.Linecast(_root.position + Vector3.up, zombie.AimPoint, out RaycastHit wall, ShotLayers, QueryTriggerInteraction.Ignore) && wall.collider.GetComponentInParent<SurvivalZombie>() != zombie) continue;
                HitMonster(zombie.HitPoints, zombie, MeleeDamage, zombie.AimPoint);
            }
        }
        public void ResetMotion()
        {
            _verticalSpeed = 0f;
            _rollEnd = 0f;
            _depthShotPending = false;
        }

        // Sniper reuses the rifle tracer, projectile model, impacts and surface marks.
        public bool FireSniper(Transform muzzle, float bodyDamage, float headDamage, float range)
        {
            if (_health == null || !_health.IsAlive || _chatOpen || _status.IsStunned || muzzle == null) return false;
            Ray aim = View.ViewportPointToRay(new Vector3(.5f, .5f));
            bool found = FindHit(aim.origin, aim.direction, range, out RaycastHit hit);
            Vector3 target = found ? hit.point : aim.GetPoint(range);
            Vector3 origin = muzzle.position;
            Vector3 direction = target - origin;
            found = FindHit(origin, direction.normalized, direction.magnitude + .02f, out hit);
            if (found) target = hit.point;
            ShowTracer(origin, target);
            if (Body != null) Body.CrossFadeInFixedTime("Fire", .03f, 1, 0f);
            if (found) DamageTarget(hit, bodyDamage, headDamage);
            return true;
        }

        private void DamageTarget(RaycastHit hit) => DamageTarget(hit, ShotDamage, HeadDamage);

        private void DamageTarget(RaycastHit hit, float bodyDamage, float headDamage)
        {
            Health victim = hit.collider.GetComponentInParent<Health>();
            if (victim != null && victim.Team == CombatTeam.Monster)
                HitMonster(victim, hit.collider.GetComponentInParent<SurvivalZombie>(), IsHeadHit(hit) ? headDamage : bodyDamage, hit.point);
            else if (victim == null && CombatEffects != null) CombatEffects.MarkSurface(hit);
        }

        private void HitMonster(Health victim, SurvivalZombie zombie, float damage, Vector3 point)
        {
            if (!victim.IsAlive || damage <= 0f) return;
            if (CombatEffects != null) CombatEffects.ShowMonsterImpact(point);
            victim.TakeDamage(damage, gameObject);
            if (zombie != null) zombie.ReactToShot(this);
        }

        private void RefreshCompanionDialogue(string _) => _lastCompanionDialogue = null;

        private void SetChatInput(bool open)
        {
            _chatOpen = open;
            _aiming = false;
            SetCursor(!open && !DepthView);
            if (!open && AimGuide != null) Cursor.visible = false;
        }

        private static void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void OnDisable() => SetCursor(false);

        private void OnDestroy()
        {
            if (Companion != null && Companion.Llm != null) Companion.Llm.Replied -= RefreshCompanionDialogue;
            if (_tracerMaterial != null) Destroy(_tracerMaterial);
        }

        public bool HideControlsHud { get; set; } = true;

        private void OnGUI()
        {
            if (_ammo != null)
            {
                GUI.Label(new Rect(Screen.width - 190, Screen.height - 42, 180, 30),
                    $"탄약 {_ammo.LoadedAmmo} / {_ammo.TotalAmmo}");
                if (Time.unscaledTime < _ammoWarningEnd)
                {
                    _ammoWarningStyle ??= new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 28,
                        fontStyle = FontStyle.Bold
                    };
                    GUI.Label(new Rect(0, Screen.height * .42f, Screen.width, 48), "장탄수 부족", _ammoWarningStyle);
                }
            }
            if (_health == null || Companion == null) return;
            if (!HideControlsHud) GUI.Box(new Rect(12, 12, 500, 78),
                $"HP {_health.Current:0} / {_health.MaxHealth:0}   |   CPC {Companion.HitPoints.Current:0} / {Companion.HitPoints.MaxHealth:0}\n" +
                "WASD Move  Shift Sprint  Space Jump  Ctrl Roll\n" +
                (DepthView ? "Mouse Aim  LMB Fire  Enter Chat" : "LMB Fire  RMB Aim  Enter Chat  Esc Cursor"));
            if (!_chatOpen && !DepthView) GUI.Label(new Rect(Screen.width * .5f - 5, Screen.height * .5f - 10, 20, 20), "+");
            DrawCompanionDialogue();
            if (_chatOpen) GUI.Label(new Rect(14, 92, Mathf.Min(Screen.width - 28, 600), 25), Companion.Llm.Status);
        }

        private void DrawCompanionDialogue()
        {
            string dialogue = Companion.Dialogue;
            if (_lastCompanionDialogue != dialogue)
            {
                _lastCompanionDialogue = dialogue;
                _companionDialogueEnd = Time.unscaledTime + CompanionDialogueDuration;
            }
            if (string.IsNullOrWhiteSpace(dialogue) || Time.unscaledTime >= _companionDialogueEnd) return;

            Vector3 anchor = _companionHead != null ? _companionHead.position :
                Companion.transform.position + Vector3.up * (DepthView ? .35f : 1.8f);
            Vector3 screen = View.WorldToScreenPoint(anchor + Vector3.up * CompanionBubbleOffset);
            if (screen.z <= View.nearClipPlane || !View.pixelRect.Contains(screen)) return;

            _companionBubble ??= new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                richText = false,
                normal = { textColor = Color.white },
                padding = new RectOffset(2, 2, 2, 2)
            };
            _companionBubble.fontSize = CompanionDialogueFontSize;
            _companionBubbleContent.text = dialogue;
            GUIContent content = _companionBubbleContent;
            float width = Mathf.Clamp(_companionBubble.CalcSize(content).x + 4f, 60f, Mathf.Min(360f, Screen.width - 16f));
            float height = _companionBubble.CalcHeight(content, width);
            float x = Mathf.Clamp(screen.x - width * .5f, 8f, Screen.width - width - 8f);
            float y = Mathf.Max(8f, Screen.height - screen.y - height);
            Color previousColor = GUI.color;
            Color previousContentColor = GUI.contentColor;
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            if (Event.current.type == EventType.Repaint)
                _companionBubble.Draw(new Rect(x, y, width, height), content, false, false, false, false);
            GUI.contentColor = previousContentColor;
            GUI.color = previousColor;
        }
    }
}








