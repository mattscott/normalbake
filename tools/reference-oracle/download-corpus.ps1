<#
.SYNOPSIS
    Downloads tier-B (real-asset) corpus models into tests/corpus/_downloads (git-ignored).

.DESCRIPTION
    The procedural tier-A corpus (sphere / box / cylinder) is generated in-code and committed; it needs
    no download and is where validation starts. This script fetches downloaded, real-world assets used
    for realism/stress tests once the pipeline is further along (do NOT block P4 on these).

    Highest priority are the glTF Sample Assets purpose-built for tangent-space correctness:
      * NormalTangentTest        — validates tangent-space decoding
      * NormalTangentMirrorTest  — validates mirrored-UV handedness (the w sign)
    plus real game assets (DamagedHelmet) that ship with UVs + normal maps.

    THE UV CAVEAT: dense scan meshes (Stanford bunny/armadillo/etc.) are high-poly ONLY — they have no
    low-poly + UV counterpart, and UV unwrapping is out of scope for NormalBake. Generating a matching
    UV-unwrapped low-poly is a one-time scripted Blender prep step (decimate -> smart UV unwrap ->
    export); see Agents/Prompts/01-implementation-kickoff.md §9. This script fetches only assets that
    already ship UV-unwrapped, plus the Stanford sources for that later prep step.

.EXAMPLE
    pwsh tools/reference-oracle/download-corpus.ps1
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\..\tests\corpus\_downloads')
)

$ErrorActionPreference = 'Stop'

# glTF Sample Assets (KhronosGroup) — UV-unwrapped, ship normal maps. glTF-Binary (.glb) is self-contained.
$gltfBase = 'https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/main/Models'
$gltfModels = @('NormalTangentTest', 'NormalTangentMirrorTest', 'DamagedHelmet')

# Stanford 3D Scanning Repository — dense HIGH-POLY sources (need a later Blender low-poly+UV prep).
$stanford = @(
    @{ Name = 'bunny';     Url = 'https://graphics.stanford.edu/pub/3Dscanrep/bunny.tar.gz' },
    @{ Name = 'armadillo'; Url = 'https://graphics.stanford.edu/pub/3Dscanrep/armadillo/Armadillo.ply.gz' }
)

function Save-File {
    param([string]$Url, [string]$Path)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    if (Test-Path $Path) {
        Write-Host "  exists  $Path"
        return
    }
    Write-Host "  GET     $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing
}

Write-Host "Downloading tier-B corpus into $OutDir"

Write-Host 'glTF Sample Assets (UV-unwrapped, normal-mapped):'
foreach ($m in $gltfModels) {
    $url = "$gltfBase/$m/glTF-Binary/$m.glb"
    Save-File -Url $url -Path (Join-Path $OutDir "gltf/$m/$m.glb")
}

Write-Host 'Stanford 3D Scanning Repository (high-poly sources; need later low-poly+UV prep):'
foreach ($s in $stanford) {
    $ext = [System.IO.Path]::GetExtension($s.Url)
    Save-File -Url $s.Url -Path (Join-Path $OutDir "stanford/$($s.Name)$ext")
}

Write-Host 'Done. (These are git-ignored; commit only small derived goldens under tests/golden.)'
