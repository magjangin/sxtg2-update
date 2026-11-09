# 커스텀 곡과 CUSTOM 탭

코드: `Core.cs`, `Managers/CustomTrackManager.cs`, `Features/CustomMusicSelect.cs`

## 왜 별도 탭인가

처음에는 `AllTrackSelector.GetList`에 커스텀 곡을 섞어 넣었습니다. ALL TRACKS에 공식 곡과 뒤섞여서 찾기 어려웠습니다.
그래서 카테고리 줄에 **CUSTOM** 탭을 하나 더 만들고, 그 탭을 고를 때만 커스텀 곡 목록을 돌려줍니다. 공식 카테고리는 그대로입니다.

## 탭 추가

| 훅 | 종류 | 하는 일 |
| --- | --- | --- |
| `CategorySelectController.LoadData` (RVA `0x605740`) | Postfix | 정적 리스트 `Categories`에 `"CUSTOM"` 추가 |
| `RhythmGameUtils.GetSelectableCategoty` (RVA `0x6B72C0`) | Postfix | 반환 배열 끝에 `"CUSTOM"` 추가 |

탭 이름 표시는 게임의 `CategorySelectViewer.GetCategoryDisplayName`이 알아서 대문자로 보여 줍니다.

## 목록 만들기

`TrackSelectController.GetTracks(string category)` (RVA `0x617CF0`) Prefix:

```text
category == "CUSTOM" ?
  ├─ 아니오 → 원본 실행 (return true)
  └─ 예 → 캐시가 없으면
           1. GetAllTracks()에서 "Starting Point"를 틀(template)로 고름 (없으면 첫 곡)
           2. CustomTrackManager.CreateCustomTrackList(template)
                앨범마다 new TrackLoader.TrackData { TrackID, DisplayName, Composer, Category="CUSTOM",
                  DisplayBPM, solarLevels/lunarLevels(=info.txt 난이도), BGAOffset·VisibilityID·UnlockID·
                  UpdateData·ArtistInfos는 틀에서 복사 }
           3. MediaOverrideManager.PreloadAll(자켓 백그라운드 디코딩)
         → 캐시를 __result로, return false
```

- 틀을 쓰는 이유: `TrackData`에는 화면이 기대하는 필드가 많습니다. 빈 객체로 만들면 일부 화면이 NRE를 냅니다.
- 난이도 배열 길이는 틀을 따릅니다(보통 4). info.txt 난이도가 모자라면 틀 값, 그것도 없으면 `"10"`.
- 같은 목록을 다시 쓰므로 탭을 오갈 때 비용이 없습니다. 게임을 다시 켜야 새로 스캔합니다.

## 결과 → 목록 복귀 위치

문제: `SelectTrack`(RVA `0x619510`)이 `"LastSelectedTrack"` 플래그에 곡 ID를 저장하고, `LoadData`(RVA `0x618500`)가 그 ID를 **공식 곡 목록(AllTracks)**에서 찾습니다. 커스텀 ID는 못 찾아서 목록 첫 곡으로 돌아갔습니다.

해결 (`CustomMusicSelect.cs`):
- `SelectTrack` Postfix / `GotoPlayScene` Prefix: 마지막 커스텀 곡 ID를 기억 (공식 곡을 고르면 지움)
- `LoadData` Postfix: CUSTOM 목록에서 같은 ID의 `ITrack`을 찾아 `SelectedTrack`에 넣음

게임을 다시 켜면 기억이 없어 원래대로 첫 곡에서 시작합니다.

## 다국어 곡 정보 및 메타데이터

`Loaders/TrackInfoParser.cs`(곡 정보 텍스트)와 `Loaders/BmsParser.cs`(BMS 헤더)가 제목·아티스트·난이도를 읽습니다. 제약과 오탐 사례는 [AUDIT_2026-11-09.md](../03-development/AUDIT_2026-11-09.md) 1장에 실측과 함께 있습니다.

**제목/아티스트 우선순위:** 곡 정보 텍스트에서 읽은 값 → BMS `#TITLE`/`#ARTIST` → 폴더 이름(아티스트는 `Custom Artist`). BMS 값은 텍스트에서 **값을 못 읽었을 때만** 씁니다. sxtg2 차트 대부분이 템플릿 값 `#TITLE test`를 갖고 있기 때문입니다(2026-11-08 PR #14).

**곡 정보 텍스트 키** (줄마다 `키: 값`, 키는 소문자로 바꾼 뒤 비교):
- 곡 제목: `곡 제목`, `title`, `曲名`, `歌名`, `标题`, `標題`(부분 일치), `제목`, `曲目`(정확 일치)
- 아티스트: `아티스트`, `artist`, `composer`, `艺术家`, `藝術家`, `曲师`, `曲師`, `作曲`, `作者`(부분 일치), `작곡가`(정확 일치)
- 난이도: `난이도`, `difficulty`, `level`, `难度`, `難度`, `等级`, `等級`(부분 일치). 값은 반각 쉼표·공백·탭으로 나눈 ASCII 숫자.

**현재 제약 (코드 그대로, 미수정):**
- 구분자는 **반각 콜론 `:`만**. 중국어 IME 기본인 전각 `：`로 쓴 줄은 통째로 무시됩니다.
- 난이도의 전각 쉼표 `，`, 모점 `、`, `/`, 전각 숫자 `１０`은 읽지 못해 템플릿 난이도로 대체됩니다.
- 부분 일치라 `副标题`/`subtitle`은 제목을, `谱面作者`/`chart artist`는 아티스트를 덮어씁니다(같은 키는 나중 줄이 이김).
- 파일은 **UTF-8**로 읽습니다. GBK·Big5·CP949·Shift-JIS로 저장한 파일은 글자가 깨집니다.
- 게임 언어가 한국어·일본어·영어뿐이라 간체 전용 글자는 □로 보일 수 있습니다(실기 미확인).

**`NormalizeMetadataText` (제목·아티스트·폴더 이름에 적용):**
- 전각 공백 `\u3000` → 반각 공백, 연속 공백 하나로, 앞뒤 공백 제거. 한글·한자·가나는 바꾸지 않습니다.
- 축약형 사이 공백 제거: `you'  re` → `you're`, `don ' t` → `don't`, `it ' s` → `it's`, `rock '  n '  roll` → `rock 'n' roll`, `3 o ' clock` → `3 o'clock`, `D ' Arc` → `D'Arc`.
- 따옴표 문자 `'` `’` `＇` `` ` `` `′`가 이 패턴에 걸릴 때만 `'`로 바뀝니다(`‘`는 대상 아님, 패턴 밖의 `’`는 그대로).
- 알려진 오탐(실측): `Guns N' Roses` → `Guns N'Roses`, `Mister 'T'` → `Mister'T'`, `the 'S' Club` → `the'S'Club`.

## 커스텀 곡 판별

`SixtarGlobal.GotoPlayScene` Prefix에서 아래 중 하나면 커스텀 곡입니다.
- `track.Category == "CUSTOM"`
- `track.TrackID`가 `CUSTOM_`으로 시작
- 표시 이름에 `(Custom 1-Note)` (빈 hwa 폴더용 시험 곡)

판별되면 `Core.IsCustomTrackActive = true`, `CustomTrackManager.CurrentSelectedAlbum` = 그 앨범.
`GotoMusicSelect` Postfix에서 둘 다 해제합니다(마라톤으로 다음 곡에 넘어갈 때는 유지).
