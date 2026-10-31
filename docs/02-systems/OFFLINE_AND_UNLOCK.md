# 오프라인·서버 종료 우회, 전곡 해금, 출석 창

코드: `Features/OfflineBypass.cs`, `Core.cs` 1번 영역(해금), `Features/AttendancePopup.cs`

## 1. 완전 오프라인 / 서버 종료 우회 (`Features/OfflineBypass.cs`)

빌드 `26218612`는 두 갈래의 네트워크 계층을 동시에 갖고 있습니다.
1. **리뉴얼 + 뒤끝(BACKND) 파이프라인 (`SixtarGate.Web.BackEnd*`, `SixtarGate`)** — 현재 빌드에서 실제로 부팅·테이블·모드 선택·결과 화면을 구동하는 핵심 경로.
2. **레거시 자체 HTTP 파이프라인 (`ManagerWebRequest`, `UpdateChecker`, `UserAccountModule`, `ItemHelper`)** — `sxtg2 saver test`에서 검증된 구버전 오프라인 우회 경로.

기존에는 점검 팝업과 점수 업로드 4개만 막고 있어서, **뒤끝(BACKND) 서버나 게임 서버가 완전히 내려가거나 오프라인인 상태**에서는 부팅 코루틴(`DefaultBootLoader` → `SixtarGlobal.Initialize`)이 `WaitUntil(() => Web.HasInitialized)`에서 무한 대기하거나 `BackEndWebRequest.Exit()`로 클라이언트를 종료시켰습니다.
`Features/OfflineBypass.cs`는 리뉴얼 39개 훅 + 레거시(`sxtg2 saver test` 이식) 24개 훅(총 63개, 전부 `il2cpp_dump/script.json` 기준 `ICF=1` 고유 주소 검증 완료)으로 서버가 완전히 꺼진 상태에서도 부팅 → 타이틀 → 모드 선택 → 곡 선택 → 플레이 → 결과창(Arkk 보상 계산 포함) 전 구간이 동작하도록 우회합니다.

### 1-1. 리뉴얼 부팅 & 테이블·아이템 초기화 체인

```text
DefaultBootLoader.Start (Renewal_Boot)
  └─ SixtarGlobal.Initialize (WaitUntil HasInitialized)
       ├─ ClientDataProvider.InitializeModule (RVA 0x79B930)
       │    └─ Steam 미실행 예외 발생 시 Finalizer가 InitializeUnityEditor (RVA 0x79BDB0) 폴백 호출
       ├─ BackEndWebRequest.InitializeModule (RVA 0x580210)
       │    └─ Prefix에서 Request/UserData/Shop/Ranking 서브서비스 지연 생성,
       │       로컬 Users 객체 주입, _init = true, HasInitialized = true,
       │       LicenseRecoveryState = Ready 설정 후 코루틴(BACKND 로그인/점검/차트/Exit) 스킵
       ├─ TableLoader.InitializeModule (RVA 0x7E3690)
       │    ├─ LoadCachedTable()로 로컬 캐시 먼저 로드
       │    └─ BackEndRequestService.GetTableVersions (RVA 0x57D1D0) Prefix가 빈 리스트 반환
       │         → 원격 다운로드 스킵 → TableLoader.HasInitialized = true 완료
       │         → 이후 LoadTable(name) 호출 시 캐시에 없으면 빌트인 직렬화 에셋(SXGT_TextAsset) 폴백 사용
       ├─ TrackLoader & ItemProvideModule
       │    └─ BackEndUserDataService.GetUnlockData (RVA 0x57F450) Prefix가 빈 리스트 콜백 호출
       │         → loadedUnlockItems = true, ItemProvideModule.HasInitialized = true 완료
       └─ AchievementModule.InitializeModule (RVA 0x7995A0)
            └─ Steam 미실행 시에도 Finalizer가 예외를 삼키고 HasInitialized = true 보장
```

### 1-2. 리뉴얼 `SixtarGate.Web` / 모드 선택 / 결과 화면 훅 요약 (39개, 모두 `ICF=1`)

| 분류 | 대상 메서드 | RVA | 동작 요약 |
| --- | --- | --- | --- |
| 부팅·점검 | `BackEndWebRequest.InitializeModule` | `0x580210` | 서브서비스 생성 후 `HasInitialized = true`, `LicenseRecoveryState = Ready` 즉시 완료 |
| 부팅·점검 | `BackEndWebRequest.Exit` | `0x580050` | BACKND 오류로 인한 강제 종료 차단 |
| 부팅·점검 | `BackEndWebRequest.ShowMaintenance` / `CheckMaintenanceStatus` | `0x581400` / `0x57FF10` | 점검 상태 확인 및 팝업 차단 |
| 부팅·점검 | `BackEndMaintenance.Enter` / `Observe` | `0x55EFA0` / `0x55F1D0` | 점검 진입 차단 및 `Observe` `false` 반환 |
| 유저 DB | `BackEndWebRequest.LoadUserData` / `UpdateUserData`(2종) / `SaveUserProfile` | `0x5808A0` / `0x581A50` / `0x581910` / `0x5811E0` | 로컬 `Users` 인스턴스로 즉시 성공 콜백 |
| 테이블·로그인 | `BackEndRequestService.GetTableVersions` | `0x57D1D0` | 빈 버전 목록 반환 → 로컬 캐시 / 내장 `SXGT_TextAsset` 테이블 사용 |
| 테이블·로그인 | `BackEndRequestService.FetchServer` / `GetAttendanceEventTable` / `Login` | `0x57CEC0` / `0x57CF80` / `0x57D6C0` | 서버 조회 없이 즉시 로컬 완료 콜백 |
| 유저 데이터 | `BackEndUserDataService.GetUnlockData` | `0x57F450` | 빈 해금 목록 콜백 → `TrackLoader`·`ItemProvideModule` 대기 해제 |
| 유저 데이터 | `BackEndUserDataService.GetUserArkk` / `RefreshUserData` / `RequestItemUnlock` / `GetAttendanceReward` / `RequestStartLicenseTest` | `0x57F550` / `0x57F8D0` / `0x57F950` / `0x57F3D0` / `0x57FAB0` | 오프라인 Arkk(`999999`) 및 즉시 성공 콜백 |
| 결과창 보상 | `BackEndUserDataService.RequestTravelModeArkkRewards` | `0x57FAD0` | 내장 `TravelArkkRewardProvider(resultArgs).GetRewards()`로 로컬 Arkk 보상을 계산해 콜백 전달 (`ArkkRewardModule.FinishInitialize()` 완료 보장) |
| 상점 | `BackEndShopService.RequestItemPurchase` | `0x57EDB0` | `success(true)` 콜백 |
| 랭킹·라이선스 | `BackEndRankingService.GetHighScoreList` / `GetRankingList` / `TravelRankingReadRequest.Start` | `0x561390` / `0x561590` / `0x577840` | 빈 랭킹 결과 즉시 반환 (`TravelRankingQueryStatus.Success`) |
| 랭킹·라이선스 | `BackEndRankingService.PostHighScoreData` / `SubmitTravelHighScore` / `TravelHighScoreRequest.Start` | `0x5623E0` / `0x562730` / `0x585C50` | 서버 점수 업로드 차단 및 로컬 1위(`Confirmed`) 콜백 |
| 랭킹·라이선스 | `BackEndRankingService.GetUserLicenseLevel` / `PostClearLicense` | `0x561660` / `0x561C00` | 로컬 `LicenseLevel` 객체 및 타임스탬프 콜백 (`LicenseDataState.Ready` 보장) |
| 모드 선택·팝업 | `UpdateCheckController.Initialize` / `UpdateCheckPopup.OpenPopup` | `0x53F1A0` / `0x49A830` | 모드 선택 진입 시 서버 버전 체크 팝업을 띄우지 않고 `VersionCheckPass = true` 설정 |
| 모드 선택·팝업 | `ServerMaintenanceNotice.Show` | `0x491C10` | 점검 공지 팝업(→ 종료) 차단 |
| 멀티 세션 | `UnitySessionLegacyConnector.Connect` / `ReqeustSessionPlayingCount` | `0x579BA0` / `0x57A510` | 레거시 멀티 세션 서버 연결 스킵(`false`, `0`명) |
| Steam 폴백 | `ClientDataProvider.InitializeModule` / `AchievementModule.InitializeModule`·`ContainsAchievement`·`UnlockReqeustAchievement` | `0x79B930` / `0x7995A0` / `0x798930` / `0x799870` | Finalizer로 Steam 미실행 시 `InitializeUnityEditor()` 폴백 및 예외 무시 |

> **주의 (ICF 함정):** `NeedClientUpdatePopup.OpenPopup`과 `ServerDisconnectedPopup.OpenPopup`은 둘 다 빈 가상 메서드 주소 `0x450BA0`(`ICF=5192`)으로 접혀 있으므로 절대 패치하지 않습니다. 상위 호출자인 `UpdateCheckController.Initialize`(`0x53F1A0`)와 `UpdateCheckPopup.OpenPopup`(`0x49A830`)에서 차단합니다.

### 1-3. 레거시 `sxtg2 saver test` 이식 훅 요약 (24개, 모두 `ICF=1`)

| 대상 형식 | 메서드 | RVA | 동작 요약 |
| --- | --- | --- | --- |
| `ManagerWebRequest` | `CommonErrorAction` | `0x59D560` | 에러 오버레이 숨김 및 빈 코루틴 반환 |
| `ManagerWebRequest` | `FetchServer` / `CheckServerConnection` / `GetRemoteVersion` | `0x59D5F0` / `0x59D430` / `0x59E060` | 서버 연결·버전(`207`) 즉시 성공 콜백 |
| `ManagerWebRequest` | `GetUnlockData` / `RefreshRequest` / `RequestItemUnlock` | `0x59E570` / `0x59EC40` / `0x59EF80` | `ItemHelper.Instance.InitializeUnlockData(빈 리스트)` 호출 및 성공 콜백 |
| `ManagerWebRequest` | `GetUserArkk` / `RequestTravelModeArkkRewards` | `0x59E690` / `0x59F690` | 오프라인 Arkk(`999999`) 및 빈 보상 리스트 콜백 |
| `ManagerWebRequest` | `GetAttendanceEventTable` / `GetAttendanceReward` / `GetNewsList` | `0x59D770` / `0x59DA40` / `0x59DD80` | 출석·뉴스 빈 응답 콜백 |
| `ManagerWebRequest` | `GetUserLicenseLevel` / `GetUnityQuestProcess` / `GetUnityQuestServer` | `0x59E800` / `0x59E1A0` / `0x59E440` | 기본 라이선스·유니티 퀘스트 더미 응답 콜백 |
| `ManagerWebRequest` | `PostHighScoreData` / `GetHighScoreList` | `0x59E970` / `0x59DC80` | 레거시 점수 전송 차단(`onComplete(1)`) 및 빈 `ScoreList` 반환 |
| `UpdateChecker` | `Check` | `0x70A1F0` | `checkedFlag = true`, `isUpdateChecked = true`, 오브젝트 비활성화 후 `onLatestVersion` 호출 |
| `UserAccountModule` | `CheckTutorial` / `LoadFavorites` | `0x5B2440` / `0x5B3080` | 튜토리얼 강제 진입 스킵(`false`) 및 즐겨찾기 로드 실패 시 빈 `SXGT_Favorites` 반환(Finalizer) |
| `ItemHelper` | `LoadUnlockCache` / `CheckItemsUnlock` / `CheckOwnedState`(2종) | `0x59A390` / `0x5996D0` / `0x599CA0` / `0x599DD0` | 빈 해금 캐시 반환 및 보유 확인 `true` 반환 |

---

## 2. 전곡 & DLC 해금 (`Core.cs` + `Features/OfflineBypass.cs`)

곡·DLC·아이템 해금 확인은 모두 로컬에서 `true`를 돌려줍니다.

| 대상 | RVA | 원래 확인 |
| --- | --- | --- |
| `ItemUnlockChecker.IsUnlocked(hash)` | `0x7A4EA0` | 빈 문자열이면 true, `DLC`가 들어가면 `SteamApps.IsDlcInstalled`, 아니면 `ItemProvider.IsOwned` |
| `ItemUnlockChecker.IsDLCUnlocked(hash)` | `0x7A4DE0` | `DLCHashMap`에서 Steam AppID 찾아 `IsDlcInstalled` |
| `ItemProvideModule.IsOwned(itemID)` | `0x7B7050` | 리뉴얼 보유 아이템 |
| `Il2Cpp.TrackData.IsOwned` | `0x45E650` | 구버전 트랙 |
| `TrackLoader.TrackData.IsOwned` (getter) | `0x7F56A0` | 리뉴얼 트랙 |
| `ItemHelper.CheckOwnedState(string / ICollectable)` | `0x599CA0` / `0x599DD0` | 레거시 인벤토리 보유 아이템 |

스탠바이의 스킨 목록도 이 확인을 거칩니다(`MultipleOperationsOption.UseUnlockFilter` → `IItemChecker.IsUnlocked`). 그래서 WACCA·KALPA 스킨은 구매 전에도 목록에 보입니다.

---

## 3. 출석 보상 창 (`SkipAttendancePopup`, 기본 1)

```text
AttendanceController.Initialize (RVA 0x527920) → <WaitUpdateCheck>d__6 코루틴
  state 0: 업데이트 확인 대기
  state 1: IRequestService로 출석표 → _attendanceTableData
  state 2: IUserDataService로 오늘 보상 → _attendanceReward
  state 3: _loadingAttendanceData = null
           보상을 받을 수 있으면 Debug.Log("receivable attendance reward!!!")
           + PopupModule.OpenPopup<AttendanceRewardPopup>
```

`MoveNext`(RVA `0x53EC40`, ICF 없음) Prefix에서 state 3일 때만 원본처럼 정리(state = −1, `_loadingAttendanceData = null`)하고 끝냅니다.
- 데이터는 남아 있어 게임의 `OpenAttendancePopup`(RVA `0x5279C0`)은 그대로 동작합니다.
- 서버로 아무것도 보내지 않습니다. 보상은 받지 않은 상태로 남습니다.
- `<WaitUpdateCheck>b__7`은 다른 함수와 주소를 공유하므로 패치하지 않습니다.
- 실기 확인: 로그 `[Attendance] Skipped the automatic attendance reward popup (SkipAttendancePopup=1)`.
