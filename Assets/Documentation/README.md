# Inno Rendering 2D

`Inno.Rendering.2D` is a source Plugin implemented exclusively through public Inno APIs. It owns every 2D
concept—camera, sprites, atlas data, animation, tilemaps, lights, batching, shader contracts, project settings,
and Editor viewport integration. The engine Rendering Core remains unaware of 2D.

## Included runtime features

### Shader authoring and coverage

Create a **2D / Sprite** Shader template for an ordinary explicit-stage graph. Its five blend roles share
the same Vertex and Fragment computations; lighting, shadow-volume preparation, Bloom and final composition are
separate Pipeline-owned Shader assets. Materials only reference a Shader and override its exposed parameters.

The Sprite implementation is composed from three ordinary Shader graphs: `SpriteVertex.ishader`,
`SpriteTexture.ishader`, and `SpriteSurface.ishader`. Each declares a reusable typed Function interface and its
complete internal graph. `DefaultSprite.ishader` connects those nodes to the standard Vertex Output and Fragment
Output and owns the five passes and Sprite contract directly. There is no Sprite-specific node compiler, hidden
stage builder, Domain Output, or `ShaderTarget`; editing a node graph changes the automatically discovered
Create / Graph Nodes entry, ports, dependencies, and inlined computation through the same public engine protocol
available to any plugin.

`DefaultSprite.ishader` also uses the generic automatic Stage bridge: outputs from the graph-authored
`Sprite Vertex` node connect directly to the Fragment-side `Sprite Texture` and `Sprite Surface` nodes. The
compiler derives and shares the required Vertex-to-Fragment varyings; the authored graph does not contain
duplicate `v_*` plumbing nodes. Any plugin can build the same pattern, or use **Collapse to Subgraph** on a
same-Stage selection to create an input-only, output-only, or multi-port reusable `.ishader` node.

Shader sources are grouped by ownership rather than kept in one flat directory:

```text
Shaders/
├── Sprite/
│   ├── DefaultSprite.ishader
│   ├── Nodes/       Reusable graph-authored Sprite nodes
│   └── Sources/     Private Sprite stage/function sources
└── Pipeline/
    ├── Lighting/
    ├── Shadows/
    └── PostProcessing/
        └── Bloom/
```

The Sprite Vertex node's optional **Vertex Offset** is world-space XYZ. The default graph still connects an
explicit `float3(0)` so its data flow remains visible. Connected computations are lowered into the vertex stage; fragment-only Sprite texture sampling
cannot feed vertex positions. Set `SpriteRenderer2D.boundsPadding` to the maximum XY displacement in world units
so CPU culling conservatively includes deformed vertices. This is a culling bound, not a geometry scale.

`SpriteMask2D.material` can reference the visible Sprite's Material to share vertex deformation and alpha
coverage. Match its geometry, texture, transform and `boundsPadding` as well. Unassigned coverage inherits the
Pipeline's default Sprite Material; a missing required coverage resource makes the output explicitly unavailable,
never silently unmasked. An independently authored `ShadowCaster2D` remains an explicit shadow polygon, not an
automatically inferred silhouette of arbitrary Shader code.

Material Preview and the Shader Inspector's always-visible Preview are isolated Editor-only GPU swatches. Their
drafts do not modify Scene/Game or source assets. Use Save to publish through import and compilation, or Revert to
discard draft edits.

### Drawing and effects

- Orthographic and pixel-perfect `Camera2D`, including explicit Scene scopes, layer culling, priorities,
  deterministic Base/Overlay camera stacks, explicit offscreen requests, and automatic backbuffer requests.
- Direct-texture and trim-aware atlas sprites with pivot, flip, linear or point sampling, clamp or repeat,
  simple, nine-sliced, and tiled geometry.
- Texture-free procedural square, circle, triangle, and capsule sprites. The pipeline owns a one-pixel white
  GPU texture for these shapes, so a project `TestTexture` or other placeholder asset is never required.
- Stable sorting layers, per-layer order, transform depth, bounded frame generation, adjacent state batching,
  shared-quad instancing, and frustum rejection.
- Straight alpha, premultiplied alpha, additive, multiply, and opaque material roles under the open
  `inno.rendering.2d.sprite` shader contract.
- Sorting groups and `SpriteMask2D` inside/outside interactions implemented through a D24S8 stencil attachment.
- A GPU-rasterized screen-space 2D light path with global, point, spot, and freeform volumes, four independent
  blend styles per Game Layer, cookies, normal/emission maps, and D24S8 hard/soft shadow volumes.
- An HDR intermediate and final composite path with exposure, contrast, saturation, tone mapping, a configurable
  one-to-eight-level downsample/upsample Bloom pyramid, vignette, pixelation, and a capability-aware RGBA8 fallback.
- Sparse layered tilemaps, atlas-backed tile sets, cell transforms and tint, and open collision/gameplay
  metadata that the renderer does not interpret.
- Timed atlas animation with looping, speed control, stable frame event IDs, pause, resume, and stop.
- Deterministic fixed-seed CPU particles with point, box, circle, and cone emitters, local/world simulation,
  curves, gradients, flipbooks, gravity, and procedural noise.
- Opt-in GPU 2D lighting with Game Layer and four-bit blend-style masks. Sprites and tilemaps remain unlit by
  default, so adding an unrelated light cannot black out existing content.
- CPU picking against the exact immutable frame snapshot used for the Scene viewport.
- Unified `GameBehavior` enablement for cameras, lights, sprite renderers, tilemap renderers, and sprite
  animators. Their Inspector header checkbox, serialized state, Play copy, and hierarchy activity semantics
  are identical to project script behaviors.
- Plugin-owned Project Settings on the dedicated `Project/Rendering/2D` page, plus unified reload-safe
  documents for atlas, animation, tile-set, tilemap, post-process, and particle assets.
- Scene View integration with Host-owned reload-safe neutral navigation, cursor-anchored zoom, pan, Frame
  Selection, adaptive world grid, and X/Y axes drawn behind scene content. Runtime pixel-perfect state is never
  copied into the independent Editor view.

Physics, navigation, audio, gameplay UI, skeletal animation, SpriteShape, and Aseprite/PSD import remain
independent work. The text product is deliberately split into `Inno.Text` for font/shaping/MSDF ownership and
`Inno.Text.Rendering2D` for the optional renderer integration; neither Rendering Core nor this plugin owns
FreeType/HarfBuzz/MSDF policy. Atlas import now composes PNG/TGA sources into
deterministic page artifacts; JPEG/HDR atlas decoding and the direct-manipulation slicing canvas remain pending.
The exact milestone boundary is tracked in [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).

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

A newly created `SpriteRenderer2D` stores an explicit reference to the Plugin-owned
`Materials/DefaultSprite.imaterial` asset (published as `Rendering2DIds.defaultSpriteMaterialPath`). The
Inspector therefore shows the Material that will actually render the object. Clearing the field to `None`
means that the renderer has no Material: the object is skipped with a diagnostic, and the Pipeline does not
silently substitute its configured default.

Assigning a valid atlas region or direct texture through `SpriteRenderer2D.sprite` takes precedence over
`primitive`. Set `primitive` to
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

Create `SpriteAtlas2DAsset`, assign PNG or TGA source textures, define stable source rectangles with `SetSlices`,
and save it through `Rendering2DAssets.SaveAtlas`. The importer decodes sources, scans transparent trim bounds,
performs deterministic multi-page MaxRects placement, copies rotated pixels with edge extrusion, writes portable
PNG page artifacts, and rebuilds runtime regions. Atlas UV rectangles use normalized top-left coordinates.
Region records retain stable IDs, source size, trim offset, pivot, nine-slice borders, outline, and packed rotation.
Every page is exposed as a named generic texture artifact and compiled independently for the active target.

Create `SpriteAnimation2DAsset`, fill every frame with a `SpriteReference2D`, call `SetClips`, and save it
through `Rendering2DAssets.SaveAnimation`. Add `SpriteAnimator2D` next to `SpriteRenderer2D`; animation changes
only stable sprite references and never keeps delegates in serialized state.

## Tilemap authoring

Create a `TileSet2DAsset`, assign ordinary, animated, or rule-driven `SpriteReference2D` visuals, and call
`SetTiles`. Create a `Tilemap2DAsset`, assign the tile set, then edit sparse cells with `SetCell`, `RemoveCell`,
`ApplyCells`, and `TryGetCell`. Batched writes rebuild each affected chunk once and advance its revision only
when content changes. Save them through
`Rendering2DAssets.SaveTileSet` and `Rendering2DAssets.SaveTilemap`. The runtime emits only visible cells and
enforces project-configured frame bounds. Static ordinary-tile chunks acquire persistent instance buffers keyed by
their stable map/owner/layer/chunk identity and exact content revision; animated and rule tiles stay transient.

## Asset creation and Inspector authoring

The File Browser **Create** submenu discovers this Plugin's `AssetCreationTemplate` subclasses and groups them
under **Rendering 2D / Sprites**, **World**, and **Effects**. It can create Sprite Atlases, Sprite Animations,
Tile Sets, Tilemaps, Particle Effects, Post Process Profiles, and a Render Pipeline already configured with
`Rendering2DIds.pipeline`. The File Browser contains no 2D asset-type switch; source extension, name, ordering,
and default content are supplied by the Plugin templates.

Selecting any of the six native 2D content files opens a dedicated Inspector draft with shared Save/Revert and
read-only Plugin-source behavior. Particle Effects expose sprite/flipbook, material, blending/sampling, emitter
shape and simulation space, capacity/emission/lifetime/speed, gravity/noise, size curve, and color gradient.
Post Process Profiles expose HDR, exposure, contrast, saturation, tone mapping, Bloom threshold/intensity/levels/
scatter, vignette, and pixelation. Atlases, animations, tile sets, and tilemaps retain their specialized authoring
tools through the same draft. Inspector selection and double-click document editing share one draft, so they do
not overwrite each other or publish edits before Save.

The complete asset audit intentionally separates authorable structured sources from imported products. Texture,
geometry, audio, and arbitrary text/binary assets are created by importing their source files; producing an empty
placeholder would not be a valid authoring workflow. Scene and Prefab creation remains owned by the Scene feature.

## Asset documents

Selecting or double-clicking a native 2D asset opens its dedicated Inspector backed by the headless Document Service.
Drafts are single-instance by persistent asset ID, retain neutral asset bytes and stable paths/IDs/tool/view values
across script reload, and share dirty, Save, Revert, Undo/Redo, and close behavior. Atlas drafts use the
generation-scoped preview service, so GPU handles never enter asset state or survive device recreation.

## Custom materials

The default shader contains one technique with contract `inno.rendering.2d.sprite` and five open roles:

- `inno.rendering.2d.alpha`
- `inno.rendering.2d.premultiplied`
- `inno.rendering.2d.additive`
- `inno.rendering.2d.multiply`
- `inno.rendering.2d.opaque`

A custom shader can implement any or all of these roles and expose additional properties. Assign its material
to a sprite or tilemap; the pipeline resolves the selected role through the normal Shader → Technique →
Material contract rather than through hard-coded backend programs. All source stages are shared `.ishadersource` files;
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

Viewport contributors and automatic runtime submission reuse stable `RenderPipelineAsset` selections. All
layouts, shared-quad descriptors, shader bindings, stencil states, and built-in resources are created lazily
inside the active pipeline generation, so script reload does not retain stale scripting objects and steady
frames do not rebuild pipeline generations.

## GPU validation

`Rendering2DViewportFrame.statistics` exposes immutable camera, batch, instance, lit-instance, light,
shadow-caster, and Bloom counters without exposing internal batch objects. Applications and external test
projects can use those counters for their own validation without adding benchmark fixtures or environment-variable
branches to the production Plugin.

`Tools/Validate-Rendering2D.sh` validates macOS arm64/Metal. `Tools/Validate-Rendering2D.ps1` validates Windows
x64/D3D11, D3D12, or Vulkan. Both build the matching Editor, run the ordinary authored project for a finite frame
count, reject import, backend, reference, fatal, and teardown failures, and rebuild generated Editor scripts with
warnings as errors. They never rebuild a hidden scene or mutate project assets. `.github/workflows/rendering2d-gpu.yml`
binds those commands to physical self-hosted GPU runners so a software adapter cannot be mistaken for backend
validation.

## Installation and authoring

This repository is the authoring workspace: develop and run the renderer as normal project-owned content under
`<Project>/Assets`, then use File → `Export as Plugin...` to produce an `.iplugin`. The exporter derives the
manifest and active dependency set from the current Project generation; no definition asset is created. Script
code uses `Assets.LocalPath` for source-local resources, so the same source resolves Project assets during
development and its own read-only Plugin mount after installation without hard-coding a Plugin source ID.
Consumers install only complete `.iplugin` files under `<Project>/Plugins`; unpacked folders and `.zip` files
are rejected. Plugin code runs with the same native process permissions as project scripts; the collectible
load context is not a security sandbox.

## Viewport participation and reload ownership

Scene View scope caching tracks both content identity and each scene's immutable System list. Adding,
removing, replacing, undoing or redoing a `Rendering2DSceneSystem` changes participation on the next
viewport evaluation, including scenes that previously had no rendering system. Stable scope reads reuse
the cached snapshot without managed allocation.

Pipeline settings caches resolve the asset through its original Identity owner and discard retired owners.
They do not attach collectible Plugin values to host-owned assets through a static `ConditionalWeakTable`.
The engine also retires cached Pipeline generations when their asset owner exits, so a stopped Play session
cannot retain Plugin assemblies during Reload Plugins. The full GC unload barrier remains mandatory.

An installed `.iplugin` is a source snapshot, not a live link to this development repository. After source
updates, export the Plugin again and replace the consumer project's installed package. Restart an Editor
that has already entered a terminal generation-retirement fault before testing the updated build.
