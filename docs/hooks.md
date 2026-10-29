# Sixtar Gate STARTRAIL IL2CPP 후킹 후보 목록

- 대상 모듈: `GameAssembly.dll`
- Unity 버전: `2022.3.50f1`
- 메타데이터 버전: `v31`

---

## 1. 핵심 후킹 후보 요약표

| 클래스 (Namespace 포함) | 메서드 / 멤버명 | RVA | VA (Base 0x180000000) | 시그니처 / 반환값 | 역할 및 모딩 활용 의도 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `SixtarGate.BMSConverter` | `Parse` | `0x7EDB00` | `0x1807EDB00` | `Pattern Parse(string content)` | **커스텀 BMS 차트 파서 훅**: BMS 텍스트 데이터를 받아 인게임 `Pattern` 객체로 변환하는 핵심 진입점. |
| `SixtarGate.Boot.BMSBootLoader` | `Boot` | `0x5B5780` | `0x1805B5780` | `void Boot(BootArgs args, [Optional] Func<bool> IsReady)` | **인게임 차트 로더 진입점**: 플레이할 차트의 `BootArgs`를 가로채 커스텀 차트 데이터 주입. |
| `SixtarGate.TrackLoader.TrackData` | `get_IsOwned` | `0x7F56A0` | `0x1807F56A0` | `bool get_IsOwned()` | **곡 소유권/해금 여부 반환**: `true` 반환 시 모든 곡 및 DLC 강제 해금. |
| `SixtarGate.TrackLoader` | `InitializeModule` | `0x7F5B50` | `0x1807F5B50` | `void InitializeModule()` | **트랙 목록 모듈 초기화**: 게임 시작 시 기본 트랙 리스트를 로드할 때 커스텀 트랙 데이터 추가 주입. |
| `SixtarGate.TrackLoader` | `CheckUnlockTracks` | `0x7F5780` | `0x1807F5780` | `void CheckUnlockTracks([Optional] Action callback)` | **곡 해금 검증 트리거**: 해금 상태 갱신 루틴 가로채기. |
| `JudgeCounter` | `AddJudge` | `0x59B2B0` | `0x18059B2B0` | `void AddJudge(EJudge judge)` | **인게임 판정 카운트**: 노트를 칠 때 판정 등급을 누적하는 함수. 판정 인자를 `Flawless`로 고정하여 **All-Perfect** 모드 구현 가능. |
| `JudgeCounter` | `AddJudgeBy` | `0x59B280` | `0x18059B280` | `void AddJudgeBy(int judgeIndex, int count)` | **판정 개수 직접 누적**: 롱노트/다중 판정 누적 시 호출. |
| `JudgeCounter` | `CalculateAccuracy` | `0x59B2E0` | `0x18059B2E0` | `static float CalculateAccuracy(int[] judges)` | **정확도 계산 공식**: 판정 카운트 배열을 기반으로 실시간/최종 정확도 산출. |
| `JudgeBalancer` | `ScoreRatioPerJudge` | `Offset 0x0` | - | `static float[]` | **판정별 점수 비율**: 각 판정 등급(Flawless, Blue Perfect 등)의 득점 계수 테이블. |
| `JudgeBalancer` | `BalanceList` | `Offset 0x10`| - | `static JudgeBalance[]` | **판정 타이밍 윈도우(ms)**: 밀리초 단위 판정 폭 배열(`float[] judgeRange`). |

---

## 2. 상세 분석 메모

### (1) `SixtarGate.BMSConverter::Parse`
- **의사코드 경로**: `logs/BMSConverter_Parse.rea.c`
- **로직 요약**:
  1. 개행 문자 `\r` 제거 및 `\n` 기준으로 분할.
  2. `#BPM` 태그 검색 및 기본 BPM 파싱.
  3. `ParseList`를 통해 BMS 채널별 라인 토큰 분리.
  4. 채널 `03`(BPM 변경) 및 `08`(확장 BPM 변경) 처리.
  5. 최종 `Sixtarithm.Pattern` 객체 반환.
- **모딩 적용**: 이 메서드의 `Prefix`에서 입력 BMS 문자열을 커스텀 BMS로 치환하거나, `Postfix`에서 파싱된 `Pattern` 인스턴스의 노트를 동적으로 수정 가능.

### (2) `JudgeCounter::AddJudge`
- **어셈블리 경로**: `logs/JudgeCounter.asm`
- **로직 요약**:
  - `rcx`: `JudgeCounter` 인스턴스 (Offset `0x10`에 `int[] judgeCount` 위치)
  - `edx`: 판정 등급 enum (`EJudge`)
  - `judgeCount[judge]++` 로직 수행
- **모딩 적용**: MelonLoader Harmony 훅에서 `judge = EJudge.Flawless` (또는 인덱스 0)로 강제 변경(`ref` 패치)하면 손쉽게 올 퍼펙트 판정 처리 가능.
