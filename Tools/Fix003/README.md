# FIX-003 — Downtown 직선 도시 (2026-11-17)

KayKit_DepthSurvival 씬의 길이 24m/X축 진행을 유지하고 Downtown City MegaKit[Standard]/Exports/FBX (Unity)의 Street_2Lane 4개와 Building_Small_1 / Building_Medium_2_001 / Building_Large_2를 양쪽 6채씩 배치했습니다. 원본 패키지 에셋은 변경하지 않습니다.

Hierarchy의 Downtown City Street 아래 Road and Sidewalks, South Buildings, North Buildings를 편집하면 됩니다. StreetBuildingCulling.Player / Sniper / NegativeSide / PositiveSide는 저장된 참조이며 SideDeadZone은 카메라가 플레이어와 같은 Z 위치에 있을 때의 경계값입니다. 기본 3인칭은 남쪽 행을 숨기며 카메라가 반대로 이동하면 북쪽 행을 숨깁니다. 1인칭 진입부터 복귀 보간 종료까지 컬링을 해제하며 스코프/캐릭터 캡처/Scene View에는 적용하지 않습니다. Renderer.forceRenderingOff를 렌더 직전에 저장·변경하고 직후 복원하며 Collider는 유지합니다.

기존 KayKit City의 도로·건물·차량 등은 비활성 보존했습니다. Walkable Street와 외곽 4개 충돌 및 Pixel Street Props의 그래플 가로등 6개는 유지합니다. 새 보도 2개와 건물 12채에 충돌이 있습니다. 변경 전 씬 사본: Temp/Fix003/Before.unity. 설치 도구: Assets/CpcPrototype/Editor/DowntownStreetSetup.cs (이미 설치된 씬에서는 중복 실행을 거부).

- [플레이 검사](Checks.txt): 32 PASS. 양쪽 배치·머티리얼·바닥 12지점·이동·라이플 입력·그래플·스코프·시점 복귀·렌더 상태 복원과 실제 프레임 진행.
- [기존 통합 회귀](Regression.txt): 57 PASS. 실제 가상 Mouse 입력, 피해·총알/효과·반동·장비·시간 복구.
- [경계 회귀](BoundaryChecks.txt): 13 PASS. 채팅·포커스 콜백·사망·도중 장애물·이동/라이플 복귀.
- [씬 재로드](SceneChecks.txt): 12 PASS. 저장 참조·모델 프리팹·충돌·6개 앵커·미저장 변경 없음.
- [Console](ConsoleErrors.json): 실행 오류 0, 컴파일 오류 0. 기존 GPUResidentDrawer / Forward+ 경고는 BUG-001에 유지.
- 화면: [전체](../../Assets/Screenshots/Fix003/Overview.png), [3인칭](../../Assets/Screenshots/Fix003/ThirdPerson.png), [1인칭](../../Assets/Screenshots/Fix003/FirstPerson.png), [스코프](../../Assets/Screenshots/Fix003/Scoped.png).

Unity 6000.6.3f1 Windows Editor의 실제 씬·렌더 콜백·물리와 가상 입력을 검사했습니다. 플레이어 빌드·실제 마이크·외부 모델 검사는 하지 않았습니다. 검사 작성 중 바닥 광선이 플레이어에 먼저 맞는 문제는 바닥 Collider 직접 검사로 수정한 후 재실행했습니다.

재실행: Play 모드에서 unity command script run --project-path . -- --file AgentScripts/Fix003Checks.cs --entry Fix003Checks.Execute --timeout 50. 검사는 AI·체력·위치를 변경하므로 종료 후 Play를 중지합니다. 이번에는 CLI discovery 파일 접근이 거부되어 동일 프로젝트의 Unity MCP script_run을 사용했습니다.
