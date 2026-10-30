# 기어 스킨과 노트 스킨

`sharedassets0.assets`의 `GearPackage` / `NotePackage`와 옵션 정의(`GearSkin`, `NoteSkin`)를 직접 읽었습니다(`tools/datadump/skin_packages.py`).

## 개수

- **기어 22개**, **노트 11개**. 둘은 서로 묶이지 않은 별개 옵션입니다.
- 노트 패키지에는 11종 × (파랑 `{skin}`, 빨강 `{skin}_red`, 게이트 `{skin}_gate`) + 기본 이펙터 3개(`default_barline`, `default_startline`, `default_gatealarm`) = 36개 프리팹.

## 짝

| 구분 | 스킨 |
| --- | --- |
| 기어·노트 같은 이름 (8쌍) | default, fkns, gothic, pianist, stellar, voyager, reincarnationOfKalpa, cosmicSymphony |
| WACCA | 기어 4개 `WaccaLily`, `WaccaLilyR`, `WaccaReverse`, `WaccaS` ↔ 노트 `wacca` 1개 |
| 기어만 (10개) | defaultna, Arch, Cat, Equalizer, HeartBeat, Oriental, Relic, Rose, StarBeat, Steampunk |
| 노트만 (2개) | voyagerorigin, snowflake |

- 기어만 있는 스킨은 프레임 모양과 애니메이션(`SteampunkAnimator_New`, `EqualizerAnimatior` 등)만 바뀌는 디자인입니다.
- HeartBeat, StarBeat, Relic은 구버전 기어 이름(`RG_Gear_Heartbeat`, `RG_Gear_Starbeat`, `RG_Gear_Relic`)이 남아 있어 예전 기어를 옮겨 온 것으로 보입니다.

## DLC 표시

옵션 정의의 `_dlcHashs`:

| 스킨 | DLC |
| --- | --- |
| 기어 `WaccaLily`, `WaccaLilyR`, `WaccaReverse`, `WaccaS` / 노트 `wacca` | `DLC_Wacca` (WACCA Collaboration Pack) |
| 기어·노트 `reincarnationOfKalpa`, `cosmicSymphony` | `DLC_Kalpa` (KALPA Collaboration Pack) |

나머지는 표시가 없어 항상 보입니다. 모드는 해금 확인을 항상 통과시키므로 DLC 스킨도 구매 전부터 보입니다.

## `button01`은 노트 스킨이 아님

`button01_*`는 스프라이트만 있고 노트 프리팹은 없습니다. 전부 기어 양옆의 **키 버튼 그림**으로 쓰입니다.

| 쓰는 기어 | 위치 |
| --- | --- |
| default, defaultna, gothic, stellar, pianist, voyager | `Gear Side Object/…/KeyAnimation {레인}/Button Released`·`Button Pressed`, `Gear Gate Mask Object/KeyAnimation GATE` |

색별(파랑/빨강/게이트), 상태별(active, deactive, input), 장식(nov_bg, star_glow, gate_deco)로 나뉩니다. 확인 스크립트: `tools/datadump/sprite_users.py`.

## 모드의 노트 스킨 기능

- `CustomNotes\*.png`로 직접 그린 노트 ([NOTE_EFFECTS_AND_SKINS.md](../02-systems/NOTE_EFFECTS_AND_SKINS.md))
- `RandomNoteSkin=1`: 노트마다 위 11종 중 무작위
