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

- 2026-11-18~19 변경분(v2.3.0 파일 분리, `verifiedPlay` 항상 기록 수정, 업데이트 내성 강화·런타임 버그/누수 수정·리팩토링)은 **Release/Debug 빌드(경고/오류 0), xUnit 40건, `lambdascan` 68건, `icf_check.py` 120개 대상(ICF 증가 0)**까지 오프라인 검증 완료 상태.
- 다음 실기 세션에서 **커스텀 곡 1곡 플레이 → `Result` → `MusicSelect` 복귀** 및 `Latest.log`(`패치 118개 모두 적용 완료`, `verifiedPlay=` 기록) / `Player.log`(예외 0) 확인이 필요함.
