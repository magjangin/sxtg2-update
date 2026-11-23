# 변경 이력

형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)를 따르고, 버전은 `mod/SixtarGateMod.csproj`의 `<Version>`입니다. 상세 기록은 `docs/CURRENT_STATUS.md`와 `docs/03-development/REVIEW_*.md`.

## [Unreleased]

### 추가
- `tools/bodycheck`: 게임 DLL 없이 모드 본체를 net6.0으로 컴파일해 .NET 7+ 전용 API·모드 내부 이름 오타·문법 오류를 잡는 검사(Roslyn, 공개 NuGet의 MelonLoader/HarmonyX/Il2CppInterop 참조). 게임 인터롭이 없어 나는 오류와 그 연쇄 오류는 거름. CI에서 단위 테스트 다음에 실행하고 `--self-test`로 검사기 자체도 확인. 모드 DLL은 바뀌지 않음.
- `tools/release.ps1`: csproj 버전 기준으로 빌드·테스트·시그니처 대조 후 zip을 만들고, `-Publish`면 태그 `v<버전>`과 GitHub 릴리즈(zip 업로드, CHANGELOG 항목을 노트로)를 만듦.

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
