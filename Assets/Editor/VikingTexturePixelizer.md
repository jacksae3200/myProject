# Viking Village 텍스처만 픽셀화

Unity 메뉴 **Tools > Pixelize > Viking Texture Only**에서 Maximum Texture Size를 고른 후 **Create Texture Only Preview**를 누릅니다. 32는 큰 픽셀, 64는 기본, 128/256은 더 세밀한 결과입니다. 매번 새 폴더와 씬을 생성하므로 기존 결과를 덮어쓰지 않습니다.

원본은 `Assets/Viking Village/Scenes/The_Viking_Village.unity`, 생성 결과는 `Assets/VikingTexturePreview/Preview64/Viking_TextureOnly.unity`입니다. 같은 해상도로 다시 만들면 폴더명에 번호가 붙습니다.

## 구현

- 원본 씬의 Renderer에 연결된 머티리얼을 복제합니다. 기존 셰이더, 색상, 타일링, 투명도와 조명 설정을 유지합니다.
- 머티리얼의 Texture2D 중 Viking Village 폴더에 속하고 TextureImporter가 있는 파일을 복제합니다. 같은 텍스처와 머티리얼은 복사본을 공유합니다.
- 복사본의 Max Size를 64로 제한하고 Filter Mode를 Point로 설정합니다. 원본 종횡비에 따라 한쪽 크기는 64보다 작을 수 있습니다. Mip Maps를 끄고 압축을 해제해 픽셀 경계를 선명하게 만듭니다. 노멀맵 등도 원래 Texture Type과 색공간 설정을 유지한 채 해상도를 낮춥니다.
- 복제 머티리얼의 텍스처 참조만 교체하고 새 씬으로 저장합니다. 원본 텍스처와 머티리얼은 변경하지 않습니다.
- MeshFilter.sharedMesh와 SkinnedMeshRenderer.sharedMesh 참조를 전후 비교하고 버텍스 합계를 Verification.txt에 기록합니다. 합계에는 비활성 오브젝트와 모든 LOD 인스턴스도 포함되므로 화면의 실제 렌더링 통계와 다릅니다.

화면 해상도를 낮추는 후처리는 추가하지 않습니다. 모델 외곽선은 원래 해상도로 유지되고 표면 텍스처만 각져 보입니다. 조명과 그림자는 기존 셰이더 방식으로 계산되므로 그라데이션이 남을 수 있습니다.

기존 프로젝트의 전체화면 픽셀화가 겹치지 않게 Renderer Data도 비교 폴더에 복제하고 FullScreenPassRendererFeature를 비활성화합니다. 복제 렌더러를 현재 URP 에셋의 목록에 추가하고 비교 씬 카메라에만 할당합니다. 기존 렌더러와 기본 렌더러 선택은 유지됩니다. 비교 이미지 `Screenshots/VikingTextureOnly/Original.png`와 `Pixel64.png`는 동일 카메라, 동일 전용 렌더러로 촬영했습니다.

복제한 원본 이미지 파일은 디스크에 그대로 보관되므로 프로젝트 소스 용량은 늘어납니다. Unity가 임포트한 GPU 텍스처 해상도만 낮아집니다. Mip Maps를 끄면 먼 표면이나 움직이는 화면에서 반짝임이 생길 수 있습니다.

적용 범위는 Renderer 머티리얼의 파일 기반 Texture2D입니다. Skybox, TerrainLayer, 큐브맵, 런타임 생성 텍스처는 별도로 변경하지 않습니다. 버텍스 감소나 메시 단순화는 수행하지 않습니다.
