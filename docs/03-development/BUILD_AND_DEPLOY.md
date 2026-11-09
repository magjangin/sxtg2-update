# 빌드와 배포

## 준비물

- .NET SDK 6.0 이상
- 게임에 MelonLoader 0.7.3을 설치하고 한 번 실행 (`MelonLoader\Il2CppAssemblies` 생성)

`mod/SixtarGateMod.csproj`는 다음 DLL을 `HintPath`로 참조합니다. 게임 경로가 다르면 csproj에서 바꾸세요.

```text
H:\steam\steamapps\common\Sixtar Gate STARTRAIL\MelonLoader\net6\MelonLoader.dll, 0Harmony.dll, Il2CppInterop.*.dll
H:\steam\steamapps\common\Sixtar Gate STARTRAIL\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll 외
```

## 빌드

```powershell
& "C:\Program Files\dotnet\dotnet.EXE" build "H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\mod\SixtarGateMod.csproj"
```

결과: `mod\bin\Debug\net6.0\SixtarGateMod.dll`. 경고 0 / 오류 0이 정상입니다.

## 배포

**게임을 끈 상태에서** 복사합니다(켜져 있으면 DLL이 잠겨 `Device or resource busy`).

```powershell
Copy-Item -Path "H:\source\repos\Sixtar Gate STARTRAIL-unity-il2cpp\mod\bin\Debug\net6.0\SixtarGateMod.dll" -Destination "H:\steam\steamapps\common\Sixtar Gate STARTRAIL\Mods\SixtarGateMod.dll" -Force
```

## 확인

1. 게임 실행
2. `MelonLoader\Latest.log`에서 `[Harmony] 패치 N개 모두 적용 완료`
3. 바꾼 기능을 게임에서 직접 확인
4. 게임 내부 오류는 `%USERPROFILE%\AppData\LocalLow\Lyrebird Studio\Sixtar Gate STARTRAIL\Player.log`

## 저장소 작업 흐름

- 원격: `https://github.com/magjangin/sxtg2-update` (공개)
- 기능마다 브랜치 → PR → merge → 원격·로컬 브랜치 삭제
- 커밋하지 않는 것(`.gitignore`): `mod/bin`, `mod/obj`, `il2cpp_dump/`, `cpp2il_out/`, `src_dummy/`, `slices/`, `logs/`, `input/`, `*.dll`, `.reatool/`

## 게임 버전 고정

Steam Build `26218612`에서 만들고 `26455233`에서 실기 검증했습니다. 게임이 업데이트되면 함수 주소와 구조가 바뀌어 일부 패치가 실패할 수 있습니다.

업데이트 후 점검 순서:
1. 게임을 한 번 실행합니다(MelonLoader가 interop과 Cpp2IL 출력을 다시 만듦). 시작 로그의 `[UpdateGuard]`가 검증되지 않은 빌드라고 경고합니다.
2. `powershell -ExecutionPolicy Bypass -File tools\version_diff\check_update.ps1` — 기준 `cpp2il_out`과 새 출력의 형식·필드·시그니처·RVA 차이, 그중 모드가 훅하는 형식에 걸린 줄, `slices/` 함수 바이트 비교를 출력합니다.
3. 모드를 다시 빌드하고(컴파일 오류 = 시그니처 변경), 로그의 `[Harmony] 패치 '...' 실패`, `[Harmony/ICFGuard] … 건너뜀`, `[Harmony] 필수 패치 빠짐` 줄을 확인합니다. `[ICFScan]`이 새 빌드에서 공유 개수를 다시 세어 `UserData/SixtarGateMod.IcfCounts.txt`에 남깁니다(대상별 `0xRVA=개수|공유 메서드`). 메타데이터가 바뀌었으면 `il2cpp_dump`도 다시 만듭니다([REVERSE_ENGINEERING.md](REVERSE_ENGINEERING.md)).
   - **ICF 개수 재확인:** 새 `GameAssembly.dll`로 Il2CppDumper를 돌려 얻은 `script.json`을 `check_update.ps1 -ScriptJson <경로>`에 주면(또는 `python -I tools\version_diff\icf_check.py <script.json> docs\02-systems\HOOK_TABLE.md`) 훅 대상 120개의 주소 공유 개수를 다시 셉니다. `[ICF 증가]`가 나온 대상은 업데이트로 다른 함수와 접힌 것이라 패치하면 엉뚱한 `this`로 실행됩니다(실행 중 `IcfScanner`도 같은 것을 자동으로 잡아 건너뛰지만, 제네릭 인스턴스 코드는 세지 않으므로 이 스크립트를 보조로 씀). 2026-11-09 빌드 `26455233` 기준 결과: 증가 0, 못 찾음 0. 2026-11-10 `IcfScanner` 결과도 같음.
4. **커스텀 곡 1곡을 결과 화면까지 플레이**하고, `Latest.log`의 `[UpdateGuard] 게임 내부 예외` 경고(원래 `Player.log`에만 찍히던 Unity 예외)를 확인합니다. 부팅·곡 선택만 보고 넘어가면 안 됩니다.
5. 문제가 없으면 `mod/Managers/UpdateGuard.cs`의 `VerifiedBuilds`에 빌드 ID를, `VerifiedGameAssemblySizes`에 그 빌드의 `GameAssembly.dll` 크기(바이트)를 추가하고, `check_update.ps1 -UpdateBaseline`으로 기준을 교체합니다. 크기 기록은 `.acf`를 고쳐 업데이트를 막았거나 매니페스트 없이 설치한 경우에도 빌드를 맞게 판별하는 데 씁니다.

함수 본문 확인은 `python -I tools\version_diff\dis.py <GameAssembly.dll> il2cpp_dump\script.json <이름 일부 또는 0xRVA>`(call 대상 이름 표시)로 할 수 있습니다.
