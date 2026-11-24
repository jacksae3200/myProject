# M 키 한국어 음성 입력

대상 씬: `Assets/Scenes/KayKit_DepthSurvival.unity`.

M을 누르면 마이크를 열고, 놓으면 즉시 닫습니다. `Cpc Voice Input > Recognition Mode`로 **SlidingWindow(기본)** 또는 기존 **BatchInference**를 선택할 수 있습니다. SlidingWindow는 M을 누른 채 말하는 동안 겹치는 음성 구간을 인식하고, 발화가 끝난 뒤 무음이 이어지면 M을 놓기 전에도 최종 문장을 보냅니다. BatchInference는 종전처럼 M을 놓은 뒤 녹음 전체를 한 번 인식합니다. 두 모드 모두 중간 결과는 CPC에 보내지 않습니다. 최종 결과만 `CpcChatPanel.TrySendToCompanion → ShieldCompanion.SayToCompanion → CpcTactics.Chat → LocalLlmClient(Ollama)`로 한 번 전달합니다. 기존 Enter 채팅도 같은 경로입니다.

두 방식 모두 같은 로컬 `/inference` 서버를 사용합니다. 새 방식은 Unity에서 윈도우를 나눠 순차 HTTP 요청하는 방식으로, 서버 자체의 스트리밍 API는 아닙니다. 설정·비교 방법과 한계는 [SlidingWindow.md](SlidingWindow.md)를 참고하세요.

화면 왼쪽 아래에 녹음 중 / 음성 인식 중 / CPC 응답 대기 중 / 실패 상태를 표시합니다. CPC의 답은 기존 말풍선에 표시됩니다. 채팅창이 열려 있거나 창 포커스를 잃으면 녹음/인식을 취소합니다. 다시 M을 누르면 이전 STT 요청을 취소하고 세대 번호를 바꾸므로 늦은 결과가 전달되지 않습니다. 이미 CPC에 전송된 완료 발언은 취소하지 않습니다.

## Windows 로컬 STT 설치

기존 STT 연동이 없어 **whisper.cpp**의 로컬 HTTP 서버를 선택했습니다. 클라우드 키나 Python이 필요하지 않습니다. 소스 빌드에는 Git, CMake, Visual Studio Build Tools의 **C++를 사용한 데스크톱 개발**과 Windows SDK가 필요합니다. 이미 whisper-server.exe가 있다면 빌드를 건너뛰어도 됩니다.

Unity 프로젝트 밖의 원하는 작업 폴더에서 PowerShell로 실행합니다.

```powershell
git clone --branch v1.9.5 --depth 1 --recurse-submodules https://github.com/ggml-org/whisper.cpp.git
cmake -S whisper.cpp -B whisper.cpp/build -DWHISPER_BUILD_SERVER=ON -DWHISPER_BUILD_TESTS=OFF -DGGML_CUDA=OFF
cmake --build whisper.cpp/build --config Release --target whisper-server -j 4
Invoke-WebRequest -Uri 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin' -OutFile 'whisper.cpp/models/ggml-small.bin'
```

`small`은 다국어 모델(약 466 MiB)입니다. **`.en` 모델은 사용하지 마세요.** CPU 부담이 크면 `ggml-base.bin`(약 142 MiB)으로 바꿀 수 있으나 실제 한국어 정확도는 마이크와 환경에 따라 확인해야 합니다. 실행 파일에 딸린 DLL도 같은 폴더에 두세요.

프로젝트 루트에서 다음 명령으로 서버를 시작합니다. 예시 절대 경로는 실제 설치 위치로 바꾸세요.

```powershell
.\Tools\Voice\Start-LocalStt.ps1 -ServerPath 'D:\STT\whisper.cpp\build\bin\Release\whisper-server.exe' -ModelPath 'D:\STT\whisper.cpp\models\ggml-small.bin'
```

서버는 CPU를 사용하고 `127.0.0.1:8080`에만 바인딩합니다. Unity/Ollama GPU 사용과 겹치지 않게 했습니다. 모델 로딩 완료는 `Tools/Voice/logs/stt.stderr.log`에서 확인하세요. 서버를 직접 실행할 수도 있습니다:

```powershell
& 'D:\STT\whisper.cpp\build\bin\Release\whisper-server.exe' -m 'D:\STT\whisper.cpp\models\ggml-small.bin' -l ko --host 127.0.0.1 --port 8080 -ng
```

공식 근거: [서버와 /inference API](https://github.com/ggml-org/whisper.cpp/tree/master/examples/server), [모델 목록](https://github.com/ggml-org/whisper.cpp/tree/master/models), [버전](https://github.com/ggml-org/whisper.cpp/releases/tag/v1.9.5).

## Unity / Windows 설정

1. Windows 설정 → 개인정보 및 보안 → 마이크 → 마이크 액세스와 데스크톱 앱 액세스를 켭니다. Unity Editor도 데스크톱 앱입니다. 거부/장치 오류는 화면에 표시되며 CPC 요청을 보내지 않습니다.
2. 플레이어의 `Cpc Voice Input`: `MicrophoneDevice`가 비어 있으면 첫 장치. 장치를 지정하려면 정확한 장치명을 입력합니다. `SilenceRmsThreshold` 기본 .008, 최소 발언 .2초, 최대 녹음 30초입니다. 최대 시간을 넘으면 자동 전송하지 않고 취소합니다.
3. 같은 오브젝트의 `Whisper Local Stt`: Endpoint=`http://127.0.0.1:8080/inference`, Language=`ko`, TimeoutSeconds=90. 로컬 주소만 허용합니다. WAV PCM16 mono를 사용하므로 ffmpeg 변환은 불필요합니다.
4. 기존 `GemmaLocalSettings`와 Ollama 모델을 사용합니다. Ollama 서버가 실행되어 있어야 합니다. 새 Ollama 모델을 자동 다운로드하거나 기존 모델 설정을 바꾸지 않았습니다.
5. Play → Game 창 포커스 → M을 누르고 “여기서 기다려” → SlidingWindow에서는 무음 후 최종 전달 확인, BatchInference에서는 M 해제 후 확인 → M 해제 → 6초 이상 기다리기 → “지금 뭐 하고 있어?” → “따라와”. 채팅창을 열면 M은 문자 입력으로만 취급됩니다.

플레이어가 내린 Hold와 Follow는 새 지시까지 유지됩니다. 긴급 회피나 저체력 후퇴 중에는 잠시 중단하고, 안전해진 뒤 유효한 지시를 재개합니다. 상태 질문은 현재 실제 행동을 프롬프트에 포함하며, 응답의 action이 잘못 나와도 기존 명령을 바꾸지 않습니다. 일반 자동 전술은 기존 6초 유효기간과 대상/허용 행동 검증을 유지합니다. 서버 실패 시 수락한 것처럼 말하지 않고 행동을 유지합니다.

## 재현 가능한 검증

Play 모드에서 `Tools/CPC/Run Voice Feature Checks (Play Mode)`를 실행합니다. 실제 마이크 대신 녹음기/인식기를 주입하고 M 키 입력을 Unity InputSystem에 넣습니다. 최종 인식 중복·무음·빈 결과·권한 실패·STT 오류·채팅 M·포커스 취소·빠른 재입력·비활성화 정리와 Unity Hold/Follow/후퇴를 검사합니다. 결과는 `Temp/CpcVoiceFeatureChecks.txt`와 Console에 남습니다. 이 검사는 실제 음성 인식 정확도나 실제 Windows 권한 거부를 재현했다는 의미가 아닙니다.

실제 음성 인식 및 Ollama 대화는 별도로 위 수동 절차를 확인해야 합니다. 마이크를 자동으로 녹음하지 않으며 M 입력이 있어야 사용합니다. 서버/모델 설치는 위 방법으로 사용자가 수행할 수 있습니다.


