<#
.SYNOPSIS
  SixtarGateMod 릴리즈: 버전 확인 → Release 빌드 → 단위 테스트 → 패치 시그니처 대조 → zip → (선택) 태그·GitHub 릴리즈.

.DESCRIPTION
  버전은 mod\SixtarGateMod.csproj 의 <Version> 하나만 기준으로 삼고, 태그는 v<버전> 입니다.
  mod\Core.cs 의 MelonInfo 버전과 CHANGELOG.md 의 "## [<버전>]" 항목이 같은 버전인지도 확인합니다.

  기본 실행은 아무것도 게시하지 않습니다: 검증 후 dist\SixtarGateMod-v<버전>.zip 과 릴리즈 노트 초안만 만듭니다.
  -Publish 를 주면 main 이 origin/main 과 같고 작업 트리가 깨끗한지 확인한 뒤
  주석 태그를 만들어 origin 에 푸시하고 gh 로 GitHub 릴리즈를 만들어 zip 을 올립니다.

  모드 빌드에는 게임의 MelonLoader\net6 와 Il2CppAssemblies 가 필요해서 GitHub Actions 에서는 빌드할 수 없습니다.
  그래서 게임이 설치된 PC 에서 실행하는 로컬 스크립트입니다.

  zip 에는 SixtarGateMod.dll 하나만 넣습니다(v2.3.1 릴리즈와 같은 구성). 게임 파일·덤프·디컴파일 코드는 넣지 않습니다.

.PARAMETER Publish
  태그 생성·푸시와 GitHub 릴리즈 생성까지 합니다. 없으면 검증과 zip 까지만.

.PARAMETER GameDir
  게임 폴더. 없으면 csproj 와 같은 순서(환경변수 SIXTAR_GAME_DIR → Directory.Build.props.user → 기본 H:\ 경로)를 따릅니다.

.PARAMETER SkipSigcheck
  tools\sigcheck 를 건너뜁니다. -Publish 와 함께 쓰면 릴리즈 노트에 "시그니처 대조 생략"이 적힙니다.

.EXAMPLE
  .\tools\release.ps1              # 검증 + dist\SixtarGateMod-v2.3.2.zip
  .\tools\release.ps1 -Publish     # 위 + 태그 v2.3.2 푸시 + GitHub 릴리즈
#>
[CmdletBinding()]
param(
    [switch]$Publish,
    [string]$GameDir,
    [switch]$SkipSigcheck
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Step($msg) { Write-Host "`n== $msg" -ForegroundColor Cyan }
function Invoke-Native {
    param([string]$What, [string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$What 실패 (종료 코드 $LASTEXITCODE)" }
}
function Write-Utf8NoBom($path, $text) {
    # Windows PowerShell 5.1 의 Set-Content -Encoding utf8 은 BOM 을 붙여 gh 릴리즈 노트 첫 글자가 깨질 수 있음
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false)))
}

# ---------------------------------------------------------------- 1. 버전
Step '버전 확인'
$csprojPath = Join-Path $root 'mod\SixtarGateMod.csproj'
[xml]$csproj = Get-Content $csprojPath -Raw -Encoding UTF8
$version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw "csproj 에 <Version> 이 없습니다: $csprojPath" }
$version = $version.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "버전 형식이 x.y.z 가 아닙니다: $version" }
$tag = "v$version"

$core = Get-Content (Join-Path $root 'mod\Core.cs') -Raw -Encoding UTF8
if ($core -notmatch 'MelonInfo\(\s*typeof\(\s*SixtarGateMod\.Core\s*\)\s*,\s*"[^"]*"\s*,\s*"(?<v>[^"]+)"') {
    throw 'mod\Core.cs 에서 MelonInfo 버전을 찾지 못했습니다.'
}
if ($Matches['v'] -ne $version) { throw "버전 불일치: csproj $version / Core.cs MelonInfo $($Matches['v'])" }

$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw -Encoding UTF8
$entryPattern = "(?ms)^## \[$([regex]::Escape($version))\][^\r\n]*\r?\n(?<body>.*?)(?=^## \[|\z)"
$changelogBody = if ($changelog -match $entryPattern) { $Matches['body'].Trim() } else { $null }
if (-not $changelogBody) {
    if ($Publish) { throw "CHANGELOG.md 에 '## [$version]' 항목이 없습니다." }
    Write-Warning "CHANGELOG.md 에 '## [$version]' 항목이 없습니다 (-Publish 때는 중단됩니다)."
}
Write-Host "버전 $version (태그 $tag), csproj·Core.cs·CHANGELOG 일치"

# ---------------------------------------------------------------- 2. git 상태 (게시할 때만 강제)
Step 'git 상태 확인'
Invoke-Native 'git fetch' git @('fetch', 'origin', '--tags', '--quiet')
$dirty = git status --porcelain
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
$head = (git rev-parse HEAD).Trim()
$originMain = (git rev-parse origin/main).Trim()
$localTag = git tag --list $tag
$remoteTag = git ls-remote --tags origin "refs/tags/$tag"

$problems = @()
if ($dirty) { $problems += '작업 트리에 커밋하지 않은 변경이 있습니다.' }
if ($branch -ne 'main') { $problems += "현재 브랜치가 main 이 아닙니다($branch)." }
if ($head -ne $originMain) { $problems += 'HEAD 가 origin/main 과 다릅니다(푸시·머지 또는 pull 필요).' }
if ($localTag -or $remoteTag) { $problems += "태그 $tag 가 이미 있습니다. csproj·Core.cs·CHANGELOG 의 버전을 올리세요." }
if ($problems) {
    if ($Publish) { throw ("게시할 수 없습니다:`n - " + ($problems -join "`n - ")) }
    $problems | ForEach-Object { Write-Warning $_ }
} else { Write-Host "main = origin/main ($($head.Substring(0, 7))), 태그 $tag 없음" }

if ($Publish) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh(GitHub CLI)가 없습니다. https://cli.github.com 설치 후 gh auth login.' }
    Invoke-Native 'gh auth status' gh @('auth', 'status')
}

# ---------------------------------------------------------------- 3. 빌드·테스트·시그니처 대조
$gameArgs = @()
if ($GameDir) { $gameArgs = @("-p:GameDir=$GameDir") }

Step 'Release 빌드 (게임 Mods 폴더로 복사하지 않음)'
Invoke-Native '빌드' dotnet (@('build', 'mod\SixtarGateMod.csproj', '-c', 'Release', '-p:SkipDeploy=true', '-nologo') + $gameArgs)
$dll = Join-Path $root 'mod\bin\Release\net6.0\SixtarGateMod.dll'
if (-not (Test-Path $dll)) { throw "DLL 없음: $dll" }
$fileVer = (Get-Item $dll).VersionInfo.ProductVersion
if ($fileVer -and -not $fileVer.StartsWith($version)) { throw "빌드된 DLL 버전($fileVer)이 $version 과 다릅니다." }

Step '단위 테스트'
Invoke-Native '단위 테스트' dotnet @('test', 'tests\SixtarGateMod.Tests\SixtarGateMod.Tests.csproj', '-c', 'Release', '-nologo')

$sigcheckLine = '생략(-SkipSigcheck)'
if (-not $SkipSigcheck) {
    Step '패치 시그니처 대조 (tools\sigcheck)'
    $sigArgs = @('run', '--project', 'tools\sigcheck', '-c', 'Release', '--', $dll)
    if ($GameDir) { $sigArgs += $GameDir }
    $sigOut = & dotnet @sigArgs 2>&1 | ForEach-Object { "$_" }
    $sigExit = $LASTEXITCODE
    $sigOut | ForEach-Object { Write-Host $_ }
    if ($sigExit -eq 1) { throw '시그니처 대조: 부팅 때 건너뛰어질 패치가 있습니다(종료 코드 1).' }
    if ($sigExit -ne 0) { throw "시그니처 대조를 실행하지 못했습니다(종료 코드 $sigExit). 게임 폴더·인터롭을 확인하거나 -SkipSigcheck." }
    $summary = $sigOut | Where-Object { $_ -like '패치 시그니처 대조:*' } | Select-Object -Last 1
    $sigcheckLine = if ($summary) { $summary.Trim() } else { '통과' }
}

# ---------------------------------------------------------------- 4. 패키징
Step 'zip 패키징'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zipName = "SixtarGateMod-$tag.zip"
$zip = Join-Path $dist $zipName
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $dll -DestinationPath $zip
$zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$dllHash = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "생성: $zip"
Write-Host "SHA256 zip $zipHash"
Write-Host "SHA256 dll $dllHash"

# 게임 기준 빌드는 AGENTS.md 1절에서 읽는다 (고정 빌드를 바꾸면 그 문서를 먼저 갱신)
$agents = Get-Content (Join-Path $root 'AGENTS.md') -Raw -Encoding UTF8
$gameBuild = if ($agents -match 'Steam Build ID:\*\*\s*`(?<b>\d+)`\s*\(게임 내 표기 `(?<v>ver [^ `/]+)') { "``$($Matches['b'])`` ($($Matches['v']))" } else { '(AGENTS.md 확인)' }

$bt = [char]96
$notes = @"
$(if ($changelogBody) { $changelogBody } else { "$tag" })

## 설치
${bt}SixtarGateMod.dll${bt}을 ${bt}{게임 폴더}\Mods\${bt}에 넣습니다 (MelonLoader 0.7.3 필요). 게임 빌드 $gameBuild 기준이며, 게임 실행 중에는 DLL이 잠겨 있으니 종료 후 복사하세요.

## 릴리즈 스크립트 검증
- Release 빌드 성공, 단위 테스트 통과
- $sigcheckLine
- SHA256 (${bt}$zipName${bt}): ${bt}$zipHash${bt}
- SHA256 (${bt}SixtarGateMod.dll${bt}): ${bt}$dllHash${bt}

자세한 내용은 [CHANGELOG.md](https://github.com/magjangin/sxtg2-update/blob/main/CHANGELOG.md).
"@
$notesFile = Join-Path $dist "notes-$tag.md"
Write-Utf8NoBom $notesFile $notes
Write-Host "릴리즈 노트 초안: $notesFile"

if (-not $Publish) {
    Write-Host "`n게시하지 않았습니다. 태그·릴리즈까지 하려면: .\tools\release.ps1 -Publish" -ForegroundColor Yellow
    return
}

# ---------------------------------------------------------------- 5. 태그·릴리즈
Step "태그 $tag 생성·푸시"
Invoke-Native 'git tag' git @('tag', '-a', $tag, '-m', "SixtarGateMod $tag")
try {
    Invoke-Native 'git push tag' git @('push', 'origin', "refs/tags/$tag")
} catch {
    git tag -d $tag | Out-Null
    throw "태그 푸시 실패, 로컬 태그를 지웠습니다: $_"
}

Step 'GitHub 릴리즈 생성'
try {
    Invoke-Native 'gh release create' gh @('release', 'create', $tag, $zip, '--verify-tag', '--title', $tag, '--notes-file', $notesFile)
} catch {
    Write-Warning "태그 $tag 는 푸시됐지만 릴리즈 생성이 실패했습니다. 원인을 고친 뒤 다음으로 다시 만드세요:"
    Write-Warning "  gh release create $tag `"$zip`" --verify-tag --title $tag --notes-file `"$notesFile`""
    throw
}
Write-Host "`n릴리즈 게시 완료: https://github.com/magjangin/sxtg2-update/releases/tag/$tag" -ForegroundColor Green
