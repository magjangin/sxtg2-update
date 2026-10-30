# 모드 선택 화면

게임 데이터(`level5~12`의 `ModeItemController`)와 코드(`ModeItemController.Confirm/OnConfirm/OnDevelop`)를 직접 읽어 정리했습니다.

## 고르면 무슨 일이 일어나나

```text
ModeItemController.Confirm
  ├─ isDevelop = 1 → OnDevelop: 효과음 + "개발 중" 패널(ModeDevelopViewer). 들어갈 수 없음
  └─ isDevelop = 0 → OnConfirm
        플래그 "LastSelectedModeName" = 모드 이름 (다음에 커서가 여기 있음)
        이동할 씬 = targetSceneName (비어 있으면 모드 이름)
        PlayLoadingController가 있으면 그걸로, 없으면 SceneLoader로 이동
TRAVEL MODE (MusicSelectItemController / Kalpa는 PlayStyleSelectItemController)
  └─ 먼저 SOLAR / LUNAR 선택 → 곡 선택
```

## 모드 7가지

| 모드 (`modeName`) | 화면 이름 | 이동 씬 | 하는 것 |
| --- | --- | --- | --- |
| `TravelMode` | TRAVEL MODE | `MusicSelect` | **기본 플레이.** Solar(7레인) / Lunar(5레인)를 고른 뒤 곡과 난이도를 골라 한 곡씩 플레이. 모드의 CUSTOM 탭도 여기 있음. 결과 화면에서 시이가 나옴([RESULT_ANIMATIONS.md](RESULT_ANIMATIONS.md)) |
| `OuterSpace` | OUTER SPACE (WACCA 테마: OrbitalTravel) | `OuterSpace` | **코스 모드.** 정해진 여러 곡을 이어서 치는 코스를 고름. 코스 정렬(이름, 점수, 업데이트 순), 코스별 최고 점수, DLC 표시 |
| `LicenseTest` | LICENSE TEST | `LicenseTest` | **단위 인정(라이선스 시험).** 시험 코스에 합격하면 라이선스를 받음. 일부 곡은 후보 중에서 고름. 판정 조건, 진행도, 최근 기록, 내 라이선스 보기 |
| `Adventure` | ADVENTURE (MODE) | `AdventureTitle` → `Adventure` | **스토리 모드.** 에피소드 진행, 컷신·대사·캐릭터 연출, 배틀(곡 플레이), 난이도 선택, 보상 |
| `VoyageReport` | VOYAGE REPORT | `Statistics` | **통계.** 플레이 횟수, 클리어 수, 난이도별 램프 막대, 많이 친 곡, 순위, 라이선스·아우터 스페이스 기록 |
| `Lesson` | LESSON MODE / TUTORIAL | (플레이 로딩) | **튜토리얼.** 레슨용 플레이(`PlaySceneMode.Lesson`). 테마에 따라 "개발 중"으로 막혀 있음 |
| `UnityQuest` | UNITY QUEST | — | **모든 테마에서 "개발 중"**이라 들어갈 수 없음. 씬 `UnityQuest_Lobby`는 게임에 들어 있음 |

> 곡 선택 화면 왼쪽의 **UNITY SESSION**(F5)은 이것과 다른 기능입니다. 곡 선택 중에 쓰는 온라인 세션 표시입니다.

## 테마 8가지와 항목 차이

모드 선택 화면의 모습은 장착한 **테마**(아이템)로 바뀝니다(`ThemeChangeController`). 테마마다 별도 씬입니다.

| 테마 (씬) | TRAVEL | OUTER SPACE | LICENSE | ADVENTURE | VOYAGE REPORT | LESSON | UNITY QUEST |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Default (`level5`) | ○ | ○ | ○ | ○ | ○ | **○ 들어감** | 개발 중 |
| 2021 Xmas (`level6`) | ○ | ○ | ○ | ○ | ○ | 개발 중 (TUTORIAL) | 개발 중 |
| Crossing Delta (`level7`) | ○ | ○ | ○ | ○ | ○ | 개발 중 | 개발 중 |
| Flower & Destiny (`level8`) | ○ | ○ | ○ | ○ | ○ | 개발 중 | 개발 중 |
| 동방 Project 01 (`level9`) | ○ | ○ | ○ | ○ | ○ | 개발 중 | 없음 |
| Luminous & Darkness (`level10`) | ○ | ○ | ○ | ○ | ○ | 개발 중 | 개발 중 |
| WACCA (`level11`) | ○ | ○ (OrbitalTravel) | ○ | ○ | ○ | 개발 중 | 개발 중 |
| KALPA (`level12`) | ○ | ○ | ○ | ○ | ○ | 없음 | 없음 |

- 지금 쓰시는 테마는 로그상 **2021 Xmas**(`ModeSelect_2021Xmas`)입니다. 이 테마에서는 튜토리얼이 막혀 있습니다.
- KALPA 테마는 TRAVEL에서 Solar/Lunar 선택을 자체 화면(`KalpaPlayStyleSelectViewer`, `KalpaPreviewGear`)으로 합니다.
- Flower & Destiny, WACCA 테마는 모드 전환·플레이 로딩 연출도 자체 것을 씁니다.
- 테마 장착은 아이템이고 날짜에 따라 바뀌지 않습니다([EVENTS.md](EVENTS.md)).

## 모드와 플레이 씬

플레이는 모두 같은 `Renewal_Play` 씬에서 하고, 들어온 경로가 `PlaySceneMode`로 구분됩니다.

| `PlaySceneMode` | 경로 |
| --- | --- |
| `Default` | Travel, Outer Space, Adventure |
| `Lesson` | 튜토리얼 |
| `LicenseTest` | 라이선스 시험 |
| `MultiPlay` | 멀티 플레이 |
| `ContentTest` | 개발용 |

결과 화면도 경로별로 다릅니다. Travel은 곡별 특수 연출이 있고, Outer Space와 License Test는 항상 기본 연출입니다([RESULT_ANIMATIONS.md](RESULT_ANIMATIONS.md)).
