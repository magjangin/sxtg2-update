# Sixtar Gate STARTRAIL IL2CPP 모딩 진행 상황

- 대상 게임: Sixtar Gate STARTRAIL
- 대상 실행 파일: `H:\steam\steamapps\common\Sixtar Gate STARTRAIL\Sixtar Gate STARTRAIL.exe`
- 백엔드: Unity IL2CPP (Unity 2022.3.50f1, metadata v31)
- 설치된 로더: MelonLoader 0.7.3 (.NET 6 기반)

---

## 단계별 상태
- [완료] 1. IL2CPP 메타데이터 덤프 — `H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\il2cpp_dump` — Il2CppDumper로 dump.cs(32MB), script.json(99MB), DummyDll\ 생성 완료
- [완료] 2. 시그니처 탐색 — `H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\src_dummy` — ilspycmd로 DummyDll\Assembly-CSharp.dll 전체 역공학 완료 (2,039개 소스 파일)
- [완료] 3. 네이티브 로직 분석 — `H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\slices\SixtarGate_BMSConverter.dll`, `logs\BMSConverter_Parse.rea.c`, `logs\JudgeCounter.asm`, `docs\hooks.md` — pe_slice 추출, REA 의사코드 및 어셈블리 덤프 확보, 후킹 후보 표 정리 완료
- [완료] 4. 모드 스켈레톤 구축 — `H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\mod\SixtarGateMod.csproj` — MelonLoader 0.7.3 .NET 6 모드 프로젝트 생성 및 dotnet build 빌드 성공
- [완료] 5. 실행 검증 — `H:\steam\steamapps\common\Sixtar Gate STARTRAIL\MelonLoader\Latest.log` — 게임 구동 후 Mods\SixtarGateMod.dll 정상 로드 확인 완료
- [완료] 6. 서버 가드 & 전곡/DLC 해금 모듈 구현 및 검증 — `mod\Core.cs` v1.1.0 — 점검 팝업 억제, 점검/오프라인 중 서버 스코어 업로드 차단(더미 성공 반환), ItemUnlockChecker/TrackData 전곡 및 DLC 완전 해금 적용 및 게임 내 정상 초기화 검증 완료
- [완료] 7. 독립 "CUSTOM" 카테고리 탭 생성 및 1-노트 커스텀 트랙 주입 — `mod\Core.cs` v1.5.0, `Mods\SixtarGateMod.dll` (19KB) — 단축키 완전 제거(100% GUI 조작), `CategorySelectController.Categories` 및 `RhythmGameUtils.GetSelectableCategoty`에 "CUSTOM" 탭 추가, `TrackSelectController.GetTracks("CUSTOM")`으로 오직 커스텀 탭에서만 1-노트 커스텀 트랙(`Starting Point (Custom 1-Note)`) 노출 및 ALL/공식 카테고리와 완벽 격리 성공.
- [완료] 8. 리뉴얼 플레이(`Renewal_Play` / `PlayManager`) 차트 주입 파이프라인 연결 — `mod\Core.cs` v1.5.1, `Mods\SixtarGateMod.dll` (21KB) — `SixtarGlobal.GotoPlayScene`에서 커스텀 트랙 선택 감지(`IsCustomTrackActive = true`), `PatternConverter.Initialize` -> `MikazukiConverter.Parse` 호출 시 Prefix에서 가로채 `BMSConverter.Parse`로 1-노트 `Sixtarithm.Pattern` 직접 주입 완료. 원본 BGM + BGA 정상 유지.
- [완료] 9. 인게임 조기 클리어 방지 & 곡 전체 완주 보장 — `mod\Core.cs` v1.5.3, `Mods\SixtarGateMod.dll` (22KB) — `FinishProcessor.Initialize` Postfix 후킹으로 `TrackEndTiming = 125.0f` 강제 연장, 2번 마디(`#00211:01000000`) 블루 노트 주입으로 스타트 라인 겹침 해결 및 Starting Point 음원 전체 완주 검증 완료. 디버깅 로그 정리 및 프로덕션 빌드 배포 완료.
- [완료] 10. `sxtg2` 핵심 로직 완전 이식 (Custom Chart & Media Loader v2.0.0) — `mod\Loaders\BmsParser.cs`, `mod\Loaders\TrackInfoParser.cs`, `mod\Models\CustomAlbumInfo.cs`, `mod\Managers\CustomTrackManager.cs`, `mod\Managers\MediaOverrideManager.cs`, `mod\Core.cs` v2.0.0, `Mods\SixtarGateMod.dll` (30KB) — `{게임폴더}\hwa\` 앨범 폴더 자동 스캔, `trackinfo.txt`/`info.txt` 메타데이터 및 다중 BMS/BME/BML 차트 파싱, "CUSTOM" 탭 동적 트랙 리스트 주입, 인게임 실제 커스텀 BMS 파싱 주입, 차트 길이에 맞춘 `FinishProcessor.TrackEndTiming` 동적 자동 연장, 곡 선택 화면(Music Select) 자켓(`TrackInfoViewer.Set`) 및 리스트 썸네일(`TrackObject.SetTrack`) 다중 탐색(필드 리플렉션 + 계층 검색) 및 진단 로거 탑재, `BackgroundUIProcessor` 훅을 통한 인게임 커스텀 BGA(.mp4) 및 자켓 썸네일(.png) 오버라이드 지원 완료. 빌드 및 배포 완료.
- [완료] 11. 커스텀 곡 미디어 전 구간 실기 검증 (v2.1.0, 2026-10-29) — `mod\Managers\CustomAudioManager.cs`, `mod\Loaders\SxtgBmsConverter.cs`, `mod\Core.cs` — 곡 선택 프리뷰 음악(FMOD `PlayLocalSound`), 플레이 진입 시 FMOD 뱅크/SXGTBIN 조회 우회, sxtg2 BMS → 게임 BMSConverter 채널 자동 변환(1,131 마커), 인게임 음악(Game 채널), 인게임 BGA(URL 재생 + PlayBGA/PauseBGA 대체), 로딩/일시정지/결과/플레이 배경 자켓 교체, Harmony 이중 적용 버그 수정. 우측 곡 목록은 게임 디자인상 썸네일 슬롯 없음(미구현). 상세는 `AGENTS.md` 4장 9~15, 5장.
- [완료] 12. sxtg2 플레이 보조 기능 이식 (v2.2.0, 2026-10-29) — `mod\Managers\ModConfig.cs`, `mod\Features\PlayAssist.cs`, `Overlay.cs`, `JudgmentBar.cs`, `KeyViewer.cs`, `CustomNoteSkin.cs`, `mod\Managers\CustomAudioManager.cs`(FMOD Core 스트림으로 교체) — `SaveCustomKey\config.txt` 읽기, AllPerfect/BlockSave/MaxScore, 판정바·키뷰어(uGUI 오버레이), 곡 끝까지 BGM 재생·정상 종료 실기 확인. AutoPlay와 커스텀 노트 스킨은 구현만 되고 실기 미확인, 키뷰어 키 이름은 레인 이름으로 대체(리뉴얼 입력 경로 미분석). 상세는 `AGENTS.md` 4장 16~20, 5장.
- [진행] 13. sxtg2 남은 기능 + 성능 정리 (2026-10-29) — `Features\NoteEffects.cs`(노트 속도 카오스, 히든, 페이드 인/아웃), `Features\CustomMarathon.cs`, `Loaders\PngDecoder.cs`(자켓 백그라운드 디코딩), config v3 자동 마이그레이션(`config.txt.bak` 보존), `DebugLog` 로그 레벨. 실기 확인: 키뷰어 실제 키 이름(S D F SPC J K L), CUSTOM 탭 자켓 경고 없음. 미확인: 속도 카오스/히든/페이드/마라톤/AutoPlay/커스텀 노트 스킨 실제 화면. 노트 흔들림·서든은 게임 기본 설정 사용(이식 안 함).
- [진행] 14. 커스텀 곡 프리뷰 볼륨 추종 (2026-10-29) — `Managers\CustomAudioManager.cs` — 씬 전환 페이드(`CurrectionVolume`)·볼륨 옵션 변경을 Core 스트림에도 적용. 빌드·배포 완료, 실기 미확인. (결과 화면 판정 통계 패널은 만들었다가 사용자 요청으로 제거.)
- [진행] 19. 3자리 차트(`#WAV001`) 지원 (2026-10-29) — `Loaders\SxtgBmsConverter.cs` `NormalizeValueWidth` — 3글자 노트 값을 2글자로 정규화 후 변환. 스크래치 콘솔로 2자리 차트와 같은 결과 확인, 빌드·배포 완료, 실기 미확인(메갈로바니아 차트 파일 미확인).
- [진행] 18. 점수 반전 + MaxScore 시작값 (2026-10-29) — `PlayAssist.cs` `GearObject_SubscribeObserver_Patch` — 점수 반전 시 첫 노트 전 100만점으로 시작하던 문제(실기 로그로 200만 시작 확인). 이어서 config v5 `ScoreInvertCountdown`: 얻은 점수만큼 줄어 곡 끝에 0(게임 원래는 잃은 점수만 빠짐). 카운트다운은 빌드·배포 완료, 실기 미확인.
- [진행] 17. 랜덤 노트 스킨 (2026-10-29) — `Features\RandomNoteSkin.cs`, config v4 `RandomNoteSkin` — 노트마다 게임 내 노트 스킨 무작위 적용. 빌드·배포 완료, 실기 미확인(스킨별 하위 Image 구성 차이·애니메이터가 있으면 일부 어긋날 수 있음).
- [진행] 16. 커스텀 곡 종료 시점을 음원 길이 기준으로 (2026-10-29) — `Core.cs` `CustomSongLength`, `CustomAudioManager.GetStreamLength` — 마지막 노트 뒤 음원이 긴 곡이 일찍 끝나던 문제. 빌드·배포 완료, 실기 미확인.
- [진행] 15. 곡 선택 커스텀 곡 보정 (2026-10-29) — `Features\CustomMusicSelect.cs` — 결과 화면에서 돌아올 때 플레이한 커스텀 곡으로 커서 복귀(MAX COMBO 계산은 실기에서 안 나와 제거). 빌드·배포 완료, 실기 미확인. 상세는 `AGENTS.md` 4장 21.
- [진행] 20. AllPerfect 안 누른 롱노트 불빛 수정 (2026-10-30) — `Features\PlayAssist.cs` `GearObject_PlayNoteEffect_Patch` — AllPerfect가 RedStar(미스) 틱을 BlueStar로 바꿔 레인 히트 이펙트가 눌린 것처럼 켜지던 문제. 원래 미스였던 틱은 이펙트만 생략. 빌드·배포 완료, 실기 미확인.
- [완료] 21. 성능·코드 정리 + README (2026-10-30) — 오버레이 값 캐시/managed 기하, 앨범 `Has*` 디스크 접근 제거, 차트 텍스트 캐시, 노트 효과·랜덤 스킨 캐시, 죽은 구버전 훅 7개 제거, 로더 축소(4,217 → 3,746줄). 파서는 기존 구현과 실제 차트 15개 결과 동일. 빌드·배포 완료. 상세는 `AGENTS.md` 4장 23.
- [완료] 22. 출석 보상 팝업 자동 표시 끄기 (2026-10-30) — `Features\AttendancePopup.cs`, config v6 `SkipAttendancePopup=1` — 모드 선택 진입 시 매일 뜨던 출석 보상 창 생략(보상은 안 받은 채로 남음). 실기 로그 확인(`[Attendance] Skipped ...`).
- [완료] 23. 곡 목록 자켓 복구 (2026-10-30) — 21번 정리 때 `TrackObject.SetTrack`/`<LoadThumbnail>b__1` 훅을 잘못 지워 CUSTOM 목록 자켓이 빈 칸이 됨(사용자 화면으로 확인) → 복구. 22번 출석 창 생략은 실기 로그로 동작 확인(`[Attendance] Skipped ...`).
- [완료] 24. 문서 묶음 `docs/` (2026-10-30) — sxtg2 커스텀 리드미 구성을 따라 00-overview / 01-user-guide / 02-systems / 03-development / 04-game-reference / 90-archive. 훅 표는 소스에서 생성. 결과 화면 특수 연출(시이 대신 Rath·Christmas_2022)과 모드 선택 7종을 게임 데이터로 확인. UnityPy 스크립트 `tools/datadump/`.
- [완료] 25. FAQ (2026-10-30) — `docs/01-user-guide/FAQ.md`: 이번 세션 질문(올 퍼펙트 불빛, 난이도 4/5, DLC·스킨 짝, button01 정정, 결과 화면 시이 특수 곡, 모드 7종, 출석 창, 목록 자켓, 노란 줄) 정리.
- [완료] 26. 전체 병목 점검·성능 개선 및 문서 최신화 (2026-10-31) — 비플레이 씬 `OnLateUpdate` 얼리 리턴 + `Overlay.HideAll()`(비플레이 매 프레임 IL2CPP 호출 제거, 마라톤 Esc 중단 시 배너 잔류 버그 수정), `Overlay.Item` 초기 `Active = true` 동기화, `SxtgBmsConverter`·`BmsParser` 루프 내 `Substring` 할당 제거, `ModConfig`/`MediaOverrideManager`/`Overlay`의 잔여 Unity 값 형식 생성자(`new Color(...)`, `new Rect(...)`, `new Vector2(...)`, `Mathf.Clamp01`)를 순수 managed 필드 초기화로 통일, `csproj` 버전 `2.2.0` 동기화, `AGENTS.md`/`docs/` 최신 상태 반영.
