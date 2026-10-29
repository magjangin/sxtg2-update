# SixtarGateMod — Sixtar Gate: STARTRAIL 커스텀 곡 & 플레이 보조 모드

**Sixtar Gate: STARTRAIL**(Steam, Unity IL2CPP)용 [MelonLoader](https://github.com/LavaGang/MelonLoader) 모드입니다.
Mono 시절 모드 **sxtg2**의 기능을 현재 IL2CPP 빌드로 옮겼습니다. 내 컴퓨터에 있는 BMS 차트, 음원, 영상, 자켓을 게임 안에서 그대로 플레이할 수 있습니다.

> 개인이 만든 팬 모드이며 Lyrebird Studio와는 관계가 없습니다. 게임 파일, 덤프, 디컴파일한 코드는 이 저장소에 들어 있지 않습니다.

## 주요 기능

**커스텀 곡**
- 곡 선택 화면에 **CUSTOM** 탭이 따로 생깁니다. 공식 카테고리(ALL TRACKS 등)에는 커스텀 곡이 섞이지 않습니다.
- `hwa\` 폴더의 곡을 자동으로 읽습니다. 차트, 음원(프리뷰 포함), 배경 영상(BGA), 자켓을 쓸 수 있습니다.
- sxtg2 형식 BMS(레인 채널 16/11/12/13/14/15/18, 3자리 `#WAV001` 차트 포함)를 게임 형식으로 자동 변환합니다.
- 곡 길이는 마지막 노트와 음원 길이 중 긴 쪽을 따릅니다. 노트가 끝나도 음악이 끝날 때까지 플레이가 이어집니다.
- 로딩, 일시정지, 결과 화면에도 커스텀 자켓이 나옵니다.
- **마라톤**: 커스텀 곡 하나를 시작하면 나머지 커스텀 곡이 무작위 순서로 이어서 나옵니다.

**플레이 보조** (`SaveCustomKey\config.txt`)
- 오토 플레이, 올 퍼펙트, 점수 상한(MaxScore) 변경, 점수 반전 카운트다운
- 실시간 판정바(가로/세로, 모양 선택)와 키뷰어
- 노트 속도 카오스, 히든, 페이드 인/아웃
- 커스텀 노트 스킨(PNG), 랜덤 노트 스킨(게임 안의 스킨을 노트마다 무작위로)

**기록 보호**
- 오토 플레이, 올 퍼펙트, 점수 상한 변경을 켜면 로컬 기록 저장이 항상 막힙니다(`BlockSave`).
- 점수는 랭킹 서버로 보내지 않습니다. 서버 점검 팝업 때문에 게임이 꺼지지도 않습니다.

## 요구 사항

| 항목 | 버전 |
| --- | --- |
| 게임 | Sixtar Gate: STARTRAIL (Steam AppID `1802720`, Build ID `26218612`에서 확인) |
| 모드 로더 | MelonLoader **0.7.3** (.NET 6) |
| 빌드할 때 | .NET SDK 6.0 이상 |

게임이 업데이트되면 함수 주소와 구조가 바뀌어 모드가 동작하지 않을 수 있습니다.

## 설치

1. 게임 폴더에 MelonLoader 0.7.3을 설치하고 게임을 한 번 실행합니다. `MelonLoader\Il2CppAssemblies`가 생깁니다.
2. `SixtarGateMod.dll`을 `{게임 폴더}\Mods\`에 넣습니다. 직접 빌드하는 방법은 아래에 있습니다.
3. 게임을 실행하면 다음 폴더와 파일이 자동으로 생깁니다.
   - `{게임 폴더}\hwa\` — 커스텀 곡 폴더
   - `{게임 폴더}\SaveCustomKey\config.txt` — 설정 파일
   - `{게임 폴더}\CustomNotes\` — 커스텀 노트 스킨 폴더

> 게임이 실행 중일 때는 `Mods\SixtarGateMod.dll`이 잠겨 있어서 덮어쓸 수 없습니다. 게임을 끄고 바꾸세요.

## 커스텀 곡 넣기

곡 하나당 `hwa\` 아래에 폴더를 하나 만듭니다. 폴더 이름이 곡 ID가 됩니다.

```
{게임 폴더}\hwa\
└── 하얀 그리움\
    ├── chart.bms         ← 차트 (.bms / .bme / .bml, 폴더의 첫 번째 파일)
    ├── info.txt          ← 곡 정보 (선택)
    ├── music.ogg         ← 인게임 음원 (.ogg / .mp3 / .wav)
    ├── demo.ogg          ← 곡 선택 프리뷰 (선택, 없으면 music 사용)
    ├── video.mp4         ← 배경 영상 (bg.mp4 도 됨, 선택)
    └── thumbnail.png     ← 자켓 (thumb / jacket .png .jpg 도 됨, 선택)
```

파일 이름이 정해진 이름과 다르면 폴더 안의 첫 번째 같은 종류 파일을 씁니다.

`info.txt`(또는 `trackinfo.txt`, `{차트 이름}.txt`) 예시:

```
곡 제목: 하얀 그리움
아티스트: 누군가
난이도: 3, 6, 9, 12
```

`title` / `artist` / `level` 같은 영어 키도 됩니다. 난이도는 COMET, NOVA, SUPERNOVA, QUASAR 순서입니다.

## 설정 (`SaveCustomKey\config.txt`)

파일을 저장하면 **다음 곡부터** 적용됩니다. 게임을 다시 켤 필요는 없습니다. 켜기/끄기 값은 `1/0`, `true/false`, `켜짐/꺼짐`, `on/off`를 모두 받습니다. 파일 안에 항목마다 설명이 있습니다.

| 키 | 기본값 | 설명 |
| --- | --- | --- |
| `AutoPlay` | 0 | 오토 플레이 |
| `AllPerfect` | 0 | 모든 판정을 BLUESTAR로 |
| `BlockSave` | 1 | 로컬 기록 저장 차단 (보조 기능을 켜면 항상 차단) |
| `MaxScore` | 1000000 | 만점 기준값 |
| `ScoreInvertCountdown` | 1 | 게임의 "점수 반전"을 켰을 때 얻은 점수만큼 줄어 0으로 끝남 |
| `EnableJudgmentBar` | 1 | 판정바 (`JudgmentBarVertical`, `JudgmentBarShape`, `JudgmentBarSide` …) |
| `EnableKeyViewer` | 1 | 키뷰어 (`KeyViewerPressedColor` 등, `#RRGGBB` 또는 색 이름) |
| `CustomNotes` / `RandomNoteSkin` | 1 / 0 | 커스텀 노트 스킨 / 랜덤 노트 스킨 |
| `NoteSpeedChaos` | 0 | 노트마다 다른 낙하 속도 (`…Min`, `…Max`, `…PerLane`) |
| `Hidden`, `FadeIn`, `FadeOut` | 0 | 레인 가리기. 게임을 켤 때마다 0으로 돌아갑니다 |
| `CustomMarathon` | 0 | 커스텀 곡 마라톤 (`CustomMarathonAutoNext` 초 뒤 자동 진행) |
| `DebugLog` | 0 | 자세한 로그 |

노트 흔들림과 서든은 게임 옵션("노트 흔들림 설정", "실링 다운")에 이미 있어서 모드에서는 다루지 않습니다.

## 커스텀 노트 스킨 (`CustomNotes\`)

| 파일 | 적용 위치 |
| --- | --- |
| `Blue.png` | LL / L / R / RR 노트 머리 |
| `Red.png` | LT / RT 노트 머리 |
| `Gate.png` | GATE 노트 머리 (없으면 Blue) |
| `Hold.png`, `BlueHold.png`, `RedHold.png` | 롱노트 몸통 |
| `Tail.png`, `BlueTail.png`, `RedTail.png` | 롱노트 끝 |

## 직접 빌드하기

`mod\SixtarGateMod.csproj`는 게임 폴더의 MelonLoader DLL을 참조합니다(`HintPath`). 게임 경로가 다르면 csproj의 경로를 바꾸세요.

```powershell
dotnet build mod\SixtarGateMod.csproj
Copy-Item mod\bin\Debug\net6.0\SixtarGateMod.dll "{게임 폴더}\Mods\" -Force
```

로그는 `{게임 폴더}\MelonLoader\Latest.log`에 남습니다. 시작할 때 `[Harmony] All patches applied successfully!`가 보이면 정상입니다.

## 프로젝트 구조

```
mod/
├── Core.cs                 진입점, CUSTOM 탭, 차트/미디어 주입, 오프라인 가드
├── Features/               플레이 보조 (PlayAssist), 판정바, 키뷰어, 오버레이, 노트 효과, 스킨, 마라톤
├── Managers/               커스텀 곡 스캔, 오디오(FMOD Core 스트림), 자켓/영상, 설정 파일
├── Loaders/                BMS 길이/BPM 읽기, sxtg2 → 게임 BMS 변환, PNG 디코더, 곡 정보
└── Models/
AGENTS.md                   기술 인수인계 문서 (훅 목록, 설계 이유, 실패한 시도)
docs/hooks.md               후킹 대상 표
```

훅 주소와 우회 이유, IL2CPP에서 조심할 점(동일 코드 접기, 값 형식 호출 비용 등)은 [AGENTS.md](AGENTS.md)에 정리되어 있습니다.

## 주의

- 게임 파일을 바꾸지 않고 실행 중에만 동작합니다. `Mods\SixtarGateMod.dll`을 지우면 원래대로 돌아갑니다.
- 보조 기능으로 낸 점수는 저장되거나 서버로 전송되지 않게 막혀 있습니다. 이 동작을 바꿔 랭킹에 올리지 마세요.
- 커스텀 곡의 음원, 영상, 이미지 저작권은 각 권리자에게 있습니다. 직접 권리가 있는 파일만 쓰세요.
