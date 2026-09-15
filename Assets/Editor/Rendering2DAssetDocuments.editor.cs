using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using InnoEditor.Assets;
using InnoEditor.Core;
using InnoEditor.ImGui;
using InnoEditor.Inspection;
using InnoEditor.Interactions;
using InnoEditor.Rendering;
using InnoEngine.Assets;
using InnoEngine.Core;
using InnoEngine.Rendering;

namespace Inno.Rendering2D;

/// <summary>Registers the reload-safe headless document provider for native 2D assets.</summary>
[EditorModule("rendering2d.asset-documents", order: 400)]
public sealed class Rendering2DAssetDocumentModule(
    EditorInteractions interactions,
    IEditorPreviewService previews) : EditorModule
{
    private IDisposable? m_registration;
    private Rendering2DAssetDocumentProvider? m_provider;

    internal void DrawInspectorHeader(InspectionDrawContext context, AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(asset);
        Rendering2DAssetDocumentProvider provider = m_provider
            ?? throw new InvalidOperationException("The 2D asset document provider is unavailable.");
        EditorDocumentContext document = interactions.documents.Open(
            asset.assetPath.ToString(),
            asset.identity.persistentId);
        provider.DrawInspectorHeader(document);
    }

    internal void DrawInspectorHeader(InspectionDrawContext context, AssetFileEntry source)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);
        Rendering2DAssetDocumentProvider provider = m_provider
            ?? throw new InvalidOperationException("The 2D asset document provider is unavailable.");
        EditorDocumentContext document = interactions.documents.Open(source.assetPath.ToString(), Guid.Empty);
        provider.DrawInspectorHeader(document);
    }

    internal void DrawInspector(InspectionDrawContext context, AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(asset);
        Rendering2DAssetDocumentProvider provider = m_provider
            ?? throw new InvalidOperationException("The 2D asset document provider is unavailable.");
        EditorDocumentContext document = interactions.documents.Open(
            asset.assetPath.ToString(),
            asset.identity.persistentId);
        provider.DrawInspector(document, context);
    }

    internal void DrawInspector(InspectionDrawContext context, AssetFileEntry source)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);
        Rendering2DAssetDocumentProvider provider = m_provider
            ?? throw new InvalidOperationException("The 2D asset document provider is unavailable.");
        EditorDocumentContext document = interactions.documents.Open(
            source.assetPath.ToString(),
            Guid.Empty);
        provider.DrawInspector(document, context);
    }

    internal EditorHistoryAvailability QueryHistory(
        EditorHistoryChange change,
        EditorHistoryDirection direction)
        => m_provider?.QueryHistory(change, direction)
           ?? EditorHistoryAvailability.Unavailable("The 2D asset draft provider is reloading.");

    internal EditorHistoryResult ApplyHistory(
        EditorHistoryChange change,
        EditorHistoryDirection direction)
        => m_provider?.ApplyHistory(change, direction)
           ?? EditorHistoryResult.Failure("The 2D asset draft provider is reloading.");

    internal bool TryMergeHistory(
        EditorHistoryChange older,
        EditorHistoryChange newer,
        out EditorHistoryChange? merged)
        => Rendering2DAssetDocumentProvider.TryMergeHistory(older, newer, out merged);

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_provider = new Rendering2DAssetDocumentProvider(
            interactions.documents,
            previews,
            interactions.history);
        m_registration = interactions.documents.RegisterProvider(m_provider);
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_registration?.Dispose();
        m_registration = null;
        m_provider = null;
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        m_registration?.Dispose();
        m_registration = null;
        m_provider = null;
    }
}

/// <summary>Draws native Rendering2D authoring drafts through the shared Inspector.</summary>
[InspectionDrawer(typeof(SpriteAtlas2DAsset))]
[InspectionDrawer(typeof(SpriteAnimation2DAsset))]
[InspectionDrawer(typeof(TileSet2DAsset))]
[InspectionDrawer(typeof(Tilemap2DAsset))]
[InspectionDrawer(typeof(PostProcessProfile2DAsset))]
[InspectionDrawer(typeof(ParticleEffect2DAsset))]
public sealed class Rendering2DAssetInspectionDrawer : InspectionDrawer<AssetObject>
{
    /// <inheritdoc />
    public override string icon => ImGuiIcon.File;

    /// <inheritdoc />
    protected override (string, Action<string>?) BindName(
        InspectionDrawContext context,
        AssetObject target)
        => (target.name, null);

    /// <inheritdoc />
    protected override void DrawHeader(InspectionDrawContext context, AssetObject target)
    {
        if (context.interactions.TryGetModule<Rendering2DAssetDocumentModule>(out var module)
            && module is not null)
        {
            module.DrawInspectorHeader(context, target);
        }
    }

    /// <inheritdoc />
    protected override void Draw(InspectionDrawContext context, AssetObject target)
    {
        if (context.interactions.TryGetModule<Rendering2DAssetDocumentModule>(out var module)
            && module is not null)
        {
            module.DrawInspector(context, target);
        }
    }
}

/// <summary>Draws selected Rendering2D source files through the same native asset drafts.</summary>
[InspectionDrawer(typeof(AssetFileEntry), priority: 100, conditional: true)]
public sealed class Rendering2DAssetSourceInspectionDrawer(
    IInspectionIconProvider<AssetFileEntry> icons) : InspectionDrawer<AssetFileEntry>
{
    /// <inheritdoc />
    public override string icon => ImGuiIcon.File;

    /// <inheritdoc />
    protected override bool CanInspect(AssetFileEntry target)
        => !target.isDirectory && Rendering2DAssetDocumentProvider.Supports(target.extension);

    /// <inheritdoc />
    protected override string GetIcon(InspectionDrawContext context, AssetFileEntry target)
        => icons.GetIcon(target);

    /// <inheritdoc />
    protected override (string, Action<string>?) BindName(
        InspectionDrawContext context,
        AssetFileEntry target)
        => (target.nameWithoutExtension, null);

    /// <inheritdoc />
    protected override void DrawHeader(InspectionDrawContext context, AssetFileEntry target)
    {
        if (context.interactions.TryGetModule<Rendering2DAssetDocumentModule>(out var module)
            && module is not null)
        {
            module.DrawInspectorHeader(context, target);
        }
    }

    /// <inheritdoc />
    protected override void Draw(InspectionDrawContext context, AssetFileEntry target)
    {
        if (context.interactions.TryGetModule<Rendering2DAssetDocumentModule>(out var module)
            && module is not null)
        {
            module.DrawInspector(context, target);
        }
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
    private readonly Dictionary<Guid, byte[]> m_baselines = [];

    public override string id => Rendering2DIds.assetDocumentProvider;

    public override bool CanOpen(string assetPath)
        => Supports(Path.GetExtension(assetPath));

    internal static bool Supports(string extension)
        => Array.Exists(
            S_EXTENSIONS,
            value => string.Equals(value, extension, StringComparison.OrdinalIgnoreCase));

    public override void Open(EditorDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.title = Path.GetFileNameWithoutExtension(context.assetPath);
        Draft draft = CreateDraft(context);
        byte[] baseline = draft.Capture();
        if (context.isDirty
            && context.TryGetViewParameter("draft", out string encodedDraft)
            && context.TryGetViewParameter("baseline", out string encodedBaseline))
        {
            baseline = Convert.FromBase64String(encodedBaseline);
            draft.Restore(Convert.FromBase64String(encodedDraft));
        }
        m_drafts[context.documentId] = draft;
        m_baselines[context.documentId] = baseline;
        SynchronizeState(context, draft);
    }

    internal void DrawInspector(
        EditorDocumentContext context,
        InspectionDrawContext inspection)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inspection);
        if (!m_drafts.TryGetValue(context.documentId, out Draft? draft))
            draft = m_drafts[context.documentId] = CreateDraft(context);

        bool readOnly = AssetPath.Parse(context.assetPath).source != AssetSourceId.project;
        ImGui.SeparatorText("Asset");
        ImGui.Text(context.isDirty
            ? "Unsaved changes · runtime content unchanged"
            : "Saved source");
        if (readOnly)
            ImGui.Text("Installed Plugin asset · copy to the project to edit");

        var edits = new InspectorDraftEdits(this, context, draft, readOnly);
        draft.DrawInspector(inspection, previews, edits, readOnly);
    }

    internal void DrawInspectorHeader(EditorDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!m_drafts.TryGetValue(context.documentId, out _))
            m_drafts[context.documentId] = CreateDraft(context);
        bool readOnly = AssetPath.Parse(context.assetPath).source != AssetSourceId.project;
        ImGui.BeginDisabled(readOnly);
        try
        {
            if (ImGui.Button("Save"))
                _ = documents.Save(context.documentId);
            ImGui.SameLine();
            if (ImGui.Button("Revert"))
                _ = documents.Revert(context.documentId);
        }
        finally
        {
            ImGui.EndDisabled();
        }
    }

    public override bool Save(EditorDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!m_drafts.TryGetValue(context.documentId, out Draft? draft))
            return false;
        if (!draft.Save(AssetPath.Parse(context.assetPath))) return false;
        m_baselines[context.documentId] = draft.Capture();
        SynchronizeState(context, draft);
        return true;
    }

    public override bool Revert(EditorDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Draft previous = m_drafts[context.documentId];
        byte[] before = previous.Capture();
        Draft replacement = CreateDraft(context);
        m_drafts[context.documentId] = replacement;
        m_baselines[context.documentId] = replacement.Capture();
        RecordApplied(context, replacement, before, "Revert 2D Asset");
        return true;
    }

    public override void Close(EditorDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ = m_drafts.Remove(context.documentId);
        _ = m_baselines.Remove(context.documentId);
    }

    internal EditorHistoryAvailability QueryHistory(
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            ChangeData data = DecodeChange(change);
            if (!m_drafts.TryGetValue(data.documentId, out Draft? draft))
                return EditorHistoryAvailability.Unavailable($"Reopen '{data.assetPath}' to use its History.");
            byte[] expected = direction == EditorHistoryDirection.Undo ? data.after : data.before;
            return draft.Capture().AsSpan().SequenceEqual(expected)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable("The 2D asset draft changed outside this History entry.");
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidOperationException)
        {
            return EditorHistoryAvailability.Unavailable(error.Message);
        }
    }

    internal EditorHistoryResult ApplyHistory(
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            ChangeData data = DecodeChange(change);
            if (!m_drafts.TryGetValue(data.documentId, out Draft? draft))
                return EditorHistoryResult.Failure($"Reopen '{data.assetPath}' to use its History.");
            byte[] expected = direction == EditorHistoryDirection.Undo ? data.after : data.before;
            if (!draft.Capture().AsSpan().SequenceEqual(expected))
                return EditorHistoryResult.Failure("The 2D asset draft changed outside this History entry.");
            draft.Restore(direction == EditorHistoryDirection.Undo ? data.before : data.after);
            EditorDocumentContext? document = documents.documents.FirstOrDefault(
                value => value.documentId == data.documentId);
            if (document is null)
                return EditorHistoryResult.Failure($"Reopen '{data.assetPath}' to use its History.");
            SynchronizeState(document, draft);
            return EditorHistoryResult.Success();
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidOperationException)
        {
            return EditorHistoryResult.Failure(error.Message);
        }
    }

    internal static bool TryMergeHistory(
        EditorHistoryChange older,
        EditorHistoryChange newer,
        out EditorHistoryChange? merged)
    {
        merged = null;
        try
        {
            ChangeData first = DecodeChange(older);
            ChangeData second = DecodeChange(newer);
            if (older.mergeKey is null
                || !string.Equals(older.mergeKey, newer.mergeKey, StringComparison.Ordinal)
                || first.documentId != second.documentId
                || !first.after.AsSpan().SequenceEqual(second.before)) return false;
            var combined = new ChangeData(first.documentId, second.assetPath, first.before, second.after);
            merged = new EditorHistoryChange(
                Rendering2DIds.assetDraftHistory,
                EditorHistoryPayload.FromBytes(EncodeChange(combined)),
                older.mergeKey);
            return true;
        }
        catch (Exception error) when (error is IOException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private void RecordApplied(
        EditorDocumentContext context,
        Draft draft,
        byte[] before,
        string name,
        string? mergeKey = null)
    {
        byte[] after = draft.Capture();
        if (before.AsSpan().SequenceEqual(after)) return;
        var data = new ChangeData(context.documentId, context.assetPath, before, after);
        var change = new EditorHistoryChange(
            Rendering2DIds.assetDraftHistory,
            EditorHistoryPayload.FromBytes(EncodeChange(data)),
            mergeKey);
        try
        {
            history.RecordApplied(name, change);
            SynchronizeState(context, draft);
        }
        catch
        {
            change.Dispose();
            draft.Restore(before);
            SynchronizeState(context, draft);
            throw;
        }
    }

    private void SynchronizeState(EditorDocumentContext context, Draft draft)
    {
        byte[] current = draft.Capture();
        byte[] baseline = m_baselines[context.documentId];
        context.SetViewParameter("draft", Convert.ToBase64String(current));
        context.SetViewParameter("baseline", Convert.ToBase64String(baseline));
        documents.SetDirty(context.documentId, !current.AsSpan().SequenceEqual(baseline));
    }

    private static byte[] EncodeChange(ChangeData data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(data.documentId.ToByteArray());
        writer.Write(data.assetPath);
        writer.Write(data.before.Length);
        writer.Write(data.before);
        writer.Write(data.after.Length);
        writer.Write(data.after);
        return stream.ToArray();
    }

    private static ChangeData DecodeChange(EditorHistoryChange change)
    {
        if (!string.Equals(change.kind, Rendering2DIds.assetDraftHistory, StringComparison.Ordinal))
            throw new InvalidOperationException("The History entry does not belong to 2D asset drafts.");
        using var stream = new MemoryStream(change.payload.ReadBytes(), writable: false);
        using var reader = new BinaryReader(stream);
        var documentId = new Guid(reader.ReadBytes(16));
        string assetPath = reader.ReadString();
        byte[] before = ReadBytes(reader);
        byte[] after = ReadBytes(reader);
        if (stream.Position != stream.Length) throw new FormatException("The 2D asset History payload has trailing data.");
        return new ChangeData(documentId, assetPath, before, after);

        static byte[] ReadBytes(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > 268_435_456) throw new FormatException("The 2D asset History payload length is invalid.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException("The 2D asset History payload is incomplete.");
            return bytes;
        }
    }

    private sealed record ChangeData(Guid documentId, string assetPath, byte[] before, byte[] after);

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
            return new TilemapDraft(Load<Tilemap2DAsset>(context));
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
        internal abstract byte[] Capture();
        internal abstract void Restore(byte[] bytes);
        internal abstract bool Draw(IEditorPreviewService previews);
        internal abstract bool Save(AssetPath path);

        internal virtual void DrawInspector(
            InspectionDrawContext context,
            IEditorPreviewService previews,
            IInspectionPropertyEditService edits,
            bool readOnly)
        {
            byte[] before = Capture();
            if (!readOnly && Draw(previews))
                ((InspectorDraftEdits)edits).RecordApplied(before, "Edit 2D Asset");
            else if (readOnly)
            {
                ImGui.BeginDisabled();
                try
                {
                    _ = Draw(previews);
                }
                finally
                {
                    ImGui.EndDisabled();
                }
            }
        }
    }

    private sealed class InspectorDraftEdits(
        Rendering2DAssetDocumentProvider provider,
        EditorDocumentContext context,
        Draft draft,
        bool readOnly) : IInspectionPropertyEditService
    {
        public bool ChangeProperty(
            object owner,
            string propertyName,
            Action mutation,
            string historyName)
        {
            if (readOnly)
                return false;
            byte[] before = draft.Capture();
            mutation();
            provider.RecordApplied(
                context,
                draft,
                before,
                historyName,
                context.documentId.ToString("N") + ":" + propertyName);
            return true;
        }

        internal void RecordApplied(byte[] before, string historyName)
            => provider.RecordApplied(context, draft, before, historyName);
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

        internal override byte[] Capture() => EditorAssets.EncodeNative(CreateAsset(normalize: false));

        internal override void Restore(byte[] bytes)
        {
            SpriteAtlas2DAsset restored = EditorAssets.DecodeNative<SpriteAtlas2DAsset>(bytes);
            m_packing = restored.packing;
            m_slices.Clear();
            m_slices.AddRange(restored.slices.Select(CloneSlice));
        }

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
            return Rendering2DAssets.SaveAtlas(path, CreateAsset(normalize: true));
        }

        private SpriteAtlas2DAsset CreateAsset(bool normalize)
        {
            SpriteAtlasPackingSettings2D packing = m_packing;
            if (normalize)
            {
                packing.maximumWidth = Math.Max(1, packing.maximumWidth);
                packing.maximumHeight = Math.Max(1, packing.maximumHeight);
                packing.padding = Math.Max(0, packing.padding);
                packing.extrude = Math.Max(0, packing.extrude);
            }
            return new SpriteAtlas2DAsset
            {
                packing = packing,
                sources = asset.sources,
                slices = m_slices.Select(CloneSlice).ToArray(),
                pages = asset.pages,
                regions = asset.regions
            };
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
        private SpriteAnimationClip2D[] m_clips = CloneClips(asset.clips);
        private int m_selectedClip;
        private float m_playhead;
        private bool m_playing;
        private bool m_onionSkin;

        internal override byte[] Capture()
            => EditorAssets.EncodeNative(new SpriteAnimation2DAsset { clips = CloneClips(m_clips) });

        internal override void Restore(byte[] bytes)
            => m_clips = CloneClips(EditorAssets.DecodeNative<SpriteAnimation2DAsset>(bytes).clips);

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
            return Rendering2DAssets.SaveAnimation(
                path,
                new SpriteAnimation2DAsset { clips = CloneClips(m_clips) });
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
        private TileDefinition2D[] m_tiles = (TileDefinition2D[])asset.tiles.Clone();

        internal override byte[] Capture()
            => EditorAssets.EncodeNative(new TileSet2DAsset { tiles = (TileDefinition2D[])m_tiles.Clone() });

        internal override void Restore(byte[] bytes)
            => m_tiles = (TileDefinition2D[])EditorAssets.DecodeNative<TileSet2DAsset>(bytes).tiles.Clone();

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
            return Rendering2DAssets.SaveTileSet(
                path,
                new TileSet2DAsset { tiles = (TileDefinition2D[])m_tiles.Clone() });
        }
    }

    private sealed class TilemapDraft(Tilemap2DAsset asset) : Draft
    {
        private readonly Tilemap2DAsset m_asset = EditorAssets.DecodeNative<Tilemap2DAsset>(EditorAssets.EncodeNative(asset));
        private TilemapLayer2D[] m_layers = (TilemapLayer2D[])asset.layers.Clone();
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

        internal override byte[] Capture()
            => EditorAssets.EncodeNative(CreateAsset(normalize: false));

        internal override void Restore(byte[] bytes)
        {
            Tilemap2DAsset restored = EditorAssets.DecodeNative<Tilemap2DAsset>(bytes);
            m_asset.tileSet = restored.tileSet;
            m_asset.cellSize = restored.cellSize;
            m_asset.chunkSize = restored.chunkSize;
            m_asset.layers = restored.layers;
            m_asset.chunks = restored.chunks;
            m_cellWidth = restored.cellSize.x;
            m_cellHeight = restored.cellSize.y;
            m_chunkSize = restored.chunkSize;
            m_layers = (TilemapLayer2D[])restored.layers.Clone();
        }

        internal override bool Draw(IEditorPreviewService previews)
        {
            ImGui.SeparatorText("Grid");
            bool changed = ImGui.InputFloat("Cell Width", ref m_cellWidth);
            changed |= ImGui.InputFloat("Cell Height", ref m_cellHeight);
            changed |= ImGui.InputInt("Chunk Size", ref m_chunkSize);
            m_chunkSize = Math.Max(1, m_chunkSize);
            ImGui.Text($"Sparse chunks: {m_asset.chunks.Length}   Revision: {m_asset.revision}");

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
                changed |= Record("Paint Tile", TilemapEditing2D.Brush(m_asset, m_layerId, new TilemapPosition2D(m_x, m_y), paint));
            ImGui.SameLine();
            if (ImGui.Button("Erase"))
                changed |= Record("Erase Tile", TilemapEditing2D.Erase(m_asset, m_layerId, [new TilemapPosition2D(m_x, m_y)]));
            ImGui.SameLine();
            if (ImGui.Button("Pick") && m_asset.TryGetCell(m_x, m_y, m_layerId, out TilemapCell2D picked))
                m_tileId = picked.tileId;
            ImGui.SameLine();
            if (ImGui.Button("Box"))
                changed |= Record("Box Paint Tiles", TilemapEditing2D.Box(m_asset, m_layerId, selection, paint));
            ImGui.SameLine();
            if (ImGui.Button("Fill"))
            {
                changed |= Record(
                    "Fill Tiles",
                    TilemapEditing2D.Fill(
                        m_asset,
                        m_layerId,
                        new TilemapPosition2D(m_x, m_y),
                        selection,
                        paint));
            }
            if (ImGui.Button("Select"))
                m_selectedCount = TilemapEditing2D.Select(m_asset, m_layerId, selection).Count;
            ImGui.SameLine();
            _ = ImGui.InputInt("Move X", ref m_moveX);
            _ = ImGui.InputInt("Move Y", ref m_moveY);
            if (ImGui.Button("Move Selection"))
                changed |= Record("Move Tiles", TilemapEditing2D.Move(m_asset, m_layerId, selection, m_moveX, m_moveY));
            ImGui.SameLine();
            if (ImGui.Button("Stamp Selection"))
            {
                IReadOnlyList<TilemapCell2D> selected = TilemapEditing2D.Select(m_asset, m_layerId, selection);
                TilemapStampCell2D[] stamp = selected
                    .Select(cell => new TilemapStampCell2D(
                        cell.x - selection.minimumX,
                        cell.y - selection.minimumY,
                        cell))
                    .ToArray();
                changed |= Record(
                    "Stamp Tiles",
                    TilemapEditing2D.Stamp(
                        m_asset,
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
            return true;
        }

        internal override bool Save(AssetPath path)
        {
            return Rendering2DAssets.SaveTilemap(path, CreateAsset(normalize: true));
        }

        private Tilemap2DAsset CreateAsset(bool normalize)
        {
            float width = normalize ? MathF.Max(0.0001f, m_cellWidth) : m_cellWidth;
            float height = normalize ? MathF.Max(0.0001f, m_cellHeight) : m_cellHeight;
            int chunkSize = normalize ? Math.Max(1, m_chunkSize) : Math.Max(1, m_chunkSize);
            return new Tilemap2DAsset
            {
                tileSet = m_asset.tileSet,
                cellSize = new InnoEngine.Mathematics.Vector2(width, height),
                chunkSize = chunkSize,
                layers = (TilemapLayer2D[])m_layers.Clone(),
                chunks = m_asset.chunks
            };
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
        private int m_bloomLevels = asset.bloomLevels;
        private float m_bloomScatter = asset.bloomScatter;
        private float m_vignette = asset.vignette;
        private int m_pixelation = asset.pixelation;

        internal override byte[] Capture() => EditorAssets.EncodeNative(CreateAsset(normalize: false));

        internal override void Restore(byte[] bytes)
        {
            PostProcessProfile2DAsset restored = EditorAssets.DecodeNative<PostProcessProfile2DAsset>(bytes);
            m_exposure = restored.exposure;
            m_contrast = restored.contrast;
            m_saturation = restored.saturation;
            m_toneMapping = restored.toneMapping;
            m_bloomIntensity = restored.bloomIntensity;
            m_bloomThreshold = restored.bloomThreshold;
            m_bloomLevels = restored.bloomLevels;
            m_bloomScatter = restored.bloomScatter;
            m_vignette = restored.vignette;
            m_pixelation = restored.pixelation;
        }

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
            changed |= ImGui.InputInt("Bloom Levels", ref m_bloomLevels);
            changed |= ImGui.SliderFloat("Bloom Scatter", ref m_bloomScatter, 0f, 1f);
            changed |= ImGui.SliderFloat("Vignette", ref m_vignette, 0f, 1f);
            changed |= ImGui.InputInt("Pixelation", ref m_pixelation);
            return changed;
        }

        internal override void DrawInspector(
            InspectionDrawContext context,
            IEditorPreviewService previews,
            IInspectionPropertyEditService edits,
            bool readOnly)
        {
            ImGui.SeparatorText("Color and Tone");
            Property("exposure", "Exposure", () => m_exposure, value => m_exposure = value);
            Property("contrast", "Contrast", () => m_contrast, value => m_contrast = value, minimum: 0.0001);
            Property("saturation", "Saturation", () => m_saturation, value => m_saturation = value, minimum: 0);
            Property("toneMapping", "Tone Mapping", () => m_toneMapping, value => m_toneMapping = value);

            ImGui.SeparatorText("Bloom");
            Property("bloomIntensity", "Intensity", () => m_bloomIntensity, value => m_bloomIntensity = value, minimum: 0);
            Property("bloomThreshold", "Threshold", () => m_bloomThreshold, value => m_bloomThreshold = value, minimum: 0);
            Property("bloomLevels", "Levels", () => m_bloomLevels, value => m_bloomLevels = value, minimum: 1, maximum: 8);
            Property("bloomScatter", "Scatter", () => m_bloomScatter, value => m_bloomScatter = value, minimum: 0, maximum: 1);

            ImGui.SeparatorText("Screen Effects");
            Property("vignette", "Vignette", () => m_vignette, value => m_vignette = value, minimum: 0, maximum: 1);
            Property("pixelation", "Pixelation", () => m_pixelation, value => m_pixelation = value, minimum: 1);

            void Property<T>(
                string path,
                string label,
                Func<T> getter,
                Action<T> setter,
                double? minimum = null,
                double? maximum = null)
            {
                context.properties.DrawValue(
                    context.editorContext,
                    this,
                    "post-process." + path,
                    label,
                    typeof(T),
                    () => getter(),
                    value => setter(value is T typed
                        ? typed
                        : throw new InvalidOperationException($"'{path}' received an incompatible value.")),
                    edits,
                    readOnly,
                    minimum: minimum,
                    maximum: maximum);
            }
        }

        internal override bool Save(AssetPath path)
        {
            return Rendering2DAssets.SavePostProcess(path, CreateAsset(normalize: true));
        }

        private PostProcessProfile2DAsset CreateAsset(bool normalize)
            => new()
            {
                exposure = m_exposure,
                contrast = normalize ? MathF.Max(0.0001f, m_contrast) : m_contrast,
                saturation = normalize ? MathF.Max(0f, m_saturation) : m_saturation,
                toneMapping = m_toneMapping,
                bloomIntensity = normalize ? MathF.Max(0f, m_bloomIntensity) : m_bloomIntensity,
                bloomThreshold = normalize ? MathF.Max(0f, m_bloomThreshold) : m_bloomThreshold,
                bloomLevels = normalize ? Math.Clamp(m_bloomLevels, 1, 8) : m_bloomLevels,
                bloomScatter = normalize ? Math.Clamp(m_bloomScatter, 0f, 1f) : m_bloomScatter,
                vignette = normalize ? Math.Clamp(m_vignette, 0f, 1f) : m_vignette,
                pixelation = normalize ? Math.Max(1, m_pixelation) : m_pixelation
            };
    }

    private sealed class ParticleDraft(ParticleEffect2DAsset asset) : Draft
    {
        private SpriteReference2D m_sprite = asset.sprite;
        private SpriteReference2D[] m_flipbookFrames = asset.flipbookFrames.ToArray();
        private float m_flipbookFramesPerSecond = asset.flipbookFramesPerSecond;
        private MaterialAsset? m_material = asset.material;
        private SpriteBlendMode2D m_blendMode = asset.blendMode;
        private SpriteSamplingMode2D m_sampling = asset.sampling;
        private ParticleEmitterShape2D m_shape = asset.shape;
        private ParticleSimulationSpace2D m_simulationSpace = asset.simulationSpace;
        private int m_maximumParticles = asset.maximumParticles;
        private float m_emissionRate = asset.emissionRate;
        private float m_minimumLifetime = asset.minimumLifetime;
        private float m_maximumLifetime = asset.maximumLifetime;
        private float m_minimumSpeed = asset.minimumSpeed;
        private float m_maximumSpeed = asset.maximumSpeed;
        private InnoEngine.Mathematics.Vector2 m_shapeSize = asset.shapeSize;
        private float m_coneAngle = asset.coneAngle;
        private InnoEngine.Mathematics.Vector2 m_gravity = asset.gravity;
        private float m_noiseStrength = asset.noiseStrength;
        private ParticleCurve2D m_sizeOverLifetime = Clone(asset.sizeOverLifetime);
        private ParticleGradient2D m_colorOverLifetime = Clone(asset.colorOverLifetime);

        internal override byte[] Capture() => EditorAssets.EncodeNative(CreateAsset(normalize: false));

        internal override void Restore(byte[] bytes)
        {
            ParticleEffect2DAsset restored = EditorAssets.DecodeNative<ParticleEffect2DAsset>(bytes);
            m_sprite = restored.sprite;
            m_flipbookFrames = restored.flipbookFrames.ToArray();
            m_flipbookFramesPerSecond = restored.flipbookFramesPerSecond;
            m_material = restored.material;
            m_blendMode = restored.blendMode;
            m_sampling = restored.sampling;
            m_shape = restored.shape;
            m_simulationSpace = restored.simulationSpace;
            m_maximumParticles = restored.maximumParticles;
            m_emissionRate = restored.emissionRate;
            m_minimumLifetime = restored.minimumLifetime;
            m_maximumLifetime = restored.maximumLifetime;
            m_minimumSpeed = restored.minimumSpeed;
            m_maximumSpeed = restored.maximumSpeed;
            m_shapeSize = restored.shapeSize;
            m_coneAngle = restored.coneAngle;
            m_gravity = restored.gravity;
            m_noiseStrength = restored.noiseStrength;
            m_sizeOverLifetime = Clone(restored.sizeOverLifetime);
            m_colorOverLifetime = Clone(restored.colorOverLifetime);
        }

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
            changed |= ImGui.InputFloat("Flipbook FPS", ref m_flipbookFramesPerSecond);
            ImGui.Text($"Emitter: {m_shape}   Space: {m_simulationSpace}   Flipbook frames: {m_flipbookFrames.Length}");
            return changed;
        }

        internal override void DrawInspector(
            InspectionDrawContext context,
            IEditorPreviewService previews,
            IInspectionPropertyEditService edits,
            bool readOnly)
        {
            ImGui.SeparatorText("Rendering");
            Property("sprite", "Sprite", () => m_sprite, value => m_sprite = value);
            Property("flipbookFrames", "Flipbook Frames", () => m_flipbookFrames, value => m_flipbookFrames = value ?? []);
            Property("flipbookFramesPerSecond", "Flipbook FPS", () => m_flipbookFramesPerSecond, value => m_flipbookFramesPerSecond = value, minimum: 0);
            Property<MaterialAsset?>("material", "Material", () => m_material, value => m_material = value);
            Property("blendMode", "Blend Mode", () => m_blendMode, value => m_blendMode = value);
            Property("sampling", "Sampling", () => m_sampling, value => m_sampling = value);

            ImGui.SeparatorText("Emitter");
            Property("shape", "Shape", () => m_shape, value => m_shape = value);
            Property("simulationSpace", "Simulation Space", () => m_simulationSpace, value => m_simulationSpace = value);
            Property("maximumParticles", "Maximum Particles", () => m_maximumParticles, value => m_maximumParticles = value, minimum: 1);
            Property("emissionRate", "Emission Rate", () => m_emissionRate, value => m_emissionRate = value, minimum: 0);
            Property("shapeSize", "Shape Size", () => m_shapeSize, value => m_shapeSize = value);
            Property("coneAngle", "Cone Angle", () => m_coneAngle, value => m_coneAngle = value, minimum: 0, maximum: 360);

            ImGui.SeparatorText("Lifetime and Motion");
            Property("minimumLifetime", "Minimum Lifetime", () => m_minimumLifetime, value => m_minimumLifetime = value, minimum: 0.0001);
            Property("maximumLifetime", "Maximum Lifetime", () => m_maximumLifetime, value => m_maximumLifetime = value, minimum: 0.0001);
            Property("minimumSpeed", "Minimum Speed", () => m_minimumSpeed, value => m_minimumSpeed = value);
            Property("maximumSpeed", "Maximum Speed", () => m_maximumSpeed, value => m_maximumSpeed = value);
            Property("gravity", "Gravity", () => m_gravity, value => m_gravity = value);
            Property("noiseStrength", "Noise Strength", () => m_noiseStrength, value => m_noiseStrength = value);

            ImGui.SeparatorText("Over Lifetime");
            Property("sizeOverLifetime", "Size", () => m_sizeOverLifetime, value => m_sizeOverLifetime = value);
            Property("colorOverLifetime", "Color", () => m_colorOverLifetime, value => m_colorOverLifetime = value);

            void Property<T>(
                string path,
                string label,
                Func<T> getter,
                Action<T> setter,
                double? minimum = null,
                double? maximum = null)
            {
                context.properties.DrawValue(
                    context.editorContext,
                    this,
                    "particle." + path,
                    label,
                    typeof(T),
                    () => getter(),
                    value => setter(value is T typed
                        ? typed
                        : value is null && default(T) is null
                            ? default!
                            : throw new InvalidOperationException($"'{path}' received an incompatible value.")),
                    edits,
                    readOnly,
                    minimum: minimum,
                    maximum: maximum);
            }
        }

        internal override bool Save(AssetPath path)
        {
            return Rendering2DAssets.SaveParticleEffect(path, CreateAsset(normalize: true));
        }

        private ParticleEffect2DAsset CreateAsset(bool normalize)
        {
            float minimumLifetime = normalize ? MathF.Max(0.0001f, m_minimumLifetime) : m_minimumLifetime;
            float minimumSpeed = m_minimumSpeed;
            return new ParticleEffect2DAsset
            {
                sprite = m_sprite,
                flipbookFrames = m_flipbookFrames.ToArray(),
                flipbookFramesPerSecond = normalize ? MathF.Max(0f, m_flipbookFramesPerSecond) : m_flipbookFramesPerSecond,
                material = m_material,
                blendMode = m_blendMode,
                sampling = m_sampling,
                shape = m_shape,
                simulationSpace = m_simulationSpace,
                maximumParticles = normalize ? Math.Max(1, m_maximumParticles) : m_maximumParticles,
                emissionRate = normalize ? MathF.Max(0f, m_emissionRate) : m_emissionRate,
                minimumLifetime = minimumLifetime,
                maximumLifetime = normalize ? MathF.Max(minimumLifetime, m_maximumLifetime) : m_maximumLifetime,
                minimumSpeed = minimumSpeed,
                maximumSpeed = normalize ? MathF.Max(minimumSpeed, m_maximumSpeed) : m_maximumSpeed,
                shapeSize = m_shapeSize,
                coneAngle = normalize ? Math.Clamp(m_coneAngle, 0f, 360f) : m_coneAngle,
                gravity = m_gravity,
                noiseStrength = m_noiseStrength,
                sizeOverLifetime = Clone(m_sizeOverLifetime),
                colorOverLifetime = Clone(m_colorOverLifetime)
            };
        }

        private static ParticleCurve2D Clone(ParticleCurve2D value)
        {
            value.keys = value.keys?.ToArray() ?? [];
            return value;
        }

        private static ParticleGradient2D Clone(ParticleGradient2D value)
        {
            value.keys = value.keys?.ToArray() ?? [];
            return value;
        }
    }
}

/// <summary>Interprets neutral, reload-safe History records for every native Rendering2D asset draft.</summary>
[EditorHistoryHandler(Rendering2DIds.assetDraftHistory)]
public sealed class Rendering2DAssetDraftHistoryHandler(
    Rendering2DAssetDocumentModule documents) : EditorHistoryHandler
{
    /// <inheritdoc />
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        ArgumentNullException.ThrowIfNull(context);
        return documents.QueryHistory(change, direction);
    }

    /// <inheritdoc />
    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        ArgumentNullException.ThrowIfNull(context);
        return documents.ApplyHistory(change, direction);
    }

    /// <inheritdoc />
    protected override bool TryMerge(
        EditorHistoryChange older,
        EditorHistoryChange newer,
        out EditorHistoryChange? merged)
        => documents.TryMergeHistory(older, newer, out merged);
}
