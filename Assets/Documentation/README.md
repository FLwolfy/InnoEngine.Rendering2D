# Inno Rendering 2D

`Inno.Rendering.2D` is a source Plugin implemented exclusively through public Inno APIs. It owns every 2D
concept—camera, sprites, atlas data, animation, tilemaps, lights, batching, shader contracts, project settings,
and Editor viewport integration. The engine Rendering Core remains unaware of 2D.

## Included runtime features

- Orthographic and pixel-perfect `Camera2D`, including explicit Scene scopes, layer culling, priorities,
  deterministic Base/Overlay camera stacks, explicit offscreen requests, and automatic backbuffer requests.
- Direct-texture and trim-aware atlas sprites with pivot, flip, linear or point sampling, clamp or repeat,
  simple, nine-sliced, and tiled geometry.
- Texture-free procedural square, circle, triangle, and capsule sprites. The pipeline owns a one-pixel white
  GPU texture for these shapes, so a project `TestTexture` or other placeholder asset is never required.
- Stable sorting layers, per-layer order, transform depth, bounded frame generation, adjacent batching, and
  frustum rejection.
- Straight alpha, premultiplied alpha, additive, multiply, and opaque material roles under the open
  `inno.rendering.2d.sprite` shader contract.
- Sparse layered tilemaps, atlas-backed tile sets, cell transforms and tint, and open collision/gameplay
  metadata that the renderer does not interpret.
- Timed atlas animation with looping, speed control, stable frame event IDs, pause, resume, and stop.
- Opt-in, capability-independent global, point, and spot vertex lighting with Game Layer masks. Sprites and
  tilemaps remain unlit by default, so adding an unrelated light cannot black out existing content.
- CPU picking against the exact immutable frame snapshot used for the Scene viewport.
- Unified `GameBehavior` enablement for cameras, lights, sprite renderers, tilemap renderers, and sprite
  animators. Their Inspector header checkbox, serialized state, Play copy, and hierarchy activity semantics
  are identical to project script behaviors.
- Plugin-owned Project Settings on the dedicated `Project/Rendering/2D` page.
- Scene View integration with Host-owned reload-safe neutral navigation, cursor-anchored zoom, pan, Frame
  Selection, adaptive world grid, and X/Y axes drawn behind scene content. Runtime pixel-perfect state is never
  copied into the independent Editor view.

The implementation deliberately does not include physics, navigation, audio, gameplay UI, bitmap fonts,
particle authoring, or a texture-packing tool. Those are independent Plugins that can consume the public
sprite, atlas, material, and request APIs without changing this renderer or the engine.

## Sample distribution

Optional examples belong in a `~`-prefixed directory. In this development Project they are ordinary authored
content, so their assets and scripts import, compile, and run exactly like the rest of `Assets`. They are included
when the Project is exported as an `.iplugin`. After installation, the read-only Plugin mount displays those
directories as `ISAMPLE` and offers `Import Sample` from the context menu; installed sample content does not
compile or load until copied into the consuming Project. Import preserves the original `~`-prefixed root name;
the copied Project content then imports, compiles, and runs normally. No `~` subtree enters a Player build directly.

A scene opts into this Plugin by containing exactly one `Rendering2DSceneSystem`. Scenes without that system
are skipped instead of invalidating the viewport, so one host content scope can contain pure 3D scenes, pure
2D scenes, and scenes that opt into both models. The system is the scene-owned extraction index: it rebuilds
Camera, Drawable, and Light membership only after GameObject or Component structure changes, and every Camera
reads the same immutable index while observing current component values. Stable frames therefore do not scan
every GameObject once per Camera and do not allocate new extraction lists. Disabling, destroying, or
hot-unloading the system releases all retained Plugin component references before the collectible generation
retires. A disabled system remains the scene's installed 2D model, so the Scene viewport keeps its 2D authoring
grid and navigation while extraction and Game/Player output stop. Removing the system opts the scene out of the
2D model completely. More than one 2D system in the same scene remains a hard ownership error.

```csharp
using Inno.Rendering2D;
using InnoEngine.Mathematics;
using InnoEngine.Scene;

GameObject cameraObject = scene.CreateObject("Camera");
Camera2D camera = cameraObject.AddComponent<Camera2D>();
camera.pixelPerfect = true;

scene.AddSystem<Rendering2DSceneSystem>();

GameObject spriteObject = scene.CreateObject("Sprite");
SpriteRenderer2D sprite = spriteObject.AddComponent<SpriteRenderer2D>();
sprite.primitive = SpritePrimitive2D.Circle;
sprite.color = new Color(0.2f, 0.65f, 1f, 1f);
sprite.size = new Vector2(2f, 2f);
sprite.sortingLayer = "default";
```

Assigning a valid atlas region or direct `texture` takes precedence over `primitive`. Set `primitive` to
`None` when a missing texture should suppress rendering instead of using the default square. The procedural
shape path uses the same material contract, sorting, tint, lighting, batching, picking, and transform logic as
textured sprites.

Sprite density and the pixel-perfect camera grid both use the project-wide `Default Pixels Per Unit` value under
Project Settings → Rendering 2D; `Camera2D` does not own a second density value. A SpriteRenderer can still
override that default when its source artwork intentionally uses a different density.

Sorting Layer authoring accepts only a display name and order. Creation derives an immutable project-local key,
and components serialize that key in `sortingLayer`; the complete logical identity is resolved as
`projectId.name` through the current Project Identity. Renaming Project ID therefore does not rewrite sprites,
tilemaps, scenes, or Plugin contributions, and no full ID field is exposed in the Settings UI.

One deterministic camera stack whose cameras enable `renderToBackbuffer` is submitted automatically. For an offscreen target, call
`Rendering2DRenderer.CreateRequest` with an explicit `Rendering2DSceneScope`, or use
`CreateCameraStackRequest` to composite the selected Base plus every matching Overlay camera. A target has one
deterministically selected Base stack: duplicate primary/base cameras, overlays without a base, and overlays
whose `stackId` does not match the selected base produce clear diagnostics instead of relying on loaded-scene
order. Set `Rendering2DViewportOptions.backbufferOnly` when a host-created stack must apply the same
backbuffer eligibility rule as automatic submission.

```csharp
var scope = new Rendering2DSceneScope(scenesToRender);
var viewport = new RenderViewport(0, 0, width, height);
RenderRequest request = Rendering2DRenderer.CreateCameraStackRequest(
    scope,
    RenderTarget.backbuffer,
    viewport);
context.requests.Submit(request);
```

## Atlas and animation authoring

Create `SpriteAtlas2DAsset`, call `SetRegions`, and save it through `Rendering2DAssets.SaveAtlas`. Atlas UV
rectangles use normalized top-left coordinates. Region records retain source size, trim offset, pivot,
nine-slice borders, and packed rotation, so external atlas packers can import without losing layout semantics.

Create `SpriteAnimation2DAsset`, assign its atlas, call `SetClips`, and save it through
`Rendering2DAssets.SaveAnimation`. Add `SpriteAnimator2D` next to `SpriteRenderer2D`; animation changes only
stable region IDs and never keeps delegates in serialized state.

## Tilemap authoring

Create a `TileSet2DAsset`, assign an atlas, and call `SetTiles`. Create a `Tilemap2DAsset`, assign the tile set,
then edit sparse cells with `SetCell`, `RemoveCell`, and `TryGetCell`. Save them through
`Rendering2DAssets.SaveTileSet` and `Rendering2DAssets.SaveTilemap`. The runtime emits only visible cells and
enforces project-configured frame bounds.

## Custom materials

The default shader contains one technique with contract `inno.rendering.2d.sprite` and five open roles:

- `inno.rendering.2d.alpha`
- `inno.rendering.2d.premultiplied`
- `inno.rendering.2d.additive`
- `inno.rendering.2d.multiply`
- `inno.rendering.2d.opaque`

A custom shader can implement any or all of these roles and expose additional properties. Assign its material
to a sprite or tilemap; the pipeline resolves the selected role through the normal Shader → Technique →
Material contract rather than through hard-coded backend programs. All source stages are shared `.sc` files;
BGFX shaderc selects Metal, Direct3D, or another supported target profile.

## Editor viewport behavior

Scene and Game background colors are Editor preferences under `Editor/Appearance/Viewports`; they are not
serialized into `Camera2D` or project runtime settings. The Scene host owns navigation state, while this Plugin
maps it to its isolated Editor-only `Camera2D`. It forces that camera's `pixelPerfect` off and never copies the
project-wide pixel density, so Scene navigation is independent while Game View preserves runtime pixel rules.
Use middle-mouse drag or Alt + left-mouse drag to pan, the mouse wheel to zoom around the cursor, and
`F` to frame the selected 2D object. Scene/Game contributors consume the Host's explicit ordered content scope;
the automatic runtime request provider also consumes the host-selected `RenderContentScope`. No 2D collector
or contributor scans `SceneManager.loadedScenes`. Both Editor contributors use composition order `1000` and
load the existing presentation color when an earlier model already rendered overlapping pixels. A future 3D
contributor can therefore render at a lower order while this Plugin supplies 2D overlay content in the same
Scene or Game viewport. The grid and world axes are emitted by this Plugin before ordinary sprites, so scene
content always has the later draw domain and can cover them naturally.

## Installation and authoring

This repository is the authoring workspace: develop and run the renderer as normal project-owned content under
`<Project>/Assets`, then use File → `Export as Plugin...` to produce an `.iplugin`. The exporter derives the
manifest and active dependency set from the current Project generation; no definition asset is created. Script
code uses `Assets.LocalPath` for source-local resources, so the same source resolves Project assets during
development and its own read-only Plugin mount after installation without hard-coding a Plugin source ID.
Consumers install only complete `.iplugin` files under `<Project>/Plugins`; unpacked folders and `.zip` files
are rejected. Plugin code runs with the same native process permissions as project scripts; the collectible
load context is not a security sandbox.
