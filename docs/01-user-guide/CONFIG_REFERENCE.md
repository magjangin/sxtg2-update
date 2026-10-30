# 설정 파일 (`SaveCustomKey\config.txt`)

- sxtg2와 같은 `키=값` 형식입니다. `#` 또는 `//`로 시작하는 줄은 주석입니다.
- 저장하면 **다음 곡부터** 적용됩니다(플레이 씬에 들어갈 때 파일 수정 시각을 보고 다시 읽음). 게임을 다시 켤 필요 없습니다.
  - `SkipAttendancePopup`은 모드 선택 화면에 들어갈 때 읽습니다.
- 켜기/끄기 값: `1/0`, `true/false`, `on/off`, `yes/no`, `켜짐/꺼짐`, `사용/미사용`, `활성화/비활성화` 등.
- 파일 형식 버전은 `ConfigVersion`입니다(현재 6). 옛 버전 파일을 넣으면 값은 살린 채 새 템플릿으로 다시 쓰고, 원래 파일은 `config.txt.bak`으로 남깁니다.

## [플레이]

| 키 | 기본 | 설명 |
| --- | --- | --- |
| `AutoPlay` | 0 | 게임 내장 오토 판정(`IsAutoJudge`)을 켬 |
| `AllPerfect` | 0 | 모든 판정 틱을 BLUESTAR로. 원래 미스였던 틱은 레인 불빛 없이 |
| `BlockSave` | 1 | 로컬 기록 저장 차단. AutoPlay / AllPerfect / MaxScore 변경 중에는 이 값과 무관하게 항상 차단 |
| `MaxScore` | 1000000 | 만점 기준값. 점수를 이 비율로 늘이거나 줄임 |
| `ScoreInvertCountdown` | 1 | 게임 옵션 "점수 반전"을 켰을 때: 1 = 얻은 점수만큼 줄어 곡 끝에 0, 0 = 게임 원래 방식(잃은 점수만 빠짐) |

## [판정바]

| 키 | 기본 | 설명 |
| --- | --- | --- |
| `EnableJudgmentBar` | 1 | 판정바 표시 |
| `JudgmentBarVertical` | 1 | 1 = 세로, 0 = 가로 |
| `JudgmentBarShape` | 0 | 0 사각, 1 알약, 2 다이아몬드 |
| `JudgmentBarRangeShape` | -1 | 안쪽 판정 범위 상자 모양. -1 = 판정바 모양 따라감 |
| `JudgmentBarSide` | Center | `Left` / `Right` / `Center` (세로는 왼쪽, 가로는 정중앙이 기본) |

## [키뷰어]

| 키 | 기본 | 설명 |
| --- | --- | --- |
| `EnableKeyViewer` | 1 | 키뷰어 표시 |
| `KeyViewerPressedColor` | `#26BFD9D9` | 일반 키 눌렀을 때 |
| `KeyViewerNormalColor` | `#141414A6` | 안 눌렀을 때 |
| `KeyViewerGatePressedColor` | `#FF4081E6` | GATE 키 눌렀을 때 |

색 형식: `#RRGGBB`, `#RRGGBBAA`, `R,G,B,A`(0~1 또는 0~255), 색 이름(시안, 마젠타, 노랑, 빨강, 파랑, 초록, 흰색, 검정, 주황, 보라, 분홍, 하늘, 민트 / 영어 이름).

## [노트 속도 카오스]

| 키 | 기본 | 설명 |
| --- | --- | --- |
| `NoteSpeedChaos` | 0 | 노트마다 낙하 속도를 다르게 (판정 영향 없음) |
| `NoteSpeedChaosMin` | 0.6 | 최소 배율 |
| `NoteSpeedChaosMax` | 1.8 | 최대 배율 |
| `NoteSpeedChaosPerLane` | 1 | 1 = 레인마다 다름(같은 레인은 순서 유지), 0 = 노트마다 다름 |

## [커스텀 노트 스킨]

| 키 | 기본 | 설명 |
| --- | --- | --- |
| `CustomNotes` | 1 | `CustomNotes\*.png`로 노트 모양 바꾸기 |
| `RandomNoteSkin` | 0 | 노트마다 게임 내 노트 스킨을 무작위로. 켜면 CustomNotes는 안 씀 |

## [히든] / [페이드 인·아웃]

판정선 = 0%, 노트가 나오는 화면 끝 = 100%. **게임을 켤 때마다 `Hidden`, `FadeIn`, `FadeOut`은 0으로 돌아갑니다**(위치·길이는 남음).

| 키 | 기본 | 설명 |
| --- | --- | --- |
| `Hidden` | 0 | 판정선 쪽을 가릴 비율(%), 0 = 끔 |
| `LaneCoverSoftness` | 24 | 경계 흐림 폭(픽셀) |
| `FadeIn` | 0 | 내려오는 노트가 서서히 나타남 |
| `FadeInStart` / `FadeInLength` | 100 / 20 | 나타나기 시작하는 위치 / 그 길이 |
| `FadeOut` | 0 | 판정선 가까이서 서서히 사라짐 |
| `FadeOutStart` / `FadeOutLength` | 20 / 20 | 사라지기 시작하는 위치 / 그 길이 |

## [커스텀 곡 마라톤]

| 키 | 기본 | 설명 |
| --- | --- | --- |
| `CustomMarathon` | 0 | 커스텀 곡 하나를 시작하면, 결과창 Enter마다 아직 안 친 커스텀 곡이 무작위로 이어짐. Esc = 중단 |
| `CustomMarathonAutoNext` | 0 | 결과창에서 몇 초 뒤 자동으로 다음 곡 (0 = Enter만) |

## [화면]

| 키 | 기본 | 설명 |
| --- | --- | --- |
| `SkipAttendancePopup` | 1 | 모드 선택 화면의 출석 보상 창을 자동으로 안 띄움. 보상은 안 받은 채로 남음 |

## [진단]

| 키 | 기본 | 설명 |
| --- | --- | --- |
| `DebugLog` | 0 | 곡 선택·자켓·음원처럼 자주 일어나는 일도 로그에 남김 |

## 모드에서 다루지 않는 sxtg2 키

게임에 이미 있는 기능이거나 빠진 키입니다. 파일에 있어도 무시합니다.

| sxtg2 키 | 대신 쓸 것 |
| --- | --- |
| `NoteSway` | 게임 옵션 "노트 흔들림" (`NoteShake`, [EVENTS.md](../04-game-reference/EVENTS.md)) |
| `Sudden` | 게임 옵션 "실링 다운" |
| `LaneCoverDebug` | 없음 |
