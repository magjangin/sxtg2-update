# 빌드와 배포

## 준비물

- .NET SDK 6.0 이상
- 게임에 MelonLoader 0.7.3을 설치하고 한 번 실행 (`MelonLoader\Il2CppAssemblies` 생성)

`mod/SixtarGateMod.csproj`는 `$(GameDir)` 아래에서 MelonLoader·Interop DLL을 참조합니다(기본값 `H:\steam\steamapps\common\Sixtar Gate STARTRAIL`). 경로가 다르면 환경변수 `SIXTAR_GAME_DIR`을 설정하거나 `mod/Directory.Build.props.user`(Git 무시)에서 `<GameDir>`을 덮어쓰세요.

```text
$(GameDir)\MelonLoader\net6\MelonLoader.dll, 0Harmony.dll, Il2CppInterop.*.dll
$(GameDir)\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll 외
```

## 빌드

```powershell
dotnet build mod/SixtarGateMod.csproj
```

결과: `mod\bin\Debug\net6.0\SixtarGateMod.dll`. 경고 0 / 오류 0이 정상입니다.

## 테스트 (게임 DLL 불필요)

```powershell
dotnet test tests/SixtarGateMod.Tests/SixtarGateMod.Tests.csproj
```

## 모드 본체 컴파일 검사 (게임 DLL 불필요, 2026-11-23)

```powershell
dotnet run --project tools/bodycheck -c Release            # 검사
dotnet run --project tools/bodycheck -c Release -- --self-test   # 검사기가 일부러 넣은 오류를 잡는지 확인
dotnet run --project tools/bodycheck -c Release -- --interop "<게임>\MelonLoader\Il2CppAssemblies"   # 로컬 전체 검사
```

단위 테스트는 순수 모듈만 net8.0으로 컴파일하므로, 모드 본체(net6.0)에서만 깨지는 오류(.NET 7+ 전용 API, 모드 내부 메서드 이름 오타, 문법 오류)는 게임이 있는 PC에서 빌드할 때까지 보이지 않았습니다. `tools/bodycheck`는 Roslyn으로 `mod/**/*.cs` 전체를 `SixtarGateMod.csproj`와 같은 설정으로 컴파일합니다. 참조는 net6.0 참조 어셈블리와 공개 NuGet의 MelonLoader 0.7.3·HarmonyX 2.10.2·Il2CppInterop 1.5.1입니다. CI에서 단위 테스트 다음에 돌고, 실패하면 종료 코드 1입니다.

- 같은 소스를 net8.0 참조로도 컴파일해, net6에서만 나는 오류를 **.NET 7+ API**로 보고합니다.
- 게임 인터롭이 없어 생기는 오류(형식·네임스페이스를 못 찾음, `SixtarGlobal.X`처럼 못 찾은 대문자 이름의 멤버 접근)와, 못 찾은 게임 형식이 낀 문장에서 나는 연쇄 오류는 거릅니다(2026-11-23 기준 각각 844건, 92건). 나머지는 전부 보고합니다.
- **한계:** 게임 API의 멤버 이름·시그니처는 검사하지 못합니다(로컬 빌드와 `tools/sigcheck` 몫). 게임 형식이 낀 문장 안의 다른 실수도 같이 가려지고, 모드 자기 형식 이름의 오타는 "형식을 못 찾음"으로 분류돼 걸러집니다. NuGet의 MelonLoader/Il2CppInterop은 게임 폴더의 것과 빌드가 다를 수 있습니다. `--interop` 전체 검사는 게임 PC에서 아직 돌려 보지 않았습니다.

## 배포

`$(GameDir)\Mods` 폴더가 존재하면 빌드 후 자동으로 `SixtarGateMod.dll`을 복사합니다(게임 실행 중 DLL이 잠겨 있으면 경고만 출력하고 빌드는 성공). 자동 복사를 건너뛰려면 `-p:SkipDeploy=true`를 넘기세요.

## 확인

1. 게임 실행
2. `MelonLoader\Latest.log`에서 `[Harmony] 패치 N개 모두 적용 완료`
3. 바꾼 기능을 게임에서 직접 확인
4. 게임 내부 오류는 `%USERPROFILE%\AppData\LocalLow\Lyrebird Studio\Sixtar Gate STARTRAIL\Player.log`

## 저장소 작업 흐름

- 원격: `https://github.com/magjangin/sxtg2-update` (공개)
- 기능마다 브랜치 → PR → merge → 원격·로컬 브랜치 삭제
- 커밋하지 않는 것(`.gitignore`): `mod/bin`, `mod/obj`, `mod/Directory.Build.props.user`, `tests/*/bin`, `tests/*/obj`, `tools/*/bin`, `tools/*/obj`, `il2cpp_dump/`, `cpp2il_out/`, `src_dummy/`, `slices/`, `logs/`, `input/`, `*.dll`, `.reatool/`

## 게임 버전 고정

Steam Build `26218612`에서 만들고 `26455233`에서 실기 검증했습니다. 게임이 업데이트되면 함수 주소와 구조가 바뀌어 일부 패치가 실패할 수 있습니다.

업데이트 후 점검 순서:
1. 게임을 한 번 실행합니다(MelonLoader가 interop과 Cpp2IL 출력을 다시 만듦). 시작 로그의 `[UpdateGuard]`가 검증되지 않은 빌드라고 경고하고, `지난 실행과 달라진 것: … A → B`로 `GameAssembly.dll`·`global-metadata.dat`·`UnityPlayer.dll`의 크기·PE 링크 시각·메타데이터 버전/헤더 변화와 MelonLoader/Il2CppInterop 버전(`loaderVersion`, 2026-11-23~, 옛 기록에 없으면 비교 안 함) 변화를 알립니다(2026-11-18부터 크기가 같은 재빌드도 잡음. `UserData\SixtarGateMod.BuildState.txt`와 비교. 메타데이터·Unity 런타임·모드 로더가 바뀌면 인터롭 형식·필드 오프셋이 달라졌을 수 있으니 2~3단계를 특히 꼼꼼히). 패치 적용 뒤 `업데이트 점검 결과:` 한 줄이 정상/일부 빠짐/필수 빠짐/검증 안 됨을 요약합니다. 지문은 패치가 전부 붙었을 때만 갱신됩니다.
2. `powershell -ExecutionPolicy Bypass -File tools\version_diff\check_update.ps1` — 기준 `cpp2il_out`과 새 출력의 형식·필드·시그니처·RVA 차이, 그중 모드가 훅하는 형식에 걸린 줄, `slices/` 함수 바이트 비교를 출력합니다. Release 빌드된 모드 DLL이 있으면 5단계로 `tools/sigcheck`를 돌려 모든 패치의 Prefix/Postfix/Finalizer 인자를 **새** `Il2CppAssemblies`의 훅 대상 시그니처와 대조합니다(게임을 켜기 전에 확인 가능, 종료 코드 1 = 부팅 시 `[Harmony/Signature]`로 건너뛰어질 패치 있음). 단독 실행: `dotnet run --project tools\sigcheck -- [모드 DLL] [게임 폴더]`. 빌드 `26455233` 기준 통과 114·오류 0·경고 0·오버로드로 고름 0·바뀐 시그니처로 고름 0(2026-11-23), 클로저 콜백 4개는 IL2CPP 런타임이 있어야 대상을 찾으므로 "대상 미확인"으로 셉니다(부팅 시에는 검사됨).
3. 모드를 다시 빌드하고(컴파일 오류 = 시그니처 변경), 로그의 `[Harmony] 패치 '...' 실패`, `[Harmony/Preflight] … 건너뜀`(인터롭에서 사라진 멤버 — 컴파일은 옛 interop으로 했거나 이름은 같고 형식이 바뀐 경우), `[Harmony/Signature] … 건너뜀`(훅 대상 인자의 이름·값/포인터 형식이 바뀜 — Harmony는 이름으로만 묶어 그대로 붙이면 잘못된 값·크래시), `[Harmony/Signature] … 참조 형식 불일치`(경고만), `[Harmony/Resolve]`(오타 대응 메서드를 정확한 시그니처로 못 찾아 이름·개수로 대체했거나, 속성 대상에 오버로드가 생겨 패치 인자로 자동 선택/보류했거나, 인자 형식을 지정한 대상이 없어져 같은 이름 하나뿐인 메서드를 패치 인자로 확인해 골랐음 — 하나라도 고르면 `업데이트 점검 결과`가 "정상" 대신 실기 확인을 요청. sigcheck에서는 `오버로드`/`시그니처변경` 줄), `[Harmony/ICFGuard] … 건너뜀`, `[Harmony] 필수 패치 빠짐` 줄을 확인합니다. `[ICFScan]`이 새 빌드에서 공유 개수를 다시 세어 `UserData/SixtarGateMod.IcfCounts.txt`에 남깁니다(대상별 `0xRVA=개수|공유 메서드`). 메타데이터가 바뀌었으면 `il2cpp_dump`도 다시 만듭니다([REVERSE_ENGINEERING.md](REVERSE_ENGINEERING.md)).
   - **ICF 개수 재확인:** 새 `GameAssembly.dll`로 Il2CppDumper를 돌려 얻은 `script.json`을 `check_update.ps1 -ScriptJson <경로>`에 주면(또는 `python -I tools\version_diff\icf_check.py <script.json> docs\02-systems\HOOK_TABLE.md`) 정적 패치 118개(+ 동적 2개)의 주소 공유 개수를 다시 셉니다. `[ICF 증가]`가 나온 대상은 업데이트로 다른 함수와 접힌 것이라 패치하면 엉뚱한 `this`로 실행됩니다(실행 중 `IcfScanner`도 같은 것을 자동으로 잡아 건너뛰지만, 제네릭 인스턴스 코드는 세지 않으므로 이 스크립트를 보조로 씀). 2026-11-09 빌드 `26455233` 기준 결과: 증가 0, 못 찾음 0. 2026-11-10 `IcfScanner` 결과도 같음.
   - **람다 번역:** 빌드가 바뀌면 `[LambdaTranslator] 게임 빌드 변경 감지 -> … 재번역`과 `번역 완료: 새로 번역 10개`가 나옵니다(2026-11-14 전에는 9개). `'…' 기계어 대조 실패`, `필드 '…'를 읽는 후보가 n개`, `유일한 후보 …의 본문에서 소유자는 읽지만` 경고가 있으면 그 람다(대개 자켓 콜백)를 `dis.py`로 열어 어느 필드를 읽는지 보고, `config.txt`의 `DebugLog=1`로 `how=` 값을 확인합니다. 기계어 대조 코드를 고쳤다면 `dotnet run --project tools\lambdascan -- "<게임>\GameAssembly.dll"`(RVA는 빌드 `26455233` 기준이라 새 빌드에선 `dis.py`로 찾은 RVA로 바꿔야 함)로 확인합니다. 새 빌드에서 `X64Decoder`가 모든 명령을 읽는지는 `python -I tools\lambdascan\capstone_lengths.py <GameAssembly.dll> <새 script.json> caplens.txt` 후 lambdascan 두 번째 인자로 `caplens.txt`를 주면 확인됩니다(`길이 다름 0, 미지원에서 멈춤 0`이어야 함).
4. **커스텀 곡 1곡을 결과 화면까지 플레이**하고, `Latest.log`의 `[UpdateGuard] 게임 내부 예외` 경고(원래 `Player.log`에만 찍히던 Unity 예외)를 확인합니다. 부팅·곡 선택만 보고 넘어가면 안 됩니다. 결과 화면까지 게임 내부 예외 없이 가면 모드가 `[UpdateGuard] … 이 PC에서 실기 확인된 지문으로 기록했습니다`를 찍고 `BuildState.txt`에 `verifiedPlay=`를 남깁니다(다음 부팅부터 이 PC에서는 "검증 안 됨" 경고가 사라짐, 게임 파일이 바뀌면 자동으로 버려짐). 예외가 있었으면 `플레이 중 게임 내부 예외 N건` 경고가 대신 나옵니다.
5. 문제가 없으면 `mod/Managers/UpdateGuard.cs`의 `VerifiedBuilds`에 빌드 ID를, `VerifiedGameAssemblySizes`에 그 빌드의 `GameAssembly.dll` 크기(바이트)를 추가하고(자동 기록은 이 PC에만 남으므로 다른 설치를 위해 소스에도 적음), `check_update.ps1 -UpdateBaseline`으로 기준을 교체합니다. 크기 기록은 `.acf`를 고쳐 업데이트를 막았거나 매니페스트 없이 설치한 경우에도 빌드를 맞게 판별하는 데 씁니다.

함수 본문 확인은 `python -I tools\version_diff\dis.py <GameAssembly.dll> il2cpp_dump\script.json <이름 일부 또는 0xRVA>`(call 대상 이름 표시)로 할 수 있습니다. 반대로 어떤 클래스를 누가 부르는지는 `python -I tools\version_diff\xrefs.py <GameAssembly.dll> il2cpp_dump\script.json <클래스 전체 이름>`(직접 call/jmp + TypeInfo/MethodInfo 참조. 가상·인터페이스 호출과 씬의 MonoBehaviour 메시지는 안 보임).
