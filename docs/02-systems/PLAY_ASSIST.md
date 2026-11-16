# 플레이 보조

코드: `Features/PlayAssist.cs`

리뉴얼 판정 파이프라인은 `SixtarGate.Play.JudgeProcessor`입니다.

## 세션 시작 감지

`JudgeProcessor` 생성자 패치는 이 빌드에서 **한 번도 호출되지 않습니다.**
그래서 `Judge()` / `OnTickEvent` / `SetPressedState` 중 처음 불린 곳에서 인스턴스 포인터가 바뀌었으면 새 플레이로 보고 초기화합니다.
- 판정 범위 `_range._timings`(ms) 읽기 → 판정바 눈금
- `add_OnNoteJudged` 구독 → 판정바 점
- config 다시 읽기, 커스텀 노트 스킨 초기화

## 판정 흐름 (원본)

```text
Judge()  (매 프레임)
  ├─ IsAutoJudge ? ProcessAutoJudge(tick)           judgeID 0, delta 0
  └─ JudgeLongTick(tick)                            누르고 있으면 0, 아니면 3(REDSTAR)
     CheckMissBreak(tick)                           머리를 놓치면 3
JudgeHeadTick(lane)  (키 입력)                      범위 안이면 0~3
  └─ 모두 OnTickEvent(JudgeResult) 를 거침
       → JudgeData.TickJudgeList에 쌓고 OnNoteJudged 방송
         → 콤보, 점수, 라이프, 결과, GearObject.PlayNoteEffect(레인 불빛)
```

## AutoPlay

`Judge()` Prefix에서 `IsAutoJudge = true`. 게임 내장 오토 경로를 그대로 씁니다.
`set_IsAutoJudge`는 44개 함수와 코드를 공유하므로 **호출만** 하고 패치하지 않습니다.

## AllPerfect

`OnTickEvent(JudgeResult)` Prefix에서 `result.judgeID = 0`. 히트, 오토, 미스(3) 모두 이 함수를 지나므로 콤보·점수·결과에 다 반영됩니다.

**레인 불빛 문제 (2026-10-30 수정):** `NoteEffectObject.Play(judge)`는 REDSTAR용 이펙트가 없으면 아무것도 안 켭니다. 그래서 원래 게임은 안 누른 롱노트 몸통 틱이나 놓친 머리에 불빛이 없습니다.
AllPerfect가 이걸 BLUESTAR로 바꾸자 BLUESTAR 불빛이 켜져, 롱노트를 안 누르고 있어도 틱마다 레인이 눌린 것처럼 깜빡였습니다.
→ `OnTickEvent` Prefix에서 원래 값이 3이었는지 기억하고(Postfix에서 해제), `GearObject.PlayNoteEffect`(RVA `0x6772E0`) Prefix에서 그 경우만 건너뜁니다. `OnNoteJudged` → 기어까지는 동기 호출이라 플래그 범위가 맞습니다.

**2026-11-16 보강:** "원래 3이었을 때만 끄기"로는 부족해서, 안 누른 레인에 기어 버튼과 레인 불빛이 켜지는 증상이 계속 보고됐습니다(Oriental 기어 스크린샷). 그래서 조건을 넓혔습니다: AllPerfect일 때 **원래 3이었거나, 그 틱의 레인(`result.tick.LaneIndex`)이 `PlayAssist.LanePressed`(=`SetPressedState` 거울)에서 눌려 있지 않으면** 불빛을 끕니다. 진짜 친 노트는 판정 순간 레인이 눌려 있습니다(`SetPressedState`가 먼저 오고, 게임이 떼기를 `DelayReleaseLane`으로 늦춤). 실기: 3곡 플레이, 증상 사라짐, 크래시·예외 0. 어느 판정이 RedStar 조건을 빠져나갔는지는 확인하지 못했습니다(추적 훅이 크래시를 냄, `AGENTS.md` 5장).

- 확인한 것: Oriental·default 기어의 `GearObject.animationConnector`는 `PressLane`/`ReleaseLane`(입력) 15개뿐이고 `NoteJudged` 애니메이션이 없음(`tools/datadump/gear_animations.py`). `PressLane` 알림은 `StartrailPatternPlayer.<SetInputMap>b__0`에서만, `NoteJudged`(EJudge) 알림은 `<RegisterIngameDataNotifier>b__0`에서만 보냄. 레인 바인딩은 키보드뿐(`LL='D', L='F', R='J', RR='K', GATE='Space | H', LT='S', RT='L'`, 키뷰어가 플레이 시작 때 한 번 기록) — PC의 가상 패드(Flydigi)는 원인이 아님.

## MaxScore

`ScoreProcessor.UpdateScore`는 `floor(fraction / notes * 1000000.0)`을 네이티브 상수로 계산합니다(Transpiler 불가).
→ Prefix에서 `OnScoreUpdate` / `OnReverseScoreUpdate` 델리게이트를 배율을 곱하는 래퍼로 바꿨다가 Postfix에서 원래대로 돌립니다.

점수 반전 옵션의 시작값: `GearObject.SubscribeObserver`(RVA `0x678FE0`)가 `ReverseScoreObserver`를 만들며 `targetNumber = 1000000f`로 시작합니다. Postfix에서 MaxScore로 바꿉니다.

## 점수 반전 카운트다운

게임의 점수 반전 값은 `1000000 − 잃은 점수`입니다(실측: 퍼펙트면 계속 1000000).
`ScoreInvertCountdown=1`이면 reverse 델리게이트를 빈 함수로 바꿔 두고, Postfix에서 원래 reverse에 `만점 − 이번 점수`를 넘깁니다. 얻은 점수만큼 줄어 곡 끝에 0이 됩니다.
(`UpdateScore`는 reverse를 score보다 먼저 부르므로 Postfix에서 처리합니다.)

## 기록 저장 차단

`PlayRecordModule.RecordPlayData / RecordCoursePlayData / RecordLicensePlayData` Prefix에서 막습니다.
조건: `BlockSave` 또는 AutoPlay, AllPerfect, MaxScore ≠ 1000000 중 하나라도 켜짐.

## 판정 오프셋 부호

`JudgeResult.deltaTime = inputTime − noteTime` (음수 = 빠름). sxtg2와 반대라 판정바는 부호를 뒤집어 +가 FAST입니다.
