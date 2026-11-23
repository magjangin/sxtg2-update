# 현재 상태 (2026-11-24)

게임 Steam Build `26455233`(ver 1.1.007, 커스텀 곡 플레이~결과 화면 실기 검증), MelonLoader 0.7.3, 패치 클래스 118개(`[Harmony] 패치 118개 모두 적용 완료`, 2026-11-14 3차 정리 전 120개). 시작 로그의 `[UpdateGuard] 게임 빌드 … (실기 검증된 빌드)`로 빌드 확인.

## 기능별 상태

| 기능 | 상태 | 비고 |
| --- | --- | --- |
| CUSTOM 탭 + `hwa\` 곡 스캔 | 실기 확인 | 15곡으로 사용 중 |
| 커스텀 차트 플레이 (sxtg2 BMS 자동 변환) | 실기 확인 | 3자리 차트는 변환만 검증 |
| 프리뷰 / 인게임 음원 (FMOD Core 스트림) | 실기 확인 | 볼륨 페이드 추종은 실기 미확인 |
| 인게임 BGA (URL 재생) | 실기 확인 | |
| 자켓: 곡 선택 가운데, 로딩, 일시정지, 결과 | 실기 확인 | |
| 자켓: 곡 선택 오른쪽 목록 행 | 실기 확인 | 2026-10-30 잘못 지웠다가 복구 |
| 곡 끝 시점 = 음원 길이 | 실기 확인 | |
| AutoPlay | 실기 미확인 | |
| AllPerfect | 실기 확인 | 안 누른 롱노트 불빛 버그 수정 (2026-10-30) |
| MaxScore, 점수 반전 카운트다운 | 실기 미확인 | |
| 기록 저장 차단 | 실기 확인 | |
| 판정바 / 키뷰어 | 실기 확인 | 2026-10-30 성능 정리 후 재확인 필요 |
| 노트 속도 카오스, 히든, 페이드 | 실기 미확인 | |
| 커스텀 노트 스킨 / 랜덤 노트 스킨 | 실기 미확인 | |
| 마라톤 | 실기 미확인 | |
| 오프라인 가드·서버 종료 우회 (`OfflineBypass.cs`) | 실기 확인 (2026-11-07) | 리뉴얼 BACKND 42개(UnitySession 3개 포함) + `sxtg2 saver test` 24개 (총 66개 훅, 전부 `ICF=1`) |
| 전곡·DLC 해금 | 실기 확인 | |
| 출석 보상 창 자동 표시 끄기 | 실기 확인 | 로그 `[Attendance] Skipped ...` |

## 최근 변경 (2026-10-30 ~ 2026-11-22, `#n` = PR 번호)

1. **#1** AllPerfect가 안 누른 롱노트 틱에 레인 불빛을 켜던 문제 수정
2. **#2** README, 성능 정리(오버레이 IL2CPP 호출 제거, 디스크 접근 제거, 차트 캐시), 죽은 훅 제거 (4,217 → 3,746줄)
3. **#3** 출석 보상 창 자동 표시 끄기 (`SkipAttendancePopup`, config v6)
4. **#4** #2에서 잘못 지운 목록 행 자켓 훅 복구
5. **#5** 시작 시 노란 `[WARNING]` 2개 제거 (`TypeByName` → `typeof`), 실기 로그로 확인
6. **#6** 스킨 구성 기록, `button01`은 노트 스킨이 아님을 정정
7. **#7** 이 문서 묶음 추가
8. **#8** 자주 묻는 질문(FAQ) 추가
9. **#9** 비플레이 씬 `OnLateUpdate` 가드 + `Overlay.HideAll()`, BMS 파서/변환기 문자열 할당 제거, 잔여 Unity 구조체 생성자 제거 및 문서 최신화 (2026-10-31)
10. **#10** `sxtg2 saver test` 레거시 오프라인 패치 이식 및 리뉴얼(`SixtarGate.Web.BackEnd*`) 서버 종료/오프라인 완전 우회(`Features/OfflineBypass.cs`, 63개 훅) 구현 (2026-10-31)
11. **#11** 업데이트 내성 강화(`Managers/PatchResilience.cs`): 컴파일러 생성 클로저·코루틴(`__c__DisplayClass*`, `_*_d__*`, `_b__*`) 하드코딩 전면 제거 및 시그니처 기반 동적 탐색, 게임 오타 메서드 양방향 해석, 런타임 ICF 자명 스텁(1~3바이트) 가드, `TrackData` 속성 전체 자동 복제 (2026-10-31)
12. **#12** 오프라인 커스텀 곡 플레이 및 결과 화면 복구 (`OfflineBypass.cs` UnitySession 커넥터 등록 3개 훅), 모드 로그 한국어화(102개 문장), 실행 시 Steam 빌드 확인 및 Unity 예외 전달(`Managers/UpdateGuard.cs`), 업데이트 비교 도구(`tools/version_diff`) 추가 (2026-11-07)
13. **#13** 이중래퍼 람다 번역가(`Managers/LambdaTranslator.cs`): O(1) Fast-Path 캐시(`UserData/SixtarGateMod.LambdaMap.json`) + x64 기계어 대상 필드 변위 기반 스마트 구조 번역 (2026-11-07)
14. `04c94da` (main 직접 커밋) 중국어 간체·번체 메타데이터 키와 BMS `#TITLE`/`#ARTIST` 폴백 (2026-11-07). 실제로 읽히는 조건(반각 콜론·UTF-8)은 아래 알려진 한계 참고
15. `35bde96` (main 직접 커밋) 메모리 누수 방지: 공식 곡 전환 시 커스텀 오디오 스트림 해제, 디코딩/업로드 실패 텍스처 파괴, `NoteEffects` 동적 패치 해제, `JudgmentBar`·`KeyViewer` 리셋 (2026-11-07)
16. `1e3e64f` (main 직접 커밋) `TrackInfoParser.NormalizeMetadataText`: 영문 축약형 사이 공백·전각 공백 교정 (2026-11-07)
17. **#14** 제목/아티스트 우선순위 수정: `info.txt` → BMS `#TITLE` → 폴더 이름. BMS 폴백은 `info.txt`에서 값을 못 읽었을 때만 (CUSTOM 목록 15곡 중 14곡이 `test`로 나오던 문제, 2026-11-08 실기 확인)
18. **#15** 감사 (2026-11-09, 문서만): 중국어 메타데이터 로직·업데이트 내성·문서 정합성. 결과는 [AUDIT_2026-11-09.md](03-development/AUDIT_2026-11-09.md)
19. **#16** 감사 결과 수정 (2026-11-09): 곡 정보 전각 콜론·전각 쉼표/숫자·첫 값 채택·부제/채보자 키 무시·일본어 키, `TextDecoder`(UTF-8 외 인코딩), 축약형 교정 오탐 제거, 트랙 ID 충돌 번호 붙이기, `[CriticalPatch]` 필수 패치 표시, `GameAssembly.dll` 크기로 빌드 대조, 람다 스캔 확장·경고, `icf_check.py`. 오프라인 테스트 49건 통과, 실기 부팅(패치 120개·15곡·모드 선택 진입·예외 0) 확인, 커스텀 곡 플레이는 이번에 안 함

20. **#17** 코드 리뷰·업데이트 내성 강화 (2026-11-10): 실행 중 ICF 검사(`Managers/IcfScanner.cs`, 훅 대상 주소를 공유하는 IL2CPP 메서드 수를 세어 허용치 초과 패치를 건너뜀, `[IcfShared]`, 빌드별 캐시), `NoteEffects` 동적 훅 안전 확인, 버그 7건(플레이 진입 Prefix null 안전, 게임 BMS 해석기 예외 시 1노트 대체, 곡 끝 연장 로그, Unity 예외 전달 집합 상한, 깨진 자켓 반복 디코딩, `*.bms`가 `.bmson`까지 맞던 문제·정렬, 람다 번역 시간 표시), 매 프레임 `UpdatePatternTime` 조기 반환. 실기 부팅 3회(검사 결과가 `HOOK_TABLE.md`와 일치, 시험 패치 건너뜀 확인, 캐시 경로). 커스텀 곡 플레이는 이번에 안 함. [REVIEW_2026-11-10.md](03-development/REVIEW_2026-11-10.md)

21. **#18** 코드 리뷰·람다 찾기 강화 (2026-11-12): 후보에 바깥 형식 인스턴스 메서드(`this`만 잡은 람다) 포함, 람다 번호 정렬, 정확한 형식 우선, 캡처 필드·소유자 도달로 거르기, 레지스터를 따라가는 기계어 대조(`Managers/NativeLambdaScan.cs`, 모든 후보 대조·후보 1개도 검증), 사전에 `how`·RVA 대조·안 쓰는 키 정리, 코루틴 이름 정확 일치, `GotoPlayScene` 번들 콜백 `[CriticalPatch]`, ICF 검사 없이 붙은 패치 경고. 오프라인 시험 `tools/lambdascan` 35건, 실기 부팅 3회(옛 사전 → 새 형식, 직행 9개, 사전 삭제 후 재번역 9개 RVA 동일·경고 0). 커스텀 곡 플레이는 이번에 안 함. [REVIEW_2026-11-12.md](03-development/REVIEW_2026-11-12.md)

22. **#19** 병목 확인·람다 기계어 대조 명령 단위·사전 JIT 검사 (2026-11-14): 부팅 비용 실측(Harmony 120개 2.1초 = 패치당 고정 17ms, 튀는 패치 없음 → 패치별 시간 로그만 추가), `Managers/X64Decoder.cs`로 명령 경계를 따라 레지스터 추적(`lea`/`call`/`xor` 덮어쓰기 반영, capstone 대조 명령 188,398개 불일치 0), `FindMethodInType`도 번역 사전에(항목 10개), `Managers/PatchPreflight.cs`로 패치 코드를 미리 JIT해 업데이트로 사라진 인터롭 멤버를 쓰는 패치는 건너뜀, hwa 폴더 경로를 게임 루트로 통일. 실기 부팅 2회(120개 적용·사전 JIT 435개 54ms·재번역 10개 RVA 동일·경고 0). 커스텀 곡 플레이는 이번에도 안 함(사용자가 PC 사용 중). [REVIEW_2026-11-14.md](03-development/REVIEW_2026-11-14.md)
23. 전 파일 리팩토링·병목 개선·업데이트 내성 강화 (2026-11-14): `LambdaTranslator.TranslateMethodInType` 외부 타입/캐시 바인딩·로그 중복 수정, `PatchPreflight` IL/서브그래프 캐시 및 `InlineField`/`InlineType`/`InlineTok` 토큰 검사, `PatchResilience` Expression 델리게이트 캐시 + `CreateSetter` 타입 폴백 + 핵심 7개 패치 `[CriticalPatch]` 확대, `CustomTrackManager`/`Loaders` 폴더당 단일 디렉터리 스캔·무할당 `Span`/`char[]` 파싱, `UpdatePatternTime` 매 프레임 IL2CPP 프로퍼티 호출 제거, `tools/` 핸들·캐시 정리. Release 빌드(경고/오류 0), `lambdascan` 40건 `ALL PASS`, 실제 `hwa\` 15곡+엣지 케이스 SHA-256 동등성 100% 검증 및 `Mods/` 배포(실기 부팅은 PC 사용 중으로 생략). [REVIEW_2026-11-14.md](03-development/REVIEW_2026-11-14.md) 7장.
24. 3차 감사·중복 패치 통합 (2026-11-14): 판정바·키뷰어를 끈 플레이에서 오버레이 캔버스·매 프레임 화면 크기 조회 생략, `RandomNoteSkin` 프리팹 Image 값을 플레이당 1회 읽음, 같은 대상 이중 패치(`GotoMusicSelect`, `GetNoteObejct`) 통합으로 패치 클래스 118개, `NoteEffects` 가림 없는 플레이 사전 조회 생략, `JudgmentBarSide` 비교 1회화. 실기 부팅(`패치 118개 모두 적용 완료`, 오류 0) + 커스텀 곡 `blue` 플레이 → `Result` → `MusicSelect` 복귀, `Player.log` 예외 0. [REVIEW_2026-11-14.md](03-development/REVIEW_2026-11-14.md) 8장.
25. 4차 감사·부팅 메인 스레드 비용 (2026-11-15): `GameAssembly.dll` 주소를 모듈 전체 열거 대신 `GetModuleHandleW`로, hwa 스캔을 작업 스레드에서 Harmony 적용과 겹쳐 돌림(`ModLog`로 로그 순서 유지), 노트 스킨 PNG는 작업 스레드 해독 + 플레이 준비 때 업로드(`CustomNotes=0`이면 업로드 안 함), 판정바를 끈 플레이에서 판정마다 쌓이던 기록·문자열 생략, 설정의 NaN/Infinity 거부, `RandomNoteSkin` 스폰당 IL2CPP 호출 1개 감소. 레거시 훅 24개는 정적 호출 분석(`tools/version_diff/xrefs.py`) 결과 리뉴얼 코드도 레거시 `UserAccountModule`을 불러 유지. Release 빌드 경고/오류 0, `lambdascan` `ALL PASS`. 실기: 모드 초기화 4.3초 → 2.0초, CUSTOM 15곡, 커스텀 곡 플레이 → 결과 화면, 오류·예외 0. [REVIEW_2026-11-15.md](03-development/REVIEW_2026-11-15.md)
26. 업데이트 지문 비교·점검 결과 한 줄 (2026-11-16): `UpdateGuard`가 `GameAssembly.dll`·`global-metadata.dat`·`UnityPlayer.dll` 크기와 빌드 ID를 지난 실행(`UserData\SixtarGateMod.BuildState.txt`)과 비교해 달라진 것을 알리고(메타데이터·Unity 런타임 변경은 별도 경고), 패치 적용 뒤 `업데이트 점검 결과:` 한 줄로 정상/일부 빠짐/필수 빠짐/검증 안 됨을 요약. 순수 로직은 `BuildFingerprint.cs`로 분리해 `lambdascan` 7건 추가. Release 빌드 경고/오류 0, `lambdascan` `ALL PASS`, 실기는 안 함(2026-11-18 부팅에서 확인). [REVIEW_2026-11-16.md](03-development/REVIEW_2026-11-16.md)
27. 크기가 같은 변경 감지·실기 확인 자동 기록 (2026-11-18): 지문에 `GameAssembly.dll`·`UnityPlayer.dll` PE 링크 시각, `global-metadata.dat` 버전·헤더(섹션 표) 해시 추가(파일 앞 4KB만 읽음). 커스텀 곡으로 `Renewal_Play` → `Result`를 게임 내부 예외 없이 지나면 그 지문을 `BuildState.txt`에 `verifiedPlay=`로 기록하고, 다음 부팅부터 지문이 완전히 같으면 "검증 안 됨" 대신 `정상 (이 PC에서 실기 플레이로 확인된 빌드 …)`. 소스의 검증 목록을 고치지 않아도 새 빌드를 확인할 수 있음. `lambdascan` 새 시험 21건 `ALL PASS`, 실기 부팅(지문 기록·`정상`·패치 118개·15곡)과 커스텀 곡 플레이 → 결과 화면(예외 0) 확인. 처음 판은 검증 목록에 있는 빌드면 기록을 로그 없이 건너뛰어 그 플레이가 기록되지 않았음 → 항상 기록·로그로 고침(수정판의 결과 화면 기록은 실기 미확인). [REVIEW_2026-11-18.md](03-development/REVIEW_2026-11-18.md)
28. 구조·빌드·테스트·문서 개선 및 v2.3.0 릴리스 (2026-11-18): `AGENTS.md` 슬림화(상세 기록 `docs/03-development/HANDOVER_NOTES.md` 분리), `SixtarGateMod.csproj` `<GameDir>`(환경변수 `SIXTAR_GAME_DIR` / `Directory.Build.props.user`) 및 `PostBuild` `Mods\` 자동 복사, `Core.cs`에서 `Features/CustomBga.cs`·`CustomJackets.cs`·`Unlock.cs` 분리, `hwa\` 변경 감지 재스캔(`RefreshIfChanged`), `LoadChartText` `TextDecoder` 통일, 배너 버전 동적 조회, 자켓 디코딩 상한 1024 축소, `BmsParser` 변속(03/08)·변박(02)·데이터 줄 필터 보강, `OnDeinitializeMelon` `StopAll()`·`PlayAssist` 점수 래퍼 캐시 정리, xUnit 순수 모듈 테스트(`tests/SixtarGateMod.Tests/`, 18건 통과) 및 GitHub Actions CI(`.github/workflows/ci.yml`) 추가.
29. 전 파일 점검·병목/안정성 개선·문서 최신화 (2026-11-19): `MediaOverrideManager.PreloadRoutine` `_preloading` 플래그 `finally` 해제 및 `RefreshIfChanged` 시 `ClearFailed()` 호출, `CustomTrackManager` 하위 폴더 이름순 정렬, `PatchPreflight.TrimStartupCaches()` 부팅 후 IL 캐시·옵코드 테이블 회수, `UpdateGuard` 예외 카운터 `Interlocked`/`Volatile` 적용, `CustomMarathon` 곡 선택 복귀 시 `IsActive` 누수 정리, `SxtgBmsConverter.IsSxtgFormat` 무할당 `Span` 파싱 및 `Lcm` 오버플로 방지, `README.md`·`PROJECT_OVERVIEW.md`·`CODE_STRUCTURE.md` v2.3.0 반영. xUnit 19건·Release/Debug 빌드(경고/오류 0)·`lambdascan` 통과(실기 미실행).
30. 업데이트 내성·런타임 버그 수정·테스트 확충·문서 정합성 강화 (2026-11-19):
    - **업데이트 내성:** `LambdaTranslator`·`IcfScanner` 캐시 지문을 `UpdateGuard.BinaryFingerprint`(PE 링크 시각 + `global-metadata.dat` 헤더 해시)로 통일, `IcfScanner` 원자적 캐시 저장 및 미스캔 주소 `-1` 구분 경고, `PatchResilience` 대상 탐색·동적 타깃 예외 로깅, `LambdaTranslator` 소유자 오프셋 `0x18` 임의 폴백 제거(`-1` → `Inconclusive`) 및 빈 RVA 캐시 무효화, `PatchPreflight` 어셈블리 로드 예외 포착, `X64Decoder` `REX.W`+`0x66` 및 VEX `map==1` imm8 수정, `[CriticalPatch]` 6개 추가(`GotoPlayScene`·`GotoMusicSelect`·`GetSelectableCategoty`·`RecordCoursePlayData`·`RecordLicensePlayData`·`SoundModuleForFMOD.Stop`), `tools/version_diff/check_update.ps1`·`icf_check.py` 개선.
    - **버그·누수 수정:** `PlayAssist.EnsureSession` 포인터 재사용 충돌 방지 및 `ScoreProcessor` `Finalizer` 복원, `SxtgBmsConverter` 3자리 차트 채널 `02` 변박 보존·`Lcm` 상한 클램프, `BmsParser` 동일 위치 중복 BPM 덮어쓰기, `PngDecoder` 청크/크기/필터/컬러 타입 0·2 `tRNS` 경계 검증, `MediaOverrideManager.LoadAsync` `try/finally` `Pending` 누수 방지, `CustomJackets` 목록 행·재적용 코루틴 stale 가드, `CustomMarathon` 씬 이탈·연타 이중 진행 가드, `Overlay.Create()` 풀 초기화, `JudgmentBar.ShapeCache` GPU 텍스처/스프라이트 해제, `CustomNoteSkin.PrepareForPlay` 리셋, `CustomAudioManager` FMOD 핸들 누수 방지, `ModConfig` 공백 허용 초기화 및 색상 `IsFinite`/클램프.
    - **테스트·문서:** xUnit 순수 테스트 19건 → 40건(`NativeLambdaScanTests`·`X64DecoderTests`·`BuildFingerprintTests`·회귀 테스트 추가), `HOOK_TABLE.md`·`ARCHITECTURE.md`·`OFFLINE_AND_UNLOCK.md`·`MEDIA_SYSTEM.md`·`INSTALL_AND_LAYOUT.md`·`TROUBLESHOOTING.md`·`CUSTOM_TRACKS.md`·`BUILD_AND_DEPLOY.md`·`DEBUGGING_GUIDE.md`·`docs/README.md` 현행화. Release/Debug 빌드(경고/오류 0)·xUnit 40건·`lambdascan` `ALL PASS`(실기 미실행).
31. 수정 기록 감사·회귀 수정·리팩토링·인수인계 강화 (2026-11-19):
    - **수정 기록·업데이트 내성 감사 수정:** `CustomMarathon.OnSceneLoaded` `inPlayFlow`에 `"Loading"` 씬 누락 회귀 수정, `CustomBga.PlayAfter`에서 지연 BGA를 막을 수 있던 `PlayAssist.IsPlaying` 조건 제거, `LambdaTranslator.RvaMismatch` `code == IntPtr.Zero` 검사 순서 교정, `UpdateGuard.LogBuildVerdict` `!_fingerprintChanged` 조건 통일, `CustomJackets.ClearRowCache()` 비-MusicSelect 씬 정리, `HOOK_TABLE.md` `Resume` 두 오버로드 행 분리(`icf_check.py` 120/120 전수 검사).
    - **리팩토링·인수인계:** `Managers/AtomicFile.cs` 추출로 `UpdateGuard`/`LambdaTranslator`/`IcfScanner`/`ModConfig` 원자적 쓰기 통일, `Overlay.White`·`CustomJackets.SetJacketImage`·`Core.LoadCurrentAlbumBmsOrDefault` 중복 제거 및 미사용 `using`·배열 할당 정리, `AGENTS.md`·`HANDOVER_NOTES.md`·`CODE_STRUCTURE.md` 최신화. Release/Debug 빌드(경고/오류 0)·xUnit 40건·`lambdascan`·`icf_check.py` 통과(실기 미실행).
32. 패치 시그니처 대조·오버로드 내성·오프라인 대조 도구 (2026-11-19): Harmony는 패치 인자를 이름으로만 묶고 형식을 확인하지 않아, 업데이트로 훅 대상 인자 형식이 바뀌면(`int`→`long` 등) 패치가 붙은 뒤 첫 호출에서 잘못된 값·크래시가 남. `Managers/PatchSignatureCheck.cs`가 부팅 시 Prefix/Postfix/Finalizer 인자를 대상과 대조해 값 형식 불일치·없는 인자·정적 대상 `__instance`·void 대상 `__result`면 건너뛰고(`[Harmony/Signature]`, `[CriticalPatch]`면 필수 빠짐), 참조 형식 불일치는 경고만. `PatchResilience.ResolveMethod`가 업데이트로 생긴 오버로드(`AmbiguousMatchException`)를 잡고, 이름·개수 대체 경로를 `[Harmony/Resolve]` 경고로 남김. `tools/sigcheck`(오프라인, `check_update.ps1` 5단계)로 게임을 켜기 전 전수 대조: 빌드 `26455233` 통과 114·오류 0·경고 0·클로저 4개 런타임 필요(인자는 `Il2CppObjectBase __instance`뿐). xUnit 40 → 50건, Release 빌드 경고/오류 0. 실기 미실행. [REVIEW_2026-11-19.md](03-development/REVIEW_2026-11-19.md)
33. 추가 점검 (2026-11-20): **버그** `ModConfig.ReloadIfChanged`가 읽기 전에 수정 시각을 기록해, 편집기 저장 순간 잠긴 파일을 못 읽으면 그 변경이 다음 저장까지 무시되던 문제 수정(성공 후 기록, `FileShare.ReadWrite`로 읽음). **개선** `CustomTrackManager.RefreshIfChanged`가 폴더 수정 시각만 봐서 `info.txt` 제자리 저장을 못 잡던 것을 곡 폴더 안 파일 이름·크기·수정 시각까지 접어 비교(열거 속성 사용, 1초 간격). **업데이트 내성** 인자 형식 없는 `[HarmonyPatch(typeof(X), "M")]`에 업데이트로 오버로드가 생기면(게임 HarmonyX가 `AmbiguousMatchException`을 던짐을 확인) 패치 인자 목록으로 맞는 오버로드가 하나일 때 골라 직접 패치(`PatchSignatureCheck.PickOverload`, `PatchResilience.PatchManually`), `tools/sigcheck`도 같은 해석. xUnit 52건, Release 빌드 경고/오류 0, sigcheck 통과 114·오류 0·오버로드로 고름 0. 실기 미실행. [REVIEW_2026-11-19.md](03-development/REVIEW_2026-11-19.md) 5장
34. 심층 감사·오프라인/기능 모듈 안정화·시그니처 대조 보강 (2026-11-20):
    - **시그니처 대조·도구:** `PatchSignatureCheck` `__0`/`__1` 위치 인자 검증·`__instance` 값/참조 불일치 오류·`Strip()` 포인터 유지(`int*` vs `int` 감지), `tools/sigcheck` `overloaded++` 조건 버그 수정·`BaseType` 탐색·`TargetMethods()` 지원 동기화.
    - **업데이트 내성·기능 안정화:** `RandomNoteSkin.BindGetNoteReference`의 오타 직접 호출 폴백 제거(업데이트 시 `PatchPreflight` 공유 스폰 훅 탈락 방지) 및 `EndPlay()` 스킨/레인 캐시 정리, `NoteEffects.SetNotePatches` 부분 패치 롤백 및 `RestoreAndClearCovers()` 재시작 투명도 복원, `CustomMusicSelect` `nameof` 통일·`TrackID` null/예외 가드, `OfflineBypass` 핵심 4개 훅(`[CriticalPatch]` 16→20개) 추가·Finalizer/catch `__instance` null 가드·오타 로그 수정, `AttendancePopup` `_getOwner` 누락 경고 및 `Prefix` 예외 가드, `CustomNoteSkin` 스프라이트 생성 실패 시 `Texture2D` 파괴.
    - **검증:** xUnit **57건 통과**, Release/Debug 빌드(경고/오류 0), `tools/sigcheck` 통과 114·오류 0·경고 0·오버로드로 고름 0(실기 미실행).
35. 빈 `catch` 정리·`PngDecoder` 상한·테스트 보강·v2.3.1 (2026-11-22, [상세](03-development/REVIEW_2026-11-22.md)): 빈 `catch { }` 12곳을 예외 필터·이유 주석·`DebugLog`로 바꿈(마라톤 Esc 입력 오류는 한 번만 경고), `PngDecoder`에 `MaxPixels`(40,000,000) 상한 추가(16384×16384 PNG가 해독 버퍼 2GB를 잡던 것 → Unity `LoadImage` 폴백), xUnit 57건 → 71건(`PngDecoder`·`AtomicFile`·소스 규칙 `SourceHygieneTests`). **검증:** Release 빌드 경고/오류 0, xUnit 71건 통과. 실기는 미실행.

36. 업데이트 내성·병목·v2.3.2 (2026-11-23, [상세](03-development/REVIEW_2026-11-23.md)): **업데이트 내성** 인자 형식을 지정한 `[HarmonyPatch]` 대상이 업데이트로 인자가 늘거나 형식이 바뀌어 없어지면 같은 이름 메서드가 하나뿐이고 패치 인자와 맞을 때만 골라 직접 붙임(`PickChangedSignature`, 전에는 필수 패치도 그대로 빠짐), 대체 해석이 있으면 `업데이트 점검 결과`가 "정상" 대신 실기 확인 요청, `tools/sigcheck` 동기화. **병목** 오버레이의 그리기 호출마다 하던 Unity `== null`(IL2CPP `op_Equality`, 키뷰어를 켜면 프레임당 약 50회)과 매 프레임 화면 크기 조회를 관리 플래그·30프레임 간격으로, 판정바 라벨 지연 생성, 노트 스폰·목록 행 자켓 훅의 Unity null 비교 제거. **검증:** Release/Debug 빌드 경고/오류 0, xUnit 74건, sigcheck 통과 114·오류 0·대체 해석 0. 실기는 미실행.

37. 로더 지문·카테고리 비교 통일·v2.3.3 (2026-11-23, [상세](03-development/REVIEW_2026-11-23.md) 5장): **업데이트 내성** 지문에 `loaderVersion`(MelonLoader/Il2CppInterop 버전, 위험 변경) 추가 — 옛 기록에 없으면 비교하지 않는 선택 키, CUSTOM 카테고리 비교를 대소문자 무시로 통일. **병목** `CustomNoteSkin.Apply` Unity null 비교 제거. **검증:** Release/Debug 빌드 경고/오류 0, xUnit 77건, sigcheck 통과 114·오류 0·대체 해석 0, lambdascan `ALL PASS`. 실기는 미실행.
38. 증분 재스캔·자켓 교체 반영·v2.3.4 (2026-11-24, [상세](03-development/REVIEW_2026-11-24.md)): **병목** `hwa\` 재스캔이 폴더별 지문이 같은 곡은 재사용(곡 하나 추가에 전 곡 BMS 재해석 → 바뀐 폴더만), 자켓 캐시 조회의 Unity null 비교 제거. **한계 해소** 자켓 캐시 키에 파일 크기·수정 시각을 넣어 그림 교체가 재시작 없이 반영. **검증:** Release/Debug 빌드 경고/오류 0, xUnit 77건, bodycheck·sigcheck(통과 114·오류 0)·lambdascan 통과. 실기는 미실행.

## 알려진 한계

- 오른쪽 곡 목록에서 MAX COMBO가 `---`로 나옵니다. 직접 채우는 시도를 했으나 화면에 반영되지 않아 뺐습니다.
- 커스텀 곡 자켓 그림을 바꾸면 CUSTOM 탭에 다시 들어가(재스캔) 그 곡을 고를 때 반영됩니다(v2.3.4, 실기 미확인). 이미 화면에 그려진 목록 행은 다시 그려질 때 바뀝니다.
- 키뷰어 키 이름은 리뉴얼 입력 모듈에서 읽고, 못 읽으면 레인 이름(LL, L …)을 씁니다.
- 결과 화면 특수 연출(시이 대신 다른 모델)은 커스텀 곡에 나오지 않습니다. 곡 ID로 고르기 때문입니다. [RESULT_ANIMATIONS.md](04-game-reference/RESULT_ANIMATIONS.md) 참고.
- 노란 줄 `[Il2CppInterop] Class::Init signatures have been exhausted`는 MelonLoader 자체 메시지라 남습니다.
- 곡 정보 파일이 UTF-8이 아니면 시스템 ANSI(한국어 PC = CP949)부터 시도하므로, 다른 나라 PC에서 "ANSI"로 저장한 파일(예: GBK)은 잘못 풀릴 수 있습니다. 간체 전용 글자는 게임 글꼴에 없을 수 있습니다(미확인). [CUSTOM_TRACKS.md](02-systems/CUSTOM_TRACKS.md).
- 실행 중 ICF 검사는 제네릭 인스턴스 코드를 세지 않습니다. 업데이트 후에는 `tools/version_diff/icf_check.py`로도 한 번 확인하세요([BUILD_AND_DEPLOY.md](03-development/BUILD_AND_DEPLOY.md)).
