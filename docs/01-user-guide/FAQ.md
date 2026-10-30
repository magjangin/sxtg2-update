# 자주 묻는 질문

실제로 나온 질문을 모았습니다(2026-10-30). 자세한 근거는 각 답의 링크에 있습니다.

## 플레이

### 올 퍼펙트를 켰는데 안 누른 롱노트에서 레인 불빛이 계속 켜져요
고쳤습니다(PR #1).

- **원인:** 원래 게임은 미스(REDSTAR) 판정에 레인 불빛을 켜지 않습니다. 올 퍼펙트가 미스를 BLUESTAR로 바꾸면서, 안 누른 롱노트 몸통의 틱마다 BLUESTAR 불빛이 켜졌습니다.
- **지금:** 원래 미스였던 틱은 점수와 콤보만 BLUESTAR로 치고, 불빛은 켜지 않습니다.

→ [PLAY_ASSIST.md](../02-systems/PLAY_ASSIST.md)

### 원본 곡 난이도는 4개예요, 5개예요?
**기본 4개(COMET, NOVA, SUPERNOVA, QUASAR)이고, 일부 곡만 5번째 STARLIGHT가 더 있습니다.**

- STARLIGHT는 솔라와 루나를 따로 판단합니다.
- 해금도 따로 합니다.

→ [DIFFICULTIES.md](../04-game-reference/DIFFICULTIES.md)

### 매일 출석 보상 창이 떠요. 안 뜨게 할 수 있어요?
`config.txt`의 `SkipAttendancePopup=1`(기본값)이면 자동으로 뜨지 않습니다.

- 그날 보상(ARKK)은 받지 않은 상태로 남습니다.
- 받고 싶은 날에는 0으로 바꾸고 게임을 다시 켜세요.

→ [OFFLINE_AND_UNLOCK.md](../02-systems/OFFLINE_AND_UNLOCK.md)

## 스킨과 DLC

### DLC를 샀는데 스탠바이에 새 스킨이 안 떠요
모드 문제가 아닙니다.

- **스킨이 딸린 DLC:** 기어/노트 스킨이 들어 있는 DLC는 **WACCA와 KALPA 두 개뿐**입니다. 나머지 DLC는 곡이나 테마입니다.
- **이미 보이던 스킨:** 모드가 해금 확인을 항상 통과시키기 때문에, 이 스킨들은 **사기 전부터 이미 목록에 있었습니다.** 그래서 사도 새로 생기는 칸이 없습니다.
- **추가 다운로드:** DLC 내용은 모두 기본 설치 파일에 들어 있어서 따로 받을 것도 없습니다.

KALPA 팩의 스킨은 기어 `Reincarnation of Kalpa`, `Cosmic Symphony`와 노트 `Reincarnation of Kalpa`, `Cosmic Symphony`입니다. 목록 순서로는 노트 9·10번, 기어 11·12번입니다.

→ [DLC.md](../04-game-reference/DLC.md), [SKINS.md](../04-game-reference/SKINS.md)

### 기어 스킨은 많은데 짝이 맞는 노트 스킨은 적은 것 같아요
맞습니다. **기어 22개, 노트 11개**이고, 둘은 서로 묶이지 않은 별개 옵션입니다.

| 구분 | 스킨 |
| --- | --- |
| 기어·노트 짝 (8쌍) | default, fkns, gothic, pianist, stellar, voyager, Reincarnation of Kalpa, Cosmic Symphony |
| WACCA | 기어 4개 ↔ 노트 1개 |
| 기어만 | defaultna, Arch, Cat, Equalizer, HeartBeat, Oriental, Relic, Rose, StarBeat, Steampunk |
| 노트만 | voyagerorigin, snowflake |

→ [SKINS.md](../04-game-reference/SKINS.md)

### 숨은 노트 스킨 `button01`이 있다던데요?
**없습니다.** 한때 잘못 안내한 내용입니다.

- `button01_*`는 노트가 아니라 **기어 양옆의 키 버튼 그림**입니다.
- 쓰는 기어는 default, defaultna, gothic, stellar, pianist, voyager입니다.
- 노트 스킨 목록(`NotePackage`)에는 11종뿐입니다.
- 그래서 이스터에그도 아니고, 스탠바이에 꺼낼 것도 없습니다.

→ [SKINS.md](../04-game-reference/SKINS.md)

## 결과 화면

### 어떤 곡은 결과 화면에서 시이 대신 다른 게 나와요. 만우절이나 할로윈 곡인가요?
아닙니다. 바뀌는 곡은 **두 곡뿐**입니다.

| 곡 ID | 결과 화면 |
| --- | --- |
| (그 밖의 모든 곡) | **시이**(`ShiiDefault`) |
| `zantetsuken` | **`Rath`** 모델 + 전용 영상·음악 + "Voyage Complete" 글자 |
| `lustroustravelremix` | **`Christmas_2022`** 모델·영상·음악 (2022 크리스마스) |

- **조건:** TRAVEL 모드에서 **70만 점을 넘기고 클리어**해야 합니다. 실패하거나 70만 점 이하면 시이가 나옵니다.
- **다른 모드:** OUTER SPACE, LICENSE TEST는 항상 시이 기본 연출입니다.
- **커스텀 곡:** 곡 ID가 목록에 없어서 항상 시이입니다.

→ [RESULT_ANIMATIONS.md](../04-game-reference/RESULT_ANIMATIONS.md)

## 모드 선택 화면

### 모드가 여러 개 있던데 각각 뭐예요?
| 모드 | 하는 것 |
| --- | --- |
| **TRAVEL MODE** | 기본 플레이. SOLAR(7레인)/LUNAR(5레인)를 고른 뒤 곡을 한 곡씩 칩니다. CUSTOM 탭도 여기 있습니다 |
| **OUTER SPACE** | 코스 모드. 정해진 여러 곡을 이어서 칩니다 (WACCA 테마에서는 OrbitalTravel) |
| **LICENSE TEST** | 단위 인정. 시험 코스에 합격하면 라이선스를 받습니다 |
| **ADVENTURE** | 스토리 모드. 에피소드, 컷신, 곡으로 하는 배틀, 보상 |
| **VOYAGE REPORT** | 통계. 플레이 횟수, 클리어 수, 난이도별 기록, 많이 친 곡 |
| **LESSON / TUTORIAL** | 튜토리얼. **Default 테마에서만** 들어갈 수 있습니다 |
| **UNITY QUEST** | 모든 테마에서 "개발 중"이라 들어갈 수 없습니다 |

"개발 중"인 항목을 고르면 효과음과 개발 중 안내만 나오고 넘어가지 않습니다.

곡 선택 화면 왼쪽의 **UNITY SESSION(F5)**은 UNITY QUEST와 다른 기능입니다.

### 튜토리얼이 막혀 있어요
지금 쓰시는 모드 선택 테마가 **2021 Xmas**라서 그렇습니다. 튜토리얼은 Default 테마에서만 열려 있습니다.

모드 선택 화면의 모습(테마)은 장착한 아이템으로 바뀌고, 8종(Default, 2021 Xmas, Crossing Delta, Flower & Destiny, 동방 01, Luminous & Darkness, WACCA, KALPA)이 있습니다.

→ [MODE_SELECT.md](../04-game-reference/MODE_SELECT.md)

## 화면·로그

### 곡 선택 오른쪽 목록의 자켓이 사라졌어요
코드 정리 중에 잘못 지운 것이라 바로 되돌렸습니다(PR #4). 지금은 정상으로 나옵니다.

→ [MEDIA_SYSTEM.md](../02-systems/MEDIA_SYSTEM.md)

### 콘솔에 노란 줄이 떠요
- **모드가 원인이던 2개:** `AccessTools.GetTypesFromAssembly … LightProbesQueryDisposeJob` 경고 블록은 없앴습니다(PR #5).
- **남는 1줄:** `[Il2CppInterop] Class::Init signatures have been exhausted`는 MelonLoader가 직접 내는 메시지입니다. 모드에서는 없앨 수 없고 동작에는 지장 없습니다.

→ [TROUBLESHOOTING.md](TROUBLESHOOTING.md)
