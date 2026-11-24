# BUG-005 — FixedUpdate 역할별 함수 분리

2026-11-17. PlayerGrappleAction.FixedUpdate를 다음 5개 private 메서드로 분리했습니다.

| 메서드 | 역할 |
|---|---|
| CanContinueTravel | 이동 상태·앵커·컨트롤러 확인, 무효 시 Release |
| TryEnterSwing | 줄 발사 대기·후킹 재검사·스윙 진입과 타이머 초기화 |
| CalculateSwingTarget | Smoothstep 진행률의 반원 목표 위치, 기존 Debug.Log 유지 |
| TryMoveToSwingTarget | CharacterController 이동·기존 거리 비교에 따른 취소 |
| CompleteSwing | Holding·대기 시작 시간·ResetMotion·Arrived 순서 유지 |

기존 공개 필드·네임스페이스·타이밍·궤적·이벤트 순서는 유지했습니다. FixedUpdate에서 진행률을 구해 이동하고, 성공한 마지막 틱에만 CompleteSwing을 호출합니다.

- 컴파일: Unity script_recompile 성공, 오류 0.
- [통합](Checks.txt): 실제 씬의 가상 Mouse 입력·CharacterController·원호 반경/반대편 종착점·충돌 취소·스나이퍼·시간 복원 65 PASS.
- [경계/회귀](BoundaryChecks.txt): 채팅·포커스 상실 콜백·사망·줄 발사 중 장애물·이동·라이플 13 PASS.
- [Console](ConsoleErrors.json): 실행 오류 0.

기존 AgentScripts/GrappleSniperChecks.cs와 GrappleSniperBoundaryChecks.cs를 순서대로 Unity MCP script_run으로 실행했습니다. 초기 컴파일 직후 연결이 일시 끊겼으며 editor_status의 ready 확인 후 플레이를 시작했습니다. 검사 후 Play를 중지하고 Run In Background override를 해제했습니다. 플레이어 빌드는 미실행입니다.
