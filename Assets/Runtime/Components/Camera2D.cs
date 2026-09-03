using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Defines whether a 2D camera starts or extends a composited camera stack.</summary>
public enum CameraComposition2D
{
    /// <summary>Starts a stack and optionally clears its target.</summary>
    Base,

    /// <summary>Loads the selected base result and draws additional content over it.</summary>
    Overlay
}

/// <summary>Defines one orthographic 2D render view without modifying the engine's scene model.</summary>
[StableTypeId("fc1efc8e-54be-4233-83a7-bab21d520f64")]
public sealed class Camera2D : GameBehavior
{
    /// <summary>Gets or sets whether this camera is preferred by automatic and Editor game views.</summary>
    [SerializableProperty]
    public bool primary { get; set; } = true;

    /// <summary>Gets or sets whether the automatic request provider targets the main backbuffer.</summary>
    [SerializableProperty]
    public bool renderToBackbuffer { get; set; } = true;

    /// <summary>Gets or sets whether this camera starts or extends a composited camera stack.</summary>
    [SerializableProperty]
    public CameraComposition2D composition { get; set; } = CameraComposition2D.Base;

    /// <summary>Gets or sets the stable stack identity used to associate overlay cameras with one base.</summary>
    [SerializableProperty]
    public string stackId { get; set; } = "default";

    /// <summary>Gets or sets whether a base camera clears its target before drawing.</summary>
    [SerializableProperty]
    public bool clearTarget { get; set; } = true;

    /// <summary>Gets or sets the linear target clear color.</summary>
    [SerializableProperty]
    public Color clearColor { get; set; } = Color.DARKGRAY;

    /// <summary>Gets or sets the vertical half-size of the orthographic view in world units.</summary>
    [SerializableProperty]
    public float orthographicSize { get; set; } = 5f;

    /// <summary>
    /// Gets or sets whether camera translation and vertical extent use the project-wide 2D pixel density.
    /// </summary>
    [SerializableProperty]
    public bool pixelPerfect { get; set; }

    /// <summary>Gets or sets the accepted GameObject layers.</summary>
    [SerializableProperty]
    public GameLayerMask cullingMask { get; set; } = GameLayerMask.everything;

    /// <summary>Gets or sets ascending request priority.</summary>
    [SerializableProperty]
    public int priority { get; set; }
}
