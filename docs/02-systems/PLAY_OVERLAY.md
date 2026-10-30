# 판정바와 키뷰어 오버레이

코드: `Features/Overlay.cs`, `JudgmentBar.cs`, `KeyViewer.cs`, `CustomMarathon.Draw`

## 왜 uGUI인가

이 게임 빌드에서 IMGUI 그리기(`GUI.DrawTexture`, `GUI.Label`, `GUIStyle(GUIStyle)`)가 빠져 있어 `Method unstripping failed`가 매 프레임 납니다.
그래서 `Canvas`(ScreenSpaceOverlay, sortingOrder 32000) 하나에 `Image`/`Text`를 풀링해 즉시모드처럼 씁니다.

```text
Overlay.Begin()           프레임마다 1번: 화면 크기, 캔버스 배율 읽기
Overlay.Rect(Box, Color, Sprite?)
Overlay.Label(Box, text, Color, size, anchor, outline)
Overlay.End()             이번에 안 쓴 풀 객체 숨기기, 그리기 순서 맞추기
```

좌표 `Box`는 IMGUI처럼 왼쪽 위 원점, 화면 픽셀입니다.

## 성능 원칙 (2026-10-30)

이 인터롭 빌드에서는 `new Rect(...)`, `rect.x`, `Mathf.Clamp01`, `new Color(r,g,b,a)`, `Color.white`, `Rect ==` 같은 **Unity 값 형식 헬퍼가 전부 네이티브 호출**(`il2cpp_runtime_invoke`)이고 결과를 박싱합니다. 오버레이가 이걸 프레임마다 수백 번 하고 있었습니다.

| 바꾼 것 | 방법 |
| --- | --- |
| 좌표 계산 | 관리 코드 `Box` record struct, `MathF` |
| 색 만들기 | `Overlay.Rgba(r,g,b,a)` = `new Color { r = … }` (필드 초기화는 순수 관리 코드) |
| 풀 객체 갱신 | 각 객체가 위치·색·스프라이트·글자·크기·정렬·외곽선·활성 상태를 관리 코드로 기억, 바뀔 때만 IL2CPP 호출 |
| 그리기 순서 | 전에는 요소마다 매 프레임 `SetAsLastSibling`(캔버스 재빌드). 지금은 그리는 순서가 지난 프레임과 다를 때만 |
| 판정바 모양 텍스처 | 픽셀당 `SetPixel` 대신 RGBA 배열 한 번 업로드 |
| 판정바 글자 | 판정 들어올 때 한 번 만들고 재사용 |
| 마라톤 배너 글자 | 남은 초가 바뀔 때만 다시 만듦 |

**그리기 순서가 왜 중요한가:** 풀 객체를 호출 순서대로 재사용하면 형제 순서(=렌더 순서)와 어긋나서, 나중에 생긴 반투명 키 상자가 키 글자를 덮어 글자가 회색↔흰색으로 깜빡였습니다.

## 판정바

- 판정은 `JudgeProcessor.OnNoteJudged` 구독으로 받습니다. 롱노트는 머리 틱(`TickJudgeList.Count == 1`)만 씁니다.
- 판정 범위는 세션 시작 때 `JudgeRange._timings`에서 읽습니다(기본 36/85/136/187ms).
- 점은 1.5초 동안 서서히 사라집니다. 지난 점은 앞에서부터 잘라 냅니다(시간 순으로 쌓이므로).

## 키뷰어

- 눌림 상태: `JudgeProcessor.SetPressedState(int lane, bool)` Postfix. 레인 순서 LL, L, R, RR, Gate, LT, RT.
- 표시 순서: Solar `LT LL L GATE R RR RT`, Lunar `LL L GATE R RR`.
- 키 이름: 리뉴얼 입력 모듈 `InputListenerForUnityNewInputSystem._sixtarInputAction`의 `PlayAction` 바인딩. 없으면 구버전 `InputListener`, 그것도 없으면 약 2초 재시도 후 레인 이름.
  - 재시도는 10프레임마다 (`FindObjectOfType`이 무거워서).
- 리뉴얼 플레이 씬에는 구버전 `InputListener`가 없습니다(실측).

## 숨기기

`OnSceneWasLoaded`에서 씬이 `Renewal_Play`가 아니면 플레이 세션을 끝내고, 그 프레임부터 아무것도 그리지 않습니다.
