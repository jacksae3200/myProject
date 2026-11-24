# 01. 구현 현황 및 변경 이력

마지막 정리: 2026-11-25

이 문서는 현재 구현을 확인하는 기준 문서입니다. 추가 요청은 [02_FEATURE_REQUESTS.md](02_FEATURE_REQUESTS.md), 버그·수정 요청은 [03_BUGS_AND_FIXES.md](03_BUGS_AND_FIXES.md)에 작성합니다. 작업 절차는 [agents.md](../agents.md)의 7절을 따릅니다.

## 기록 기준

- 구현 현황은 코드·씬·기존 문서를 확인한 초기 목록입니다. 에셋 패키지 전체의 기능 목록은 아닙니다.
- **코드 확인**은 해당 코드가 존재함을 뜻합니다. 씬 연결 및 실제 플레이 성공을 보장하지 않습니다.
- **기존 검증 기록 있음**은 아래 상세 문서에 과거 결과가 있다는 뜻입니다. FEAT-001 초기 문서 정리에서는 실행 검사를 반복하지 않았습니다. 이후 작업의 실제 검증은 각 항목과 변경 이력에 별도로 기록합니다.
- 앞으로 완료한 작업은 현황을 수정하고 변경 이력에 요청 ID, 변경 파일, 검증 결과를 함께 남깁니다. 미검증 작업은 완료로 기록하지 않습니다.

## 환경과 주요 씬

- Unity: 6000.6.10f1, URP 17.6.0, Input System 사용. 근거: ProjectSettings/ProjectVersion.txt, Packages/manifest.json.
- 게임 코드의 주요 네임스페이스: CpcPrototype.
- 생존 전투 기본 씬: [KayKit_DepthSurvival.unity](../Assets/Scenes/KayKit_DepthSurvival.unity).
- 전술 검증 씬: [KayKit_TacticsTest.unity](../Assets/Scenes/KayKit_TacticsTest.unity).
- 초기 CPC 테스트: [PicoCpcTest.unity](../Assets/CpcPrototype/Scenes/PicoCpcTest.unity).
- 빌드 시작 씬은 [KayKit_DepthSurvival.unity](../Assets/Scenes/KayKit_DepthSurvival.unity)입니다. `ProjectSettings/EditorBuildSettings.asset`에서 이 씬만 활성화되어 있습니다. 플레이 테스트용 `SampleScene`은 빌드 목록에 포함하지 않습니다.

## 현재 구현 목록

| ID | 영역 | 확인한 구현 | 근거 / 검증 범위 |
|---|---|---|---|
| SYS-001 | 초기 CPC 프로토타입 | 3인칭 이동·점프·공격·카메라, 체력, 행동 트리 몬스터, 동료 추종 및 동시 점프 | [초기 README](../Assets/CpcPrototype/README.md), Runtime/ThirdPersonPlayer.cs, CompanionCpc.cs, CubeMonsterBrain.cs. 기존 검증 기록 있음 |
| SYS-002 | 로컬 LLM·채팅 | Ollama HTTP 대화, 사건 큐와 대화 우선 처리, 응답 상태·실패 대사, GPU 오류 시 CPU 재시도, 한국어 IME 채팅 | [LocalLlmClient.cs](../Assets/CpcPrototype/Runtime/LocalLlmClient.cs), [CpcChatPanel.cs](../Assets/CpcPrototype/Runtime/CpcChatPanel.cs), 초기 README 및 전술 문서. 기존 검증 기록 있음 |
| SYS-003 | 대사 정책 | 시스템 프롬프트·금지 표현 필터·대체 대사, 출력 전 검사 | [CpcResponseFilter.cs](../Assets/CpcPrototype/Runtime/CpcResponseFilter.cs), CpcResponsePolicy.cs, Editor/CpcResponseFilterChecks.cs. 코드 및 기존 설명 확인 |
| SYS-004 | 생존 플레이어 전투 | 총격·머리 판정·근접공격·이동·구르기·조준, 상태 이상 처리 | [SurvivalPlayer.cs](../Assets/CpcPrototype/Survival/SurvivalPlayer.cs), CombatStatus.cs, KayKitAimGuide.cs, [전투 설명](../Assets/CpcPrototype/Survival/CombatFeatures.md). 코드 확인 |
| SYS-005 | 좀비 6종 | Normal, Spitter, Boomer, Crawler, Burrower, Jumper 및 종류별 스킬 컴포넌트, 기존 설정 이전 지원 | [전투 설명](../Assets/CpcPrototype/Survival/CombatFeatures.md), SurvivalZombie.cs, ZombieAbility.cs, 종류별 Ability.cs, ZombieAbilitySelection.cs. 코드 확인 |
| SYS-006 | 방패 동료·스킬 | 추종·대기·도발·근접공격·방어 피해 감소·저체력 후퇴, 돌진·밀치기 | [ShieldCompanion.cs](../Assets/CpcPrototype/Survival/ShieldCompanion.cs), CpcSkills.cs, 전투 및 전술 문서. 코드 및 관련 기존 검증 기록 있음 |
| SYS-007 | 전술 판단 | 관측 위협·JSON 행동 검증, 대상 자동 선택, Follow/Hold 유지, intercept·evade·counterattack, NavMesh 경로와 실행 직전 재검증, 긴급 안전 대응 | [전술 README](../Tools/Tactics/README.md), [전술 검증 기록](../Tools/Tactics/VALIDATION.md), CpcTactics.cs, CpcThreatSensor.cs, CpcTacticalMotor.cs. 기존 Unity/실제 모델 검사 기록 있음 |
| SYS-008 | 따라오며 보호·군집 보호 | 추종 중 주변 교전과 복귀, 비전투 추종, 군집 시 플레이어 곁 방어·도발, 방향 기반 근접 피해 대리 수신 | [전술 README 후속 변경](../Tools/Tactics/README.md), [CpcCrowdProtection.cs](../Assets/CpcPrototype/Survival/CpcCrowdProtection.cs), CpcShieldInterceptor.cs. 기존 FollowProtection 및 CrowdProtection 검사 파일 있음 |
| SYS-009 | 한국어 음성 명령 | M 키 녹음, 로컬 Whisper STT, BatchInference/SlidingWindow, 최종 문장 1회 전송, 취소·포커스·채팅 입력 처리, 지연 측정 및 WAV 비교 | [음성 README](../Tools/Voice/README.md), [슬라이딩 윈도우](../Tools/Voice/SlidingWindow.md), [음성 검증 기록](../Tools/Voice/VALIDATION.md). 가짜 장치 검사와 실제 합성 WAV 서버 비교의 과거 기록 있음 |
| SYS-010 | 캐릭터·전투 연출 | 방향별 이동 표현, 픽셀 쿼드·바닥 그림자, 발소리·총격·피격·발자국 효과, 장비 그립 | [Survival 폴더](../Assets/CpcPrototype/Survival), DirectionalLocomotion.cs, ActorPixelQuad.cs, PixelGroundShadow.cs, KayKitCombatEffects.cs, KayKitFootsteps.cs, EquipmentGrip.cs. 코드 확인 |
| SYS-011 | 스코프 렌즈 | 카메라 자기 렌더링 제외, 렌더러 상태 복구, 원형 마스크·노멀 왜곡·확대·틴트, 알파 조준선 합성과 안쪽 원 바깥의 검은 그라데이션 | [ScopeLens.cs](../Assets/CpcPrototype/Survival/ScopeLens.cs), [Shader Graph](../Assets/sniper/ScopeLens/ScopeLensGraph.shadergraph), [편집 안내](../Tools/GrappleSniper/ScopeLensGraph/README.md). BUG-002, FEAT-005, BUG-006 렌더·플레이 검증 기록 있음 |
| SYS-012 | 환경 편집·비교 도구 | 환경 저폴리·픽셀 머티리얼 편집, Viking 텍스처 복제·픽셀화, 비교 촬영 도우미 | [Viking 설명](../Assets/Editor/VikingTexturePixelizer.md), [환경 도구](../Assets/scripts), [편집기 도구](../Assets/Editor). 코드 및 기존 설명 확인 |
| SYS-013 | 문서 기반 작업 관리 | 구현 현황, 기능 요청, 버그·수정 요청의 3개 문서와 agents.md 작업 전후 갱신 규칙 | FEAT-001. 문서 경로·필수 항목·상호 링크 정적 확인 |
| SYS-014 | 가로등 그래플 | 전구 6개 타깃·초록색 링·줄 발사, 플레이어와 전구를 잇는 수직 평면 안에서 스윙해 반대편의 초기 거리 절반 지점에 도착, Inspector 최대 거리 설정, 장애물 검사·취소 | FEAT-002, BUG-010. [가이드 및 검증](../Tools/GrappleSniper/README.md), PlayerGrappleAction.cs, GrappleAnchor.cs. 가상 입력·궤적·충돌·스나이퍼 연계 확인 |
| SYS-015 | 그래플 후 스나이퍼 | Assets/sniper의 AWM 프리팹·등/손 소켓 교체·그래플 도착 후 1인칭·5초 슬로모션·화면 픽셀 수치 2배/복구·부드러운 1인칭/3인칭·오른쪽 아래 비조준 배치·RMB 시 스코프가 중앙으로 이동·LMB 사격·반동·기존 탄환과 효과 공유·거리 독립 최대 2배 픽셀 해상도 | FEAT-003, BUG-004. [가이드 및 검증](../Tools/GrappleSniper/README.md), GrappleSniperAction.cs. 비조준/조준 전환 및 사격·복귀 플레이 확인 |
| SYS-016 | Downtown 직선 도시 환경 | Downtown City 도로·보도 4타일, 양쪽 건물 6채씩, 3인칭 카메라 쪽 건물 렌더 컬링 및 1인칭/스코프에서 해제. 기존 그래플 전구·맵 바닥·게임플레이 유지 | FIX-003. [설정 및 검증](../Tools/Fix003/README.md), StreetBuildingCulling.cs, DowntownStreetSetup.cs. 플레이·회귀·재로드 검증 통과 |
| SYS-017 | Unity 독립 Core 첫 분리 | `CpcPrototype.Core` asmdef에서 Unity 참조 없이 행동 트리 실행. NUnit 테스트는 Unity Edit Mode와 별도 .NET CI 실행 경로를 구성, Actions 결과는 FEAT-006 검증 대기 | [Core](../Assets/CpcPrototype/Core), [테스트](../Assets/CpcPrototype/Tests/EditMode), [CI/CD 가이드](CI_CD.md). Unity Editor 기반 테스트 미실행 |
| SYS-018 | 탄약 규칙과 플레이어 연동 | 발사·재장전·잔탄 규칙을 순수 `AmmoInventory` Core로 분리하고 R키, 빈 탄창 자동 재장전, 탄약 HUD/소진 경고를 SurvivalPlayer에 연결 | FEAT-004. [AmmoInventory](../Assets/CpcPrototype/Core/AmmoInventory.cs), [SurvivalPlayer](../Assets/CpcPrototype/Survival/SurvivalPlayer.cs), [전투 안내](../Assets/CpcPrototype/Survival/CombatFeatures.md). NUnit 테스트 13개 통과(로컬 NUnit 실행), Unity 통합 플레이 미실행 |
| SYS-019 | GitHub 업로드 정리 | 로컬 녹화·생성 프리뷰를 Git에서 제외하고 필수 100 MiB 초과 텍스처 3개를 Git LFS로 관리 | FEAT-007. 저장소 준비 과정이며 앱 실행 및 원격 푸시는 별도 |
| SYS-020 | Windows 빌드 시작 씬 | `KayKit_DepthSurvival`을 유일한 활성 빌드 씬으로 설정. 로컬 Actions runner 폴더는 Git에서 제외 | FEAT-008. 씬 파일 경로·GUID 정적 확인, 실제 Player 빌드 미실행 |
| SYS-021 | Windows Player CI 빌드 | self-hosted Windows x64 runner에서 Unity batch mode 빌드 후 GitHub Actions artifact로 보관 | FEAT-009, BUG-011. 첫 실행의 artifact 업로드 실패를 수정 중; 재실행 및 산출물 확인 대기 |

## 상세 문서와 검증 시 주의점

- 기존 README·CombatFeatures·Tools 문서는 세부 사용법과 과거 검증 근거로 유지합니다. 현재 요약과 요청 상태는 이 3개 문서에서 관리합니다.
- 초기 CPC README의 조작·성능 수치는 초기 테스트 기준입니다. 생존 씬의 실제 수치는 해당 씬·프리팹·설정 에셋을 확인합니다.
- 초기 README의 gemma4:26b와 후속 전술 검증의 gemma4:12b는 시점이 다른 기록입니다. 현재 사용할 모델은 GemmaLocalSettings 에셋을 확인하며 문서 정리만으로 변경하지 않습니다.
- 짧은 공격 예고에서 LLM 응답을 항상 기다릴 수 없으므로 Unity의 즉시 안전 대응을 사용합니다. 예고 시간을 늘린 과거 모델 검사를 기본 전투 타이밍 보장으로 해석하지 않습니다.
- 실제 사용자 마이크·소음 환경·Windows 권한 UI의 검증은 별도로 필요합니다. 합성 WAV 1회 비교는 일반적인 인식 품질이나 성능 보장이 아닙니다.
- 과거 GPU Resident Drawer/Forward+ 경고는 BUG-001에서 재확인 대상으로 관리합니다.

## 변경 이력

최신 항목을 위에 추가합니다. 기존 기능의 최초 구현 날짜는 확인되지 않아 임의로 부여하지 않았습니다.

### 2026-11-25 — BUG-011 · Windows 빌드 artifact 업로드 경로 수정

- 최신 실행 로그에서 runner 임시 출력 확인과 workspace 복사 단계를 통과한 뒤, `upload-artifact`가 상대 경로 디렉터리 입력에서 파일을 찾지 못한 것을 확인했습니다. 업로드 입력을 `${{ github.workspace }}\artifacts\Windows`로 지정하고, workspace 절대 경로와 파일 경로를 사전에 출력하도록 바꿨습니다. 생성 폴더는 `.gitignore`에 추가했습니다.
- 검증: GitHub 로그에서 upload 단계에 도달함을 확인했습니다. 새 absolute path 입력 재실행은 남아 있어 BUG-011은 검증 대기입니다.
- 변경: `.github/workflows/windows-player-build.yml`, `.gitignore`, `Assets/CpcPrototype/Editor/WindowsPlayerBuild.cs`, `Docs/03_BUGS_AND_FIXES.md`, `Docs/02_FEATURE_REQUESTS.md`, `Docs/01_IMPLEMENTED.md`, `Docs/CI_CD.md`.
- 다음 행동: 수정사항을 푸시한 뒤 Actions의 출력 파일 목록과 artifact를 확인합니다.

### 2026-11-25 — FEAT-008 · 생존 씬을 빌드 시작 씬으로 설정

- `ProjectSettings/EditorBuildSettings.asset`의 유일한 활성 씬을 `Assets/Scenes/KayKit_DepthSurvival.unity`로 변경했습니다. `.meta`의 GUID `54bb00d9487e7cb4f9334c07994eaba7`와 설정 GUID가 일치하는지 확인했습니다. `SampleScene` 에셋은 삭제하지 않았으며 빌드 목록에서만 제외했습니다.
- GitHub runner를 프로젝트 내부에 풀어 둔 로컬 `actions-runner/` 폴더가 `git add .`에 들어가지 않도록 `.gitignore`에 추가했습니다. 기존 runner 파일은 건드리지 않았습니다.
- 검증: 빌드 씬 파일 존재, 유일한 활성 씬 경로 및 GUID 정적 대조, `git diff --check`. Unity Player 빌드와 플레이 실행은 미실행.
- 변경: `ProjectSettings/EditorBuildSettings.asset`, `.gitignore`, `Docs/01_IMPLEMENTED.md`, `Docs/02_FEATURE_REQUESTS.md`, `Docs/CI_CD.md`.
- 남은 사항: Windows x64 빌드 workflow와 Butler 배포 단계는 별도 작업입니다.

### 2026-11-25 — FEAT-009 · GitHub Actions Windows x64 플레이어 빌드

- `Assets/CpcPrototype/Editor/WindowsPlayerBuild.cs`에 활성 Build Settings 씬을 Windows x64 대상으로 빌드하는 Unity batch mode 진입점을 추가했습니다. 출력 경로를 환경 변수로 받고, 빌드 실패를 workflow 실패로 반환합니다.
- `.github/workflows/windows-player-build.yml`은 `main` 푸시 및 수동 실행에서 Git LFS 파일을 체크아웃하고, `ProjectVersion.txt`와 일치하는 Unity Editor를 self-hosted Windows x64 runner에서 찾아 빌드합니다. 성공한 플레이어 파일을 14일 보관 Actions artifact로 업로드합니다. PR 이벤트는 self-hosted runner가 외부 PR 코드를 실행하지 않도록 트리거에 넣지 않았습니다.
- 검증: 프로젝트 버전 6000.6.10f1, 활성 빌드 씬 경로와 파일 존재, workflow의 runner/수동 트리거/artifact 업로드, 빌드 진입점의 Windows x64 타깃을 정적 확인했습니다. `git diff --check` 통과. Unity Editor 빌드와 Actions runner 실행은 아직 수행하지 않았으므로 FEAT-009는 검증 대기입니다.
- 변경: `Assets/CpcPrototype/Editor/WindowsPlayerBuild.cs` 및 `.meta`, `.github/workflows/windows-player-build.yml`, `Docs/02_FEATURE_REQUESTS.md`, `Docs/01_IMPLEMENTED.md`, `Docs/CI_CD.md`.
- 앞선 실행들은 출력 폴더가 비거나 upload-artifact 입력 경로에서 파일을 찾지 못해 종료됐습니다(BUG-011). 확인한 파일을 workspace `artifacts/Windows`로 복사하고 절대 경로 입력으로 올리도록 수정했습니다. 수정 workflow 재실행/다운로드가 남았습니다. itch.io 업로드는 이 workflow에 포함하지 않았습니다.

### 2026-11-24 — FEAT-007 · GitHub 업로드를 위한 저장소 정리

- `Recordings/`, `Assets/VikingTexturePreview/`, `CpcCleanVideos/`, `CpcTacticalVideos/`, `CpcComparisonVideos/`를 `.gitignore`에 추가했습니다. 추적 대상에서 제외했지만 작업 폴더의 파일은 보존했습니다.
- 일반 Git 100 MiB 제한을 넘는 필수 원본 텍스처 3개를 `.gitattributes`에서 Git LFS 대상으로 지정했습니다. Unity가 재생성하는 필수 프로젝트 파일·원본 에셋은 유지했습니다.
- 최초 업로드의 단일 팩은 2.38 GiB로 GitHub의 2 GiB 제한을 넘었습니다. 초기 이력을 코드/설정, Viking Village, 주요 에셋, 나머지 에셋의 4개 순차 커밋으로 다시 구성했습니다. 원본 최초 커밋과 정리된 단일 커밋은 각각 `backup-initial-upload-before-cleanup`, `backup-cleaned-single-commit` 브랜치에 보존했습니다.
- 검증: Git 상태 깨끗함. `git ls-tree`에서 일반 Git 100 MiB 초과 blob 없음. `git lfs ls-files`에 텍스처 3개 확인. 녹화/프리뷰 폴더는 `!!`로 무시되고 로컬 경로 존재 확인. GitHub 원격 푸시는 아직 실행하지 않았습니다.
- 변경: `.gitignore`, `.gitattributes`, `Docs/02_FEATURE_REQUESTS.md`, `Docs/01_IMPLEMENTED.md`, 초기 커밋 체인.

### 2026-11-24 — FEAT-004 · 라이플 탄약 제한과 재장전

- `AmmoInventory`를 Unity 참조 없는 Core에 추가하고 탄약 감소, 0발 차단, 빈 탄창 30발/약실 포함 31발 재장전, 예비 탄약만큼 부분 재장전 규칙을 구현했습니다. `SurvivalPlayer`에 초기 장탄/예비탄 설정, R키 수동 장전, 마지막 탄 사용 후 자동 장전, 우측 하단 잔탄 표기와 2초 소진 안내를 연결했습니다.
- 테스트 우선으로 NUnit 테스트를 작성하고 실행 과정에서 총 잔탄 기대값 및 재장전 후 예비탄 수 기대값 오류를 찾아 수정했습니다. 최종 행동 트리 6개와 탄약 7개, 총 13개가 로컬 NUnit assertion 실행에서 통과했습니다. Unity 배치 실행은 리포트 서버 초기화 중 에디터가 중단되어 Test Runner 결과 XML 및 최종 SurvivalPlayer 통합 컴파일 결과를 확보하지 못했습니다.
- 변경: `Assets/CpcPrototype/Core/AmmoInventory.cs`, `Assets/CpcPrototype/Survival/SurvivalPlayer.cs`, `Assets/CpcPrototype/Survival/CombatFeatures.md`, `Assets/CpcPrototype/Tests/EditMode/AmmoInventoryTests.cs`, `Docs/01_IMPLEMENTED.md`, `Docs/02_FEATURE_REQUESTS.md`, `Docs/CI_CD.md`.
- 남은 검증: KayKit_DepthSurvival 씬에서 실제 입력·UI·사격/조준·전투 회귀 플레이를 확인해야 합니다. 이에 따라 FEAT-004는 검증 대기입니다.

### 2026-11-24 — FEAT-006 · Unity 독립 Core와 CI/CD 기반

- `BehaviorTree.cs`를 Unity 참조를 금지하는 `CpcPrototype.Core` 어셈블리로 이동하고, 순수 C# 동작 검증을 위한 NUnit Edit Mode 테스트 6개를 추가했습니다. 기존 `.meta` GUID를 유지했습니다.
- `.github/workflows/core-nunit-tests.yml`을 추가해 기존 Core NUnit 테스트 소스를 .NET 10에서 실행하고 TRX 결과를 보관합니다. Unity Edit Mode workflow는 GameCI 이미지가 준비되지 않아 수동 실행으로 제한했습니다. 별도 서버/백엔드 분리가 아니라 Unity 내부 Core와 엔진 어댑터 경계를 시작한 상태입니다.
- 검증 결과: 사용자가 GitHub Actions에서 Core NUnit workflow 통과를 확인했습니다. GameCI Unity Edit Mode workflow는 실패했습니다. 앞선 실행 로그에서는 `unityci/editor:ubuntu-6000.6.10f1-linux-il2cpp-3` 이미지를 찾지 못해 테스트 시작 전에 종료됐습니다. 이번 실패가 같은 원인인지 최신 로그 확인이 필요합니다. FEAT-006은 Unity 기반 검증이 남아 검증 대기입니다.

### 2026-11-17 — BUG-010 · 그래플 방향과 최대 후킹 거리

- 플레이어와 전구를 잇는 수직 평면에서 스윙합니다. 지면 여유가 부족할 경우 같은 평면에서 줄을 당기거나 최저 높이를 조절합니다. Inspector의 `Max Hook Distance`가 전구 표시와 발사에 적용됩니다.
- 변경 파일은 PlayerGrappleAction.cs, GrappleSniperChecks.cs, GrappleDirectionChecks.cs, CombatFeatures.md, 그래플 사용 안내와 검증 기록입니다.
- Unity 컴파일·실행 오류 0. [방향 20개](../Tools/GrappleSniper/Bug010/DirectionChecks.txt), [통합 68개](../Tools/GrappleSniper/Bug010/Checks.txt), [경계·회귀 13개](../Tools/GrappleSniper/Bug010/BoundaryChecks.txt) 통과. 플레이어 빌드는 미실행했습니다.

### 2026-11-17 — BUG-009 · 그래플 수평 경로 및 절반 거리 도착

- 위에서 봤을 때 전구를 향하는 수직 스윙 평면을 우선합니다. 지면을 피하기 위해 기울일 때는 카메라에서 멀어지는 방향으로 기울입니다. 스윙하는 동안 전구와의 거리가 계속 줄어 반대편의 초기 거리 절반 지점에 도착합니다.
- 변경 파일은 PlayerGrappleAction.cs, GrappleSniperChecks.cs, GrappleDirectionChecks.cs, 그래플 사용 안내와 검증 기록입니다.
- Unity 컴파일·실행 오류 0. [방향 20개](../Tools/GrappleSniper/Bug009/DirectionChecks.txt), [통합 66개](../Tools/GrappleSniper/Bug009/Checks.txt), [경계·회귀 13개](../Tools/GrappleSniper/Bug009/BoundaryChecks.txt) 통과. 플레이어 빌드는 미실행했습니다.

### 2026-11-17 — BUG-008 · 그래플 스윙 방향 제한

- 후킹 순간 화면에서 시작 줄보다 아래쪽인 원호를 선택합니다. 바닥에 맞춰 기울일 때도 같은 쪽만 허용하며, 경로가 막히면 그래플을 취소합니다.
- 변경 파일은 PlayerGrappleAction.cs, AgentScripts/GrappleDirectionChecks.cs, Tools/GrappleSniper/README.md 및 검증 기록입니다.
- Unity 컴파일·실행 오류 0. [방향 9개](../Tools/GrappleSniper/Bug008/DirectionChecks.txt), [통합 65개](../Tools/GrappleSniper/Bug008/Checks.txt), [경계·회귀 13개](../Tools/GrappleSniper/Bug008/BoundaryChecks.txt) 통과. 플레이어 빌드는 미실행했습니다.

### 2026-11-17 — BUG-007 · ProtectPlayer 컴파일 오류 해결

- CpcTactics.cs의 CpcAction enum에 누락된 ProtectPlayer = 512를 복원했습니다. 기존 0~256 플래그 값은 유지했습니다. CpcTacticalMotor 및 Editor/CpcTacticsSceneSetup의 참조가 다시 컴파일됩니다.
- 검증: Unity 재컴파일 성공, compilationFailed=false, Console 오류 0. 에디터에 로드된 enum의 기존 값 보존·512 복원·값 중복 없음 확인. 플레이·외부 모델·빌드 검사는 미실행. [요청 기록](03_BUGS_AND_FIXES.md#bug-007--protectplayer-액션-누락으로-인한-컴파일-오류).

### 2026-11-17 — BUG-006 · 스코프 바깥쪽 검은 그라데이션

- `ScopeLensGraph.shadergraph`에서 조준선 합성 뒤 정규화 반경 0.45부터 0.98까지 RGB를 검게 보간했습니다. [렌즈 편집 안내](../Tools/GrappleSniper/ScopeLensGraph/README.md).
- 셰이더 컴파일, 반경별 GPU 렌더 밝기 및 원형 마스크, 실제 씬 조준 화면과 조준 해제 후 복귀를 확인했습니다. 콘솔 오류 0. 플레이어 빌드는 미실행입니다. [요청과 검증](03_BUGS_AND_FIXES.md#bug-006--스코프-안쪽-원-바깥에-검은-그라데이션).

### 2026-11-17 — BUG-005 · 그래플 FixedUpdate 역할별 함수 분리

- PlayerGrappleAction.cs의 물리 틱 처리를 이동 가능 여부, 줄 도착 후 스윙 전환, 목표 위치 계산, 이동/충돌, 도착의 5개 private 메서드로 분리했습니다. 기존 타이밍·궤적·취소·이벤트·디버그 로그를 유지했습니다.
- 변경: PlayerGrappleAction.cs, Tools/GrappleSniper/README.md 및 BUG-005 요청/검증 기록.
- 검증: Unity 컴파일 오류 0, 실제 플레이 통합 65개·경계/회귀 13개 PASS, Console 실행 오류 0. [결과](../Tools/GrappleSniper/Bug005/README.md). 플레이어 빌드는 미실행.

### 2026-11-17 — FEAT-005 · 스코프 조준선 이미지 합성

- `ScopeLensGraph.shadergraph`에서 카메라 영상의 틴트·비네팅 다음에 조준선을 알파 합성하고, `ScopeLens.mat`의 `_ReticleTex`에 `Assets/sniper/reticle.png`를 연결했습니다. 원형 마스크와 원본 셰이더를 유지했습니다.
- Shader Graph 컴파일, GPU 렌더 비교, 실제 조준 표시·해제 및 복귀 검사에 통과했습니다. 플레이어 빌드는 미실행입니다. [요청 및 검사 범위](02_FEATURE_REQUESTS.md#feat-005--스코프-조준선-이미지-합성), [렌즈 편집 안내](../Tools/GrappleSniper/ScopeLensGraph/README.md).

### 2026-11-17 — BUG-004 · 그래플 스나이퍼 비조준 1인칭 구도

- 비조준 시 AWM을 화면 오른쪽 아래에 두고, 우클릭 시 총과 스코프를 중앙으로 보간합니다. 조준 시 기존 32도 시야각과 접안 거리를 적용합니다.
- 변경 파일은 [GrappleSniperAction.cs](../Assets/CpcPrototype/Survival/GrappleSniperAction.cs), [그래플 스나이퍼 가이드](../Tools/GrappleSniper/README.md), [BUG-004 요청 기록](03_BUGS_AND_FIXES.md#bug-004--그래플-스나이퍼-비조준-1인칭-구도)입니다.
- Unity 컴파일 및 실행 오류 0. 플레이 캡처와 가상 입력을 사용한 통합·회귀 검사 71개 통과. 플레이어 빌드는 미실행했습니다.

### 2026-11-17 — BUG-003 · 가로등 전구 중심 반원 그래플

- 후킹 위치를 전구 중심으로 180도 회전한 점에 도착합니다. 지면 여유가 부족할 때는 반경과 종착점을 유지하며 원호를 기울이고, 보도 턱처럼 진행을 막는 충돌체가 있으면 더 열린 쪽을 우선합니다.
- 변경 파일은 PlayerGrappleAction.cs, AgentScripts/GrappleSniperChecks.cs, CombatFeatures.md, Tools/GrappleSniper/README.md 및 검증 기록입니다.
- Unity 컴파일 및 실행 오류 0. [통합 65개](../Tools/GrappleSniper/Bug003/Checks.txt)와 [경계·회귀 13개](../Tools/GrappleSniper/Bug003/BoundaryChecks.txt) 통과. 플레이어 빌드는 미실행했습니다.

### 2026-11-17 — FIX-003: Downtown City 직선 거리 및 시점별 건물 컬링

- Assets/Downtown City MegaKit[Standard]의 Unity FBX 도로 4타일과 건물 3종을 사용하여 X축 24m 거리와 양쪽 6채씩을 배치했습니다. 보도 및 건물 충돌 14개를 추가하고 기존 바닥·경계 충돌과 그래플 전구 6개를 유지했습니다.
- 기존 KayKit 도로·건물·비그래플 장식은 씬에 비활성 보존했습니다. 변경 전 씬은 Temp/Fix003/Before.unity에 복사했습니다.
- StreetBuildingCulling이 메인 카메라 렌더 구간에만 카메라 쪽 건물 행을 숨기고 렌더 후 상태를 복원합니다. 스나이퍼 시점 전환 중과 1인칭·스코프에서는 양쪽을 표시합니다. 충돌과 다른 카메라 마스크는 유지합니다.
- 변경: Assets/CpcPrototype/Survival/StreetBuildingCulling.cs, Editor/DowntownStreetSetup.cs, KayKit_DepthSurvival.unity, AgentScripts/Fix003Checks.cs 및 Tools/Fix003.
- 검증: 컴파일 오류 0, 전용 32·그래플/스나이퍼 회귀 57·경계/기존 전투 회귀 13·씬 재로드 12 PASS. 실제 렌더 이벤트·가상 InputSystem·물리 검사 및 화면 검토. 실행 오류 0. [검증 기록](../Tools/Fix003/README.md). 플레이어 빌드는 미실행, 기존 URP 경고는 BUG-001로 유지. CLI 검색 파일 접근 제한으로 동일 프로젝트의 연결된 Unity MCP 도구를 사용했습니다.

### 2026-11-17 — FIX-001: 그래플 완료 후 1인칭 및 화면 픽셀 복구

- 그래플 시작 때의 중간 카메라 연출을 제거했습니다. 이동 중 기본 3인칭을 유지하고 Arrived 이벤트 후에만 1인칭·슬로모션을 시작합니다.
- 가로등 전구 6개의 콜라이더를 제거하고 OneWayPlatforms의 삭제 참조를 정리했습니다. 전구 후킹은 물리 콜라이더 없이 GrappleAnchor로 유지됩니다.
- 캐릭터 캡처와 함께 실제 화면 후처리 머티리얼의 _PixelHeight를 진입 당시 값의 2배로 보간하고, 복귀·취소·비활성화 시 원상 복구합니다. 현재 씬은 200→400→200입니다.
- 변경: GrappleSniperAction.cs, Editor/GrappleFix001Setup.cs, GrappleSniperSceneSetup.cs, KayKit_DepthSurvival.unity, AgentScripts/Fix001Checks.cs 및 재로드 검사. 기존 미저장 씬을 Temp/Fix001/Before.unity에 보존했습니다.
- 검증: 컴파일 오류 0, 전용 17개·기존 통합 57개·씬 재로드 15개 PASS, 실행 오류 0. [검증 기록](../Tools/GrappleSniper/Fix001/README.md). 빌드는 검사하지 않았으며 기존 URP 경고는 별도 대기입니다.

### 2026-11-17 — BUG-002: Scope Lens Shader Graph 전환

- ScopeLens.shader의 수학 계산을 기본 노드 49개·연결 58개의 URP Unlit Shader Graph로 옮겼습니다. Blackboard 프로퍼티 7개와 원형 마스크·노멀 왜곡·줌·틴트·비네팅을 유지합니다.
- ScopeLens.mat를 새 그래프로 교체하고 기존 RenderTexture·노멀맵·수치·틴트를 보존했습니다. 씬·AWM 프리팹은 동일 공유 머티리얼을 통해 적용되며 SetupScopeLens 도구도 새 그래프를 사용합니다.
- 원본 Assets/sniper/ScopeLens/ScopeLens.shader는 수정·삭제하지 않았으며 SHA-256 전후 동일함을 확인했습니다.
- 검증: 셰이더 컴파일 통과, 9종 원본 대비 GPU RGB 렌더 차이 0/255, 머티리얼 재로드 및 참조 검사 통과, 실제 플레이 통합 57개 검사 PASS, 실행 오류 0. [결과·편집 안내](../Tools/GrappleSniper/ScopeLensGraph/README.md).
- Windows Unity Editor에서 확인했으며 플레이어 빌드 검사는 수행하지 않았습니다. 기존 URP 경고는 BUG-001에 유지합니다.

### 2026-11-16 — FEAT-002 / FEAT-003: 가로등 그래플 및 스나이퍼 연계

- 결과: 가로등 전구에 우클릭 후 직선/원운동으로 이동하고, 도착 시 손의 라이플을 등에 넣고 스나이퍼로 전환합니다. 기본 5초 실제 시간 동안 슬로모션을 적용하며 RMB 유지로 조준, LMB로 사격한 뒤 부드럽게 기본 시점으로 복귀합니다.
- 카메라: 가장 가까운 보이는 좀비, 없으면 CPC를 선택해 그래플 중 두 캐릭터 사이에서 대상을 바라봅니다. 1인칭 이동·조준은 unscaledDeltaTime을 사용하며 타이밍·피해·반동·배치 등은 public 필드로 조절합니다.
- 픽셀: ActorPixelQuad의 최대 해상도에서 2배까지 보간하고 복귀 시 거리 기반 해상도로 되돌립니다. 전환 텍스처는 준비 후 재사용합니다.
- 주요 코드: Assets/CpcPrototype/Survival의 GrappleAnchor.cs, PlayerGrappleAction.cs, GrappleSniperAction.cs 추가. SurvivalPlayer.cs, ActorPixelQuad.cs, KayKitAimGuide.cs, KayKitCombatEffects.cs, EquipmentGrip.cs 연동. Editor/GrappleSniperSceneSetup.cs 추가.
- 에셋: AWM 패키지를 Assets/sniper/VCR_AWM, 렌즈 리소스·셰이더를 Assets/sniper/ScopeLens로 GUID를 유지해 이동. Assets/sniper/Prefabs/AWM_GrappleSniper.prefab 생성 및 KayKit_DepthSurvival 씬 저장. 기존 미저장 씬은 Temp/BeforeGrapple에 먼저 보존했습니다.
- 검증: Unity 컴파일 오류 0. 실제 플레이 통합 검사 57개, 경계/회귀 검사 13개, 씬 재로드 검사 13개 PASS. Console 실행 오류 0. [검증 기록과 재실행](../Tools/GrappleSniper/README.md), [캡처](../Screenshots/GrappleSniper/Scoped.png).
- 범위: 가상 입력을 사용한 Windows Unity Editor 검사입니다. 빌드·실제 마이크·외부 모델 검사는 수행하지 않았습니다. 기존 URP 경고(BUG-001)는 재현됨을 기록했으며 수정 범위에는 포함하지 않았습니다. FEAT-004 탄약/장전은 대기 상태입니다.

### 2026-11-16 — FEAT-001: 3개 문서 기반 작업 구조 도입

- 변경: 기존 코드와 상세 문서에서 초기 구현 현황을 정리하고, 기능 요청·버그 요청 양식 및 상태 규칙을 추가했습니다.
- 파일: Docs/01_IMPLEMENTED.md, Docs/02_FEATURE_REQUESTS.md, Docs/03_BUGS_AND_FIXES.md, agents.md.
- 검증: 문서 존재·상대 링크·요청 ID 연결·작업 규칙 확인. Unity 실행 검증은 문서 변경 범위에 해당하지 않아 수행하지 않았습니다.
- 남은 사항: BUG-001은 과거 경고의 현재 재현 여부 확인 대기입니다.
