# DLC

Steam 상점 API(`appdetails?appids=1802720`)의 DLC 목록과 상점 설명, 게임 데이터를 맞춰 봤습니다(2026-10-30).

| AppID | 이름 | 출시 | 상점 설명에 있는 것 |
| --- | --- | --- | --- |
| 1986060 | Flower & Destiny Pack | 2022-05-20 | 곡, 테마 |
| 2148660 | Touhou Project Pack 01 | 2022-10-13 | 곡, 테마 |
| 2197460 | Luminous & Darkness Pack | 2022-11-04 | 곡, 테마 |
| 2447170 | POCOTONE Pack | 2023-07-03 | 곡 |
| 2648850 | WACCA Collaboration Pack | 2023-11-26 | 곡, 테마, **기어 스킨, 노트 스킨**, 이모티콘 |
| 2648860 | yomoha's Planet Pack | 2023-10-26 | 곡 |
| 2712520 | Oshiribeat Pack | 2023-12-18 | 곡 |
| 2766750 | Dystopia Pack | 2024-02-20 | 곡 |
| 2909210 | UNITED NETWALK Pack | 2024-08-28 | 곡 |
| 3913740 | KALPA Collaboration Pack | 2025-08-06 | 곡, 테마, **기어 스킨, 노트 스킨** |
| 4785690 | Berry Melody Collaboration Pack | 2026-08-24 | 곡 |

- 테마 = 모드 선택 화면 테마([MODE_SELECT.md](MODE_SELECT.md)): Flower & Destiny, Touhou01, Luminous & Darkness, Wacca, kalpa.
- 스킨이 딸린 DLC는 WACCA와 KALPA뿐입니다([SKINS.md](SKINS.md)).

## 게임 안의 DLC 처리

- 11개 AppID가 모두 `sharedassets0.assets`(`DLCHashMap`)에 있습니다. 지금 빌드는 Berry Melody까지 압니다.
- DLC 내용은 **기본 디포(1802721) 하나에 다 들어 있습니다.** DLC 전용 디포가 없어서 구매해도 따로 받는 파일이 없습니다.
- 확인 경로: `ItemUnlockChecker.IsUnlocked(hash)` → 해시에 `DLC`가 있으면 `DLCHashMap`에서 AppID → `SteamApps.IsDlcInstalled(appID)`.
- 모드는 이 확인을 항상 통과시킵니다. 그래서 구매 전후로 화면이 달라지지 않습니다.

## 확인 방법

```bash
python -I tools/datadump/dlc_ids.py "{게임}/Sixtar Gate STARTRAIL_Data"
curl -s "https://store.steampowered.com/api/appdetails?appids=1802720&filters=basic"
```
