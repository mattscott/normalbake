<#
.SYNOPSIS
    Times normalbake across resolutions, serial vs all-cores, and checks bit-exactness.

.DESCRIPTION
    A lightweight throughput harness (not a CI gate). The parallel bake is tile-partitioned and sums each
    texel's samples in fixed triangle-index order, so its output is byte-identical to the serial bake —
    this script verifies that while reporting wall-clock. Build Release first: dotnet build -c Release.

.EXAMPLE
    pwsh tools/benchmark.ps1 -Resolutions 512,1024,2048
#>
[CmdletBinding()]
param(
    [int[]]$Resolutions = @(512, 1024),
    [int]$Samples = 3,
    [string]$High = 'tests/corpus/sphere/sphere_high.obj',
    [string]$Low = 'tests/corpus/sphere/sphere_low.obj'
)

$ErrorActionPreference = 'Stop'
$cli = 'src/NormalBake.Cli/bin/Release/net8.0/normalbake.dll'
if (-not (Test-Path $cli)) {
    Write-Host 'Building Release...'
    dotnet build -c Release | Out-Null
}

function Measure-Bake {
    param([int]$Res, [int]$Threads, [string]$Out)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    dotnet $cli $High $Low -o $Out --res $Res --samples $Samples --threads $Threads | Out-Null
    $sw.Stop()
    return $sw.Elapsed.TotalSeconds
}

Write-Host ('{0,-8} {1,-12} {2,-12} {3,-10} {4}' -f 'res', 'serial(s)', 'parallel(s)', 'speedup', 'identical')
foreach ($res in $Resolutions) {
    $serialOut = Join-Path $env:TEMP "nb_serial_$res.png"
    $parOut = Join-Path $env:TEMP "nb_par_$res.png"
    $serial = Measure-Bake -Res $res -Threads 1 -Out $serialOut
    $parallel = Measure-Bake -Res $res -Threads -1 -Out $parOut
    $identical = (Get-FileHash $serialOut).Hash -eq (Get-FileHash $parOut).Hash
    $speedup = if ($parallel -gt 0) { [math]::Round($serial / $parallel, 2) } else { 0 }
    Write-Host ('{0,-8} {1,-12:N2} {2,-12:N2} {3,-10} {4}' -f $res, $serial, $parallel, "${speedup}x", $identical)
}
