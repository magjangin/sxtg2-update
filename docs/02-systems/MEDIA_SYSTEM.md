# 자켓과 BGA

코드: `Managers/MediaOverrideManager.cs`, `Loaders/PngDecoder.cs`, `Core.cs` 6~10번 영역

## 자켓 로딩

| 경로 | 언제 | 방식 |
| --- | --- | --- |
| `RequestAlbumSprite` | 곡 선택(가운데, 목록 행) | 비동기: 작업 스레드에서 `PngDecoder`로 RGBA 디코딩 → 메인 스레드에서 `TextureFromRgba` 한 번 업로드 → 콜백 |
| `PreloadAll` | CUSTOM 탭 첫 진입 | 모든 자켓을 하나씩 백그라운드 로드 |
| `GetOrCreateAlbumSprite` | 로딩·일시정지·결과 | 캐시 조회 (없으면 동기 `LoadImage`) |

- 1080p PNG를 메인 스레드에서 `ImageConversion.LoadImage`하면 50~100ms 멈춥니다. 그래서 비동기로 바꿨습니다.
- Unity 자체 비동기 경로(`DownloadHandlerTexture`)는 이 빌드에서 생성자가 빠져 있어 못 씁니다.
- `PngDecoder`는 8비트·비인터레이스 PNG(색 형식 0/2/3/4/6)만 지원. 그 밖(JPG, 16비트 등)은 `LoadImage`로 대신합니다.

## 자켓을 붙이는 곳

모두 게임이 `Track/{id}` 번들에서 스프라이트를 비동기로 읽어 콜백에서 대입하는 곳입니다. 커스텀 ID는 null이 들어가 흰 박스/빈 칸이 되므로 그 뒤에 다시 붙입니다.

| 화면 | 훅 |
| --- | --- |
| 곡 선택 가운데 큰 자켓 | `TrackInfoViewer.Set` Postfix |
| 곡 선택 오른쪽 목록 행 | `TrackObject.SetTrack` + `TrackObject.__c__DisplayClass18_0._LoadThumbnail_b__1` Postfix |
| 로딩 | `PlayLoadingManager.OnLoadResource_Travel` + `DisplayClass34_0._b__2/_b__4` Postfix |
| 일시정지 | `Play.Pause.TrackInfoViewerModule.Set` Postfix + 0.1/0.4/1.0초 뒤 재적용 |
| 결과 | `Result.TrackInfoModule.DisplayClass13_0._Show_b__3/_b__4`, `TravelBackgroundModule.DisplayClass5_0._InitializeModule_b__1` |
| 플레이 배경 | `BackgroundUIProcessor.Initialize` Postfix + 재적용 코루틴 |

일시정지·플레이 배경은 콜백 대신 재적용 코루틴을 씁니다. 그 콜백(`_Set_b__3_1`, `<Initialize>b__1`)이 다른 함수 여러 개와 코드를 공유(ICF)해서 패치하면 엉뚱한 화면에서 실행되기 때문입니다.

> 목록 행 자켓 칸은 **있습니다.** 예전 기록에 "목록 프리팹에 자켓 Image 없음"이라 적혀 있어 2026-10-30 정리 때 훅을 지웠다가, 실제 화면에서 빈 칸이 나와 바로 복구했습니다.

## BGA (영상)

게임 원래 BGA 경로는 VideoClip 전용입니다.

```text
BackgroundUIProcessor.Initialize → Track/{id} 번들 → <Initialize>b__2(VideoClip)
  bgaPlayer.clip = vc     ← 커스텀은 null. 이 대입이 source를 Clip으로 되돌려 URL이 지워짐
  Prepare()
PlayBGA / PauseBGA / IsVideoPlayable: clip == null 이면 바로 return
```

모드:
- `Initialize` Postfix와 `b__2` Postfix에서 `clip = null` → `source = Url` → `url = 절대경로` 순서로 다시 설정 (`OverrideBGA`)
- `IsVideoPlayable` getter Postfix: true
- `PlayBGA` / `PauseBGA` Prefix: 원본 로직(시작 예약, 일시정지 예약, 지연, 자켓 숨김)을 clip 검사만 빼고 그대로 재현

경로는 반드시 절대경로입니다. 상대경로면 VideoPlayer가 `*_Data\hwa\…`에서 찾고 `WindowsMediaFoundation received empty file`을 냅니다.
