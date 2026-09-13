using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEditor.Annotations;

namespace Inno.Rendering2D;

/// <summary>Groups descendant 2D drawables into one deterministic painter-order interval.</summary>
[StableTypeId("a4d9d84e-41c3-4b17-8e1f-a63568a89889")]
public sealed class SortingGroup2D : GameBehavior
{
    /// <summary>Gets or sets the stable project-local sorting-layer name.</summary>
    [SerializableProperty]
    [Header("Group Order", "Descendant 2D drawables remain together in one deterministic painter-order interval.")]
    public string sortingLayer { get; set; } = "default";

    /// <summary>Gets or sets group order within the selected sorting layer.</summary>
    [SerializableProperty]
    public int orderInLayer { get; set; }
}
