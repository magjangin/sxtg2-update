# 코드 구조

`mod/` 20개 파일, 3,820줄 (2026-10-30). 모듈 관계는 [ARCHITECTURE.md](../00-overview/ARCHITECTURE.md), 훅 목록은 [HOOK_TABLE.md](../02-systems/HOOK_TABLE.md).

| 파일 | 줄 | 역할 |
| --- | ---: | --- |
| `Core.cs` | 794 | 진입점. CUSTOM 탭, 플레이 진입, 차트 주입, 곡 끝, BGA, 자켓, 프리뷰, 해금, 오프라인 가드 |
| `Managers/ModConfig.cs` | 454 | 설정 파일. 절반 이상이 기본 템플릿 문자열 |
| `Features/PlayAssist.cs` | 269 | 판정 세션, AutoPlay, AllPerfect(+불빛 생략), 점수, 기록 차단 |
| `Managers/CustomTrackManager.cs` | 250 | hwa 스캔, 차트 텍스트 캐시, TrackData 복제 |
| `Features/Overlay.cs` | 217 | uGUI 즉시모드 그리기, `Box`, `Rgba` |
| `Managers/MediaOverrideManager.cs` | 211 | 자켓 비동기/캐시, RGBA 업로드, BGA URL |
| `Features/NoteEffects.cs` | 184 | 속도 카오스, 히든/페이드, 동적 패치 |
| `Managers/CustomAudioManager.cs` | 179 | FMOD Core 스트림 |
| `Loaders/SxtgBmsConverter.cs` | 164 | sxtg2 → 게임 BMS |
| `Loaders/PngDecoder.cs` | 158 | 관리 코드 PNG 디코더 |
| `Features/JudgmentBar.cs` | 154 | 판정바 |
| `Features/KeyViewer.cs` | 142 | 키뷰어 |
| `Features/RandomNoteSkin.cs` | 142 | 랜덤 노트 스킨 |
| `Features/CustomMarathon.cs` | 138 | 마라톤 |
| `Features/CustomNoteSkin.cs` | 93 | PNG 노트 스킨 |
| `Loaders/BmsParser.cs` | 88 | BPM, 곡 길이 |
| `Loaders/TrackInfoParser.cs` | 62 | info.txt |
| `Features/CustomMusicSelect.cs` | 51 | 목록 복귀 위치 |
| `Features/AttendancePopup.cs` | 42 | 출석 창 생략 |
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
