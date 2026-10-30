# IL2CPP 함정 목록

코드를 고치기 전에 읽으세요. 전부 이 프로젝트에서 실제로 밟은 것입니다.

## 1. 동일 코드 접기 (ICF)

내용이 같은 함수들이 네이티브 주소 하나를 공유합니다. 그 주소를 패치하면 **모든 공유 함수**에서 Postfix가 돌고, 엉뚱한 클래스의 `this`로 필드를 읽어 크래시나 오작동이 납니다.

확인법:

```python
import json, collections
d = json.load(open('il2cpp_dump/script.json', encoding='utf-8'))
c = collections.Counter(m['Address'] for m in d['ScriptMethod'])
print(c[0x6772E0])   # 1이면 안전
```

실제 사례: `NoteObject.SetTickData`(102개), `JudgeProcessor.set_IsAutoJudge`(44개), `FinishProcessor.set_TrackEndTiming`(11개), `TrackInfoViewerModule._Set_b__3_1`(11개), `BackgroundUIProcessor.<Initialize>b__1`, `NoteJudgedObserver.Notify`(입력 이벤트와 공유).
작은 getter/setter, 빈 함수, 람다는 거의 다 공유된다고 보면 됩니다. 목록은 [HOOK_TABLE.md](../02-systems/HOOK_TABLE.md).

## 2. Unity 값 형식 헬퍼는 네이티브 호출

인터롭 빌드에서 `new Rect(...)`, `rect.x`, `rect.yMax`, `Rect ==`, `Mathf.Clamp01`, `Mathf.Lerp`, `new Color(r,g,b,a)`, `Color.white`는 전부 `IL2CPP.il2cpp_runtime_invoke` + 결과 박싱입니다(`ilspycmd -t UnityEngine.Rect`로 확인).
매 프레임 코드에서 쓰면 IL2CPP 힙에 쓰레기가 쌓입니다.

| 쓰지 말 것 | 대신 |
| --- | --- |
| `new Rect(x, y, w, h)`, `r.x` | `Box(X, Y, W, H)` (Overlay.cs) |
| `new Color(r, g, b, a)`, `Color.white` | `Overlay.Rgba(...)` = `new Color { r = … }` |
| `new Vector2(x, y)` | `new Vector2 { x = …, y = … }` |
| `Mathf.*` | `MathF.*`, `Math.*` |
| `unityObj == null` (반복문 안) | 관리 코드 쪽 캐시 |

`Color`, `Vector2`의 `r g b a`, `x y`는 public 필드라 직접 접근은 무료입니다.

## 3. 패치해도 안 불리는 함수

- `JudgeProcessor` 생성자: 적용 오류는 없는데 한 번도 안 불림 → 인스턴스 메서드에서 포인터가 바뀌면 지연 초기화
- `TrackObject.LoadThumbnail`: `SetTrack`에 인라인됨 → `SetTrack`과 비동기 콜백을 패치

## 4. 빌드에서 빠진 Unity 기능

- IMGUI 그리기(`GUI.DrawTexture`, `GUI.Label`) → `Method unstripping failed`. uGUI 캔버스로
- `DownloadHandlerTexture` 생성자 → "Method not found". 자체 PNG 디코더로

## 5. Harmony 이중 적용

`HarmonyInstance.PatchAll`을 직접 부르면서 MelonLoader 자동 패치도 켜 두면 모든 훅이 두 번 돕니다.
`return false` Prefix가 두 번 돌아 씬 이동이 두 번 일어나 로딩이 멈췄습니다. → `[assembly: HarmonyDontPatchAll]`.

## 6. `AccessTools.TypeByName`

모든 어셈블리를 훑다가 `UnityEngine.CoreModule`의 손상된 형식(`LightProbesQueryDisposeJob`)에서 예외가 나 노란 `[WARNING]` 블록이 찍힙니다. → `typeof(...)`.

## 7. Transpiler 불가

네이티브 코드라 IL이 없습니다. 함수 안에 박힌 상수(예: `UpdateScore`의 1000000.0)는 Prefix/Postfix로 입력·출력·델리게이트를 바꿔 우회합니다.

## 8. 비동기 콜백이 나중에 덮어씀

번들에서 스프라이트·VideoClip을 비동기로 읽어 콜백에서 대입하는 곳이 많습니다. `Initialize` Postfix에서 바꿔도 콜백이 나중에 null을 넣습니다.
→ 콜백 자체를 Postfix(ICF 확인 후), 안 되면 0.1/0.4/1.0초 재적용 코루틴.

## 9. 문서를 믿고 코드를 지우지 말 것

2026-10-30에 "목록 행에 자켓 칸 없음"이라는 옛 기록을 믿고 훅을 지웠다가 목록 자켓이 사라졌습니다. 같은 날 "NotePackage에 button01 있음"이라는 기록도 틀린 것으로 확인됐습니다.
지우거나 "있다/없다"를 말하기 전에 게임 데이터나 실기 화면으로 확인하세요.

## 10. 상대 경로

`AppDomain.BaseDirectory` 기준 상대 경로는 VideoPlayer가 `*_Data\` 아래에서 찾습니다. 미디어 경로는 `Path.GetFullPath`로 절대경로화.
