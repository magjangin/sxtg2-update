# 이벤트 흔적과 게임 내장 옵션

## 만우절

이 빌드에 남은 만우절 흔적은 아래뿐입니다. 문자열 리터럴, `dump.cs`, Addressables 카탈로그, 일반 데이터 파일을 모두 찾아봤습니다.

| 흔적 | 내용 |
| --- | --- |
| Steam 업적 | `APRIL_FOOLS_START`, `APRIL_FOOLS_COMPLETE`. `TitleManager.Start`와 `PlayManager.<Start>d__10`에서 날짜가 4/1이면 줌 |
| 노트 흔들림 옵션 | `NoteShake` (아래) |
| 프로필 아이콘 | `aprilfools2022_icon` |

- 옵션을 날짜로 숨기는 코드는 없습니다(`DateTime.get_Month` 호출부 전수 확인).
- 4/1로 날짜를 속여도 업적만 풀립니다. Steam 서버로 가는 것이니 하지 마세요.
- 결과 화면이 바뀌는 곡은 만우절 곡이 아니라 `zantetsuken`과 `lustroustravelremix`(2022 크리스마스)입니다([RESULT_ANIMATIONS.md](RESULT_ANIMATIONS.md)).

### 2023 만우절 "라미의 반란" (EA 0.2.039)

당시에는 모드 선택 화면이 낙서풍으로 바뀌고 라미를 누르면 이벤트 곡 `It's RAMIx`로 들어갔습니다.
지금 빌드에는 그 낙서 테마가 없습니다(모드 선택 씬 8개뿐). 곡은 남아 있습니다(`itsramix_assets_all_*.bundle`, 뱅크 `itsramix.bytes`, SOLAR 04/07/11/14, LUNAR 03/06/11/14, 프리뷰).

## 노트 흔들림 (`NoteShake`)

게임에 내장된 옵션입니다. 모드는 sxtg2의 `NoteSway`를 이식하지 않고 이걸 쓰라고 안내합니다.

```text
NoteObject.GetPosX / set_HeadPosition (RVA 0x67EC40 / 0x67F470)
  옵션 "NoteShake" 값이 "Disable"이 아니면
  x 오프셋 = sin(head / 1000 * freq) * amp * sign
  노트마다 처음 한 번: freq = Random(2.22, 6.66), amp = Random(20, 44), sign = ±1
  head가 판정선까지 거리라서 판정선에서 0으로 모임
```

- 옵션 정의(`sharedassets0.assets`): `NoteShake`, OnOff, 0 = Enable / 1 = Disable, 곡 시작 전 옵션 썸네일 `StandbyThumb_NoteShake`
- 저장 위치: `%USERPROFILE%\AppData\LocalLow\Lyrebird Studio\Sixtar Gate STARTRAIL\options` (`NoteShake=1`이 꺼짐)

## 계절 테마

모드 선택 테마(2021 Xmas 등)는 장착 아이템입니다. 날짜에 따라 바뀌지 않습니다([MODE_SELECT.md](MODE_SELECT.md)).

## 출석 보상

매달 1일 오전 0시(KST/JST)에 초기화되는 출석표입니다. ARKK를 줍니다. 모드는 창 자동 표시만 끌 수 있습니다([OFFLINE_AND_UNLOCK.md](../02-systems/OFFLINE_AND_UNLOCK.md)).
