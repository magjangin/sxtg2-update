# 구조

## 모듈 구성

```text
mod/
├── Core.cs                   진입점(MelonMod). CUSTOM 탭, 차트 주입, 오디오 연결, 곡 끝 시점
├── Features/
│   ├── OfflineBypass.cs      완전 오프라인 / 서버 종료 우회 (리뉴얼 BACKND 42개 + sxtg2 saver test 24개, 총 66개)
│   ├── Unlock.cs             전곡·DLC·아이템 보유 해금 오버라이드 (5개 훅)
│   ├── CustomBga.cs          인게임 커스텀 BGA URL 교체 및 재생/일시정지 (5개 훅)
│   ├── CustomJackets.cs      곡 선택/목록 행/로딩/일시정지/결과 자켓 비동기·지연 적용 (10개 훅)
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
│   ├── AtomicFile.cs         .tmp → File.Move 원자적 쓰기 (실패 시 .tmp 정리)
│   ├── BuildFingerprint.cs   게임 파일 지문(크기·PE 타임스탬프·메타데이터 헤더·로더 버전) 비교 및 실기 확인 추적(순수 로직)
│   ├── CustomTrackManager.cs hwa 스캔(작업 스레드)·변경 감지 재스캔, 앨범 정보, 차트 텍스트(캐시), TrackData 복제
│   ├── CustomAudioManager.cs FMOD Core 스트림 재생/일시정지/볼륨
│   ├── MediaOverrideManager.cs 자켓 비동기 로드, BGA URL 교체
│   ├── ModConfig.cs          SaveCustomKey\config.txt 읽기/마이그레이션
│   ├── PatchResilience.cs    업데이트 내성 (클로저·코루틴 동적 탐색, 오타 양방향 해석, ICF 가드로 패치 건너뛰기)
│   ├── IcfScanner.cs         실행 중 ICF 검사 (훅 대상 주소를 공유하는 IL2CPP 메서드 수, 빌드별 캐시)
│   ├── LambdaTranslator.cs   이중래퍼 람다 번역가 (Fast-Path 캐시 + x64 네이티브 오프셋 스마트 번역)
│   ├── NativeLambdaScan.cs   람다 기계어에서 소유자 → 대상 필드 로드 찾기 (명령 단위 레지스터 추적, 순수 함수)
│   ├── X64Decoder.cs         최소 x64 명령 길이 해독기 (NativeLambdaScan용, capstone 대조 시험)
│   ├── PatchPreflight.cs     패치 적용 전 사전 JIT 검사 (업데이트로 사라진 인터롭 멤버를 쓰는 패치 건너뜀)
│   ├── PatchSignatureCheck.cs 패치 인자 ↔ 훅 대상 시그니처 대조, 오버로드·바뀐 시그니처 고르기 (순수 리플렉션)
│   ├── ModLog.cs             작업 스레드 로그 버퍼 (메인 스레드에서 출력)
│   └── UpdateGuard.cs        Steam 빌드 ID 검증 및 Unity 내부 예외 포워딩
├── Loaders/
│   ├── BmsParser.cs          BPM, 마지막 노트 끝 시각, #TITLE/#ARTIST
│   ├── SxtgBmsConverter.cs   sxtg2 채널 → 게임 채널, 3자리 값 정규화
│   ├── PngDecoder.cs         관리 코드 PNG 디코더 (작업 스레드)
│   ├── TextDecoder.cs        UTF-8 외 인코딩 판별 (곡 정보, BMS)
│   └── TrackInfoParser.cs    info.txt / trackinfo.txt, 제목·아티스트 정규화
└── Models/CustomAlbumInfo.cs 앨범 한 개의 경로와 메타데이터
```

## Harmony 적용 방식

```text
[assembly: HarmonyDontPatchAll]          ← MelonLoader 자동 PatchAll 끔 (켜 두면 모든 훅이 2번 돎)
Core.OnInitializeMelon
  ├─ CustomTrackManager.Initialize: hwa 스캔을 작업 스레드에서 시작 (CustomNoteSkin PNG 해독도 작업 스레드)
  ├─ PatchResilience.ApplyAllPatches(HarmonyInstance, LoggerInstance, gameRoot)
       → PatchPreflight.Check: 패치 코드·호출 그래프 사전 JIT (사라진 인터롭 멤버면 건너뜀)
       → 대상 해석(TargetMethod / [HarmonyPatch], 오버로드·바뀐 시그니처는 패치 인자로 골라 ManualTargets)
       → PatchSignatureCheck.Check: 패치 인자 ↔ 대상 시그니처 (값 형식 불일치면 건너뜀)
       → 네이티브 주소 수집(+ NoteEffects 동적 대상) → IcfScanner.Prepare (같은 빌드면 UserData 캐시)
       → 대상별로 빈 스텁(1~3바이트 ret/xor/mov)이거나 공유 수 > 허용([IcfShared], 기본 1)이면 건너뜀
       → 나머지는 CreateClassProcessor(type).Patch() 또는 PatchManually
       → 하나가 실패해도 나머지는 적용, 로그 "[Harmony] 패치 N개 모두 적용 완료"
       → UpdateGuard.ReportPatchSummary: "업데이트 점검 결과:" 한 줄 + 지문 저장
  └─ (finally) CustomTrackManager.CompleteScan: 스캔 결과·로그를 메인 스레드에서 받음
```

예외 두 가지:
- `NoteEffects`의 `NoteObject.set_HeadPosition` / `set_NoteLength`는 매 프레임·노트마다 불리므로 `[HarmonyPatch]`를 붙이지 않고, 플레이 시작 때 필요하면 `HarmonyInstance.Patch`, 아니면 `Unpatch`합니다.
- 패치 대상은 항상 `typeof(...)`로 지정합니다. `AccessTools.TypeByName`은 모든 어셈블리를 훑다가 노란 경고를 냅니다.

## 매 프레임 코드

`Core.OnLateUpdate` 한 곳에서만 돌며, 플레이 씬(`Renewal_Play`)이거나 마라톤 결과 배너가 켜져 있을 때만 실행됩니다(그 외 씬에서는 초입에서 바로 반환).

```text
OnLateUpdate (Renewal_Play 또는 마라톤 결과 배너 표시 중일 때만)
  ├─ NoteEffects.OnFrame()        렌더러 스크롤 배율 1번 읽기 (히든/페이드용)
  ├─ CustomMarathon.Update(now)   결과 화면 Esc / 자동 진행 (v2.3.5: 오버레이와 무관하게 먼저 실행)
  ├─ Overlay.Begin()              캔버스 생존·화면 크기·배율은 30프레임마다만 조회
  ├─ JudgmentBar.Draw(now)
  ├─ CustomMarathon.Draw(now)
  ├─ KeyViewer.Draw()
  └─ Overlay.End()                안 그린 풀 객체 숨기기, 순서 바뀐 프레임만 재정렬
```

그 밖의 매 프레임 훅: `JudgeProcessor.Judge` Prefix(세션 감지·오토), `FinishProcessor.UpdatePatternTime` Prefix(곡 끝 시점).
이 두 곳은 딕셔너리 조회 수준만 합니다.
