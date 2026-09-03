using System;
using System.Collections.Generic;
using System.Linq;
using InnoEngine.Assets;
using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Rendering;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Describes one trim-aware sprite region in normalized top-left texture coordinates.</summary>
public struct SpriteRegion2D
{
    /// <summary>Gets or sets the stable region identity within its atlas.</summary>
    public string id { get; set; }

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
}

/// <summary>Stores one texture and stable trim-aware regions used by sprites, animation, and tile sets.</summary>
[StableTypeId("94818e9d-f610-44f7-b4ac-dc240ff1bd85")]
public sealed class SpriteAtlas2DAsset : AssetObject
{
    private SpriteRegion2D[] m_regions = [];

    /// <summary>Gets or sets the sampled atlas texture.</summary>
    [SerializableProperty]
    public TextureAsset? texture { get; set; }

    /// <summary>Gets or sets all stable regions.</summary>
    [SerializableProperty]
    public SpriteRegion2D[] regions
    {
        get => m_regions;
        set => m_regions = value?.ToArray() ?? [];
    }

    /// <summary>Tries to resolve a stable region identity.</summary>
    /// <param name="id">Stable region identity.</param>
    /// <param name="region">Receives the matching region.</param>
    /// <returns><see langword="true"/> when the region exists.</returns>
    public bool TryGetRegion(string id, out SpriteRegion2D region)
    {
        for (int index = 0; index < m_regions.Length; index++)
        {
            if (!string.Equals(m_regions[index].id, id, StringComparison.Ordinal))
                continue;
            region = m_regions[index];
            return true;
        }
        region = default;
        return false;
    }

    /// <summary>Replaces all regions after validating IDs and normalized bounds.</summary>
    /// <param name="regions">Complete region set.</param>
    /// <exception cref="ArgumentException">Thrown when identities or region geometry are invalid.</exception>
    public void SetRegions(IEnumerable<SpriteRegion2D> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        SpriteRegion2D[] values = regions.ToArray();
        if (values.Any(static value => string.IsNullOrWhiteSpace(value.id)))
            throw new ArgumentException("Sprite region IDs cannot be empty.", nameof(regions));
        if (values.Select(static value => value.id).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Sprite region IDs must be unique.", nameof(regions));
        foreach (SpriteRegion2D region in values)
        {
            if (region.uvRect.width <= 0f || region.uvRect.height <= 0f
                || region.sourceSizePixels.x <= 0f || region.sourceSizePixels.y <= 0f
                || region.trimmedSizePixels.x <= 0f || region.trimmedSizePixels.y <= 0f)
            {
                throw new ArgumentException("Sprite regions require positive UV and pixel extents.", nameof(regions));
            }
        }
        m_regions = values;
    }
}
