#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
    echo "Usage: $0 <InnoEngine-root> [smoke-frames]" >&2
    exit 2
fi

engine_root="$(cd "$1" && pwd)"
source_root="$(cd "$(dirname "$0")/.." && pwd)"
# A clean clone compiles its Editor scripts asynchronously; a short uncapped run can
# consume thousands of frames before the first authoring generation becomes ready.
smoke_frames="${2:-60000}"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
editor_project="$engine_root/src/composition/editor/host/Inno.Editor.Application/Inno.Editor.Application.csproj"
editor_assembly="$engine_root/src/composition/editor/host/Inno.Editor.Application/bin/Debug/net9.0/Inno.Editor.Application.dll"
acceptance_log="$source_root/Logs/Rendering2D-metal-GpuAcceptance.log"

if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "arm64" ]]; then
    echo "Metal GPU acceptance requires a macOS arm64 host." >&2
    exit 1
fi
if [[ ! "$smoke_frames" =~ ^[0-9]+$ ]] || (( smoke_frames < 300 )); then
    echo "smoke-frames must be an integer greater than or equal to 300." >&2
    exit 2
fi

"$dotnet_command" restore "$editor_project" --disable-build-servers -p:NuGetAudit=false
"$dotnet_command" build "$editor_project" --no-restore --disable-build-servers -m:1 -nr:false

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT
project_root="$temporary_directory/Project"
mkdir -p "$project_root"
cp -R "$source_root/Assets" "$project_root/Assets"
cp "$source_root/Settings.Project.inno" "$project_root/Settings.Project.inno"
if [[ -d "$source_root/Plugins" ]]; then cp -R "$source_root/Plugins" "$project_root/Plugins"; fi
# The Editor activates project Target extensions before compiling graph artifacts. A standalone
# shaderc invocation cannot validate a Plugin's high-level graph or its authoring dependencies.

mkdir -p "$source_root/Logs"
INNO_RENDERING2D_REBUILD_GPU_ACCEPTANCE=1 \
INNO_RENDERING2D_RUN_GPU_ACCEPTANCE=1 \
INNO_RENDERING2D_RUN_PERFORMANCE_GATE=1 \
INNO_RENDERING2D_RUN_SCALE_GATE=1 \
INNO_RENDERING2D_RUN_GAME_VIEW_RESIZE_GATE=1 \
"$dotnet_command" "$editor_assembly" \
    "$project_root" \
    --graphics-api metal \
    --smoke-frames "$smoke_frames" 2>&1 | tee "$acceptance_log"

grep -Eq "Rendering initialized with Metal" "$acceptance_log"
grep -Eq "Rebuilt the Rendering2D GPU acceptance sample" "$acceptance_log"
grep -Eq "Rendering2D GPU acceptance frame prepared: .* lit instances, (3[2-9]|[4-9][0-9]|[1-9][0-9]{2,}) lights, .* shadow casters, 5-level Bloom" "$acceptance_log"
grep -Eq "Rendering2D GPU acceptance passed: (3[2-9]|[4-9][0-9]|[1-9][0-9]{2,}) lights, .* light draws, .* shadow-volume draws, HDR/MRT/D24S8, 5-level Bloom" "$acceptance_log"
grep -Eq "Rendering2D performance gate passed: 0 managed bytes" "$acceptance_log"
grep -Eq "Rendering2D runtime-camera performance gate passed: 0 managed bytes" "$acceptance_log"
grep -Eq "Rendering2D camera-stack performance gate passed: 0 managed bytes" "$acceptance_log"
grep -Eq "Rendering2D request-submission performance gate passed: 0 managed bytes" "$acceptance_log"
grep -Eq "Rendering2D scale gate passed: 100000 visible tiles in a 1000000-cell sparse domain, 32 lights, 0 managed bytes" "$acceptance_log"
grep -Eq "Rendering2D Editor Game View resize gate passed: 96 consecutive Metal render-target rebuilds completed" "$acceptance_log"
grep -Eq "Rendering2D steady-state GPU resource gate armed after 120 rendered frames" "$acceptance_log"
grep -Eq "Smoke frame limit reached after ${smoke_frames} frame\(s\)\." "$acceptance_log"

if grep -Eq "BGFX FATAL|Unhandled exception|Teardown failure|unknown stable type|\[Warn\][[:space:]]+ASSET-REFERENCE" "$acceptance_log"; then
    echo "Metal GPU acceptance log contains a fatal, teardown, or tombstone-reference failure." >&2
    exit 1
fi

if awk '
    /Rendering2D steady-state GPU resource gate armed after 120 rendered frames/ { armed = 1; next }
    armed && /BGFX Texture .*RT\[x\]/ { rebuilt = 1 }
    END { exit rebuilt ? 0 : 1 }
' "$acceptance_log"; then
    echo "Metal GPU acceptance rebuilt a render-target texture after the steady-state gate was armed." >&2
    exit 1
fi

"$dotnet_command" build "$project_root/Inno.EditorScripts.csproj" \
    -p:NuGetAudit=false \
    --disable-build-servers \
    -m:1 \
    -warnaserror

echo "Rendering2D Metal GPU acceptance passed. Log: $acceptance_log"
