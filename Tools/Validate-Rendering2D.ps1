[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EngineRoot,

    [ValidateSet("d3d11", "d3d12", "vulkan")]
    [string]$Backend = "d3d12",

    [ValidateRange(300, 10000)]
    [int]$SmokeFrames = 600,

    [switch]$PrepareEngine
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Operation
    )

    & $Operation
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [string]$Pattern,

        [Parameter(Mandatory = $true)]
        [string]$Failure
    )

    if ($Text -notmatch $Pattern) {
        throw $Failure
    }
}

if (-not $IsWindows) {
    throw "Windows GPU acceptance must run on Windows x64 hardware."
}

$architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
if ($architecture -ne "X64") {
    throw "Windows GPU acceptance requires an x64 process; current architecture is '$architecture'."
}

$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$engineRootPath = (Resolve-Path $EngineRoot).Path
$engineSolution = Join-Path $engineRootPath "InnoEngine.sln"
$editorProject = Join-Path $engineRootPath "src/composition/editor/host/Inno.Editor.Application/Inno.Editor.Application.csproj"
$editorAssembly = Join-Path $engineRootPath "src/composition/editor/host/Inno.Editor.Application/bin/Debug/net9.0/Inno.Editor.Application.dll"

if (-not (Test-Path $engineSolution)) {
    throw "EngineRoot '$engineRootPath' does not contain InnoEngine.sln."
}

if ($PrepareEngine) {
    Push-Location $engineRootPath
    try {
        Invoke-Checked "SDL3 debug build" { dotnet run --project build/toolchains/Inno.Build.Toolchains.Sdl3 -- build --config debug }
        Invoke-Checked "MiniAudio debug build" { dotnet run --project build/toolchains/Inno.Build.Toolchains.MiniAudio -- build --config debug }
        Invoke-Checked "ImGui debug build" { dotnet run --project build/toolchains/Inno.Build.Toolchains.ImGui -- build --config debug }
        Invoke-Checked "ImGuizmo debug build" { dotnet run --project build/toolchains/Inno.Build.Toolchains.ImGuizmo -- build --config debug }
        Invoke-Checked "BGFX debug runtime build" { dotnet run --project build/toolchains/Inno.Build.Toolchains.Bgfx -- native --config debug }
        Invoke-Checked "BGFX debug tools build" { dotnet run --project build/toolchains/Inno.Build.Toolchains.Bgfx -- tools --config debug }
    }
    finally {
        Pop-Location
    }
}

Invoke-Checked "Inno Editor build" {
    dotnet restore $editorProject --disable-build-servers -p:NuGetAudit=false
    dotnet build $editorProject --no-restore --disable-build-servers -m:1 -nr:false
}

if (-not (Test-Path $editorAssembly)) {
    throw "The Editor build did not produce '$editorAssembly'."
}

$projectRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("InnoRendering2DAcceptance-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $projectRoot | Out-Null
Copy-Item -Recurse (Join-Path $sourceRoot "Assets") (Join-Path $projectRoot "Assets")
Copy-Item (Join-Path $sourceRoot "Settings.Project.inno") (Join-Path $projectRoot "Settings.Project.inno")
if (Test-Path (Join-Path $sourceRoot "Plugins")) {
    Copy-Item -Recurse (Join-Path $sourceRoot "Plugins") (Join-Path $projectRoot "Plugins")
}
# Shader compilation runs only after the Editor activates the Plugin's graph Target extensions.
# Active compilation errors fail the smoke run; no complete-source shaderc bypass is retained.
$logDirectory = Join-Path $sourceRoot "Logs"
New-Item -ItemType Directory -Force $logDirectory | Out-Null
$acceptanceLog = Join-Path $logDirectory "Rendering2D-$Backend-GpuAcceptance.log"
$bootLog = Join-Path $projectRoot "Logs/EditorBoot.log"
$bootLineCount = if (Test-Path $bootLog) { @(Get-Content $bootLog).Count } else { 0 }

$previousRebuild = $env:INNO_RENDERING2D_REBUILD_GPU_ACCEPTANCE
$previousGpuAcceptance = $env:INNO_RENDERING2D_RUN_GPU_ACCEPTANCE
$previousPerformance = $env:INNO_RENDERING2D_RUN_PERFORMANCE_GATE
$previousScale = $env:INNO_RENDERING2D_RUN_SCALE_GATE
$previousGameViewResize = $env:INNO_RENDERING2D_RUN_GAME_VIEW_RESIZE_GATE
try {
    $env:INNO_RENDERING2D_REBUILD_GPU_ACCEPTANCE = "1"
    $env:INNO_RENDERING2D_RUN_GPU_ACCEPTANCE = "1"
    $env:INNO_RENDERING2D_RUN_PERFORMANCE_GATE = "1"
    $env:INNO_RENDERING2D_RUN_SCALE_GATE = "1"
    $env:INNO_RENDERING2D_RUN_GAME_VIEW_RESIZE_GATE = "1"
    $processOutput = & dotnet $editorAssembly `
        $projectRoot `
        --graphics-api $Backend `
        --smoke-frames $SmokeFrames 2>&1 | Tee-Object -FilePath $acceptanceLog
    $editorExitCode = $LASTEXITCODE
}
finally {
    $env:INNO_RENDERING2D_REBUILD_GPU_ACCEPTANCE = $previousRebuild
    $env:INNO_RENDERING2D_RUN_GPU_ACCEPTANCE = $previousGpuAcceptance
    $env:INNO_RENDERING2D_RUN_PERFORMANCE_GATE = $previousPerformance
    $env:INNO_RENDERING2D_RUN_SCALE_GATE = $previousScale
    $env:INNO_RENDERING2D_RUN_GAME_VIEW_RESIZE_GATE = $previousGameViewResize
}

if ($editorExitCode -ne 0) {
    throw "Rendering2D $Backend GPU acceptance exited with code $editorExitCode. See '$acceptanceLog'."
}

$newBootLines = if (Test-Path $bootLog) {
    @(Get-Content $bootLog | Select-Object -Skip $bootLineCount)
} else {
    @()
}
$acceptanceText = (@($processOutput) + $newBootLines) -join [Environment]::NewLine
$backendPattern = switch ($Backend) {
    "d3d11" { "Rendering initialized with Direct3D11" }
    "d3d12" { "Rendering initialized with Direct3D12" }
    "vulkan" { "Rendering initialized with Vulkan" }
}

Assert-Contains $acceptanceText $backendPattern "The requested $Backend renderer was not initialized."
Assert-Contains $acceptanceText "Rebuilt the Rendering2D GPU acceptance sample" "The deterministic GPU acceptance scene was not rebuilt."
Assert-Contains $acceptanceText "Rendering2D GPU acceptance frame prepared: .* lit instances, (3[2-9]|[4-9][0-9]|[1-9][0-9]{2,}) lights, .* shadow casters, 5-level Bloom" "The frame did not contain the required lit geometry, at least 32 dynamic lights, shadow caster, and Bloom pyramid."
Assert-Contains $acceptanceText "Rendering2D GPU acceptance passed: (3[2-9]|[4-9][0-9]|[1-9][0-9]{2,}) lights, .* light draws, .* shadow-volume draws, HDR/MRT/D24S8, 5-level Bloom" "The GPU render graph did not pass at least 32-light HDR lighting, MRT normals, stencil shadows, and multi-level Bloom acceptance."
Assert-Contains $acceptanceText "Rendering2D performance gate passed: 0 managed bytes" "Stable Scene extraction allocated managed memory or did not run."
Assert-Contains $acceptanceText "Rendering2D runtime-camera performance gate passed: 0 managed bytes" "Stable runtime-camera extraction allocated managed memory or did not run."
Assert-Contains $acceptanceText "Rendering2D camera-stack performance gate passed: 0 managed bytes" "Stable camera-stack extraction allocated managed memory or did not run."
Assert-Contains $acceptanceText "Rendering2D request-submission performance gate passed: 0 managed bytes" "Stable request extraction/submission allocated managed memory or did not run."
Assert-Contains $acceptanceText "Rendering2D scale gate passed: 100000 visible tiles in a 1000000-cell sparse domain, 32 lights, 0 managed bytes" "The 100k-visible/million-cell/32-light scale gate allocated managed memory or did not run."
Assert-Contains $acceptanceText "Rendering2D Editor Game View resize gate passed: 96 consecutive .* render-target rebuilds completed" "The Editor Game View did not survive 96 consecutive render-target size changes."
Assert-Contains $acceptanceText "Rendering2D steady-state GPU resource gate armed after 120 rendered frames" "The steady-state GPU resource gate did not reach its warmup boundary."
Assert-Contains $acceptanceText "Smoke frame limit reached after $SmokeFrames frame\(s\)\." "The Editor did not submit the requested number of frames."

if ($acceptanceText -match "BGFX FATAL|Unhandled exception|Teardown failure|Microsoft Basic Render Driver|SwiftShader|llvmpipe|\bWARP\b") {
    throw "Rendering2D $Backend acceptance reported a fatal error, teardown failure, or software renderer."
}
if ($acceptanceText -match "unknown stable type|\[Warn\]\s+ASSET-REFERENCE") {
    throw "Rendering2D $Backend acceptance reported an asset-reference tombstone warning."
}

$steadyStateMarker = "Rendering2D steady-state GPU resource gate armed after 120 rendered frames"
$steadyStateStart = $acceptanceText.IndexOf($steadyStateMarker, [StringComparison]::Ordinal)
$steadyStateOutput = $acceptanceText.Substring($steadyStateStart + $steadyStateMarker.Length)
if ($steadyStateOutput -match "BGFX Texture .*RT\[x\]") {
    throw "Rendering2D $Backend acceptance rebuilt a render-target texture after the steady-state gate was armed."
}

Invoke-Checked "Rendering2D generated script build" {
    dotnet build (Join-Path $projectRoot "Inno.EditorScripts.csproj") `
        -p:NuGetAudit=false `
        --disable-build-servers `
        -m:1 `
        -warnaserror
}

Write-Host "Rendering2D Windows GPU acceptance passed on $architecture/$Backend. Log: $acceptanceLog"
Write-Host "Disposable acceptance project retained for inspection: $projectRoot"
