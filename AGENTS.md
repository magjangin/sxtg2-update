# Sixtar Gate STARTRAIL IL2CPP Modding Handover & Technical Specification

> 상세 이력·시스템별 설명·전체 훅 표는 [`docs/`](docs/README.md), 현재 상태는 [`docs/CURRENT_STATUS.md`](docs/CURRENT_STATUS.md), 상세 조사 아카이브는 [`docs/03-development/HANDOVER_NOTES.md`](docs/03-development/HANDOVER_NOTES.md)를 참조하세요.

## 1. 대상 게임 정보 & 버전 고정
- **게임명:** Sixtar Gate: STARTRAIL (`Steam AppID: 1802720`)
- **Steam Build ID:** `26455233` (게임 내 표기 `ver 1.1.007 / Release 1 (1027)`, 2026-11-06 업데이트. 이전 기준: `26218612`)
  - `.acf` `AutoUpdateBehavior=0`으로 자동 업데이트 미차단. 업데이트 시 `tools/version_diff/check_update.ps1`로 `cpp2il_out`·`slices/` diff 확인.
- **Unity 버전:** `2022.3.50f1` (IL2CPP 메타데이터 `v31` / 31.1)
- **모드 로더:** MelonLoader 0.7.3 (.NET 6 런타임)

## 2. 런타임 백엔드 & 디컴파일/분석 경로
- **런타임 백엔드:** Unity IL2CPP (x64)
- **메타데이터 덤프:** `il2cpp_dump/` (`dump.cs`, `script.json`, `il2cpp.h`, `DummyDll/`) — `Il2CppDumper`
- **Cpp2IL DummyDll:** `cpp2il_out/` (93개 DLL, `--skip-analysis`, 빌드 `26218612`/`26455233` 공용 기준)
- **ilspycmd 스켈레톤:** `src_dummy/` (2,039개 `.cs` 파일)
- **Interop 어셈블리:** `{GameDir}\MelonLoader\Il2CppAssemblies\` (`Assembly-CSharp.dll` 등)

## 3. 설계 의도 및 우회 전략 (Rationale)
1. **REA 대용량 바이너리 타임아웃 우회 (`pe_slice`):** 70MB `GameAssembly.dll` 전체 대신 관심 클래스만 `pe_slice.bat`으로 분리 추출 후 Ghidra(`provider_id: "ghidra"`)로 분석.
2. **독립 "CUSTOM" 카테고리 격리:** `CategorySelectController.Categories` 및 `RhythmGameUtils.GetSelectableCategoty`에 `"CUSTOM"`을 추가하고 `TrackSelectController.GetTracks("CUSTOM")`에서만 `hwa\` 앨범을 반환해 공식 카테고리를 오염시키지 않음.
3. **오프라인 & 서버 종료 대비 (`Features/OfflineBypass.cs`, 66개 훅):** BACKND 초기화 실패 시 `HasInitialized = true`에 도달하지 못해 게임이 멈추는 문제와, BACKND 초기화 생략 시 `UnitySessionModule` 커넥터가 누락되어 곡 시작·종료가 멈추는 문제를 함께 차단(`BackEndChatServer.Initialize` 우회 포함 리뉴얼 42개 + 레거시 24개).
4. **전곡 & DLC 해금 (`Features/Unlock.cs`):** `ItemUnlockChecker`, `ItemProvideModule`, `TrackData.IsOwned`를 일괄 `true`로 오버라이드.
5. **업데이트 내성 (`Managers/PatchResilience.cs`, `LambdaTranslator.cs`, `IcfScanner.cs`, `PatchPreflight.cs`, `UpdateGuard.cs`):**
   - `[assembly: HarmonyDontPatchAll]` + 부팅 시 4단계 검증: ① `IcfScanner`(모든 IL2CPP 메서드 `methodPointer`를 훑어 공유 개수가 허용치를 넘으면 스킵, `[IcfShared]`), ② `PatchPreflight`(패치 메서드·호출 그래프를 사전 JIT 및 필드/타입 토큰 검사), ③ `LambdaTranslator`(`LambdaMap.json` 1단계 캐시 + `NativeLambdaScan`/`X64Decoder` 명령 단위 레지스터 추적으로 클로저·코루틴 동적 번역), ④ `UpdateGuard`(8개 지문 키 비교 + `Renewal_Play`→`Result` 예외 0건 시 `verifiedPlay` 자동 기록).
6. **성능 원칙 (IL2CPP 네이티브 호출 최소화):**
   - `new Rect`, `new Color`, `Mathf.*` 등 Unity 값 형식 헬퍼는 전부 `il2cpp_runtime_invoke` + boxing을 유발하므로 `Overlay.Rgba`/`Box` 및 `MathF`/`Math`만 사용.
   - `Overlay`는 풀링된 uGUI `Image`/`Text` 값 캐싱 및 비플레이 씬 `OnLateUpdate` 조기 반환.
   - `NoteEffects`는 기능이 켜져 있을 때만 `NoteObject.set_HeadPosition/set_NoteLength`를 동적 패치.

## 4. 핵심 후킹 타깃 요약 (전체 표: [`docs/02-systems/HOOK_TABLE.md`](docs/02-systems/HOOK_TABLE.md))
- **커스텀 카테고리/목록 (`Core.cs`):** `CategorySelectController.LoadData` (`0x605740`), `RhythmGameUtils.GetSelectableCategoty` (`0x6B72C0`), `TrackSelectController.GetTracks` (`0x617CF0`, `hwa\` 변경 감지 재스캔 지원).
- **플레이 진입 & 차트 (`Core.cs`, `Loaders/SxtgBmsConverter.cs`):** `SixtarGlobal.GotoPlayScene` (`0x7DBD20`), `<GotoPlayScene>b__12` (`0x7EB2E0`, FMOD 뱅크 조회 우회), `MikazukiConverter.Parse` (`0x7F0F20`, sxtg2 2/3자리 BMS 자동 변환).
- **곡 종료 시점 (`Core.cs`):** `FinishProcessor.Initialize` (`0x63FE10`) + `FinishProcessor.UpdatePatternTime` (`0x6401A0`, `max(album.Duration, FMOD 스트림 길이)`로 연장).
- **오디오 (`Core.cs`, `Managers/CustomAudioManager.cs`):** `SoundModuleForFMOD.Play` (`0x7E11E0`) → FMOD Core `createStream` 직접 재생 및 `Stop`/`Pause`/`Resume`/`GetTimelinePosition`/`set_CurrectionVolume`/`ChangeVolume` 연동.
- **BGA & 자켓 (`Features/CustomBga.cs`, `Features/CustomJackets.cs`):** `BackgroundUIProcessor` URL 재적용·`PlayBGA`/`PauseBGA`/`IsVideoPlayable` 대체; `TrackInfoViewer.Set`, `TrackObject.SetTrack`/`<LoadThumbnail>b__1`, `PlayLoadingManager`, `Pause.TrackInfoViewerModule`, `Result.TrackInfoModule`/`TravelBackgroundModule` 비동기/지연 자켓 적용.
- **플레이 보조 & 오버레이 (`Features/PlayAssist.cs`, `Overlay.cs`, `JudgmentBar.cs`, `KeyViewer.cs`, `NoteEffects.cs`, `CustomNoteSkin.cs`, `RandomNoteSkin.cs`, `CustomMarathon.cs`):** `JudgeProcessor.Judge`/`OnTickEvent`/`SetPressedState`, `ScoreProcessor.UpdateScore`, `PlayRecordModule.Record*`, `StartrailPatternRenderer.GetNoteObejct`/`Register`, `AttendanceController` 팝업 생략.
- **메타데이터 & 설정 (`Loaders/TrackInfoParser.cs`, `TextDecoder.cs`, `Managers/ModConfig.cs`):** `{게임}\hwa\` 스캔(`info.txt` → BMS `#TITLE`/`#ARTIST` → 폴더명), `{게임}\SaveCustomKey\config.txt` 동적 리로드.

## 5. 시도했다가 실패한 것들 (Dead Ends / Failed Attempts)
- **IL2CPP 동일 코드 접기(ICF) 미확인 패치:** 공유 주소 패치 시 엉뚱한 `this`로 실행됨 (`OpenPopup` 5192개, `SetTickData` 102개, `set_IsAutoJudge` 44개, `set_TrackEndTiming` 11개, `_Set_b__3_1` 11개 등). 반드시 `IcfScanner`/`script.json` 확인.
- **`SoundModuleForFMOD.PlayLocalSound` 사용:** 타임라인 약 3분 고정으로 긴 곡이 중간에 끊김 → FMOD Core `createStream` 사용.
- **`JudgeProcessor` 생성자 패치:** 빌드에서 호출되지 않음 → 인스턴스 메서드에서 지연 초기화.
- **IMGUI (`OnGUI`) 오버레이:** `Method unstripping failed` 발생 → uGUI 풀링 캔버스 사용.
- **자켓 PNG 동기 디코딩 (`ImageConversion.LoadImage`):** 1080p PNG당 50~100ms 스터터링 → 작업 스레드 `PngDecoder` + `TextureFromRgba`.
- **매 프레임/매 노트 훅 상시 부착:** `NoteObject.set_HeadPosition/set_NoteLength`는 필요할 때만 동적 부착/해제.
- **`AccessTools.TypeByName` 사용:** `LightProbesQueryDisposeJob`에서 `ReflectionTypeLoadException` 경고 다발 → `typeof(...)` 직접 지정.
- **`TrackObject.LoadThumbnail` 직접 패치:** `SetTrack`에 인라인되어 호출되지 않음 → `SetTrack` + `<LoadThumbnail>b__1` 사용.
- **`HarmonyInstance.PatchAll` + MelonLoader 자동 패치 중복:** 모든 훅 2회 실행 → `[assembly: HarmonyDontPatchAll]` 필수.
- **BGA URL 상대 경로:** `"file://" + 상대경로`는 `*_Data/` 기준으로 탐색 실패 → `Path.GetFullPath` 절대 경로화.
- **오프라인 우회 후 부팅만 확인:** `OfflineBypass`가 `UnitySession` 커넥터를 막아 플레이가 깨졌음 → 서버 훅 수정 후 반드시 커스텀 곡 1곡 결과 화면까지 플레이 + `Player.log` 예외 0 확인.
- **기계어 바이트 단위 스캔 & `dump.cs` 오프셋 신뢰:** `dump.cs` 오프셋(`trackThumbnail` 0x48 vs 실제 0x40)이 틀릴 수 있고 바이트 스캔은 거짓 Hit 발생 → `il2cpp_field_get_offset` + `X64Decoder` 명령 단위 추적.
- **판정/입력 경로 매 노트 신규 진단 훅 (`ISixtarJudgeData` 인자 등):** 네이티브 크래시 유발(원인 미확인) → 덤프 분석 없이 매 노트 훅 실험 금지.
- **기타:** `pe_slice` 네임스페이스 필수(`SixtarGate.BMSConverter`), `Cpp2IL`은 `< NUL` 리디렉션 필수, `AllTracks` 직접 주입 금지, 게임 실행 중 `Mods\SixtarGateMod.dll` 잠금.

## 6. 빌드, 테스트 및 배포 경로
- **프로젝트:** `mod/SixtarGateMod.csproj` (`<GameDir>`은 환경변수 `SIXTAR_GAME_DIR` 또는 `mod/Directory.Build.props.user`로 오버라이드, 기본 `H:\steam\steamapps\common\Sixtar Gate STARTRAIL`)
- **빌드 & 자동 배포:**
  ```powershell
  dotnet build mod/SixtarGateMod.csproj
  ```
  - 빌드 후 `$(GameDir)\Mods\`가 있으면 `SixtarGateMod.dll`을 자동 복사함(게임 실행 중 잠겨 있으면 경고만 출력). 복사 생략 시 `-p:SkipDeploy=true`.
- **순수 모듈 단위 테스트 (게임 DLL 불필요, GitHub Actions CI 연동):**
  ```powershell
  dotnet test tests/SixtarGateMod.Tests/SixtarGateMod.Tests.csproj
  ```
- **기계어 대조 오프라인 시험 (`GameAssembly.dll` 필요):**
  ```powershell
  dotnet run --project tools/lambdascan -- "<게임>\GameAssembly.dll" [caplens.txt]
  ```
- **로그:** `{GameDir}\MelonLoader\Latest.log` (정상 기동: `[Harmony] 패치 118개 모두 적용 완료`), Unity 예외는 `%USERPROFILE%\AppData\LocalLow\Lyrebird Studio\Sixtar Gate STARTRAIL\Player.log`.
