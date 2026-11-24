# Scope Lens Shader Graph

완료: 2026-11-17 / BUG-002.

- 사용 중인 그래프: [ScopeLensGraph.shadergraph](../../../Assets/sniper/ScopeLens/ScopeLensGraph.shadergraph)
- 실제 머티리얼: [ScopeLens.mat](../../../Assets/sniper/ScopeLens/ScopeLens.mat)
- 보존한 원본: [ScopeLens.shader](../../../Assets/sniper/ScopeLens/ScopeLens.shader)

Unity Project 창에서 ScopeLensGraph.shadergraph를 더블클릭하면 노드 편집 화면이 열립니다. URP Unlit, Transparent / Alpha, 양면, Depth Write Off로 구성했습니다. 기본 Shader Graph 노드로 계산합니다.

## 그래프 구성

1. UV Radius: UV × 2 − 1의 길이로 원형 반경 계산.
2. Circular Mask: abs(DDX) + abs(DDY)로 원본 fwidth를 구성하고 Edge Softness와 최댓값을 사용한 Smoothstep 마스크.
3. Normal / Zoom: tangent-space 노멀맵의 XY와 반경 제곱으로 왜곡, 중심 기준 확대 후 UV Saturate.
4. Camera / Tint / Vignette: 카메라 텍스처 샘플에 틴트와 반경 4제곱 비네팅 적용, 원형 마스크와 틴트 알파 연결.
5. Reticle: 카메라 영상의 최종 RGB를 `Lerp A`, 메시 UV0로 읽은 `_ReticleTex`의 RGB를 `Lerp B`, 알파를 `Lerp T`에 연결합니다. 공유 `ScopeLens.mat`에서 `Assets/sniper/reticle.png`를 할당했습니다.
6. Scope Outer Gradient: `Lerp` 출력에 `1 - Smoothstep(0.45, 0.98, r)`을 곱해 `Base Color`에 연결합니다. 렌즈 중심은 반경 0, 렌즈 테두리는 반경 1입니다. 시작·종료 위치는 `Scope Outer Gradient` 그룹의 두 Float 노드에서 조절합니다.

Blackboard의 기존 렌즈 속성은 `_MainTex`, `_BumpMap`, `_Distortion`, `_Zoom`, `_EdgeSoftness`, `_Vignette`, `_Tint`입니다. 조준선 이미지 속성의 참조 이름은 `_ReticleTex`입니다. 기존 씬과 AWM 프리팹은 같은 공유 머티리얼을 사용합니다.

## 검증

BUG-006에서 흰 테스트 영상의 중심과 바깥 반경 밝기(255, 255, 243, 185, 90, 18 / 255), 원형 마스크, 중앙 조준선을 GPU 렌더로 확인했습니다. 실제 씬에서 우클릭 조준 및 해제 후 복귀를 확인했습니다.

FEAT-005에서 셰이더 컴파일, 조준선 유무·카메라 효과 변경에 따른 GPU 렌더 비교, 실제 씬의 우클릭 조준·해제 및 카메라 복귀를 확인했습니다. 아래 원본 비교는 조준선 추가 전 BUG-002 검증 기록입니다.

- [GPU 렌더 비교](RenderComparison.txt): 기본값, 왜곡 없음, 양/음 곡률, 최대 확대, 부드러운 가장자리, 반투명 틴트, 뒷면, 저해상도 안티앨리어싱의 9가지 조건. 원본과 그래프의 RGB 픽셀 차이 모두 0/255.
- 예: [원본](Default_Original.png), [그래프](Default_Graph.png).
- [머티리얼 연결](MaterialChecks.txt): 값 보존·저장 후 재로드·프리팹·현재 씬 적용 통과.
- [플레이 검사](GameplayChecks.txt): 실제 InputSystem 가상 입력으로 그래플→조준→사격→복귀 57개 검사 통과. [조준 화면](GameplayScoped.png).
- [플레이 Console](ConsoleErrors.json): 실행 오류 0. 기존 URP GPUResidentDrawer 경고는 별도 BUG-001에 유지.
- 원본 SHA-256: 6182a3407173cd8fb9520bdbd2fcba57431cf545997458a6ee09093dd8d1651b. 작업 전후 동일하며 파일을 수정·삭제하지 않았습니다.

렌더 비교는 Windows Unity 6000.6.3f1 / 현재 GPU의 편집기 카메라에서 RGB 결과를 검사했습니다. 플레이어 빌드나 다른 GPU는 검사하지 않았습니다.

## 도우미

- [BuildScopeLensGraph.cs](../../../AgentScripts/BuildScopeLensGraph.cs): BUG-002 시점의 초기 렌즈 구성을 작성합니다. `Rebuild`는 현재 그래프에 추가된 조준선과 수동 편집을 덮어쓰므로, 현재 렌즈는 Shader Graph 편집기에서 수정하세요.
- [VerifyScopeLensGraph.cs](../../../AgentScripts/VerifyScopeLensGraph.cs): 원본과 그래프를 분리된 프리뷰 씬에서 렌더 비교합니다.
- [ApplyScopeLensGraph.cs](../../../AgentScripts/ApplyScopeLensGraph.cs): 기존 값을 검사하면서 공유 머티리얼을 교체하고 저장합니다.
- [SetupScopeLens.cs](../../../AgentScripts/SetupScopeLens.cs): 재설정 시에도 새 그래프를 사용하도록 갱신했습니다.
