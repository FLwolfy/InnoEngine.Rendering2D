using System;
using InnoEngine.Assets;
using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Rendering;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEditor.Annotations;

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

/// <summary>Selects how a drawable interacts with active 2D sprite masks.</summary>
public enum SpriteMaskInteraction2D
{
    /// <summary>Ignores sprite masks.</summary>
    None,
    /// <summary>Draws only inside accepted sprite masks.</summary>
    VisibleInside,
    /// <summary>Draws only outside accepted sprite masks.</summary>
    VisibleOutside
}

/// <summary>Renders one texture or atlas region through an open 2D material contract.</summary>
[StableTypeId("c4a4e191-4b28-4c62-9890-fe921d5ac8f5")]
public sealed class SpriteRenderer2D : GameBehavior
{
    private SpriteReference2D m_crossFadeSprite;
    private float m_crossFadeWeight;
    private byte m_lightBlendStyles = 0x0f;
    private float m_boundsPadding;

    /// <summary>Allows pointer input to reach content drawn behind this sprite when enabled.</summary>
    [SerializableProperty]
    [Header("Interaction", "Controls scene pointer occlusion without changing the visible sprite.")]
    public bool pointerPassThrough { get; set; }

    /// <summary>Gets or sets the stable atlas-region or standalone-texture reference.</summary>
    [SerializableProperty]
    [Header("Visual", "Choose an atlas region, standalone texture, or a procedural shape.")]
    [Tooltip("Stable reference to an atlas region or standalone texture.")]
    public SpriteReference2D sprite { get; set; }

    /// <summary>
    /// Gets or sets the procedural fallback used when neither a valid atlas region nor texture is assigned.
    /// </summary>
    [SerializableProperty]
    [Header("Geometry", "No sprite is assigned, so one simple procedural shape is rendered.")]
    [ShowIf(nameof(sprite), InspectorCondition.NotAssigned)]
    [HelpBox(
        "Assign a Sprite to enable atlas geometry, borders, pixel density, and sampling controls.",
        nameof(sprite),
        InspectorCondition.NotAssigned)]
    public SpritePrimitive2D primitive { get; set; } = SpritePrimitive2D.Square;

    /// <summary>Gets or sets geometry generation mode.</summary>
    [SerializableProperty]
    [Header("Geometry", "Sliced and tiled modes use the selected sprite region's authored borders.")]
    [ShowIf(nameof(sprite), InspectorCondition.Assigned)]
    public SpriteDrawMode2D drawMode { get; set; } = SpriteDrawMode2D.Simple;

    /// <summary>Gets or sets local world size; non-positive components use source pixel size.</summary>
    [SerializableProperty]
    [Tooltip("Zero or negative components use the selected source's natural pixel size.")]
    public Vector2 size { get; set; } = Vector2.ZERO;

    /// <summary>Gets or sets the nonnegative world-space XY padding used to conservatively cull shader-deformed geometry.</summary>
    [SerializableProperty]
    [Tooltip("Maximum world-space displacement of your Shader's Vertex Offset. Expand this when the Shader moves vertices beyond the original geometry; it does not change rendered size.")]
    public float boundsPadding
    {
        get => m_boundsPadding;
        set => m_boundsPadding = float.IsFinite(value) ? MathF.Max(0f, value) : 0f;
    }

    /// <summary>Gets or sets normalized local pivot when no atlas region overrides it.</summary>
    [SerializableProperty]
    public Vector2 pivot { get; set; } = new(0.5f, 0.5f);

    /// <summary>Gets or sets source pixels represented by one world unit; non-positive values use project settings.</summary>
    [SerializableProperty]
    [ShowIf(nameof(sprite), InspectorCondition.Assigned)]
    public float pixelsPerUnit { get; set; }

    /// <summary>Gets or sets horizontal texture and geometry mirroring.</summary>
    [SerializableProperty]
    public bool flipX { get; set; }

    /// <summary>Gets or sets vertical texture and geometry mirroring.</summary>
    [SerializableProperty]
    public bool flipY { get; set; }

    /// <summary>Gets or sets texture filtering and addressing.</summary>
    [SerializableProperty]
    [ShowIf(nameof(sprite), InspectorCondition.Assigned)]
    public SpriteSamplingMode2D sampling { get; set; } = SpriteSamplingMode2D.PointClamp;

    /// <summary>Gets or sets the explicitly assigned material implementing the 2D sprite contract.</summary>
    [SerializableProperty]
    [Header("Appearance", "Tint and the explicitly assigned Material are applied before blending with the camera target.")]
    public MaterialAsset? material { get; set; } = LoadDefaultMaterial();

    /// <summary>Gets or sets the linear vertex tint.</summary>
    [SerializableProperty]
    public Color color { get; set; } = Color.WHITE;

    /// <summary>Gets or sets blend semantics resolved to an open material pass role.</summary>
    [SerializableProperty]
    [InspectorName("Blend Mode")]
    public SpriteBlendMode2D blendMode { get; set; } = SpriteBlendMode2D.Alpha;

    /// <summary>Gets or sets the stable project-local sorting-layer name.</summary>
    [SerializableProperty]
    [Header("Ordering & Mask", "Painter order stays deterministic within a sorting layer.")]
    public string sortingLayer { get; set; } = "default";

    /// <summary>Gets or sets order within the selected sorting layer.</summary>
    [SerializableProperty]
    public int orderInLayer { get; set; }

    /// <summary>Gets or sets how this sprite is clipped by active sprite masks.</summary>
    [SerializableProperty]
    public SpriteMaskInteraction2D maskInteraction { get; set; }

    /// <summary>Gets or sets whether the sprite samples the matching HDR 2D light buffers.</summary>
    [SerializableProperty]
    [Header("Lighting", "Enable lighting to reveal normal, emission, and blend-style controls.")]
    public bool receiveLighting { get; set; }

    /// <summary>Gets or sets an optional tangent-space normal map.</summary>
    [SerializableProperty]
    [Header("Light Response", "The four low bits select which Light2D blend styles affect this sprite.")]
    [ShowIf(nameof(receiveLighting))]
    public TextureAsset? normalMap { get; set; }

    /// <summary>Gets or sets an optional linear emission map.</summary>
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

    /// <summary>Gets the transient source sprite retained while an animator cross-fades into <see cref="sprite"/>.</summary>
    public SpriteReference2D crossFadeSprite => m_crossFadeSprite;

    /// <summary>Gets the transient opacity of <see cref="crossFadeSprite"/> in the range zero through one.</summary>
    public float crossFadeWeight => m_crossFadeWeight;

    internal void SetCrossFade(SpriteReference2D source, float weight)
    {
        m_crossFadeSprite = source;
        m_crossFadeWeight = Math.Clamp(weight, 0f, 1f);
        if (m_crossFadeWeight <= 0f)
            m_crossFadeSprite = default;
    }

    private static MaterialAsset LoadDefaultMaterial()
        => Assets.Load<MaterialAsset>(Assets.LocalPath(Rendering2DIds.defaultSpriteMaterialPath));
}
