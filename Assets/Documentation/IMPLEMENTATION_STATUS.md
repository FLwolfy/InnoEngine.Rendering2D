# Rendering 2D implementation status

This file is the acceptance boundary for the staged 2D product work. A checked item means the source,
generated scripting surface, and its relevant local verification are present; it does not imply completion of
later phases.

## Delivered foundation

- Generic named texture-artifact protocol in Rendering Core, including stable asset/slot references, target
  compilation, last-good behavior, device-generation resolution, and Editor previews.
- Reload-safe headless Editor document service with single-source ownership, dirty state,
  Save/Save All, Apply/Revert, close confirmation, and stable state restoration.
- Reload-safe viewport-tool protocol with pointer capture, shortcuts, cursor/overlay hooks, coordinate
  conversion, and one history transaction per gesture.
- Deterministic Panel open/close/focus operations and comfortable/compact neutral dark Editor density tokens.
- Stable sprite references and a complete PNG/TGA atlas composition path: multi-source/multi-page deterministic
  MaxRects placement, transparent trim scanning, rotated pixel copies, edge extrusion, pivot, nine-slice borders,
  outline metadata, stable region IDs, and independently named page artifacts.
- Shared quad plus BGFX instance streams while preserving painter order and merging only adjacent compatible
  state. Generation-scoped pipeline state owns immutable layouts and shared-buffer descriptors, and every
  request source reuses a stable pipeline selection so the runtime does not create a generation per frame.
  The Metal shader uses the required `i_data0` through `i_data4` contract.
- Structure-revision scene indexing, camera-frustum rejection before tile expansion, sparse 32 by 32 default
  tile chunks, dirty chunk revisions, persistent generation-scoped GPU instance buffers for static chunks,
  ordinary/animated/rule tiles, layers, transforms, tint, and open metadata.
- Tile Palette editing operations for brush, erase, pick, box, bounded fill, select, overlap-safe move, and
  reusable stamps. A gesture is coalesced into one compact before/after history payload and one Undo transaction.
- Sprite frame animation with variable durations, events, playback/scrubbing/onion-skin document previews, and
  blend-safe runtime cross-fade; camera stack/reference resolution/viewport/post-process data, sorting groups,
  shadow-caster data, deterministic particles, and post-process profile data.
- `SpriteMask2D` extraction, bounded mask membership, D24S8 stencil-union writes, and `VisibleInside` /
  `VisibleOutside` sprite tests without exposing mask semantics to Rendering Core.
- A screen-space GPU lighting pipeline with four independently accumulated blend styles per Game Layer.
  Global, point, spot, and freeform lights rasterize analytical or polygonal volumes into HDR color and
  direction buffers; lit sprites consume those buffers with normal maps, emission maps, cookies, and explicit
  layer/style masks. D24S8 stencil shadow volumes and four deterministic jitter samples provide hard and soft
  shadows without moving any 2D semantic into Rendering Core.
- An HDR intermediate and final composite path for exposure, contrast, saturation, ACES-style tone mapping,
  vignette, and pixelation, plus a one-to-eight-level downsample/upsample Bloom pyramid with first-level
  thresholding and configurable scatter. RGBA8 is selected with a diagnostic when RGBA16F sampling or
  attachment support is unavailable.
- Asset Browser document entry points for atlas, animation, tile-set, tilemap, post-process, and particle
  sources. Atlas documents display generation-scoped texture previews.
- A deterministic GPU acceptance sample builder that supplies one lit receiver, 32 visible dynamic-capable
  lights, a soft-shadow light/caster pair, HDR emission, and a five-level Bloom profile. GPU acceptance refuses
  to pass unless the active graph submits HDR light buffers, MRT direction output, D24S8 shadow volumes, and
  the complete Bloom configuration.
- A zero-managed-allocation stable frame gate with 32 warmups and 240 measured samples. It covers Scene,
  runtime-camera, camera-stack snapshots, and the complete request-provider extraction/submission path while
  enforcing P95 <= 5 ms. The opt-in scale fixture additionally
  proves exactly 100,000 visible tiles in a 1,000,000-cell sparse domain with 32 visible lights under the same
  allocation and latency budget.
- BGFX transient textures, buffers, and exact-attachment framebuffers are pooled across isomorphic graph
  generations. The GPU acceptance gate arms after 120 rendered frames and fails if any render target is created
  afterward. The Editor Game View gate first forces 96 consecutive target-size changes to cover dock/window resize
  churn; the Metal acceptance run for this revision completed all 96 rebuilds and observed zero steady-state
  target rebuilds.
- Checked-in Metal and Windows GPU validation entry points. They compile the shared shader sources for the
  exact target profile, run 600 native Editor frames, reject software renderers/fatal diagnostics/tombstone
  warnings, verify all GPU and allocation markers, and rebuild generated scripts with warnings as errors.
- Reproducible GameScripts and EditorScripts builds, Core Editor interaction tests, Rendering runtime tests,
  and atomic `.iplugin` export.

## Partially delivered

- Atlas documents support whole-source, grid, and manual rectangle slicing, pivot/border editing, automatic
  alpha-hull outlines or explicit rectangle outlines, packing diagnostics, and page previews. Direct manipulation
  on a zoomable canvas, JPEG/HDR source decoding, and drag-based resize handles remain pending.
- Tilemaps cull sparse chunks before expansion and reuse persistent GPU buffers when a chunk revision is stable.
  The current tools run in the unified tilemap document; direct Scene viewport painting, a graphical palette,
  brush previews, and visual rule-tile authoring remain pending. Dirty revisions rebuild affected snapshots;
  unchanged frames use the zero-allocation cached extraction path enforced by the performance gate.
- Particles have deterministic simulation and instanced rendering. The full curve/gradient/flipbook authoring
  canvas and benchmark coverage remain pending.
- The screen-space light list is currently emitted as capability-neutral raster work. A compute-tiled accelerator
  may be added later, but it must preserve the already implemented raster semantics and output.
- Camera post-process profiles and document editing feed the render graph. Richer color grading and interactive
  overdraw/light-buffer visualization remain pending.

## Not yet delivered

- Text is intentionally deferred to independent plugins. `Inno.Text` will own Font/Family assets, FreeType,
  HarfBuzz shaping, fallback, asynchronous MSDF pages, rich text, and CJK coverage; a small
  `Inno.Text.Rendering2D` integration plugin will own `TextRenderer2D` while depending on both product lines.
  Rendering Core and this plugin will not absorb those font-domain concepts.
- Full Unity-class Hierarchy/Inspector/Asset Browser interaction set, command palette, global search, notification
  center, layout presets, and the specialized animation/tile/atlas canvases.
- Fixed-seed screenshot image-difference baselines. Structural GPU, scale, allocation, and backend acceptance
  gates are present; Windows D3D11/D3D12/Vulkan still require an attached self-hosted x64 GPU runner to produce
  hardware evidence for a given revision.
- Skeletal animation, SpriteShape, Aseprite, and PSD extension plugins.

These pending items require further implementation and must not be represented as complete until their tests
and platform acceptance gates pass.
