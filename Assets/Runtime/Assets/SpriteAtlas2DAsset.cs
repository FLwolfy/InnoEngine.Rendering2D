using System;
using System.Collections.Generic;
using System.Linq;
using InnoEngine.Assets;
using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Rendering;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Identifies one sprite region independently from its display name and packed location.</summary>
public record struct SpriteRegionId
{
    /// <summary>Creates a stable sprite-region identifier.</summary>
    /// <param name="value">Non-empty stable identifier.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public SpriteRegionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>Gets or sets the stable serialized identifier.</summary>
    public string value { get; set; }

    /// <summary>Gets whether this identifier contains a usable value.</summary>
    public readonly bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>Creates a globally unique sprite-region identifier.</summary>
    /// <returns>A new stable identifier formatted without separators.</returns>
    public static SpriteRegionId Create() => new(Guid.NewGuid().ToString("N"));

    /// <summary>Formats this identifier for diagnostics and authoring UI.</summary>
    /// <returns>The stable identifier value.</returns>
    public readonly override string ToString() => value ?? string.Empty;
}

/// <summary>Describes deterministic atlas packing behavior for authoring tools.</summary>
public struct SpriteAtlasPackingSettings2D
{
    /// <summary>Gets or sets the positive maximum page width.</summary>
    public int maximumWidth { get; set; }

    /// <summary>Gets or sets the positive maximum page height.</summary>
    public int maximumHeight { get; set; }

    /// <summary>Gets or sets transparent padding around each packed region.</summary>
    public int padding { get; set; }

    /// <summary>Gets or sets duplicated edge texels outside each packed region.</summary>
    public int extrude { get; set; }

    /// <summary>Gets or sets whether the deterministic packer may rotate regions clockwise.</summary>
    public bool allowRotation { get; set; }

    /// <summary>Gets or sets whether transparent borders are removed before packing.</summary>
    public bool trimTransparent { get; set; }

    /// <summary>Gets the recommended balanced packing settings.</summary>
    public static SpriteAtlasPackingSettings2D defaults => new()
    {
        maximumWidth = 4096,
        maximumHeight = 4096,
        padding = 2,
        extrude = 1,
        allowRotation = true,
        trimTransparent = true
    };
}

/// <summary>Declares one source texture consumed by atlas authoring and dependency tracking.</summary>
public struct SpriteAtlasSource2D
{
    /// <summary>Gets or sets the source texture.</summary>
    public TextureAsset? texture { get; set; }

    /// <summary>Gets or sets the optional tangent-space normal map matching the color source dimensions.</summary>
    public TextureAsset? normalMap { get; set; }

    /// <summary>Gets or sets the optional linear emission map matching the color source dimensions.</summary>
    public TextureAsset? emissionMap { get; set; }

    /// <summary>Gets or sets an optional author-facing source label.</summary>
    public string name { get; set; }
}

/// <summary>Defines one stable source rectangle before trim and deterministic packing.</summary>
public struct SpriteAtlasSlice2D
{
    /// <summary>Gets or sets the stable region identity preserved across repacking.</summary>
    public SpriteRegionId id { get; set; }

    /// <summary>Gets or sets the author-facing region name.</summary>
    public string name { get; set; }

    /// <summary>Gets or sets the zero-based source texture index.</summary>
    public int sourceIndex { get; set; }

    /// <summary>Gets or sets the source rectangle in top-left pixel coordinates.</summary>
    public Rect pixelRect { get; set; }

    /// <summary>Gets or sets the normalized pivot in the untrimmed source rectangle.</summary>
    public Vector2 pivot { get; set; }

    /// <summary>Gets or sets left, bottom, right, and top nine-slice borders in source pixels.</summary>
    public System.Numerics.Vector4 borderPixels { get; set; }

    /// <summary>Gets or sets an optional normalized author-defined outline.</summary>
    public Vector2[] outline { get; set; }
}

/// <summary>Describes one independently compiled page in a multi-page sprite atlas.</summary>
public struct SpriteAtlasPage2D
{
    /// <summary>Gets or sets the stable page identifier.</summary>
    public string id { get; set; }

    /// <summary>Gets or sets the positive page width in pixels.</summary>
    public int width { get; set; }

    /// <summary>Gets or sets the positive page height in pixels.</summary>
    public int height { get; set; }

    /// <summary>Gets or sets whether this page has a packed normal-map companion.</summary>
    public bool hasNormalMap { get; set; }

    /// <summary>Gets or sets whether this page has a packed emission-map companion.</summary>
    public bool hasEmissionMap { get; set; }

    /// <summary>Gets the named color source artifact written by the atlas importer.</summary>
    public readonly string colorSourceOutputName => $"texture-color-{id}";

    /// <summary>Gets the named normal source artifact written by the atlas importer.</summary>
    public readonly string normalSourceOutputName => $"texture-normal-{id}";

    /// <summary>Gets the named emission source artifact written by the atlas importer.</summary>
    public readonly string emissionSourceOutputName => $"texture-emission-{id}";
}

/// <summary>Describes one trim-aware sprite region in normalized top-left page coordinates.</summary>
public struct SpriteRegion2D
{
    /// <summary>Gets or sets the stable region identity within its atlas.</summary>
    public SpriteRegionId id { get; set; }

    /// <summary>Gets or sets the independent author-facing display name.</summary>
    public string name { get; set; }

    /// <summary>Gets or sets the zero-based atlas page index.</summary>
    public int pageIndex { get; set; }

    /// <summary>Gets or sets normalized top-left UV bounds.</summary>
    public Rect uvRect { get; set; }

    /// <summary>Gets or sets the original untrimmed source size in pixels.</summary>
    public Vector2 sourceSizePixels { get; set; }

    /// <summary>Gets or sets the packed trimmed size in pixels.</summary>
    public Vector2 trimmedSizePixels { get; set; }

    /// <summary>Gets or sets the trimmed bottom-left offset inside the original source rectangle.</summary>
    public Vector2 trimOffsetPixels { get; set; }

    /// <summary>Gets or sets normalized pivot in the original source rectangle.</summary>
    public Vector2 pivot { get; set; }

    /// <summary>Gets or sets left, bottom, right, and top nine-slice borders in source pixels.</summary>
    public System.Numerics.Vector4 borderPixels { get; set; }

    /// <summary>Gets or sets whether packed texels are rotated ninety degrees clockwise.</summary>
    public bool rotatedClockwise { get; set; }

    /// <summary>Gets or sets the optional normalized alpha-outline polygon used by tools and shadows.</summary>
    public Vector2[] outline { get; set; }
}

/// <summary>Stores deterministic multi-page texture and region data used by 2D content.</summary>
[StableTypeId("94818e9d-f610-44f7-b4ac-dc240ff1bd85")]
public sealed class SpriteAtlas2DAsset : AssetObject, IRenderTextureArtifactSource
{
    private SpriteAtlasSource2D[] m_sources = [];
    private SpriteAtlasSlice2D[] m_slices = [];
    private SpriteAtlasPage2D[] m_pages = [];
    private SpriteRegion2D[] m_regions = [];
    private RenderTextureArtifactSlot[] m_textureArtifacts = [];

    /// <summary>Gets or sets deterministic packing behavior used by atlas authoring.</summary>
    [SerializableProperty]
    public SpriteAtlasPackingSettings2D packing { get; set; } = SpriteAtlasPackingSettings2D.defaults;

    /// <summary>Gets or sets source textures that invalidate authored atlas content.</summary>
    [SerializableProperty]
    public SpriteAtlasSource2D[] sources
    {
        get => m_sources;
        set => m_sources = value?.ToArray() ?? [];
    }

    /// <summary>Gets or sets stable source rectangles consumed by deterministic atlas composition.</summary>
    [SerializableProperty]
    public SpriteAtlasSlice2D[] slices
    {
        get => m_slices;
        set => m_slices = value?.ToArray() ?? [];
    }

    /// <summary>Gets or sets all independently compiled atlas pages.</summary>
    [SerializableProperty]
    public SpriteAtlasPage2D[] pages
    {
        get => m_pages;
        set
        {
            m_pages = value?.ToArray() ?? [];
            RebuildTextureArtifacts();
        }
    }

    /// <summary>Gets or sets all stable regions.</summary>
    [SerializableProperty]
    public SpriteRegion2D[] regions
    {
        get => m_regions;
        set => m_regions = value?.ToArray() ?? [];
    }

    /// <summary>Gets the complete immutable set of page texture artifacts.</summary>
    public IReadOnlyList<RenderTextureArtifactSlot> textureArtifacts => m_textureArtifacts;

    /// <summary>Tries to resolve a stable region identity.</summary>
    /// <param name="id">Stable region identity.</param>
    /// <param name="region">Receives the matching region.</param>
    /// <returns><see langword="true"/> when the region exists.</returns>
    public bool TryGetRegion(SpriteRegionId id, out SpriteRegion2D region)
    {
        for (int index = 0; index < m_regions.Length; index++)
        {
            if (m_regions[index].id != id)
                continue;
            region = m_regions[index];
            return true;
        }
        region = default;
        return false;
    }

    /// <summary>Creates a current-generation texture reference for one page.</summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <returns>A stable texture artifact reference for the selected page.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pageIndex"/> is outside the page set.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the atlas has no persistent identity.</exception>
    public RenderTextureArtifactReference GetPageTexture(int pageIndex)
    {
        return GetPageArtifact(pageIndex, AtlasPageMap2D.Color);
    }

    /// <summary>Tries to create a current-generation normal-map reference for one page.</summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="reference">Receives the stable normal-map artifact reference.</param>
    /// <returns><see langword="true"/> when the page contains authored normal data.</returns>
    public bool TryGetPageNormalTexture(int pageIndex, out RenderTextureArtifactReference reference)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, m_pages.Length);
        if (!m_pages[pageIndex].hasNormalMap)
        {
            reference = default;
            return false;
        }
        reference = GetPageArtifact(pageIndex, AtlasPageMap2D.Normal);
        return true;
    }

    /// <summary>Tries to create a current-generation emission-map reference for one page.</summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="reference">Receives the stable emission-map artifact reference.</param>
    /// <returns><see langword="true"/> when the page contains authored emission data.</returns>
    public bool TryGetPageEmissionTexture(int pageIndex, out RenderTextureArtifactReference reference)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, m_pages.Length);
        if (!m_pages[pageIndex].hasEmissionMap)
        {
            reference = default;
            return false;
        }
        reference = GetPageArtifact(pageIndex, AtlasPageMap2D.Emission);
        return true;
    }

    /// <summary>Replaces all pages after validating stable IDs and source dimensions.</summary>
    /// <param name="pages">Complete page set in stable draw order.</param>
    /// <exception cref="ArgumentException">Thrown when a page is incomplete or page IDs are duplicated.</exception>
    public void SetPages(IEnumerable<SpriteAtlasPage2D> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        SpriteAtlasPage2D[] values = pages.ToArray();
        if (values.Any(static value => string.IsNullOrWhiteSpace(value.id)
            || value.width <= 0
            || value.height <= 0))
        {
            throw new ArgumentException("Atlas pages require IDs and positive dimensions.", nameof(pages));
        }
        if (values.Select(static value => value.id).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Atlas page IDs must be unique.", nameof(pages));
        this.pages = values;
    }

    /// <summary>Replaces source slices after validating stable IDs, texture indices, and pixel bounds.</summary>
    /// <param name="slices">Complete authored slice set.</param>
    /// <exception cref="ArgumentException">Thrown when a slice is incomplete or outside its source.</exception>
    public void SetSlices(IEnumerable<SpriteAtlasSlice2D> slices)
    {
        ArgumentNullException.ThrowIfNull(slices);
        SpriteAtlasSlice2D[] values = slices.ToArray();
        if (values.Any(static value => !value.id.isValid
            || string.IsNullOrWhiteSpace(value.name)
            || value.sourceIndex < 0
            || value.pixelRect.x < 0f
            || value.pixelRect.y < 0f
            || value.pixelRect.width <= 0f
            || value.pixelRect.height <= 0f))
        {
            throw new ArgumentException("Atlas slices require stable IDs, names, source indices, and positive pixel rectangles.", nameof(slices));
        }
        if (values.Select(static value => value.id).Distinct().Count() != values.Length)
            throw new ArgumentException("Atlas slice IDs must be unique.", nameof(slices));
        foreach (SpriteAtlasSlice2D slice in values)
        {
            if (slice.sourceIndex >= m_sources.Length
                || m_sources[slice.sourceIndex].texture is not TextureAsset texture
                || slice.pixelRect.right > texture.width
                || slice.pixelRect.bottom > texture.height)
            {
                throw new ArgumentException($"Atlas slice '{slice.name}' is outside its source texture.", nameof(slices));
            }
        }
        m_slices = values;
    }

    /// <summary>Replaces all regions after validating IDs, page references, and normalized bounds.</summary>
    /// <param name="regions">Complete region set.</param>
    /// <exception cref="ArgumentException">Thrown when identities or region geometry are invalid.</exception>
    public void SetRegions(IEnumerable<SpriteRegion2D> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        SpriteRegion2D[] values = regions.ToArray();
        if (values.Any(static value => !value.id.isValid || string.IsNullOrWhiteSpace(value.name)))
            throw new ArgumentException("Sprite regions require stable IDs and display names.", nameof(regions));
        if (values.Select(static value => value.id).Distinct().Count() != values.Length)
            throw new ArgumentException("Sprite region IDs must be unique.", nameof(regions));
        foreach (SpriteRegion2D region in values)
        {
            if (region.pageIndex < 0 || region.pageIndex >= m_pages.Length
                || region.uvRect.width <= 0f || region.uvRect.height <= 0f
                || region.sourceSizePixels.x <= 0f || region.sourceSizePixels.y <= 0f
                || region.trimmedSizePixels.x <= 0f || region.trimmedSizePixels.y <= 0f)
            {
                throw new ArgumentException("Sprite regions require valid pages and positive UV and pixel extents.", nameof(regions));
            }
        }
        m_regions = values;
    }

    private void RebuildTextureArtifacts()
    {
        int count = m_pages.Length;
        for (int index = 0; index < m_pages.Length; index++)
        {
            count += m_pages[index].hasNormalMap ? 1 : 0;
            count += m_pages[index].hasEmissionMap ? 1 : 0;
        }
        m_textureArtifacts = new RenderTextureArtifactSlot[count];
        int outputIndex = 0;
        for (int index = 0; index < m_pages.Length; index++)
        {
            SpriteAtlasPage2D page = m_pages[index];
            if (string.IsNullOrWhiteSpace(page.id))
                continue;
            m_textureArtifacts[outputIndex++] = new RenderTextureArtifactSlot(
                GetSlotId(page.id, AtlasPageMap2D.Color),
                page.colorSourceOutputName,
                TextureColorSpace.Srgb);
            if (page.hasNormalMap)
            {
                m_textureArtifacts[outputIndex++] = new RenderTextureArtifactSlot(
                    GetSlotId(page.id, AtlasPageMap2D.Normal),
                    page.normalSourceOutputName,
                    TextureColorSpace.Linear);
            }
            if (page.hasEmissionMap)
            {
                m_textureArtifacts[outputIndex++] = new RenderTextureArtifactSlot(
                    GetSlotId(page.id, AtlasPageMap2D.Emission),
                    page.emissionSourceOutputName,
                    TextureColorSpace.Linear);
            }
        }
    }

    private RenderTextureArtifactReference GetPageArtifact(int pageIndex, AtlasPageMap2D map)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, m_pages.Length);
        Guid atlasId = identity.persistentId;
        if (atlasId == Guid.Empty)
            throw new InvalidOperationException("Sprite atlas must have a persistent asset identity.");
        string slotId = GetSlotId(m_pages[pageIndex].id, map);
        for (int index = 0; index < m_textureArtifacts.Length; index++)
        {
            if (string.Equals(m_textureArtifacts[index].id, slotId, StringComparison.Ordinal))
                return new RenderTextureArtifactReference(atlasId, contentVersion, m_textureArtifacts[index]);
        }
        throw new InvalidOperationException($"Sprite atlas page '{pageIndex}' has no {map} artifact.");
    }

    private static string GetSlotId(string pageId, AtlasPageMap2D map)
        => $"{map.ToString().ToLowerInvariant()}-{pageId}";

    private enum AtlasPageMap2D
    {
        Color,
        Normal,
        Emission
    }
}

/// <summary>References either one atlas region or one standalone texture without using display names.</summary>
public struct SpriteReference2D
{
    /// <summary>Gets or sets the atlas that owns <see cref="regionId"/>.</summary>
    public SpriteAtlas2DAsset? atlas { get; set; }

    /// <summary>Gets or sets the stable atlas region identity.</summary>
    public SpriteRegionId regionId { get; set; }

    /// <summary>Gets or sets a standalone texture used when no atlas is assigned.</summary>
    public TextureAsset? texture { get; set; }

    /// <summary>Gets whether this reference selects an atlas region or standalone texture.</summary>
    public readonly bool isAssigned => atlas is not null && regionId.isValid || texture is not null;
}
