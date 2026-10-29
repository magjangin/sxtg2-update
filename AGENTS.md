# Sixtar Gate STARTRAIL IL2CPP Modding Handover & Technical Specification

## 1. 대상 게임 정보 & 버전 고정
- **게임명:** Sixtar Gate: STARTRAIL
- **Steam AppID:** `1802720`
- **Steam Build ID:** `26218612` (고정)
- **Unity 버전:** `2022.3.50f1`
- **IL2CPP 메타데이터 버전:** `v31` (31.1)
- **모드 로더:** MelonLoader 0.7.3 (.NET 6 런타임)

---

## 2. 런타임 백엔드 & 디컴파일/분석 경로
- **런타임 백엔드:** Unity IL2CPP (x64)
- **메타데이터 덤프:** `H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\il2cpp_dump`
  - `dump.cs` (32MB), `script.json` (99MB), `il2cpp.h` (56MB), `DummyDll/`
- **Cpp2IL DummyDll:** `H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\cpp2il_out` (93개 DLL, `--skip-analysis`. 원래 형제 폴더 `Sixtar Gate STARTRAIL test\`에 잘못 출력돼 있던 것을 옮김)
- **ilspycmd 스켈레톤 소스:** `H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\src_dummy` (총 2,039개 `.cs` 파일)
- **Interop 어셈블리:** `H:\steam\steamapps\common\Sixtar Gate STARTRAIL\MelonLoader\Il2CppAssemblies`
  - `Assembly-CSharp.dll`, `Il2Cppmscorlib.dll`, `UnityEngine.CoreModule.dll` 등

---

## 3. 설계 의도 및 우회 전략 (Rationale)
1. **REA 대용량 바이너리 타임아웃 우회 (`pe_slice` 기법):**
   - 70MB에 달하는 `GameAssembly.dll` 원본을 Ghidra/REA로 통째로 열면 분석 타임아웃이 발생함.
   - `pe_slice.bat`을 사용하여 관심 클래스(`SixtarGate.BMSConverter`, `SixtarGate.MusicSelect.TrackSelectController`, `SixtarGate.MusicSelect.CategorySelectViewer`)만 독립 조각 PE로 분리 추출 후 REA Ghidra로 분석하여 수초 내에 완벽한 의사코드 확보.
2. **독립 "CUSTOM" 카테고리 격리 탭 생성:**
   - 기존 "ALL TRACKS"나 "Original" 등의 공식 카테고리를 오염시키지 않고, 상단 카테고리 셀렉터에 완전히 독립된 **"CUSTOM"** 카테고리 탭을 생성.
   - `CategorySelectController.Categories` 및 `RhythmGameUtils.GetSelectableCategoty`에 "CUSTOM"을 등록하고, `TrackSelectController.GetTracks("CUSTOM")` 호출 시에만 커스텀 곡(`Starting Point (Custom 1-Note)`) 1곡을 제공.
   - ALL TRACKS 및 기타 공식 카테고리는 공식 곡만 순수하게 유지되어 완벽한 GUI 분리 및 단축키 배제 달성.
3. **서버 의존성 격리 및 오프라인 보존 (Offline Guard):**
   - 게임 구동 및 플레이 결과 화면에서 서버(독자 HTTP API 및 뒤끝 BACKND SDK)와 지속적으로 통신을 시도함.
   - `ServerMaintenanceNotice::Show`를 Prefix 단계에서 억제하여 점검 중 또는 오프라인 상태에서도 타이틀 강제 종료를 차단.
   - `ManagerWebRequest::PostHighScoreData` 및 `BackEndRankingService+TravelHighScoreRequest::Start`를 Prefix 단계에서 차단(`return false`)하고 클라이언트 내부 성공 콜백(`onComplete?.Invoke(1)`)을 에뮬레이션하여, 연출/결과창은 정상 진행되되 외부 서버로는 일체 패킷이 전송되지 않도록 보호.
4. **완전한 클라이언트 전곡/DLC 해금 (Full Unlock):**
   - 곡과 DLC 해금 검증이 `ItemUnlockChecker` 및 `TrackLoader.TrackData.get_IsOwned`에 집중되어 있음.
   - `ItemUnlockChecker.IsUnlocked`, `ItemUnlockChecker.IsDLCUnlocked`, `ItemProvideModule.IsOwned`, `TrackLoader+TrackData.IsOwned`를 일괄 `true`로 오버라이드하여 서버 검증 없이 전곡 및 DLC 해금 구현.

---

## 4. 핵심 후킹 타깃 및 커스텀 에셋 주입 파이프라인
상세 목록은 `docs/hooks.md` 참조.

1. **독립 "CUSTOM" 카테고리 탭 생성:**
   - **타깃:** `Il2CppSixtarGate.MusicSelect.CategorySelectController::LoadData()` (RVA `0x605740`) 및 `Il2CppRhythmGame.RhythmGameUtils::GetSelectableCategoty()` (RVA `0x6B72C0`)
   - **역할:** 카테고리 탭 목록에 "CUSTOM" 문자열 추가. 화면 렌더링 시 `CategorySelectViewer.GetCategoryDisplayName`이 자동으로 대문자 "CUSTOM"으로 표시함.
2. **"CUSTOM" 카테고리 전용 트랙 격리 주입:**
   - **타깃:** `Il2CppSixtarGate.MusicSelect.TrackSelectController::GetTracks(string category)` (RVA `0x617CF0`)
   - **역할:** `category == "CUSTOM"`일 때만 템플릿(Starting Point) 기반 `TrackLoader.TrackData` 커스텀 곡(`Starting Point (Custom 1-Note)`) 1곡만 포함된 리스트를 반환 (`return false`). "ALL TRACKS"나 다른 카테고리 요청 시에는 원래 함수를 실행하여 커스텀 곡이 전혀 섞이지 않음.
3. **커스텀 BMS 차트 파싱 및 주입 (리뉴얼 플레이 파이프라인 대응):**
   - **타깃:** `Il2CppSixtarGate.SixtarGlobal::GotoPlayScene(ITrack track, ELevels level, EPlayStyle playStyle)` (RVA `0x7DBD20`), `Il2CppSixtarGate.MikazukiConverter::Parse(string content)` (RVA `0x7F0F20`), `Il2CppSixtarGate.BMSConverter::Parse(string content)` (RVA `0x7EDB00`)
   - **역할:**
     - 구버전 씬(`ManagerPlayOld`)이 아닌 현재 버전 리뉴얼 플레이 씬(`Renewal_Play` / `PlayManager`)으로의 진입을 `SixtarGlobal.GotoPlayScene` Prefix에서 정확히 감지하여 `IsCustomTrackActive = true` 활성화.
     - `Renewal_Play` 진입 시 `PatternConverter.Initialize`가 `MikazukiConverter.Parse`를 호출할 때 Prefix로 가로채, `BMSConverter.Parse`를 실행하여 1노트 BMS(`#BPM 160\n#00111:01000000\n`)로 파싱된 `Sixtarithm.Pattern` 객체를 반환.
     - 원본 템플릿(Starting Point)의 BGM 및 MP4 BGA는 그대로 유지하면서 인게임 차트만 첫 마디 1개 블루 노트로 교체 주입.
4. **All-Perfect 판정 조작:**
   - **타깃:** `Il2Cpp.JudgeCounter::AddJudge(EJudge judge)` (RVA `0x59B2B0`)
   - **역할:** 노트를 칠 때 판정 등급 누적. `judge = EJudge.BlueStar`로 고정 시 올 퍼펙트 달성 가능.
5. **전곡 & 모든 DLC 해금:**
   - **타깃:** `ItemUnlockChecker::IsUnlocked`, `ItemUnlockChecker::IsDLCUnlocked`, `TrackLoader.TrackData::get_IsOwned`, `ItemProvideModule::IsOwned`
   - **역할:** 항상 `true` 반환으로 모든 DLC 및 해금곡 즉시 플레이 가능.
6. **오프라인 팝업 억제 & 스코어 전송 차단:**
   - **타깃:** `ServerMaintenanceNotice::Show`, `ManagerWebRequest::PostHighScoreData`, `TravelHighScoreRequest::Start`
   - **역할:** 점검 화면 스킵 및 변조 스코어 서버 업로드 차단.

7. **인게임 조기 클리어 방지 & 곡 전체 완주 보장 (FinishProcessor Hook):**
   - **타깃:** `Il2CppSixtarGate.Play.FinishProcessor::Initialize(IPatternPlayer player)` (RVA `0x63FE10`)
   - **역할:**
     - 1-노트 차트는 최대 틱이 1개 노트(약 0.5초)뿐이므로, 원본 로직상 `LatestNoteTiming`이 0.5초로 잡히고 `TrackEndTiming`이 초기화되지 않아(0초), `0.0 + 0.0 + 3.0f(_finishOffset) <= patternTime` 조건에 의해 노트를 치고 3초 만에 `OnFinished`가 발동되어 클리어 씬으로 강제 전환되는 버그가 발생함.
     - `FinishProcessor.Initialize` Postfix에서 선택된 앨범의 실제 곡 길이(`album.Duration`)에 맞춰 `TrackEndTiming`을 동적으로 확장 설정. 검증 완료 후 주기적 디버그 로그는 제거하여 최적화.
8. **`sxtg2` 핵심 로직 이식 & 미디어 에셋 오버라이드 (v2.0.0):**
   - **타깃:** `Il2CppSixtarGate.Play.BackgroundUIProcessor::Initialize(string trackID)` (RVA `0x63CD80`), `Il2CppSixtarGate.MusicSelect.TrackInfoViewer::Set(ITrack track, TrackSelectArgs args)` (RVA `0x634CB0`), `Il2CppSixtarGate.MusicSelect.TrackObject::SetTrack(ITrack track)` (RVA `0x636620`), `CustomTrackManager`, `BmsParser`, `TrackInfoParser`, `MediaOverrideManager`
   - **역할:**
     - `{게임폴더}\hwa\` 하위 앨범 폴더를 자동 스캔하여 `trackinfo.txt`/`info.txt`, `chart.bms`(`hwa2.bms` 등), `music.ogg`/`.mp3`/`.wav`, `video.mp4`/`bg.mp4`, `thumbnail.png`/`thumb.png`/`jacket.png`를 자동 매핑.
     - "CUSTOM" 카테고리 진입 시 스캔된 모든 앨범을 `TrackData` 리스트로 변환하여 GUI에 등록 (제목, 작곡가, BPM, 난이도 반영).
     - 곡 선택 화면(Music Select)에서 가운데 메인 정보창 큰 자켓(`TrackInfoViewer.Set`)을 로컬 PNG 스프라이트로 렌더링. (우측 목록은 썸네일 슬롯이 없음 — 아래 12번 참조)
     - 곡 진입 시 실제 BMS 텍스트를 `MikazukiConverter.Parse`로 주입하고, `BackgroundUIProcessor.Initialize` Postfix에서 `VideoPlayer` URL을 로컬 MP4 경로로 교체하여 인게임 BGA 오버라이드 및 자켓 썸네일 교체 수행.

9. **커스텀 곡 프리뷰/인게임 음원 (FMOD Core 스트림, v2.2):**
   - **타깃:** `Il2CppSixtarGate.SoundModuleForFMOD::Play(string audioName, ISoundModule.SoundChannel channel, float position)` (RVA `0x7E11E0`) Prefix → `Managers/CustomAudioManager.cs`
   - **원리:** 곡 선택은 `ISoundModule.Play("preview_" + trackID, BGM)`, 플레이 씬은 `Play(trackID, Game)`. 원본은 `event:/{audioName}` Studio 이벤트를 만드는데 커스텀 ID는 없어 `EventNotFoundException`(무음). → `RuntimeManager.CoreSystem.createStream(path, CREATESTREAM|LOOP_OFF)` + `playSound(master group)`으로 직접 재생, 볼륨 `GetVolume(channel) * CurrectionVolume`.
   - 이 채널은 `_soundChannels`(EventInstance 사전) 밖에 있으므로 `Stop`/`Pause`/`Resume`/`Resume(time,ch)`/`GetTimelinePosition` Prefix·Postfix로 함께 구동(원본 5개 모두 ContainsKey 가드, 고유 주소 확인). `GetGamePlayAudioLength`(Game 키 없으면 0 반환)는 Postfix로 앨범 길이 반환.
   - 프리뷰 파일 우선순위: `demo.*` → `music.*` → 첫 오디오 파일. 경로는 `Path.GetFullPath`.
   - 볼륨 추종: `SceneLoader.<FadeIn>`은 `CurrectionVolume`을 1→0, `<FadeOut>`은 0→1로 매 프레임 바꾸고(인터페이스 슬롯 1), 옵션 변경은 `ChangeVolume`(RVA `0x7DF860`)을 탐. 원본은 둘 다 `_soundChannels` 이벤트에만 `setVolume`하므로 `set_CurrectionVolume`(RVA `0x7E27B0`)·`ChangeVolume` Postfix에서 Core 채널에도 `GetVolume(ch) * CurrectionVolume`을 다시 적용(둘 다 고유 주소). 이게 없으면 곡 선택 진입 페이드 중에 시작된 프리뷰가 페이드 중간 볼륨으로 고정돼 작게 들림. (`GetVolume` 자체는 원본 이벤트와 동일한 계산.)
10. **플레이 씬 진입 시 FMOD 뱅크/패턴 조회 우회:**
   - **타깃:** `SixtarGlobal.<>c__DisplayClass54_0::<GotoPlayScene>b__12(ISixtarAssetBundle ab)` (RVA `0x7EB2E0`) Prefix
   - **원리:** 원본 흐름은 `b__12` → `ab.LoadAsset<TextAsset>(trackID)` → `b__13`(`RuntimeManager.LoadBank(ta)`, `args.BankName = ta.name`, `SXGTBIN_{pattern}` 조회) → `b__14`(`args.Pattern = ta.text`, BGM Stop, `MoveScene("Renewal_Play")`). 커스텀 ID는 뱅크 TextAsset이 null이라 `LoadBank`에서 NRE → 로딩 화면 정지. 커스텀이면 `args._BankName_k__BackingField = trackID`만 넣고 `b__14(new TextAsset(bms))`를 직접 호출.
11. **sxtg2 형식 BMS 자동 변환 (`Loaders/SxtgBmsConverter.cs`):**
   - 게임 `BMSConverter.GetLaneIndex`(RVA `0x7ED300`) 채널표(10진): 일반 `11 LL, 12 L, 18 R, 19 RR, 14 Gate, 13 LT, 15 RT`, 롱 `51 52 58 59 54 53 55`(같은 레인), `56 → 99(GateAlarm)`. 값 `00/0A/04`는 무시, 나머지 값은 전부 마커. 롱노트는 같은 5x 채널의 연속 마커 쌍(`CreateLongMarker`가 채널별 buffer로 짝지음). 없는 채널(예: 16)은 `KeyNotFoundException`.
   - sxtg2 차트는 레인을 채널(`16 LL, 11 L, 12 R, 13 RR, 14 LT, 15 RT, 18 GATE`, `RhythmGame.LaneIndexOld` 순서)로, 종류를 값(`01` 일반, `02`/`03` 롱 시작/끝, `04`/`05` OPEN 시작/끝)으로 표현 → 위 표로 재배치. 채널 16 또는 값 02/03/05가 있고 19·5x가 없으면 sxtg2로 판정. `CustomTrackManager.LoadChartText`에서 일괄 적용.
   - **3자리 차트(sxtg2 확장):** `#WAV` 키가 3글자(`#WAV001`, `#WAV010 music.ogg`)면 데이터 값도 3글자씩(`001` 일반, `002`/`003` 롱, `010` BGM). sxtg2 `BmsParser`는 앞의 `0`을 떼서 해석(앞이 `0`이 아니면 무시). `SxtgBmsConverter.NormalizeValueWidth`가 변환 전에 2글자 값·`#WAVxx`로 다시 써서 변환기와 게임 `BMSConverter`(둘 다 2글자 단위)가 같은 노트를 보게 함. 모드 `Loaders/BmsParser`(곡 길이/BPM)는 원래부터 3글자 지원.
12. **곡 선택 목록(우측) 썸네일:** 리뉴얼 목록 프리팹(`SelectedTrack`)에는 자켓 Image가 **없음**(`TrackObject.thumbnail` 필드 null, 계층 덤프로 확인 — BG/Locked/이름/랭크/클리어램프/즐겨찾기만 존재). 공식 곡도 목록에 썸네일이 안 나오는 게 원래 디자인. 표시하려면 행마다 Image를 새로 생성해야 함(미구현).

13. **인게임 BGA (URL 재생) — `BackgroundUIProcessor` 전면 대체:**
   - 원본 BGA 경로는 VideoClip 전용: `Initialize` → `Track/{id}` 번들 → `<Initialize>b__2(VideoClip)`이 `bgaPlayer.clip = vc`(커스텀은 null, 이 대입이 source를 VideoClip으로 되돌려 URL을 지움) + `Prepare()`. `PlayBGA()`/`PauseBGA()`/`IsVideoPlayable`은 모두 `clip == null`이면 즉시 return → URL 영상은 영원히 재생 안 되고 자켓(`jacket` GameObject, PlayBGA 안에서만 꺼짐)이 계속 덮음.
   - 대응: `b__2` Postfix에서 URL 재적용(`clip=null` → `source=Url` → `url` 순서), `IsVideoPlayable` getter Postfix true, `PlayBGA`/`PauseBGA` Prefix에서 원본 로직(_isPlayStarted / _isReservedPause / _reservedDelayTime / jacket 숨김)을 clip 체크만 빼고 재현.
14. **플레이 흐름 자켓 (로딩/일시정지/결과/플레이 배경):** 전부 `Track/{id}` 번들에서 `{id}_jacket`/`Thumbnail` 스프라이트를 비동기 로드해 콜백에서 대입 → 커스텀은 null(흰 박스). 각 콜백 Postfix에서 `ApplyCurrentAlbumJacket` 재적용.
   - `SixtarGate.PlayLoading.PlayLoadingManager.__c__DisplayClass34_0._OnLoadResource_Travel_b__2/_b__4` (jacketFull / trackThumbnail)
   - `SixtarGate.Play.Pause.TrackInfoViewerModule.Set` Postfix + 0.1/0.4/1.0초 뒤 재적용 코루틴 (콜백 `_Set_b__3_1`은 ICF 공유라 패치 금지)
   - `SixtarGate.Result.TrackInfoModule.__c__DisplayClass13_0._Show_b__3/_b__4`, `SixtarGate.Result.TravelBackgroundModule.__c__DisplayClass5_0._InitializeModule_b__1`
   - `SixtarGate.Play.BackgroundUIProcessor.Initialize` Postfix + 재적용 코루틴 (콜백 `<Initialize>b__1`은 ICF 공유라 패치 금지)
   - PlayLoading `b__2/_b__4`는 OuterSpace 판(`DisplayClass35_0`, 같은 필드 배치)과 공유 — 레이아웃이 같아 안전.
   - 로딩 영상(`_specifiedLoadingClip`, `b__3`)은 게임 기본 영상 유지.
15. **Harmony 적용 방식:** `[assembly: HarmonyDontPatchAll]` + 클래스별 `CreateClassProcessor(type).Patch()` (하나 실패해도 나머지 적용, 로그에 `All patches applied successfully! (N)`).

16. **곡 종료 시점 (`FinishProcessor.UpdatePatternTime`, RVA `0x6401A0`):** 종료 조건은 `TrackEndOffset + TrackEndTiming + _finishOffset(3s) <= patternTime`. `Initialize` Postfix로 `TrackEndTiming`을 넣어도 플레이 시작 직후 게임이 `TrackEndTiming = 0.3`으로 다시 덮어씀(실측 로그). `set_TrackEndTiming`은 11개 함수와 접힌(ICF) 코드라 패치 금지 → `UpdatePatternTime` Prefix에서 `offset + timing < 곡 길이`면 `timing = 곡 길이 - offset`.
   - **곡 길이 = max(`album.Duration`, Game 채널 스트림 길이):** `album.Duration`은 BMS 마지막 노트 + 3초라, 음원/BGA가 마지막 노트보다 긴 곡(하얀 그리움: 노트 237.5초, 음원 257.8초)은 노트가 끝나고 3초 뒤 결과로 넘어갔음. `CustomAudioManager.GetStreamLength(Game)`(FMOD `Sound.getLength`)을 함께 씀.
17. **설정 파일 `{게임}\SaveCustomKey\config.txt` (`Managers/ModConfig.cs`):** sxtg2와 같은 key=value 형식(1/0, 켜짐/꺼짐, #RRGGBB(AA), R,G,B,A, 색 이름). 플레이 시작마다 파일 변경 시각을 확인해 재로딩. 구현된 키: AutoPlay, AllPerfect, BlockSave, MaxScore, EnableJudgmentBar, JudgmentBarVertical/Shape/RangeShape/Side, EnableKeyViewer, KeyViewer*Color, CustomNotes, RandomNoteSkin(v4), ScoreInvertCountdown(v5). `ConfigVersion`이 템플릿보다 낮으면 값을 유지한 채 새 템플릿으로 다시 쓰고 `config.txt.bak` 보존. 나머지 sxtg2 키(NoteSway, NoteSpeedChaos, Sudden/Hidden, FadeIn/Out, CustomMarathon, LaneCoverDebug)는 무시(미이식).
   - **NoteSway를 이식하지 않는 이유 — 게임 내장 `NoteShake` 옵션:** `NoteObject.GetPosX`/`set_HeadPosition`(RVA `0x67EC40`/`0x67F470`)이 `ISettingModule.GetOption<MultipleOperationsOption>("NoteShake")` 값이 `"Disable"`이 아니면 x 오프셋 = `sin(head/1000 * freq) * amp * sign`을 적용함(노트마다 처음 한 번 `freq = Random(2.22, 6.66)`, `amp = Random(20, 44)`, `sign = ±1`. head가 판정선까지의 거리라 판정선에서 0으로 모임). 옵션 정의(`sharedassets0.assets`): `NoteShake`, OnOff, `0 = Enable / 1 = Disable`, 곡 시작 전 옵션 썸네일 `StandbyThumb_NoteShake`. 저장 위치 `%USERPROFILE%\AppData\LocalLow\Lyrebird Studio\Sixtar Gate STARTRAIL\options`(`NoteShake=1`이 꺼짐). 만우절(4/1) 날짜 검사는 `TitleManager.Start`/`PlayManager.<Start>d__10`에서 Steam 업적 `APRIL_FOOLS_START`/`APRIL_FOOLS_COMPLETE`를 줄 때만 쓰고, 옵션을 날짜로 숨기는 코드는 없음(`DateTime.get_Month` 호출부 전수 확인).
   - **만우절 테마/플레이 변경은 이 빌드에 없음:** 만우절 흔적은 위 업적 2개, `NoteShake` 옵션, 프로필 아이콘 아이템 `aprilfools2022_icon`(Addressables `Assets/Sprites/icon/aprilfools2022_icon.png`, 구버전 `ManagerMusicSelect.<>c.<Start>b__78_9`에서 이름 비교) 뿐. 문자열 리터럴·dump.cs·Addressables 카탈로그·`_Data` 비번들 파일에서 april/fool 검색 결과 이외 없음. 모드 셀렉트 테마(`ThemeChangeController`, Xmas/Wacca/Touhou 등)는 장착 아이템이고 날짜 분기 없음. 4/1로 날짜를 속여도 Steam 업적만 풀림(외부 전송이라 하지 말 것).
   - **2023 만우절 이벤트("라미의 반란", EA 0.2.039 / 빌드 2001069, 유튜브 `Xt33lOEwDSI`):** 모드 셀렉트가 낙서풍 테마로 바뀌고 라미를 눌러 이벤트 곡 `It's RAMIx`(당시 SOLAR ★3 / LUNAR ★1)로 진입. 현재 빌드에는 낙서 테마가 **없음** — 빌드 씬 목록(`globalgamemanagers`: ModeSelect는 Default/2021Xmas/CrossingDelta/FlowerAndDestiny/Touhou01/LuminousAndDarkness/Wacca/kalpa뿐), Addressables 키, level/sharedassets에서 doodle/crayon/sketch/april 검색 결과 없음(`Rami` 문자열은 L&D 모드 셀렉트·MusicSelect·AdventureTitle의 캐릭터 이름). 곡은 남아 있음: 번들 `itsramix_assets_all_*.bundle`(뱅크 `itsramix.bytes`, 자켓, SXGTBIN `04/07/11/14`(SOLAR), `03_L/06_L/11_L/14_L`(LUNAR) — 당시와 난이도 구성이 다름), `Previews.bank`에 프리뷰. 곡 목록은 서버 테이블 `Data_Track`(`ITableLoader`) + `TrackLoader.HideTracks`(해금 아이템 기반)로 결정.
18. **플레이 보조 (`Features/PlayAssist.cs`) — 리뉴얼 판정 파이프라인 `SixtarGate.Play.JudgeProcessor`:**
   - 세션 시작: `JudgeProcessor` **생성자 패치는 이 빌드에서 절대 호출되지 않음** → `Judge()`/`OnTickEvent`/`SetPressedState` 첫 호출에서 인스턴스 포인터가 바뀌면 지연 초기화(판정 범위 `_range._timings` ms 읽기, `add_OnNoteJudged` 구독).
   - AutoPlay: `Judge()` Prefix에서 `IsAutoJudge = true` (게임 내장 `ProcessAutoJudge`, judgeID 0 / delta 0). `set_IsAutoJudge`는 44개와 ICF라 호출만, 패치 금지.
   - AllPerfect: `OnTickEvent(JudgeResult)` Prefix에서 `result.judgeID = 0`(BlueStar). 히트·오토·`Missbreak`(judgeID 3) 모두 이 함수를 거쳐 `TickJudgeList`에 쌓이고 `OnNoteJudged`로 방송됨 → 콤보/점수/결과 모두 반영.
   - **AllPerfect 레인 불빛 버그(수정):** `OnNoteJudged` → `GearObject.PlayNoteEffect`(RVA `0x6772E0`) → `NoteEffectObject.Play(judge)`는 그 레인에 RedStar(3) 이펙트 항목이 없으면 아무것도 안 함 → 원래 게임은 안 누른 롱노트 몸통 틱(`JudgeLongTick`: `_pressedState`가 false면 3)·놓친 머리(`CheckMissBreak`/`Missbreak`)에 불빛이 없음. AllPerfect가 이걸 0으로 바꾸면 BlueStar 이펙트가 재생돼, 롱노트를 안 누르고 있어도 틱마다 레인이 눌린 것처럼 번쩍임. → `OnTickEvent` Prefix에서 원래 값이 3이었는지 기억(Postfix에서 해제)하고 `PlayNoteEffect` Prefix에서 그 경우만 건너뜀(점수/콤보는 BlueStar 유지). `OnNoteJudged` → 기어까지는 동기 호출(`IngameDataNotifier.Notify` → `NoteJudgedObserver.Notify`). `NoteJudgedObserver.Notify`(`0x67EA90`)는 `InputEventArgs.Invoke`와 ICF 공유라 패치 금지.
   - MaxScore: `ScoreProcessor.UpdateScore`가 `floor(fraction/notes*1000000.0)`을 네이티브 상수로 계산(IL 트랜스파일 불가) → Prefix에서 `OnScoreUpdate`/`OnReverseScoreUpdate` 델리게이트를 스케일 래퍼로 바꿨다가 Postfix에서 원복.
   - MaxScore + 게임 옵션 `ScoreInvert`(점수 반전): `GearObject.SubscribeObserver`(RVA `0x678FE0`)가 `ReverseScoreObserver`를 인라인으로 만들며 `score.targetNumber = 1000000f`(상수) + `StartUpdater(0,0)` → 첫 노트 전까지 100만점 표시. Postfix에서 target이 1000000이면 `MaxScore`로 바꾸고 `StartUpdater(0,0)` 재호출. (`ScoreObserver`/`ReverseScoreObserver` 생성자는 ICF 공유 `0x6862B0`라 패치 금지.)
   - **게임의 점수 반전 값 = 1000000 − 잃은 점수**(실측: 퍼펙트로 score 442→885→1328일 때 reverse는 계속 1000000). 즉 원래 게임은 전부 BLUESTAR면 만점에서 줄지 않음. `UpdateScore`는 `OnReverseScoreUpdate`를 `OnScoreUpdate`보다 **먼저** 부름. config `ScoreInvertCountdown=1`(기본, v5)이면 reverse 델리게이트를 no-op으로 바꿔 두고 Postfix에서 원래 reverse에 `만점 − 이번 점수`를 넘겨, 얻은 점수만큼 줄어 곡 끝에 0이 되게 함.
   - BlockSave: `PlayRecordModule.RecordPlayData/RecordCoursePlayData/RecordLicensePlayData` Prefix 차단. AutoPlay/AllPerfect/MaxScore≠1000000이면 설정과 무관하게 항상 차단.
   - `JudgeResult.deltaTime = inputTime - noteTime` (음수 = 빠름). sxtg2와 반대라 판정바는 부호를 뒤집어 +가 FAST.
19. **판정바/키뷰어 오버레이 (`Features/Overlay.cs`, `JudgmentBar.cs`, `KeyViewer.cs`):** 이 게임 빌드는 IMGUI(`GUI.DrawTexture`/`GUI.Label`, `GUIStyle(GUIStyle)`)가 스트리핑되어 `Method unstripping failed` → uGUI `Canvas`(ScreenSpaceOverlay, sortingOrder 32000) + 풀링된 `Image`/`Text`(LegacyRuntime 폰트)로 즉시모드 렌더링, `OnLateUpdate`에서 그림. 씬이 `Renewal_Play`가 아니게 되면(`OnSceneWasLoaded`) 숨김.
   - 키뷰어 눌림 상태: `JudgeProcessor.SetPressedState(int lane, bool)` Postfix (LaneIndex 순서 LL,L,R,RR,Gate,LT,RT; 표시 순서 LT LL L GATE R RR RT, Lunar는 LL L GATE R RR). 키 이름: 리뉴얼 플레이 씬에는 `InputListener`가 **없음**(실측) → 현재 레인 이름 표시. 실제 키 이름은 리뉴얼 입력 경로 추가 분석 필요.
21. **곡 선택 화면 커스텀 곡 보정 (`Features/CustomMusicSelect.cs`):**
   - **결과 → 곡 목록 복귀 위치:** `SelectTrack`(RVA `0x619510`)이 `"LastSelectedTrack"` 플래그에 TrackID를 저장하고, `LoadData`(RVA `0x618500`)는 그 ID를 `AllTracks`(공식 곡만)에서 찾아 `SelectedTrack`에 넣음 → 커스텀 ID는 못 찾아 첫 곡. `RefreshTrackList`는 `SelectedTrack.TrackID`로 현재 목록에서 커서를 잡으므로, `SelectTrack` Postfix/`GotoPlayScene` Prefix로 마지막 커스텀 ID를 기억하고 `LoadData` Postfix에서 CUSTOM 목록의 같은 `ITrack`을 `SelectedTrack`에 넣음. 게임 재시작 후에는 기억이 없어 첫 곡(원래 동작).
20. **커스텀 노트 스킨 (`Features/CustomNoteSkin.cs`):** `{게임}\CustomNotes\*.png` (Blue/Red/Gate, Tail/BlueTail/RedTail, Hold/BlueHold/RedHold). 훅 `StartrailPatternRenderer.GetNoteObejct(LaneIndex, Transform)` Postfix — 노트 풀이 레인별(`Dictionary<LaneIndex, Queue<NoteObject>>`)이라 lane 인자가 곧 노트 레인. `NoteObject.headImage`(머리) / `bodyImage`·`holdImage`(롱 몸통) / `tailImage`(롱 끝)에 sprite 대입. sxtg2의 인벤토리 "커스텀 노트" 항목은 미이식(config 토글만).
22. **랜덤 노트 스킨 (`Features/RandomNoteSkin.cs`, config v4 `RandomNoteSkin`):** `IngameUIRenderer.notePackage.noteObjects`(`NotePackage`, `sharedassets0.assets`)에 모든 스킨의 노트 프리팹이 `{skin}`(파랑) / `{skin}_red` / `{skin}_gate`로 들어 있음(default, button01, cosmicSymphony, fkns, gothic, pianist, reincarnationOfKalpa, snowflake, stellar, voyager, voyagerorigin, wacca 등 + 이펙터 `barline`/`startline`/`gatealarm`). 레인별 프리팹은 `SetupNoteReferance`/`GetNoteReferance(lane)`가 `{스킨옵션}` + `GetNoteTypeName(lane)`(NoteTypeLane* 옵션 → `""`/`_red`/`_gate`)로 고름. `StartrailPatternRenderer.GetNoteObejct` Postfix(우선순위 Low)에서 그 레인 프리팹의 접미사와 같은 무작위 스킨 프리팹의 head/body/hold/tail 하위 `Image`들(sprite·type·color·material·preserveAspect)을 순서대로 복사. 풀 재사용 시에도 스폰마다 다시 입힘. 켜면 `CustomNotes`는 건너뜀. 스킨 목록은 `IngameUIRenderer` 포인터가 바뀔 때(플레이마다) 다시 만듦.

---

## 5. 시도했다가 실패한 것들 (Dead Ends / Failed Attempts)
- **IL2CPP 동일 코드 접기(ICF) 주의 — 패치 전 반드시 확인:** 내용이 같은 메서드들이 하나의 네이티브 주소를 공유함. 공유 주소를 패치하면 Postfix가 엉뚱한 클래스의 `this`로 실행됨(필드 오프셋 오독 → 크래시/오작동). 실제 사례: `TrackInfoViewerModule._Set_b__3_1`(11개 공유: 프로필 아이콘·언락 리스트 등), `BackgroundUIProcessor.<Initialize>b__1`(인벤토리 썸네일 2개와 공유), `NoteObject.SetTickData`(102개), `JudgeProcessor.set_IsAutoJudge`(44개), `FinishProcessor.set_TrackEndTiming`(11개), `GearObject.NoteJudgedObserver.Notify`(`InputEventArgs.Invoke`와 공유). 확인법: `il2cpp_dump/script.json`의 `ScriptMethod[].Address` 중복 개수 세기.
- **로컬 음원을 `SoundModuleForFMOD.PlayLocalSound`(programmer sound 이벤트)로 재생:** 그 이벤트 타임라인이 약 3분 고정이라 긴 곡은 중간부터 무음, 그 길이가 `GetGamePlayAudioLength`로 새어 186초에 결과 화면 강제 전환. → FMOD Core `createStream`으로 교체.
- **`JudgeProcessor` 생성자 Harmony 패치:** 적용 에러는 없지만 한 번도 호출되지 않음. → 인스턴스 메서드에서 지연 초기화.
- **IMGUI(`OnGUI`)로 판정바/키뷰어 그리기:** `Method unstripping failed`가 매 프레임 발생. → uGUI 캔버스.
- **자켓 PNG 동기 디코딩 (`ImageConversion.LoadImage`):** 1080p PNG 하나에 메인 스레드 50~100ms. CUSTOM 탭 진입 시 우측 목록 행(썸네일 슬롯 없음)용 진단 폴백이 10장을 한꺼번에 디코딩해 0.75초 멈춤 + 곡 이동마다 끊김. → 목록 행 폴백 제거, 가운데 자켓은 `MediaOverrideManager.RequestAlbumSprite`(UnityWebRequestTexture 비동기 디코딩, 콜백) + 탭 진입 시 `PreloadAll` 백그라운드 순차 로드. 플레이 흐름 화면은 캐시된 sprite를 동기 조회.
- **매 프레임·매 노트 훅 상시 부착:** `NoteObject.set_HeadPosition/set_NoteLength`는 화면의 모든 노트에 대해 매 프레임 호출됨 → IL2CPP 디트로 비용+래퍼 할당. 속도 카오스/히든/페이드를 쓸 때만 `Register` Postfix에서 `HarmonyInstance.Patch`, 아니면 `Unpatch`.
- **루틴 로그 남발:** 곡 선택마다 5~6줄(TrackInfoViewer.Set ×2, 자켓, 프리뷰 Play/Streaming, LoadImage)이 콘솔 출력 비용까지 유발. → `Core.DebugLog`(config `DebugLog=1`일 때만 출력).
- **uGUI 풀 재사용 시 그리기 순서:** 풀링된 Image/Text를 호출 순서대로 재사용하면 렌더 순서(형제 순서=생성 순서)와 어긋나, 나중에 생성된 반투명 키 박스가 키 글자를 덮어 글자가 회색↔흰색으로 깜빡임. → Rect/Label 호출마다 `SetAsLastSibling()`.
- **곡 선택 MAX COMBO(`---`) 직접 채우기:** `TrackInfoViewer.CalcTotalCombo`(RVA `0x6344E0`)는 `"---"`를 쓰고 `MaxComboLoader`가 곡 번들 패턴을 읽어 `RefreshMaxCombo`로 채우는데, 커스텀은 번들이 없어 `---`. Prefix에서 BMS → `StartrailPattern` → `SixtarTickFactory.Interprete(p, null)` → `TickCount` 합으로 채워 봤으나 실기에서 표시되지 않아 제거(원인 미확인).
- **`InputListener.Instance.playActionMap`으로 키 이름 읽기:** 리뉴얼 플레이 씬엔 InputListener 자체가 없음.
- **BGA를 `BackgroundUIProcessor.Initialize` Postfix에서 URL로만 교체:** 비동기 `b__2`가 나중에 `clip = null`을 대입해 URL을 덮어쓰고, `PlayBGA`가 clip null이라 재생 자체를 안 함 → 영상 안 나옴(로그상 "Overrode BGA"는 찍혀서 착각하기 쉬움).
- **`TrackObject.LoadThumbnail` 패치:** IL2CPP 빌드에서 `SetTrack`에 인라인되어 있어 패치가 절대 호출되지 않음. 비동기 콜백은 `TrackObject.__c__DisplayClass18_0._LoadThumbnail_b__1(Sprite)`이며, 이마저 `thumbnail == null`이면 로드 자체를 건너뜀(리뉴얼 목록이 그 경우).
- **`HarmonyInstance.PatchAll` 수동 호출 + MelonLoader 자동 패치 중복:** 모든 훅이 2번씩 실행됨(로그가 같은 타임스탬프로 2줄). 반환 false Prefix가 두 번 돌아 `b__14`(씬 이동)가 두 번 호출되며 로딩이 멈춤. → `[assembly: HarmonyDontPatchAll]` 추가.
- **BGA URL `"file://" + 상대경로`:** `AppDomain.BaseDirectory` 기반 경로가 `hwa\...` 상대경로로 남아 VideoPlayer가 `*_Data/hwa/...`에서 찾음("WindowsMediaFoundation received empty file"). → `HwaFolderPath`와 BGA URL을 `Path.GetFullPath`로 절대경로화.
- **게임 실행 중 DLL 덮어쓰기:** `Mods/SixtarGateMod.dll`은 실행 중 잠김(`Device or resource busy`). 게임 종료 후 복사.
- **"ALL TRACKS" 카테고리에 무차별 주입 시도:** `AllTrackSelector.GetList`에 직접 주입하면 ALL 목록에 공식 곡들과 섞여서 들어가며, 독립 카테고리 구분이 되지 않아 사용성이 저하됨. -> `CategorySelectController.Categories`에 "CUSTOM" 탭을 별도 등록하고 `TrackSelectController.GetTracks("CUSTOM")`으로 분기하여 완벽 격리.
- **`BMSConverter.Parse` 중복 가로채기로 인한 마디 축소:** `MikazukiConverter.Parse`에서 85마디짜리 BMS를 생성했으나, 하위 `BMSConverter.Parse` Prefix에서 `#00111:01000000`로 다시 덮어쓰는 바람에 차트 길이가 1마디(1.5초)로 축소되고 스타트 라인(StartLine)과 노트가 겹치는 현상 발생 -> `BMSConverter.Parse`에서도 85마디 보존 및 노트 위치를 2번 마디(`#00211:01000000`)로 분리.
- **노트 연주 후 3초 만에 클리어 화면 강제 전환:** `FinishProcessor.TrackEndTiming`이 0으로 남아있어 `_finishOffset(3.0s)` 경과 시 즉시 `OnFinished` 격발 -> `FinishProcessor.Initialize` Postfix로 `TrackEndTiming = 125.0f` 강제 연장.
- **`pe_slice` 클래스 지정 누락:** 네임스페이스를 생략하고 `--class BMSConverter`를 주면 실패함. 반드시 `--class SixtarGate.BMSConverter`와 같이 네임스페이스를 완전히 명시해야 함.
- **`Cpp2IL` 단독 실행 시 콘솔 멈춤 현상:** Cpp2IL은 예외나 완료 시 키보드 Enter 입력을 기다리므로, 자동화 스크립트 실행 시 반드시 `< NUL` 리디렉션을 붙여야 행(hang) 현상을 방지할 수 있음.
- **REA `open_binary` 다중 프로바이더 충돌:** 공급자 ID 미지정 시 ambiguous provider 에러 발생하므로 `provider_id: "ghidra"`를 반드시 명시해야 함.

---

## 6. 빌드 명령 및 배포 경로
- **모드 프로젝트 경로:** `H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\mod\SixtarGateMod.csproj`
- **빌드 명령:**
  ```powershell
  & "C:\Program Files\dotnet\dotnet.EXE" build "H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\mod\SixtarGateMod.csproj"
  ```
- **배포 경로:**
  ```powershell
  Copy-Item -Path "H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\mod\bin\Debug\net6.0\SixtarGateMod.dll" -Destination "H:\steam\steamapps\common\Sixtar Gate STARTRAIL\Mods\SixtarGateMod.dll" -Force
  ```
- **설정/스킨 폴더:** `{게임}\SaveCustomKey\config.txt`, `{게임}\CustomNotes\` (sxtg2 Mono 설치본 `H:\Sixtar Gate STARTRAIL custom mode`에서 복사해 옴)
- **검증 로그:** `H:\steam\steamapps\common\Sixtar Gate STARTRAIL\MelonLoader\Latest.log`
- **Unity 예외 로그(게임 내부 NRE/FMOD 오류는 여기에만 찍힘):** `%USERPROFILE%\AppData\LocalLow\Lyrebird Studio\Sixtar Gate STARTRAIL\Player.log` (이전 실행은 `Player-prev.log`)
- **게임 실행 중에는 Mods DLL이 잠김** → 게임 종료 후 복사.
