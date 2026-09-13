# InnoEngine.Rendering2D

A source-based 2D rendering plugin for [InnoEngine](https://github.com/FLwolfy/InnoEngine).

`InnoEngine.Rendering2D` adds an orthographic 2D rendering model without coupling 2D concepts to the engine's rendering core. The plugin is authored entirely against InnoEngine's public scripting APIs and can run directly from this repository or be exported as a self-contained `.iplugin` package.

> [!IMPORTANT]
> InnoEngine and this plugin are works in progress. APIs and serialized formats may change without backward compatibility. Use matching revisions of both repositories.

## Features

- Orthographic and pixel-perfect cameras, layer culling, and deterministic Base/Overlay camera stacks
- Textured, atlas-backed, nine-sliced, tiled, and procedural sprites
- Deterministically composed PNG/TGA sprite atlases and timed animation clips with stable region/event IDs
- Sparse layered tilemaps with dirty chunk revisions, persistent static instance buffers, compact Undo, and tools
- GPU-rasterized global, point, spot, and freeform 2D lights with cookies, four blend styles, layer masks,
  normal/emission maps, and D24S8 hard/soft shadow volumes
- Sorting groups and D24S8 stencil-backed sprite masks with inside/outside interactions
- HDR intermediate rendering with exposure, contrast, saturation, tone mapping, configurable multi-level Bloom,
  vignette, and pixelation
- Deterministic CPU particles with fixed-seed simulation and instanced rendering
- Stable sorting layers, shared-quad instancing, frustum culling, and bounded frame generation
- Straight alpha, premultiplied alpha, additive, multiply, and opaque material roles
- CPU picking against the same immutable frame used by the Scene viewport
- Scene and Game viewport integration, including pan, cursor-anchored zoom, framing, grid, and axes
- Unified Asset Browser documents for atlases, animations, tile sets, tilemaps, post-processing, and particles
- Deterministic MaxRects atlas composition with trim, rotation, extrusion, and named multi-page texture artifacts
- Plugin-owned project settings and native asset importers
- Opt-in zero-allocation/P95 scale gates for 100,000 visible tiles, a million-cell sparse domain, and 32 lights
- Native Metal, D3D11, D3D12, and Vulkan acceptance scripts plus a self-hosted GPU CI matrix

Physics, navigation, audio, gameplay UI, skeletal animation, SpriteShape, and Aseprite/PSD importing remain
separate product lines. Text is deliberately split into future `Inno.Text` and `Inno.Text.Rendering2D` plugins;
this repository does not embed FreeType, HarfBuzz, MSDF caches, font assets, or shaping policy. See the
[implementation status](Assets/Documentation/IMPLEMENTATION_STATUS.md) for the exact completed and pending milestones.

## Requirements

- A current checkout or build of [InnoEngine](https://github.com/FLwolfy/InnoEngine)
- The .NET 9 SDK used by the current InnoEngine toolchain
- Any platform support packs and native dependencies required by InnoEngine

## Run the development project

The simplest source workflow is to clone the engine and this repository side by side:

```text
GameEngineDev/
├── InnoEngine/
└── InnoEngine.Rendering2D/
```

From this repository, launch the Inno Editor with the repository root as the project directory:

```bash
dotnet run --project ../InnoEngine/src/composition/editor/host/Inno.Editor.Application -- .
```

The editor imports the authored content under `Assets/` and regenerates `Library/`, IDE project files, logs, and other local state. Open `Assets/~Samples/SampleScene.iscene` for a working example in Edit or Play Mode. The same launch command accepts a project path with a trailing directory separator.

The pipeline publishes diagnostics through `InnoEngine.Diagnostics.Diagnostic` and `DiagnosticSeverity`, using the reporter supplied by `RenderPipelineContext`. Scene input comes from the current identity-backed content scope; the plugin does not require an engine-owned 2D scene bridge or a native backend API.

## GPU and performance acceptance

On macOS arm64, run the complete Metal shader, GPU graph, scale, and allocation gate with:

```bash
DOTNET_COMMAND=/Users/aaronliao/.dotnet/dotnet \
  ./Tools/Validate-Rendering2D.sh ../InnoEngine 600
```

On a Windows x64 machine with a physical GPU and the requested driver installed, run one backend at a time:

```powershell
./Tools/Validate-Rendering2D.ps1 `
  -EngineRoot ../InnoEngine `
  -Backend d3d12 `
  -SmokeFrames 600 `
  -PrepareEngine
```

Valid Windows backend values are `d3d11`, `d3d12`, and `vulkan`. The script compiles both shared shader stages
for the exact backend profile, rebuilds a deterministic 32-light/soft-shadow/five-level-Bloom scene, runs the
native Editor, enforces zero allocations and P95 <= 5 ms over stable extraction and request submission, rejects
known software renderers and post-warmup render-target creation, and treats any missing acceptance marker as
failure. The checked-in GPU workflow intentionally uses
self-hosted runners carrying the `gpu` label; a generic hosted VM is not accepted as hardware evidence.

## Use the plugin in a scene

Every scene that opts into 2D rendering must contain exactly one `Rendering2DSceneSystem`. Add a `Camera2D`, then add `SpriteRenderer2D` or `TilemapRenderer2D` components to scene objects.

```csharp
using Inno.Rendering2D;
using InnoEngine.Mathematics;
using InnoEngine.Scene;

scene.AddSystem<Rendering2DSceneSystem>();

GameObject cameraObject = scene.CreateObject("Camera");
Camera2D camera = cameraObject.AddComponent<Camera2D>();
camera.pixelPerfect = true;

GameObject spriteObject = scene.CreateObject("Sprite");
SpriteRenderer2D sprite = spriteObject.AddComponent<SpriteRenderer2D>();
sprite.primitive = SpritePrimitive2D.Circle;
sprite.color = new Color(0.2f, 0.65f, 1f, 1f);
sprite.size = new Vector2(2f, 2f);
```

Scenes without `Rendering2DSceneSystem` are skipped by this plugin, allowing 2D, 3D, and mixed scenes to coexist in the same project.

## Package and install

To produce an installable package, open this repository in the Inno Editor and choose **File → Export as Plugin...**. The package is written to:

```text
Builds/rendering2d.iplugin
```

`Settings.Project.inno` explicitly owns the stable Project/Plugin ID `rendering2d`; it is not inferred again from the checkout directory name. The equivalent headless export uses the same engine build pipeline:

```bash
dotnet run --project ../InnoEngine/build/pipeline/Inno.Build.Cli -- plugin --project . --output Builds/rendering2d.iplugin --display-name InnoEngine.Rendering2D
```

Install it in another Inno project by copying the complete package to that project's `Plugins/` directory:

```text
MyGame/
├── Assets/
└── Plugins/
    └── rendering2d.iplugin
```

Installed plugin mounts are read-only. To modify the plugin, edit this authoring project and export a new package. Content under a `~`-prefixed directory is distributed as an optional sample. Importing a sample preserves its `~` directory name and makes it available in the consuming project's Editor/Play Mode, but no `~` subtree enters a Player build. For a Player, author a scene outside `~` directories and select it in Build Settings. This plugin authoring project therefore leaves its Player startup scene unassigned.

## Project layout

```text
Assets/
├── Documentation/   Detailed design and authoring notes
├── Editor/          Importers, settings UI, and viewport integration
├── Materials/       Default sprite material
├── Pipelines/       Default 2D render pipeline asset
├── Runtime/
│   ├── Assets/      Atlas, animation, tile set, and tilemap asset types
│   ├── Components/  Camera, light, sprite, animator, and tilemap components
│   ├── Pipeline/    Render request and pipeline integration
│   ├── Runtime/     Immutable frame extraction and batching
│   └── Systems/     Per-scene 2D extraction system
├── Shaders/         Sprite shader contract and BGFX shader sources
└── ~Samples/        Optional sample content
```

For the full feature contract, authoring APIs, camera composition rules, custom materials, and viewport behavior, see the [plugin documentation](Assets/Documentation/README.md).

## Version-control notes

`Assets/` and every `.imeta` sidecar are authored source and must remain under version control. `Settings.Project.inno` and `Settings.Build.inno`, when present, are also project-level source of truth. Generated caches, exported builds, IDE projections, logs, and per-user editor preferences are excluded by `.gitignore`.

## License

This project is available under the [MIT License](LICENSE).
