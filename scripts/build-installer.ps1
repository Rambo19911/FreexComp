# Builds the offline setup program and the portable zip of the product from local inputs only (INS-001).
#
#   -Output <dir>          where the results go (default: artifacts\installer in the repository, git-ignored)
#   -Version <x.y.z>       product version (default: the version the IDE project builds with)
#
# Steps: publish FreexComp.Ide, opc, pbuild and prc self-contained for win-x64 and merge the four outputs (a file that two
# of them publish must be byte-identical); copy what installer\payload-programs.txt selects of them (everything but debug
# symbol files) into the staging folder H; add the library sources, help pages and samples (tracked files only), the
# licence files, NETWORK-POLICY.md and the third-party files of installer\payload-llvm.txt and payload-third-party.txt;
# write H\payload.sha256; publish the setup program self-contained single-file and append the payload; zip H together
# with the marker portable.json (INS-020):
#
#   <Output>\FreexComp-<version>-win64-setup.exe
#   <Output>\FreexComp-<version>-win64-portable.zip
#
# The script then runs the new setup with /S /verify. It downloads nothing: package restore may use only the local
# package cache (an empty folder is the only package source), so a missing package fails the build. The bootstrap
# script must have run once. Works in Windows PowerShell 5.1; keep this file ASCII.
param(
    [string]$Output,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$watch = [System.Diagnostics.Stopwatch]::StartNew()
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($Output)) { $Output = Join-Path $repo 'artifacts\installer' }
$Output = [System.IO.Path]::GetFullPath($Output)
$build = Join-Path $Output 'build'
$stage = Join-Path $Output 'stage'
$feed = Join-Path $build 'offline-feed'

# no build server or node outlives the script; no first-run or telemetry traffic of the SDK
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

function Copy-Merged {
    # copies every file of $From to the same relative path under $To; a file already there must be byte-identical
    param([string]$From, [string]$To)
    $root = (Resolve-Path -LiteralPath $From).ProviderPath.TrimEnd('\')
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $relative = $file.FullName.Substring($root.Length + 1)
        $target = Join-Path $To $relative
        if (Test-Path -LiteralPath $target) {
            $same = ((Get-Item -LiteralPath $target).Length -eq $file.Length) -and
                ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)
            if (-not $same) { throw "build-installer: '$relative' differs between two published programs ($From)" }
            continue
        }
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target)
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

function Copy-Tracked {
    # copies the tracked files under the repository folders $Paths (none tracked: nothing)
    param([string[]]$Paths)
    $files = & git -C $repo -c core.quotepath=off ls-files -- @Paths
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed' }
    foreach ($relative in $files) {
        $target = Join-Path $stage ($relative -replace '/', '\')
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target)
        Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $target
    }
    return @($files).Count
}

if ([string]::IsNullOrEmpty($Version)) {
    $Version = (& dotnet build (Join-Path $repo 'ide\src\FreexComp.Ide\FreexComp.Ide.csproj') -getProperty:Version -nologo).Trim()
    if ($LASTEXITCODE -ne 0 -or $Version -notmatch '^\d+\.\d+\.\d+$') { throw "build-installer: cannot read the product version ('$Version')" }
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "build-installer: -Version must look like 1.2.3, not '$Version'" }

foreach ($required in @('third_party\llvm\bin', 'third_party\sqlite', 'third_party\python-embed')) {
    if (-not (Test-Path -LiteralPath (Join-Path $repo $required))) { throw "build-installer: $required is missing; run scripts\bootstrap.ps1 once" }
}

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$null = New-Item -ItemType Directory -Force -Path $stage, $feed
$restore = @('--source', $feed, '-p:NuGetAudit=false', '--artifacts-path', $build, "-p:Version=$Version", '--nologo', '-v:minimal')

# 1. the four programs, self-contained, merged into one folder
$merged = Join-Path $build 'programs'
if (Test-Path -LiteralPath $merged) { Remove-Item -LiteralPath $merged -Recurse -Force }
$null = New-Item -ItemType Directory -Force -Path $merged
$programs = [ordered]@{
    'FreexComp.Ide' = 'ide\src\FreexComp.Ide\FreexComp.Ide.csproj'
    'opc'           = 'compiler\src\opc\opc.csproj'
    'pbuild'        = 'compiler\src\pbuild\pbuild.csproj'
    'prc'           = 'compiler\src\prc\prc.csproj'
}
foreach ($name in $programs.Keys) {
    $published = Join-Path $build "publish\$name"
    if (Test-Path -LiteralPath $published) { Remove-Item -LiteralPath $published -Recurse -Force }
    Write-Host "build-installer: publishing $name"
    Invoke-Dotnet (@('publish', (Join-Path $repo $programs[$name]), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $published) + $restore)
    Copy-Merged $published $merged
}

# 2. library sources with the Win32 binding units and import definitions the compiler looks for beside the runtime
#    library root, help pages, samples (tracked files), licences, network policy
$tracked = Copy-Tracked @('rtl/src', 'rtl/winapi/src', 'rtl/winapi/def', 'ui-framework/src', 'data/src', 'help', 'samples')
$null = New-Item -ItemType Directory -Force -Path (Join-Path $stage 'licenses')
foreach ($licence in @('LICENSE', 'LICENSE-RUNTIME', 'NOTICE', 'THIRD-PARTY-NOTICES.txt')) {
    Copy-Item -LiteralPath (Join-Path $repo $licence) -Destination (Join-Path $stage 'licenses')
}
Copy-Item -LiteralPath (Join-Path $repo 'NETWORK-POLICY.md') -Destination $stage

# 3. the packaging tool; the files of the payload lists: the published programs, the third-party files
$pack = Join-Path $build 'publish\pack'
Invoke-Dotnet (@('publish', (Join-Path $repo 'installer\tools\FreexComp.SetupPack\FreexComp.SetupPack.csproj'), '-c', 'Release', '-o', $pack) + $restore)
$packExe = Join-Path $pack 'FreexComp.SetupPack.exe'
& $packExe copy-list (Join-Path $repo 'installer\payload-programs.txt') $merged $stage
if ($LASTEXITCODE -ne 0) { throw 'build-installer: payload-programs.txt failed' }
& $packExe copy-list (Join-Path $repo 'installer\payload-llvm.txt') (Join-Path $repo 'third_party\llvm') (Join-Path $stage 'third_party\llvm')
if ($LASTEXITCODE -ne 0) { throw 'build-installer: payload-llvm.txt failed' }
& $packExe copy-list (Join-Path $repo 'installer\payload-third-party.txt') (Join-Path $repo 'third_party') (Join-Path $stage 'third_party')
if ($LASTEXITCODE -ne 0) { throw 'build-installer: payload-third-party.txt failed' }

# 4. the setup program (self-contained single file) with the payload appended
$setupProgram = Join-Path $build 'publish\setup'
if (Test-Path -LiteralPath $setupProgram) { Remove-Item -LiteralPath $setupProgram -Recurse -Force }
Invoke-Dotnet (@('publish', (Join-Path $repo 'installer\src\FreexComp.Setup\FreexComp.Setup.csproj'), '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=none', '-o', $setupProgram) + $restore)
$extra = @(Get-ChildItem -LiteralPath $setupProgram -File | Where-Object { $_.Name -ne 'FreexComp.Setup.exe' })
if ($extra.Count -gt 0) { throw "build-installer: the setup program is not a single file ($($extra.Name -join ', '))" }
$setup = Join-Path $Output "FreexComp-$Version-win64-setup.exe"
& $packExe setup (Join-Path $setupProgram 'FreexComp.Setup.exe') $stage $setup
if ($LASTEXITCODE -ne 0) { throw 'build-installer: writing the setup exe failed' }

# 5. the portable zip (INS-020): the same staging folder plus the marker portable.json, which exists only in the zip
$portable = Join-Path $Output "FreexComp-$Version-win64-portable.zip"
& $packExe portable $stage $portable
if ($LASTEXITCODE -ne 0) { throw 'build-installer: writing the portable zip failed' }

# 6. self-check: the new setup accepts its own payload (INS-003), with its log in the build folder
$verifyTemp = Join-Path $build 'verify-temp'
$null = New-Item -ItemType Directory -Force -Path $verifyTemp
$oldTemp = $env:TEMP; $oldTmp = $env:TMP
try {
    $env:TEMP = $verifyTemp; $env:TMP = $verifyTemp
    $check = Start-Process -FilePath $setup -ArgumentList '/S', '/verify' -Wait -PassThru
} finally {
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp
}
if ($check.ExitCode -ne 0) { throw "build-installer: $setup /verify failed with exit code $($check.ExitCode)" }

$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File).Count
Write-Host ("build-installer: {0} ({1:N1} MB)" -f $setup, ((Get-Item -LiteralPath $setup).Length / 1MB))
Write-Host ("build-installer: {0} ({1:N1} MB)" -f $portable, ((Get-Item -LiteralPath $portable).Length / 1MB))
Write-Host ("build-installer: {0} payload files, {1} tracked source/help files, in {2:N0} s" -f $files, $tracked, $watch.Elapsed.TotalSeconds)
