# FIX-001 검증 (2026-11-17)

그래플 이동 중 기본 3인칭을 유지하고 도착 후에만 1인칭으로 전환합니다. 전구 콜라이더 6개를 제거하고, 실제 카메라의 화면 후처리 픽셀 수치를 1인칭에서 2배로 보간한 뒤 복귀 시 원래 값으로 되돌립니다.

- [전용 플레이 검사](Checks.txt): 17 PASS. 이동 중 시점·시간·화면 픽셀 값 유지, 도착 후 전환, 200→400→200 및 임의 값 173→346→173, 채팅/비활성화 복구, 전구 위치에서 실제 추락.
- [기존 그래플/스나이퍼 회귀](Regression.txt): 57 PASS.
- [씬 재로드](SceneChecks.txt): 15 PASS. 실제 화면 머티리얼 연결, 기본 수치, 콜라이더 제거와 기존 장비 참조 보존.
- [Console](ConsoleErrors.json): 플레이 실행 오류 0. 기존 GPUResidentDrawer 경고는 BUG-001로 유지.
- Unity 컴파일 오류 0. Windows Unity 6000.6.3f1에서 가상 InputSystem 장치와 실제 CharacterController/렌더 머티리얼을 검사했습니다. 플레이어 빌드는 검사하지 않았습니다.

설정: Player의 GrappleSniperAction.ScreenPixelMaterials / ScreenPixelProperty / ScreenPixelMultiplier (기본 2). 실제 씬은 CityPixelPass.mat의 _PixelHeight를 사용합니다. 캐릭터의 ActorPixelQuad 해상도 증가도 함께 유지합니다.

재실행: Play에서 AgentScripts/Fix001Checks.cs의 Execute를 unity command script run으로 실행합니다. 검사는 AI·위치·체력을 일시 변경하므로 완료 후 Play를 중지합니다. SceneChecks는 Play 중지 및 씬 저장 후 실행합니다. 기존 설치 메뉴도 GrappleFix001Setup.Apply를 호출하므로 재설치 시 콜라이더가 복원되지 않습니다.
