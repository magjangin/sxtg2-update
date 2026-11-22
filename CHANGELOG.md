# 변경 이력

형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)를 따르고, 버전은 `mod/SixtarGateMod.csproj`의 `<Version>`입니다. 상세 기록은 `docs/CURRENT_STATUS.md`와 `docs/03-development/REVIEW_*.md`.

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
