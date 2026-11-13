# One-time developer setup. The ONLY script allowed to use the network (ADR 003, ARCHITECTURE §9).
# Usage:  powershell -ExecutionPolicy Bypass -File scripts/bootstrap.ps1 [-CheckOnly]
# Exit codes: 0 = environment ready, 1 = something required is missing (details printed).
param([switch]$CheckOnly)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cfg  = Get-Content (Join-Path $PSScriptRoot 'toolchain.json') -Raw | ConvertFrom-Json
$fail = $false

function Say($ok, $msg) { if ($ok) { Write-Host "[ ok ] $msg" } else { Write-Host "[FAIL] $msg"; $script:fail = $true } }

# --- .NET SDK -------------------------------------------------------------------------------------------
$sdks = @(& dotnet --list-sdks)
Say (($sdks | Where-Object { $_ -like "$($cfg.dotnetSdk)*" }).Count -gt 0) ".NET SDK $($cfg.dotnetSdk).x"

# --- git ------------------------------------------------------------------------------------------------
Say ($null -ne (Get-Command git -ErrorAction SilentlyContinue)) 'git'
if (-not (Test-Path (Join-Path $root '.git')) -and -not $CheckOnly) { & git -C $root init | Out-Null; Write-Host '[ ok ] git repository initialised' }

# --- LLVM -----------------------------------------------------------------------------------------------
$llvmDir = Join-Path $root $cfg.llvm.installDir
$llvmBin = Join-Path $llvmDir 'bin'
if (-not (Test-Path (Join-Path $llvmBin 'clang.exe')) -and -not $CheckOnly) {
    $tmp = Join-Path $root 'third_party\_download'
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $archive = Join-Path $tmp $cfg.llvm.archive
    if (-not (Test-Path $archive)) {
        Write-Host "Downloading $($cfg.llvm.url) ..."
        & curl.exe -L --fail -o $archive $cfg.llvm.url
    }
    if ($cfg.llvm.sha256) {
        $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLower()
        if ($hash -ne $cfg.llvm.sha256.ToLower()) { throw "LLVM archive SHA-256 mismatch: $hash" }
    } else {
        Write-Host "[warn] no sha256 pinned for LLVM; computed: $((Get-FileHash $archive -Algorithm SHA256).Hash.ToLower()) - record it in scripts/toolchain.json"
    }
    New-Item -ItemType Directory -Force $llvmDir | Out-Null
    & tar.exe -xf $archive -C $llvmDir --strip-components=1     # Windows 10+ bsdtar handles .tar.xz
}
foreach ($t in $cfg.llvm.requiredTools) { Say (Test-Path (Join-Path $llvmBin $t)) "LLVM required tool $t" }
foreach ($t in $cfg.llvm.optionalTools) {
    if (Test-Path (Join-Path $llvmBin $t)) { Write-Host "[ ok ] LLVM optional tool $t" }
    else { Write-Host "[warn] LLVM optional tool $t missing (needed from Phase 6; see docs/QUESTIONS.md T1)" }
}
if (Test-Path (Join-Path $llvmBin 'clang.exe')) {
    $v = (& (Join-Path $llvmBin 'clang.exe') --version)[0]
    Say ($v -like "*$($cfg.llvm.version)*") "clang version matches pin $($cfg.llvm.version) ($v)"
}

if ($fail) { Write-Host "`nEnvironment NOT ready."; exit 1 }
Write-Host "`nEnvironment ready."; exit 0
