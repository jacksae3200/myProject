# 03. 버그 및 수정 요청

마지막 갱신: 2026-11-17

오류, 불편한 동작, 밸런스·UI·기존 기능 변경 요청을 이 파일에 적습니다. 새 기능은 [02_FEATURE_REQUESTS.md](02_FEATURE_REQUESTS.md), 현재 구현은 [01_IMPLEMENTED.md](01_IMPLEMENTED.md)에서 관리합니다.

## 사용 방법

1. 양식을 복사해 **진행할 요청**에 추가합니다. ID는 기존 최대 번호 다음의 BUG-번호를 사용합니다. 오류가 아닌 개선 요청도 이 ID를 사용합니다.
2. 실제 동작과 원하는 동작을 적고, 가능하면 재현 순서·씬·로그를 추가합니다. 코드 원인은 몰라도 됩니다.
3. “3번 파일의 BUG-002를 수정해줘”처럼 작업을 요청합니다. 빈 양식은 작업 대상으로 취급하지 않습니다.
4. 수정 후 재현 절차와 관련 동작을 검증하고, 완료 항목을 **완료한 요청**으로 옮깁니다. 결과는 1번 문서에도 남깁니다.

## 상태와 우선순위

- 상태: 대기 → 진행 중 → 검증 대기 → 완료. 보류할 경우 이유와 다음 행동을 기록합니다.
- 재현 여부는 상태와 별도로 확인 전 / 재현됨 / 재현 안 됨을 기록합니다. 재현되지 않았다는 이유만으로 수정 완료로 처리하지 않습니다.
- 우선순위: P0 진행 불가 / P1 높음 / P2 보통 / P3 낮음. 생략하면 P2로 취급합니다.
- 이번에 발견한 별도 문제는 새 항목으로 남깁니다. 현재 요청을 끝내는 데 필요한 수정은 같은 항목에 기록합니다.

## 진행할 요청

### BUG-011 — Windows 빌드 artifact가 업로드 경로에서 누락됨

- 종류: CI/CD 버그
- 상태: 검증 대기
- 우선순위: P1
- 작성일: 2026-11-25
- 적용 환경: GitHub Actions `Windows Player Build`, self-hosted Windows x64 runner
- 재현 여부: 재현됨. 사용자가 제공한 로그에서 업로드 단계가 `No files were found with the provided path: artifacts/Windows/`로 실패함.
- 현재 동작: 빌드 및 runner 임시 폴더 확인, workspace 복사 확인까지 통과한 실행에서 `actions/upload-artifact@v4`가 `artifacts/Windows/` 상대 경로에 파일이 없다고 실패했다. workspace 실제 경로와 각 파일의 절대 경로를 로그로 출력하고, 업로드 입력은 workspace 절대 경로로 지정한다. `punycode` 문구는 Node deprecation 경고다.
- 원하는 동작: 생성된 Windows 플레이어 파일과 데이터 폴더를 workflow artifact로 업로드하고 내려받을 수 있다.
- 재현 순서:
  1. `Windows Player Build` workflow를 실행한다.
  2. `Upload Windows build artifact` 단계의 경로 검색 결과를 확인한다.
- 완료 기준:
  - [x] 빌드 출력을 `.exe` 파일인지 확인하고 실패 시 Unity 로그와 출력 폴더 항목을 남긴다.
  - [x] 빌드와 파일 확인을 같은 workflow 단계에서 연속 수행한다.
  - [x] 빌드 출력과 artifact를 runner 임시 폴더 아래 저장한다.
  - [x] 검증한 빌드 출력을 workspace로 복사한다.
  - [x] workspace와 복사된 파일 경로를 로그에 기록하고, 절대 경로로 업로드한다.
  - [ ] Actions 재실행 후 artifact 업로드 및 다운로드를 확인한다.
- 관련 요청 / 의존성: FEAT-009.
- 원인 / 변경 파일: runner 임시 폴더 절대 glob과 workspace 상대 디렉터리 입력 모두 action에서 파일 매치 0건으로 처리되었다. 검증된 파일을 workspace로 복사한 뒤 `${{ github.workspace }}\artifacts\Windows`를 업로드 대상으로 쓰고 경로를 로그로 대조한다. 생성 결과가 Git에 추가되지 않도록 `/artifacts/`를 `.gitignore`에 등록했다. `.github/workflows/windows-player-build.yml`, `.gitignore`.
- 검증 방법 / 결과: 사용자가 제공한 로그에서 빌드/복사 단계를 통과하고 upload 단계에 도달한 것을 확인했다. 새 절대 경로 입력은 아직 Actions에서 실행하지 않았다.
- 남은 사항: 수정사항을 푸시한 뒤 Actions 재실행 로그를 확인하고 artifact를 다운로드한다.
- 완료일:
- 구현 기록: [1번 문서 BUG-011 변경 이력](01_IMPLEMENTED.md#2026-11-25--bug-011--windows-빌드-artifact-업로드-경로-수정), FEAT-009.


### BUG-001 — 과거 URP GPU Resident Drawer / Forward+ 경고 재확인

- 종류: 버그 조사
- 상태: 대기
- 우선순위: P3
- 작성일: 2026-11-16
- 출처: [초기 CPC README](../Assets/CpcPrototype/README.md), [과거 음성 검증 기록](../Tools/Voice/VALIDATION.md).
- 적용 씬 / 환경: KayKit_DepthSurvival, Unity 6000.6.3f1 / URP. 과거 프로토타입에서도 기록됨.
- 재현 여부: 재현됨. 2026-11-16 FEAT-002/003 플레이 검증 중 KayKit_DepthSurvival에서 반복 발생 확인.
- 현재 동작: GPUResidentDrawer Disabled due to some configured Universal Renderers not using the Forward+ or Deferred+ rendering paths. 경고가 반복됨.
- 원하는 동작: 현재 재현 여부와 정확한 경고를 확인하고, 재현되면 원인을 해결한다.
- 재현 순서:
  1. 관련 씬을 열고 Console을 정리한 뒤 Play한다.
  2. 경고 원문·발생 씬·사용 중인 URP 에셋과 Renderer 설정을 기록한다.
- 완료 기준:
  - [x] 현재 재현 여부와 근거를 기록한다.
  - [ ] 재현되면 원인 수정 후 경고 재발 여부 및 기존 조명·그림자를 확인한다.
  - [ ] 재현되지 않으면 수정 완료를 주장하지 않고 확인한 범위와 후속 필요 여부를 기록한다.
- 원인 / 변경 파일: 미조사.
- 검증 방법 / 결과: 2026-11-16 실제 플레이 Console에서 경고 원문 확인. FEAT-002/003 검사 중 실행 오류는 0이었음. 원인 수정 및 조명·그림자 회귀는 미실행.
- 남은 사항: 렌더 설정 원인 조사 및 수정은 별도 요청 대기. FEAT-002/003 범위에서는 변경하지 않음.
- 완료일:
- 구현 기록:

## 요청 양식 (복사용, 실제 요청 아님)

~~~md
### BUG-NNN — 버그 또는 수정 제목

- 종류: 버그 / 기존 기능 개선 / 밸런스 / UI
- 상태: 대기
- 우선순위: P2
- 작성일: YYYY-MM-DD
- 적용 씬 / 환경:
- 재현 여부: 확인 전
- 현재 동작:
- 원하는 동작:
- 재현 순서:
  1. 시작 조건과 조작을 적기
  2. 문제가 발생하는 지점을 적기
- 발생 빈도 / 로그 / 캡처 경로:
- 완료 기준:
  - [ ] 재현 절차에서 원하는 동작 확인
  - [ ] 영향받는 기존 동작 회귀 확인
- 관련 요청 / 의존성: 없음
- 원인 / 변경 파일: (에이전트 작성)
- 검증 방법 / 결과: (에이전트 작성, 미실행 항목 구분)
- 남은 사항 / 보류 이유: 없음
- 완료일:
- 구현 기록: (완료 후 1번 문서 연결)
~~~

## 완료한 요청

### BUG-010 — 플레이어에서 전구로 향하는 그래플과 거리 설정

- 상태: 완료
- 작성일: 2026-11-17
- 적용 씬 / 환경: KayKit_DepthSurvival / Player
- 원하는 동작: 위에서 보면 플레이어와 전구를 잇는 선을 따라 이동하고, 측면에서는 아래로 돌아 전구 반대편의 초기 거리 절반 지점에 도착한다. 최대 후킹 거리를 Inspector에서 설정한다.
- 구현 및 완료 기준: 카메라에 따른 원호 기울임을 제거해 플레이어와 전구를 잇는 수직 평면 안에서 이동한다. 지면과 겹치는 구간은 줄을 더 일찍 당기거나 최저 높이를 조절한다. `Max Hook Distance`가 조준 표시와 발사에 함께 적용되며 생존 씬 값은 8이다.
- 변경 파일: PlayerGrappleAction.cs, GrappleSniperChecks.cs, GrappleDirectionChecks.cs, CombatFeatures.md, Tools/GrappleSniper/README.md 및 검증 기록.
- 검증: Unity 컴파일·실행 오류 0. [방향 20개](../Tools/GrappleSniper/Bug010/DirectionChecks.txt), [통합 68개](../Tools/GrappleSniper/Bug010/Checks.txt), [경계·회귀 13개](../Tools/GrappleSniper/Bug010/BoundaryChecks.txt) 통과. 플레이어 빌드는 미실행.
- 완료일: 2026-11-17
- 구현 기록: [1번 문서 변경 이력](01_IMPLEMENTED.md#변경-이력), SYS-014.

### BUG-009 — 카메라 쪽 그래플 휘어짐 및 도착 거리

- 상태: 완료
- 작성일: 2026-11-17
- 적용 씬 / 환경: KayKit_DepthSurvival / Player
- 원하는 동작: 위에서 보았을 때 전구를 향하는 파란 경로를 우선하고, 지면 때문에 기울여야 할 경우 카메라 쪽 노란 원호를 피한다. 기존 스윙 중 거리가 점차 줄어 초기 거리의 절반에서 끝난다.
- 구현 및 완료 기준: 지면 여유가 있으면 플레이어와 전구를 잇는 수직 평면에서 스윙한다. 기울여야 하는 경우 카메라에서 멀어지는 쪽으로 회전하며 전구 반대편의 `n/2` 지점에 도착한다. 장애물 취소 및 스나이퍼 전환 확인.
- 변경 파일: PlayerGrappleAction.cs, AgentScripts/GrappleSniperChecks.cs, AgentScripts/GrappleDirectionChecks.cs, Tools/GrappleSniper/README.md 및 검증 기록.
- 검증: Unity 컴파일·실행 오류 0. [방향 20개](../Tools/GrappleSniper/Bug009/DirectionChecks.txt), [통합 66개](../Tools/GrappleSniper/Bug009/Checks.txt), [경계·회귀 13개](../Tools/GrappleSniper/Bug009/BoundaryChecks.txt) 통과. 자동 틱 설정은 시간 초과됐으며 배경 실행으로 실제 플레이를 검증했다.
- 남은 사항: 플레이어 빌드는 미실행.
- 완료일: 2026-11-17
- 구현 기록: [1번 문서 변경 이력](01_IMPLEMENTED.md#변경-이력), SYS-014.

### BUG-008 — 그래플 스윙 방향 제한

- 종류: 기존 기능 개선
- 상태: 완료
- 작성일: 2026-11-17
- 적용 씬 / 환경: KayKit_DepthSurvival / Player
- 현재 동작: 반원을 기울일 때 그림의 빨간쪽으로 도는 원호가 선택될 수 있다.
- 원하는 동작: 그림의 파란쪽으로 회전하며 같은 종착점까지 이동한다. 허용된 방향이 막히면 그래플을 취소한다.
- 완료 기준: 화면 좌우 출발에서 파란쪽을 선택하고, 허용된 원호에 장애물이 있어도 빨간쪽으로 우회하지 않는다.
- 관련 요청 / 의존성: BUG-003, BUG-005.
- 변경 파일: PlayerGrappleAction.cs, AgentScripts/GrappleDirectionChecks.cs, Tools/GrappleSniper/README.md 및 검증 기록.
- 검증: Unity 컴파일·실행 오류 0. [방향 검사 9개](../Tools/GrappleSniper/Bug008/DirectionChecks.txt), [통합 65개](../Tools/GrappleSniper/Bug008/Checks.txt), [경계·회귀 13개](../Tools/GrappleSniper/Bug008/BoundaryChecks.txt) 통과.
- 남은 사항: 플레이어 빌드는 미실행.
- 완료일: 2026-11-17
- 구현 기록: [1번 문서 변경 이력](01_IMPLEMENTED.md#변경-이력), SYS-014.

### BUG-007 — ProtectPlayer 액션 누락으로 인한 컴파일 오류

- 종류: 버그
- 상태: 완료
- 우선순위: P0
- 작성일: 2026-11-17
- 적용 환경: Unity 6000.6.3f1 / CpcTactics, CpcTacticalMotor.
- 재현 여부: 재현됨. Unity Console에서 CS0117 3개 확인.
- 현재 동작: CpcTacticalMotor.cs의 40·50·115행에서 CpcAction.ProtectPlayer 정의를 찾지 못해 컴파일 실패. Editor/CpcTacticsSceneSetup.cs도 같은 항목을 참조.
- 원하는 동작: 누락된 액션을 복구해 기존 군집 보호와 컴파일을 유지한다.
- 완료 기준: Unity 재컴파일 성공, 기존 플래그 값 보존 및 ProtectPlayer의 독립 비트 확인.
- 원인 / 변경 파일: CpcTactics.cs의 CpcAction enum에 ProtectPlayer 누락.
- 검증 결과: ProtectPlayer = 512 복원 후 Unity 재컴파일 completed / failed=false / compilationFailed=false. 로드된 enum에서 기존 값 0~256 유지, 새 항목 512 및 값 중복 없음 확인. Console 오류 0.
- 남은 사항: 컴파일 오류 해결 범위 내 없음. 플레이·외부 모델·빌드 검사는 미실행.
- 완료일: 2026-11-17
- 구현 기록: [1번 문서 변경 이력](01_IMPLEMENTED.md#변경-이력).

### BUG-006 — 스코프 안쪽 원 바깥에 검은 그라데이션

- 종류: 기존 기능 개선
- 상태: 완료
- 작성일: 2026-11-17
- 원하는 동작: 조준선의 안쪽 원 바깥에서 렌즈 가장자리까지 부드럽게 검어지는 효과를 추가한다.
- 완료 기준: 안쪽 원의 밝기를 유지하고 바깥쪽으로 갈수록 검어지며, 기존 원형 마스크와 조준선 알파가 정상적으로 표시된다.
- 관련 요청: FEAT-005, BUG-002.
- 변경 파일: [ScopeLensGraph.shadergraph](../Assets/sniper/ScopeLens/ScopeLensGraph.shadergraph), [렌즈 편집 안내](../Tools/GrappleSniper/ScopeLensGraph/README.md).
- 수정 내용: 합성된 렌즈 RGB에 `1 - Smoothstep(0.45, 0.98, r)`을 곱했다. 안쪽 원 경계를 기준으로 어두워지고 렌즈 테두리 직전에는 검게 표시된다.
- 검증: Shader Graph 컴파일, GPU 렌더의 안쪽 밝기 유지·바깥 밝기 감소·중앙 조준선·원형 마스크 확인. `KayKit_DepthSurvival`에서 우클릭 조준·해제, 카메라 복귀 및 화면 확인. 콘솔 오류 0.
- 남은 사항: 플레이어 빌드 미실행.
- 완료일: 2026-11-17
- 구현 기록: [1번 문서 변경 이력](01_IMPLEMENTED.md#2026-11-17--bug-006--스코프-바깥쪽-검은-그라데이션), SYS-011.

### BUG-005 — 그래플 FixedUpdate 역할별 함수 분리

- 종류: 기존 코드 개선 / 리팩터링
- 상태: 완료
- 우선순위: P2
- 작성일: 2026-11-17
- 적용 씬 / 환경: KayKit_DepthSurvival / PlayerGrappleAction.cs
- 재현 여부: 오류 수정이 아닌 구조 개선 요청.
- 현재 동작: FixedUpdate에 이동 유효성·줄 도착·원호 계산·이동/충돌·도착 처리가 모여 있음.
- 원하는 동작: 기능별 private 메서드로 분리하고 FixedUpdate는 실행 순서를 표현한다. 기존 궤적·시간·취소·이벤트·디버그 로그를 유지한다.
- 완료 기준:
  - [x] 역할별 메서드 분리와 기존 네임스페이스·공개 인터페이스 유지.
  - [x] Unity 컴파일 및 그래플/스나이퍼·취소/충돌 회귀 확인.
- 관련 요청 / 의존성: BUG-003, BUG-004.
- 변경 파일 / 검증 결과: PlayerGrappleAction.cs의 FixedUpdate를 CanContinueTravel, TryEnterSwing, CalculateSwingTarget, TryMoveToSwingTarget, CompleteSwing으로 분리. 기존 타이머 초기화·원호 계산·충돌 비교·도착 이벤트·Debug.Log 3개 유지. Unity 컴파일 오류 0, 통합 65 PASS, 경계/회귀 13 PASS, 실제 플레이 Console 오류 0. [검증 기록](../Tools/GrappleSniper/Bug005/README.md).
- 남은 사항: 요청 범위 내 없음. 플레이어 빌드는 미실행.
- 완료일: 2026-11-17
- 구현 기록: [1번 문서 변경 이력](01_IMPLEMENTED.md#변경-이력).

### BUG-004 — 그래플 스나이퍼 비조준 1인칭 구도

- 종류: 기존 기능 개선
- 상태: 완료
- 작성일: 2026-11-17
- 적용 씬: KayKit_DepthSurvival
- 재현 여부: 재현됨. 그래플 도착 후 우클릭을 놓은 상태에서 스코프가 카메라 가까이 배치되어 오른쪽 화면을 크게 가렸습니다.
- 원하는 동작: 첨부 이미지처럼 비조준 시 총을 화면 오른쪽 아래에 두고, 우클릭 시 총과 스코프가 화면 중앙으로 이동해 기존 배율로 조준합니다.
- 완료 기준: 그래플 도착 후 비조준 구도, 우클릭 조준 및 해제, 기존 사격·복귀 동작을 플레이 모드에서 확인합니다.
- 관련 요청: FEAT-003
- 변경 파일: [GrappleSniperAction.cs](../Assets/CpcPrototype/Survival/GrappleSniperAction.cs), [그래플 스나이퍼 가이드](../Tools/GrappleSniper/README.md).
- 구현: 머리 기준 카메라 위치에서 무기 위치와 각도를 보간합니다. 비조준 시 오른쪽 아래 구도를 적용하고 완전 조준 시 기존 접안 거리와 32도 시야각을 사용합니다. 비조준 반동의 후퇴량은 스코프가 카메라 앞에 머물도록 제한합니다.
- 검증: Unity 컴파일 오류 0. 가상 마우스를 사용한 통합·회귀 검사 71개 통과, 실행 오류 0. 비조준·조준 캡처, 스코프 중앙 및 접안 거리, 우클릭 해제, 사격 피해·반동, 시간·장비 복귀를 확인했습니다.
- 남은 사항: 플레이어 빌드는 미실행.
- 완료일: 2026-11-17
- 구현 기록: [01. 구현 현황 및 변경 이력](01_IMPLEMENTED.md#2026-11-17--bug-004--그래플-스나이퍼-비조준-1인칭-구도)

### BUG-003 — 가로등 전구 중심 반원 그래플

- 종류: 기존 기능 개선
- 상태: 완료
- 우선순위: P2
- 작성일: 2026-11-17
- 적용 씬 / 환경: KayKit_DepthSurvival / Player
- 재현 여부: 기존 코드에서 확인. 낮은 각도에서 직선 이동하며 원운동은 줄이 줄어들고 전구 위쪽에서 끝난다.
- 원하는 동작: 첨부 스케치처럼 전구를 중심으로 줄 길이를 유지하며 아래쪽을 지나 시작점의 정반대편까지 이동한다.
- 재현 순서: 가로등 전구에 포인터를 올려 우클릭하고 이동 궤적과 도착 위치를 확인한다.
- 완료 기준:
  - [x] 전구 아래쪽으로 스윙하며 일정한 반경으로 180도 이동한다. 지면 여유가 부족하면 원호 평면을 기울인다.
  - [x] 시작점에서 전구를 관통하는 직선의 반대쪽에서 도착하고 스나이퍼 시퀀스를 시작한다.
  - [x] 얕은 각도에서도 스윙하며 경로의 장애물에 막히면 안전하게 취소한다.
- 관련 요청 / 의존성: FEAT-002, FEAT-003, FIX-001. FIX-002의 궤적 조건은 이번 첨부 스케치 기준으로 작업했다.
- 변경 파일: PlayerGrappleAction.cs, AgentScripts/GrappleSniperChecks.cs, CombatFeatures.md, Tools/GrappleSniper/README.md 및 검증 기록.
- 검증 결과: Unity 컴파일 및 실행 오류 0. 가상 우클릭·CharacterController·장애물을 사용한 [통합 65개](../Tools/GrappleSniper/Bug003/Checks.txt) 및 [경계·회귀 13개](../Tools/GrappleSniper/Bug003/BoundaryChecks.txt) 통과.
- 남은 사항: 플레이어 빌드는 미실행.
- 완료일: 2026-11-17
- 구현 기록: [1번 문서 변경 이력](01_IMPLEMENTED.md#변경-이력), SYS-014.

### BUG-002 — 스코프 렌즈를 Shader Graph로 전환

- 종류: 기존 기능 개선
- 상태: 완료
- 우선순위: P2
- 작성일: 2026-11-17
- 원하는 동작: ScopeLens.shader를 기반으로 편집 가능한 Shader Graph를 만들고 기존 렌즈 머티리얼을 교체한다. 원본 shader 파일은 삭제하거나 변경하지 않는다.
- 완료 기준:
  - [x] 원형 마스크·노멀 왜곡·확대·가장자리 어둡게·틴트 계산을 노드로 구성한다.
  - [x] 기존 텍스처와 프로퍼티 값을 유지한 머티리얼 교체를 저장한다.
  - [x] 셰이더 컴파일과 원본 대비 렌더 결과를 검증하고 원본 보존을 확인한다.
- 변경 파일: Assets/sniper/ScopeLens/ScopeLensGraph.shadergraph, ScopeLens.mat 및 AgentScripts/BuildScopeLensGraph.cs, VerifyScopeLensGraph.cs, ApplyScopeLensGraph.cs, SetupScopeLens.cs.
- 검증 결과: 그래프 셰이더 컴파일 통과. 원본 대비 9가지 GPU 렌더의 RGB 차이 0/255. 기존 텍스처·수치·프리팹·씬 공유 참조 보존 및 실제 플레이 통합 57개 검사 PASS, 실행 오류 0. 원본 셰이더 SHA-256 전후 동일.
- 구현 기록: [1번 문서](01_IMPLEMENTED.md#변경-이력), [설정과 검증](../Tools/GrappleSniper/ScopeLensGraph/README.md).
- 완료일: 2026-11-17

### FIX-001 — 수정

- 종류: 기존 기능 개선 / 밸런스 / UI
- 상태: 대기
- 우선순위: P2
- 작성일: YYYY-MM-DD
- 적용 씬 / 환경: KayKit_DepthSurvival / player
- 재현 여부: 확인 전
- 현재 동작: 그래플링액션을 하는 동시에 카메라가 1인칭으로 이동
- 원하는 동작: 그래플링 액션 이후 카메라가 1인칭으로 이동
- 관련 요청 / 의존성: 가로등의 전구에 콜라이더제거로 플레이어가 가로등 위에 서 있을 수 없게 한다. 추가적으로 1인칭일때는 화면 픽셀라이즈 pixel수치를 2배로 늘린다. 3인칭이 되면 다시 원상복구 한다.
- 원인 / 변경 파일: (에이전트 작성)
- 검증 방법 / 결과: (에이전트 작성, 미실행 항목 구분)
- 남은 사항 / 보류 이유: 없음
- 완료일:
- 구현 기록: (완료 후 1번 문서 연결)

### FIX-002 — 수정

- 종류: 기존 기능 개선 / 밸런스 / UI
- 상태: 대기
- 우선순위: P2
- 작성일: YYYY-MM-DD
- 적용 씬 / 환경: KayKit_DepthSurvival / player
- 재현 여부: 확인 전
- 현재 동작: 그래플링 액션중 원운동이 생각의 반대 방향으로 작동함, 직선 이동의 각도가 생각보다 큼 그리고 이동하는 거리가 생각보다 짧음  
- 원하는 동작: 바라보는 방향으로 이동하고 공중에 뜨게끔한다, 직선이동이 가능한 각도를 20도로 줄이고, 가로등의 전구를 지나 더 이동하게 설정.
- 관련 요청 / 의존성: 가로등의 전구에 콜라이더제거로 플레이어가 가로등 위에 서 있을 수 없게 한다. 추가적으로 1인칭일때는 화면 픽셀라이즈 pixel수치를 2배로 늘린다. 3인칭이 되면 다시 원상복구 한다.
- 원인 / 변경 파일: (에이전트 작성)
- 검증 방법 / 결과: (에이전트 작성, 미실행 항목 구분)
- 남은 사항 / 보류 이유: 없음
- 완료일:
- 구현 기록: (완료 후 1번 문서 연결)


### FIX-003 — Downtown City 직선 거리 및 시점별 건물 컬링

- 종류: 기존 기능 개선 / UI
- 상태: 완료
- 우선순위: P2
- 작성일: 2026-11-17
- 적용 씬 / 환경: KayKit_DepthSurvival
- 재현 여부: 기존 KayKit 환경과 한쪽 건물 배치 확인.
- 현재 동작: KayKit 에셋으로 환경 구축 중
- 원하는 동작: kayKit_DepthSurvival 씬에서 건물들이나 도로 같은 환경 오브젝트들을 바꾸고 싶어서 assets/DowntownCityMegaKit[standard] 폴더에 있는 오브젝트들을 이용해서 지금 씬 같이 1자 맵으로 만들어 주면 되는데 건물을 양쪽에 배치 해주고 3인칭일 때 카메라가 있는 부분은 컬링한다. 1인칭으로 변경시 카메라 컬링모드를 푼다.
- 완료 기준:
  - [x] Downtown City 도로와 양쪽 건물로 기존 X축 직선 맵을 교체한다.
  - [x] 3인칭에서 카메라 쪽 건물을 숨기고 1인칭·스코프에서는 양쪽을 표시한다.
  - [x] 이동·전투·그래플 참조, 충돌, 저장/재로드 및 시점 복귀를 확인한다.
- 관련 요청 / 의존성: FEAT-002/003, FIX-001. FIX-002와 FEAT-004는 작업 범위 밖.
- 원인 / 변경 파일: Assets/CpcPrototype/Survival/StreetBuildingCulling.cs, Editor/DowntownStreetSetup.cs, Assets/Scenes/KayKit_DepthSurvival.unity, AgentScripts/Fix003Checks.cs. Downtown City의 Unity FBX 도로 4개와 건물 12채(양쪽 6채)를 배치. 기존 KayKit 환경은 비활성화하고 바닥/경계 충돌·그래플 전구 6개·게임플레이를 보존. 카메라 렌더 구간에만 가까운 건물 행을 숨기며, 1인칭 진입/복귀 중·스코프에서는 양쪽을 표시.
- 검증 방법 / 결과: Unity 컴파일 오류 0, 전용 플레이 32 PASS, 기존 그래플/스나이퍼 57 PASS, 경계·라이플/이동 회귀 13 PASS, 씬 재로드 12 PASS. 실제 렌더 콜백과 InputSystem 가상 입력·물리 검사, 실행 오류 0. [검증 기록](../Tools/Fix003/README.md).
- 남은 사항 / 보류 이유: 요청 범위 내 없음. 플레이어 빌드는 미실행. 기존 URP 경고 BUG-001과 FIX-002·FEAT-004는 별도 대기.
- 완료일: 2026-11-17
- 구현 기록: [1번 문서 변경 이력](01_IMPLEMENTED.md#변경-이력), SYS-016.
