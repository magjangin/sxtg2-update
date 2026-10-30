# 오프라인 가드, 해금, 출석 창

코드: `Core.cs` 1·2번 영역, `Features/AttendancePopup.cs`

## 오프라인 가드

게임은 시작과 결과 화면에서 자체 HTTP API와 BACKND(뒤끝) SDK로 서버와 통신합니다.

| 훅 | 종류 | 하는 일 |
| --- | --- | --- |
| `ServerMaintenanceNotice.Show` | Prefix, false | 점검 팝업(→ 게임 종료)을 띄우지 않음 |
| `ManagerWebRequest.PostHighScoreData` | Prefix, false | 점수 전송 대신 `onComplete(1)`로 성공 흉내 |
| `ManagerWebRequest.GetHighScoreList` | Prefix, false | 빈 `ScoreList`를 콜백으로 |
| `BackEndRankingService.TravelHighScoreRequest.Start` | Prefix, false | BACKND 랭킹 업로드 차단 |

결과 연출은 정상으로 진행되고, 서버로는 점수가 가지 않습니다. 로그에 `[OfflineGuard] ...`가 남습니다.

## 해금

곡·DLC 해금 확인이 아래에 모여 있습니다. 모두 같은 Prefix(`Core.Unlocked`)로 `true`를 돌려줍니다.

| 대상 | 원래 확인 |
| --- | --- |
| `ItemUnlockChecker.IsUnlocked(hash)` | 빈 문자열이면 true, `DLC`가 들어가면 `SteamApps.IsDlcInstalled`, 아니면 `ItemProvider.IsOwned` |
| `ItemUnlockChecker.IsDLCUnlocked(hash)` | `DLCHashMap`에서 Steam AppID 찾아 `IsDlcInstalled` |
| `ItemProvideModule.IsOwned(itemID)` | 보유 아이템 |
| `Il2Cpp.TrackData.IsOwned` | 구버전 트랙 |
| `TrackLoader.TrackData.IsOwned` (getter) | 리뉴얼 트랙 |

스탠바이의 스킨 목록도 이 확인을 거칩니다(`MultipleOperationsOption.UseUnlockFilter` → `IItemChecker.IsUnlocked`). 그래서 WACCA·KALPA 스킨은 구매 전에도 목록에 보입니다.

## 출석 보상 창 (`SkipAttendancePopup`, 기본 1)

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
