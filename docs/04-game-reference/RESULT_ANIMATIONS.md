# 결과 화면 연출 (시이 / 특수 곡)

## 결론

결과 화면의 3D 캐릭터는 보통 **시이(`ShiiDefault` 모델)**입니다. **두 곡만** 다른 연출로 바뀝니다.

| 곡 ID | 연출 이름 | 3D 모델 | 영상 | 결과 BGM |
| --- | --- | --- | --- | --- |
| `zantetsuken` | `Zantetsuken` | **`Rath`** | `Result_zantetsuken` | `BGM_Result_Zantetsuken` |
| `lustroustravelremix` | `Christmas_2022` | **`Christmas_2022`** | `result_christmas_2022` | `BGM_Result_Christmas_2022` |
| (그 밖의 모든 곡) | `Default` | `ShiiDefault` | — | `BGM_Result` |

- 만우절·할로윈 곡은 이 목록에 없습니다. 바뀌는 곡은 위 두 곡뿐입니다. 하나는 2022 크리스마스 연출입니다.
- `Zantetsuken` 연출에는 "Voyage Complete" 글자, 섬광, 곡 자켓(`PlayJacket`)도 들어 있습니다.

## 조건 (`ResultAnimationController.TravelResultAnimation`)

```text
점수 > 700,000  이고  라이프 게이지 > 0 (클리어)
  ├─ 예   → ResultAnimationMap.Get(곡 ID)
  └─ 아니오 → (없음)
없으면 → Get("default")
```

즉 그 두 곡을 **70만 점을 넘겨 클리어**해야 특수 연출이 나옵니다. 실패하거나 70만 점 이하면 시이 기본 연출입니다.

- **Travel 모드만** 해당됩니다. Outer Space와 License Test의 맵에는 `default` 하나뿐입니다.
- License Test는 합격 여부로 BGM만 갈립니다(`BGM_LicenseTestSuccess` / `BGM_LicenseTestFail`).

## 데이터 위치

- 맵: `StreamingAssets\aa\StandaloneWindows64\contents_assets_all_*.bundle`의 `ResultAnimation/Travel`, `ResultAnimation/OuterSpace`, `ResultAnimation/LicenseTest` (`ResultAnimationMap`)
- 각 연출은 `ResultAnimationViewer` 프리팹이고, 하위 `Commands`의 액션을 차례로 실행합니다:
  `PlayBGMAction`(BGMkey), `PlayVideoAction`(videoKey), `PlayModelAction`(modelKey), `PlayJacketAction`, `PlayAnimAction`(클리어/실패 애니메이션)
- 읽는 스크립트: `tools/datadump/result_animation_map.py`, `prefab_tree.py`

## 커스텀 곡은?

커스텀 곡 ID(`CUSTOM_…`)는 맵에 없으므로 항상 시이 기본 연출입니다.
`ResultAnimationMap.Get` Postfix로 커스텀 곡에 특수 연출 id를 돌려주면 다른 연출을 띄울 수 있습니다(미구현).
