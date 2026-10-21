<#
.SYNOPSIS
    Numbers a Release All run: the next release version, or a validated explicit one.

.DESCRIPTION
    Desktop and mobile used to number their nightlies from two separate github.run_number
    counters, so one build shipped as v0.1.122 to desktops and mobile-v0.1.63 to phones. One
    release now carries both packages under one tag, numbered from the tags already
    published rather than from a counter that belongs to whichever workflow happens to run.

    Without -Requested, the version is the highest MAJOR.MINOR.PATCH over the v* and mobile-v*
    tags with the patch raised by one. Taking the highest over both namespaces is what keeps
    every package monotonic: the MSIX Version must rise past the last desktop release, and
    the APK versionCode past the last phone release, whichever of the two is higher.

    With -Requested (a dispatched version, Deploy All's, or the pushed v* tag), the version is
    that one, and it may not be lower than the highest existing tag: a lower MSIX or
    versionCode would not install over what the devices already have. Equal is a rerun of
    that release and is allowed here; Release All accepts it only from the commit the tag
    already points at, since this script sees tag names, not commits.

    The packed versionCode follows ApkVersion (src/App/Backlog.Mobile.UI/AppUpdate/ApkVersion.cs)
    digit for digit: MAJOR * 10^7 + MINOR * 10^5 + PATCH, with the limits that keep the packing
    unambiguous: 0.1.123 packs to 100123.

.PARAMETER Tag
    The repository's release tags. Anything that is not v<MAJOR.MINOR.PATCH> or
    mobile-v<MAJOR.MINOR.PATCH> is ignored.

.PARAMETER Requested
    An explicit version, with or without the leading v. Blank numbers the release itself.

.OUTPUTS
    PSCustomObject with Tag (v-prefixed), Display (MAJOR.MINOR.PATCH), Package (the four-part
    MSIX version), VersionCode (the APK's), and Derived (true when this script chose the
    number, which is what Release All treats as a nightly).
#>
[CmdletBinding()]
param(
    [string[]] $Tag = @(),

    [string] $Requested = ''
)

$ErrorActionPreference = 'Stop'

$releaseTag = '^(?:mobile-)?v(\d+)\.(\d+)\.(\d+)$'

$highest = $null
foreach ($candidate in $Tag) {
    if ("$candidate".Trim() -notmatch $releaseTag) { continue }

    $version = [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
    if ($null -eq $highest -or $version -gt $highest) { $highest = $version }
}

$requestedText = "$Requested".Trim()
$derived = [string]::IsNullOrEmpty($requestedText)

if ($derived) {
    $next = if ($null -eq $highest) { [version]::new(0, 1, 1) }
            else { [version]::new($highest.Major, $highest.Minor, $highest.Build + 1) }
}
else {
    if ($requestedText -notmatch '^v?(\d+)\.(\d+)\.(\d+)$') {
        throw "Version must be MAJOR.MINOR.PATCH or a v-prefixed tag (got '$requestedText')."
    }
    $next = [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])

    if ($null -ne $highest -and $next -lt $highest) {
        throw "Requested version $next is lower than the highest existing release, $highest. A lower version would not install over it; release $highest again, or a higher version."
    }
}

$major = $next.Major
$minor = $next.Minor
$patch = $next.Build
if ($major -gt 214) { throw "Major version $major exceeds the versionCode packing limit of 214." }
if ($minor -gt 99) { throw "Minor version $minor exceeds the versionCode packing limit of 99." }
if ($patch -gt 99999) { throw "Patch version $patch exceeds the versionCode packing limit of 99999." }

$versionCode = ([long]$major * 10000000) + ($minor * 100000) + $patch
if ($versionCode -lt 1) { throw "Derived versionCode must be at least 1 (got $versionCode)." }

$display = "$major.$minor.$patch"

[pscustomobject]@{
    Tag         = "v$display"
    Display     = $display
    Package     = "$display.0"
    VersionCode = [long]$versionCode
    Derived     = $derived
}
