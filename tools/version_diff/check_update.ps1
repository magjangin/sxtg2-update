<#
게임 업데이트 후 모드 영향 점검 (Windows PowerShell 5.1).

1. 기준 Cpp2IL 출력(repo cpp2il_out)과 MelonLoader가 새로 만든 Cpp2IL 출력을 비교
   → 형식·필드 오프셋·메서드 시그니처·RVA 차이 개수.
2. 차이 중 모드가 훅하는 형식(mod 소스의 typeof(...))에 걸린 줄만 따로 출력.
3. slices/에 남은 이전 바이너리 함수와 새 GameAssembly.dll 바이트 비교(python+pefile 있을 때).
4. -ScriptJson을 주면 훅 대상의 ICF(주소 공유) 개수를 새 빌드 기준으로 다시 셈(icf_check.py).

사용:
  powershell -ExecutionPolicy Bypass -File tools\version_diff\check_update.ps1
  ... -ScriptJson <새 빌드 Il2CppDumper 출력\script.json>
  ... -UpdateBaseline   # 검토가 끝난 뒤 새 출력을 기준(cpp2il_out)으로 교체
#>
param(
    [string]$Repo = (Resolve-Path "$PSScriptRoot\..\..").Path,
    [string]$Game = "H:\steam\steamapps\common\Sixtar Gate STARTRAIL",
    [string]$ScriptJson = "",
    [switch]$UpdateBaseline
)
$ErrorActionPreference = "Stop"
$baseline = Join-Path $Repo "cpp2il_out"
$current  = Join-Path $Game "MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\cpp2il_out"
$tool     = Join-Path $PSScriptRoot "bin\dumpdiff.exe"
$work     = Join-Path $env:TEMP "sgm_version_diff"
New-Item -ItemType Directory -Force $work | Out-Null

foreach ($p in @($baseline, $current)) { if (-not (Test-Path $p)) { throw "폴더 없음: $p (게임을 한 번 실행해 MelonLoader가 Cpp2IL 출력을 만들게 하세요)" } }

$acf = Join-Path (Split-Path (Split-Path $Game)) "appmanifest_1802720.acf"
if (Test-Path $acf) {
    $m = Select-String -Path $acf -Pattern '"buildid"\s+"(\d+)"' | Select-Object -First 1
    if ($m) { Write-Host "현재 Steam 빌드: $($m.Matches[0].Groups[1].Value)" }
}
Write-Host "GameAssembly.dll: $((Get-Item (Join-Path $Game 'GameAssembly.dll')).LastWriteTime)"
Write-Host "global-metadata.dat: $((Get-Item (Join-Path $Game 'Sixtar Gate STARTRAIL_Data\il2cpp_data\Metadata\global-metadata.dat')).LastWriteTime)"

if (-not (Test-Path $tool)) {
    Write-Host "dumpdiff 빌드 중..."
    & "C:\Program Files\dotnet\dotnet.EXE" build (Join-Path $PSScriptRoot "dumpdiff.csproj") -o (Join-Path $PSScriptRoot "bin") -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dumpdiff 빌드 실패" }
}

& $tool $baseline (Join-Path $work "old.txt") | Out-Null
& $tool $current  (Join-Path $work "new.txt") | Out-Null
$old = Get-Content (Join-Path $work "old.txt")
$new = Get-Content (Join-Path $work "new.txt")
$diff = @(Compare-Object $old $new)
Write-Host ""
Write-Host "[1] 메타데이터 비교: 차이 $($diff.Count)줄 (<= 이전에만, => 새 빌드에만)"

# 모드가 훅하는 형식 이름 수집 (typeof(X) / typeof(A.B.X) → 마지막 이름)
$hooked = New-Object System.Collections.Generic.HashSet[string]
Get-ChildItem (Join-Path $Repo "mod") -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | ForEach-Object {
    foreach ($mm in [regex]::Matches((Get-Content $_.FullName -Raw), 'typeof\(([\w\.]+)\)')) {
        [void]$hooked.Add(($mm.Groups[1].Value -split '\.')[-1])
    }
}
$hits = @($diff | Where-Object {
    $line = $_.InputObject
    $t = ($line -split '::')[0]
    $leaf = (($t -split '\|')[-1] -split '[\./]')
    ($leaf | Where-Object { $hooked.Contains($_) }).Count -gt 0
})
Write-Host "[2] 그중 모드가 훅하는 형식($($hooked.Count)종)에 걸린 줄: $($hits.Count)"
$hits | Select-Object -First 80 | ForEach-Object { Write-Host "    $($_.SideIndicator) $($_.InputObject)" }
if ($diff.Count -gt 0) {
    $diff | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" } | Set-Content (Join-Path $work "diff.txt") -Encoding UTF8
    Write-Host "    전체 차이: $(Join-Path $work 'diff.txt')"
}

$py = Get-Command python -ErrorAction SilentlyContinue
$slices = Join-Path $Repo "slices"
if ($py -and (Test-Path $slices)) {
    Write-Host ""
    Write-Host "[3] slices/ 함수 바이트 비교:"
    & python -I (Join-Path $PSScriptRoot "slicecmp.py") $slices (Join-Path $Game "GameAssembly.dll")
}

if ($py -and $ScriptJson) {
    Write-Host ""
    Write-Host "[4] 훅 대상 ICF 개수 (새 script.json 기준):"
    & python -I (Join-Path $PSScriptRoot "icf_check.py") $ScriptJson (Join-Path $Repo "docs\02-systems\HOOK_TABLE.md")
    if ($LASTEXITCODE -ne 0) { Write-Host "    ICF가 늘어난 대상이 있습니다. 그 패치는 다른 함수까지 건드리니 대상을 바꾸세요." }
} else {
    Write-Host ""
    Write-Host "[4] ICF 개수 점검 생략: 새 빌드로 Il2CppDumper를 돌린 뒤 -ScriptJson <script.json>을 주세요."
}

Write-Host ""
if ($diff.Count -eq 0) { Write-Host "결론: 메타데이터 동일. 그래도 커스텀 곡 1곡을 결과 화면까지 플레이하고 Latest.log의 [UpdateGuard] 경고를 확인하세요." }
else { Write-Host "결론: 메타데이터가 바뀜. [2]의 줄과 docs/02-systems/HOOK_TABLE.md를 대조하세요." }

if ($UpdateBaseline) {
    Remove-Item $baseline -Recurse -Force
    Copy-Item $current $baseline -Recurse
    Write-Host "기준 cpp2il_out을 새 출력으로 교체했습니다."
}
