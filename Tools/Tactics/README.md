# CPC 전술 확장

기본 플레이 씬은 `Assets/Scenes/KayKit_DepthSurvival.unity`, 분리된 검증 씬은 `Assets/Scenes/KayKit_TacticsTest.unity`입니다. 기존 `GemmaLocalSettings`, Ollama `/api/chat`, CPC 채팅 UI, 음성 입력 경로, NavMesh, 방패 피해 감소, 도발, 근접 공격을 재사용합니다.

## 실행 경로

`좀비 스킬/근접 공격 → SurvivalZombie.Threat → CpcThreatSensor → CpcTactics → LocalLlmClient → JSON 검증 → CpcTacticalMotor/ShieldCompanion`

텍스트 입력은 기존 Enter 채팅창의 전송 버튼을 사용합니다. 음성 입력도 같은 `TrySendToCompanion → SayToCompanion → CpcTactics.Chat` 경로입니다. 자연어의 의도와 대상은 모델이 해석하며 특정 문장과 일치시키는 명령 사전은 없습니다.

응답의 기본 필드는 `action`, `targetId`, `speech`입니다. 추가 `intent`는 `command`, `question`, `clarify` 중 하나입니다. 기존 3개 필드 응답도 처리합니다. 질문/되묻기에는 `action=none`을 요청하며 기존 지시는 유지됩니다. 대상을 생략한 전투 부탁에는 관측한 후보의 위험도와 플레이어 기준 거리로 스스로 대상을 고릅니다. targetId=auto, targetType, targetPolicy로 선택을 위임할 수 있고, Unity가 실제 대상과 경로를 다시 검증합니다. 후보가 없거나 행동 자체가 불명확할 때만 확인합니다. 질문으로 분류했는데 이동 행동이 포함된 모순된 응답은 이동을 실행하지 않고 실제 상태를 표시합니다.

## 관측 정보

- 세션 동안 안정적인 `zombie_<EntityId>` 식별자와 유형.
- Approach / Aiming / Windup / Projectile / Emerging / Recovering / Leaping / Exploding 단계와 공격 단계 버전.
- 공격 대상(player/CPC), 예상 타격점, 공격 방향, 위험 반경, 남은 예고 시간.
- 플레이어와 CPC의 위치·체력, 유지 중인 플레이어 지시, 실제 수행 중인 행동.
- 노출된 적의 위치·체력. 숨은 버로우는 주황색 지면 출현 예고가 보일 때만 **그 흔적의 위치**를 전달합니다. 숨은 본체의 위치·체력은 전달하지 않습니다.

거리와 물리 시야를 검사하고 최대 48개를 센서에 저장합니다. 모델 요청에는 우선 위협부터 최대 8개를 넣습니다. 위치가 조금 변하거나 시간이 흐르는 것만으로는 요청하지 않고 적 구성, 공격 단계/대상, 우선 위협 변화, 피격에 반응합니다. 우선 위협에는 히스테리시스가 있어 근소한 거리 차이로 요청이 흔들리지 않습니다. 동일 신호는 중복 요청하지 않으며 모델 처리 중에는 최신 변화로 합칩니다.

각 대상에는 Unity 검증을 통과한 `feasible` 행동 목록도 제공합니다. `evade` 역시 피할 공격을 낸 좀비의 ID를 필수로 사용합니다. 최종 선택은 다시 현재 상태로 검증하므로 이 목록은 실행 보증이 아닙니다.

## 행동

기존 `follow`, `hold`, `guard`, `taunt`, `attack`, `retreat`를 유지합니다.

- `intercept`: 플레이어와 침 공격 사이에 방패 위치를 계산하고, 예고 안에 도착 가능한 완전한 NavMesh 경로를 검사합니다. CPC 앞의 실제 트리거와 투사체가 충돌하면 투사체를 소비하고 기존 방어 피해 배율(.2)을 적용합니다.
- `evade`: 타격 반경 밖 후보 위치 16개를 검사하고 다른 관측 위험, 장애물, 층 높이와 경로를 검증합니다.
- `counterattack`: 공격 후 Recovering 단계에만 기존 근접 공격을 시도합니다. 도착 시간, 재사용 대기와 타격 준비 시간을 합쳐 빈틈 안에 타격 가능한지 확인합니다. 실제 타격 순간에도 대상 생존·거리·시야·Recovering 단계를 다시 검사합니다.

플레이어의 follow/hold는 새 지시까지 지속됩니다. hold는 수락한 위치를 저장하므로 긴급 이동 후 그 위치로 돌아갑니다. 대상 지시는 대상 사망·관측 불가·무효화 또는 도발/반격/회피 완료 시 종료됩니다. 공격/가로막기는 지시한 대상을 계속 우선합니다. 새 지시가 수락되면 이전 지시를 대체하고, 질문은 대체하지 않습니다. Hold/지정 대상 지시는 일반 자율 판단으로 덮어쓰지 못합니다. 기본 Follow는 추종을 유지하면서 주변 위협과 교전할 수 있습니다. 거리가 벌어지면 교전을 끊고 플레이어에게 복귀합니다. 명시적인 비전투 추종은 followMode=passive로 구분합니다.

짧은 공격 예고에는 Unity가 즉시 대응합니다. 침 가로막기, 위험 범위 이탈, 가까운 기어오는 좀비 도발을 우선 시도하고, 시간/경로상 불가능하면 그 자리에서 방패를 듭니다. 순간이동이나 플레이어 강제 이동은 하지 않습니다. 기존 저체력 후퇴는 유지되며 플레이어 지시는 지우지 않고 잠시 중단합니다. 안전해진 뒤 아직 유효한 지시를 재개합니다.

모델이 좌표를 지정하지 않습니다. Unity가 경로와 목적지를 계산합니다. 수락 전과 실행 직전에 대상·시야·경로·공격 단계·타이밍을 다시 검사합니다. 거절된 응답의 원래 speech는 버리고 거절 사유만 표시합니다. 오래된 상황 응답은 표시하지 않습니다.

## 로그

- `[CPC/Request/PlayerCommand]` / `[CPC/Selected/PlayerCommand]`: 채팅/음성에 대한 모델 선택.
- `[CPC/Request/SituationLlm]` / `[CPC/Selected/SituationLlm]`: 관측 변화에 대한 모델 선택.
- `[CPC/Safety]`: 모델을 기다리지 않은 즉시 안전 대응과 긴급 후퇴.
- `[CPC/Execute/<origin>]`: Unity 실행 시작.
- `[CPC/Reject/<origin>]`: 수락 또는 실행 직전 거절.
- `[CPC/Shield]`: 실제 투사체 가로막기.

CUDA 초기화/메모리 오류로 Ollama 추론이 실패하면 동일 모델을 `num_gpu=0`으로 한 번 재시도합니다. 이 기능은 LocalLlmClient의 `RetryGpuFailuresOnCpu`로 끌 수 있습니다. 모델명과 설정 에셋은 변경하지 않습니다. CPU로도 실패하면 기존 기본 대사와 안전 대응을 사용하고 게임을 계속 진행합니다.

## 재현

1. `KayKit_DepthSurvival`을 열고 Play 합니다. 필요하면 `Tools/CPC/Install Extended Tactics`로 참조를 다시 연결합니다.
2. 기존 Enter 채팅창에서 “여기서 기다려 줘” → 6초 이상 기다리기 → “어떤 놈이 제일 위험해?” → “이제 다시 따라와”를 입력합니다.
3. 보이는 Spitter가 있을 때 “저기 침 뱉는 놈부터 잡아”, “내 앞에서 침 좀 막아줘”를 입력합니다. Crawler가 플레이어에게 붙으면 “기어오는 놈 좀 떼어줘”를 입력합니다. 모델에 표시된 대상만 유효합니다.
4. 분리된 `KayKit_TacticsTest`에는 Spitter, Burrower, Crawler 각 1개만 활성화되어 있습니다. Spitter 조준, 주황색 버로우 출현 예고, Crawler 공격 후 회복 단계를 관찰합니다.
5. 이 테스트 씬의 Play 모드에서 `Tools/CPC/Run Extended Tactics Checks (Play Mode)`를 실행하면 모델 응답을 주입한 결정적 검사와 실제 물리/이동 검사가 실행됩니다.
6. 새 Play 세션에서 `Tools/CPC/Run Real Ollama Tactics Checks (Play Mode)`를 실행하면 **기존 모델에 실제 요청**합니다. 짧은 예고와 모델 지연을 분리해서 확인하기 위해 이 검사만 예고/회복 시간을 늘리고 기본 자율 폴백을 끕니다. 설정 에셋은 복제본을 사용하므로 저장된 실제 게임 수치에는 영향을 주지 않습니다.

결과는 `UnityChecks.txt`, `RealModelChecks.txt`에, 선택/실행 로그는 각 파일 아래에 기록됩니다. 실행 중/실패한 검사도 통과로 표시하지 않습니다. 실제 모델 검사가 실패해도 결정적 검사 결과는 별도 파일로 남습니다. 추가 검증 메모는 `VALIDATION.md`를 참고하세요.

## 변경 파일

- `CpcTactics.cs`: 관측 프롬프트, 자연어/JSON 의도, 플레이어 지시 우선순위와 수명, 신호 중복 억제, 응답 검증.
- `CpcThreatSensor.cs`, `ZombieThreat.cs`: 관측 가능한 위협 정보와 데이터 구조.
- `CpcTacticalMotor.cs`: 안전 경로, 가로막기/회피/반격, 즉시 대응과 실행 직전 검증.
- `ShieldCompanion.cs`, `CpcShieldInterceptor.cs`: 기존 NavMesh/도발/근접/방패 실행 재사용, 긴급 후퇴 시 지시 보존.
- `SurvivalZombie.cs`, `SpitterZombieAbility.cs`, `BurrowerZombieAbility.cs`, `BoomerZombieAbility.cs`, `JumperZombieAbility.cs`, `ZombieAbility.cs`, `SpitProjectile.cs`: 실제 공격 단계와 대상, 예고/흔적, 실제 투사체 방패 충돌.
- `LocalLlmClient.cs`: 기존 로컬 모델 재사용, 압축한 대화 이력, 구조화 요청 컨텍스트, GPU 오류 CPU 재시도.
- `CpcTacticsSceneSetup.cs`, `CpcExtendedTacticsChecks.cs`, `CpcVoiceFeatureChecks.cs`: 씬 연결, 재현 검사와 변경된 긴급 후퇴 정책 회귀.
- `KayKit_DepthSurvival.unity`, `KayKit_TacticsTest.unity`: 실제 연결과 분리된 테스트 배치. 기존 채팅 UI/음성 경로는 유지합니다.

## 따라오며 보호 / 자동 대상 선택 (후속 변경)

- 기본 “다시 따라와”: Follow 지시를 유지하면서 근처 도발·방패·공격 및 LLM 전술을 허용합니다. 교전 종료/적 사망/플레이어 거리 이탈 후 추종으로 돌아갑니다.
- “싸우지 말고 나만 따라와”: 모델이 followMode=passive를 선택하면 일반 교전 없이 추종합니다. 기존 긴급 안전 대응은 유지합니다.
- 전투 대상이 생략되면 관측한 후보 중 플레이어를 위협하는 특수 좀비와 플레이어 가까운 적을 우선합니다. 자동 선택한 경우 실제 유형에 맞춰 대사를 표시합니다.
- `targetPolicy=nearest`는 플레이어와 가장 가까운 후보, `targetType`은 명시한 유형만 선택합니다. 숨은 적이나 보이지 않는 적은 만들지 않습니다.
- CpcTactics의 FollowCombatRadius(기본 2), FollowRejoinDistance(기본 2.5)와 CpcThreatSensor의 PlayerThreatWeight / SpecialZombieWeight / DistanceWeight를 인스펙터에서 조정할 수 있습니다. 월드 거리 기준입니다.
- 최신 검사: Play 모드의 `Tools/CPC/Check Follow Protection (Play Mode)`와 `Check Follow Protection With Ollama (Play Mode)`.
- 결과 파일: `FollowProtectionUnity.txt`, `FollowProtectionModel.txt`. 이전 검사 문서의 ‘모호하면 되묻기’ 정책은 이 후속 변경으로 대체되었습니다.

## 군집 보호

`CpcCrowdProtection` 컴포넌트는 플레이어 주변에 좀비가 몰리면 LLM을 기다리지 않고 개별 추격을 중단하고 플레이어 곁으로 복귀합니다. 몰려오는 방향을 바라보며 방패를 들고, 가까운 대상에는 기존 도발도 사용합니다. 유효한 NavMesh 경로로만 이동하며 경로가 막히면 현재 위치에서 방패를 듭니다.

기본 설정은 CrowdRadius=1.25, EnterCount=3, ExitCount=1, MinimumGuardSeconds=1, ClearDelay=1, StandDistance=.22입니다. 이 값들과 ProtectDuringHold, UseTaunt는 인스펙터에서 조정할 수 있습니다. 거리는 프로젝트 월드 단위입니다. 기본값에서는 3마리 이상이면 보호를 시작하고 1마리 이하로 줄어든 상태가 1초 유지되면 기존 지시로 돌아갑니다. 중간 수량에서는 보호를 유지해 상태가 빠르게 반복되지 않게 합니다.

벽·차량 뒤의 적, 사망한 적과 숨은 버로우 본체는 군집 수에서 제외합니다. 플레이어의 가까운 시야/접촉 범위를 기준으로 하며, 좀비의 몸이 서로 가린다고 군집 수가 과소평가되지는 않도록 처리합니다. 저체력 긴급 후퇴와 즉시 위험 범위 회피가 군집 보호보다 우선합니다. Hold 중 보호를 허용하면 원래 대기 지시/위치를 보존하고 군집 해소 후 복귀합니다. ProtectDuringHold를 끄면 Hold 중 군집 복귀는 하지 않습니다.

CPC가 공격자와 플레이어 사이에 서서 그 방향으로 방어 중일 때는 실제 좀비 근접 공격을 CPC가 대신 받습니다. 기존 방어 배율(.2)을 적용하므로 피해 10은 CPC에게 2가 되고 플레이어에게는 적용하지 않습니다. 뒤나 측면의 공격까지 모두 차단하는 무적 상태는 아닙니다. 기존 투사체 방패 충돌도 유지합니다. 근접 가로막기 폭은 ShieldCompanion의 MeleeInterceptWidth로 조정할 수 있습니다.

모델에도 playerNearbyZombies/crowdThreshold/crowdProtecting 정보를 제공하고 protectplayer 행동을 추가했습니다. 즉시 군집 보호는 [CPC/Safety] 및 [CPC/Execute/Safety] ProtectPlayer로 기록하며 일반 명령을 지우지 않습니다.

검사 메뉴: KayKit_TacticsTest의 Play 모드에서 Tools/CPC/Check Crowd Protection (Play Mode). 실제 이동, 방향, 근접 피해 대리 수신, 보호 종료/대기 위치 복귀, 저체력 우선순위, NavMesh 상실, 벽 뒤 적 제외와 도달 불가 경로 거절을 검사합니다. 결과는 CrowdProtectionChecks.txt에 기록됩니다. 군집 즉시 보호 검사는 LLM 응답을 주입하지 않으며, 모델 추론 성공이 필요한 행동도 아닙니다.
