# 플레이 진입과 곡 종료

코드: `Core.cs`

## 1. 뱅크·SXGTBIN 조회 건너뛰기

공식 곡의 원래 흐름:

```text
SixtarGlobal.GotoPlayScene
  → <>c__DisplayClass54_0.<GotoPlayScene>b__12(ISixtarAssetBundle ab)   (RVA 0x7EB2E0)
       ab.LoadAsset<TextAsset>(trackID)
  → b__13: RuntimeManager.LoadBank(ta), args.BankName = ta.name, "SXGTBIN_{pattern}" 조회
  → b__14: args.Pattern = ta.text, BGM 정지, MoveScene("Renewal_Play")
```

커스텀 곡은 뱅크 TextAsset이 null이라 `LoadBank`에서 NRE가 나고 로딩 화면이 멈춥니다.
→ `b__12` Prefix에서 커스텀이면 `args._BankName_k__BackingField = trackID`만 넣고 `b__14(new TextAsset(차트 텍스트))`를 직접 부른 뒤 `return false`.

## 2. 차트 주입

리뉴얼 플레이 씬에서 `PatternConverter.Initialize`가 `MikazukiConverter.Parse(string)`(RVA `0x7F0F20`)를 부릅니다.
Prefix에서 커스텀이면:

```text
bmsContent = CustomTrackManager.LoadChartText(album)   (실패 시 1노트 기본 차트)
__result = new BMSConverter().Parse(bmsContent)
return false
```

기본 차트 `#BPM 160 / #00211:01000000 / #08511:00000000`은 85마디(약 128초) 길이에 2마디째 노트 하나입니다. 1마디에 두면 시작선과 겹쳐서 2마디로 옮겼습니다.

## 3. 곡이 끝나는 시점

종료 조건 (`FinishProcessor.UpdatePatternTime`, RVA `0x6401A0`):

```text
TrackEndOffset + TrackEndTiming + _finishOffset(3초) <= patternTime  →  OnFinished
```

겪은 문제:
1. 노트 하나짜리 차트는 `TrackEndTiming`이 0이라 노트 치고 3초 만에 결과로 넘어감
2. `Initialize` Postfix에서 늘려도, 플레이 시작 직후 게임이 `TrackEndTiming = 0.3`으로 다시 씀 (실측)
3. `set_TrackEndTiming`은 11개 함수와 코드를 공유(ICF)해서 패치하면 안 됨
4. 노트보다 음원이 긴 곡(하얀 그리움: 노트 237.5초, 음원 257.8초)은 노트 끝나고 3초 뒤에 끝남

해결:
- **곡 길이 = max(`album.Duration`, Game 채널 스트림 길이)** (`CustomSongLength`)
- `UpdatePatternTime` Prefix: `offset + timing < 곡 길이`면 `TrackEndTiming = 곡 길이 - offset`
- `SoundModuleForFMOD.GetGamePlayAudioLength` Postfix: 곡 길이 반환 (원래는 Game 채널 이벤트가 없어 0)
- `FinishProcessor.Initialize` Postfix: 처음 값도 곡 길이로 (`LatestNoteTiming = 길이 - 3`)

## 4. 레거시 경로

예전에는 구버전 플레이 씬(`ManagerPlayOld.Set`, `SXGTReader.ReadByTextAsset`)과 `BMSConverter.Parse` 가드도 패치했습니다.
커스텀 곡은 리뉴얼 경로만 타고(실측 씬 목록에 구버전 플레이 씬 없음), 가드는 아무것도 바꾸지 않아 2026-10-30에 지웠습니다.
