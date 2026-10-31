# SixtarGateMod 문서

Sixtar Gate: STARTRAIL(Unity IL2CPP)용 MelonLoader 모드 **SixtarGateMod** 문서 모음입니다.
Mono 시절 모드 `sxtg2`의 문서 구성(`sxtg2 커스텀 리드미`)을 따라 나눴습니다.

기준 시점: 2026-10-31, Steam Build `26218612`, 모드 소스 `mod/`.

## 먼저 볼 문서

| 문서 | 내용 |
| --- | --- |
| [01-user-guide/FAQ.md](01-user-guide/FAQ.md) | **자주 묻는 질문** (올 퍼펙트 불빛, 난이도, DLC 스킨, 결과 화면 시이, 모드 설명 등) |
| [CURRENT_STATUS.md](CURRENT_STATUS.md) | 지금 되는 것 / 실기 확인 여부 / 최근 변경 |
| [00-overview/PROJECT_OVERVIEW.md](00-overview/PROJECT_OVERVIEW.md) | 이 모드가 하는 일 한눈에 보기 |
| [01-user-guide/INSTALL_AND_LAYOUT.md](01-user-guide/INSTALL_AND_LAYOUT.md) | 설치, `hwa` 폴더 배치 |
| [01-user-guide/CONFIG_REFERENCE.md](01-user-guide/CONFIG_REFERENCE.md) | `config.txt` 모든 키 |
| [04-game-reference/MODE_SELECT.md](04-game-reference/MODE_SELECT.md) | 게임의 모드 선택 화면 설명 |
| [03-development/IL2CPP_PITFALLS.md](03-development/IL2CPP_PITFALLS.md) | 코드 고치기 전에 꼭 읽을 함정 목록 |

## 폴더 구조

```text
docs/
├── README.md               ← 지금 이 문서
├── CURRENT_STATUS.md
├── 00-overview/            # 개요, 구조, 데이터 흐름, 용어
├── 01-user-guide/          # FAQ, 설치·폴더 배치, 설정 키, 문제 해결
├── 02-systems/             # 기능별 동작 원리 (훅 대상, 이유)
├── 03-development/         # 코드 구조, 빌드·배포, IL2CPP 함정, 분석 방법, 디버깅
├── 04-game-reference/      # 게임 자체 정보 (모드, 결과 연출, 스킨, DLC, 난이도, 이벤트)
└── 90-archive/             # 초기 분석 메모 (최신 아님)
```

## 전체 목록

**00-overview**
- [PROJECT_OVERVIEW.md](00-overview/PROJECT_OVERVIEW.md): 목표, 기능 목록, 게임 버전
- [ARCHITECTURE.md](00-overview/ARCHITECTURE.md): 모듈 구성과 Harmony 적용 방식
- [DATA_FLOW.md](00-overview/DATA_FLOW.md): 곡 선택부터 결과까지 흐름도
- [GLOSSARY.md](00-overview/GLOSSARY.md): 용어집

**01-user-guide**
- [FAQ.md](01-user-guide/FAQ.md): 자주 묻는 질문
- [INSTALL_AND_LAYOUT.md](01-user-guide/INSTALL_AND_LAYOUT.md)
- [CONFIG_REFERENCE.md](01-user-guide/CONFIG_REFERENCE.md)
- [TROUBLESHOOTING.md](01-user-guide/TROUBLESHOOTING.md)

**02-systems**
- [CUSTOM_TRACKS.md](02-systems/CUSTOM_TRACKS.md): CUSTOM 탭, 트랙 데이터 복제, 곡 선택 보정
- [BMS_CONVERSION.md](02-systems/BMS_CONVERSION.md): sxtg2 BMS → 게임 BMS, 3자리 차트
- [PLAY_PIPELINE.md](02-systems/PLAY_PIPELINE.md): 플레이 씬 진입, 차트 주입, 곡 종료 시점
- [AUDIO_SYSTEM.md](02-systems/AUDIO_SYSTEM.md): FMOD Core 스트림, 프리뷰, 볼륨
- [MEDIA_SYSTEM.md](02-systems/MEDIA_SYSTEM.md): 자켓, 목록 썸네일, BGA
- [PLAY_ASSIST.md](02-systems/PLAY_ASSIST.md): 오토, 올 퍼펙트, 점수, 기록 차단
- [PLAY_OVERLAY.md](02-systems/PLAY_OVERLAY.md): 판정바, 키뷰어
- [NOTE_EFFECTS_AND_SKINS.md](02-systems/NOTE_EFFECTS_AND_SKINS.md): 속도 카오스, 히든/페이드, 노트 스킨
- [OFFLINE_AND_UNLOCK.md](02-systems/OFFLINE_AND_UNLOCK.md): 서버 차단, 해금, 출석 창
- [HOOK_TABLE.md](02-systems/HOOK_TABLE.md): 훅 전체 표 (RVA, 종류, 파일)

**03-development**
- [CODE_STRUCTURE.md](03-development/CODE_STRUCTURE.md)
- [BUILD_AND_DEPLOY.md](03-development/BUILD_AND_DEPLOY.md)
- [IL2CPP_PITFALLS.md](03-development/IL2CPP_PITFALLS.md)
- [REVERSE_ENGINEERING.md](03-development/REVERSE_ENGINEERING.md)
- [DEBUGGING_GUIDE.md](03-development/DEBUGGING_GUIDE.md)

**04-game-reference**
- [MODE_SELECT.md](04-game-reference/MODE_SELECT.md): 모드 7종과 테마 8종
- [RESULT_ANIMATIONS.md](04-game-reference/RESULT_ANIMATIONS.md): 결과 화면 시이(Shii) 연출과 특수 곡
- [SKINS.md](04-game-reference/SKINS.md): 기어/노트 스킨 짝
- [DLC.md](04-game-reference/DLC.md): DLC 11종 구성
- [DIFFICULTIES.md](04-game-reference/DIFFICULTIES.md): 난이도 5단계와 Starlight
- [EVENTS.md](04-game-reference/EVENTS.md): 만우절 흔적, 게임 내장 노트 흔들림

**90-archive**
- [hooks_initial_candidates.md](90-archive/hooks_initial_candidates.md): 프로젝트 초기 후킹 후보 메모

## 문서 신뢰도

- 기술 인수인계의 원본은 저장소 루트의 [AGENTS.md](../AGENTS.md)입니다. 이 폴더는 그 내용을 주제별로 풀어 쓴 것입니다. 둘이 다르면 AGENTS.md와 코드를 믿으세요.
- 게임 데이터에서 읽은 값(스킨, 모드, 결과 연출)은 `sharedassets0.assets`, `level5~12`, `contents_assets_all_*.bundle`을 UnityPy로 직접 읽어 확인했습니다. 읽는 방법은 [REVERSE_ENGINEERING.md](03-development/REVERSE_ENGINEERING.md)에 있습니다.
- "실기 미확인" 표시가 붙은 기능은 빌드·배포만 했고 게임에서 직접 보지 않은 것입니다.
