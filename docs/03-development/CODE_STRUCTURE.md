# 코드 구조

`mod/` 36개 파일, 9,413줄 (2026-11-19 갱신). 순수 모듈 단위 테스트는 `tests/SixtarGateMod.Tests/`(8개 테스트 파일, 50건)에 있습니다. 모듈 관계는 [ARCHITECTURE.md](../00-overview/ARCHITECTURE.md), 훅 목록은 [HOOK_TABLE.md](../02-systems/HOOK_TABLE.md).

| 파일 | 줄 | 역할 |
| --- | ---: | --- |
| `Features/OfflineBypass.cs` | 1007 | 완전 오프라인·서버 종료 우회 (리뉴얼 BACKND 42개 + `sxtg2 saver test` 레거시 24개, 총 66개 훅) |
| `Managers/LambdaTranslator.cs` | 960 | 이중래퍼 람다 번역가: 통합 지문 캐시 직행(RVA 대조) + 후보 수집·거르기 + 기계어 대조로 번역, `TranslateMethodInType` |
| `Managers/PatchResilience.cs` | 746 | 업데이트 내성: 패치 적용 순서(사전 JIT → 시그니처 대조 → ICF → Harmony), 클로저·코루틴 동적 탐색, 오타 대응(오버로드·대체 해석 경고), ICF 가드(빈 스텁 + `IcfScanner` 공유 수/미스캔 경고), 캐시된 Expression 접근자·복제기 |
| `Core.cs` | 544 | 진입점. CUSTOM 탭, 플레이 진입, 차트 주입(`LoadCurrentAlbumBmsOrDefault`), 곡 끝, 프리뷰 |
| `Managers/ModConfig.cs` | 473 | 설정 파일(`AtomicFile` 저장, 공백 허용 초기화, 색상 `IsFinite`/클램프). 절반 이상이 기본 템플릿 문자열 |
| `Managers/UpdateGuard.cs` | 377 | Steam 빌드 확인, 지문 비교·`BinaryFingerprint` 노출, 패치 후 점검 결과 한 줄, Unity 예외 포워딩, 실기 확인 자동 기록 |
| `Managers/CustomTrackManager.cs` | 364 | hwa 스캔(작업 스레드, 폴더당 1회 조회, 변경 감지 재스캔 `RefreshIfChanged`), 차트 텍스트 캐시, TrackData 복제, BMS 헤더 폴백 |
| `Loaders/BmsParser.cs` | 325 | BPM, 변속(03/08)·변박(02)·중복 BPM 덮어쓰기, 곡 길이, BMS 헤더(#TITLE, #ARTIST) Span 파싱 + 레인 버킷 정렬 |
| `Features/PlayAssist.cs` | 314 | 판정 세션(확정 경계 포인터 리셋), AutoPlay, AllPerfect(+불빛 생략), 점수(`Finalizer` 복원), 기록 차단 |
| `Managers/X64Decoder.cs` | 293 | 최소 x64 명령 길이 해독기 + 레지스터 쓰기 마스크 (`REX.W`+`0x66`, VEX `map==1` imm8 포함, capstone 대조 불일치 0) |
| `Managers/MediaOverrideManager.cs` | 254 | 자켓 비동기/캐시(`LoadAsync` `try/finally` 누수 방지), RGBA 업로드, BGA URL, 실패 시 텍스처 즉시 파괴 |
| `Features/CustomJackets.cs` | 246 | 곡 선택(가운데·목록 행 stale 가드), 로딩, 일시정지, 결과 화면 자켓 오버라이드 및 지연 재적용 |
| `Loaders/SxtgBmsConverter.cs` | 240 | sxtg2 → 게임 BMS (`char[]` 슬롯, 3자리 차트 채널 `02` 보존, `Lcm` 클램프, 무할당 Span 포맷 판별) |
| `Managers/IcfScanner.cs` | 233 | 실행 중 ICF 검사: 훅 대상 주소 공유 수(미스캔 `-1`), `[IcfShared]`, `UpdateGuard.BinaryFingerprint` 기반 원자적 캐시 |
| `Features/Overlay.cs` | 232 | uGUI 즉시모드 그리기, `Box`, `Rgba`, `White`, 재생성 시 풀 초기화 |
| `Features/NoteEffects.cs` | 232 | 속도 카오스, 히든/페이드, 동적 패치, 씬 종료 시 자동 언패치 및 정리 |
| `Managers/PatchPreflight.cs` | 225 | 패치 적용 전 사전 JIT·토큰(`Method`/`Field`/`Type`/`Tok` + 어셈블리 로드 예외) 검사, 서브그래프 메모이제이션, 부팅 후 캐시 정리 |
| `Features/RandomNoteSkin.cs` | 203 | 랜덤 노트 스킨 (프리팹 값 1회 읽기, 스폰 훅은 CustomNoteSkin과 공용) |
| `Managers/CustomAudioManager.cs` | 201 | FMOD Core 스트림 재생/정지(예외 시 핸들 해제 보장), 공식 곡 전환 시 스트림 자동 해제 |
| `Features/CustomMarathon.cs` | 175 | 마라톤 (`Loading` 씬 유지, 비플레이 씬 이탈 정리, 연타 이중 진행 방지) |
| `Features/JudgmentBar.cs` | 171 | 판정바 (히스토리 수명 관리, `ShapeCache` 초과 시 GPU 텍스처/스프라이트 파괴) |
| `Features/CustomNoteSkin.cs` | 167 | PNG 노트 스킨(작업 스레드 해독, 플레이 준비 때 리셋·업로드) + 노트 스폰 훅(`GetNoteObejct`, 랜덤 스킨 분기) |
| `Features/KeyViewer.cs` | 167 | 키뷰어 |
| `Loaders/PngDecoder.cs` | 167 | 관리 코드 PNG 디코더 (청크/CRC/크기 상한/필터/컬러 타입 0·2 `tRNS` 검증) |
| `Managers/PatchSignatureCheck.cs` | 148 | 패치 인자 ↔ 훅 대상 시그니처 대조(이름·값 형식·`__instance`·`__result`), 순수 리플렉션이라 테스트·`tools/sigcheck`에서 그대로 사용 |
| `Managers/BuildFingerprint.cs` | 166 | 지문(크기·PE 링크 시각·메타데이터 버전/헤더 해시) 비교·`SameBuild`·읽기·쓰기, `VerifiedPlayTracker` 순수 함수 |
| `Loaders/TrackInfoParser.cs` | 160 | info.txt / trackinfo.txt (한/영/중/일 키, `:`/`：`, 첫 값 채택, 부제·채보자 키 무시), `NormalizeMetadataText` |
| `Managers/NativeLambdaScan.cs` | 141 | 람다 x64 본문에서 클로저 → 소유자 → 대상 필드 로드를 명령 단위로 레지스터를 따라 찾는 순수 함수 |
| `Features/CustomBga.cs` | 122 | 인게임 BGA URL 오버라이드 (`BackgroundUIProcessor` 초기화·콜백·PlayBGA/PauseBGA/IsVideoPlayable) |
| `Loaders/TextDecoder.cs` | 89 | 텍스트 인코딩 판별: BOM → 엄격 UTF-8 → 코드 페이지 순차(곡 정보·BMS) |
| `Managers/ModLog.cs` | 61 | 작업 스레드 로그를 모았다가 메인 스레드에서 출력(hwa 스캔용) |
| `Features/AttendancePopup.cs` | 59 | 출석 창 생략 |
| `Features/CustomMusicSelect.cs` | 51 | 목록 복귀 위치 |
| `Features/Unlock.cs` | 37 | 전곡 & DLC 클라이언트 해금 오버라이드 |
| `Managers/AtomicFile.cs` | 35 | 임시 파일(`.tmp`) → `File.Move` 원자적 UTF-8 쓰기 유틸 (`UpdateGuard`·`LambdaTranslator`·`IcfScanner`·`ModConfig` 공용) |
| `Models/CustomAlbumInfo.cs` | 28 | 앨범 정보 |

## 순수 모듈 테스트 (`tests/SixtarGateMod.Tests/`, 50건)

| 파일 | 건수 | 대상 |
| --- | ---: | --- |
| `PatchSignatureCheckTests.cs` | 10 | `PatchSignatureCheck` 정상 통과·인자 이름 변경·값 형식 변경·정적 `__instance`·void `__result`·열거형↔정수 크기·참조 형식 경고·특수 인자 |
| `BuildFingerprintTests.cs` | 8 | `BuildFingerprint` 지문 비교·왕복·PE 타임스탬프·메타데이터 헤더·`VerifiedPlayTracker` |
| `NativeLambdaScanTests.cs` | 8 | `NativeLambdaScan` 레지스터 추적·`call`/`xor`/`lea`/즉값 위장/`int3` 경계 |
| `SxtgBmsConverterTests.cs` | 7 | `SxtgBmsConverter` 포맷 판별·채널 매핑·3자리 정규화·채널 `02` 보존 |
| `X64DecoderTests.cs` | 7 | `X64Decoder` `MovLoad64`·`REX.W`+`0x66`·VEX `map==1` imm8·RIP-relative·SIB·잘린 입력 |
| `BmsParserTests.cs` | 5 | `BmsParser` BPM·변속·변박·롱노트·중복 BPM 덮어쓰기 |
| `TextDecoderTests.cs` | 3 | `TextDecoder` BOM·UTF-8·CP949/Shift-JIS 판별 |
| `TrackInfoParserTests.cs` | 2 | `TrackInfoParser` 다국어 키·전각 구분자·축약형 정규화 |

## 코드 규칙

- **패치 클래스 하나 = 훅 하나.** `[HarmonyPatch(typeof(대상), nameof(메서드))]`를 단 `public static class`. 이름은 `{대상}_{메서드}_Patch`.
- **핵심 패치는 `[CriticalPatch("누락 시 영향")]` 표시.** 부팅 시 누락되면 `[UpdateGuard]`가 즉시 `문제 있음`으로 경고합니다(현재 16개).
- **오버로드가 생길 수 있는 대상은 인자 형식까지 지정.** `[HarmonyPatch(typeof(X), nameof(X.M), new[] { typeof(int) })]`. 인자 형식 없이 이름만 주면 업데이트로 같은 이름 오버로드가 생겼을 때 `AmbiguousMatchException`으로 패치가 빠집니다. 패치 인자는 대상과 **같은 이름·같은 형식**으로 받습니다(부팅 시 `PatchSignatureCheck`가 대조, 값 형식이 다르면 건너뜀).
- **대상은 `typeof`로.** `AccessTools.TypeByName` 금지 (노란 경고).
- **패치 전에 ICF 확인.** `script.json`에서 주소 중복 개수를 셉니다 ([IL2CPP_PITFALLS.md](IL2CPP_PITFALLS.md)). 일부러 공유 주소를 패치하면 `[IcfShared(n, "안전한 이유")]`를 붙입니다(없으면 실행 중 검사가 건너뜀).
- **매 프레임 코드에 Unity 값 형식 헬퍼 금지.** `Box`, `Overlay.Rgba`, `MathF`만.
- **작업 스레드에서는 Unity/IL2CPP를 부르지 않고 로그는 `ModLog`로.** `ModLog.RunCaptured` 안의 로그는 메인 스레드에서 `Flush`할 때 찍힙니다.
- **자주 일어나는 로그는 `Core.DebugLog`.** `DebugLog=1`일 때만 찍힙니다. 처음 한 번만 알릴 것은 플래그로 막습니다.
- **주석은 "왜"를 씁니다.** 어떤 함수를 왜 이 방식으로 건드렸는지, 무엇이 실패했는지.

## 새 기능 넣는 순서

1. `il2cpp_dump/dump.cs`에서 클래스·메서드·RVA 찾기
2. `pe_slice` + REA로 원본 동작 확인 ([REVERSE_ENGINEERING.md](REVERSE_ENGINEERING.md))
3. ICF 확인
4. 설정이 필요하면 `ModConfig`에 키 추가 + `TEMPLATE_VERSION` 올리기 + 템플릿에 설명
5. 빌드, 배포, 게임에서 확인, 로그 확인
6. `AGENTS.md`와 이 `docs/`에 기록
