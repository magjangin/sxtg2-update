# 용어집

| 용어 | 뜻 |
| --- | --- |
| **IL2CPP** | Unity가 C#을 C++로 바꿔 네이티브로 컴파일하는 방식. 게임 로직이 `GameAssembly.dll` 안에 기계어로 들어 있음 |
| **인터롭 어셈블리** | MelonLoader가 만든 `Il2Cpp*` C# 껍데기 DLL. 호출하면 실제로는 네이티브 함수로 넘어감 (`MelonLoader\Il2CppAssemblies`) |
| **RVA** | `GameAssembly.dll` 안 함수 주소(이미지 기준). `dump.cs` / `script.json`에 적혀 있음 |
| **ICF** | 동일 코드 접기. 내용이 같은 함수들이 네이티브 주소 하나를 공유. 그런 주소를 패치하면 다른 함수까지 같이 걸림 |
| **pe_slice** | 70MB `GameAssembly.dll`에서 필요한 함수만 잘라 작은 PE로 만드는 도구. REA/Ghidra 분석 시간 단축 |
| **REA** | 역공학 도구 MCP 서버. Ghidra로 의사코드를 뽑음 |
| **Prefix / Postfix** | Harmony 패치. 원본 함수 전/후에 끼워 넣는 코드. Prefix가 `false`를 돌려주면 원본을 건너뜀 |
| **리뉴얼 플레이 씬** | 현재 플레이 화면 `Renewal_Play` (`StartrailPatternPlayer`, `JudgeProcessor`). 구버전 `ManagerPlayOld`와 구분 |
| **Travel** | 일반 곡 선택 플레이 모드 (게임 내 이름 TRAVEL MODE) |
| **Solar / Lunar** | 플레이 스타일. Solar = 7레인(`EPlayStyle.STANDARD`), Lunar = 5레인(`EPlayStyle.LIGHT`) |
| **COMET / NOVA / SUPERNOVA / QUASAR / STARLIGHT** | 난이도 5단계. Starlight는 일부 곡만 ([DIFFICULTIES.md](../04-game-reference/DIFFICULTIES.md)) |
| **BLUESTAR / WHITESTAR / YELLOWSTAR / REDSTAR** | 판정 4단계 (`EJudge` 0~3). REDSTAR가 미스 |
| **틱** | 롱노트를 일정 간격으로 쪼갠 판정 단위. 롱노트 몸통도 틱마다 판정됨 |
| **SXGTBIN** | 게임 공식 곡의 바이너리 차트. 커스텀 곡은 이 대신 BMS 텍스트를 씀 |
| **sxtg2 BMS** | sxtg2 모드용 BMS 형식. 레인을 채널(16/11/12/13/14/15/18)로, 노트 종류를 값(01~05)으로 표현 |
| **hwa 폴더** | `{게임}\hwa\` 커스텀 곡 폴더. 하위 폴더 하나 = 곡 하나 |
| **CUSTOM 탭** | 곡 선택 카테고리 줄에 모드가 추가하는 탭. 커스텀 곡만 들어감 |
| **ARKK** | 게임 내 화폐. 출석 보상 등으로 받음 |
| **시이 (Shii)** | 결과 화면 기본 3D 캐릭터 (`ShiiDefault` 모델) |
| **BACKND (뒤끝)** | 게임이 쓰는 서버 SDK. 랭킹, 출석 등 |
