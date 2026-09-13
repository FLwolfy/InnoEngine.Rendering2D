using System;
using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Rendering;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEditor.Annotations;

namespace Inno.Rendering2D;

/// <summary>Writes one sprite-shaped stencil mask for ordered 2D drawables.</summary>
[StableTypeId("e00a7951-61b4-4cbd-bc57-5b2b51598a7d")]
public sealed class SpriteMask2D : GameBehavior
{
    private int m_frontOrder = int.MinValue;
    private int m_backOrder = int.MaxValue;
    private float m_alphaCutoff = 0.01f;
    private float m_boundsPadding;

    /// <summary>Gets or sets the stable mask sprite.</summary>
    [SerializableProperty]
    [Header("Mask Shape", "Choose a sprite alpha shape or a procedural fallback.")]
    public SpriteReference2D sprite { get; set; }

    /// <summary>Gets or sets the procedural fallback used when no sprite is assigned.</summary>
    [SerializableProperty]
    [ShowIf(nameof(sprite), InspectorCondition.NotAssigned)]
    public SpritePrimitive2D primitive { get; set; } = SpritePrimitive2D.Square;

    /// <summary>Gets or sets the Sprite material whose vertex and alpha coverage also write this mask; null inherits the Pipeline default.</summary>
    [SerializableProperty]
    [Header("Coverage Material", "Use the visible Sprite's Material to share its deformation and alpha clipping. Unassigned uses the Pipeline default.")]
    public MaterialAsset? material { get; set; }

    /// <summary>Gets or sets local world size; non-positive components use source pixel size.</summary>
    [SerializableProperty]
    [Header("Geometry", "The cutoff controls which source alpha values write stencil.")]
    public Vector2 size { get; set; } = Vector2.ZERO;

    /// <summary>Gets or sets the nonnegative world-space XY padding enclosing the coverage material's vertex displacement.</summary>
    [SerializableProperty]
    [Tooltip("Maximum world-space Vertex Offset displacement, matching the visible Sprite's Bounds Padding when sharing its Material.")]
    public float boundsPadding
    {
        get => m_boundsPadding;
        set => m_boundsPadding = float.IsFinite(value) ? MathF.Max(0f, value) : 0f;
    }

    /// <summary>Gets or sets the normalized local pivot used by standalone texture masks.</summary>
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

    /// <summary>Gets or sets texture filtering used while evaluating the mask alpha.</summary>
    [SerializableProperty]
    public SpriteSamplingMode2D sampling { get; set; } = SpriteSamplingMode2D.PointClamp;

    /// <summary>Gets or sets normalized alpha cutoff used while writing stencil.</summary>
    [SerializableProperty]
    [Range(0, 1)]
    public float alphaCutoff
    {
        get => m_alphaCutoff;
        set => m_alphaCutoff = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0.01f;
    }

    /// <summary>Gets or sets the stable sorting layer whose sprites can observe this mask.</summary>
    [SerializableProperty]
    [Header("Sorting Range", "Only drawables in this inclusive layer and order interval observe the mask.")]
    public string sortingLayer { get; set; } = "default";

    /// <summary>Gets or sets the inclusive beginning of the mask sorting interval.</summary>
    [SerializableProperty]
    public int frontOrder
    {
        get => m_frontOrder;
        set
        {
            m_frontOrder = value;
            if (m_backOrder < value)
                m_backOrder = value;
        }
    }

    /// <summary>Gets or sets the inclusive end of the mask sorting interval.</summary>
    [SerializableProperty]
    public int backOrder
    {
        get => m_backOrder;
        set
        {
            m_backOrder = value;
            if (m_frontOrder > value)
                m_frontOrder = value;
        }
    }

}
