# 데이터 흐름

## 1. 게임 시작

```text
Core.OnInitializeMelon
  ├─ CustomTrackManager.Initialize      hwa\ 하위 폴더마다 BuildAlbumInfo
  │    ├─ TrackInfoParser.ParseTrackInfo    제목/아티스트/난이도
  │    ├─ BmsParser.ParseBmsFile            기본 BPM, 마지막 노트 끝 시각
  │    └─ 음원·프리뷰·영상·자켓 경로 (있는 파일만)
  ├─ ModConfig.Initialize               config.txt 생성/마이그레이션, 챌린지 토글 0으로
  ├─ CustomNoteSkin.Initialize          CustomNotes\*.png 로드
  └─ Harmony 클래스별 적용
```

## 2. 모드 선택 화면

```text
AttendanceController.Initialize → <WaitUpdateCheck>d__6 코루틴
  state 0~2: 업데이트 확인, 출석표·오늘 보상 받기
  state 3  : 받을 보상 있으면 AttendanceRewardPopup 열기
             └─ SkipAttendancePopup=1 이면 여기서 끝냄 (AttendancePopup.cs)
```

## 3. 곡 선택 (MusicSelect)

```text
CategorySelectController.LoadData (Postfix)      Categories에 "CUSTOM" 추가
RhythmGameUtils.GetSelectableCategoty (Postfix)  선택 가능 카테고리 배열에도 추가
TrackSelectController.GetTracks("CUSTOM") (Prefix, return false)
  └─ 처음 한 번: Starting Point TrackData를 틀로 앨범마다 복제 → 캐시
              + MediaOverrideManager.PreloadAll (자켓 백그라운드 디코딩)
TrackObject.SetTrack / <LoadThumbnail>b__1 (Postfix)  목록 행 자켓
TrackInfoViewer.Set (Postfix)                         가운데 큰 자켓
SoundModuleForFMOD.Play("preview_" + id) (Prefix)     프리뷰를 FMOD Core 스트림으로
TrackSelectController.SelectTrack / LoadData          결과에서 돌아올 때 커서 복귀
```

## 4. 플레이 진입

```text
SixtarGlobal.GotoPlayScene (Prefix)
  └─ CUSTOM 곡이면 IsCustomTrackActive = true, CurrentSelectedAlbum 설정
SixtarGlobal.<>c__DisplayClass54_0.<GotoPlayScene>b__12 (Prefix, return false)
  └─ FMOD 뱅크·SXGTBIN 조회를 건너뛰고 b__14(new TextAsset(차트 텍스트)) 직접 호출
PlayLoadingManager 콜백 (Postfix)                     로딩 화면 자켓
MikazukiConverter.Parse (Prefix, return false)
  └─ CustomTrackManager.LoadChartText (캐시) → 게임 BMSConverter.Parse → Pattern
```

## 5. 플레이 중

```text
SoundModuleForFMOD.Play(trackID, Game)      음원 스트림
BackgroundUIProcessor.Initialize / b__2      BGA URL, PlayBGA/PauseBGA 대체
JudgeProcessor.Judge                         세션 감지, AutoPlay
JudgeProcessor.OnTickEvent                   AllPerfect (RedStar였던 틱은 불빛 생략)
GearObject.PlayNoteEffect                    └─ 그 생략을 여기서 함
ScoreProcessor.UpdateScore                   MaxScore, 점수 반전 카운트다운
StartrailPatternRenderer.GetNoteObejct       커스텀/랜덤 노트 스킨
NoteObject.set_HeadPosition / set_NoteLength 속도 카오스, 히든/페이드 (켰을 때만)
FinishProcessor.UpdatePatternTime            곡 끝 = max(마지막 노트+3초, 음원 길이)
Core.OnLateUpdate                            판정바, 키뷰어
```

## 6. 결과

```text
TrackInfoModule / TravelBackgroundModule 콜백   결과 자켓
PlayRecordModule.Record*PlayData (Prefix)       기록 저장 차단 조건이면 막음
ManagerWebRequest / BackEndRankingService       점수 업로드 차단
SixtarGlobal.GotoMusicSelect (Prefix)           마라톤이면 다음 곡으로
```
