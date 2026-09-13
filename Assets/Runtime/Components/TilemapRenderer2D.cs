using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Rendering;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEditor.Annotations;

namespace Inno.Rendering2D;

/// <summary>Renders visible sparse tilemap cells through the same material and batching contract as sprites.</summary>
[StableTypeId("2fd89229-439b-4f23-aa44-355723c4da35")]
public sealed class TilemapRenderer2D : GameBehavior
{
    private byte m_lightBlendStyles = 0x0f;

    /// <summary>Gets or sets sparse tilemap content.</summary>
    [SerializableProperty]
    [Header("Tilemap", "Only visible dirty chunks are expanded and uploaded.")]
    public Tilemap2DAsset? tilemap { get; set; }

    /// <summary>Gets or sets an optional material implementing the 2D sprite contract.</summary>
    [SerializableProperty]
    [Header("Appearance", "These values are shared by every visible tile in this renderer.")]
    public MaterialAsset? material { get; set; }

    /// <summary>Gets or sets a linear tint multiplied with tile and cell colors.</summary>
    [SerializableProperty]
    public Color color { get; set; } = Color.WHITE;

    /// <summary>Gets or sets the stable project-local sorting-layer name.</summary>
    [SerializableProperty]
    [Header("Ordering", "Tilemap layer order is added to this stable base order.")]
    public string sortingLayer { get; set; } = "default";

    /// <summary>Gets or sets base order before each cell's tilemap layer is applied.</summary>
    [SerializableProperty]
    public int orderInLayer { get; set; }

    /// <summary>Gets or sets blend semantics resolved to an open material pass role.</summary>
    [SerializableProperty]
    public SpriteBlendMode2D blendMode { get; set; } = SpriteBlendMode2D.Alpha;

    /// <summary>Gets or sets texture filtering and addressing.</summary>
    [SerializableProperty]
    public SpriteSamplingMode2D sampling { get; set; } = SpriteSamplingMode2D.PointClamp;

    /// <summary>Gets or sets whether tiles sample the matching HDR 2D light buffers.</summary>
    [SerializableProperty]
    [Header("Lighting", "Enable lighting to expose shared normal and emission maps.")]
    public bool receiveLighting { get; set; }

    /// <summary>Gets or sets an optional tangent-space normal map shared by visible tiles.</summary>
    [SerializableProperty]
    [Header("Light Response", "The four low bits select the accepted Light2D blend styles.")]
    [ShowIf(nameof(receiveLighting))]
    public TextureAsset? normalMap { get; set; }

    /// <summary>Gets or sets an optional linear emission map shared by visible tiles.</summary>
    [SerializableProperty]
    [ShowIf(nameof(receiveLighting))]
    public TextureAsset? emissionMap { get; set; }

    /// <summary>Gets or sets linear emission multiplied with the optional emission map.</summary>
    [SerializableProperty]
    [ShowIf(nameof(receiveLighting))]
    public Color emissionColor { get; set; } = Color.BLACK;

    /// <summary>Gets or sets the accepted light blend-style bits.</summary>
    [SerializableProperty]
    [ShowIf(nameof(receiveLighting))]
    public byte lightBlendStyles
    {
        get => m_lightBlendStyles;
        set => m_lightBlendStyles = (byte)(value & 0x0f);
    }
}
