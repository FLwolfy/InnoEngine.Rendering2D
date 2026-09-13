using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEditor.Annotations;

namespace Inno.Rendering2D;

/// <summary>Contributes a freeform polygon to the 2D light shadow-occluder set.</summary>
[StableTypeId("5c9d90f7-9b65-4a67-97a9-512ca5c114f8")]
public sealed class ShadowCaster2D : GameBehavior
{
    private Vector2[] m_shape = [];
    private byte m_lightBlendStyles = 0x0f;

    /// <summary>Gets or sets the local-space closed occluder polygon.</summary>
    [SerializableProperty]
    [Header("Occluder", "Author a closed local-space polygon with at least three non-collinear points.")]
    public Vector2[] shape
    {
        get => m_shape;
        set => m_shape = value ?? [];
    }

    /// <summary>Gets or sets whether the polygon also blocks light inside its boundary.</summary>
    [SerializableProperty]
    public bool selfShadows { get; set; }

    /// <summary>Gets or sets the light blend-style bits affected by this caster.</summary>
    [SerializableProperty]
    [Header("Light Styles", "The four low bits select which Light2D blend styles this caster can occlude.")]
    public byte lightBlendStyles
    {
        get => m_lightBlendStyles;
        set => m_lightBlendStyles = (byte)(value & 0x0f);
    }
}
