# Unity GitHub Actions CI/CD 진행 가이드

## 현재 분리 경계

`Assets/CpcPrototype/Core`는 `CpcPrototype.Core` 어셈블리로, `noEngineReferences`를 켜 UnityEngine 참조를 차단합니다. 현재 행동 트리의 `Node`, `Selector`, `Sequence`, leaf 노드가 첫 순수 C# Core입니다. Unity 게임플레이 코드는 기본 `Assembly-CSharp`에서 이 어셈블리를 사용하고, `Assets/CpcPrototype/Tests/EditMode`의 NUnit 테스트는 Core 어셈블리만 참조합니다.

이것은 프론트엔드와 백엔드의 프로세스/서버 분리가 아니라, Unity 앱 안에서 게임 규칙(Core)과 엔진 어댑터/표현(UI·씬·물리)을 나누는 첫 단계입니다. 별도 서버가 필요해지면 네트워크 계약과 권한 경계를 별도 설계해야 합니다.

## 권장 분리 순서

1. **순수 도메인 규칙**: 데미지 계산, 탄약/장전 규칙, 쿨다운, 전술 행동 선택처럼 입력과 결과를 값으로 표현할 수 있는 코드를 `Core`로 옮깁니다. 이식할 때마다 NUnit 케이스를 함께 추가합니다.
2. **Unity 어댑터**: `MonoBehaviour`, `Time`, `Physics`, `NavMesh`, `Input`, `Transform`, `GameObject`, 씬/에셋 참조를 가진 코드는 Unity 쪽에 남겨 Core API를 호출합니다. 예를 들어 `CombatStatus`는 현재 `Time.time`, `GameObject`, `Health`에 결합되어 있으므로 먼저 시간/상태 규칙을 순수 타입으로 추출해야 합니다.
3. **프런트엔드 표현**: HUD, 채팅 패널, 카메라, 애니메이션, 오디오, 이펙트는 Core 상태를 읽거나 명령/이벤트를 전달하는 뷰·어댑터로 둡니다.
4. **경계 강화**: Core 어셈블리에 엔진 참조가 유입되지 않는지 확인하고, 게임플레이 어셈블리가 Core를 참조하는 방향만 유지합니다. 네트워크 서버는 오프라인 규칙 코드와 동일시하지 않습니다.

## GitHub Actions 도입 순서

1. `main` 보호 규칙과 PR 체크를 정하고 Unity 라이선스 방식(개인/조직 라이선스 및 좌석)을 확인합니다.
2. Git LFS와 Unity 프로젝트 캐시를 구성하고, 테스트를 수행할 Unity 버전을 프로젝트에 맞춥니다. CI의 Unity 버전은 `ProjectVersion.txt`와 일치시킵니다.
3. 첫 워크플로에서 체크아웃 → LFS 파일 가져오기 → Unity Edit Mode NUnit 실행 → 결과 XML 업로드를 구성합니다. 테스트 실패는 PR을 막는 필수 체크로 둡니다.
4. 다음 단계로 Linux/Windows 대상 빌드를 별도 job 또는 workflow로 추가합니다. 프로젝트가 URP·플랫폼 에셋에 의존하므로 CI에서 실제 대상 빌드를 확인합니다.
5. 배포는 빌드 산출물을 보관한 뒤 별도 수동 승인 환경으로 진행합니다. 시크릿/서명 키는 GitHub Environments에 두고 로그나 저장소에 넣지 않습니다.
6. PR에서 테스트, 기본 브랜치에서 테스트+빌드, 릴리스 태그/승인에서 배포하는 흐름으로 확장합니다. 초기에는 CI 검증까지만 자동화하고, 배포 대상과 라이선스가 확인된 뒤 배포 단계를 켭니다.

## 엔진 독립 NUnit CI

`.github/workflows/core-nunit-tests.yml`은 `main` 푸시, `main` 대상 PR, 수동 실행에서 순수 Core 테스트를 `.NET 10`의 `dotnet test`로 실행합니다. 기존 `BehaviorTreeTests`와 `AmmoInventoryTests` 소스 파일을 직접 연결하므로 테스트 로직을 복사하지 않습니다. 이 job은 Unity Editor, GameCI Docker 이미지, Unity 라이선스 Secret 없이 실행됩니다. NUnit 결과 TRX를 `core-nunit-test-results` 아티팩트로 올립니다. GitHub Actions Core NUnit 실행은 통과했다고 사용자가 확인했습니다.

이 검사는 엔진 독립 Core 규칙만 컴파일하고 테스트합니다. `MonoBehaviour`, 씬, 물리, UI 연결이나 전체 Unity 프로젝트 컴파일을 검증하지는 않습니다.

## Unity Edit Mode workflow

`.github/workflows/unity-tests.yml`은 GameCI의 Unity 6000.6.10f1 Edit Mode 테스트용으로 수동 실행만 남겨두었습니다. 현재 GameCI가 요청한 `unityci/editor:ubuntu-6000.6.10f1-linux-il2cpp-3` 태그를 찾지 못해 테스트를 시작하지 못했습니다. 이미지가 게시된 뒤 **Actions → Unity Edit Mode Tests (GameCI) → Run workflow**로 실행합니다. Git LFS 체크아웃, Unity `Library` 캐시, 테스트 결과 아티팩트 업로드를 구성했습니다.

저장소의 **Settings → Secrets and variables → Actions → New repository secret**에 다음 세 값이 등록되어 있어야 합니다.

- `UNITY_LICENSE`: 활성화된 Unity 라이선스 파일 전체 내용
- `UNITY_EMAIL`: Unity ID의 이메일
- `UNITY_PASSWORD`: Google 비밀번호가 아닌 Unity ID용 비밀번호

Unity Secrets `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`는 GameCI workflow에서만 필요합니다. 이미지 오류는 NUnit 테스트 실패가 아니라 컨테이너 준비 단계 실패입니다. Unity Test Runner가 통과해도 빌드·씬 플레이 및 실제 게임 통합 검증까지 통과했다는 뜻은 아닙니다.

## Windows 빌드 시작 씬

`ProjectSettings/EditorBuildSettings.asset`은 `KayKit_DepthSurvival.unity`를 유일한 활성 빌드 씬으로 사용합니다. `SampleScene`은 빌드에 들어가지 않습니다. 이 설정만으로 Player 빌드가 완료된 것은 아니며 Windows x64 빌드 workflow는 아직 필요합니다.

## Windows x64 빌드 workflow

`.github/workflows/windows-player-build.yml`은 `main` 푸시와 수동 실행에서 `[self-hosted, windows, x64]` runner를 사용합니다. `Assets/CpcPrototype/Editor/WindowsPlayerBuild.cs`가 Build Settings에 활성화된 씬을 Windows x64 플레이어로 빌드하고, 결과는 `windows-player-<커밋 SHA>` 이름의 Actions artifact로 14일간 보관됩니다. 출력은 runner 임시 폴더에 만들고 Unity 빌드 및 exe/파일 확인을 같은 workflow 단계에서 진행한 뒤 workspace의 `artifacts/Windows`로 복사합니다. 업로드 액션은 `artifacts/Windows/**` 상대 glob을 사용합니다. 실패 시 Unity Editor 로그와 출력 폴더를 표시합니다. 생성 파일은 `.gitignore`에서 제외합니다. 이 단계는 빌드 파일을 만들 뿐 itch.io로 배포하거나 `main`에 자동 병합하지 않습니다.

Runner 준비 조건:

- `ProjectSettings/ProjectVersion.txt`에 적힌 정확한 Unity Editor 버전(현재 6000.6.10f1)을 설치합니다.
- 해당 Editor에 Windows Build Support 모듈과 현재 Player Settings에서 선택한 빌드 백엔드 모듈을 설치합니다.
- Actions runner를 실행하는 동일 Windows 계정에서 Unity 라이선스를 활성화합니다. 이 로컬 Editor 실행 방식은 GameCI용 `UNITY_LICENSE`/이메일/비밀번호 Secret과 별개입니다.
- Unity가 기본 경로 `C:\Program Files\Unity\Hub\Editor\<버전>\Editor\Unity.exe`가 아닌 곳에 있으면 runner 계정에 `UNITY_EDITOR_PATH` 환경 변수를 설정하고 runner를 다시 시작합니다.

실행 순서:

1. workflow와 빌드 스크립트를 커밋하고 `main`에 푸시합니다. runner 콘솔에 `Listening for Jobs`가 표시된 상태여야 합니다.
2. GitHub 저장소의 **Actions → Windows Player Build**에서 실행 내역을 확인합니다. 수동 실행은 **Run workflow**를 누릅니다.
3. 성공한 실행을 열고 **Artifacts**에서 `windows-player-<커밋 SHA>`를 내려받습니다. 압축 파일을 풀면 `.exe`와 `_Data` 폴더 등 플레이어 파일이 있습니다.
4. Windows에서 압축을 푼 폴더의 실행 파일을 실행해 시작 씬과 기본 플레이를 확인합니다.

이 workflow는 self-hosted runner에서 신뢰할 수 있는 저장소 브랜치만 실행하도록 push-to-main과 수동 실행만 받습니다. GitHub에 아직 커밋/푸시되지 않은 변경은 Actions에서 실행할 수 없습니다. runner 임시 폴더의 절대 경로 glob이 매치되지 않아 BUG-011에서 빌드 결과를 workspace로 복사하고 상대 경로로 업로드하도록 바꿨습니다. 재실행 결과가 확인될 때까지 FEAT-009는 검증 대기입니다.
## 이번 변경에서 아직 하지 않은 것

- Butler 기반 itch.io 배포 workflow는 아직 추가하지 않았습니다. Windows 빌드 artifact를 먼저 확인한 뒤 별도 단계로 구성합니다.
- Unity Test Runner 또는 Unity batchmode 실행 결과는 확인되지 않았습니다. GameCI가 6000.6.10f1 Linux IL2CPP 이미지를 찾지 못해 컨테이너 시작 전에 멈췄습니다. `.NET` CI는 GitHub Actions에서 실행한 뒤 별도 결과를 확인해야 합니다.
- CI/로컬 기준 Unity 버전은 `ProjectSettings/ProjectVersion.txt`에 현재 기록된 6000.6.10f1입니다. Unity 배치 Test Runner는 XML 생성 전 리포트 서버 초기화 중단으로 끝났습니다. 프로젝트에 포함된 NUnit framework로 순수 Core 테스트 13개를 로컬에서 실행해 통과했으며 Unity 통합 컴파일/씬 검증 결과와는 별도로 취급합니다.
