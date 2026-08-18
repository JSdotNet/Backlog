<#
.SYNOPSIS
    Builds a signed Backlog Android APK and installs it on a connected device or
    running emulator via adb.

.DESCRIPTION
    Developer sideload helper. It is the local counterpart of the "Release mobile"
    GitHub Actions workflow: same publish command, same package format (APK, not
    AAB), but signed with a throwaway *developer* keystore instead of the release
    keystore held in repository secrets.

    On first run it creates that developer keystore under build/.local/, which is
    gitignored. That keystore is for local sideloading only — it is not the release
    identity, and an APK signed with it cannot upgrade a release-signed install
    (uninstall first, or vice versa; Android refuses signature changes).

    Requirements, all located automatically when they are in their usual places:
      - .NET SDK with the android/maui-android workload
      - A JDK (for keytool) — JAVA_HOME, or the Visual Studio OpenJDK
      - The Android SDK (for adb) — ANDROID_HOME/ANDROID_SDK_ROOT, or the
        Visual Studio Android SDK

.PARAMETER Configuration
    Build configuration. Defaults to Release, which is what produces an installable
    signed APK. Debug Android builds expect a debugger/fast-deployment host.

.PARAMETER DisplayVersion
    ApplicationDisplayVersion (the user-visible version, e.g. "1.0"). Defaults to
    whatever Backlog.Mobile.csproj declares.

.PARAMETER VersionCode
    ApplicationVersion (the integer Android versionCode). Defaults to whatever
    Backlog.Mobile.csproj declares. Increase it to install over a previous local
    build without uninstalling.

.PARAMETER KeystorePath
    Developer keystore location. Defaults to build/.local/backlog-android.keystore
    and is created on first use.

.PARAMETER KeyAlias
    Key alias inside the developer keystore. Defaults to "backlog-dev".

.PARAMETER KeystorePassword
    Password for the developer keystore and key. When omitted, the script uses
    $env:BACKLOG_ANDROID_KEYSTORE_PASSWORD, and failing that a random password
    generated on keystore creation and cached next to the keystore in the same
    gitignored folder.

.PARAMETER Device
    Target a specific adb device/emulator serial (adb -s). Omit when exactly one
    device is attached.

.PARAMETER SkipInstall
    Build and sign the APK but do not install it. Useful for producing an APK to
    copy onto a phone manually.

.EXAMPLE
    ./build/Install-AndroidApp.ps1

    Build a signed APK and install it on the single attached device or emulator.

.EXAMPLE
    ./build/Install-AndroidApp.ps1 -Device emulator-5554 -VersionCode 2

    Install onto a named emulator, bumping the versionCode so it upgrades in place.

.EXAMPLE
    ./build/Install-AndroidApp.ps1 -SkipInstall

    Produce the APK only, and print its path for manual transfer to a phone.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$DisplayVersion,
    [int]$VersionCode,
    [string]$KeystorePath,
    [string]$KeyAlias = 'backlog-dev',
    [string]$KeystorePassword,
    [string]$Device,
    [switch]$SkipInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src/App/Backlog.Mobile/Backlog.Mobile.csproj'
$targetFramework = 'net10.0-android'
$applicationId = 'com.jsdotnet.backlog.mobile'

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Mobile project not found: $projectPath"
}

$localDir = Join-Path $PSScriptRoot '.local'
if (-not $KeystorePath) {
    $KeystorePath = Join-Path $localDir 'backlog-android.keystore'
}
$passwordCachePath = "$KeystorePath.password"

function Resolve-Tool {
    <#
        Finds an executable by trying, in order: an explicit root hint, the PATH,
        then a list of well-known install locations. Android tooling is routinely
        installed by Visual Studio without landing on PATH, so "not found" almost
        always means "not on PATH", not "not installed".
    #>
    param(
        [Parameter(Mandatory)][string]$Name,
        [string[]]$Candidates = @()
    )

    $onPath = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($onPath) { return $onPath.Source }

    foreach ($candidate in $Candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Get-AndroidSdkRoot {
    foreach ($fromEnv in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)) {
        if ($fromEnv -and (Test-Path -LiteralPath $fromEnv)) { return $fromEnv }
    }

    $known = @(
        (Join-Path $env:LOCALAPPDATA 'Android/Sdk'),
        'C:/Program Files (x86)/Android/android-sdk',
        (Join-Path $HOME 'Android/Sdk'),
        (Join-Path $HOME 'Library/Android/sdk')
    )
    foreach ($path in $known) {
        if ($path -and (Test-Path -LiteralPath $path)) { return (Resolve-Path -LiteralPath $path).Path }
    }

    return $null
}

function Get-KeyToolPath {
    $candidates = @()
    if ($env:JAVA_HOME) {
        $candidates += (Join-Path $env:JAVA_HOME 'bin/keytool.exe')
        $candidates += (Join-Path $env:JAVA_HOME 'bin/keytool')
    }

    # Visual Studio / .NET Android install their OpenJDK here.
    foreach ($root in @('C:/Program Files/Android/openjdk', 'C:/Program Files/Microsoft')) {
        if (Test-Path -LiteralPath $root) {
            $candidates += Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending |
                ForEach-Object { Join-Path $_.FullName 'bin/keytool.exe' }
        }
    }

    return Resolve-Tool -Name 'keytool' -Candidates $candidates
}

function New-DeveloperKeystore {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Alias,
        [Parameter(Mandatory)][string]$Password
    )

    $keytool = Get-KeyToolPath
    if (-not $keytool) {
        throw @"
keytool was not found, so the developer keystore cannot be created.
Set JAVA_HOME to a JDK 17 or 21 installation, or install one with the .NET MAUI
workload (Visual Studio installs it under 'C:\Program Files\Android\openjdk').
"@
    }

    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    Write-Host "Creating developer keystore at $Path (alias '$Alias')." -ForegroundColor Cyan
    & $keytool -genkeypair `
        -alias $Alias `
        -keyalg RSA `
        -keysize 2048 `
        -validity 10000 `
        -storetype pkcs12 `
        -keystore $Path `
        -storepass $Password `
        -keypass $Password `
        -dname 'CN=Backlog Developer, OU=Local Sideload, O=JSdotNet, C=NL'
    if ($LASTEXITCODE -ne 0) {
        throw "keytool failed to create the developer keystore at $Path."
    }
}

# --- Resolve the keystore password ------------------------------------------

$passwordSource = 'the -KeystorePassword parameter'
if (-not $KeystorePassword) {
    if ($env:BACKLOG_ANDROID_KEYSTORE_PASSWORD) {
        $KeystorePassword = $env:BACKLOG_ANDROID_KEYSTORE_PASSWORD
        $passwordSource = 'BACKLOG_ANDROID_KEYSTORE_PASSWORD'
    }
    elseif (Test-Path -LiteralPath $passwordCachePath) {
        $KeystorePassword = (Get-Content -LiteralPath $passwordCachePath -Raw).Trim()
        $passwordSource = $passwordCachePath
    }
    elseif (Test-Path -LiteralPath $KeystorePath) {
        throw @"
The keystore $KeystorePath exists but its password is unknown.
Pass -KeystorePassword, set BACKLOG_ANDROID_KEYSTORE_PASSWORD, or delete the
keystore to have a fresh developer one generated.
"@
    }
    else {
        # Fresh developer keystore: generate a random password and cache it beside
        # the keystore, inside the same gitignored folder. This identity has no
        # distribution value and never leaves the machine.
        $bytes = [byte[]]::new(24)
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
        $KeystorePassword = [Convert]::ToBase64String($bytes)
        $passwordSource = 'a newly generated developer password'
    }
}

if (-not (Test-Path -LiteralPath $KeystorePath)) {
    New-DeveloperKeystore -Path $KeystorePath -Alias $KeyAlias -Password $KeystorePassword

    if (-not (Test-Path -LiteralPath $passwordCachePath)) {
        $directory = Split-Path -Parent $passwordCachePath
        if ($directory -and -not (Test-Path -LiteralPath $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
        Set-Content -LiteralPath $passwordCachePath -Value $KeystorePassword -NoNewline -Encoding utf8
        Write-Host "Cached the developer keystore password at $passwordCachePath (gitignored)." -ForegroundColor Cyan
    }
}
else {
    Write-Host "Using existing developer keystore $KeystorePath (password from $passwordSource)." -ForegroundColor Cyan
}

# --- Publish a signed APK ----------------------------------------------------

# The env: prefix keeps the password out of the MSBuild command line and the
# process table; .NET Android reads it from the environment instead.
$env:BACKLOG_ANDROID_SIGNING_PASSWORD = $KeystorePassword

$publishArgs = @(
    'publish', $projectPath,
    '-f', $targetFramework,
    '-c', $Configuration,
    # APK, not AAB: an .aab is only consumable by Google Play, and this package is
    # installed straight onto a device.
    '-p:AndroidPackageFormat=apk',
    '-p:AndroidKeyStore=true',
    "-p:AndroidSigningKeyStore=$((Resolve-Path -LiteralPath $KeystorePath).Path)",
    "-p:AndroidSigningKeyAlias=$KeyAlias",
    '-p:AndroidSigningStorePass=env:BACKLOG_ANDROID_SIGNING_PASSWORD',
    '-p:AndroidSigningKeyPass=env:BACKLOG_ANDROID_SIGNING_PASSWORD'
)
if ($DisplayVersion) { $publishArgs += "-p:ApplicationDisplayVersion=$DisplayVersion" }
if ($PSBoundParameters.ContainsKey('VersionCode')) { $publishArgs += "-p:ApplicationVersion=$VersionCode" }

Write-Host "Publishing $targetFramework ($Configuration)..." -ForegroundColor Cyan
try {
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item Env:BACKLOG_ANDROID_SIGNING_PASSWORD -ErrorAction SilentlyContinue
}

$outputRoot = Join-Path $repoRoot "src/App/Backlog.Mobile/bin/$Configuration"
$apk = Get-ChildItem -Path $outputRoot -Recurse -Filter '*-Signed.apk' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $apk) {
    throw "No signed .apk was produced under $outputRoot."
}

Write-Host "Signed APK: $($apk.FullName)" -ForegroundColor Green

if ($SkipInstall) {
    Write-Host 'Skipping install (-SkipInstall). Copy the APK to the device and open it there.' -ForegroundColor Yellow
    return
}

# --- Install via adb ---------------------------------------------------------

$sdkRoot = Get-AndroidSdkRoot
$adbCandidates = @()
if ($sdkRoot) {
    $adbCandidates += (Join-Path $sdkRoot 'platform-tools/adb.exe')
    $adbCandidates += (Join-Path $sdkRoot 'platform-tools/adb')
}
$adb = Resolve-Tool -Name 'adb' -Candidates $adbCandidates
if (-not $adb) {
    throw @"
adb was not found, so the APK cannot be installed.
Set ANDROID_HOME to your Android SDK, or install the SDK platform-tools. The APK
is already built at:
  $($apk.FullName)
Copy it to the phone and open it there, or re-run with -SkipInstall to build only.
"@
}

$deviceLines = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\S' })
$attached = @($deviceLines | Where-Object { $_ -match '^\s*(\S+)\s+device\s*$' } | ForEach-Object { $Matches[1] })

if ($attached.Count -eq 0) {
    $unauthorized = @($deviceLines | Where-Object { $_ -match 'unauthorized' })
    if ($unauthorized.Count -gt 0) {
        throw @"
An Android device is attached but unauthorized. Unlock the phone and accept the
'Allow USB debugging' prompt, then re-run this script.
"@
    }
    throw @"
No Android device or emulator is attached.
Enable Developer options and USB debugging on the phone and connect it over USB,
or start an emulator, then re-run. The APK is already built at:
  $($apk.FullName)
"@
}

if (-not $Device) {
    if ($attached.Count -gt 1) {
        throw "More than one device is attached ($($attached -join ', ')). Re-run with -Device <serial>."
    }
    $Device = $attached[0]
}
elseif ($attached -notcontains $Device) {
    throw "Device '$Device' is not attached. Attached: $($attached -join ', ')."
}

Write-Host "Installing $($apk.Name) on $Device..." -ForegroundColor Cyan
& $adb -s $Device install -r $apk.FullName
if ($LASTEXITCODE -ne 0) {
    throw @"
adb install failed. The most common cause is a signature mismatch with an already
installed copy of $applicationId (for example a release-signed build). Uninstall it
first:
  adb -s $Device uninstall $applicationId
"@
}

Write-Host "Installed $applicationId on $Device." -ForegroundColor Green
Write-Host "Launch it from the app drawer, or run: adb -s $Device shell monkey -p $applicationId 1"
