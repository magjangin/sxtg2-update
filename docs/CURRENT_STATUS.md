# 현재 상태 (2026-11-14)

게임 Steam Build `26455233`(ver 1.1.007, 커스텀 곡 플레이~결과 화면 실기 검증), MelonLoader 0.7.3, 패치 클래스 120개(`[Harmony] 패치 120개 모두 적용 완료`). 시작 로그의 `[UpdateGuard] 게임 빌드 … (실기 검증된 빌드)`로 빌드 확인.

## 기능별 상태

| 기능 | 상태 | 비고 |
| --- | --- | --- |
| CUSTOM 탭 + `hwa\` 곡 스캔 | 실기 확인 | 15곡으로 사용 중 |
| 커스텀 차트 플레이 (sxtg2 BMS 자동 변환) | 실기 확인 | 3자리 차트는 변환만 검증 |
| 프리뷰 / 인게임 음원 (FMOD Core 스트림) | 실기 확인 | 볼륨 페이드 추종은 실기 미확인 |
| 인게임 BGA (URL 재생) | 실기 확인 | |
| 자켓: 곡 선택 가운데, 로딩, 일시정지, 결과 | 실기 확인 | |
| 자켓: 곡 선택 오른쪽 목록 행 | 실기 확인 | 2026-10-30 잘못 지웠다가 복구 |
| 곡 끝 시점 = 음원 길이 | 실기 확인 | |
| AutoPlay | 실기 미확인 | |
| AllPerfect | 실기 확인 | 안 누른 롱노트 불빛 버그 수정 (2026-10-30) |
| MaxScore, 점수 반전 카운트다운 | 실기 미확인 | |
| 기록 저장 차단 | 실기 확인 | |
| 판정바 / 키뷰어 | 실기 확인 | 2026-10-30 성능 정리 후 재확인 필요 |
| 노트 속도 카오스, 히든, 페이드 | 실기 미확인 | |
| 커스텀 노트 스킨 / 랜덤 노트 스킨 | 실기 미확인 | |
| 마라톤 | 실기 미확인 | |
| 오프라인 가드·서버 종료 우회 (`OfflineBypass.cs`) | 실기 확인 (2026-11-07) | 리뉴얼 BACKND 42개(UnitySession 3개 포함) + `sxtg2 saver test` 24개 (총 66개 훅, 전부 `ICF=1`) |
| 전곡·DLC 해금 | 실기 확인 | |
| 출석 보상 창 자동 표시 끄기 | 실기 확인 | 로그 `[Attendance] Skipped ...` |

## 최근 변경 (2026-10-30 ~ 2026-11-12, `#n` = PR 번호)

1. **#1** AllPerfect가 안 누른 롱노트 틱에 레인 불빛을 켜던 문제 수정
2. **#2** README, 성능 정리(오버레이 IL2CPP 호출 제거, 디스크 접근 제거, 차트 캐시), 죽은 훅 제거 (4,217 → 3,746줄)
3. **#3** 출석 보상 창 자동 표시 끄기 (`SkipAttendancePopup`, config v6)
4. **#4** #2에서 잘못 지운 목록 행 자켓 훅 복구
5. **#5** 시작 시 노란 `[WARNING]` 2개 제거 (`TypeByName` → `typeof`), 실기 로그로 확인
6. **#6** 스킨 구성 기록, `button01`은 노트 스킨이 아님을 정정
7. **#7** 이 문서 묶음 추가
8. **#8** 자주 묻는 질문(FAQ) 추가
9. **#9** 비플레이 씬 `OnLateUpdate` 가드 + `Overlay.HideAll()`, BMS 파서/변환기 문자열 할당 제거, 잔여 Unity 구조체 생성자 제거 및 문서 최신화 (2026-10-31)
10. **#10** `sxtg2 saver test` 레거시 오프라인 패치 이식 및 리뉴얼(`SixtarGate.Web.BackEnd*`) 서버 종료/오프라인 완전 우회(`Features/OfflineBypass.cs`, 63개 훅) 구현 (2026-10-31)
11. **#11** 업데이트 내성 강화(`Managers/PatchResilience.cs`): 컴파일러 생성 클로저·코루틴(`__c__DisplayClass*`, `_*_d__*`, `_b__*`) 하드코딩 전면 제거 및 시그니처 기반 동적 탐색, 게임 오타 메서드 양방향 해석, 런타임 ICF 자명 스텁(1~3바이트) 가드, `TrackData` 속성 전체 자동 복제 (2026-10-31)
12. **#12** 오프라인 커스텀 곡 플레이 및 결과 화면 복구 (`OfflineBypass.cs` UnitySession 커넥터 등록 3개 훅), 모드 로그 한국어화(102개 문장), 실행 시 Steam 빌드 확인 및 Unity 예외 전달(`Managers/UpdateGuard.cs`), 업데이트 비교 도구(`tools/version_diff`) 추가 (2026-11-07)
13. **#13** 이중래퍼 람다 번역가(`Managers/LambdaTranslator.cs`): O(1) Fast-Path 캐시(`UserData/SixtarGateMod.LambdaMap.json`) + x64 기계어 대상 필드 변위 기반 스마트 구조 번역 (2026-11-07)
14. `04c94da` (main 직접 커밋) 중국어 간체·번체 메타데이터 키와 BMS `#TITLE`/`#ARTIST` 폴백 (2026-11-07). 실제로 읽히는 조건(반각 콜론·UTF-8)은 아래 알려진 한계 참고
15. `35bde96` (main 직접 커밋) 메모리 누수 방지: 공식 곡 전환 시 커스텀 오디오 스트림 해제, 디코딩/업로드 실패 텍스처 파괴, `NoteEffects` 동적 패치 해제, `JudgmentBar`·`KeyViewer` 리셋 (2026-11-07)
16. `1e3e64f` (main 직접 커밋) `TrackInfoParser.NormalizeMetadataText`: 영문 축약형 사이 공백·전각 공백 교정 (2026-11-07)
17. **#14** 제목/아티스트 우선순위 수정: `info.txt` → BMS `#TITLE` → 폴더 이름. BMS 폴백은 `info.txt`에서 값을 못 읽었을 때만 (CUSTOM 목록 15곡 중 14곡이 `test`로 나오던 문제, 2026-11-08 실기 확인)
18. **#15** 감사 (2026-11-09, 문서만): 중국어 메타데이터 로직·업데이트 내성·문서 정합성. 결과는 [AUDIT_2026-11-09.md](03-development/AUDIT_2026-11-09.md)
19. **#16** 감사 결과 수정 (2026-11-09): 곡 정보 전각 콜론·전각 쉼표/숫자·첫 값 채택·부제/채보자 키 무시·일본어 키, `TextDecoder`(UTF-8 외 인코딩), 축약형 교정 오탐 제거, 트랙 ID 충돌 번호 붙이기, `[CriticalPatch]` 필수 패치 표시, `GameAssembly.dll` 크기로 빌드 대조, 람다 스캔 확장·경고, `icf_check.py`. 오프라인 테스트 49건 통과, 실기 부팅(패치 120개·15곡·모드 선택 진입·예외 0) 확인, 커스텀 곡 플레이는 이번에 안 함

20. **#17** 코드 리뷰·업데이트 내성 강화 (2026-11-10): 실행 중 ICF 검사(`Managers/IcfScanner.cs`, 훅 대상 주소를 공유하는 IL2CPP 메서드 수를 세어 허용치 초과 패치를 건너뜀, `[IcfShared]`, 빌드별 캐시), `NoteEffects` 동적 훅 안전 확인, 버그 7건(플레이 진입 Prefix null 안전, 게임 BMS 해석기 예외 시 1노트 대체, 곡 끝 연장 로그, Unity 예외 전달 집합 상한, 깨진 자켓 반복 디코딩, `*.bms`가 `.bmson`까지 맞던 문제·정렬, 람다 번역 시간 표시), 매 프레임 `UpdatePatternTime` 조기 반환. 실기 부팅 3회(검사 결과가 `HOOK_TABLE.md`와 일치, 시험 패치 건너뜀 확인, 캐시 경로). 커스텀 곡 플레이는 이번에 안 함. [REVIEW_2026-11-10.md](03-development/REVIEW_2026-11-10.md)

21. **#18** 코드 리뷰·람다 찾기 강화 (2026-11-12): 후보에 바깥 형식 인스턴스 메서드(`this`만 잡은 람다) 포함, 람다 번호 정렬, 정확한 형식 우선, 캡처 필드·소유자 도달로 거르기, 레지스터를 따라가는 기계어 대조(`Managers/NativeLambdaScan.cs`, 모든 후보 대조·후보 1개도 검증), 사전에 `how`·RVA 대조·안 쓰는 키 정리, 코루틴 이름 정확 일치, `GotoPlayScene` 번들 콜백 `[CriticalPatch]`, ICF 검사 없이 붙은 패치 경고. 오프라인 시험 `tools/lambdascan` 35건, 실기 부팅 3회(옛 사전 → 새 형식, 직행 9개, 사전 삭제 후 재번역 9개 RVA 동일·경고 0). 커스텀 곡 플레이는 이번에 안 함. [REVIEW_2026-11-12.md](03-development/REVIEW_2026-11-12.md)

22. **#19** 병목 확인·람다 기계어 대조 명령 단위·사전 JIT 검사 (2026-11-14): 부팅 비용 실측(Harmony 120개 2.1초 = 패치당 고정 17ms, 튀는 패치 없음 → 패치별 시간 로그만 추가), `Managers/X64Decoder.cs`로 명령 경계를 따라 레지스터 추적(`lea`/`call`/`xor` 덮어쓰기 반영, capstone 대조 명령 188,398개 불일치 0), `FindMethodInType`도 번역 사전에(항목 10개), `Managers/PatchPreflight.cs`로 패치 코드를 미리 JIT해 업데이트로 사라진 인터롭 멤버를 쓰는 패치는 건너뜀, hwa 폴더 경로를 게임 루트로 통일. 실기 부팅 2회(120개 적용·사전 JIT 435개 54ms·재번역 10개 RVA 동일·경고 0). 커스텀 곡 플레이는 이번에도 안 함(사용자가 PC 사용 중). [REVIEW_2026-11-14.md](03-development/REVIEW_2026-11-14.md)
23. 전 파일 리팩토링·병목 개선·업데이트 내성 강화 (2026-11-14): `LambdaTranslator.TranslateMethodInType` 외부 타입/캐시 바인딩·로그 중복 수정, `PatchPreflight` IL/서브그래프 캐시 및 `InlineField`/`InlineType`/`InlineTok` 토큰 검사, `PatchResilience` Expression 델리게이트 캐시 + `CreateSetter` 타입 폴백 + 핵심 7개 패치 `[CriticalPatch]` 확대, `CustomTrackManager`/`Loaders` 폴더당 단일 디렉터리 스캔·무할당 `Span`/`char[]` 파싱, `UpdatePatternTime` 매 프레임 IL2CPP 프로퍼티 호출 제거, `tools/` 핸들·캐시 정리. Release 빌드(경고/오류 0), `lambdascan` 40건 `ALL PASS`, 실제 `hwa\` 15곡+엣지 케이스 SHA-256 동등성 100% 검증 및 `Mods/` 배포(실기 부팅은 PC 사용 중으로 생략). [REVIEW_2026-11-14.md](03-development/REVIEW_2026-11-14.md) 7장.

## 알려진 한계

- 오른쪽 곡 목록에서 MAX COMBO가 `---`로 나옵니다. 직접 채우는 시도를 했으나 화면에 반영되지 않아 뺐습니다.
- 키뷰어 키 이름은 리뉴얼 입력 모듈에서 읽고, 못 읽으면 레인 이름(LL, L …)을 씁니다.
- 결과 화면 특수 연출(시이 대신 다른 모델)은 커스텀 곡에 나오지 않습니다. 곡 ID로 고르기 때문입니다. [RESULT_ANIMATIONS.md](04-game-reference/RESULT_ANIMATIONS.md) 참고.
- 노란 줄 `[Il2CppInterop] Class::Init signatures have been exhausted`는 MelonLoader 자체 메시지라 남습니다.
- 곡 정보 파일이 UTF-8이 아니면 시스템 ANSI(한국어 PC = CP949)부터 시도하므로, 다른 나라 PC에서 "ANSI"로 저장한 파일(예: GBK)은 잘못 풀릴 수 있습니다. 간체 전용 글자는 게임 글꼴에 없을 수 있습니다(미확인). [CUSTOM_TRACKS.md](02-systems/CUSTOM_TRACKS.md).
- 실행 중 ICF 검사는 제네릭 인스턴스 코드를 세지 않습니다. 업데이트 후에는 `tools/version_diff/icf_check.py`로도 한 번 확인하세요([BUILD_AND_DEPLOY.md](03-development/BUILD_AND_DEPLOY.md)).
