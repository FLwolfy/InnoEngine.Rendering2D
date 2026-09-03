# InnoEngine.Rendering2D

A source-based 2D rendering plugin for [InnoEngine](https://github.com/FLwolfy/InnoEngine).

`InnoEngine.Rendering2D` adds an orthographic 2D rendering model without coupling 2D concepts to the engine's rendering core. The plugin is authored entirely against InnoEngine's public scripting APIs and can run directly from this repository or be exported as a self-contained `.iplugin` package.

> [!IMPORTANT]
> InnoEngine and this plugin are works in progress. APIs and serialized formats may change without backward compatibility. Use matching revisions of both repositories.

## Features

- Orthographic and pixel-perfect cameras, layer culling, and deterministic Base/Overlay camera stacks
- Textured, atlas-backed, nine-sliced, tiled, and procedural sprites
- Sprite atlases and timed animation clips with stable region and event IDs
- Sparse layered tilemaps with per-cell transforms, tint, and gameplay metadata
- Global, point, and spot 2D lights with opt-in lighting and layer masks
- Stable sorting layers, batching, frustum culling, and bounded frame generation
- Straight alpha, premultiplied alpha, additive, multiply, and opaque material roles
- CPU picking against the same immutable frame used by the Scene viewport
- Scene and Game viewport integration, including pan, cursor-anchored zoom, framing, grid, and axes
- Plugin-owned project settings and asset importers for the complete authoring workflow

Physics, navigation, audio, gameplay UI, bitmap fonts, particle authoring, and texture packing are intentionally outside this plugin's scope.

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
dotnet run --project ../InnoEngine/src/editor/Inno.Editor.Application -- .
```

The editor imports the authored content under `Assets/` and regenerates `Library/`, IDE project files, logs, and other local state. Open `Assets/~Samples/SampleRender2D.iscene` for a working example.

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

Install it in another Inno project by copying the complete package to that project's `Plugins/` directory:

```text
MyGame/
├── Assets/
└── Plugins/
    └── rendering2d.iplugin
```

Installed plugin mounts are read-only. To modify the plugin, edit this authoring project and export a new package. Content under a `~`-prefixed directory is distributed as an optional sample and does not enter a Player build until imported into the consuming project's `Assets/` directory.

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

`Assets/` and every `.imeta` sidecar are authored source and must remain under version control. `ProjectSettings.inno` and `BuildProfile.inno`, when present, are also project-level source of truth. Generated caches, exported builds, IDE projections, logs, and per-user editor preferences are excluded by `.gitignore`.

## License

This project is available under the [MIT License](LICENSE).
