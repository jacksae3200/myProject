# 그래플 / 스나이퍼 연계

구현 요청: FEAT-002, FEAT-003, BUG-003, BUG-004. 대상 씬: Assets/Scenes/KayKit_DepthSurvival.unity.

## 조작

1. 가로등 전구의 원 안에 포인터를 올립니다. 사용 가능한 전구는 초록색 원으로 표시됩니다.
2. 우클릭하면 줄이 전구로 날아갑니다. 스윙은 플레이어와 전구를 잇는 수직 평면에서 진행됩니다. 위에서 보면 카메라 위치와 무관하게 전구를 향합니다. 측면에서는 아래쪽으로 돌며, 지면에 닿을 만큼 낮으면 같은 평면 안에서 줄을 일찍 당기거나 원호의 낮은 구간을 바닥 높이에 맞춥니다. 출발점에서 전구를 지나 초기 거리의 절반만큼 더 간 지점에 도착합니다. 경로가 장애물에 막히면 그래플을 취소합니다.
3. 도착하면 플레이어가 공중에서 멈추고 라이플을 등에, AWM을 오른손으로 옮깁니다. 기본 5초의 실제 시간 동안 슬로모션과 1인칭 시점을 사용합니다.
4. 마우스로 둘러보고 우클릭을 누르는 동안 스코프를 조준합니다. 놓으면 줌이 풀립니다. 좌클릭은 사격입니다.
5. 시간이 끝나면 기본 시점으로 부드럽게 복귀하고 라이플을 다시 듭니다. Escape는 조기 복귀입니다. 채팅·포커스 상실·사망·컴포넌트 비활성화는 즉시 정리합니다.

우클릭 근접공격 바인딩은 제거했습니다. 기존 피해 판정은 SurvivalPlayer.Melee()에 public 메서드로 남아 있습니다. 그래플 전구 밖의 우클릭은 일반 상태에서 아무 동작도 하지 않습니다. 기존 중간 마우스 조준 경로는 유지됩니다.

## 카메라 해석

FIX-001 이후 그래플 이동 중에는 기본 3인칭 카메라를 유지합니다. 도착 이벤트에서 가장 가까운 보이는 좀비(없으면 CPC)를 선택한 뒤 플레이어 머리 위치의 1인칭으로 보간합니다. 비조준 상태에서는 총이 화면 오른쪽 아래에서 안쪽을 향합니다. 우클릭을 누르면 머리 기준 카메라 위치를 유지한 채 총이 화면 중앙으로 이동하고, scopeEyepoint가 카메라 정면에 정렬되면서 기존 배율로 줌합니다. 버튼을 놓으면 총이 오른쪽 아래로 돌아옵니다. CinematicTarget과 AutoSelectTarget은 도착 후 바라볼 대상을 설정합니다.

기본 5초는 카메라 진입 시간을 포함하며, 종료 시 슬로모션을 해제한 뒤 기본 시점으로 돌아갑니다. 조준·카메라 보간·반동 회복·사격 간격은 실제 시간 기준입니다. 캐릭터·적·기존 탄환 효과는 게임의 시간 배율을 따릅니다.

## Inspector 설정

Player의 두 컴포넌트에서 모든 새 조절 파라미터를 public 필드로 제공합니다.

| 컴포넌트 | 주요 필드 | 의미 |
|---|---|---|
| PlayerGrappleAction | Anchors, MaxHookDistance, MinHookDistance | 명시된 전구만 대상으로 선택하며 거리와 장애물 검사. `Max Hook Distance`를 설정하면 이 값 안에서만 원이 표시되고 발사됩니다. 기본 생존 씬 값은 8 Unity 단위입니다. |
| PlayerGrappleAction | ExtensionSpeed, SwingDuration | 줄 발사 속도와 180도 스윙 시간 |
| PlayerGrappleAction | MinimumRingPixels, RopeWidth, ReadyColor | 조준 원·줄 표시 |
| PlayerGrappleAction | MaximumHoldSeconds | 스나이퍼가 없는 경우의 최대 공중 대기 시간. 활성 스나이퍼는 자신의 종료 시간을 사용 |
| GrappleSniperAction | SlowMotionSeconds, SlowMotionScale | 실제 시간 기준 지속시간과 시간 배율 (기본 5초 / 0.2) |
| GrappleSniperAction | CameraTransitionSeconds, AimSeconds | 1인칭/복귀 보간과 스코프 전환 시간 |
| GrappleSniperAction | FirstPersonFov, ScopedFov, EyeRelief | 시야각과 스코프 접안 거리 |
| GrappleSniperAction | BodyDamage, HeadDamage, ShotInterval, ShotRange | 몸/머리 피해, 사격 간격, 사거리 |
| GrappleSniperAction | LookSensitivity, RecoilDegrees, RecoilDistance, RecoilRecovery | 조준 감도와 반동 |
| GrappleSniperAction | CinematicTarget, AutoSelectTarget | 그래플 도착 후 1인칭의 초기 시선 대상 |
| GrappleSniperAction | ScreenPixelMaterials, ScreenPixelProperty, ScreenPixelMultiplier | 실제 화면 후처리 픽셀 수치의 저장·2배 보간·복구 |
| GrappleSniperAction | HandSocket, BackSocket, SniperHandGrip | 손 소켓·등 소켓·양손 그립 |
| GrappleSniperAction | HipScopeOffset, HipWeaponAngles | 카메라 기준 비조준 스코프 위치와 총 각도 (도 단위) |
| GrappleSniperAction | PixelActors, PixelResolutionMultiplier | 대상별 MaxResolution 기준 해상도 증가 (기본 2배) |
| ActorPixelQuad | FocusResolutionLimit | 전환 해상도 상한 (기본 2048) |

거리 기반 픽셀 해상도는 기본 상태에서 유지됩니다. 1인칭 전환 동안은 각 ActorPixelQuad의 MaxResolution부터 2배까지 거리와 무관하게 변경합니다 (128 → 256). 전환용 9단계 RenderTexture를 준비하고 재사용하며, 복귀 완료 후 기존 거리 기반 경로로 돌아갑니다. PixelActors에 포함된 캐릭터 수·최대 해상도가 클수록 전환 텍스처 메모리가 늘어납니다.

스나이퍼는 라이플의 BulletModel, ShowTracer, 총구 빛·소리·피격·탄흔·혈흔 경로를 공유합니다. 총구 효과는 발사한 무기의 총구를 유지하므로 등에 있는 라이플에서 재생되지 않습니다.

## 에셋과 설치

- [플레이어용 AWM 프리팹](../../Assets/sniper/Prefabs/AWM_GrappleSniper.prefab)
- [원본 AWM 리소스](../../Assets/sniper/VCR_AWM)
- [렌즈·RenderTexture·노멀맵·셰이더](../../Assets/sniper/ScopeLens)
- [설치 도구](../../Assets/CpcPrototype/Editor/GrappleSniperSceneSetup.cs)

AWM 및 렌즈 에셋 이동은 AssetDatabase.MoveAsset으로 GUID를 유지했습니다. C# 기능 코드는 CpcPrototype 네임스페이스의 기존 Runtime/Survival 구조를 유지합니다. 현재 씬에는 설치가 저장되어 있습니다. 재연결이 필요하면 해당 씬에서 Play를 중지한 뒤 Tools/CPC/Install Grapple And Sniper를 실행합니다. 설치 도구는 현재 열린 생존 씬을 저장합니다.

## 그래플 전환 수정 (FIX-001, 2026-11-17)

그래플 이동 중에는 3인칭을 유지하고 도착 후에만 1인칭으로 전환합니다. 전구 6개의 콜라이더와 삭제된 OneWayPlatforms 참조를 제거했습니다. 후킹은 GrappleAnchor 마커로 유지됩니다. 실제 화면 후처리 CityPixelPass의 _PixelHeight도 1인칭 전환에 맞춰 200→400으로 증가하고 복귀 시 200으로 돌아갑니다. 진입 당시 값을 저장하므로 임의 수치도 정확히 복원합니다. 기존 ActorPixelQuad 해상도 변화는 함께 유지됩니다. [검증 기록](Fix001/README.md).

## 렌즈 Shader Graph (2026-11-17)

ScopeLens.mat는 ScopeLensGraph.shadergraph를 사용합니다. 원본 ScopeLens.shader는 보존했으며 렌즈 텍스처·수치·씬 및 프리팹 참조를 유지했습니다. [그래프 구성 및 원본 비교 검증](ScopeLensGraph/README.md).

## 최초 구현 검증 (2026-11-16)

- Unity 6000.6.3f1에서 전체 스크립트 컴파일 오류 0. 기존 패키지 및 이전 코드의 obsolete 경고는 남아 있습니다.
- [통합 검사: 57 PASS](Checks.txt): 실제 Mouse 입력의 전구 선택/우클릭, 두 이동 방식, 충돌, 장비 교체, 보간, 거리 독립 2배 해상도, scopeEyepoint 중앙, RMB 해제, 실제 레이캐스트 몸 피해, 반동, 사격 간격, 시간/장비 복원.
- [경계 및 회귀: 13 PASS](BoundaryChecks.txt): 채팅, 포커스 상실 콜백, 사망, 줄이 날아가는 중 새 장애물, 조절한 공중 대기 시간, 종료 후 WASD 및 기존 라이플 피해.
- [씬 재로드: 13 PASS](SceneChecks.txt): 전구 6개, 등/손·양손 그립, 프리팹 유일 마커, 무기 레이어, 카메라/리스너, 렌즈, 이동한 의존성 및 테스트용 타이밍 변경 미저장.
- [플레이 Console](ConsoleErrors.json): 실행 오류 0. 기존 GPUResidentDrawer / Forward+ 설정 경고는 재현되었고 BUG-001에 기록했습니다.
- 화면 확인: [초록 타깃](../../Screenshots/GrappleSniper/GreenTarget.png), [스코프 조준](../../Screenshots/GrappleSniper/Scoped.png), [복귀](../../Screenshots/GrappleSniper/Returned.png).

실제 씬의 CharacterController, 물리 레이캐스트와 InputSystem 가상 장치를 사용했습니다. 마이크·외부 모델·플레이어 빌드 검사는 수행하지 않았습니다. 포커스 취소는 Unity 콜백을 직접 호출한 검사이며 OS 창 전환 자동화는 하지 않았습니다.

## 검사 재실행

현재 통합 검사는 기본 타이밍(5초)과 씬 배치를 기준으로 최대 후킹 거리, 절반 거리 도착, 지상·공중 출발, 경로 충돌과 스나이퍼 전환을 확인합니다. 통합 검사와 방향 검사의 결과는 `Bug010` 폴더에 저장됩니다. Play 상태에서 다음 명령을 사용합니다.

~~~powershell
unity command script run --project-path . -- --file AgentScripts/GrappleSniperChecks.cs --entry GrappleSniperChecks.Execute --timeout 50
unity command script run --project-path . -- --file AgentScripts/GrappleDirectionChecks.cs --entry GrappleDirectionChecks.Execute --timeout 40
unity command script run --project-path . -- --file AgentScripts/GrappleSniperBoundaryChecks.cs --entry GrappleSniperBoundaryChecks.Execute --timeout 40
~~~

검사 중에는 좀비/CPC AI를 정지시키고 테스트 위치·체력·가상 입력을 사용합니다. 경계 검사는 통합 검사 직후 실행하는 흐름입니다. 완료 후 반드시 Play를 중지해 기본 상태로 복원하세요. 씬 저장 검사는 Play를 중지하고 미저장 변경이 없는 상태에서 실행합니다.

~~~powershell
unity command script run --project-path . -- --file AgentScripts/GrappleSniperSceneChecks.cs
~~~

에디터가 포커스 없이 멈춘다면 검증 전에 editor set_auto_tick과 editor.playmode set_run_in_background를 사용하고, 종료 후 배경 실행 override를 복원합니다. 초기 씬의 미저장 변경은 작업 전에 Temp/BeforeGrapple/KayKit_DepthSurvival.unity로 보존했습니다.

## FixedUpdate 구조 (BUG-005)

PlayerGrappleAction.FixedUpdate는 실행 순서만 조율합니다. CanContinueTravel은 이동 가능 여부와 무효 상태 취소, TryEnterSwing은 줄 도착 재검사와 스윙 타이머 초기화, CalculateSwingTarget은 진행률 기반 원호 위치/기존 로그, TryMoveToSwingTarget은 CharacterController 이동 및 충돌 시 취소, CompleteSwing은 Holding 전환과 Arrived 이벤트를 담당합니다. 기존 동작과 public 인터페이스는 유지합니다. [검증](Bug005/README.md).
