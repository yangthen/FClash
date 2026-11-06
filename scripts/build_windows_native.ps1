param(
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$Label = '0.8.96-winui-preview',
    [string]$Targets = 'zip',
    [string]$InnoCompiler = $env:INNO_SETUP_COMPILER
)
$ErrorActionPreference = 'Stop'
$requestedTargets = @($Targets.Split(',') | ForEach-Object { $_.Trim() })
if (!$requestedTargets.Count -or @($requestedTargets | Where-Object { $_ -notin @('exe', 'zip') }).Count) {
    throw "Use -Targets 'exe,zip', 'exe' or 'zip'"
}
$repo = Split-Path $PSScriptRoot -Parent
$rid = 'win-' + $Architecture
$platform = if ($Architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
$goArch = if ($Architecture -eq 'arm64') { 'arm64' } else { 'amd64' }
$output = Join-Path $repo ('build/native-windows/' + $Label + '/' + $rid)
$dist = Join-Path $repo 'dist/windows-native'
New-Item -ItemType Directory -Path $output, $dist -Force | Out-Null
Push-Location $repo
try {
    $versions = ConvertFrom-StringData (Get-Content (Join-Path $repo 'version.properties') -Raw)
    $semver = $versions.semver
    if (!$semver -or $semver -notmatch '^\d+\.\d+\.\d+$') { throw 'version.properties has no valid semver' }
    dotnet publish windows-native/FClash.WinUI/FClash.WinUI.csproj -c Release -r $rid -p:Platform=$platform "-p:Version=$semver" "-p:InformationalVersion=$Label" -o $output --nologo
    if ($LASTEXITCODE -ne 0) { throw 'WinUI publish failed' }
    foreach ($required in @('FClash.pri', 'App.xbf', 'MainWindow.xbf', 'Assets/material-palettes.json', 'Assets/Fonts/materialicons-regular.otf', 'Assets/Fonts/roboto-regular.ttf')) {
        if (!(Test-Path -LiteralPath (Join-Path $output $required))) { throw "Published UI asset is missing: $required" }
    }
    $env:CGO_ENABLED = '0'; $env:GOOS = 'windows'; $env:GOARCH = $goArch
    Push-Location core
    try {
        go build -trimpath -tags with_gvisor -ldflags '-s -w -H windowsgui' -o (Join-Path $output 'FlClashCore.exe') .
        if ($LASTEXITCODE -ne 0) { throw 'Core build failed' }
    } finally { Pop-Location }
    $geo = Join-Path $output 'GeoData'
    New-Item -ItemType Directory -Path $geo -Force | Out-Null
    Copy-Item -Path resources/geodata/* -Destination $geo -Force
    foreach ($document in @('README.md', 'ui-parity.md', 'validation.md')) {
        Copy-Item -LiteralPath (Join-Path $repo ('windows-native/' + $document)) -Destination (Join-Path $output $document) -Force
    }
    Copy-Item -LiteralPath LICENSE -Destination (Join-Path $output 'LICENSE') -Force
    $name = 'FClash-' + $Label + '-windows-' + $goArch
    if ($requestedTargets -contains 'zip') {
        $zip = Join-Path $dist ($name + '.zip')
        if (Test-Path -LiteralPath $zip) { throw "Artifact already exists: $zip. Use a distinct label or move it explicitly." }
        Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal
    }
    if ($requestedTargets -contains 'exe') {
        if (!$InnoCompiler) {
            $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($command) { $InnoCompiler = $command.Source }
        }
        if (!$InnoCompiler -and $env:INNO_SETUP_PATH) { $InnoCompiler = Join-Path $env:INNO_SETUP_PATH 'ISCC.exe' }
        if (!$InnoCompiler) {
            foreach ($path in @("${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe", "$env:ProgramFiles/Inno Setup 6/ISCC.exe", "$env:ProgramFiles/Inno Setup 7/ISCC.exe")) {
                if (Test-Path -LiteralPath $path) { $InnoCompiler = $path; break }
            }
        }
        if (!$InnoCompiler -or !(Test-Path -LiteralPath $InnoCompiler)) { throw 'Set INNO_SETUP_COMPILER to ISCC.exe for setup packaging' }
        if (Test-Path -LiteralPath (Join-Path $dist ($name + '-setup.exe'))) { throw 'Setup artifact already exists; choose a distinct label' }
        & $InnoCompiler /Q "/DSourceDir=$output" "/DOutputDir=$dist" "/DOutputName=$name-setup" "/DReleaseLabel=$Label" "/DArchitecture=$Architecture" windows-native/installer.iss
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
    }
    Get-ChildItem -LiteralPath $dist -File | Where-Object { $_.Extension -in '.exe', '.zip' } |
        Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + (Split-Path $_.Path -Leaf) } |
        Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS') -Encoding utf8
    Write-Output $dist
} finally { Pop-Location }
