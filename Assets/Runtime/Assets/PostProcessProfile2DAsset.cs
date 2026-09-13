using InnoEngine.Assets;
using InnoEngine.Reflection;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Stores camera-stack post-process settings without prescribing a rendering backend.</summary>
[StableTypeId("8d3fcd93-0cd6-4ac7-b560-52ed6a10f2cb")]
public sealed class PostProcessProfile2DAsset : AssetObject
{
    /// <summary>Gets or sets exposure in photographic stops.</summary>
    [SerializableProperty]
    public float exposure { get; set; }

    /// <summary>Gets or sets positive contrast where one preserves the source.</summary>
    [SerializableProperty]
    public float contrast { get; set; } = 1f;

    /// <summary>Gets or sets non-negative saturation where one preserves the source.</summary>
    [SerializableProperty]
    public float saturation { get; set; } = 1f;

    /// <summary>Gets or sets whether filmic tone mapping is enabled.</summary>
    [SerializableProperty]
    public bool toneMapping { get; set; } = true;

    /// <summary>Gets or sets non-negative bloom intensity.</summary>
    [SerializableProperty]
    public float bloomIntensity { get; set; }

    /// <summary>Gets or sets the linear brightness threshold for bloom.</summary>
    [SerializableProperty]
    public float bloomThreshold { get; set; } = 1f;

    /// <summary>Gets or sets the number of half-resolution bloom levels from one through eight.</summary>
    [SerializableProperty]
    public int bloomLevels { get; set; } = 5;

    /// <summary>Gets or sets how strongly lower-frequency bloom spreads into the next finer level.</summary>
    [SerializableProperty]
    public float bloomScatter { get; set; } = 0.7f;

    /// <summary>Gets or sets normalized vignette intensity.</summary>
    [SerializableProperty]
    public float vignette { get; set; }

    /// <summary>Gets or sets the positive screen-pixel block size used by pixelation.</summary>
    [SerializableProperty]
    public int pixelation { get; set; } = 1;
}
