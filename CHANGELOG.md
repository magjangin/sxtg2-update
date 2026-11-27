# 변경 이력

형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)를 따르고, 버전은 `mod/SixtarGateMod.csproj`의 `<Version>`입니다. 상세 기록은 `docs/CURRENT_STATUS.md`와 `docs/03-development/REVIEW_*.md`.

## [2.4.0] - 2026-11-27

### 성능·병목
- `JudgmentBar`: 매 프레임 `HitHistory.RemoveRange(0, expired)`로 발생하던 `Array.Copy` 메모리 이동 병목 제거. 활성 틱 인덱스를 전진시키고 만료 항목이 16개 이상 누적되었을 때만 일괄 정리하도록 최적화.
- `TextDecoder`: 자주 쓰이는 CJK 코드페이지(Shift-JIS 932, CP949 949, GBK 936, Big5 950)에 대한 `Encoding` 인스턴스를 동시성 캐시에 보관하여 반복 팩토리 조회 및 예외 생성 오버헤드 제거.
- `SxtgBmsConverter`: 비정상적인 분모 누적으로 인한 과대 해상도(resolution) 폭증을 방지하도록 최대 마디 해상도 상한(`MaxResolution = 3072`) 적용(마디당 거대 `char[]` 할당 방어).

### 기능·업데이트 내성
- `CustomNoteSkin`: `CustomNotes/` 폴더 실시간 지문 감지 및 자동 핫리로드 지원. 부팅 시 폴더가 비어 있었거나 게임 실행 중 노트를 추가/수정한 경우에도 다음 플레이 준비 시 자동 반영.
- `PatchSignatureCheck`: `___fieldName` 필드 주입 인자 대조 시 언더스코어 접두사뿐 아니라 대소문자 무시(case-insensitive) 폴백을 추가하여 게임 업데이트 시 프로퍼티/필드 명명 규칙 차이에 대한 내성 극대화.

### 테스트·문서
- 단위 테스트 109건 → **111건** 통과(`SxtgBmsConverterTests.Convert_LimitsMaxResolution`, `TextDecoderTests.Decode_CachedEncoding_ConsistentOnMultipleCalls` 추가).
- [`REVIEW_2026-11-27.md`](docs/03-development/REVIEW_2026-11-27.md) 추가, `CURRENT_STATUS.md`·`HANDOVER_NOTES.md`·`AGENTS.md`·`docs/README.md` 현행화.

### 검증
- Release/Debug 빌드 경고/오류 0, xUnit 111건, `tools/bodycheck` 통과, `tools/sigcheck` 통과 114·오류 0·경고 0·대체 해석 0.

## [2.3.9] - 2026-11-26

### 성능·병목
- `TrackInfoParser.NormalizeMetadataText`: 아포스트로피류·전각 공백·탭·다중 공백이 없는 일반 제목·아티스트 문자열에 대해 5개 정규식을 건너뛰는 Fast-Path 추가(수백 곡 대량 스캔 시 정규식 5회 및 불필요한 문자열 복사·GC 부하 제거).
- `CustomAudioManager.StopAll`: 채널 정지 순회 시 `new List<...>` 힙 할당을 제거하고 내부 사전 순회 해제 루프로 최적화.

### 업데이트 내성·안정성
- `Core.OnSceneWasLoaded`: `sceneName` null 방어 처리(`sceneName ?? ""`). 특수 씬 전환이나 언로드 시 NRE로 후속 정리/마라톤/UpdateGuard 호출이 중단되는 문제 방어.
- `PatchSignatureCheck`: 필드 주입 `___fieldName` 확인 시 대상 클래스의 언더스코어 접두사 변형(`_foo` ↔ `foo`) 자동 감지 지원으로 Il2CppInterop 필드/속성 매핑 내성 강화.
- `BmsParser`: 마지막 노트 시각 계산 시 `float.IsFinite` 검사 추가(비정상 배율/마디로 인한 `NaN`/`Infinity` 오염 방지).

### 테스트·문서
- 단위 테스트 109건 유지 및 정규화 Fast-Path/언더스코어 필드 주입 케이스 보강.
- [`REVIEW_2026-11-26.md`](docs/03-development/REVIEW_2026-11-26.md) 2차 감사 항목 갱신, `CURRENT_STATUS.md`·`HANDOVER_NOTES.md`·`AGENTS.md` 현행화.

### 검증
- Release/Debug 빌드 경고/오류 0, xUnit 109건, `tools/bodycheck` 통과, `tools/sigcheck` 통과 114·오류 0·경고 0·대체 해석 0.

## [2.3.8] - 2026-11-26

### 수정
- hwa 폴더 지문: 곡 폴더 자체의 수정 시각을 빼고 계산을 `HwaStamp` 하나로 통일. Windows가 하위 폴더 시각을 부모 색인에 늦게 반영해 스캔과 CUSTOM 탭 진입 확인이 다른 지문을 낼 수 있었고, 그러면 불필요한 재스캔과 증분 재사용 실패(BMS 재해석)가 났음. 변경 확인 중 예외는 모두 잡음.
- 큰 `MaxScore`(예: 1e10)에서 점수가 int 범위를 넘어 음수로 나오던 문제: 상한 10억, 0 이하·비정상 값은 1,000,000, 고치면 경고(`ScoreMath`).

### 테스트·문서
- 단위 테스트 95건 → **109건**(`HwaStampTests` 5, `ScoreMathTests` 9).
- [`REVIEW_2026-11-26.md`](docs/03-development/REVIEW_2026-11-26.md) 추가, `AGENTS.md`·`CURRENT_STATUS.md`·`HANDOVER_NOTES.md`·`CODE_STRUCTURE.md`·`docs/README.md` 현행화.

### 검증
- Release/Debug 빌드 경고/오류 0, xUnit 109건, `tools/bodycheck` 통과, `tools/sigcheck` 통과 114·오류 0·경고 0·대체 해석 0, `tools/lambdascan` `ALL PASS`. 게임 실기는 미실행.

## [2.3.7] - 2026-11-25

### 업데이트 내성
- CUSTOM 곡을 만들 기증 곡을 `"Starting Point"`(TrackData) → 첫 TrackData → 첫 곡 순으로 고름. 전에는 이름이 안 맞으면 목록 첫 곡을 그대로 써서, 업데이트로 그 곡이 다른 `ITrack` 구현이면 곡 속성을 복사하지 못한 커스텀 곡이 조용히 만들어졌음. 대체 시 경고 한 번.

### 성능
- `JudgmentBar`: 원시 텍스처 업로드를 못 쓰는 환경에서 모양 스프라이트 버퍼(모양 4개 × w×h×4바이트)를 매 프레임 다시 만들던 것 제거(사각형으로 그림).
- 플레이 흐름 자켓(로딩·일시정지·결과·배경): 곡 선택 화면에서 시작한 비동기 해독이 진행 중이면 동기 해독을 또 하지 않고 그 결과를 기다림(같은 PNG 이중 해독, 1080p 50~100ms 제거).
- `RandomNoteSkin`: 풀 노트에 지난번 입힌 스킨을 기억해 배치 속성(`type`·`preserveAspect`)이 같으면 건너뜀(스폰당 Image setter 5 → 3).

### 문서
- [`REVIEW_2026-11-25.md`](docs/03-development/REVIEW_2026-11-25.md) 6장 추가, `CURRENT_STATUS.md`·`HANDOVER_NOTES.md`·`CODE_STRUCTURE.md`·`docs/README.md`·`AGENTS.md` 현행화.

### 검증
- Release/Debug 빌드 경고/오류 0, xUnit 95건, `tools/bodycheck` 통과, `tools/sigcheck` 통과 114·오류 0·경고 0·대체 해석 0, `tools/lambdascan` `ALL PASS`. 게임 실기는 미실행.

## [2.3.6] - 2026-11-25

### 업데이트 내성
- 게임 업데이트로 커스텀 곡 플레이 경로의 필수 패치(`GetTracks`·`GotoPlayScene`·번들 콜백·`MikazukiConverter.Parse`·기록 저장 차단) 중 하나라도 빠지면 CUSTOM 탭을 만들지 않음. 전에는 탭이 그대로 생겨 곡을 고르면 로딩 화면에서 멈춰 강제 종료해야 했음. 공식 곡은 그대로 플레이 가능, 빠진 패치 이름은 부팅 로그 패치 요약 바로 아래에 표시(`PatchResilience.IsApplied`/`MissingOf`, `Core.CustomPlayReady`).

### 성능
- `NoteEffects`(속도 카오스·히든·페이드를 켠 플레이의 노트 위치 훅): 노트·프레임마다 만들던 `_tickData` 인터롭 래퍼(GC 핸들 할당)를 없애고 필드 오프셋으로 포인터만 읽음, `Pointer`(네이티브 호출)를 호출당 최대 4번 → 1번.
- `RandomNoteSkin`: 노트 스폰마다 렌더러·노트 `Pointer`를 한 번씩만 읽음.
- `CustomAudioManager.ApplyVolumes`: 장면 페이드 중 매 프레임 불리는데 커스텀 스트림이 없으면 보정 볼륨 getter(IL2CPP 호출)도 부르지 않음.

### 문서·테스트
- 단위 테스트 94건 → **95건**(`SourceHygieneTests.CustomPlayGatePatchesAreCritical`: CUSTOM 탭 게이트의 패치가 모두 `[CriticalPatch]`인지 고정).
- [`REVIEW_2026-11-25.md`](docs/03-development/REVIEW_2026-11-25.md) 추가, `AGENTS.md`·`CURRENT_STATUS.md`·`HANDOVER_NOTES.md`·`CODE_STRUCTURE.md`·`ARCHITECTURE.md`·`docs/README.md` 현행화.

### 검증
- Release/Debug 빌드 경고/오류 0, xUnit 95건, `tools/bodycheck` 통과, `tools/sigcheck` 통과 114·오류 0·경고 0·대체 해석 0, `tools/lambdascan` `ALL PASS`. 게임 실기는 미실행(다음 실기 때 확인).

## [2.3.5] - 2026-11-24

### 업데이트 내성
- `CustomAudioManager`: `PlayLocal`/`ApplyVolumes`의 `module.CurrectionVolume` 직접 참조를 `PatchResilience.CreateValueGetter<float>(..., "CurrectionVolume", "CorrectionVolume")`로 통일(게임 업데이트로 오타가 수정돼도 커스텀 오디오가 깨지지 않음).
- `UpdateGuard`: `VerifiedBuilds` 목록에 있는 빌드에서 메타데이터·UnityPlayer·로더가 바뀐 경우, 실기 플레이 통과 전까지 `BuildState.txt`를 덮어쓰지 않음(2번째 부팅부터 실기 확인 없이 "검증된 빌드"로 통과하던 문제 수정). 대체 해석(`fallbackResolved > 0`) 시에도 실기 확인 기록 차단.
- `PatchPreflight`: 닫힌 제네릭 메서드를 오픈 정의로 바꾸지 않고 실제 타입 인자로 사전 JIT(`PrepareMethod`) 실행.
- `PatchSignatureCheck`: `ref object`로 값 형식 받기 차단, `bool`↔`byte`·`char`↔`short` 동일 크기 정수 오판 제거, `___field` 주입 필드 존재·형식 검사 추가. `NoteEffects` 동적 패치 부착 시에도 `PatchSignatureCheck` 실행.
- `IcfScanner`: 스캔 루프에서 발견되지 않은 주소(닫힌 제네릭 클래스 등)를 `0`(안전) 대신 `-1`(미발견 경고)로 기록, 빈 지문 시 캐시 생략.
- `X64Decoder` / `LambdaTranslator`: `vzeroupper`/`vzeroall` ModRM 없음 처리, SIB 인덱스 레지스터 존재 시 `BaseReg = -1`, `0x3C`(`cmp al, imm8`) RAX 쓰기 오판 제거, `ScanNative`에 Windows `VirtualQuery` 페이지 경계 제한 추가(섹션 끝 AV 크래시 방지).
- `PatchResilience.LogResolveWarning`: 오프라인 `tools/sigcheck`에서 MelonLoader 런타임 부재로 대체 해석 경고가 예외로 묻히지 않도록 방어.

### 성능
- `PngDecoder`: 가장 흔한 RGBA(`colorType == 6`) 출력을 픽셀당 `Buffer.BlockCopy`(1080p당 약 207만 회)에서 행 단위 1회(1,080회)로 최적화, `filter == 0` 스킵 및 `switch (filter)`/`switch (colorType)` 루프 외부화.
- `CustomTrackManager.ScanHwaFolder`: 단일 디렉터리 순회에서 `_lastHwaStamp`를 함께 계산(부팅 2회·갱신 3회 → 1회 열거).
- `MediaOverrideManager.LoadAsync`: 작업 스레드가 읽은 비-PNG(JPG 등) 바이트를 메인 스레드 `ImageConversion.LoadImage`로 재사용해 이중 파일 읽기 제거.
- `RandomNoteSkin.ImagesOf` Unity `op_Equality` 및 LINQ `.ToArray()` 제거, `CustomJackets` `track.TrackID` 중복 조회·무자켓 코루틴 제거, `NoteEffects.ApplyLaneCover` 딕셔너리 중복 조회 제거, `KeyViewer._actions` 필드 제거.

### 수정
- `Overlay.HideAll()`: 파괴된 캔버스(`_root == null`) 가드 추가(씬 전환 시 `OnSceneWasLoaded` 후속 정리 중단 방지), `Overlay.Label()` 풀 재사용 시 `Outline.effectColor` 알파 갱신 누락 수정.
- `Core.OnLateUpdate()`: `CustomMarathon.Update(now)`를 `Overlay.Begin()` 이전으로 분리(오버레이 생성 실패 시에도 Esc 중단·자동 진행 동작, 프레임 중간 `HideAll` 꼬임 방지).
- `CustomBga`: 세대 카운터(`_playSeq`)로 일시정지/재개 뒤 이전 지연 재생 코루틴(`PlayAfter`)의 강제 재생 차단.
- `MediaOverrideManager`: 비동기/동기 자켓 로드 경쟁 시 중복 GPU 텍스처 누수 방지 및 `MakeSprite` 예외 시 `Texture2D` 파괴.
- `PngDecoder`: 손상된 zlib(`IDAT`)에서 `InvalidDataException` 대신 `false` 반환(`Try*` 계약 준수), `IHDR` compression/filter 0 검증.
- `BmsParser` / `SxtgBmsConverter` / `TrackInfoParser`: `#TITLE`/`#ARTIST` 공백 구분자 확인, `Infinity` 차단, 게임 네이티브 BMS 채널(`19`, `51`~`59`, 일반 WAV 키) `MaxTime` 반영, `IsSxtgFormat`에 OPEN `'04'` 추가, `BGA作者`/`Jacket` 크레딧 필터링 및 0 이하 난이도 제외.
- `RandomNoteSkin.EnsureCatalog` null 재시도 차단 수정, `PlayAssist.OnTickEvent` `Finalizer` 전환, `CustomAudioManager.StopAll` 예외 격리, `AtomicFile` 실패 시 `.tmp` 정리.
- `tools/release.ps1`·`tools/version_diff/check_update.ps1`: PowerShell 5.1 stderr 안전 처리, `tools/sigcheck` `Directory.Build.props.user` 연동 및 `SameNameMethods` 중복 제거.

### 문서·테스트
- 단위 테스트 77건 → **94건**(`SourceHygieneTests` IL2CPP/MelonLoader 금지 패턴 6종 + `HarmonyDontPatchAll`, `PngDecoder`·`BmsParser`·`SxtgBmsConverter`·`PatchSignatureCheck`·`X64Decoder`·`TrackInfoParser` 회귀 테스트 추가).
- 전체 문서 감사 및 현행화(`HANDOVER_NOTES.md` `[CriticalPatch]` 클래스명 오류 정정, `CODE_STRUCTURE.md` 줄 수·테스트 건수 일치, `BMS_CONVERSION.md`·`CUSTOM_TRACKS.md`·`ARCHITECTURE.md`·`PLAY_OVERLAY.md`·`MEDIA_SYSTEM.md`·`BUILD_AND_DEPLOY.md`·`README.md`).

### 검증
- Release/Debug 빌드 경고/오류 0, xUnit 94건, `tools/bodycheck`(`--self-test` 포함) 통과, `tools/sigcheck` 통과 114·오류 0·경고 0·대체 해석 0, `tools/lambdascan` `ALL PASS`. 게임 실기는 미실행.

## [2.3.4] - 2026-11-24

### 성능
- `hwa\` 변경 감지 재스캔이 바뀐 곡 폴더만 다시 읽음: 폴더별 지문(파일 이름·크기·수정 시각)이 지난 스캔과 같으면 곡 정보·BMS를 다시 해석하지 않고 앨범을 재사용. 전에는 곡 하나만 추가해도 CUSTOM 탭 진입 순간 메인 스레드에서 모든 곡을 다시 해석했음.
- 자켓 캐시 조회의 Unity `!= null`(IL2CPP `op_Equality`) 제거(곡 목록 스크롤 중 커스텀 곡 행마다 2회).

### 수정
- 커스텀 곡 자켓 그림을 바꿔도(같은 파일명 포함) 게임을 다시 켜야 반영되던 한계: 자켓 캐시 키에 파일 크기·수정 시각(`CustomAlbumInfo.JacketStamp`)을 넣어 CUSTOM 탭 재진입(재스캔) 뒤 새로 읽음.
- 곡 폴더를 읽지 못하면 조용히 건너뛰던 것을 `[CustomTrackManager] 폴더 읽기 실패` 경고로.
- `tools/release.ps1`: 출력을 `2>&1`/파이프로 받으면 `git push`의 정상 stderr를 오류로 보고 "태그 푸시 실패"로 로컬 태그를 지우던 문제(Windows PowerShell 5.1). 종료 코드로만 판정. 모드 DLL은 바뀌지 않음.

### 검증
- Release/Debug 빌드 경고/오류 0, xUnit 77건, `tools/bodycheck` 통과, `tools/sigcheck` 통과 114·오류 0·대체 해석 0, `tools/lambdascan` `ALL PASS`. 게임 실기는 미실행.

## [2.3.3] - 2026-11-23

### 업데이트 내성
- 게임 파일 지문에 `loaderVersion`(MelonLoader·Il2CppInterop 어셈블리 버전) 추가. 로더가 바뀌면 인터롭 어셈블리가 다시 만들어져 게임 파일이 그대로여도 동작이 달라질 수 있으므로, "이 PC 실기 확인" 기록을 이어받지 않고 위험 변경으로 경고. 옛 기록에 없는 이 키는 비교하지 않아(선택 키) 업데이트만으로 기존 확인 기록이 사라지지 않음.
- CUSTOM 카테고리 판정을 `GetTracks`와 같은 대소문자 무시 비교로 통일(`GotoPlayScene`·`CategorySelectController.LoadData`·`GetSelectableCategoty`). 업데이트로 카테고리 문자열 대소문자가 달라져도 커스텀 곡이 공식 곡으로 처리되거나 탭이 두 번 생기지 않음.

### 성능
- `CustomNoteSkin.Apply`: 풀 노트마다 Unity `==`/`!=`(IL2CPP `op_Equality`) 3회 → 관리 null 비교.

### 추가
- `tools/bodycheck`: 게임 DLL 없이 모드 본체를 net6.0으로 컴파일해 .NET 7+ 전용 API·모드 내부 이름 오타·문법 오류를 잡는 검사(Roslyn, 공개 NuGet의 MelonLoader/HarmonyX/Il2CppInterop 참조). 게임 인터롭이 없어 나는 오류와 그 연쇄 오류는 거름. CI에서 단위 테스트 다음에 실행하고 `--self-test`로 검사기 자체도 확인. 모드 DLL은 바뀌지 않음.
- `tools/release.ps1`: csproj 버전 기준으로 빌드·테스트·시그니처 대조 후 zip을 만들고, `-Publish`면 태그 `v<버전>`과 GitHub 릴리즈(zip 업로드, CHANGELOG 항목을 노트로)를 만듦.
- 단위 테스트 74건 → 77건(선택 키 무시·로더 버전 변경 위험 판정·필수 키 유지).

### 검증
- Release/Debug 빌드 경고/오류 0, xUnit 77건 통과, `tools/sigcheck` 통과 114·오류 0·경고 0·대체 해석 0, `tools/lambdascan` `ALL PASS`. 게임 실기는 미실행.

## [2.3.2] - 2026-11-23

### 업데이트 내성
- 인자 형식을 지정한 `[HarmonyPatch]` 대상이 업데이트로 인자가 늘거나 형식이 바뀌어 없어지면, 같은 이름 메서드가 하나뿐이고 패치 인자와 맞을 때만 골라 직접 붙임(`PatchSignatureCheck.PickChangedSignature`). 전에는 필수 패치(`GetTracks`·`GotoPlayScene`·`Parse`·`Play`)도 그대로 빠졌음.
- 대체 해석(오버로드 고름·바뀐 시그니처 고름·이름/개수 대체)이 하나라도 있으면 `업데이트 점검 결과`가 "정상" 대신 실기 확인을 요청.
- `tools/sigcheck`도 같은 해석을 재현(`시그니처변경` 줄, `바뀐 시그니처로 고름 N`).

### 성능
- 오버레이: Unity 객체 `== null`(IL2CPP `op_Equality`)을 그리기 호출마다 하던 것을 관리 플래그로, 캔버스 생존·화면 크기·배율 조회를 30프레임마다로(키뷰어를 켜면 프레임당 약 50회 → 0회).
- 판정바 라벨 문자열을 판정마다가 아니라 그릴 때 한 번, 모양 캐시 null 비교를 관리 비교로.
- 노트 스폰(`CustomNoteSkin`/`RandomNoteSkin`)·`NoteEffects.OnFrame`·곡 목록 행 자켓 훅의 Unity null 비교 제거, 공식 곡 행은 `thumbnail` 필드를 읽지 않음.

### 추가
- 단위 테스트 71건 → 74건(`PickChangedSignature`·`SameNameMethods`).
- `docs/03-development/REVIEW_2026-11-23.md`.

### 검증
- Release/Debug 빌드 경고/오류 0, xUnit 74건 통과, `tools/sigcheck` 통과 114·오류 0·경고 0·대체 해석 0. 게임 실기는 미실행.

## [2.3.1] - 2026-11-22

### 변경
- 이유 없이 예외를 삼키던 빈 `catch { }` 12곳을 로그·예외 필터·이유 주석으로 정리 (마라톤 Esc 입력 오류는 한 번만 경고).
- `PngDecoder`에 픽셀 상한(`MaxPixels`, 4천만 픽셀) 추가: 아주 큰 PNG는 해독하지 않고 Unity `LoadImage`로 넘김(작업 스레드 메모리 부족 방지).

### 추가
- 단위 테스트 57건 → 71건: `PngDecoder`, `AtomicFile`, 소스 규칙(`SourceHygieneTests`: 빈 `catch` 금지, `csproj`/`MelonInfo` 버전 일치).
- `docs/03-development/REVIEW_2026-11-22.md`.

### 검증
- Release 빌드 경고/오류 0, xUnit 71건 통과. 게임 실기는 미실행.
- 게임 빌드 `26455233` (ver 1.1.007) 기준.

## [2.3.0] - 2026-11-18

- `Core.cs`를 `Features/CustomBga.cs`·`CustomJackets.cs`·`Unlock.cs`로 분리, `hwa\` 변경 감지 재스캔.
- `csproj` `<GameDir>`(환경변수 `SIXTAR_GAME_DIR`)와 빌드 후 `Mods\` 자동 복사, xUnit 테스트와 GitHub Actions CI.
- `AGENTS.md` 슬림화, `BmsParser` 변속·변박 지원, 자켓 디코딩 상한 1024.
- 이후 2026-11-19~20: 패치 시그니처 대조(`PatchSignatureCheck`), 오버로드 자동 선택, `AtomicFile`, 업데이트 지문 통합.
