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

## 이번 변경에서 아직 하지 않은 것

- GitHub Actions workflow는 추가하지 않았습니다. 저장소 원격 주소, Unity 라이선스/활성화 방식, 빌드 대상과 배포 대상이 확인되지 않아 실행 가능한 빌드/배포 설정을 임의로 만들 수 없습니다.
- Unity Test Runner 또는 Unity batchmode 실행 결과는 확인되지 않았습니다. 테스트 파일 추가만으로 통과로 간주하지 않습니다.
- CI/로컬 기준 Unity 버전은 `ProjectSettings/ProjectVersion.txt`에 현재 기록된 6000.6.10f1입니다. Unity 배치 Test Runner는 XML 생성 전 리포트 서버 초기화 중단으로 끝났습니다. 프로젝트에 포함된 NUnit framework로 순수 Core 테스트 13개를 로컬에서 실행해 통과했으며 Unity 통합 컴파일/씬 검증 결과와는 별도로 취급합니다.
