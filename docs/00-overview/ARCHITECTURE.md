# 구조

## 모듈 구성

```text
mod/
├── Core.cs                   진입점(MelonMod). CUSTOM 탭, 차트/미디어 주입, 곡 끝 시점, 해금
├── Features/
│   ├── OfflineBypass.cs      완전 오프라인 / 서버 종료 우회 (리뉴얼 BACKND 39개 + sxtg2 saver test 24개)
│   ├── PlayAssist.cs         판정 파이프라인: AutoPlay, AllPerfect, MaxScore, 점수 반전, 기록 차단
│   ├── Overlay.cs            uGUI 즉시모드 그리기 (Box/Rgba, 값 캐시)
│   ├── JudgmentBar.cs        판정바
│   ├── KeyViewer.cs          키뷰어
│   ├── NoteEffects.cs        속도 카오스, 히든, 페이드 (쓸 때만 패치)
│   ├── CustomNoteSkin.cs     CustomNotes\*.png 노트 스킨
│   ├── RandomNoteSkin.cs     게임 노트 스킨 무작위
│   ├── CustomMarathon.cs     커스텀 곡 연속 플레이
│   ├── CustomMusicSelect.cs  결과 → 목록 복귀 시 커서 위치
│   └── AttendancePopup.cs    출석 보상 창 자동 표시 끄기
├── Managers/
│   ├── CustomTrackManager.cs hwa 스캔, 앨범 정보, 차트 텍스트(캐시), TrackData 복제
│   ├── CustomAudioManager.cs FMOD Core 스트림 재생/일시정지/볼륨
│   ├── MediaOverrideManager.cs 자켓 비동기 로드, BGA URL 교체
│   └── ModConfig.cs          SaveCustomKey\config.txt 읽기/마이그레이션
├── Loaders/
│   ├── BmsParser.cs          BPM, 마지막 노트 끝 시각
│   ├── SxtgBmsConverter.cs   sxtg2 채널 → 게임 채널, 3자리 값 정규화
│   ├── PngDecoder.cs         관리 코드 PNG 디코더 (작업 스레드)
│   └── TrackInfoParser.cs    info.txt / trackinfo.txt
└── Models/CustomAlbumInfo.cs 앨범 한 개의 경로와 메타데이터
```

## Harmony 적용 방식

```text
[assembly: HarmonyDontPatchAll]          ← MelonLoader 자동 PatchAll 끔 (켜 두면 모든 훅이 2번 돎)
Core.OnInitializeMelon
  └─ 모드 어셈블리의 [HarmonyPatch] 클래스마다
       HarmonyInstance.CreateClassProcessor(type).Patch()
       → 하나가 실패해도 나머지는 적용, 로그 "All patches applied successfully! (N)"
```

예외 두 가지:
- `NoteEffects`의 `NoteObject.set_HeadPosition` / `set_NoteLength`는 매 프레임·노트마다 불리므로 `[HarmonyPatch]`를 붙이지 않고, 플레이 시작 때 필요하면 `HarmonyInstance.Patch`, 아니면 `Unpatch`합니다.
- 패치 대상은 항상 `typeof(...)`로 지정합니다. `AccessTools.TypeByName`은 모든 어셈블리를 훑다가 노란 경고를 냅니다.

## 매 프레임 코드

`Core.OnLateUpdate` 한 곳에서만 돌며, 플레이 씬(`Renewal_Play`)이거나 마라톤 결과 배너가 켜져 있을 때만 실행됩니다(그 외 씬에서는 초입에서 바로 반환).

```text
OnLateUpdate (Renewal_Play 또는 마라톤 결과 배너 표시 중일 때만)
  ├─ NoteEffects.OnFrame()        렌더러 스크롤 배율 1번 읽기 (히든/페이드용)
  ├─ Overlay.Begin()              화면 크기·배율 1번 읽기
  ├─ CustomMarathon.Update(now)   결과 화면 Esc / 자동 진행
  ├─ JudgmentBar.Draw(now)
  ├─ CustomMarathon.Draw(now)
  ├─ KeyViewer.Draw()
  └─ Overlay.End()                안 그린 풀 객체 숨기기, 순서 바뀐 프레임만 재정렬
```

그 밖의 매 프레임 훅: `JudgeProcessor.Judge` Prefix(세션 감지·오토), `FinishProcessor.UpdatePatternTime` Prefix(곡 끝 시점).
이 두 곳은 딕셔너리 조회 수준만 합니다.
