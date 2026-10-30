# 오디오 (FMOD Core 스트림)

코드: `Managers/CustomAudioManager.cs`, `Core.SoundModuleForFMOD_Play_Patch`

## 게임이 소리를 내는 방식

| 장면 | 호출 |
| --- | --- |
| 곡 선택 프리뷰 | `ISoundModule.Play("preview_" + trackID, BGM)` |
| 플레이 | `ISoundModule.Play(trackID, Game)` |

`SoundModuleForFMOD.Play`(RVA `0x7E11E0`)는 `event:/{audioName}` FMOD Studio 이벤트를 만듭니다. 커스텀 ID에는 이벤트가 없어 `EventNotFoundException`이 나고 무음입니다.

## 모드가 하는 일

`Play` Prefix에서 이름이 `CUSTOM_…`(또는 `preview_CUSTOM_…`)이면:

```text
module.Stop(channel)                                    이전 스트림 정리
RuntimeManager.CoreSystem.createStream(path, CREATESTREAM | LOOP_OFF)
core.playSound(sound, masterGroup, paused: true)
channel.setVolume(module.GetVolume(ch) * module.CurrectionVolume)
position > 0 이면 setPosition
channel.setPaused(false)
return false
```

프리뷰 파일: `demo.*` → `music.*` → 첫 오디오 파일. 경로는 `Path.GetFullPath`.

## 같이 구동하는 함수

이 채널은 게임의 `_soundChannels`(Studio 이벤트 사전) 밖에 있으므로, 원본이 무시하는 경우에도 같이 움직여야 합니다. 원본 다섯 개는 모두 `ContainsKey` 가드가 있고 고유 주소(ICF 없음)입니다.

| 원본 | 모드 |
| --- | --- |
| `Stop(channel)` | Prefix: 스트림 정지·해제 |
| `Pause(channel)` | Prefix: `setPaused(true)` |
| `Resume(channel)` | Prefix: `setPaused(false)` |
| `Resume(time, channel)` | Prefix: `setPosition(time*1000)` 후 재개 |
| `GetTimelinePosition(channel)` | Postfix: 스트림 위치 반환 |
| `GetGamePlayAudioLength` | Postfix: 곡 길이 반환 ([PLAY_PIPELINE.md](PLAY_PIPELINE.md)) |

## 볼륨 따라가기

- 씬 전환: `SceneLoader.<FadeIn>` / `<FadeOut>`이 매 프레임 `CurrectionVolume`을 1→0 / 0→1로 바꿈
- 옵션 변경: `ChangeVolume`(RVA `0x7DF860`)

둘 다 원본은 Studio 이벤트에만 `setVolume`합니다. `set_CurrectionVolume`(RVA `0x7E27B0`)과 `ChangeVolume` Postfix에서 스트림에도 `GetVolume(ch) * CurrectionVolume`을 다시 적용합니다.
이게 없으면 곡 선택 진입 페이드 중에 시작된 프리뷰가 그 순간 볼륨으로 굳어 작게 들렸습니다.

## 실패했던 방법

`SoundModuleForFMOD.PlayLocalSound`(programmer sound 이벤트)로 재생: 그 이벤트 타임라인이 약 3분 고정이라 긴 곡은 중간부터 무음이었고, 그 길이가 `GetGamePlayAudioLength`로 새어 186초에 결과로 넘어갔습니다.
