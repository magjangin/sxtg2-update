# 설치와 폴더 배치

## 설치

1. 게임 폴더에 **MelonLoader 0.7.3**을 설치하고 게임을 한 번 켭니다. `MelonLoader\Il2CppAssemblies`가 생깁니다.
2. `SixtarGateMod.dll`을 `{게임}\Mods\`에 넣습니다.
3. 게임을 켜면 아래 폴더와 파일이 생깁니다.

```text
{게임}\
├── Mods\SixtarGateMod.dll
├── hwa\                      커스텀 곡
├── SaveCustomKey\config.txt  설정 (CONFIG_REFERENCE.md)
└── CustomNotes\              커스텀 노트 스킨 PNG
```

게임이 켜져 있을 때는 DLL이 잠겨서 바꿀 수 없습니다. 끄고 바꾸세요.
빼려면 `Mods\SixtarGateMod.dll`만 지우면 됩니다. 게임 파일은 건드리지 않습니다.

## hwa 폴더

곡 하나에 폴더 하나입니다. 곡 ID는 `CUSTOM_` + 폴더 이름(대문자, 공백은 `_`)입니다.

```text
hwa\
└── 하얀 그리움\
    ├── hwa2.bms          차트 (*.bms → *.bme → *.bml 순서로 첫 파일)
    ├── info.txt          곡 정보 (선택)
    ├── music.ogg         인게임 음원
    ├── demo.ogg          곡 선택 프리뷰 (선택)
    ├── video.mp4         BGA (선택)
    └── thumbnail.png     자켓 (선택)
```

### 파일을 고르는 규칙

| 용도 | 먼저 찾는 이름 | 없으면 |
| --- | --- | --- |
| 차트 | — | 폴더의 첫 `*.bms`, 그다음 `*.bme`, `*.bml` |
| 음원 | `music.ogg` / `.mp3` / `.wav` | 첫 오디오 파일 (`.ogg`, `.mp3`, `.wav` 순) |
| 프리뷰 | `demo.ogg` / `.mp3` / `.wav` | 음원과 같은 파일 |
| 영상 | `bg.mp4`, `video.mp4` | 첫 `.mp4` |
| 자켓 | `thumbnail`, `thumb`, `jacket` (`.png` `.jpg` `.jpeg`) | 첫 이미지 |
| 곡 정보 | `{차트 이름}.txt`, `trackinfo.txt`, `info.txt` | 첫 `.txt` |

파일 존재 여부는 게임을 켤 때 한 번만 확인합니다. 게임 중에 파일을 추가했다면 게임을 다시 켜세요.

### info.txt

```text
곡 제목: 하얀 그리움
아티스트: 화영왕
난이도: 3, 6, 9, 12
```

- 키는 `곡 제목`/`제목`/`title`, `아티스트`/`작곡가`/`artist`, `난이도`/`difficulty`/`level` 중 하나가 들어 있으면 됩니다.
- 난이도는 COMET, NOVA, SUPERNOVA, QUASAR 순서. 쉼표나 공백으로 구분.
- `#`, `//`로 시작하는 줄은 무시합니다.

### hwa 폴더에 곡이 하나도 없으면

CUSTOM 탭에 "Starting Point (Custom 1-Note)"가 하나 나옵니다. 노트 하나짜리 시험용 곡입니다.

## CustomNotes 폴더

[NOTE_EFFECTS_AND_SKINS.md](../02-systems/NOTE_EFFECTS_AND_SKINS.md)의 표를 보세요.
