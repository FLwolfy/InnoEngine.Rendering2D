using System;
using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Rendering;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEditor.Annotations;

namespace Inno.Rendering2D;

/// <summary>Selects the analytical shape rasterized into the screen-space 2D light buffers.</summary>
public enum LightKind2D
{
    /// <summary>Applies constant illumination to every accepted layer.</summary>
    Global,
    /// <summary>Applies radial attenuation from the light transform.</summary>
    Point,
    /// <summary>Applies radial and angular attenuation from the light transform.</summary>
    Spot,
    /// <summary>Applies illumination inside an authored freeform polygon.</summary>
    Freeform
}

/// <summary>Selects one of four independent 2D light accumulation styles.</summary>
public enum LightBlendStyle2D
{
    /// <summary>Uses the first light accumulation channel.</summary>
    Style0,
    /// <summary>Uses the second light accumulation channel.</summary>
    Style1,
    /// <summary>Uses the third light accumulation channel.</summary>
    Style2,
    /// <summary>Uses the fourth light accumulation channel.</summary>
    Style3
}

/// <summary>Contributes HDR GPU lighting, optional cookies, normal response, and soft shadows to 2D drawables.</summary>
[StableTypeId("2d51f3c8-af12-4687-a1d6-a01353d153ba")]
public sealed class Light2D : GameBehavior
{
    private LightKind2D m_kind = LightKind2D.Point;
    private float m_intensity = 1f;
    private float m_range = 5f;
    private float m_spotAngle = 60f;
    private float m_falloff = 2f;
    private Vector2[] m_shape = [];
    private bool m_castShadows;
    private float m_shadowSoftness = 0.15f;
    private float m_normalIntensity = 1f;

    /// <summary>Gets or sets the analytical light shape.</summary>
    [SerializableProperty]
    [Header("Light", "Shape, HDR intensity, color, and blend style define the accumulated light.")]
    [HelpBox(
        "Global lights affect the complete accepted culling mask and do not use range, cookie, or shadow controls.",
        nameof(kind),
        LightKind2D.Global)]
    public LightKind2D kind
    {
        get => m_kind;
        set
        {
            m_kind = value;
            if (value == LightKind2D.Global)
                m_castShadows = false;
        }
    }

    /// <summary>Gets or sets linear light color.</summary>
    [SerializableProperty]
    public Color color { get; set; } = Color.WHITE;

    /// <summary>Gets or sets non-negative light intensity.</summary>
    [SerializableProperty]
    [Tooltip("Linear HDR multiplier. Values above one intentionally exceed display white.")]
    public float intensity
    {
        get => m_intensity;
        set => m_intensity = float.IsFinite(value) ? MathF.Max(0f, value) : 0f;
    }

    /// <summary>Gets or sets the selected light accumulation style.</summary>
    [SerializableProperty]
    public LightBlendStyle2D blendStyle { get; set; }

    /// <summary>Gets or sets point and spot range in world units.</summary>
    [SerializableProperty]
    [Header("Influence", "Range and falloff control spatial attenuation.")]
    [HideIf(nameof(kind), LightKind2D.Global)]
    public float range
    {
        get => m_range;
        set => m_range = float.IsFinite(value) ? MathF.Max(0.0001f, value) : 0.0001f;
    }

    /// <summary>Gets or sets attenuation exponent.</summary>
    [SerializableProperty]
    [HideIf(nameof(kind), LightKind2D.Global)]
    public float falloff
    {
        get => m_falloff;
        set => m_falloff = float.IsFinite(value) ? MathF.Max(0.01f, value) : 2f;
    }

    /// <summary>Gets or sets an optional cookie sampled in light-local space.</summary>
    [SerializableProperty]
    [HideIf(nameof(kind), LightKind2D.Global)]
    public TextureAsset? cookie { get; set; }

    /// <summary>Gets or sets full spot cone angle in degrees.</summary>
    [SerializableProperty]
    [ShowIf(nameof(kind), LightKind2D.Spot)]
    [Range(0.1, 179.9)]
    public float spotAngle
    {
        get => m_spotAngle;
        set => m_spotAngle = float.IsFinite(value) ? Math.Clamp(value, 0.1f, 179.9f) : 60f;
    }

    /// <summary>Gets or sets the normalized freeform polygon for <see cref="LightKind2D.Freeform"/>.</summary>
    [SerializableProperty]
    [ShowIf(nameof(kind), LightKind2D.Freeform)]
    public Vector2[] shape
    {
        get => m_shape;
        set => m_shape = value ?? [];
    }

    /// <summary>Gets or sets whether matching shadow casters occlude this light.</summary>
    [SerializableProperty]
    [Header("Shadows", "ShadowCaster2D polygons are evaluated only when shadow casting is enabled.")]
    [HideIf(nameof(kind), LightKind2D.Global)]
    public bool castShadows
    {
        get => m_castShadows;
        set => m_castShadows = value && kind != LightKind2D.Global;
    }

    /// <summary>Gets or sets normalized shadow-edge softness.</summary>
    [SerializableProperty]
    [HideIf(nameof(kind), LightKind2D.Global)]
    [ShowIf(nameof(castShadows))]
    [Range(0, 1)]
    public float shadowSoftness
    {
        get => m_shadowSoftness;
        set => m_shadowSoftness = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
    }

    /// <summary>Gets or sets how strongly sprite normal maps influence this light.</summary>
    [SerializableProperty]
    [Header("Filtering", "Normal intensity scales the response of sprite normal maps.")]
    public float normalIntensity
    {
        get => m_normalIntensity;
        set => m_normalIntensity = float.IsFinite(value) ? MathF.Max(0f, value) : 0f;
    }

    /// <summary>Gets or sets GameObject layers illuminated by this light.</summary>
    [SerializableProperty]
    public GameLayerMask cullingMask { get; set; } = GameLayerMask.everything;
}
