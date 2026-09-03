using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Rendering;
using InnoEngine.Scene;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Selects fixed-function blending for a 2D drawable.</summary>
public enum SpriteBlendMode2D
{
    /// <summary>Uses conventional straight-alpha blending.</summary>
    Alpha,
    /// <summary>Uses premultiplied-alpha blending.</summary>
    Premultiplied,
    /// <summary>Adds source contribution to the destination.</summary>
    Additive,
    /// <summary>Multiplies the destination by source color.</summary>
    Multiply,
    /// <summary>Replaces destination pixels without blending.</summary>
    Opaque
}

/// <summary>Selects how sprite geometry fills its requested local size.</summary>
public enum SpriteDrawMode2D
{
    /// <summary>Draws one quad, including atlas trim information.</summary>
    Simple,
    /// <summary>Draws a nine-sliced sprite using pixel borders.</summary>
    Sliced,
    /// <summary>Repeats the sprite region and clips partial edge tiles.</summary>
    Tiled
}

/// <summary>Selects filtering and addressing for sprite texture samples.</summary>
public enum SpriteSamplingMode2D
{
    /// <summary>Uses nearest-neighbor filtering and clamps to region edges.</summary>
    PointClamp,
    /// <summary>Uses linear filtering and clamps to region edges.</summary>
    LinearClamp,
    /// <summary>Uses nearest-neighbor filtering with repeated addressing.</summary>
    PointRepeat,
    /// <summary>Uses linear filtering with repeated addressing.</summary>
    LinearRepeat
}

/// <summary>Selects a procedural shape used when a sprite has no texture or atlas region.</summary>
public enum SpritePrimitive2D
{
    /// <summary>Requires a texture or atlas region and emits no procedural fallback.</summary>
    None,
    /// <summary>Draws a filled unit square.</summary>
    Square,
    /// <summary>Draws an antialiased filled circle inside the sprite bounds.</summary>
    Circle,
    /// <summary>Draws an antialiased upward-facing triangle inside the sprite bounds.</summary>
    Triangle,
    /// <summary>Draws an antialiased vertical capsule inside the sprite bounds.</summary>
    Capsule
}

/// <summary>Renders one texture or atlas region through an open 2D material contract.</summary>
[StableTypeId("c4a4e191-4b28-4c62-9890-fe921d5ac8f5")]
public sealed class SpriteRenderer2D : GameBehavior
{
    /// <summary>Gets or sets a direct source texture used when no atlas is assigned.</summary>
    [SerializableProperty]
    public TextureAsset? texture { get; set; }

    /// <summary>Gets or sets an optional atlas that owns the sampled texture and region metadata.</summary>
    [SerializableProperty]
    public SpriteAtlas2DAsset? atlas { get; set; }

    /// <summary>Gets or sets the stable region ID used when an atlas is assigned.</summary>
    [SerializableProperty]
    public string spriteId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the procedural fallback used when neither a valid atlas region nor texture is assigned.
    /// </summary>
    [SerializableProperty]
    public SpritePrimitive2D primitive { get; set; } = SpritePrimitive2D.Square;

    /// <summary>Gets or sets an optional material implementing the 2D sprite contract.</summary>
    [SerializableProperty]
    public MaterialAsset? material { get; set; }

    /// <summary>Gets or sets the linear vertex tint.</summary>
    [SerializableProperty]
    public Color color { get; set; } = Color.WHITE;

    /// <summary>Gets or sets local world size; non-positive components use source pixel size.</summary>
    [SerializableProperty]
    public Vector2 size { get; set; } = Vector2.ZERO;

    /// <summary>Gets or sets normalized local pivot when no atlas region overrides it.</summary>
    [SerializableProperty]
    public Vector2 pivot { get; set; } = new(0.5f, 0.5f);

    /// <summary>Gets or sets source pixels represented by one world unit; non-positive values use project settings.</summary>
    [SerializableProperty]
    public float pixelsPerUnit { get; set; }

    /// <summary>Gets or sets horizontal texture and geometry mirroring.</summary>
    [SerializableProperty]
    public bool flipX { get; set; }

    /// <summary>Gets or sets vertical texture and geometry mirroring.</summary>
    [SerializableProperty]
    public bool flipY { get; set; }

    /// <summary>Gets or sets the stable project sorting-layer identity.</summary>
    [SerializableProperty]
    public string sortingLayerId { get; set; } = "inno.rendering.2d.default";

    /// <summary>Gets or sets order within the selected sorting layer.</summary>
    [SerializableProperty]
    public int orderInLayer { get; set; }

    /// <summary>Gets or sets blend semantics resolved to an open material pass role.</summary>
    [SerializableProperty]
    public SpriteBlendMode2D blendMode { get; set; } = SpriteBlendMode2D.Alpha;

    /// <summary>Gets or sets geometry generation mode.</summary>
    [SerializableProperty]
    public SpriteDrawMode2D drawMode { get; set; } = SpriteDrawMode2D.Simple;

    /// <summary>Gets or sets texture filtering and addressing.</summary>
    [SerializableProperty]
    public SpriteSamplingMode2D sampling { get; set; } = SpriteSamplingMode2D.PointClamp;

    /// <summary>Gets or sets whether 2D lights modulate vertex color.</summary>
    [SerializableProperty]
    public bool receiveLighting { get; set; }
}
