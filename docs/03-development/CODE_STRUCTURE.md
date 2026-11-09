# 코드 구조

`mod/` 25개 파일, 6,465줄 (2026-11-09 갱신). 모듈 관계는 [ARCHITECTURE.md](../00-overview/ARCHITECTURE.md), 훅 목록은 [HOOK_TABLE.md](../02-systems/HOOK_TABLE.md).

| 파일 | 줄 | 역할 |
| --- | ---: | --- |
| `Features/OfflineBypass.cs` | 1015 | 완전 오프라인·서버 종료 우회 (리뉴얼 BACKND 42개 + `sxtg2 saver test` 레거시 24개, 총 66개 훅) |
| `Core.cs` | 810 | 진입점. CUSTOM 탭, 플레이 진입, 차트 주입, 곡 끝, BGA, 자켓, 프리뷰, 해금 |
| `Managers/LambdaTranslator.cs` | 598 | 이중래퍼 람다 번역가: O(1) Fast-Path 캐시 + x64 기계어 변위 스마트 구조 번역 |
| `Managers/PatchResilience.cs` | 558 | 업데이트 내성: 클로저·코루틴 동적 탐색, 오타 대응, 런타임 ICF 스텁 가드, Expression 복제기 |
| `Managers/ModConfig.cs` | 454 | 설정 파일. 절반 이상이 기본 템플릿 문자열 |
| `Features/PlayAssist.cs` | 273 | 판정 세션, AutoPlay, AllPerfect(+불빛 생략), 점수, 기록 차단, 종료 시 리소스 리셋 |
| `Managers/CustomTrackManager.cs` | 270 | hwa 스캔, 차트 텍스트 캐시, TrackData 복제, BMS 헤더 폴백 |
| `Features/Overlay.cs` | 226 | uGUI 즉시모드 그리기, `Box`, `Rgba` |
| `Managers/MediaOverrideManager.cs` | 214 | 자켓 비동기/캐시, RGBA 업로드, BGA URL, 실패 시 텍스처 즉시 파괴 |
| `Features/NoteEffects.cs` | 195 | 속도 카오스, 히든/페이드, 동적 패치, 씬 종료 시 자동 언패치 및 정리 |
| `Managers/CustomAudioManager.cs` | 191 | FMOD Core 스트림 재생/정지, 공식 곡 전환 시 스트림 자동 해제 |
| `Loaders/SxtgBmsConverter.cs` | 167 | sxtg2 → 게임 BMS |
| `Features/RandomNoteSkin.cs` | 165 | 랜덤 노트 스킨 |
| `Features/JudgmentBar.cs` | 161 | 판정바 (히스토리 수명 관리 및 플레이 종료 시 리셋) |
| `Loaders/PngDecoder.cs` | 158 | 관리 코드 PNG 디코더 |
| `Managers/UpdateGuard.cs` | 150 | Steam 매니페스트 빌드 확인 및 Unity 내부 예외 포워딩 |
| `Features/KeyViewer.cs` | 142 | 키뷰어 |
| `Loaders/TrackInfoParser.cs` | 142 | info.txt / trackinfo.txt (한국어/영어/중국어/일본어 키, `:`/`：`, 첫 값 채택, 부제·채보자 키 무시), `NormalizeMetadataText` |
| `Features/CustomMarathon.cs` | 141 | 마라톤 |
| `Loaders/BmsParser.cs` | 110 | BPM, 곡 길이, BMS 헤더(#TITLE, #ARTIST) 자동 추출 |
| `Features/CustomNoteSkin.cs` | 98 | PNG 노트 스킨 |
| `Loaders/TextDecoder.cs` | 89 | 텍스트 인코딩 판별: BOM → 엄격 UTF-8 → 코드 페이지 순차(곡 정보·BMS) |
| `Features/AttendancePopup.cs` | 59 | 출석 창 생략 |
| `Features/CustomMusicSelect.cs` | 51 | 목록 복귀 위치 |
| `Models/CustomAlbumInfo.cs` | 28 | 앨범 정보 |

## 코드 규칙

- **패치 클래스 하나 = 훅 하나.** `[HarmonyPatch(typeof(대상), nameof(메서드))]`를 단 `public static class`. 이름은 `{대상}_{메서드}_Patch`.
- **대상은 `typeof`로.** `AccessTools.TypeByName` 금지 (노란 경고).
- **패치 전에 ICF 확인.** `script.json`에서 주소 중복 개수를 셉니다 ([IL2CPP_PITFALLS.md](IL2CPP_PITFALLS.md)).
- **매 프레임 코드에 Unity 값 형식 헬퍼 금지.** `Box`, `Overlay.Rgba`, `MathF`만.
- **자주 일어나는 로그는 `Core.DebugLog`.** `DebugLog=1`일 때만 찍힙니다. 처음 한 번만 알릴 것은 플래그로 막습니다.
- **주석은 "왜"를 씁니다.** 어떤 함수를 왜 이 방식으로 건드렸는지, 무엇이 실패했는지.

## 새 기능 넣는 순서

1. `il2cpp_dump/dump.cs`에서 클래스·메서드·RVA 찾기
2. `pe_slice` + REA로 원본 동작 확인 ([REVERSE_ENGINEERING.md](REVERSE_ENGINEERING.md))
3. ICF 확인
4. 설정이 필요하면 `ModConfig`에 키 추가 + `TEMPLATE_VERSION` 올리기 + 템플릿에 설명
5. 빌드, 배포, 게임에서 확인, 로그 확인
6. `AGENTS.md`와 이 `docs/`에 기록
