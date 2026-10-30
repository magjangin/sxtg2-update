# 난이도

## 5단계

`SixtarGate.Play.ELevels`:

| 값 | 이름 |
| --- | --- |
| 0 | COMET |
| 1 | NOVA |
| 2 | SUPERNOVA |
| 3 | QUASAR |
| 4 | **STARLIGHT** |

- 구버전 `ELevelsOld`는 COMET~QUASAR 4개뿐입니다. STARLIGHT는 나중에 추가됐습니다.
- **STARLIGHT는 일부 곡에만 있습니다.** 곡 데이터에 `STARLIGHT_INDEX = 4`, `HasStarlight(플레이 스타일)`, `GetStarlightLevel(플레이 스타일)`이 있고 Solar/Lunar를 따로 판단합니다.
- 해금도 따로 합니다: 곡 선택 화면의 `StarlightUnlockController`(`UnlockedStarlight`), `StarlightUnlockPopup`.
- 어느 곡에 STARLIGHT가 있는지는 서버 곡 표(`Data_Track`)에 있어서 덤프만으로는 알 수 없습니다.

## Solar / Lunar

| 스타일 | `EPlayStyle` | 레인 |
| --- | --- | --- |
| SOLAR | `STANDARD` (0) | 7 (LT, LL, L, GATE, R, RR, RT) |
| LUNAR | `LIGHT` (1) | 5 (LL, L, GATE, R, RR) |

곡 데이터의 `solarLevels`, `lunarLevels`가 각각 난이도 숫자 배열입니다.

## 판정

`EJudge`: 0 BLUESTAR, 1 WHITESTAR, 2 YELLOWSTAR, 3 REDSTAR(미스). 기본 판정 범위 36 / 85 / 136 / 187ms.

## 커스텀 곡

`info.txt`의 `난이도: a, b, c, d`가 COMET~QUASAR 숫자가 되어 Solar·Lunar 양쪽에 같이 들어갑니다. 배열 길이는 틀 곡(Starting Point)을 따릅니다.
