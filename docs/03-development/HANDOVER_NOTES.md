# 상세 기술 노트 & 조사 아카이브

`AGENTS.md`를 간결하게 유지하기 위해 분리한 상세 기술 기록입니다.

## 1. Steam Build 26455233 호환성 점검 (2026-11-07)

- `global-metadata.dat`는 2026-10-28 그대로이고 `GameAssembly.dll`과 에셋(`infinitia_assets_all_*`, banner/icon/select/session/thumbnail 번들, `Previews.bank`, `catalog.json`)만 변경됨.
- 업데이트 전 `cpp2il_out`과 새 출력의 형식·필드(오프셋 포함)·메서드 시그니처·RVA 183,159항목 차이 0, `slices/` 425개 함수 바이트 동일.
- 빌드 `26455233`에서도 `AGENTS.md`의 RVA가 그대로 유효하며 `cpp2il_out`을 공용 기준으로 사용함.

## 2. UnitySession 커넥터 누락 원인 및 해결 (2026-11-07)

- 오프라인 부팅은 BACKND 초기화를 건너뛰어 `Backend` 내부 정적 상태(필드 `0x30`)가 null → `UnitySessionModule.<WaitInitialize>d__6`에서 `BackEndChatServer.Initialize` → `Backend.GetBackndChatSettings` NRE(`Player.log`) → `UnitySessionModule.Initialize`(IConnector 등 제공자 등록)가 중단되어 `GetProvider<IConnector>()`가 null.
- 결과적으로 `get_IsConnect`(RVA `0x601820`), `Ingame.UnitySessionProcessor.InitailizeViewItem`(RVA `0x622780`), 종료 콜백 `SixtarGlobal.<>c.<GotoPlayScene>b__54_2`에서 NRE 발생.
- **근본 대응:** `BackEndChatServer.Initialize`(RVA `0x55C9D0`) Prefix로 오프라인(`BlockServerUpload`)일 때 건너뛰면 `<WaitInitialize>`가 `UnitySessionModule.Initialize`까지 정상 진행되어 제공자가 등록됨.
- **안전망:** `UnitySessionModule.get_IsConnect` Prefix(커넥터 null이면 false), `Ingame.UnitySessionProcessor.InitailizeViewItem` Prefix(커넥터 null이면 `Destroy(gameObject)`).

## 3. 스킨 구성 & 만우절 조사 (2026-10-30)

- **스킨 구성 (`sharedassets0.assets` UnityPy 분석):**
  - 기어 22개, 노트 11개 (`GearSkin`/`NoteSkin`, `MultipleOperationsOption`).
  - 이름 짝: `default`, `fkns`, `gothic`, `pianist`, `stellar`, `voyager`, `reincarnationOfKalpa`, `cosmicSymphony`. WACCA 기어 4개(`WaccaLily`/`LilyR`/`Reverse`/`S`) ↔ 노트 `wacca`.
  - `button01_*`는 숨은 노트 스킨이 아니라 기어 양옆 키 버튼 스프라이트임.
- **게임 내장 `NoteShake` 옵션 (NoteSway 미이식 이유):**
  - `NoteObject.GetPosX`/`set_HeadPosition`(RVA `0x67EC40`/`0x67F470`)이 `NoteShake` 옵션(`0 = Enable / 1 = Disable`)이 켜져 있으면 판정선 거리 기반 사인파 x 오프셋을 적용함. 날짜 제한 없음.
- **2023 만우절 이벤트("라미의 반란"):**
  - 현재 빌드에는 낙서풍 모드 셀렉트 테마가 존재하지 않음(ModeSelect는 Default/2021Xmas/CrossingDelta/FlowerAndDestiny/Touhou01/LuminousAndDarkness/Wacca/kalpa뿐). 이벤트 곡 `It's RAMIx` 번들(`itsramix_assets_all_*.bundle`)만 남아 있음.

## 4. 레거시 오프라인 훅 24개를 유지하는 이유 (2026-11-15)

- `tools/version_diff/xrefs.py` 정적 호출 분석 결과, `ManagerWebRequest`는 주로 구버전 매니저(`RhythmGame.*.Manager*`)가 부르지만 `UserAccountModule.SetFlag`는 리뉴얼 `StarlightUnlocker`·`ManagerPlay`에서도, `ItemHelper.CheckOwnedState`는 전역 `TrackData.IsOwned/IsVisible`에서도 호출됨.
- 레거시와 리뉴얼이 섞여 있어 정적으로 "안 불림"을 보장할 수 없으므로 유지함.

## 5. `[CriticalPatch]` (20개) & `[IcfShared]` (2개) 및 통합 캐시 지문 (2026-11-20)

- **통합 바이너리 지문 (`UpdateGuard.BinaryFingerprint`):** `"{buildId}|{gameAssemblySize}|{gameAssemblyStamp}|{metadataHeader}"` 형식으로 `BuildState.txt`·`LambdaMap.json`·`IcfCounts.txt`의 캐시 무효화를 통일함(`mtime` 의존 제거, 폴더 복사 시 불필요한 재스캔 방지, 크기가 같은 재빌드/메타데이터 교체 즉시 감지).
- **`[CriticalPatch]` 목록 (20개 — 누락 시 `[UpdateGuard]`가 `문제 있음`으로 보고):**
  - `Core.cs` (8개): `CategorySelectController_LoadData_Patch`, `RhythmGameUtils_GetSelectableCategoty_Patch`, `TrackSelectController_GetTracks_Patch`, `SixtarGlobal_GotoPlayScene_Patch`, `SixtarGlobal_GotoPlayScene_LoadBundle_Patch`, `SixtarGlobal_GotoMusicSelect_Patch`, `MikazukiConverter_Parse_Patch`, `SoundModuleForFMOD_Play_Patch`
  - `Features/OfflineBypass.cs` (8개): `BackEndWebRequest_InitializeModule_Patch`, `BackEndWebRequest_Exit_Patch`, `BackEndRequestService_GetTableVersions_Patch`, `BackEndUserDataService_GetUnlockData_Patch`, `BackEndUserDataService_RequestTravelModeArkkRewards_Patch`, `UpdateCheckController_Initialize_Patch`, `BackEndChatServer_Initialize_Patch`, `ClientDataProvider_InitializeModule_Patch`
  - `Features/PlayAssist.cs` (3개): `PlayRecordModule_RecordPlayData_Patch`, `PlayRecordModule_RecordCoursePlayData_Patch`, `PlayRecordModule_RecordLicensePlayData_Patch`
  - `Managers/CustomAudioManager.cs` (1개): `SoundModuleForFMOD_Stop_Patch`
- **`[IcfShared]` 목록 (2개):**
  - `Features/CustomJackets.cs`: `PlayLoading_JacketCallback_Patch`, `PlayLoading_ThumbnailCallback_Patch` (`MaxShared=2`, Outer Space 판 `DisplayClass35_0`과 `<>4__this` 오프셋 동일)

## 6. 실기 검증 대기 현황 (2026-11-25, v2.3.6)

- v2.3.6(2026-11-25): 커스텀 곡 경로 게이트·노트 위치 훅 최적화. 실기 때 부팅 로그 `[CustomCategory] 커스텀 곡 플레이 경로 패치 모두 적용됨, CUSTOM 탭 사용 가능`, 속도 카오스(노트별)·히든 켠 커스텀 곡 플레이 → 결과 화면 확인 필요. 오프라인 검증은 xUnit 95건·sigcheck 114·lambdascan 통과.
- 2026-11-18~24 변경분(v2.3.0~v2.3.5: 파일 분리, `verifiedPlay`, 시그니처 대조·오버로드/바뀐 시그니처 자동 선택, 로더 지문, 증분 재스캔, 2차 전 파일 감사)은 **Release/Debug 빌드(경고/오류 0), xUnit 94건, `tools/bodycheck` 통과, `tools/sigcheck` 114개 통과·오류 0·대체 해석 0, `lambdascan` `ALL PASS`**까지 오프라인 검증 완료 상태.
- v2.3.5부터 `VerifiedBuilds` 빌드라도 메타데이터·UnityPlayer·로더가 바뀌면 실기 플레이를 통과할 때까지 `BuildState.txt`를 갱신하지 않습니다(전에는 2번째 부팅부터 실기 없이 "검증된 빌드"로 통과).
- 다음 실기 세션에서 **커스텀 곡 1곡 플레이 → `Result` → `MusicSelect` 복귀** 및 `Latest.log`(`패치 118개 모두 적용 완료`, `verifiedPlay=` 기록) / `Player.log`(예외 0) 확인이 필요함.

## 7. 패치 인자 시그니처 대조 (2026-11-19, 11-20 보강)

- **문제:** Harmony는 Prefix/Postfix/Finalizer 인자를 대상 인자에 **이름으로만** 묶는다. 게임 업데이트로 대상 인자의 형식이 바뀌어도(예: `int` → `long`, `Il2CppSystem.Action<int>` → `Action<long>`) 패치는 그대로 붙고, 첫 호출에서 크기가 다른 값을 읽는다. 모드는 옛 인터롭으로 컴파일됐으니 컴파일 오류도 없고, `PatchPreflight`는 "사라진 멤버"만, ICF 검사는 "공유 주소"만 보므로 이 경우는 빈틈이었다.
- **해결:** `Managers/PatchSignatureCheck.cs`(순수 리플렉션). `PatchResilience.ApplyAllPatches`에서 대상 해석 직후, ICF 스캔 전에 실행한다. 판정 기준:
  - 건너뜀(`[Harmony/Signature] … 건너뜀`, 사전 검사 개수에 합산, `[CriticalPatch]`면 필수 빠짐): 대상에 없는 인자 이름 / 값·포인터 형식 불일치(같은 크기의 정수·열거형·`bool`끼리는 허용) / 위치 인자(`__0` 등) 범위 초과·형식 불일치 / 정적 대상의 `__instance` 또는 값↔참조 불일치 / void 대상의 `__result`.
  - 경고만: 참조 형식이 서로 대입 불가 / 알 수 없는 `__*` 특수 인자 오타. 인터롭 래퍼는 포인터만 감싸므로 즉시 크래시는 아니고, 실제로 문제가 될지는 패치가 그 객체로 무엇을 하느냐에 달려서 건너뛰지 않는다.
  - 무시: `__state`, `__exception`, `__originalMethod`, `__runOriginal`, `__args`, 값으로 받는 `object` 인자.
  - v2.3.5 보강: `ref object`로 값 형식을 받으면 오류, `bool`↔`byte`·`char`↔`short`는 더 이상 같은 크기 정수로 보지 않음, `___필드` 주입 인자는 대상 클래스(기반 형식 포함)에 그 필드/프로퍼티가 있고 형식이 맞는지 대조.
- **오탐 검증:** 현재 빌드 `26455233`의 실제 `Il2CppAssemblies`로 `tools/sigcheck`를 돌려 114개 통과, 오류 0, 경고 0. 나머지 4개(`PlayLoading_JacketCallback/ThumbnailCallback`, `ResultTrackInfo_Callback3/4`)는 `LambdaTranslator`가 네이티브 기계어를 봐야 대상이 정해져 오프라인에선 대상 미확인이지만, 인자가 `Il2CppObjectBase __instance` 하나라 어떤 클로저에도 통과한다.
- **`ResolveMethod` 보강:** 인자 형식 없이 이름으로 찾다 업데이트로 오버로드가 생기면 HarmonyX `AccessTools.Method`가 `AmbiguousMatchException`을 던져 `TargetMethod` 전체가 실패했다. 이제 잡아서 이름·개수 대조로 넘기고, 후보가 여럿이면 고르지 않고 `[Harmony/Resolve]` 경고. 이름·개수 대체로 찾은 경우도 경고(전에는 조용히 대체 → 형식이 바뀐 대상에 패치가 붙을 수 있었음, 지금은 `PatchSignatureCheck`가 이어서 형식을 대조).
- **실기:** 미실행. 다음 실기 부팅에서 `[Harmony/Signature]`·`[Harmony/Resolve]` 줄이 **0개**이고 `패치 118개 모두 적용 완료`인지 확인할 것(오프라인 대조와 같은 결과여야 함).

## 8. 오버로드 자동 선택 (2026-11-20)

- 게임의 HarmonyX(`MelonLoader/net6/0Harmony.dll`)에서 `AccessTools.Method(type, name)`은 같은 이름이 여럿이면 null이 아니라 `AmbiguousMatchException`을 던진다(scratchpad 콘솔로 확인). Harmony 클래스 처리기도 같은 해석을 하므로 그 패치는 통째로 실패한다.
- `ResolveDeclaredTargets`가 이 예외를 잡으면(일반 메서드, 인자 형식 미지정일 때만) `PatchSignatureCheck.PickOverload`로 패치 인자에 맞는 오버로드를 고르고, 그 패치 클래스는 `ManualTargets`에 넣어 `PatchManually`(`harmony.Patch(target, prefix, postfix, finalizer)`)로 붙인다. Transpiler는 이 모드에 없으므로 다루지 않는다.
- `TargetMethod()` 안의 `ResolveMethod`(오타 대응)는 패치 형식을 모르므로 고르지 않고 경고만 남긴다. 새 오타 대응 패치는 `ResolveMethod(type, new[] { typeof(...) }, ...)`처럼 인자 형식을 넘길 것.
- **바뀐 시그니처 고르기 (2026-11-23, v2.3.2):** 인자 형식을 지정한 대상이 업데이트로 사라지면 `PatchSignatureCheck.PickChangedSignature`가 같은 이름 메서드가 **하나뿐**이고 패치 인자와 맞을 때만 골라 `PatchManually`로 붙인다. 하나라도 대체 해석하면 `FallbackResolved`가 늘고 `업데이트 점검 결과`가 "정상" 대신 실기 확인을 요청하며, v2.3.5부터 그 실행에서는 실기 확인 기록도 남기지 않는다.
- **오타 대응 속성은 패치 밖에서도 동적으로 (v2.3.5):** 오타 속성(`CurrectionVolume` 등)을 훅만 동적으로 찾고 다른 코드에서 컴파일 타임으로 직접 읽으면, 오타가 고쳐진 업데이트에서 `PatchPreflight`가 그 코드를 부르는 패치까지 건너뛴다. 읽기도 `PatchResilience.CreateValueGetter`로 할 것.
