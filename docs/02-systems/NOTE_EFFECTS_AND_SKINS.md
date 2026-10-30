# 노트 효과와 노트 스킨

코드: `Features/NoteEffects.cs`, `CustomNoteSkin.cs`, `RandomNoteSkin.cs`

## 노트 위치 계산 (원본)

`StartrailPatternRenderer.PositioningNote`가 매 프레임·노트마다:

```text
head = (headWeight - weight) * _scrollSpeed * _noteDistanceInterval   → NoteObject.set_HeadPosition
tail = (tailWeight - weight) * _scrollSpeed * _noteDistanceInterval   → NoteObject.set_NoteLength
headWeight <= weight + renderRange 이면 노트를 꺼냄
```

head가 판정선까지 거리라서, head와 tail에 같은 배율을 곱하면 노트 속도는 바뀌어도 판정 시각에는 정확히 0에 닿습니다.

## 속도 카오스

- `set_HeadPosition`, `set_NoteLength` Prefix에서 값에 배율을 곱합니다.
- 배율: 레인별(`PerLane=1`, 같은 레인은 추월 없음) 또는 노트별. 노트별은 풀 재사용 때문에 노트 객체가 아니라 틱(`_tickData`) 기준으로 기억합니다.
- 느린 노트가 갑자기 나타나지 않게 `renderRange`를 `1 / 최소배율`만큼 넓힙니다.

## 히든 / 페이드

- head를 renderRange 높이의 %로 바꿔(판정선 0, 출현 끝 100) 노트의 `CanvasGroup.alpha`를 정합니다.
- 롱노트는 머리 기준으로 같이 사라집니다.

## 훅을 쓸 때만 붙이는 이유

두 setter는 화면의 모든 노트에 대해 매 프레임 불립니다. IL2CPP 디트로 비용 + 래퍼 할당이 있어서,
`StartrailPatternRenderer.Register` Postfix(플레이마다)에서 속도 카오스·히든·페이드를 쓸 때만 `HarmonyInstance.Patch`, 아니면 `Unpatch`합니다.

2026-10-30 정리: 레인 배율은 노트 포인터별로 한 번만 찾고, 스크롤 배율은 프레임당 한 번(`NoteEffects.OnFrame`), alpha는 값이 바뀔 때만 씁니다.

## 커스텀 노트 스킨 (`CustomNotes\*.png`)

| 파일 | 적용 |
| --- | --- |
| `Blue.png` | LL / L / R / RR 머리 |
| `Red.png` | LT / RT 머리 |
| `Gate.png` | GATE 머리 (없으면 Blue) |
| `Hold.png`, `BlueHold.png`, `RedHold.png` | 롱노트 몸통 (`bodyImage`, `holdImage`) |
| `Tail.png`, `BlueTail.png`, `RedTail.png` | 롱노트 끝 (`tailImage`) |

훅: `StartrailPatternRenderer.GetNoteObejct(LaneIndex, Transform)` Postfix. 노트 풀이 레인별(`Dictionary<LaneIndex, Queue<NoteObject>>`)이라 lane 인자가 곧 노트 레인입니다.
`NoteObject.SetTickData`는 102개 함수와 코드를 공유해서 쓰면 안 됩니다.

## 랜덤 노트 스킨

- `IngameUIRenderer.notePackage.noteObjects`에 모든 스킨의 노트 프리팹이 `{skin}`(파랑), `{skin}_red`, `{skin}_gate`로 들어 있습니다(11종 + 기본 이펙터 3개 = 36개).
- 레인별 프리팹은 `GetNoteReferance(lane)`가 정합니다. 그 접미사(`""`/`_red`/`_gate`)와 같은 무작위 스킨 프리팹의 head/body/hold/tail `Image`(sprite, type, color, material, preserveAspect)를 복사합니다.
- `GetNoteObejct` Postfix(우선순위 Low)라 켜면 CustomNotes는 건너뜁니다.
- 스킨 목록과 레인별 접미사는 플레이당 한 번 만듭니다.

## 게임에 이미 있는 것

- **노트 흔들림**: 게임 옵션 `NoteShake` ([EVENTS.md](../04-game-reference/EVENTS.md))
- **서든**: 게임 옵션 "실링 다운"
