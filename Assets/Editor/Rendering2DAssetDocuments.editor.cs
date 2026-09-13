using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using InnoEditor.Assets;
using InnoEditor.Core;
using InnoEditor.ImGui;
using InnoEditor.Interactions;
using InnoEditor.Rendering;
using InnoEngine.Assets;
using InnoEngine.Core;
using InnoEngine.Rendering;

namespace Inno.Rendering2D;

/// <summary>Registers the reload-safe unified document provider for native 2D assets.</summary>
[EditorModule("rendering2d.asset-documents", order: 400)]
public sealed class Rendering2DAssetDocumentModule(
    EditorInteractions interactions,
    IEditorPreviewService previews) : EditorModule
{
    private IDisposable? m_registration;

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_registration = interactions.documents.RegisterProvider(
            new Rendering2DAssetDocumentProvider(interactions.documents, previews, interactions.history));
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_registration?.Dispose();
        m_registration = null;
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        m_registration?.Dispose();
        m_registration = null;
    }
}

/// <summary>Opens supported 2D assets in the unified document host from the Asset Browser.</summary>
[AssetEditor(typeof(SpriteAtlas2DAsset))]
[AssetEditor(typeof(SpriteAnimation2DAsset))]
[AssetEditor(typeof(TileSet2DAsset))]
[AssetEditor(typeof(Tilemap2DAsset))]
[AssetEditor(typeof(PostProcessProfile2DAsset))]
[AssetEditor(typeof(ParticleEffect2DAsset))]
public sealed class Rendering2DDocumentAssetEditor : AssetEditor
{
    /// <inheritdoc />
    public override bool CanOpen(AssetEditorContext context)
        => context is not null && !context.isDirectory;

    /// <inheritdoc />
    public override void Open(AssetEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ = context.interactions.documents.Open(
            context.relativePath,
            context.info?.persistentId ?? Guid.Empty);
    }
}

internal sealed class Rendering2DAssetDocumentProvider(
    IEditorDocumentService documents,
    IEditorPreviewService previews,
    IEditorHistory history) : EditorDocumentProvider
{
    private static readonly string[] S_EXTENSIONS =
    [
        ".ispriteatlas2d",
        ".ispriteanimation2d",
        ".itileset2d",
        ".itilemap2d",
        ".ipostprocess2d",
        ".iparticle2d"
    ];

    private readonly Dictionary<Guid, Draft> m_drafts = [];

    public override string id => "inno.rendering2d.native-assets";

    public override bool CanOpen(string assetPath)
    {
        string extension = Path.GetExtension(assetPath);
        for (int index = 0; index < S_EXTENSIONS.Length; index++)
        {
            if (string.Equals(extension, S_EXTENSIONS[index], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public override void Open(EditorDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.title = Path.GetFileNameWithoutExtension(context.assetPath);
        m_drafts[context.documentId] = CreateDraft(context);
    }

    public override void Draw(EditorDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!m_drafts.TryGetValue(context.documentId, out Draft? draft))
            draft = m_drafts[context.documentId] = CreateDraft(context);

        ImGui.Text(context.assetPath);
        ImGui.Separator();
        if (draft.Draw(previews))
            documents.SetDirty(context.documentId);
    }

    public override bool Save(EditorDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!m_drafts.TryGetValue(context.documentId, out Draft? draft))
            return false;
        return draft.Save(AssetPath.Parse(context.assetPath));
    }

    public override bool Revert(EditorDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_drafts[context.documentId] = CreateDraft(context);
        return true;
    }

    public override void Close(EditorDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ = m_drafts.Remove(context.documentId);
    }

    private Draft CreateDraft(EditorDocumentContext context)
    {
        string extension = Path.GetExtension(context.assetPath);
        if (string.Equals(extension, ".ispriteatlas2d", StringComparison.OrdinalIgnoreCase))
            return new AtlasDraft(Load<SpriteAtlas2DAsset>(context));
        if (string.Equals(extension, ".ispriteanimation2d", StringComparison.OrdinalIgnoreCase))
            return new AnimationDraft(Load<SpriteAnimation2DAsset>(context));
        if (string.Equals(extension, ".itileset2d", StringComparison.OrdinalIgnoreCase))
            return new TileSetDraft(Load<TileSet2DAsset>(context));
        if (string.Equals(extension, ".itilemap2d", StringComparison.OrdinalIgnoreCase))
            return new TilemapDraft(Load<Tilemap2DAsset>(context), history, AssetPath.Parse(context.assetPath));
        if (string.Equals(extension, ".ipostprocess2d", StringComparison.OrdinalIgnoreCase))
            return new PostProcessDraft(Load<PostProcessProfile2DAsset>(context));
        if (string.Equals(extension, ".iparticle2d", StringComparison.OrdinalIgnoreCase))
            return new ParticleDraft(Load<ParticleEffect2DAsset>(context));
        throw new InvalidOperationException($"Unsupported 2D document source '{context.assetPath}'.");
    }

    private static TAsset Load<TAsset>(EditorDocumentContext context)
        where TAsset : AssetObject
    {
        if (context.assetId != Guid.Empty
            && Assets.TryLoad(context.assetId, out TAsset? identified) && identified is not null)
        {
            return identified;
        }
        if (Assets.TryLoad(AssetPath.Parse(context.assetPath), out TAsset? located) && located is not null)
        {
            return located;
        }
        throw new InvalidOperationException($"2D asset '{context.assetPath}' is not available.");
    }

    private abstract class Draft
    {
        internal abstract bool Draw(IEditorPreviewService previews);
        internal abstract bool Save(AssetPath path);
    }

    private sealed class AtlasDraft(SpriteAtlas2DAsset asset) : Draft
    {
        private SpriteAtlasPackingSettings2D m_packing = asset.packing;
        private readonly List<SpriteAtlasSlice2D> m_slices = asset.slices.Select(CloneSlice).ToList();
        private int m_sourceIndex;
        private int m_gridWidth = 32;
        private int m_gridHeight = 32;
        private int m_rectX;
        private int m_rectY;
        private int m_rectWidth = 32;
        private int m_rectHeight = 32;
        private float m_pivotX = 0.5f;
        private float m_pivotY = 0.5f;
        private string m_packingStatus = string.Empty;

        internal override bool Draw(IEditorPreviewService previews)
        {
            ImGui.SeparatorText("Packing");
            int maximumWidth = m_packing.maximumWidth;
            int maximumHeight = m_packing.maximumHeight;
            int padding = m_packing.padding;
            int extrude = m_packing.extrude;
            bool allowRotation = m_packing.allowRotation;
            bool trimTransparent = m_packing.trimTransparent;
            bool changed = ImGui.InputInt("Maximum Width", ref maximumWidth);
            changed |= ImGui.InputInt("Maximum Height", ref maximumHeight);
            changed |= ImGui.InputInt("Padding", ref padding);
            changed |= ImGui.InputInt("Extrude", ref extrude);
            changed |= ImGui.Checkbox("Allow Rotation", ref allowRotation);
            changed |= ImGui.Checkbox("Trim Transparent", ref trimTransparent);
            if (changed)
            {
                m_packing.maximumWidth = maximumWidth;
                m_packing.maximumHeight = maximumHeight;
                m_packing.padding = padding;
                m_packing.extrude = extrude;
                m_packing.allowRotation = allowRotation;
                m_packing.trimTransparent = trimTransparent;
            }

            ImGui.SeparatorText("Content");
            ImGui.Text($"Sources: {asset.sources.Length}   Slices: {m_slices.Count}   Pages: {asset.pages.Length}   Regions: {asset.regions.Length}");
            for (int sourceIndex = 0; sourceIndex < asset.sources.Length; sourceIndex++)
            {
                SpriteAtlasSource2D source = asset.sources[sourceIndex];
                string dimensions = source.texture is null ? "missing" : $"{source.texture.width} x {source.texture.height}";
                if (ImGui.Selectable($"Source {sourceIndex}: {source.name} ({dimensions})", m_sourceIndex == sourceIndex))
                    m_sourceIndex = sourceIndex;
            }

            ImGui.SeparatorText("Slicing");
            _ = ImGui.InputInt("Grid Width", ref m_gridWidth);
            _ = ImGui.InputInt("Grid Height", ref m_gridHeight);
            if (ImGui.Button("Slice Grid"))
                changed |= SliceGrid();
            ImGui.SameLine();
            if (ImGui.Button("Whole Source"))
                changed |= AddWholeSource();
            _ = ImGui.InputInt("Rect X", ref m_rectX);
            _ = ImGui.InputInt("Rect Y", ref m_rectY);
            _ = ImGui.InputInt("Rect Width", ref m_rectWidth);
            _ = ImGui.InputInt("Rect Height", ref m_rectHeight);
            _ = ImGui.SliderFloat("Pivot X", ref m_pivotX, 0f, 1f);
            _ = ImGui.SliderFloat("Pivot Y", ref m_pivotY, 0f, 1f);
            if (ImGui.Button("Add Manual Slice"))
                changed |= AddManualSlice();
            ImGui.SameLine();
            if (ImGui.Button("Packing Preview"))
                BuildPackingPreview();
            if (!string.IsNullOrWhiteSpace(m_packingStatus))
                ImGui.Text(m_packingStatus);

            ImGui.SeparatorText("Regions");
            for (int sliceIndex = 0; sliceIndex < m_slices.Count; sliceIndex++)
            {
                SpriteAtlasSlice2D slice = m_slices[sliceIndex];
                ImGui.PushId(sliceIndex + 20_000);
                string name = slice.name ?? string.Empty;
                int x = (int)slice.pixelRect.x;
                int y = (int)slice.pixelRect.y;
                int width = (int)slice.pixelRect.width;
                int height = (int)slice.pixelRect.height;
                float pivotX = slice.pivot.x;
                float pivotY = slice.pivot.y;
                float borderLeft = slice.borderPixels.X;
                float borderBottom = slice.borderPixels.Y;
                float borderRight = slice.borderPixels.Z;
                float borderTop = slice.borderPixels.W;
                bool sliceChanged = ImGui.InputText("Name", ref name, 256);
                sliceChanged |= ImGui.InputInt("X", ref x);
                sliceChanged |= ImGui.InputInt("Y", ref y);
                sliceChanged |= ImGui.InputInt("Width", ref width);
                sliceChanged |= ImGui.InputInt("Height", ref height);
                sliceChanged |= ImGui.SliderFloat("Pivot X", ref pivotX, 0f, 1f);
                sliceChanged |= ImGui.SliderFloat("Pivot Y", ref pivotY, 0f, 1f);
                sliceChanged |= ImGui.InputFloat("Border Left", ref borderLeft);
                sliceChanged |= ImGui.InputFloat("Border Bottom", ref borderBottom);
                sliceChanged |= ImGui.InputFloat("Border Right", ref borderRight);
                sliceChanged |= ImGui.InputFloat("Border Top", ref borderTop);
                if (sliceChanged)
                {
                    slice.name = name;
                    slice.pixelRect = new InnoEngine.Mathematics.Rect(x, y, width, height);
                    slice.pivot = new InnoEngine.Mathematics.Vector2(pivotX, pivotY);
                    slice.borderPixels = new System.Numerics.Vector4(
                        MathF.Max(0f, borderLeft),
                        MathF.Max(0f, borderBottom),
                        MathF.Max(0f, borderRight),
                        MathF.Max(0f, borderTop));
                    m_slices[sliceIndex] = slice;
                    changed = true;
                }
                ImGui.Text($"Outline: {slice.outline?.Length ?? 0} points");
                if (ImGui.Button("Rectangle Outline"))
                {
                    slice.outline =
                    [
                        InnoEngine.Mathematics.Vector2.ZERO,
                        new InnoEngine.Mathematics.Vector2(1f, 0f),
                        InnoEngine.Mathematics.Vector2.ONE,
                        new InnoEngine.Mathematics.Vector2(0f, 1f)
                    ];
                    m_slices[sliceIndex] = slice;
                    changed = true;
                }
                ImGui.SameLine();
                if (ImGui.Button("Automatic Outline"))
                {
                    slice.outline = [];
                    m_slices[sliceIndex] = slice;
                    changed = true;
                }
                if (ImGui.Button("Remove Slice"))
                {
                    m_slices.RemoveAt(sliceIndex--);
                    changed = true;
                }
                ImGui.PopId();
            }
            if (asset.pages.Length > 0)
            {
                SpriteAtlasPage2D page = asset.pages[0];
                ImGui.Text($"Page 0: {page.id} ({page.width} x {page.height})");
                if (previews.TryGetTextureArtifact(
                    asset.GetPageTexture(0),
                    page.width,
                    page.height,
                    out EditorPreviewHandle handle))
                {
                    float width = MathF.Min(480f, MathF.Max(64f, page.width));
                    float height = page.width > 0
                        ? width * page.height / page.width
                        : width;
                    previews.Draw(handle, new System.Numerics.Vector2(width, MathF.Min(360f, height)));
                }
            }
            return changed;
        }

        internal override bool Save(AssetPath path)
        {
            m_packing.maximumWidth = Math.Max(1, m_packing.maximumWidth);
            m_packing.maximumHeight = Math.Max(1, m_packing.maximumHeight);
            m_packing.padding = Math.Max(0, m_packing.padding);
            m_packing.extrude = Math.Max(0, m_packing.extrude);
            asset.packing = m_packing;
            asset.SetSlices(m_slices);
            return Rendering2DAssets.SaveAtlas(path, asset);
        }

        private bool SliceGrid()
        {
            if (!TryGetSelectedSource(out TextureAsset? texture) || texture is null)
                return false;
            int width = Math.Max(1, m_gridWidth);
            int height = Math.Max(1, m_gridHeight);
            bool added = false;
            for (int y = 0; y + height <= texture.height; y += height)
            {
                for (int x = 0; x + width <= texture.width; x += width)
                {
                    m_slices.Add(CreateSlice(
                        $"{GetSourceName()}_{x}_{y}",
                        new InnoEngine.Mathematics.Rect(x, y, width, height)));
                    added = true;
                }
            }
            return added;
        }

        private bool AddWholeSource()
        {
            if (!TryGetSelectedSource(out TextureAsset? texture) || texture is null)
                return false;
            m_slices.Add(CreateSlice(
                GetSourceName(),
                new InnoEngine.Mathematics.Rect(0f, 0f, texture.width, texture.height)));
            return true;
        }

        private bool AddManualSlice()
        {
            if (!TryGetSelectedSource(out TextureAsset? texture) || texture is null)
                return false;
            int width = Math.Max(1, m_rectWidth);
            int height = Math.Max(1, m_rectHeight);
            int x = Math.Max(0, m_rectX);
            int y = Math.Max(0, m_rectY);
            if (x + width > texture.width || y + height > texture.height)
            {
                m_packingStatus = "Manual slice is outside the selected source.";
                return false;
            }
            m_slices.Add(CreateSlice(
                $"{GetSourceName()}_{x}_{y}",
                new InnoEngine.Mathematics.Rect(x, y, width, height)));
            return true;
        }

        private SpriteAtlasSlice2D CreateSlice(string name, InnoEngine.Mathematics.Rect rect)
            => new()
            {
                id = SpriteRegionId.Create(),
                name = name,
                sourceIndex = m_sourceIndex,
                pixelRect = rect,
                pivot = new InnoEngine.Mathematics.Vector2(m_pivotX, m_pivotY),
                outline = []
            };

        private bool TryGetSelectedSource(out TextureAsset? texture)
        {
            if (asset.sources.Length == 0)
            {
                texture = null;
                m_packingStatus = "Assign at least one source texture in the Inspector.";
                return false;
            }
            m_sourceIndex = Math.Clamp(m_sourceIndex, 0, asset.sources.Length - 1);
            texture = asset.sources[m_sourceIndex].texture;
            if (texture is null)
                m_packingStatus = "The selected source texture is missing.";
            return texture is not null;
        }

        private string GetSourceName()
        {
            string value = asset.sources[m_sourceIndex].name ?? string.Empty;
            return string.IsNullOrWhiteSpace(value) ? $"Sprite {m_sourceIndex}" : value.Trim();
        }

        private void BuildPackingPreview()
        {
            try
            {
                SpriteAtlasPackResult2D result = SpriteAtlasMaxRectsPacker2D.Pack(
                    m_slices.Select(slice => new SpriteAtlasPackInput2D(
                        slice.id.value,
                        Math.Max(1, (int)slice.pixelRect.width),
                        Math.Max(1, (int)slice.pixelRect.height))),
                    m_packing);
                m_packingStatus = $"{result.placements.Count} regions fit in {result.pages.Count} deterministic page(s).";
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException)
            {
                m_packingStatus = exception.Message;
            }
        }

        private static SpriteAtlasSlice2D CloneSlice(SpriteAtlasSlice2D source)
        {
            source.outline = source.outline?.ToArray() ?? [];
            return source;
        }
    }

    private sealed class AnimationDraft(SpriteAnimation2DAsset asset) : Draft
    {
        private readonly SpriteAnimationClip2D[] m_clips = CloneClips(asset.clips);
        private int m_selectedClip;
        private float m_playhead;
        private bool m_playing;
        private bool m_onionSkin;

        internal override bool Draw(IEditorPreviewService previews)
        {
            ImGui.SeparatorText("Timeline");
            ImGui.Text($"Clips: {m_clips.Length}");
            bool changed = false;
            for (int clipIndex = 0; clipIndex < m_clips.Length; clipIndex++)
            {
                SpriteAnimationClip2D clip = m_clips[clipIndex];
                ImGui.PushId(clipIndex);
                if (ImGui.Selectable($"{clip.id}   Frames: {clip.frames?.Length ?? 0}", m_selectedClip == clipIndex))
                {
                    m_selectedClip = clipIndex;
                    m_playhead = 0f;
                }
                bool loop = clip.loop;
                if (ImGui.Checkbox("Loop", ref loop))
                {
                    clip.loop = loop;
                    m_clips[clipIndex] = clip;
                    changed = true;
                }
                ImGui.PopId();
            }
            if (m_clips.Length == 0)
                return changed;

            m_selectedClip = Math.Clamp(m_selectedClip, 0, m_clips.Length - 1);
            SpriteAnimationClip2D selected = m_clips[m_selectedClip];
            SpriteAnimationFrame2D[] frames = selected.frames ?? [];
            float duration = frames.Sum(static frame => MathF.Max(0.000001f, frame.duration));
            if (ImGui.Button(m_playing ? "Pause" : "Play"))
                m_playing = !m_playing;
            ImGui.SameLine();
            if (ImGui.Button("Stop"))
            {
                m_playing = false;
                m_playhead = 0f;
            }
            ImGui.SameLine();
            _ = ImGui.Checkbox("Onion Skin", ref m_onionSkin);
            if (m_playing && frames.Length > 0)
            {
                m_playhead += MathF.Max(0f, Time.deltaTime);
                if (m_playhead >= duration)
                {
                    if (selected.loop)
                        m_playhead %= duration;
                    else
                    {
                        m_playhead = duration;
                        m_playing = false;
                    }
                }
            }
            _ = ImGui.SliderFloat("Playhead", ref m_playhead, 0f, MathF.Max(0.000001f, duration));
            int activeFrame = GetFrameIndex(frames, m_playhead);
            if (activeFrame >= 0)
            {
                ImGui.Text($"Frame {activeFrame + 1}/{frames.Length}   Event: {frames[activeFrame].eventId ?? string.Empty}");
                if (m_onionSkin && frames.Length > 1)
                    DrawFramePreview(previews, frames[(activeFrame + frames.Length - 1) % frames.Length], "Previous");
                DrawFramePreview(previews, frames[activeFrame], "Current");
                if (m_onionSkin && frames.Length > 1)
                    DrawFramePreview(previews, frames[(activeFrame + 1) % frames.Length], "Next");
            }

            ImGui.SeparatorText("Frames");
            for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
            {
                SpriteAnimationFrame2D frame = frames[frameIndex];
                ImGui.PushId(frameIndex + 10_000);
                ImGui.Text($"Frame {frameIndex + 1}");
                float frameDuration = frame.duration;
                string eventId = frame.eventId ?? string.Empty;
                bool frameChanged = ImGui.InputFloat("Duration", ref frameDuration);
                frameChanged |= ImGui.InputText("Event", ref eventId, 256);
                if (frameChanged)
                {
                    frame.duration = MathF.Max(0.000001f, frameDuration);
                    frame.eventId = eventId;
                    frames[frameIndex] = frame;
                    selected.frames = frames;
                    m_clips[m_selectedClip] = selected;
                    changed = true;
                }
                ImGui.PopId();
            }
            return changed;
        }

        internal override bool Save(AssetPath path)
        {
            asset.clips = (SpriteAnimationClip2D[])m_clips.Clone();
            return Rendering2DAssets.SaveAnimation(path, asset);
        }

        private static SpriteAnimationClip2D[] CloneClips(SpriteAnimationClip2D[] source)
        {
            var result = new SpriteAnimationClip2D[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                result[index] = source[index];
                result[index].frames = (SpriteAnimationFrame2D[])(source[index].frames?.Clone()
                    ?? Array.Empty<SpriteAnimationFrame2D>());
            }
            return result;
        }

        private static int GetFrameIndex(SpriteAnimationFrame2D[] frames, float playhead)
        {
            if (frames.Length == 0)
                return -1;
            float remaining = MathF.Max(0f, playhead);
            for (int index = 0; index < frames.Length; index++)
            {
                remaining -= MathF.Max(0.000001f, frames[index].duration);
                if (remaining <= 0f)
                    return index;
            }
            return frames.Length - 1;
        }

        private static void DrawFramePreview(
            IEditorPreviewService previews,
            SpriteAnimationFrame2D frame,
            string label)
        {
            ImGui.Text(label);
            if (frame.sprite.texture is not null
                && previews.TryGetTexture(frame.sprite.texture, out EditorPreviewHandle texture))
            {
                previews.Draw(texture, new System.Numerics.Vector2(128f, 128f));
                return;
            }
            if (frame.sprite.atlas is SpriteAtlas2DAsset atlas
                && atlas.TryGetRegion(frame.sprite.regionId, out SpriteRegion2D region)
                && region.pageIndex >= 0
                && region.pageIndex < atlas.pages.Length
                && previews.TryGetTextureArtifact(
                    atlas.GetPageTexture(region.pageIndex),
                    atlas.pages[region.pageIndex].width,
                    atlas.pages[region.pageIndex].height,
                    out EditorPreviewHandle page))
            {
                previews.Draw(page, new System.Numerics.Vector2(128f, 128f));
            }
        }
    }

    private sealed class TileSetDraft(TileSet2DAsset asset) : Draft
    {
        private readonly TileDefinition2D[] m_tiles = (TileDefinition2D[])asset.tiles.Clone();

        internal override bool Draw(IEditorPreviewService previews)
        {
            ImGui.SeparatorText("Tiles");
            ImGui.Text($"Definitions: {m_tiles.Length}");
            bool changed = false;
            for (int tileIndex = 0; tileIndex < m_tiles.Length; tileIndex++)
            {
                TileDefinition2D tile = m_tiles[tileIndex];
                ImGui.PushId(tileIndex);
                ImGui.Text($"Tile {tile.id}   Animation: {tile.animation?.Length ?? 0}   Rules: {tile.rules?.Length ?? 0}");
                string metadata = tile.metadata ?? string.Empty;
                if (ImGui.InputText("Metadata", ref metadata, 2048))
                {
                    tile.metadata = metadata;
                    m_tiles[tileIndex] = tile;
                    changed = true;
                }
                ImGui.PopId();
            }
            return changed;
        }

        internal override bool Save(AssetPath path)
        {
            asset.tiles = (TileDefinition2D[])m_tiles.Clone();
            return Rendering2DAssets.SaveTileSet(path, asset);
        }
    }

    private sealed class TilemapDraft(
        Tilemap2DAsset asset,
        IEditorHistory history,
        AssetPath path) : Draft
    {
        private readonly TilemapLayer2D[] m_layers = (TilemapLayer2D[])asset.layers.Clone();
        private float m_cellWidth = asset.cellSize.x;
        private float m_cellHeight = asset.cellSize.y;
        private int m_chunkSize = asset.chunkSize;
        private int m_layerId = asset.layers.Length > 0 ? asset.layers[0].id : 0;
        private int m_tileId;
        private int m_x;
        private int m_y;
        private int m_width = 1;
        private int m_height = 1;
        private int m_moveX = 1;
        private int m_moveY;
        private int m_selectedCount;

        internal override bool Draw(IEditorPreviewService previews)
        {
            ImGui.SeparatorText("Grid");
            bool changed = ImGui.InputFloat("Cell Width", ref m_cellWidth);
            changed |= ImGui.InputFloat("Cell Height", ref m_cellHeight);
            changed |= ImGui.InputInt("Chunk Size", ref m_chunkSize);
            ImGui.Text($"Sparse chunks: {asset.chunks.Length}   Revision: {asset.revision}");

            ImGui.SeparatorText("Tile Palette");
            _ = ImGui.InputInt("Active Layer", ref m_layerId);
            _ = ImGui.InputInt("Tile ID", ref m_tileId);
            _ = ImGui.InputInt("Cell X", ref m_x);
            _ = ImGui.InputInt("Cell Y", ref m_y);
            _ = ImGui.InputInt("Width", ref m_width);
            _ = ImGui.InputInt("Height", ref m_height);
            TilemapSelection2D selection = GetSelection();
            TilemapCell2D paint = new()
            {
                tileId = m_tileId,
                color = InnoEngine.Mathematics.Color.WHITE
            };
            if (ImGui.Button("Brush"))
                changed |= Record("Paint Tile", TilemapEditing2D.Brush(asset, m_layerId, new TilemapPosition2D(m_x, m_y), paint));
            ImGui.SameLine();
            if (ImGui.Button("Erase"))
                changed |= Record("Erase Tile", TilemapEditing2D.Erase(asset, m_layerId, [new TilemapPosition2D(m_x, m_y)]));
            ImGui.SameLine();
            if (ImGui.Button("Pick") && asset.TryGetCell(m_x, m_y, m_layerId, out TilemapCell2D picked))
                m_tileId = picked.tileId;
            ImGui.SameLine();
            if (ImGui.Button("Box"))
                changed |= Record("Box Paint Tiles", TilemapEditing2D.Box(asset, m_layerId, selection, paint));
            ImGui.SameLine();
            if (ImGui.Button("Fill"))
            {
                changed |= Record(
                    "Fill Tiles",
                    TilemapEditing2D.Fill(
                        asset,
                        m_layerId,
                        new TilemapPosition2D(m_x, m_y),
                        selection,
                        paint));
            }
            if (ImGui.Button("Select"))
                m_selectedCount = TilemapEditing2D.Select(asset, m_layerId, selection).Count;
            ImGui.SameLine();
            _ = ImGui.InputInt("Move X", ref m_moveX);
            _ = ImGui.InputInt("Move Y", ref m_moveY);
            if (ImGui.Button("Move Selection"))
                changed |= Record("Move Tiles", TilemapEditing2D.Move(asset, m_layerId, selection, m_moveX, m_moveY));
            ImGui.SameLine();
            if (ImGui.Button("Stamp Selection"))
            {
                IReadOnlyList<TilemapCell2D> selected = TilemapEditing2D.Select(asset, m_layerId, selection);
                TilemapStampCell2D[] stamp = selected
                    .Select(cell => new TilemapStampCell2D(
                        cell.x - selection.minimumX,
                        cell.y - selection.minimumY,
                        cell))
                    .ToArray();
                changed |= Record(
                    "Stamp Tiles",
                    TilemapEditing2D.Stamp(
                        asset,
                        m_layerId,
                        new TilemapPosition2D(selection.minimumX + m_moveX, selection.minimumY + m_moveY),
                        stamp));
            }
            ImGui.Text($"Selected occupied cells: {m_selectedCount}");

            ImGui.SeparatorText("Layers");
            for (int layerIndex = 0; layerIndex < m_layers.Length; layerIndex++)
            {
                TilemapLayer2D layer = m_layers[layerIndex];
                ImGui.PushId(layerIndex);
                string name = layer.name ?? string.Empty;
                bool visible = layer.visible;
                int order = layer.order;
                bool layerChanged = ImGui.InputText("Name", ref name, 256);
                layerChanged |= ImGui.Checkbox("Visible", ref visible);
                layerChanged |= ImGui.InputInt("Order", ref order);
                if (layerChanged)
                {
                    layer.name = name;
                    layer.visible = visible;
                    layer.order = order;
                    m_layers[layerIndex] = layer;
                    changed = true;
                }
                ImGui.PopId();
            }
            return changed;
        }

        private TilemapSelection2D GetSelection()
            => new(
                m_x,
                m_y,
                m_x + Math.Max(1, m_width) - 1,
                m_y + Math.Max(1, m_height) - 1);

        private bool Record(string name, TilemapStroke2D stroke)
        {
            if (!stroke.hasChanges)
                return false;
            TilemapHistory2D.RecordApplied(history, name, path, asset, stroke);
            return true;
        }

        internal override bool Save(AssetPath path)
        {
            asset.cellSize = new InnoEngine.Mathematics.Vector2(
                MathF.Max(0.0001f, m_cellWidth),
                MathF.Max(0.0001f, m_cellHeight));
            asset.chunkSize = Math.Max(1, m_chunkSize);
            asset.layers = (TilemapLayer2D[])m_layers.Clone();
            return Rendering2DAssets.SaveTilemap(path, asset);
        }
    }

    private sealed class PostProcessDraft(PostProcessProfile2DAsset asset) : Draft
    {
        private float m_exposure = asset.exposure;
        private float m_contrast = asset.contrast;
        private float m_saturation = asset.saturation;
        private bool m_toneMapping = asset.toneMapping;
        private float m_bloomIntensity = asset.bloomIntensity;
        private float m_bloomThreshold = asset.bloomThreshold;
        private float m_vignette = asset.vignette;
        private int m_pixelation = asset.pixelation;

        internal override bool Draw(IEditorPreviewService previews)
        {
            ImGui.SeparatorText("Color and Tone");
            bool changed = ImGui.InputFloat("Exposure", ref m_exposure);
            changed |= ImGui.InputFloat("Contrast", ref m_contrast);
            changed |= ImGui.InputFloat("Saturation", ref m_saturation);
            changed |= ImGui.Checkbox("Tone Mapping", ref m_toneMapping);
            ImGui.SeparatorText("Effects");
            changed |= ImGui.InputFloat("Bloom Intensity", ref m_bloomIntensity);
            changed |= ImGui.InputFloat("Bloom Threshold", ref m_bloomThreshold);
            changed |= ImGui.SliderFloat("Vignette", ref m_vignette, 0f, 1f);
            changed |= ImGui.InputInt("Pixelation", ref m_pixelation);
            return changed;
        }

        internal override bool Save(AssetPath path)
        {
            asset.exposure = m_exposure;
            asset.contrast = MathF.Max(0.0001f, m_contrast);
            asset.saturation = MathF.Max(0f, m_saturation);
            asset.toneMapping = m_toneMapping;
            asset.bloomIntensity = MathF.Max(0f, m_bloomIntensity);
            asset.bloomThreshold = MathF.Max(0f, m_bloomThreshold);
            asset.vignette = Math.Clamp(m_vignette, 0f, 1f);
            asset.pixelation = Math.Max(1, m_pixelation);
            return Rendering2DAssets.SavePostProcess(path, asset);
        }
    }

    private sealed class ParticleDraft(ParticleEffect2DAsset asset) : Draft
    {
        private int m_maximumParticles = asset.maximumParticles;
        private float m_emissionRate = asset.emissionRate;
        private float m_minimumLifetime = asset.minimumLifetime;
        private float m_maximumLifetime = asset.maximumLifetime;
        private float m_minimumSpeed = asset.minimumSpeed;
        private float m_maximumSpeed = asset.maximumSpeed;
        private float m_noiseStrength = asset.noiseStrength;

        internal override bool Draw(IEditorPreviewService previews)
        {
            ImGui.SeparatorText("Emission");
            bool changed = ImGui.InputInt("Maximum Particles", ref m_maximumParticles);
            changed |= ImGui.InputFloat("Emission Rate", ref m_emissionRate);
            ImGui.SeparatorText("Lifetime and Motion");
            changed |= ImGui.InputFloat("Minimum Lifetime", ref m_minimumLifetime);
            changed |= ImGui.InputFloat("Maximum Lifetime", ref m_maximumLifetime);
            changed |= ImGui.InputFloat("Minimum Speed", ref m_minimumSpeed);
            changed |= ImGui.InputFloat("Maximum Speed", ref m_maximumSpeed);
            changed |= ImGui.InputFloat("Noise Strength", ref m_noiseStrength);
            ImGui.Text($"Emitter: {asset.shape}   Space: {asset.simulationSpace}   Flipbook frames: {asset.flipbookFrames.Length}");
            return changed;
        }

        internal override bool Save(AssetPath path)
        {
            asset.maximumParticles = Math.Max(1, m_maximumParticles);
            asset.emissionRate = MathF.Max(0f, m_emissionRate);
            asset.minimumLifetime = MathF.Max(0.0001f, m_minimumLifetime);
            asset.maximumLifetime = MathF.Max(asset.minimumLifetime, m_maximumLifetime);
            asset.minimumSpeed = m_minimumSpeed;
            asset.maximumSpeed = MathF.Max(m_minimumSpeed, m_maximumSpeed);
            asset.noiseStrength = m_noiseStrength;
            return Rendering2DAssets.SaveParticleEffect(path, asset);
        }
    }
}
