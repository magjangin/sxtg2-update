# 디버깅

## 로그 두 개

| 로그 | 보는 것 |
| --- | --- |
| `{게임}\MelonLoader\Latest.log` | 모드 로그 `[SixtarGateMod]`, 패치 수, 관리 코드 예외 |
| `%USERPROFILE%\AppData\LocalLow\Lyrebird Studio\Sixtar Gate STARTRAIL\Player.log` | 게임 내부 NRE, FMOD, 서버 통신(`[BACKND]`), `Debug.Log` |

게임의 `Debug.Log` 문자열(예: `receivable attendance reward!!!`)은 `script.json`의 `ScriptString`에서 찾을 수 있어서, 로그 한 줄로 그 코드를 거꾸로 찾아갈 수 있습니다.

## 모드 로그 접두사

| 접두사 | 출처 |
| --- | --- |
| `[Harmony]` | 패치 적용 결과 |
| `[Scene]` | 씬 로드 |
| `[CustomCategory]`, `[CustomChart]` | CUSTOM 탭, 차트 주입 |
| `[CustomTrackManager]` | hwa 스캔 |
| `[CustomAudio]` | FMOD 스트림 (일부는 DebugLog) |
| `[MediaOverride]`, `[Jacket]`, `[MusicSelect]` | 자켓·BGA (대부분 DebugLog) |
| `[FinishProcessor]` | 곡 끝 시점 |
| `[PlayAssist]` | 세션 시작, 기록 차단 |
| `[Overlay]`, `[KeyViewer]` | 오버레이 |
| `[NoteEffects]`, `[CustomNoteSkin]`, `[RandomNoteSkin]` | 노트 |
| `[Marathon]` | 마라톤 |
| `[OfflineGuard]` | 서버 차단 |
| `[Attendance]` | 출석 창 생략 |
| `[Config]` | 설정 파일 |

`config.txt`의 `DebugLog=1`이면 곡 선택·자켓·음원처럼 자주 일어나는 일도 찍힙니다. 콘솔 출력도 비용이라 평소에는 끕니다.

## 자주 쓰는 확인 방법

- **훅이 불리는지:** Prefix 첫 줄에 `Core.DebugLog`를 넣고 `DebugLog=1`.
- **두 번 불리는지:** 같은 타임스탬프에 같은 줄이 두 번 → Harmony 이중 적용([IL2CPP_PITFALLS.md](IL2CPP_PITFALLS.md) 5번).
- **엉뚱한 화면에서 불리는지:** ICF. `script.json`으로 주소 중복 확인.
- **값이 나중에 덮이는지:** 비동기 콜백. 콜백 Postfix나 재적용 코루틴.
- **성능:** 매 프레임 훅에서 Unity 값 형식 헬퍼, `GetComponent`, `FindObjectOfType`, 문자열 보간, `File.Exists`를 찾아봅니다.

## 게임을 고정된 상태로 시험하기

- 모드 선택 진입 시 출석 창: `SkipAttendancePopup`
- 서버 점검 중에도 실행: 오프라인 가드
- 기록을 더럽히지 않기: AutoPlay/AllPerfect/MaxScore를 켜면 저장이 자동 차단됩니다
