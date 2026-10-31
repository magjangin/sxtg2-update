# 훅 전체 표

`mod/` 소스의 `[HarmonyPatch]`를 읽어 만든 표입니다(2026-10-31, 정적 패치 클래스 117개 + `NoteEffects.cs` 동적 훅 2개). RVA는 `il2cpp_dump/script.json` 기준입니다.
`ICF×n`은 그 주소를 n개 함수가 공유한다는 뜻입니다([IL2CPP_PITFALLS.md](../03-development/IL2CPP_PITFALLS.md)).

## 1. 코어·커스텀 곡·오디오·플레이 보조·오버레이 (54개 + 동적 2개)

| 파일 | 대상 형식 | 메서드 | 종류 | RVA |
| --- | --- | --- | --- | --- |
| `Core.cs` | `BackgroundUIProcessor` | `Initialize` | Postfix | `0x63CD80` |
| `Core.cs` | `BackgroundUIProcessor` | `PauseBGA` | Prefix | `0x63D090` |
| `Core.cs` | `BackgroundUIProcessor` | `PlayBGA` | Prefix | `0x63D140` |
| `Core.cs` | `BackgroundUIProcessor` | `get_IsVideoPlayable` | Postfix | `0x63D460` |
| `Core.cs` | `BackgroundUIProcessor.__c__DisplayClass11_0` | `<Initialize>b__2` | Postfix | `0x654C40` |
| `Core.cs` | `CategorySelectController` | `LoadData` | Postfix | `0x605740` |
| `Core.cs` | `FinishProcessor` | `Initialize` | Postfix | `0x63FE10` |
| `Core.cs` | `FinishProcessor` | `UpdatePatternTime` | Prefix | `0x6401A0` |
| `Core.cs` | `ItemProvideModule` | `IsOwned` | Prefix | `0x7B7050` |
| `Core.cs` | `ItemUnlockChecker` | `IsDLCUnlocked` | Prefix | `0x7A4DE0` |
| `Core.cs` | `ItemUnlockChecker` | `IsUnlocked` | Prefix | `0x7A4EA0` |
| `Core.cs` | `MikazukiConverter` | `Parse` | Prefix | `0x7F0F20` |
| `Core.cs` | `Play.Pause.TrackInfoViewerModule` | `Set` | Postfix | `0x6A6120` |
| `Core.cs` | `PlayLoading.PlayLoadingManager` | `OnLoadResource_Travel` | Postfix | `0x51D650` |
| `Core.cs` | `PlayLoading.PlayLoadingManager.__c__DisplayClass34_0` | `<OnLoadResource_Travel>b__2` | Postfix | `0x525420` (ICF×2) |
| `Core.cs` | `PlayLoading.PlayLoadingManager.__c__DisplayClass34_0` | `<OnLoadResource_Travel>b__4` | Postfix | `0x5254A0` (ICF×2) |
| `Core.cs` | `Result.TrackInfoModule.__c__DisplayClass13_0` | `<Show>b__3` | Postfix | `0x509200` |
| `Core.cs` | `Result.TrackInfoModule.__c__DisplayClass13_0` | `<Show>b__4` | Postfix | `0x509230` |
| `Core.cs` | `Result.TravelBackgroundModule.__c__DisplayClass5_0` | `<InitializeModule>b__1` | Postfix | `0x509B70` |
| `Core.cs` | `RhythmGameUtils` | `GetSelectableCategoty` | Postfix | `0x6B72C0` |
| `Core.cs` | `SixtarGlobal` | `GotoMusicSelect` | Prefix/Postfix | `0x7DBC40` |
| `Core.cs` | `SixtarGlobal` | `GotoPlayScene` | Prefix | `0x7DBD20` |
| `Core.cs` | `SixtarGlobal.__c__DisplayClass54_0` | `<GotoPlayScene>b__12` | Prefix | `0x7EB2E0` |
| `Core.cs` | `SoundModuleForFMOD` | `GetGamePlayAudioLength` | Postfix | `0x7E0160` |
| `Core.cs` | `SoundModuleForFMOD` | `Play` | Prefix | `0x7E11E0` |
| `Core.cs` | `TrackData` | `IsOwned` | Prefix | `0x45E650` |
| `Core.cs` | `TrackInfoViewer` | `Set` | Postfix | `0x634CB0` |
| `Core.cs` | `TrackLoader.TrackData` | `get_IsOwned` | Prefix | `0x7F56A0` |
| `Core.cs` | `TrackObject` | `SetTrack` | Postfix | `0x636620` |
| `Core.cs` | `TrackObject.__c__DisplayClass18_0` | `<LoadThumbnail>b__1` | Postfix | `0x63B4D0` |
| `Core.cs` | `TrackSelectController` | `GetTracks` | Prefix | `0x617CF0` |
| `Features/AttendancePopup.cs` | `AttendanceController._WaitUpdateCheck_d__6` | `MoveNext` | Prefix | `0x53EC40` |
| `Features/CustomMusicSelect.cs` | `TrackSelectController` | `LoadData` | Postfix | `0x618500` |
| `Features/CustomMusicSelect.cs` | `TrackSelectController` | `SelectTrack` | Postfix | `0x619510` |
| `Features/CustomNoteSkin.cs` | `StartrailPatternRenderer` | `GetNoteObejct` | Postfix | `0x688120` |
| `Features/NoteEffects.cs` | `NoteObject` | `set_HeadPosition` | Prefix (동적) | `0x67F470` |
| `Features/NoteEffects.cs` | `NoteObject` | `set_NoteLength` | Prefix (동적) | `0x67F5F0` |
| `Features/NoteEffects.cs` | `StartrailPatternRenderer` | `Register` | Postfix | `0x6898A0` |
| `Features/PlayAssist.cs` | `GearObject` | `PlayNoteEffect` | Prefix | `0x6772E0` |
| `Features/PlayAssist.cs` | `GearObject` | `SubscribeObserver` | Postfix | `0x678FE0` |
| `Features/PlayAssist.cs` | `JudgeProcessor` | `Judge` | Prefix | `0x6420E0` |
| `Features/PlayAssist.cs` | `JudgeProcessor` | `OnTickEvent` | Prefix/Postfix | `0x642370` |
| `Features/PlayAssist.cs` | `JudgeProcessor` | `SetPressedState` | Postfix | `0x6426E0` |
| `Features/PlayAssist.cs` | `PlayRecordModule` | `RecordCoursePlayData` | Prefix | `0x7C1570` |
| `Features/PlayAssist.cs` | `PlayRecordModule` | `RecordLicensePlayData` | Prefix | `0x7C1900` |
| `Features/PlayAssist.cs` | `PlayRecordModule` | `RecordPlayData` | Prefix | `0x7C2E10` |
| `Features/PlayAssist.cs` | `ScoreProcessor` | `UpdateScore` | Prefix/Postfix | `0x65F980` |
| `Features/PlayAssist.cs` | `SixtarGlobal` | `GotoMusicSelect` | Postfix | `0x7DBC40` |
| `Features/RandomNoteSkin.cs` | `StartrailPatternRenderer` | `GetNoteObejct` | Postfix | `0x688120` |
| `Managers/CustomAudioManager.cs` | `SoundModuleForFMOD` | `ChangeVolume` | Postfix | `0x7DF860` |
| `Managers/CustomAudioManager.cs` | `SoundModuleForFMOD` | `GetTimelinePosition` | Postfix | `0x7E05F0` |
| `Managers/CustomAudioManager.cs` | `SoundModuleForFMOD` | `Pause` | Prefix | `0x7E0E40` |
| `Managers/CustomAudioManager.cs` | `SoundModuleForFMOD` | `Resume` (두 오버로드) | Prefix | `0x7E1920`, `0x7E19D0` |
| `Managers/CustomAudioManager.cs` | `SoundModuleForFMOD` | `Stop` | Prefix | `0x7E2190` |
| `Managers/CustomAudioManager.cs` | `SoundModuleForFMOD` | `set_CurrectionVolume` | Postfix | `0x7E27B0` |

## 2. 오프라인·서버 종료 우회 (`Features/OfflineBypass.cs`, 63개 — 전부 `ICF=1`)

| 파일 | 대상 형식 | 메서드 | 종류 | RVA |
| --- | --- | --- | --- | --- |
| `Features/OfflineBypass.cs` | `Web.BackEndWebRequest` | `InitializeModule` | Prefix | `0x580210` |
| `Features/OfflineBypass.cs` | `Web.BackEndWebRequest` | `ShowMaintenance` | Prefix | `0x581400` |
| `Features/OfflineBypass.cs` | `Web.BackEndWebRequest` | `CheckMaintenanceStatus` | Prefix | `0x57FF10` |
| `Features/OfflineBypass.cs` | `Web.BackEndWebRequest` | `Exit` | Prefix | `0x580050` |
| `Features/OfflineBypass.cs` | `Web.BackEndMaintenance` | `Enter` | Prefix | `0x55EFA0` |
| `Features/OfflineBypass.cs` | `Web.BackEndMaintenance` | `Observe` | Prefix | `0x55F1D0` |
| `Features/OfflineBypass.cs` | `Web.BackEndWebRequest` | `LoadUserData` | Prefix | `0x5808A0` |
| `Features/OfflineBypass.cs` | `Web.BackEndWebRequest` | `UpdateUserData(Users, Action)` | Prefix | `0x581A50` |
| `Features/OfflineBypass.cs` | `Web.BackEndWebRequest` | `UpdateUserData(Action<bool>)` | Prefix | `0x581910` |
| `Features/OfflineBypass.cs` | `Web.BackEndWebRequest` | `SaveUserProfile` | Prefix | `0x5811E0` |
| `Features/OfflineBypass.cs` | `Web.BackEndRequestService` | `GetTableVersions` | Prefix | `0x57D1D0` |
| `Features/OfflineBypass.cs` | `Web.BackEndRequestService` | `FetchServer` | Prefix | `0x57CEC0` |
| `Features/OfflineBypass.cs` | `Web.BackEndRequestService` | `GetAttendanceEventTable` | Prefix | `0x57CF80` |
| `Features/OfflineBypass.cs` | `Web.BackEndRequestService` | `Login` | Prefix | `0x57D6C0` |
| `Features/OfflineBypass.cs` | `Web.BackEndUserDataService` | `GetUnlockData` | Prefix | `0x57F450` |
| `Features/OfflineBypass.cs` | `Web.BackEndUserDataService` | `GetUserArkk` | Prefix | `0x57F550` |
| `Features/OfflineBypass.cs` | `Web.BackEndUserDataService` | `RefreshUserData` | Prefix | `0x57F8D0` |
| `Features/OfflineBypass.cs` | `Web.BackEndUserDataService` | `RequestItemUnlock` | Prefix | `0x57F950` |
| `Features/OfflineBypass.cs` | `Web.BackEndUserDataService` | `GetAttendanceReward` | Prefix | `0x57F3D0` |
| `Features/OfflineBypass.cs` | `Web.BackEndUserDataService` | `RequestTravelModeArkkRewards` | Prefix | `0x57FAD0` |
| `Features/OfflineBypass.cs` | `Web.BackEndUserDataService` | `RequestStartLicenseTest` | Prefix | `0x57FAB0` |
| `Features/OfflineBypass.cs` | `Web.BackEndShopService` | `RequestItemPurchase` | Prefix | `0x57EDB0` |
| `Features/OfflineBypass.cs` | `Web.BackEndRankingService` | `GetHighScoreList` | Prefix | `0x561390` |
| `Features/OfflineBypass.cs` | `Web.BackEndRankingService` | `GetRankingList` | Prefix | `0x561590` |
| `Features/OfflineBypass.cs` | `Web.BackEndRankingService.TravelRankingReadRequest` | `Start` | Prefix | `0x577840` |
| `Features/OfflineBypass.cs` | `Web.BackEndRankingService` | `PostHighScoreData` | Prefix | `0x5623E0` |
| `Features/OfflineBypass.cs` | `Web.BackEndRankingService` | `SubmitTravelHighScore` | Prefix | `0x562730` |
| `Features/OfflineBypass.cs` | `Web.BackEndRankingService.TravelHighScoreRequest` | `Start` | Prefix | `0x585C50` |
| `Features/OfflineBypass.cs` | `Web.BackEndRankingService` | `GetUserLicenseLevel` | Prefix | `0x561660` |
| `Features/OfflineBypass.cs` | `Web.BackEndRankingService` | `PostClearLicense` | Prefix | `0x561C00` |
| `Features/OfflineBypass.cs` | `ModeSelect.UpdateCheckController` | `Initialize` | Prefix | `0x53F1A0` |
| `Features/OfflineBypass.cs` | `UpdateCheckPopup` | `OpenPopup` | Prefix | `0x49A830` |
| `Features/OfflineBypass.cs` | `ServerMaintenanceNotice` | `Show` | Prefix | `0x491C10` |
| `Features/OfflineBypass.cs` | `Session.UnitySessionLegacyConnector` | `Connect` | Prefix | `0x579BA0` |
| `Features/OfflineBypass.cs` | `Session.UnitySessionLegacyConnector` | `ReqeustSessionPlayingCount` | Prefix | `0x57A510` |
| `Features/OfflineBypass.cs` | `ClientDataProvider` | `InitializeModule` | Finalizer | `0x79B930` |
| `Features/OfflineBypass.cs` | `AchievementModule` | `InitializeModule` | Finalizer | `0x7995A0` |
| `Features/OfflineBypass.cs` | `AchievementModule` | `ContainsAchievement` | Finalizer | `0x798930` |
| `Features/OfflineBypass.cs` | `AchievementModule` | `UnlockReqeustAchievement` | Finalizer | `0x799870` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `CommonErrorAction` | Prefix | `0x59D560` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `FetchServer` | Prefix | `0x59D5F0` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `GetUnlockData` | Prefix | `0x59E570` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `RefreshRequest` | Prefix | `0x59EC40` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `RequestItemUnlock` | Prefix | `0x59EF80` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `GetUserArkk` | Prefix | `0x59E690` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `CheckServerConnection` | Prefix | `0x59D430` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `GetRemoteVersion` | Prefix | `0x59E060` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `GetAttendanceEventTable` | Prefix | `0x59D770` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `GetAttendanceReward` | Prefix | `0x59DA40` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `GetNewsList` | Prefix | `0x59DD80` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `GetUserLicenseLevel` | Prefix | `0x59E800` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `GetUnityQuestProcess` | Prefix | `0x59E1A0` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `GetUnityQuestServer` | Prefix | `0x59E440` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `RequestTravelModeArkkRewards` | Prefix | `0x59F690` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `PostHighScoreData` | Prefix | `0x59E970` |
| `Features/OfflineBypass.cs` | `ManagerWebRequest` | `GetHighScoreList` | Prefix | `0x59DC80` |
| `Features/OfflineBypass.cs` | `RhythmGame.ModeSelect.UpdateChecker` | `Check` | Prefix | `0x70A1F0` |
| `Features/OfflineBypass.cs` | `UserAccountModule` | `CheckTutorial` | Prefix | `0x5B2440` |
| `Features/OfflineBypass.cs` | `UserAccountModule` | `LoadFavorites` | Finalizer | `0x5B3080` |
| `Features/OfflineBypass.cs` | `ItemHelper` | `LoadUnlockCache` | Prefix | `0x59A390` |
| `Features/OfflineBypass.cs` | `ItemHelper` | `CheckItemsUnlock` | Prefix | `0x5996D0` |
| `Features/OfflineBypass.cs` | `ItemHelper` | `CheckOwnedState(string)` | Prefix | `0x599CA0` |
| `Features/OfflineBypass.cs` | `ItemHelper` | `CheckOwnedState(ICollectable)` | Prefix | `0x599DD0` |

## ICF가 있는데 패치한 곳

`PlayLoadingManager.<>c__DisplayClass34_0.<OnLoadResource_Travel>b__2 / b__4`는 Outer Space 판(`DisplayClass35_0`)과 코드를 공유합니다.
두 클래스의 필드 배치가 같아서(`<>4__this` 위치 동일) Postfix가 어느 쪽 `this`로 실행돼도 안전합니다.

## 일부러 패치하지 않는 곳

| 함수 | 이유 | 대신 |
| --- | --- | --- |
| `NeedClientUpdatePopup.OpenPopup` / `ServerDisconnectedPopup.OpenPopup` | `0x450BA0` (5192개와 ICF) | `UpdateCheckController.Initialize` + `UpdateCheckPopup.OpenPopup` Prefix |
| `JudgeProcessor.set_IsAutoJudge` | 44개와 ICF | 호출만 |
| `FinishProcessor.set_TrackEndTiming` | 11개와 ICF | `UpdatePatternTime` Prefix |
| `NoteObject.SetTickData` | 102개와 ICF | `GetNoteObejct` Postfix |
| `TrackInfoViewerModule._Set_b__3_1` | 11개와 ICF | `Set` Postfix + 재적용 코루틴 |
| `BackgroundUIProcessor.<Initialize>b__1` | 인벤토리 썸네일 콜백과 ICF | 재적용 코루틴 |
| `GearObject.NoteJudgedObserver.Notify` | `InputEventArgs.Invoke`와 ICF | `PlayNoteEffect` Prefix |
| `AttendanceController.<>c__DisplayClass6_0.<WaitUpdateCheck>b__7` | 2개와 ICF | `MoveNext` Prefix |
| `ScoreObserver` / `ReverseScoreObserver` 생성자 | 같은 주소 공유 | `SubscribeObserver` Postfix |
| `JudgeProcessor` 생성자 | 패치해도 한 번도 안 불림 | 인스턴스 메서드에서 지연 초기화 |
| `TrackObject.LoadThumbnail` | `SetTrack`에 인라인됨 | `SetTrack` + 콜백 Postfix |
