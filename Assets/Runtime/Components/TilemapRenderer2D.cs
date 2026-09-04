using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Rendering;
using InnoEngine.Scene;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Renders visible sparse tilemap cells through the same material and batching contract as sprites.</summary>
[StableTypeId("2fd89229-439b-4f23-aa44-355723c4da35")]
public sealed class TilemapRenderer2D : GameBehavior
{
    /// <summary>Gets or sets sparse tilemap content.</summary>
    [SerializableProperty]
    public Tilemap2DAsset? tilemap { get; set; }

    /// <summary>Gets or sets an optional material implementing the 2D sprite contract.</summary>
    [SerializableProperty]
    public MaterialAsset? material { get; set; }

    /// <summary>Gets or sets a linear tint multiplied with tile and cell colors.</summary>
    [SerializableProperty]
    public Color color { get; set; } = Color.WHITE;

    /// <summary>Gets or sets the stable project-local sorting-layer name.</summary>
    [SerializableProperty]
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

    /// <summary>Gets or sets whether 2D lights modulate vertex color.</summary>
    [SerializableProperty]
    public bool receiveLighting { get; set; }
}
