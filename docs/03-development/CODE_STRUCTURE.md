# 코드 구조

`mod/` 34개 파일, 9,022줄 (2026-11-19 갱신). 순수 모듈 테스트는 `tests/SixtarGateMod.Tests/`에 있습니다. 모듈 관계는 [ARCHITECTURE.md](../00-overview/ARCHITECTURE.md), 훅 목록은 [HOOK_TABLE.md](../02-systems/HOOK_TABLE.md).

| 파일 | 줄 | 역할 |
| --- | ---: | --- |
| `Features/OfflineBypass.cs` | 1007 | 완전 오프라인·서버 종료 우회 (리뉴얼 BACKND 42개 + `sxtg2 saver test` 레거시 24개, 총 66개 훅) |
| `Managers/LambdaTranslator.cs` | 955 | 이중래퍼 람다 번역가: 캐시 직행(RVA 대조) + 후보 수집·거르기(바깥 형식 람다, 캡처 필드, 소유자) + 기계어 대조로 번역, 알려진 클로저 안의 람다(`TranslateMethodInType`) |
| `Managers/PatchResilience.cs` | 682 | 업데이트 내성: 클로저·코루틴 동적 탐색, 오타 대응, ICF 가드(빈 스텁 + `IcfScanner` 공유 수), 캐시된 Expression 접근자·복제기 |
| `Core.cs` | 543 | 진입점. CUSTOM 탭, 플레이 진입, 차트 주입, 곡 끝, 프리뷰 |
| `Managers/ModConfig.cs` | 460 | 설정 파일. 절반 이상이 기본 템플릿 문자열 |
| `Managers/CustomTrackManager.cs` | 364 | hwa 스캔(작업 스레드, Harmony 적용과 겹침, 폴더당 1회 조회, 변경 감지 재스캔 `RefreshIfChanged`), 차트 텍스트 캐시, TrackData 복제, BMS 헤더 폴백 |
| `Managers/UpdateGuard.cs` | 364 | Steam 매니페스트 빌드 확인, 지난 실행 지문 비교·저장, 패치 후 점검 결과 한 줄, Unity 내부 예외 포워딩·개수 세기, 커스텀 곡 결과 화면 도달 시 실기 확인 기록 |
| `Loaders/BmsParser.cs` | 321 | BPM, 변속(03/08)·변박(02), 곡 길이, BMS 헤더(#TITLE, #ARTIST) Span 파싱 + 레인 버킷 정렬 |
| `Features/PlayAssist.cs` | 303 | 판정 세션, AutoPlay, AllPerfect(+불빛 생략), 점수, 기록 차단, 종료 시 리소스 리셋 |
| `Managers/X64Decoder.cs` | 292 | 최소 x64 명령 길이 해독기 + 명령이 덮는 레지스터 (capstone 대조: 람다 6,021개 명령 188,398개 불일치 0) |
| `Features/CustomJackets.cs` | 236 | 곡 선택(가운데·목록 행), 로딩, 일시정지, 결과 화면 자켓 오버라이드 및 지연 재적용 |
| `Loaders/SxtgBmsConverter.cs` | 235 | sxtg2 → 게임 BMS (`char[]` 슬롯·인덱스 순회, 무할당 Span 포맷 판별) |
| `Managers/MediaOverrideManager.cs` | 234 | 자켓 비동기/캐시, RGBA 업로드, BGA URL, 실패 시 텍스처 즉시 파괴 |
| `Managers/IcfScanner.cs` | 233 | 실행 중 ICF 검사: 훅 대상 주소를 공유하는 IL2CPP 메서드 수, `[IcfShared]`, 빌드별 캐시 `UserData/SixtarGateMod.IcfCounts.txt` |
| `Features/NoteEffects.cs` | 232 | 속도 카오스, 히든/페이드, 동적 패치, 씬 종료 시 자동 언패치 및 정리 |
| `Features/Overlay.cs` | 227 | uGUI 즉시모드 그리기, `Box`, `Rgba` |
| `Managers/PatchPreflight.cs` | 224 | 패치 적용 전 사전 JIT·토큰(`Method`/`Field`/`Type`/`Tok`) 검사, 서브그래프 메모이제이션, 부팅 후 캐시 정리 |
| `Features/RandomNoteSkin.cs` | 203 | 랜덤 노트 스킨 (프리팹 값 1회 읽기, 스폰 훅은 CustomNoteSkin과 공용) |
| `Managers/CustomAudioManager.cs` | 191 | FMOD Core 스트림 재생/정지, 공식 곡 전환 시 스트림 자동 해제 |
| `Features/KeyViewer.cs` | 167 | 키뷰어 |
| `Features/CustomNoteSkin.cs` | 166 | PNG 노트 스킨(작업 스레드 해독, 플레이 준비 때 업로드) + 노트 스폰 훅(`GetNoteObejct`, 랜덤 스킨 분기) |
| `Managers/BuildFingerprint.cs` | 164 | 지문(크기·PE 링크 시각·메타데이터 버전/헤더 해시) 비교·`SameBuild`·읽기·쓰기, 실기 플레이 판정 `VerifiedPlayTracker` 순수 함수 |
| `Features/JudgmentBar.cs` | 161 | 판정바 (히스토리 수명 관리 및 플레이 종료 시 리셋) |
| `Loaders/TrackInfoParser.cs` | 160 | info.txt / trackinfo.txt (한국어/영어/중국어/일본어 키, `:`/`：`, 첫 값 채택, 부제·채보자 키 무시), `NormalizeMetadataText` |
| `Loaders/PngDecoder.cs` | 158 | 관리 코드 PNG 디코더 |
| `Features/CustomMarathon.cs` | 153 | 마라톤 |
| `Managers/NativeLambdaScan.cs` | 141 | 람다 x64 본문에서 클로저 → 소유자 → 대상 필드 로드를 명령 단위로 레지스터를 따라 찾는 순수 함수 (`tools/lambdascan`으로 오프라인 시험) |
| `Features/CustomBga.cs` | 121 | 인게임 BGA URL 오버라이드 (`BackgroundUIProcessor` 초기화·콜백·PlayBGA/PauseBGA/IsVideoPlayable) |
| `Loaders/TextDecoder.cs` | 89 | 텍스트 인코딩 판별: BOM → 엄격 UTF-8 → 코드 페이지 순차(곡 정보·BMS) |
| `Managers/ModLog.cs` | 61 | 작업 스레드 로그를 모았다가 메인 스레드에서 출력(hwa 스캔용) |
| `Features/AttendancePopup.cs` | 59 | 출석 창 생략 |
| `Features/CustomMusicSelect.cs` | 51 | 목록 복귀 위치 |
| `Features/Unlock.cs` | 37 | 전곡 & DLC 클라이언트 해금 오버라이드 |
| `Models/CustomAlbumInfo.cs` | 28 | 앨범 정보 |

## 코드 규칙

- **패치 클래스 하나 = 훅 하나.** `[HarmonyPatch(typeof(대상), nameof(메서드))]`를 단 `public static class`. 이름은 `{대상}_{메서드}_Patch`.
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
