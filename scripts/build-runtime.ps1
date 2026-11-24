# Rebuilds every library unit from the source that ships with the product, to prove that the source is
# complete and compiles: the runtime library (rtl\src), the Win32 binding units (rtl\winapi\src), the UI
# framework (ui-framework\src) and the data-access library (data\src).
#
#   -Home <dir>            directory that holds rtl\, ui-framework\ and data\
#                          (default: %FREEXCOMP_HOME% when set, else the directory above this script)
#   -Configuration <name>  Debug (default: -O0 -g) or Release (-O2)
#   -ObjDir <dir>          where the object files go (default: a new directory under %TEMP%, deleted at the
#                          end unless -Keep is given; a directory you name is never deleted)
#   -Keep                  keep the default object directory and print its path
#   -Opc <path>            the compiler (default: opc.exe in the home directory or its bin\ directory, else
#                          'dotnet run' on compiler\src\opc of the home directory)
#
# Units named in build-runtime.exclude.txt (next to this script) are left out. The script writes a program
# RuntimeAll.pas that uses every remaining unit, compiles it to object files, passes the compiler's messages
# on unchanged and ends with one line:
#
#   build-runtime: <n> units, <errors> errors, <warnings> warnings
#
# Exit code: that of the compiler (0 ok, 1 compile errors, 2 internal error or tools missing).
# Nothing is written inside the source directories. Downloads nothing. Works in Windows PowerShell 5.1;
# keep this file ASCII.
param(
    [Alias('Home')]
    [string]$HomeDir,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$ObjDir,
    [switch]$Keep,
    [string]$Opc
)

$ErrorActionPreference = 'Stop'

$scriptHome = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($HomeDir)) {
    if (-not [string]::IsNullOrEmpty($env:FREEXCOMP_HOME)) { $HomeDir = $env:FREEXCOMP_HOME } else { $HomeDir = $scriptHome }
}
$HomeDir = [System.IO.Path]::GetFullPath($HomeDir)

$rtl = Join-Path $HomeDir 'rtl\src'
$winapi = Join-Path $HomeDir 'rtl\winapi\src'
$framework = Join-Path $HomeDir 'ui-framework\src'
$data = Join-Path $HomeDir 'data\src'
foreach ($root in @($rtl, $winapi, $framework, $data)) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        Write-Output "build-runtime: source directory not found: $root"
        exit 2
    }
}

# The compiler: named, installed next to the home, or run from its project.
$command = $null
$prefix = @()
if (-not [string]::IsNullOrEmpty($Opc)) {
    $command = $Opc
} else {
    foreach ($candidate in @((Join-Path $HomeDir 'opc.exe'), (Join-Path $HomeDir 'bin\opc.exe'))) {
        if (($null -eq $command) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { $command = $candidate }
    }
    if ($null -eq $command) {
        $project = Join-Path $HomeDir 'compiler\src\opc'
        if (Test-Path -LiteralPath $project -PathType Container) {
            $command = 'dotnet'
            $prefix = @('run', '--project', $project, '--')
        }
    }
}
if (($null -eq $command) -or (($command -ne 'dotnet') -and -not (Test-Path -LiteralPath $command -PathType Leaf))) {
    Write-Output 'build-runtime: the compiler (opc.exe) was not found; name it with -Opc'
    exit 2
}

# The units: every .pas file directly inside the four directories (file name = unit name), minus the exclusions.
$excluded = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$excludeFile = Join-Path $PSScriptRoot 'build-runtime.exclude.txt'
if (Test-Path -LiteralPath $excludeFile -PathType Leaf) {
    foreach ($line in [System.IO.File]::ReadAllLines($excludeFile)) {
        $name = ($line -split '#', 2)[0].Trim()
        if ($name.Length -gt 0) { [void]$excluded.Add($name) }
    }
}

$units = New-Object 'System.Collections.Generic.List[string]'
foreach ($root in @($rtl, $winapi, $framework, $data)) {
    foreach ($file in [System.IO.Directory]::GetFiles($root, '*.pas')) {
        if ([System.IO.Path]::GetExtension($file) -ne '.pas') { continue }
        $name = [System.IO.Path]::GetFileNameWithoutExtension($file)
        if (-not $excluded.Contains($name)) { $units.Add($name) }
    }
}
$units.Sort([System.StringComparer]::Ordinal)
if ($units.Count -eq 0) {
    Write-Output 'build-runtime: no library unit found'
    exit 2
}

$ownObjDir = [string]::IsNullOrEmpty($ObjDir)
if ($ownObjDir) {
    $ObjDir = Join-Path ([System.IO.Path]::GetTempPath()) ('freexcomp-runtime-' + [System.Guid]::NewGuid().ToString('N'))
}
$ObjDir = [System.IO.Path]::GetFullPath($ObjDir)
foreach ($root in @($rtl, $winapi, $framework, $data)) {
    if (($ObjDir + '\').StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Output "build-runtime: the object directory must not lie inside a source directory: $ObjDir"
        exit 2
    }
}
[void][System.IO.Directory]::CreateDirectory($ObjDir)

$exit = 2
try {
    $main = Join-Path $ObjDir 'RuntimeAll.pas'
    $text = "program RuntimeAll;`r`nuses`r`n  " + ($units -join ",`r`n  ") + ";`r`nbegin`r`nend.`r`n"
    [System.IO.File]::WriteAllText($main, $text, (New-Object System.Text.UTF8Encoding($false)))

    $arguments = $prefix + @(
        $main, '--emit', 'obj', '--obj-dir', $ObjDir, '-o', (Join-Path $ObjDir 'RuntimeAll.exe'),
        '-U', $framework, '-U', $data, '--rtl', $rtl)
    $toolchain = Join-Path $HomeDir 'third_party\llvm\bin'
    if (Test-Path -LiteralPath $toolchain -PathType Container) { $arguments += @('--toolchain', $toolchain) }
    if ($Configuration -eq 'Release') { $arguments += '-O2' } else { $arguments += @('-O0', '-g') }

    $errors = 0
    $warnings = 0
    # A message of the compiler is one line: file(line,col): severity CODE: text
    & $command @arguments | ForEach-Object {
        $line = [string]$_
        if ($line -cmatch ': error [A-Z]\d{4}: ') { $errors++ }
        elseif ($line -cmatch ': warning [A-Z]\d{4}: ') { $warnings++ }
        Write-Output $line
    }
    $exit = $LASTEXITCODE

    Write-Output ('build-runtime: {0} units, {1} errors, {2} warnings' -f $units.Count, $errors, $warnings)
    if ($ownObjDir -and $Keep) { Write-Output "Object directory: $ObjDir" }
}
finally {
    if ($ownObjDir -and (-not $Keep) -and (Test-Path -LiteralPath $ObjDir)) {
        Remove-Item -LiteralPath $ObjDir -Recurse -Force -Confirm:$false
    }
}

exit $exit
