[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EngineRoot,

    [ValidateSet("d3d11", "d3d12", "vulkan")]
    [string]$Backend = "d3d12",

    [ValidateRange(300, 10000)]
    [int]$SmokeFrames = 3000,

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

if (-not $IsWindows) {
    throw "Windows GPU validation must run on Windows x64 hardware."
}

$architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
if ($architecture -ne "X64") {
    throw "Windows GPU validation requires an x64 process; current architecture is '$architecture'."
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

Invoke-Checked "Inno Editor restore" {
    dotnet restore $editorProject --disable-build-servers -p:NuGetAudit=false
}
Invoke-Checked "Inno Editor build" {
    dotnet build $editorProject --no-restore --disable-build-servers -m:1 -nr:false
}

if (-not (Test-Path $editorAssembly)) {
    throw "The Editor build did not produce '$editorAssembly'."
}

$projectRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("InnoRendering2DValidation-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $projectRoot | Out-Null
Copy-Item -Recurse (Join-Path $sourceRoot "Assets") (Join-Path $projectRoot "Assets")
Copy-Item (Join-Path $sourceRoot "Settings.Project.inno") (Join-Path $projectRoot "Settings.Project.inno")
if (Test-Path (Join-Path $sourceRoot "Plugins")) {
    Copy-Item -Recurse (Join-Path $sourceRoot "Plugins") (Join-Path $projectRoot "Plugins")
}

$logDirectory = Join-Path $sourceRoot "Logs"
New-Item -ItemType Directory -Force $logDirectory | Out-Null
$validationLog = Join-Path $logDirectory "Rendering2D-$Backend-GpuValidation.log"
$processOutput = & dotnet $editorAssembly `
    $projectRoot `
    --graphics-api $Backend `
    --smoke-frames $SmokeFrames 2>&1 | Tee-Object -FilePath $validationLog
$editorExitCode = $LASTEXITCODE

if ($editorExitCode -ne 0) {
    throw "Rendering2D $Backend GPU validation exited with code $editorExitCode. See '$validationLog'."
}

$validationText = @($processOutput) -join [Environment]::NewLine
$backendPattern = switch ($Backend) {
    "d3d11" { "Rendering initialized with Direct3D11" }
    "d3d12" { "Rendering initialized with Direct3D12" }
    "vulkan" { "Rendering initialized with Vulkan" }
}
if ($validationText -notmatch $backendPattern) {
    throw "The requested $Backend renderer was not initialized."
}
if ($validationText -notmatch "Smoke frame limit reached after $SmokeFrames frame\(s\)\.") {
    throw "The Editor did not submit the requested number of frames."
}
if ($validationText -match "BGFX FATAL|Unhandled exception|Teardown failure|Microsoft Basic Render Driver|SwiftShader|llvmpipe|\bWARP\b|unknown stable type|\[Warn\]\s+ASSET-REFERENCE") {
    throw "Rendering2D $Backend validation reported an import, backend, reference, or teardown failure."
}

Invoke-Checked "Rendering2D generated script build" {
    dotnet build (Join-Path $projectRoot "Inno.EditorScripts.csproj") `
        -p:NuGetAudit=false `
        --disable-build-servers `
        -m:1 `
        -warnaserror
}

Write-Host "Rendering2D Windows GPU validation passed on $architecture/$Backend. Log: $validationLog"
Write-Host "Disposable validation project retained for inspection: $projectRoot"
