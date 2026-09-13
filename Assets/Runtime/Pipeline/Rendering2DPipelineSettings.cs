using InnoEngine.Reflection;
using InnoEngine.Rendering;
using InnoEngine.Serialization;
using InnoEditor.Annotations;

namespace Inno.Rendering2D;

/// <summary>Stores persistent 2D resource choices inside the configured Pipeline asset.</summary>
[StableTypeId("d14002ca-3db2-4f1f-9db6-f3e1379f3d29")]
public sealed class Rendering2DPipelineSettings : ISerializable
{
    /// <summary>Gets or sets the material used only by objects without an explicit material.</summary>
    [Header("Sprite Defaults", description: "Used only by objects without an explicitly assigned Material.")]
    [SerializableProperty]
    public MaterialAsset? defaultSpriteMaterial { get; set; }

    /// <summary>Gets or sets the independent MRT light accumulation program.</summary>
    [Header("Lighting and Shadows", description: "Pipeline-owned programs. Ordinary Sprite Materials do not own these passes.")]
    [SerializableProperty]
    public ShaderAsset? lightAccumulation { get; set; }

    /// <summary>Gets or sets the independent shadow volume and stencil clear program.</summary>
    [SerializableProperty]
    public ShaderAsset? shadowStencil { get; set; }

    /// <summary>Gets or sets the first-level threshold extraction program.</summary>
    [Header("Bloom", description: "One program per operation, reused across all pyramid levels.")]
    [SerializableProperty]
    public ShaderAsset? bloomPrefilter { get; set; }

    /// <summary>Gets or sets the program reused by subsequent Bloom reduction levels.</summary>
    [SerializableProperty]
    public ShaderAsset? bloomDownsample { get; set; }

    /// <summary>Gets or sets the program reused by Bloom reconstruction levels.</summary>
    [SerializableProperty]
    public ShaderAsset? bloomUpsample { get; set; }

    /// <summary>Gets or sets the final color adjustment and output composition program.</summary>
    [Header("Final Output", description: "Color adjustments and final composition.")]
    [SerializableProperty]
    public ShaderAsset? finalComposite { get; set; }
}
