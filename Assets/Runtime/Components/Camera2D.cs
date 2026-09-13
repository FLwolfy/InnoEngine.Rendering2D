using System;
using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEditor.Annotations;

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
    private bool m_primary = true;
    private CameraComposition2D m_composition = CameraComposition2D.Base;
    private bool m_clearTarget = true;
    private float m_orthographicSize = 5f;
    private Vector2 m_referenceResolution = new(1920f, 1080f);

    /// <summary>Gets or sets whether this camera starts or extends a composited camera stack.</summary>
    [SerializableProperty]
    [Header("Composition", "Base cameras start a stack; Overlay cameras extend the matching stack.")]
    [Tooltip("Base starts a camera stack. Overlay preserves and extends a base camera with the same Stack ID.")]
    public CameraComposition2D composition
    {
        get => m_composition;
        set
        {
            m_composition = value;
            if (value == CameraComposition2D.Overlay)
            {
                m_primary = false;
                m_clearTarget = false;
            }
        }
    }

    /// <summary>Gets or sets the stable stack identity used to associate overlay cameras with one base.</summary>
    [SerializableProperty]
    [InspectorName("Stack ID")]
    [Tooltip("Stable identity used to associate overlay cameras with one base camera.")]
    public string stackId { get; set; } = "default";

    /// <summary>Gets or sets whether this camera is preferred by automatic and Editor game views.</summary>
    [SerializableProperty]
    [Header("Output", "A primary backbuffer camera is selected automatically by Game View and Player.")]
    [ShowIf(nameof(composition), CameraComposition2D.Base)]
    [Tooltip("Prefer this base camera when Game View or Player selects an automatic camera.")]
    public bool primary
    {
        get => m_primary;
        set => m_primary = value && composition == CameraComposition2D.Base;
    }

    /// <summary>Gets or sets whether the automatic request provider targets the main backbuffer.</summary>
    [SerializableProperty]
    [ShowIf(nameof(composition), CameraComposition2D.Base)]
    [InspectorName("Render To Backbuffer")]
    public bool renderToBackbuffer { get; set; } = true;

    /// <summary>Gets or sets whether a base camera clears its target before drawing.</summary>
    [SerializableProperty]
    [ShowIf(nameof(composition), CameraComposition2D.Base)]
    public bool clearTarget
    {
        get => m_clearTarget;
        set => m_clearTarget = value && composition == CameraComposition2D.Base;
    }

    /// <summary>Gets or sets the linear target clear color.</summary>
    [SerializableProperty]
    [ShowIf(nameof(composition), CameraComposition2D.Base)]
    [ShowIf(nameof(clearTarget))]
    public Color clearColor { get; set; } = Color.DARKGRAY;

    /// <summary>Gets or sets the vertical half-size of the orthographic view in world units.</summary>
    [SerializableProperty]
    [Header("Lens & Visibility", "Orthographic Size is the vertical half-extent in world units.")]
    [InspectorName("Orthographic Size")]
    [Tooltip("Positive vertical half-size of the camera view in world units.")]
    public float orthographicSize
    {
        get => m_orthographicSize;
        set => m_orthographicSize = float.IsFinite(value) && value > 0f ? value : 0.0001f;
    }

    /// <summary>Gets or sets normalized destination viewport bounds.</summary>
    [SerializableProperty]
    [Tooltip("Normalized destination rectangle within the camera target.")]
    public Rect viewport { get; set; } = new(0f, 0f, 1f, 1f);

    /// <summary>Gets or sets the accepted GameObject layers.</summary>
    [SerializableProperty]
    public GameLayerMask cullingMask { get; set; } = GameLayerMask.everything;

    /// <summary>Gets or sets ascending request priority.</summary>
    [SerializableProperty]
    public int priority { get; set; }

    /// <summary>
    /// Gets or sets whether camera translation and vertical extent use the project-wide 2D pixel density.
    /// </summary>
    [SerializableProperty]
    [Header("Pixel Perfect", "Reference resolution controls camera snapping and optional integer presentation scaling.")]
    public bool pixelPerfect { get; set; }

    /// <summary>Gets or sets whether Pixel Perfect rendering uses an integer presentation scale.</summary>
    [SerializableProperty]
    [Header("Pixel Grid", "Use integer scaling for crisp pixel art without uneven texel sizes.")]
    [ShowIf(nameof(pixelPerfect))]
    public bool integerScale { get; set; } = true;

    /// <summary>Gets or sets the positive reference resolution used by Pixel Perfect rendering.</summary>
    [SerializableProperty]
    [ShowIf(nameof(pixelPerfect))]
    public Vector2 referenceResolution
    {
        get => m_referenceResolution;
        set => m_referenceResolution = new Vector2(
            float.IsFinite(value.x) && value.x > 0f ? value.x : 1f,
            float.IsFinite(value.y) && value.y > 0f ? value.y : 1f);
    }

    /// <summary>Gets or sets an optional post-process profile applied after this camera stack.</summary>
    [SerializableProperty]
    [Header("Post Processing", "The profile is evaluated after this camera stack is composed.")]
    [InspectorName("Post Process")]
    public PostProcessProfile2DAsset? postProcess { get; set; }
}
