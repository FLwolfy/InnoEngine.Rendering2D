#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
    echo "Usage: $0 <InnoEngine-root> [smoke-frames]" >&2
    exit 2
fi

engine_root="$(cd "$1" && pwd)"
source_root="$(cd "$(dirname "$0")/.." && pwd)"
smoke_frames="${2:-3000}"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
editor_project="$engine_root/src/composition/editor/host/Inno.Editor.Application/Inno.Editor.Application.csproj"
editor_assembly="$engine_root/src/composition/editor/host/Inno.Editor.Application/bin/Debug/net9.0/Inno.Editor.Application.dll"
validation_log="$source_root/Logs/Rendering2D-metal-GpuValidation.log"

if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "arm64" ]]; then
    echo "Metal GPU validation requires a macOS arm64 host." >&2
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

mkdir -p "$source_root/Logs"
"$dotnet_command" "$editor_assembly" \
    "$project_root" \
    --graphics-api metal \
    --smoke-frames "$smoke_frames" 2>&1 | tee "$validation_log"

grep -Eq "Rendering initialized with Metal" "$validation_log"
grep -Eq "Smoke frame limit reached after ${smoke_frames} frame\(s\)\." "$validation_log"

if grep -Eq "BGFX FATAL|Unhandled exception|Teardown failure|unknown stable type|Asset import for .* failed:|\[Warn\][[:space:]]+ASSET-REFERENCE" "$validation_log"; then
    echo "Metal GPU validation log contains an import, fatal, teardown, or tombstone-reference failure." >&2
    exit 1
fi

"$dotnet_command" build "$project_root/Inno.EditorScripts.csproj" \
    -p:NuGetAudit=false \
    --disable-build-servers \
    -m:1 \
    -warnaserror

echo "Rendering2D Metal GPU validation passed. Log: $validation_log"
