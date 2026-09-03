using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Selects the analytical shape used by a CPU-evaluated 2D light.</summary>
public enum LightKind2D
{
    /// <summary>Applies constant illumination to every accepted layer.</summary>
    Global,
    /// <summary>Applies radial attenuation from the light transform.</summary>
    Point,
    /// <summary>Applies radial and angular attenuation from the light transform.</summary>
    Spot
}

/// <summary>Contributes capability-independent vertex lighting to 2D drawables.</summary>
[StableTypeId("2d51f3c8-af12-4687-a1d6-a01353d153ba")]
public sealed class Light2D : GameBehavior
{
    /// <summary>Gets or sets the analytical light shape.</summary>
    [SerializableProperty]
    public LightKind2D kind { get; set; } = LightKind2D.Point;

    /// <summary>Gets or sets linear light color.</summary>
    [SerializableProperty]
    public Color color { get; set; } = Color.WHITE;

    /// <summary>Gets or sets non-negative light intensity.</summary>
    [SerializableProperty]
    public float intensity { get; set; } = 1f;

    /// <summary>Gets or sets point and spot range in world units.</summary>
    [SerializableProperty]
    public float range { get; set; } = 5f;

    /// <summary>Gets or sets full spot cone angle in degrees.</summary>
    [SerializableProperty]
    public float spotAngle { get; set; } = 60f;

    /// <summary>Gets or sets attenuation exponent.</summary>
    [SerializableProperty]
    public float falloff { get; set; } = 2f;

    /// <summary>Gets or sets GameObject layers illuminated by this light.</summary>
    [SerializableProperty]
    public GameLayerMask cullingMask { get; set; } = GameLayerMask.everything;
}
