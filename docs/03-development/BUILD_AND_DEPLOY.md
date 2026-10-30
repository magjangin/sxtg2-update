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
2. `MelonLoader\Latest.log`에서 `[Harmony] All patches applied successfully! (58)`
3. 바꾼 기능을 게임에서 직접 확인
4. 게임 내부 오류는 `%USERPROFILE%\AppData\LocalLow\Lyrebird Studio\Sixtar Gate STARTRAIL\Player.log`

## 저장소 작업 흐름

- 원격: `https://github.com/magjangin/sxtg2-update` (공개)
- 기능마다 브랜치 → PR → merge → 원격·로컬 브랜치 삭제
- 커밋하지 않는 것(`.gitignore`): `mod/bin`, `mod/obj`, `il2cpp_dump/`, `cpp2il_out/`, `src_dummy/`, `slices/`, `logs/`, `input/`, `*.dll`, `.reatool/`

## 게임 버전 고정

Steam Build `26218612` 기준으로 만들었습니다. 게임이 업데이트되면 함수 주소와 구조가 바뀌어 일부 패치가 실패할 수 있습니다.
업데이트 후에는 `il2cpp_dump`를 다시 만들고([REVERSE_ENGINEERING.md](REVERSE_ENGINEERING.md)), 로그의 `Patch '...' failed` 줄부터 확인하세요.
