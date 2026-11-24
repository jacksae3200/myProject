# 슬라이딩 윈도우 음성 입력

## 선택과 사용

`KayKit_DepthSurvival` 플레이어의 `Cpc Voice Input > Recognition Mode`:

- `BatchInference`: 기존 경로. M을 놓은 뒤 전체 녹음을 `/inference`로 한 번 전송.
- `SlidingWindow`: M을 누르는 동안만 녹음. 음성 에너지를 감지하면 윈도우 인식을 시작. 중간 텍스트는 UI에만 표시. 발화 뒤 무음 또는 M 해제로 문장을 확정해 기존 채팅 경로로 Ollama에 한 번 전달.

편집 모드의 `Tools/CPC/Voice Mode` 메뉴로도 전환할 수 있습니다. 실행 중 모드를 바꾸면 이전 녹음과 요청은 취소됩니다. M을 계속 누른 상태에서 무음으로 구분한 여러 문장을 말할 수 있습니다. 전송된 문장은 키를 놓아도 재전송하지 않습니다. 채팅 입력, 포커스 상실, 씬 종료, 빠른 재입력은 이전 미완료 요청을 취소하고 늦은 콜백을 무시합니다. 이미 전달된 명령은 취소하지 않습니다.

## Inspector 설정

`Sliding Window Voice Input` 컴포넌트의 공개 필드:

| 필드 | 기본 | 의미 |
|---|---:|---|
| WindowSeconds | 4 | 중간 인식에 보내는 최근 음성 구간의 최대 길이(초) |
| StepSeconds | 0.5 | 중간 요청 간 최소 간격. 이전 요청이 끝나야 다음 요청 시작 |
| MinimumWindowSeconds | 1 | 첫 중간 인식을 시작할 최소 수집 길이 |
| SilenceSeconds | 0.65 | 음성 뒤 이 길이만큼 무음이면 발화 종료 |
| PreRollSeconds | 0.2 | 첫 음절 보존용 선행 구간 |
| MaxPendingUtterances | 3 | 최종 처리 대기 발화 개수 제한 |

`Cpc Voice Input`의 `SilenceRmsThreshold`와 `MinimumSpeechSeconds`는 양쪽 모드에서 사용합니다. RMS 기반 VAD이므로 주변 소음을 말소리와 완벽히 구별하지 못합니다. 소음 환경에 맞춰 임계값과 무음 시간을 조절하세요. 최대 녹음 시간 제한과 마이크 권한 처리는 유지됩니다.

동시 STT 요청은 하나로 제한하고, 밀린 중간 윈도우는 쌓지 않습니다. 최종 발화 처리가 우선입니다. 짧은 발화의 전체 음성 구간을 이미 인식한 결과가 있으면 이를 재사용합니다. 긴 발화 또는 끝부분이 빠진 결과는 발화 전체를 최종 인식하여 앞부분이 잘리는 것을 방지합니다. 따라서 긴 문장이나 느린 CPU에서는 기존 배치보다 빠르지 않을 수 있습니다. 서버를 바꾸거나 새 스트리밍 서버를 설치할 필요는 없습니다.

## 동일 조건 비교

성공적으로 CPC에 전달한 문장은 `Tools/Voice/Measurements/voice-recognition.csv`에 기록됩니다. `WriteComparisonCsv`로 끄고, `ComparisonLabel`에 모델·스레드 수·환경을 적을 수 있습니다. 빌드에서는 `Application.persistentDataPath/VoiceMeasurements`에 저장합니다. CSV에는 최종 인식 문장도 포함됩니다.

기록: 모드, 세션/발화 ID, 녹음/음성 시작·종료, 키 해제, 첫 요청/중간 결과/최종 결과/CPC 전달 시각, 발화 종료→전달 ms, 요청 수, 전송 음성 총 길이, 중간 결과 재사용/전체 재인식 여부. 시각은 `Time.realtimeSinceStartup`, 음성 경계는 20ms RMS 블록 기준 추정입니다. `-1`은 발생하지 않은 이벤트입니다. M 해제 전 전달한 문장은 key_release_s가 -1일 수 있습니다. Ollama 생성 시간은 이 지연에 포함하지 않습니다.

Play 모드에서 `Tools/CPC/Compare Voice Modes With Selected WAV (Play Mode)`로 **PCM16 / mono / 16000Hz WAV**를 선택하면 같은 파일을 실시간 속도로 두 모드에 재생합니다. 마이크 대신 파일을 주입하지만 실제 Whisper와 기존 CPC/Ollama 입력 경로를 사용합니다. `With Test WAV` 메뉴는 `ComparisonSamples/ko-hold.wav`의 한국어 합성 음성을 사용합니다. 결과는 `RealSttComparison.txt`와 CSV에 남습니다. 적과 자율 판단은 테스트 동안 비활성화하므로 종료 후 Play를 중지하세요.

모델, 언어, CPU 스레드, Ollama 부하를 같게 유지하고 여러 문장과 반복 횟수로 비교하세요. 정확도, 발화 종료→전달, 요청 수/전송 음성량을 함께 비교해야 합니다. 짧은 합성 음성 한 번의 결과는 실제 마이크 성능을 대표하지 않습니다.

## 코드와 검증

- `CpcVoiceInput`: 모드 선택, M 입력/취소, 공통 최종 전송, CSV.
- `VoiceAudio`: 기존 전체 녹음 + 실시간 20ms 청크 읽기.
- `SlidingVoiceBuffer`: RMS 발화 구분과 유한 길이 음성 버퍼.
- `SlidingWindowVoiceInput`: 중간 윈도우/최종 요청 스케줄링과 세대 번호 검증.
- `VoiceRecognitionMetrics`: 공통 측정 형식.
- `CpcSlidingVoiceChecks`: 가짜 녹음기/STT로 중간 미전송, 해제 중복, 느린 응답, 긴 발화, 취소, 무음, 채팅, 모드 전환 검사.
- `CpcVoiceReplayComparison`: 동일 WAV 실제 서버 비교.

공식 참고: [whisper.cpp stream 예제](https://github.com/ggml-org/whisper.cpp/tree/master/examples/stream), [HTTP 서버](https://github.com/ggml-org/whisper.cpp/tree/master/examples/server). 본 구현은 stream 실행 파일 대신 기존 HTTP 서버를 재사용합니다.
