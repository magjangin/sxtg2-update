# 문제 해결

먼저 볼 로그 두 개:

| 로그 | 위치 | 들어 있는 것 |
| --- | --- | --- |
| MelonLoader | `{게임}\MelonLoader\Latest.log` | 모드 메시지(`[SixtarGateMod]`), 패치 적용 수 |
| Unity | `%USERPROFILE%\AppData\LocalLow\Lyrebird Studio\Sixtar Gate STARTRAIL\Player.log` | 게임 내부 예외(NRE), FMOD 오류, 서버 통신 (`Player-prev.log` = 이전 실행) |

시작할 때 `[Harmony] 패치 N개 모두 적용 완료`가 보이면 모드는 정상 로드된 것입니다.

## 증상별

### CUSTOM 탭이 없다
- `Latest.log`에 `[CustomCategory] 카테고리 목록에 'CUSTOM' 탭 추가 완료`가 있는지 확인.
- 패치 실패 줄(`[Harmony] 패치 '...' 실패`)이 있으면 게임이 업데이트되어 함수가 바뀐 것입니다. 시작 로그의 `[UpdateGuard] 검증되지 않은 게임 빌드`도 함께 보세요.
- `[Harmony/ICFGuard] '…' 건너뜀: 네이티브 주소 0x…를 메서드 N개가 공유함`은 게임 업데이트로 그 함수가 다른 함수와 같은 코드로 합쳐졌다는 뜻입니다. 모드가 엉뚱한 곳에서 실행되지 않게 일부러 뺀 것이라, 그 기능만 동작하지 않습니다.
- `[Harmony] 필수 패치 빠짐: … — (결과)`가 있으면 그 결과(오프라인 부팅 정지, 곡 시작 안 됨 등)가 실제로 일어납니다. 모드를 새 빌드에 맞게 고쳐야 합니다.
- `[UpdateGuard] 매니페스트는 빌드 …인데 GameAssembly 크기가 기록과 다름`은 `.acf`를 고쳐 업데이트를 막았거나 게임 파일이 바뀐 경우입니다.

### 곡이 목록에 안 나온다
- 폴더 안에 `.bms`/`.bme`/`.bml`이 있는지. 차트가 없는 폴더는 건너뜁니다.
- 게임 켠 뒤에 넣은 곡은 다시 켜야 보입니다.
- 로그 `[CustomTrackManager] 검색 완료: 커스텀 곡 N곡 발견`의 숫자를 확인하세요.

### 곡 제목·아티스트·난이도가 이상하다 (`test`, 폴더 이름, 깨진 글자)
- 제목이 `test`: 곡 정보 파일에서 제목을 못 읽어 sxtg2 차트의 `#TITLE test`가 쓰인 것입니다. 키 이름과 `키: 값` 형식을 확인하세요.
- 글자가 깨지면 파일을 UTF-8로 다시 저장하세요. 다른 나라 PC에서 "ANSI"로 저장한 파일(예: 한국어 PC에서 GBK 파일)은 자동 판별이 틀릴 수 있습니다.
- 난이도가 틀 곡 값으로 나오면 숫자 사이 구분자를 확인하세요(`,` `，` `、` `/` 공백만 인식).
- 같은 항목이 여러 번 있으면 처음 값을 씁니다. 상세는 [CUSTOM_TRACKS.md](../02-systems/CUSTOM_TRACKS.md).
- 폴더 이름이 `A B`와 `A_B`처럼 곡 ID가 겹치면 나중 폴더 ID에 `_2`가 붙고 `Latest.log`에 경고가 남습니다.

### 플레이가 로딩 화면에서 멈춘다
- `Player.log`에서 `NullReferenceException`과 `LoadBank`를 찾으세요. 커스텀 곡인데 뱅크 조회를 건너뛰지 못한 경우입니다.
- `Latest.log`에 `[CustomChart] '…'의 FMOD 뱅크/SXGTBIN 조회를 건너뛰고 로컬 BMS(…자)로 Renewal_Play 진입`이 있어야 합니다.
- 오프라인에서 플레이 직후 멈추면 `Player.log`의 `UnitySessionModule`/`get_IsConnect` NRE를 보세요([OFFLINE_AND_UNLOCK.md](../02-systems/OFFLINE_AND_UNLOCK.md)).

### 노트가 이상하다 / 하나도 없다
- sxtg2 형식인지 게임 형식인지 자동 판별합니다([BMS_CONVERSION.md](../02-systems/BMS_CONVERSION.md)). 채널 16이나 값 02/03/05가 있고 19, 5x 채널이 없으면 sxtg2로 봅니다.
- `DebugLog=1`로 켜면 변환한 마커 수가 나옵니다.

### 음악이 안 나온다 / 중간에 끊긴다
- 음원 파일 이름 규칙([INSTALL_AND_LAYOUT.md](INSTALL_AND_LAYOUT.md))을 확인.
- `Latest.log`의 `[CustomAudio] createStream 실패 (...)`는 FMOD가 파일을 못 연 것입니다. 다른 형식(.ogg 권장)으로 바꿔 보세요.

### 영상이 안 나온다
- `Player.log`에 `WindowsMediaFoundation received empty file`이 있으면 경로 문제입니다.
- H.264 MP4가 가장 무난합니다.

### 곡이 너무 일찍 끝난다
- 곡 길이 = max(마지막 노트 + 3초, 음원 길이). 음원이 안 열렸으면 노트 기준으로 끝납니다.

### 오른쪽 목록 MAX COMBO가 `---`
- 알려진 한계입니다. 커스텀 곡은 번들 차트가 없어 게임이 계산하지 못합니다.

### 판정바/키뷰어가 안 보인다
- `EnableJudgmentBar`, `EnableKeyViewer` 확인.
- `Latest.log`에 `[Overlay] 그리기 오류`나 `[Overlay] 오버레이 캔버스 생성 실패`가 있는지 확인. 그리기 오류는 처음 한 번만 찍힙니다.

### 콘솔에 노란 줄이 있다
- `[Il2CppInterop] Class::Init signatures have been exhausted`는 MelonLoader 자체 메시지라 무시해도 됩니다.
- 그 외 `[WARNING]`은 모드 문제일 수 있으니 로그를 남겨 주세요.

### 게임 실행 중에 DLL을 바꿀 수 없다
- 정상입니다. 게임을 끄고 바꾸세요 (`Device or resource busy`).
