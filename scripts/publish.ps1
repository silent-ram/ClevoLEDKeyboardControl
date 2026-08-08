param(
    [string]$Configuration = "Release",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $root "publish"
$servicePublish = Join-Path $publishRoot "Service"
$trayPublish = Join-Path $publishRoot "Tray"
$installerPayload = Join-Path $root "ColorfulLedKeyboard.Installer\Payload\payload.zip"
$installerPublish = Join-Path $publishRoot "Setup"
$driverDllName = "InsydeDCHU.dll"
$versionArgs = @()
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $versionArgs = @(
        "-p:Version=$Version",
        "-p:AssemblyVersion=$Version.0",
        "-p:FileVersion=$Version.0",
        "-p:InformationalVersion=$Version"
    )
}

function Get-DriverCandidatePaths {
    if (-not [string]::IsNullOrWhiteSpace($env:CLEVO_DRIVER_DLL)) {
        $env:CLEVO_DRIVER_DLL
    }

    Join-Path $root "assets\driver\$driverDllName"

    $programRoots = @(
        ${env:ProgramFiles(x86)},
        $env:ProgramFiles,
        $env:ProgramW6432
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($programRoot in $programRoots) {
        foreach ($folder in @("ControlCenter", "Control Center", "ControlCenter3", "Control Center 3.0")) {
            Join-Path (Join-Path $programRoot $folder) $driverDllName
        }
    }
}

function Get-FirstExistingDriver {
    foreach ($candidate in Get-DriverCandidatePaths) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

if (Test-Path $publishRoot) {
    Get-ChildItem -LiteralPath $publishRoot -Force | Remove-Item -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $servicePublish, $trayPublish, $installerPublish | Out-Null

dotnet publish (Join-Path $root "ColorfulLedKeyboard.Service\ColorfulLedKeyboard.Service.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $servicePublish `
    @versionArgs

dotnet publish (Join-Path $root "ColorfulLedKeyboard.Tray\ColorfulLedKeyboard.Tray.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $trayPublish `
    @versionArgs

if (Test-Path $installerPayload) {
    Remove-Item -LiteralPath $installerPayload -Force
}

$payloadStage = Join-Path $publishRoot "Payload"
if (Test-Path $payloadStage) {
    Remove-Item -LiteralPath $payloadStage -Recurse -Force
}

New-Item -ItemType Directory -Force -Path (Join-Path $payloadStage "Service"), (Join-Path $payloadStage "Tray") | Out-Null
Copy-Item -Path (Join-Path $servicePublish "*") -Destination (Join-Path $payloadStage "Service") -Recurse -Force
Copy-Item -Path (Join-Path $trayPublish "*") -Destination (Join-Path $payloadStage "Tray") -Recurse -Force

$driverSource = Get-FirstExistingDriver
if ($driverSource) {
    Copy-Item -LiteralPath $driverSource -Destination (Join-Path $payloadStage "Service\$driverDllName") -Force
    Write-Host "Bundled $driverDllName from $driverSource"
}
else {
    Write-Warning "$driverDllName was not found. The setup executable will still be built; it will try to copy the driver from the user's OEM Control Center during installation."
}
Compress-Archive -Path (Join-Path $payloadStage "*") -DestinationPath $installerPayload -Force

dotnet publish (Join-Path $root "ColorfulLedKeyboard.Installer\ColorfulLedKeyboard.Installer.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o $installerPublish `
    @versionArgs

Copy-Item -LiteralPath (Join-Path $installerPublish "ClevoLEDKeyboardControlSetup.exe") -Destination (Join-Path $publishRoot "ClevoLEDKeyboardControlSetup.exe") -Force

Write-Host "Published service to $servicePublish"
Write-Host "Published tray app to $trayPublish"
Write-Host "Published setup executable to $(Join-Path $publishRoot "ClevoLEDKeyboardControlSetup.exe")"
Write-Host "Setup will automatically search for $driverDllName in the setup payload, next to the setup executable, old install directories, and OEM Control Center folders."
