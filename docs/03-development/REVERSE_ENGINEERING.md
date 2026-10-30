# 게임 분석 방법

게임 코드는 `GameAssembly.dll`(약 70MB) 안에 네이티브로, 게임 데이터는 `Sixtar Gate STARTRAIL_Data\` 아래 Unity 에셋으로 들어 있습니다.
이 저장소는 공개라서 분석 결과물(덤프, 디컴파일, 조각 PE)은 올리지 않습니다. 아래 순서로 로컬에서 만듭니다.

## 1. 메타데이터 덤프 (이름, 시그니처, RVA)

```powershell
Il2CppDumper "{게임}\GameAssembly.dll" "{게임}\Sixtar Gate STARTRAIL_Data\il2cpp_data\Metadata\global-metadata.dat" "il2cpp_dump"
```

산출물: `il2cpp_dump\dump.cs`(32MB, 클래스·필드 오프셋·RVA), `script.json`(99MB, 함수 이름↔주소, 문자열), `il2cpp.h`, `DummyDll\`.

- 클래스 찾기: `dump.cs`에서 `public class 이름` 검색
- 함수 주소: 선언 위 `// RVA: 0x...`
- 시그니처 확인용 C#: `ilspycmd -p -o src_dummy il2cpp_dump\DummyDll\Assembly-CSharp.dll`
- 모드에서 실제로 쓰는 이름(Il2Cpp 접두사, `__c__DisplayClass`, `_Method_b__N`)은 `MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll`을 `ilspycmd -t 형식이름`으로 확인

Cpp2IL을 쓸 때는 끝나면 Enter를 기다리므로 입력을 닫고 실행합니다(`< NUL`).

## 2. 함수 본문 (pe_slice + REA/Ghidra)

70MB를 통째로 Ghidra에 열면 타임아웃이 납니다. 필요한 함수만 잘라 작은 PE를 만듭니다.

```bash
python -I pe_slice.py --dll "{게임}/GameAssembly.dll" --script-json il2cpp_dump/script.json \
  --class "SixtarGate.Setting.MultipleOperationsOption" \
  --method "SixtarGate.Play.GearObject\$\$PlayNoteEffect" \
  --rva 53EC40 \
  --out slices/이름.dll
```

- `--class`는 네임스페이스까지 전부 (`SixtarGate.BMSConverter`). 짧게 쓰면 못 찾습니다.
- 셸에서 `$$`는 `\$\$`로.
- REA `open_binary`는 `provider_id: "ghidra"`를 명시 (안 하면 공급자 충돌).
- 조각 PE에는 `script.json` 이름이 export로 들어가서 의사코드에 함수·문자열 이름이 보입니다.
- Ghidra가 할당 함수 뒤를 "반환 안 함"으로 보고 잘라 버리면, IL2CPP 주석 디스어셈블러(`il2cpp_asm.py --binary --dump --script-json --method`)로 전체를 봅니다.

## 3. 게임 데이터 (UnityPy)

IL2CPP 빌드는 MonoBehaviour 타입트리가 없어서 필드를 직접 읽어야 합니다. 직렬화 순서는 `dump.cs`의 `[SerializeField]`/public 필드 순서와 같습니다.

```text
MonoBehaviour 원본 바이트
  m_GameObject  PPtr (int32 fileID + int64 pathID)   = 12바이트
  m_Enabled     uint8 + 3바이트 정렬
  m_Script      PPtr                                   = 12바이트
  m_Name        int32 길이 + UTF-8 + 4바이트 정렬
  … 클래스의 직렬화 필드 (부모 클래스 필드부터)
문자열·배열 다음에는 항상 4바이트 정렬
```

`tools/datadump/`의 스크립트:

| 스크립트 | 읽는 것 | 대상 파일 |
| --- | --- | --- |
| `skin_packages.py` | `GearPackage` / `NotePackage` 프리팹 이름 | `sharedassets0.assets` |
| `sprite_users.py` | 스프라이트를 쓰는 GameObject 경로 | `sharedassets0.assets` |
| `mode_items.py` | 모드 선택 항목 (모드 이름, 이동 씬, 개발 중 여부) | `globalgamemanagers` + `level5~12` |
| `result_animation_map.py` | 결과 연출 맵 (곡 ID → 연출) | `StreamingAssets\aa\StandaloneWindows64\contents_assets_all_*.bundle` |
| `prefab_tree.py` | 프리팹 계층과 참조 스프라이트 | 번들 / assets |
| `dlc_ids.py` | DLC AppID가 들어 있는 파일 | `Sixtar Gate STARTRAIL_Data` |

실행은 항상 `python -I`(현재 폴더의 파이썬 파일을 불러오지 않게).

### 씬 번호

`globalgamemanagers`의 `BuildSettings.scenes` 순서 = `level{번호}` 파일:

```text
0 _BOOT · 1 Loading · 2 Warning · 3 Splash · 4 MainTitle
5 ModeSelect_Default · 6 ModeSelect_2021Xmas · 7 ModeSelect_CrossingDelta · 8 ModeSelect_FlowerAndDestiny
9 ModeSelect_Touhou01 · 10 ModeSelect_LuminousAndDarkness · 11 ModeSelect_Wacca · 12 ModeSelect_kalpa
13 LicenseTest · 14 PlayLoading · 15 UnityQuest_Lobby · 16 StaffRoll · 17 Renewal_Play · 18 Result
19 OuterSpace · 20 MusicSelect · 21 Adventure · 22 AdventureTitle · 23 Statistics
```

## 4. 공개 정보

- DLC 목록: `https://store.steampowered.com/api/appdetails?appids=1802720`의 `dlc`
- 공개 빌드 번호·디포: `https://api.steamcmd.net/v1/info/1802720`

## 분석할 때 지킬 것

- "있다/없다"는 이름 검색만으로 결론 내지 말고, 실제 목록(패키지, 맵, 씬)을 읽어서 확인합니다. 이름 검색은 스프라이트·텍스처까지 걸립니다.
- 패치할 함수는 반드시 ICF 확인 ([IL2CPP_PITFALLS.md](IL2CPP_PITFALLS.md)).
