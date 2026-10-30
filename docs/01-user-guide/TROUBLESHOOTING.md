# 문제 해결

먼저 볼 로그 두 개:

| 로그 | 위치 | 들어 있는 것 |
| --- | --- | --- |
| MelonLoader | `{게임}\MelonLoader\Latest.log` | 모드 메시지(`[SixtarGateMod]`), 패치 적용 수 |
| Unity | `%USERPROFILE%\AppData\LocalLow\Lyrebird Studio\Sixtar Gate STARTRAIL\Player.log` | 게임 내부 예외(NRE), FMOD 오류, 서버 통신 (`Player-prev.log` = 이전 실행) |

시작할 때 `[Harmony] All patches applied successfully! (58)`이 보이면 모드는 정상 로드된 것입니다.

## 증상별

### CUSTOM 탭이 없다
- `Latest.log`에 `[CustomCategory] Successfully added 'CUSTOM'`이 있는지 확인.
- 패치 실패 줄(`Patch '...' failed`)이 있으면 게임이 업데이트되어 함수가 바뀐 것입니다.

### 곡이 목록에 안 나온다
- 폴더 안에 `.bms`/`.bme`/`.bml`이 있는지. 차트가 없는 폴더는 건너뜁니다.
- 게임 켠 뒤에 넣은 곡은 다시 켜야 보입니다.
- 로그 `[CustomTrackManager] Scan complete: Total N custom album(s)`의 숫자를 확인하세요.

### 플레이가 로딩 화면에서 멈춘다
- `Player.log`에서 `NullReferenceException`과 `LoadBank`를 찾으세요. 커스텀 곡인데 뱅크 조회를 건너뛰지 못한 경우입니다.
- `Latest.log`에 `[CustomChart] Skipping FMOD bank/SXGTBIN lookup`이 있어야 합니다.

### 노트가 이상하다 / 하나도 없다
- sxtg2 형식인지 게임 형식인지 자동 판별합니다([BMS_CONVERSION.md](../02-systems/BMS_CONVERSION.md)). 채널 16이나 값 02/03/05가 있고 19, 5x 채널이 없으면 sxtg2로 봅니다.
- `DebugLog=1`로 켜면 변환한 마커 수가 나옵니다.

### 음악이 안 나온다 / 중간에 끊긴다
- 음원 파일 이름 규칙([INSTALL_AND_LAYOUT.md](INSTALL_AND_LAYOUT.md))을 확인.
- `Latest.log`의 `[CustomAudio] createStream failed (...)`는 FMOD가 파일을 못 연 것입니다. 다른 형식(.ogg 권장)으로 바꿔 보세요.

### 영상이 안 나온다
- `Player.log`에 `WindowsMediaFoundation received empty file`이 있으면 경로 문제입니다.
- H.264 MP4가 가장 무난합니다.

### 곡이 너무 일찍 끝난다
- 곡 길이 = max(마지막 노트 + 3초, 음원 길이). 음원이 안 열렸으면 노트 기준으로 끝납니다.

### 오른쪽 목록 MAX COMBO가 `---`
- 알려진 한계입니다. 커스텀 곡은 번들 차트가 없어 게임이 계산하지 못합니다.

### 판정바/키뷰어가 안 보인다
- `EnableJudgmentBar`, `EnableKeyViewer` 확인.
- `Latest.log`에 `[Overlay] Draw error`가 있는지 확인. 처음 한 번만 찍힙니다.

### 콘솔에 노란 줄이 있다
- `[Il2CppInterop] Class::Init signatures have been exhausted`는 MelonLoader 자체 메시지라 무시해도 됩니다.
- 그 외 `[WARNING]`은 모드 문제일 수 있으니 로그를 남겨 주세요.

### 게임 실행 중에 DLL을 바꿀 수 없다
- 정상입니다. 게임을 끄고 바꾸세요 (`Device or resource busy`).
