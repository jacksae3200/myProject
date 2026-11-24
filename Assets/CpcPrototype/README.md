# PICOCHAN / CPC 로컬 LLM 테스트

## 실행

1. `Assets/CpcPrototype/Scenes/PicoCpcTest.unity`를 연 뒤 Play를 누릅니다.
2. WASD 이동, Space 점프, Shift 달리기, 마우스 좌클릭 근접공격입니다.
3. 마우스 우클릭 드래그로 카메라를 회전합니다. 이동은 카메라 방향 기준입니다.
4. Enter로 대화 입력을 열면 입력창에 자동 포커스됩니다. Enter 또는 전송 버튼으로 보내고 Esc로 닫습니다. 입력 중 이동/공격은 차단합니다.
5. 사망 시 이동·공격이 멈춥니다. Stop → Play로 초기화합니다.

메뉴 `Tools/CPC/Open PICOCHAN Test Scene`으로도 열 수 있습니다.
기존 Viking 씬의 미저장 상태는 `Scenes/Preserved_Viking_TextureOnly.unity`로 복사하여 보존했습니다.
원래 Viking 씬 파일을 덮어쓰지 않았습니다.

## 기본 전투와 행동 트리

- 플레이어/동료/큐브 몬스터 체력: 각각 100.
- 플레이어 공격: 전방 약 150도, 반경 1.8m, 피해 25, 쿨다운 0.45초.
- 몬스터 공격: 거리 1.5m, 피해 10, 쿨다운 1.3초.
- 몬스터는 가까운 살아 있는 플레이어 또는 동료를 대상으로 삼습니다.
- 탐지 8m, 추적 해제 12m, 시작 지점 기준 최대 활동 반경 15m.
- 복귀 중에는 새 대상을 잡지 않으며 시작 지점에 돌아오면 다시 탐지합니다.
- 죽은 몬스터는 공격/이동을 중단하고 테스트를 위해 큐브 시체를 유지합니다.

```text
Selector
├─ Sequence: 사망? → Dead
├─ Sequence: 유효한 대상?
│  └─ Selector
│     ├─ Sequence: 공격 거리? → Attack
│     └─ Chase
└─ ReturnHome / Idle
```

`Runtime/BehaviorTree.cs`는 실제 Condition/Action/Sequence/Selector 노드 구현입니다.
거리와 피해는 몬스터의 `CubeMonsterBrain`, 플레이어의 `ThirdPersonPlayer` 인스펙터에서 조절합니다.
평평한 테스트 공간에서 CharacterController로 직선 이동합니다. 장애물 우회/NavMesh 경로 탐색은 이번 버전에 포함하지 않습니다.

## CPC

- 플레이어 뒤를 약 1.8m 거리로 따라갑니다.
- 플레이어의 실제 점프 이벤트를 받아 동료도 지상에 있으면 점프합니다.
- 몬스터 발견(12m), 접근(4m), 플레이어/동료 피격을 `[CPC/Event]`로 즉시 출력합니다.
- 발견은 몬스터별 최초 1회, 접근은 거리 이탈 후 재진입 시, 모델 피격 요청은 5초 간격으로 제한합니다.
- 현재 탐지는 거리 기반입니다. 시야/벽 가림 검사는 포함하지 않습니다.
- 근접 몬스터는 동료도 공격할 수 있으며 동료 체력이 0이면 따라가기와 대화를 멈춥니다.
- 따라가기/점프/공격 규칙은 Unity가 실행하고 LLM은 사건에 대한 대사만 생성합니다.

## Gemma / 프롬프트 변경

### 표현 제한 및 주제 유지

- `Settings/GemmaLocalSettings.asset` → `System Prompt`: `[대화 표현 및 주제 규칙]` 아래에 욕설/모욕/혐오 표현 금지,
  게임·모험·동료 관계로 주제 복귀, 역할 해제 및 규칙 우회 요청 거부를 추가했습니다.
- `Settings/CpcResponsePolicy.asset`: 필터 활성화, `Blocked Expressions` 금지 표현 목록, `Replacement` 대체 대사 편집.
- `Runtime/CpcResponseFilter.cs`: Unicode 정규화, 공백·기호 제거 검사, 영문 단어 경계 검사.
- `Runtime/LocalLlmClient.cs` → `Publish`: NPC 화면·로그·Replied 이벤트로 보내기 전에 필터 적용.
  모델 응답의 assistant 대화 기억도 필터를 통과한 대사만 저장합니다.
- 차단 시 원문을 NPC 로그로 출력하지 않고 `[CPC/Filter]` 경고만 남깁니다.
  사용자 입력 로그(`[Player/Dialogue]`)는 디버깅용 원문입니다.
- 금지어 기반 필터는 모든 우회 표현·문맥·주제 이탈을 판별하지 못하고 오탐도 가능합니다.
  주제 이탈은 시스템 프롬프트로 유도하며 의미 기반 분류기는 포함하지 않습니다.
- 검증: `CpcPrototype.Editor.CpcResponseFilterChecks.Run()` (편집 모드에서 실행).

### HTTP 연결 경로

`CpcChatPanel` → `CompanionCpc.SayToCompanion` → `LocalLlmClient.Enqueue` →
`UnityWebRequest` HTTP POST → `127.0.0.1:11434/api/chat` → Ollama → `gemma4:26b`.
게임 사건도 `CompanionCpc.SendEvent`에서 같은 클라이언트 큐를 사용합니다.
시스템 프롬프트 + 최근 4쌍의 대화/사건 + 현재 요청을 UTF-8 JSON의 `messages`로 보냅니다.
응답 JSON의 `message.content`를 읽어서 `LastReply`, `Replied` 이벤트, `[CPC/Gemma]` 로그로 전달합니다.
현재 별도 Python 에이전트 서버나 도구 호출 루프는 없으며, Unity가 이벤트·상태·기억·행동을 관리합니다.
LLM은 한국어 대사를 생성하며 따라가기/점프/몬스터 행동 트리는 Unity 코드가 실행합니다.

### 한글 채팅

채팅 입력은 IMGUI `GUI.TextField`에서 UI Toolkit `TextField`로 교체했습니다.
새 Input System의 IME를 켜고, OS 한글 글꼴(Windows 맑은 고딕)을 사용합니다.
한글 조합 중 Enter는 조합 확정에 사용하며, 조합이 끝난 뒤 다시 Enter로 전송합니다.
전송 버튼은 포커스 변경에 따른 마지막 음절 확정이 반영되도록 두 프레임 뒤 값을 읽습니다.
조합이 계속 남아 있으면 전송을 보류하고 입력창을 다시 선택합니다.
초안은 Esc로 닫았다가 다시 열어도 보존합니다. 전송 성공 시에만 비웁니다.
Windows 한/영 전환은 OS 입력기에서 수행합니다.
`Settings/ChatPanelSettings.asset`, `UI/ChatRuntimeTheme.tss`가 채팅 패널 설정입니다.

`Settings/GemmaLocalSettings.asset`를 선택하면 다음 항목을 편집할 수 있습니다.

| 항목 | 기본값 / 의미 |
|---|---|
| Enable Llm | 켜면 모델 호출, 끄면 기본 대사만 출력 |
| Endpoint | `http://127.0.0.1:11434/api/chat` (Ollama 전용 요청 형식) |
| Model | 로컬 설치를 확인한 `gemma4:26b` |
| Timeout Seconds | 120초 |
| Temperature | 0.5 |
| Max Output Tokens | 테스트 설정 80 |
| Context Tokens | 2048 |
| Gpu Layers | 16 (GPU 메모리가 충분하면 조절, -1 자동 / 0 CPU) |
| System Prompt | 제임스의 성격/말투/대사 규칙 |
| Event Prompt | 게임 사건 입력 템플릿 |
| Dialogue Prompt | 플레이어 대화 입력 템플릿 |

사용 가능한 치환 키: `{event}`, `{message}`, `{playerHealth}`, `{companionHealth}`.
`{event}`는 사건 프롬프트, `{message}`는 대화 프롬프트에서 사용합니다.
시스템 프롬프트에는 위 치환을 하지 않습니다.
설정을 영구 보존하려면 Play를 중지한 상태에서 수정하고 저장하세요.

Ollama가 실행되어 있어야 합니다. 모델 목록은 `/api/tags`로 조회합니다.
요청은 비스트리밍이며 thinking을 끄고, 최근 대화/사건 4쌍을 유지합니다.
동시 요청은 1개이며 대화는 FIFO 큐에서 입력 순서대로 처리합니다.
새 대화는 대기 중인 사건을 비우고 생성 중인 사건 알림을 취소하여 우선 처리합니다.
대화 대기/진행 중에는 새 사건의 즉시 로그만 남기고 모델 알림 생성은 생략합니다.
대화 대기열은 최대 8개이며 초과 시 상태 표시와 경고를 출력합니다.
프로토타입 실행 중 `Application.runInBackground`를 켜서 다른 창으로 전환해도 HTTP 응답을 처리합니다.
컴포넌트가 비활성화되면 이전 백그라운드 실행 설정으로 복원합니다.
HUD에 전송 대기/답변 생성/경과 시간/응답 실패 상태가 표시됩니다.

정상 응답은 `[CPC/Gemma]`, 실패 대사는 `[CPC/Fallback]`으로 구분합니다.
타임아웃/서버 오류가 발생해도 게임은 계속 진행하며 실패 후 10초 동안 기본 대사를 사용합니다.
최초 모델 로딩과 GPU/RAM 경쟁 때문에 응답이 느릴 수 있습니다.
설치 모델은 Q4_K_M 약 18GB이며, 테스트 PC에서는 큰 기존 씬을 함께 실행할 때 첫 요청 500/타임아웃이 발생했고
테스트 씬만 분리한 뒤 Unity에서 한국어 모델 응답을 수신했습니다.

## 화면 / 애니메이션

좌측 위에 체력과 조작법, 아래에 최근 대사, 몬스터 위에 체력과 행동 상태를 표시합니다.
PICOCHAN 원본에는 모션 클립이 없어 `PicoProceduralPose`의 간단한 걷기/팔 공격 포즈와 바운스를 사용합니다.
실제 애니메이션 클립이나 Animator Controller를 적용할 때는 해당 포즈 컴포넌트를 비활성화하세요.
카메라 장애물 충돌과 세이브/리스폰은 이번 테스트 범위에 포함하지 않습니다.

## 검증

Unity 컴파일 및 플레이 모드에서 가상 키보드/마우스로 아래 14개 항목을 확인했습니다.
이동, 달리기, 점프, 동료 동시 점프, 좌클릭 공격, 동료 추종, BT 추적, BT 복귀,
원위치 대기, 몬스터 공격과 플레이어 피해, 동료 피해, 몬스터 사망,
사망 몬스터 공격 중단, 플레이어 사망 시 이동 중단.

결과: 프로젝트 루트 `Screenshots/CpcSmokeReport.json`.
재검증은 Play에서 플레이어에 `PrototypeSmokeCheck`를 추가하면 수행됩니다.
검증은 플레이 중 캐릭터를 이동시키고 체력을 소모하므로 완료 후 Stop → Play로 초기화하세요.
검증 컴포넌트는 에디터 전용이며 저장된 씬에는 추가하지 않았습니다.

기존 URP 설정에서 GPU Resident Drawer / Forward+ 구성 관련 반복 경고가 발견되었습니다.
프로토타입의 컴파일 오류/런타임 예외는 없으며 전역 렌더링 설정은 변경하지 않았습니다.
