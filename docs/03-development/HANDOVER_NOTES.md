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

## 5. `[CriticalPatch]` (16개) & `[IcfShared]` (2개) 및 통합 캐시 지문 (2026-11-19)

- **통합 바이너리 지문 (`UpdateGuard.BinaryFingerprint`):** `"{buildId}|{gameAssemblySize}|{gameAssemblyStamp}|{metadataHeader}"` 형식으로 `BuildState.txt`·`LambdaMap.json`·`IcfCounts.txt`의 캐시 무효화를 통일함(`mtime` 의존 제거, 폴더 복사 시 불필요한 재스캔 방지, 크기가 같은 재빌드/메타데이터 교체 즉시 감지).
- **`[CriticalPatch]` 목록 (16개 — 누락 시 `[UpdateGuard]`가 `문제 있음`으로 보고):**
  - `Core.cs` (8개): `CategorySelectController_LoadData_Patch`, `RhythmGameUtils_GetSelectableCategoty_Patch`, `TrackSelectController_GetTracks_Patch`, `SixtarGlobal_GotoPlayScene_Patch`, `SixtarGlobal_GotoPlayScene_LoadBundle_Patch`, `SixtarGlobal_GotoMusicSelect_Patch`, `MikazukiConverter_Parse_Patch`, `SoundModuleForFMOD_Play_Patch`
  - `Features/OfflineBypass.cs` (4개): `BackEndWebRequest_InitializeModule_Patch`, `TableLoader_LoadTable_Patch`, `ItemProvideModule_Initialize_Patch`, `BackEndChatServer_Initialize_Patch`
  - `Features/PlayAssist.cs` (3개): `PlayRecordModule_RecordPlayData_Patch`, `PlayRecordModule_RecordCoursePlayData_Patch`, `PlayRecordModule_RecordLicensePlayData_Patch`
  - `Managers/CustomAudioManager.cs` (1개): `SoundModuleForFMOD_Stop_Patch`
- **`[IcfShared]` 목록 (2개):**
  - `Features/CustomJackets.cs`: `PlayLoading_JacketCallback_Patch`, `PlayLoading_ThumbnailCallback_Patch` (`MaxShared=2`, Outer Space 판 `DisplayClass35_0`과 `<>4__this` 오프셋 동일)

## 6. 실기 검증 대기 현황 (2026-11-19)

- 2026-11-18~19 변경분(v2.3.0 파일 분리, `verifiedPlay` 항상 기록 수정, 업데이트 내성 강화·런타임 버그/누수 수정·리팩토링, 패치 시그니처 대조)은 **Release/Debug 빌드(경고/오류 0), xUnit 50건, `tools/sigcheck` 114개 통과·오류 0, `lambdascan` 68건, `icf_check.py` 120개 대상(ICF 증가 0)**까지 오프라인 검증 완료 상태.
- 다음 실기 세션에서 **커스텀 곡 1곡 플레이 → `Result` → `MusicSelect` 복귀** 및 `Latest.log`(`패치 118개 모두 적용 완료`, `verifiedPlay=` 기록) / `Player.log`(예외 0) 확인이 필요함.

## 7. 패치 인자 시그니처 대조 (2026-11-19)

- **문제:** Harmony는 Prefix/Postfix/Finalizer 인자를 대상 인자에 **이름으로만** 묶는다. 게임 업데이트로 대상 인자의 형식이 바뀌어도(예: `int` → `long`, `Il2CppSystem.Action<int>` → `Action<long>`) 패치는 그대로 붙고, 첫 호출에서 크기가 다른 값을 읽는다. 모드는 옛 인터롭으로 컴파일됐으니 컴파일 오류도 없고, `PatchPreflight`는 "사라진 멤버"만, ICF 검사는 "공유 주소"만 보므로 이 경우는 빈틈이었다.
- **해결:** `Managers/PatchSignatureCheck.cs`(순수 리플렉션). `PatchResilience.ApplyAllPatches`에서 대상 해석 직후, ICF 스캔 전에 실행한다. 판정 기준:
  - 건너뜀(`[Harmony/Signature] … 건너뜀`, 사전 검사 개수에 합산, `[CriticalPatch]`면 필수 빠짐): 대상에 없는 인자 이름 / 값 형식 불일치(같은 크기의 정수·열거형·`bool`끼리는 허용) / 정적 대상의 `__instance` / void 대상의 `__result`.
  - 경고만: 참조 형식이 서로 대입 불가. 인터롭 래퍼는 포인터만 감싸므로 즉시 크래시는 아니고, 실제로 문제가 될지는 패치가 그 객체로 무엇을 하느냐에 달려서 건너뛰지 않는다.
  - 무시: `__state`, `__exception`, `__originalMethod`, `__runOriginal`, `__args`, `___필드`, `__0` 등 특수 인자, `object`로 받는 인자.
- **오탐 검증:** 현재 빌드 `26455233`의 실제 `Il2CppAssemblies`로 `tools/sigcheck`를 돌려 114개 통과, 오류 0, 경고 0. 나머지 4개(`PlayLoading_JacketCallback/ThumbnailCallback`, `ResultTrackInfo_Callback3/4`)는 `LambdaTranslator`가 네이티브 기계어를 봐야 대상이 정해져 오프라인에선 대상 미확인이지만, 인자가 `Il2CppObjectBase __instance` 하나라 어떤 클로저에도 통과한다.
- **`ResolveMethod` 보강:** 인자 형식 없이 이름으로 찾다 업데이트로 오버로드가 생기면 HarmonyX `AccessTools.Method`가 `AmbiguousMatchException`을 던져 `TargetMethod` 전체가 실패했다. 이제 잡아서 이름·개수 대조로 넘기고, 후보가 여럿이면 고르지 않고 `[Harmony/Resolve]` 경고. 이름·개수 대체로 찾은 경우도 경고(전에는 조용히 대체 → 형식이 바뀐 대상에 패치가 붙을 수 있었음, 지금은 `PatchSignatureCheck`가 이어서 형식을 대조).
- **실기:** 미실행. 다음 실기 부팅에서 `[Harmony/Signature]`·`[Harmony/Resolve]` 줄이 **0개**이고 `패치 118개 모두 적용 완료`인지 확인할 것(오프라인 대조와 같은 결과여야 함).
