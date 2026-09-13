using System;
using System.Linq;
using InnoEngine.Assets;
using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Rendering;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Selects the local emission volume used by a 2D particle effect.</summary>
public enum ParticleEmitterShape2D
{
    /// <summary>Emits from the object origin.</summary>
    Point,
    /// <summary>Emits from a filled axis-aligned rectangle.</summary>
    Box,
    /// <summary>Emits from a filled circle.</summary>
    Circle,
    /// <summary>Emits inside an upward-facing cone.</summary>
    Cone
}

/// <summary>Selects whether simulated particle coordinates follow their emitter.</summary>
public enum ParticleSimulationSpace2D
{
    /// <summary>Particles remain in emitter-local coordinates.</summary>
    Local,
    /// <summary>Particles are emitted into world coordinates.</summary>
    World
}

/// <summary>Stores one normalized scalar-curve key.</summary>
public struct ParticleCurveKey2D
{
    /// <summary>Gets or sets normalized key time.</summary>
    public float time { get; set; }

    /// <summary>Gets or sets scalar key value.</summary>
    public float value { get; set; }
}

/// <summary>Stores a piecewise-linear scalar curve used by deterministic particle simulation.</summary>
public struct ParticleCurve2D
{
    /// <summary>Gets or sets ordered normalized curve keys.</summary>
    public ParticleCurveKey2D[] keys { get; set; }

    /// <summary>Evaluates this curve at normalized time.</summary>
    /// <param name="time">Normalized sample time.</param>
    /// <returns>The linearly interpolated value, or one when no keys exist.</returns>
    public readonly float Evaluate(float time)
    {
        if (keys is not { Length: > 0 })
            return 1f;
        float clamped = Math.Clamp(time, 0f, 1f);
        ParticleCurveKey2D previous = keys[0];
        for (int index = 1; index < keys.Length; index++)
        {
            ParticleCurveKey2D next = keys[index];
            if (clamped <= next.time)
            {
                float range = MathF.Max(0.000001f, next.time - previous.time);
                float amount = Math.Clamp((clamped - previous.time) / range, 0f, 1f);
                return previous.value + (next.value - previous.value) * amount;
            }
            previous = next;
        }
        return previous.value;
    }
}

/// <summary>Stores one normalized particle-gradient key.</summary>
public struct ParticleGradientKey2D
{
    /// <summary>Gets or sets normalized key time.</summary>
    public float time { get; set; }

    /// <summary>Gets or sets linear key color.</summary>
    public Color color { get; set; }
}

/// <summary>Stores a piecewise-linear color gradient used by particle rendering.</summary>
public struct ParticleGradient2D
{
    /// <summary>Gets or sets ordered normalized gradient keys.</summary>
    public ParticleGradientKey2D[] keys { get; set; }

    /// <summary>Evaluates this gradient at normalized time.</summary>
    /// <param name="time">Normalized sample time.</param>
    /// <returns>The linearly interpolated color, or white when no keys exist.</returns>
    public readonly Color Evaluate(float time)
    {
        if (keys is not { Length: > 0 })
            return Color.WHITE;
        float clamped = Math.Clamp(time, 0f, 1f);
        ParticleGradientKey2D previous = keys[0];
        for (int index = 1; index < keys.Length; index++)
        {
            ParticleGradientKey2D next = keys[index];
            if (clamped <= next.time)
            {
                float range = MathF.Max(0.000001f, next.time - previous.time);
                float amount = Math.Clamp((clamped - previous.time) / range, 0f, 1f);
                return new Color(
                    previous.color.r + (next.color.r - previous.color.r) * amount,
                    previous.color.g + (next.color.g - previous.color.g) * amount,
                    previous.color.b + (next.color.b - previous.color.b) * amount,
                    previous.color.a + (next.color.a - previous.color.a) * amount);
            }
            previous = next;
        }
        return previous.color;
    }
}

/// <summary>Stores deterministic authoring parameters for one reusable 2D particle effect.</summary>
[StableTypeId("5731a3c1-9380-4fb2-85fd-55410384709d")]
public sealed class ParticleEffect2DAsset : AssetObject
{
    /// <summary>Gets or sets the rendered particle sprite.</summary>
    [SerializableProperty]
    public SpriteReference2D sprite { get; set; }

    /// <summary>Gets or sets optional ordered flipbook frames after the primary sprite.</summary>
    [SerializableProperty]
    public SpriteReference2D[] flipbookFrames { get; set; } = [];

    /// <summary>Gets or sets flipbook frames advanced per second.</summary>
    [SerializableProperty]
    public float flipbookFramesPerSecond { get; set; } = 12f;

    /// <summary>Gets or sets an optional material implementing the 2D sprite contract.</summary>
    [SerializableProperty]
    public MaterialAsset? material { get; set; }

    /// <summary>Gets or sets particle blending.</summary>
    [SerializableProperty]
    public SpriteBlendMode2D blendMode { get; set; } = SpriteBlendMode2D.Alpha;

    /// <summary>Gets or sets particle texture sampling.</summary>
    [SerializableProperty]
    public SpriteSamplingMode2D sampling { get; set; } = SpriteSamplingMode2D.LinearClamp;

    /// <summary>Gets or sets the local emission volume.</summary>
    [SerializableProperty]
    public ParticleEmitterShape2D shape { get; set; }

    /// <summary>Gets or sets the simulation coordinate space.</summary>
    [SerializableProperty]
    public ParticleSimulationSpace2D simulationSpace { get; set; }

    /// <summary>Gets or sets the positive maximum number of live particles.</summary>
    [SerializableProperty]
    public int maximumParticles { get; set; } = 1000;

    /// <summary>Gets or sets emitted particles per second.</summary>
    [SerializableProperty]
    public float emissionRate { get; set; } = 10f;

    /// <summary>Gets or sets positive minimum particle lifetime.</summary>
    [SerializableProperty]
    public float minimumLifetime { get; set; } = 1f;

    /// <summary>Gets or sets positive maximum particle lifetime.</summary>
    [SerializableProperty]
    public float maximumLifetime { get; set; } = 1f;

    /// <summary>Gets or sets minimum initial speed.</summary>
    [SerializableProperty]
    public float minimumSpeed { get; set; } = 1f;

    /// <summary>Gets or sets maximum initial speed.</summary>
    [SerializableProperty]
    public float maximumSpeed { get; set; } = 1f;

    /// <summary>Gets or sets rectangular emission extents or circle radius.</summary>
    [SerializableProperty]
    public Vector2 shapeSize { get; set; } = Vector2.ONE;

    /// <summary>Gets or sets the full cone angle in degrees.</summary>
    [SerializableProperty]
    public float coneAngle { get; set; } = 45f;

    /// <summary>Gets or sets constant acceleration.</summary>
    [SerializableProperty]
    public Vector2 gravity { get; set; }

    /// <summary>Gets or sets procedural velocity-noise strength.</summary>
    [SerializableProperty]
    public float noiseStrength { get; set; }

    /// <summary>Gets or sets the size multiplier over normalized lifetime.</summary>
    [SerializableProperty]
    public ParticleCurve2D sizeOverLifetime { get; set; }

    /// <summary>Gets or sets color over normalized lifetime.</summary>
    [SerializableProperty]
    public ParticleGradient2D colorOverLifetime { get; set; }

    /// <summary>Validates all authoring parameters.</summary>
    /// <exception cref="InvalidOperationException">Thrown when any range or curve is invalid.</exception>
    public void Validate()
    {
        if (!sprite.isAssigned
            || maximumParticles <= 0 || emissionRate < 0f
            || minimumLifetime <= 0f || maximumLifetime < minimumLifetime
            || maximumSpeed < minimumSpeed || flipbookFramesPerSecond < 0f)
        {
            throw new InvalidOperationException("Particle sprite, count, emission, lifetime, speed, or flipbook rate is invalid.");
        }
        if (flipbookFrames.Any(static frame => !frame.isAssigned))
            throw new InvalidOperationException("Every particle flipbook frame must reference a sprite.");
        ValidateTimes(sizeOverLifetime.keys?.Select(static key => key.time).ToArray());
        ValidateTimes(colorOverLifetime.keys?.Select(static key => key.time).ToArray());
    }

    private static void ValidateTimes(float[]? times)
    {
        if (times is null)
            return;
        float previous = float.NegativeInfinity;
        foreach (float time in times)
        {
            if (!float.IsFinite(time) || time < previous)
                throw new InvalidOperationException("Particle curve keys must use finite ascending times.");
            previous = time;
        }
    }
}
