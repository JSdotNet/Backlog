<#
.SYNOPSIS
  The dispatch-sessions skill's machine probe: average CPU over a window and free memory,
  against the skill's two machine thresholds.

.DESCRIPTION
  Samples Win32_Processor.LoadPercentage once a second for -Seconds and reads
  Win32_OperatingSystem.FreePhysicalMemory, both through CIM, so the probe does not depend on
  the localized performance-counter names Get-Counter needs. Prints one JSON line:

    {"cpuPercent":12.3,"freeGb":9.87,"maxCpu":80,"minFreeGb":4,"fits":true,"heldBy":[]}

  heldBy names what holds the queue: "cpu" when the average is at or over -MaxCpu, "memory"
  when free memory is under -MinFreeGb. It never exits non-zero for a held machine: the
  answer is the line. A machine whose processors report no load prints cpuPercent null, and
  CPU then holds nothing.
#>
param(
    [ValidateRange(1, 600)][int] $Seconds = 30,
    [ValidateRange(1, 100)][double] $MaxCpu = 80,
    [ValidateRange(0, 1024)][double] $MinFreeGb = 4
)

$ErrorActionPreference = 'Stop'

# One sample a second until -Seconds have passed: a CIM call itself takes up to a second, so the
# pause only fills what is left of each second and the window stays -Seconds long.
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$samples = for ($i = 1; $i -le $Seconds; $i++) {
    # One value per processor package; their mean is the machine's load for this second.
    # Some virtual machines report no LoadPercentage at all: those samples are left out.
    (Get-CimInstance -ClassName Win32_Processor | Where-Object { $null -ne $_.LoadPercentage } |
        Measure-Object -Property LoadPercentage -Average).Average
    $left = $i * 1000 - $clock.ElapsedMilliseconds
    if ($i -lt $Seconds -and $left -gt 0) { Start-Sleep -Milliseconds $left }
}
$samples = @($samples | Where-Object { $null -ne $_ })

# No sample at all is a machine that cannot say: cpuPercent is null and CPU holds nothing.
$cpu = if ($samples.Count -gt 0) { [math]::Round(($samples | Measure-Object -Average).Average, 1) } else { $null }
$freeGb = [math]::Round((Get-CimInstance -ClassName Win32_OperatingSystem).FreePhysicalMemory / 1MB, 2)

$heldBy = @()
if ($null -ne $cpu -and $cpu -ge $MaxCpu) { $heldBy += 'cpu' }
if ($freeGb -lt $MinFreeGb) { $heldBy += 'memory' }

[ordered]@{
    cpuPercent = $cpu
    freeGb     = $freeGb
    maxCpu     = $MaxCpu
    minFreeGb  = $MinFreeGb
    fits       = $heldBy.Count -eq 0
    heldBy     = $heldBy
} | ConvertTo-Json -Compress
