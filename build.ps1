# Builds ResSwitch with the C# compiler already present in Windows (no SDK needed).
#   .\build.ps1            -> dist\ResSwitch.exe, dist\ResSwitch-<version>-portable.zip, and the installer if Inno Setup is found
#   .\build.ps1 -NoInstaller
param([switch]$NoInstaller)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'ResSwitch'
$dist = Join-Path $root 'dist'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4) not found.' }

# version = AssemblyVersion in Program.cs
$m = [regex]::Match((Get-Content (Join-Path $src 'Program.cs') -Raw), 'AssemblyVersion\("(\d+\.\d+\.\d+)')
$version = if ($m.Success) { $m.Groups[1].Value } else { '1.0.0' }

New-Item -ItemType Directory -Force $dist | Out-Null
$exe = Join-Path $dist 'ResSwitch.exe'
if (Test-Path $exe) { Remove-Item $exe -Force }

$args = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-', '/warn:4',
    "/out:$exe",
    "/win32icon:$(Join-Path $src 'app.ico')",
    "/win32manifest:$(Join-Path $src 'app.manifest')",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    '/r:System.Web.Extensions.dll', '/r:System.Management.dll'
)
foreach ($f in Get-ChildItem (Join-Path $src 'lang') -Filter *.json) {
    $args += "/resource:$($f.FullName),ResSwitch.lang.$($f.Name)"
}
$args += (Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName })

& $csc @args
if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit $LASTEXITCODE)." }
Write-Host "Built $exe (version $version)" -ForegroundColor Green

# portable zip (exe only + README)
$zip = Join-Path $dist "ResSwitch-$version-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
$items = @($exe)
foreach ($name in 'README.md', 'LICENSE') {
    $f = Join-Path $root $name
    if (Test-Path $f) { $items += $f }
}
Compress-Archive -Path $items -DestinationPath $zip
Write-Host "Built $zip" -ForegroundColor Green

# installer
if (-not $NoInstaller) {
    $iscc = $null
    $cands = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $cands) { if ($c -and (Test-Path $c)) { $iscc = $c; break } }
    if ($iscc) {
        & $iscc "/DAppVersion=$version" (Join-Path $root 'installer\ResSwitch.iss')
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit $LASTEXITCODE)." }
        Write-Host "Built $(Join-Path $dist 'ResSwitch-Setup.exe')" -ForegroundColor Green
    }
    else {
        Write-Host 'Inno Setup not found: installer skipped (install it from https://jrsoftware.org/isinfo.php).' -ForegroundColor Yellow
    }
}
