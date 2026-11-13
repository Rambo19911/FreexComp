# One-time developer setup. The ONLY script allowed to use the network during development.
# Usage:  powershell -ExecutionPolicy Bypass -File scripts/bootstrap.ps1 [-CheckOnly]
# Exit codes: 0 = environment ready, 1 = something required is missing (details printed).
# Nothing is installed system-wide: packages are extracted into third_party/ (git-ignored).
param([switch]$CheckOnly)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cfg  = Get-Content (Join-Path $PSScriptRoot 'toolchain.json') -Raw | ConvertFrom-Json
$dl   = Join-Path $root 'third_party\_download'
$fail = $false

function Say($ok, $msg) { if ($ok) { Write-Host "[ ok ] $msg" } else { Write-Host "[FAIL] $msg"; $script:fail = $true } }

function Get-Package($name, $url, $sha256) {
    New-Item -ItemType Directory -Force $dl | Out-Null
    $file = Join-Path $dl $name
    if (-not (Test-Path $file)) {
        Write-Host "Downloading $url ..."
        & curl.exe -sL --fail -o $file $url
        if ($LASTEXITCODE -ne 0) { throw "download failed: $url" }
    }
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLower()
    if ($hash -ne $sha256.ToLower()) { throw "SHA-256 mismatch for $name : $hash (expected $sha256)" }
    return $file
}

# --- .NET SDK, git ---------------------------------------------------------------------------------------
$sdks = @(& dotnet --list-sdks)
Say (($sdks | Where-Object { $_ -like "$($cfg.dotnetSdk)*" }).Count -gt 0) ".NET SDK $($cfg.dotnetSdk).x"
Say ($null -ne (Get-Command git -ErrorAction SilentlyContinue)) 'git'

# --- LLVM (compiler back end + debugger) -----------------------------------------------------------------
$llvmDir = Join-Path $root $cfg.llvm.installDir
$llvmBin = Join-Path $llvmDir 'bin'
if (-not (Test-Path (Join-Path $llvmBin 'lldb-dap.exe')) -and -not $CheckOnly) {
    $msi = Get-Package $cfg.llvm.package $cfg.llvm.url $cfg.llvm.sha256
    $tmp = Join-Path $root 'third_party\_llvm-extract'
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
    New-Item -ItemType Directory -Force $tmp | Out-Null
    # Administrative install = plain extraction; no registry entries, no PATH change, no elevation.
    $p = Start-Process msiexec.exe -ArgumentList "/a `"$msi`" /qn TARGETDIR=`"$tmp`"" -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "msiexec /a failed with exit code $($p.ExitCode)" }
    if (Test-Path $llvmDir) { Remove-Item -Recurse -Force $llvmDir }
    Move-Item (Join-Path $tmp 'LLVM') $llvmDir
    Remove-Item -Recurse -Force $tmp
}
foreach ($t in $cfg.llvm.requiredTools) { Say (Test-Path (Join-Path $llvmBin $t)) "LLVM required tool $t" }
foreach ($t in $cfg.llvm.optionalTools) {
    if (Test-Path (Join-Path $llvmBin $t)) { Write-Host "[ ok ] LLVM optional tool $t" } else { Write-Host "[warn] LLVM optional tool $t missing" }
}
if (Test-Path (Join-Path $llvmBin 'clang.exe')) {
    $v = (& (Join-Path $llvmBin 'clang.exe') --version)[0]
    Say ($v -like "*$($cfg.llvm.version)*") "clang version matches pin $($cfg.llvm.version)"
}

# --- Python runtime for liblldb.dll (embeddable package, not a system install) ---------------------------
$pyDir = Join-Path $root $cfg.python.installDir
if (-not (Test-Path (Join-Path $pyDir 'python3.dll')) -and -not $CheckOnly) {
    $zip = Get-Package $cfg.python.package $cfg.python.url $cfg.python.sha256
    New-Item -ItemType Directory -Force $pyDir | Out-Null
    Expand-Archive -Path $zip -DestinationPath $pyDir -Force
}
foreach ($f in $cfg.python.requiredFiles) { Say (Test-Path (Join-Path $pyDir $f)) "Python runtime file $f" }

# --- SQLite (official DLL, used by the data-access area) --------------------------------------------------
$sqlDir = Join-Path $root $cfg.sqlite.installDir
if (-not (Test-Path (Join-Path $sqlDir 'sqlite3.dll')) -and -not $CheckOnly) {
    $zip = Get-Package $cfg.sqlite.package $cfg.sqlite.url $cfg.sqlite.sha256
    New-Item -ItemType Directory -Force $sqlDir | Out-Null
    Expand-Archive -Path $zip -DestinationPath $sqlDir -Force
}
foreach ($f in $cfg.sqlite.requiredFiles) { Say (Test-Path (Join-Path $sqlDir $f)) "SQLite file $f" }

# --- lldb must start using ONLY the bundled Python -------------------------------------------------------
if ((Test-Path (Join-Path $llvmBin 'lldb.exe')) -and (Test-Path (Join-Path $pyDir 'python3.dll'))) {
    $old = $env:PATH
    try {
        $env:PATH = (($old -split ';') | Where-Object { $_ -notmatch 'python' }) -join ';'
        $env:PATH = "$pyDir;$env:PATH"
        $v = & (Join-Path $llvmBin 'lldb.exe') --version 2>&1 | Select-Object -First 1
        Say ("$v" -like "*$($cfg.llvm.version)*") "lldb starts with the bundled Python runtime"
    } finally { $env:PATH = $old }
}

if ($fail) { Write-Host "`nEnvironment NOT ready."; exit 1 }
Write-Host "`nEnvironment ready."; exit 0
