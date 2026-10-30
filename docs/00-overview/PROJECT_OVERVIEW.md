# 프로젝트 개요

## 무엇을 하는 모드인가

**SixtarGateMod**는 Sixtar Gate: STARTRAIL의 현재 IL2CPP 빌드에서 돌아가는 MelonLoader 모드입니다.
Mono 시절 모드 `sxtg2`가 하던 일을 IL2CPP로 다시 만든 것이 출발점입니다.

세 가지가 중심입니다.

1. **내 곡 플레이**: `{게임}\hwa\` 폴더의 BMS 차트, 음원, 영상, 자켓을 게임 안의 별도 **CUSTOM** 탭에서 플레이합니다.
2. **플레이 보조**: 오토 플레이, 올 퍼펙트, 점수 상한, 판정바, 키뷰어, 노트 속도 카오스, 히든/페이드, 커스텀·랜덤 노트 스킨, 마라톤.
3. **오프라인 보호**: 서버 점검 팝업 때문에 꺼지지 않게 하고, 점수가 서버로 가지 않게 막고, 보조 기능으로 낸 기록은 저장하지 않습니다.

## 대상 버전

| 항목 | 값 |
| --- | --- |
| 게임 | Sixtar Gate: STARTRAIL (Steam AppID `1802720`) |
| Steam Build | `26218612` (2026-10-30 현재 공개 빌드와 같음) |
| 엔진 | Unity `2022.3.50f1`, IL2CPP x64, 메타데이터 v31 |
| 모드 로더 | MelonLoader 0.7.3 (.NET 6) |
| 모드 버전 | 2.2.0 (`mod/Core.cs`의 `MelonInfo`) |

## sxtg2와 다른 점

| 항목 | sxtg2 (Mono) | SixtarGateMod (IL2CPP) |
| --- | --- | --- |
| 코드 접근 | 게임 C#을 그대로 참조 | 인터롭 어셈블리(`Il2Cpp*`) + 네이티브 함수 분석 |
| 패치 | Prefix/Postfix/Transpiler | Prefix/Postfix만 (Transpiler 불가) |
| 플레이 씬 | 구버전 `ManagerPlay` | 리뉴얼 `Renewal_Play` (`StartrailPatternPlayer`) |
| 음원 | 게임 BGM 플레이어 교체 | FMOD Core 스트림 직접 재생 |
| 오버레이 | IMGUI (`OnGUI`) | uGUI 캔버스 (IMGUI가 빌드에서 빠짐) |
| 차트 | 게임 노트 데이터 직접 주입 | BMS 텍스트를 게임 `BMSConverter`에 넘김 |

## 저장소에 없는 것

공개 저장소라서 게임에서 나온 파일은 올리지 않습니다: `il2cpp_dump/`, `cpp2il_out/`, `src_dummy/`, `slices/`, `logs/`.
로컬에서 만드는 방법은 [REVERSE_ENGINEERING.md](../03-development/REVERSE_ENGINEERING.md)에 있습니다.
