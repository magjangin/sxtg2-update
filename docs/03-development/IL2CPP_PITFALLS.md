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

**실행 중 자동 검사 (2026-11-10, `Managers/IcfScanner.cs`):** 부팅 때 IL2CPP 런타임의 모든 메서드(이 빌드 111,731개, 약 30ms)를 훑어
훅 대상 주소의 공유 개수를 세고, 허용치(기본 1)를 넘는 패치는 건너뜁니다. 의도적으로 공유하는 대상은 패치 클래스에
`[IcfShared(n, "이유")]`를 붙입니다. 결과는 `UserData/SixtarGateMod.IcfCounts.txt`에 저장되므로 업데이트 직후 이 파일을 열면
대상별 공유 개수와 공유 메서드 이름이 보입니다. 단, 제네릭 인스턴스 코드는 세지 않으므로 새 대상을 고를 때는 위 script.json 확인도 하세요.

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

**Unity 객체 null 비교도 네이티브 호출입니다.** `GameObject`, `Image`, `Sprite`, `NoteObject` 같은 `UnityEngine.Object` 파생 형식의 `==`/`!=`는 인터롭이 만든 `op_Equality`(→ `il2cpp_runtime_invoke`)라, 파괴 여부까지 보는 대신 호출마다 비용이 듭니다. 2026-11-23 감사에서 오버레이(`Overlay.Rect`/`Label`마다 `_root == null`, 키뷰어를 켜면 프레임당 약 50회), 판정바 모양 캐시, 노트 스폰 Postfix, `RandomNoteSkin.Copy`, 곡 목록 행 훅에서 찾아 아래 규칙으로 바꿨습니다.

| 상황 | 쓰는 비교 |
| --- | --- |
| 이번 플레이/세션 동안 우리가 만들거나 받아 둔 객체(풀 노트 Image, `DontUnloadUnusedAsset` 스프라이트, Harmony가 넘긴 `__result`) | `is null` / `is not null` (관리 비교, 무료) |
| 씬 전환 등으로 파괴됐을 수 있는 객체 | 경계에서 한 번(씬 로드, N프레임마다) `== null` 후 관리 플래그로 기억 |
| 파괴된 객체에 실수로 접근 | 인터롭이 예외를 던지므로 호출부 `try/catch`(오버레이는 `Core.OnLateUpdate`가 한 번만 경고) |

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

## 11. 업데이트 내성 (`Managers/PatchResilience.cs`)

게임이 업데이트되면 아래 네 가지가 가장 먼저 깨집니다:
1. **컴파일러 생성 클로저·코루틴 순번 변경:** 메서드나 람다가 하나만 추가·삭제돼도 `__c__DisplayClass54_0`, `_WaitUpdateCheck_d__6`, `_GotoPlayScene_b__12`의 번호가 밀립니다. 컴파일 시점에 이 타입들을 직접 참조하면 업데이트 후 `TypeLoadException`이 나거나 엉뚱한 클로저가 패치됩니다. → `PatchResilience.FindClosureMethod` / `FindClosureCallback` / `FindCoroutineMoveNext`로 `(외부 타입, 부모 메서드 접두사, 파라미터 시그니처)` 기준 동적 탐색하고, 필드 접근은 패치 시점에 `Expression.Lambda`로 1회 컴파일된 델리게이트(`CreateGetter`/`CreateSetter`)를 씁니다.
2. **게임 코드 오타 수정:** `GetNoteObejct`, `GetSelectableCategoty`, `ReqeustSessionPlayingCount`, `UnlockReqeustAchievement`, `CurrectionVolume`, `GetNoteReferance` 등 원본 오타가 업데이트에서 수정될 수 있습니다. → `PatchResilience.ResolveMethod` / `ResolvePropertySetter`에 오타·정상 철자를 함께 등록해 양쪽 모두 자동 대응합니다.
3. **업데이트 후 빈 스텁화로 인한 ICF 폴딩:** 패치 대상 함수가 추후 업데이트에서 빈 함수(`ret` / `ret 0`)나 상수 반환(`xor al,al; ret` / `mov al,1; ret`)으로 바뀌면 수천 개 함수와 주소를 공유하게 됩니다. → `PatchResilience.ApplyAllPatches`가 패치 직전 `Il2CppMethodInfo::methodPointer`의 첫 1~3바이트 기계어를 검사해 공유 자명 스텁이면 자동으로 패치를 건너뛰고 경고를 남깁니다. 내용 있는 함수끼리 접힌 경우는 `IcfScanner`가 공유 개수로 잡습니다(1장).
4. **훅 대상 인자 형식 변경 (2026-11-19):** Harmony는 Prefix/Postfix 인자를 **이름으로만** 대상 인자에 묶고 형식은 보지 않습니다. 업데이트로 `int lane`이 `long lane`이 되거나 `Action<int>`가 `Action<long>`이 되면 패치는 문제없이 붙고, 첫 호출에서 크기가 다른 값을 읽어 쓰레기 값·스택 손상·네이티브 크래시가 납니다. 컴파일은 옛 인터롭으로 했으니 컴파일 오류도 없고, `PatchPreflight`(사라진 멤버)·ICF 검사로도 안 잡힙니다. → `PatchSignatureCheck`가 부팅 시 패치 인자를 대상 시그니처와 대조해 값 형식 불일치(같은 크기 정수·열거형끼리는 허용)·없는 인자·정적 대상 `__instance`·void 대상 `__result`면 건너뛰고, 참조 형식 불일치는 경고합니다. 게임을 켜기 전에는 `tools/sigcheck`로 같은 판정을 오프라인으로 봅니다. 같은 이유로 `ResolveMethod`가 정확한 시그니처로 못 찾아 이름·개수로 대체하면 `[Harmony/Resolve]` 경고를 남기고, 업데이트로 같은 이름 오버로드가 생기면(`AmbiguousMatchException`, 게임의 HarmonyX에서 실제로 던지는 것 확인) 아무거나 고르지 않습니다. 속성 기반 `[HarmonyPatch(typeof(X), "M")]`이면 패치 자신의 인자 목록을 시그니처 단서로 써서 대조를 통과하는 오버로드가 **하나일 때만** 그것을 골라 직접 붙이고(`[Harmony/Resolve] … 고름`), 여럿이 맞거나 패치가 대상 인자를 받지 않으면 건너뜁니다. **새 패치를 쓸 때 대상에 오버로드가 생길 여지가 있으면 `[HarmonyPatch(typeof(X), nameof(X.M), new[] { typeof(...) })]`로 인자 형식을 지정하세요.**
