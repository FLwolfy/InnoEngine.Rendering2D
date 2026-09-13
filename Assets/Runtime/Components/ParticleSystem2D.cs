using System;
using InnoEngine.Core;
using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEditor.Annotations;

namespace Inno.Rendering2D;

/// <summary>Simulates a deterministic reusable 2D particle effect through the scene lifecycle.</summary>
[StableTypeId("db4c2130-c022-412a-a326-9e63cdf9297b")]
public sealed class ParticleSystem2D : GameBehavior
{
    private ParticleEffect2DAsset? m_allocatedEffect;
    private ParticleState2D[] m_particles = [];
    private int m_count;
    private float m_emissionAccumulator;
    private uint m_randomState = 1;
    private ulong m_simulationRevision = 1;
    private bool m_playing;
    private uint m_seed = 1;

    /// <summary>Gets or sets the reusable effect definition.</summary>
    [SerializableProperty]
    [Header("Effect", "Simulation settings live in the reusable effect asset.")]
    [HelpBox(
        "Assign a Particle Effect 2D asset before playback.",
        nameof(effect),
        InspectorCondition.Null)]
    public ParticleEffect2DAsset? effect { get; set; }

    /// <summary>Gets or sets whether emission starts with the behavior.</summary>
    [SerializableProperty]
    [Header("Playback", "The seed makes CPU simulation deterministic across supported backends.")]
    public bool playOnStart { get; set; } = true;

    /// <summary>Gets or sets the deterministic non-zero simulation seed.</summary>
    [SerializableProperty]
    public uint seed
    {
        get => m_seed;
        set => m_seed = value == 0 ? 1u : value;
    }

    /// <summary>Gets or sets the stable project-local sorting-layer name.</summary>
    [SerializableProperty]
    [Header("Ordering", "Particles share one stable painter-order interval.")]
    public string sortingLayer { get; set; } = "default";

    /// <summary>Gets or sets painter order within the selected sorting layer.</summary>
    [SerializableProperty]
    public int orderInLayer { get; set; }

    /// <summary>Gets whether this system currently emits and simulates particles.</summary>
    public bool isPlaying => m_playing;

    /// <summary>Gets the current live particle count.</summary>
    public int particleCount => m_count;

    /// <summary>Starts emission without clearing existing live particles.</summary>
    public void Play()
    {
        EnsureCapacity();
        if (m_playing)
            return;
        m_playing = true;
        AdvanceSimulationRevision();
    }

    /// <summary>Pauses emission and simulation while preserving live particles.</summary>
    public void Pause()
    {
        if (!m_playing)
            return;
        m_playing = false;
        AdvanceSimulationRevision();
    }

    /// <summary>Stops emission and removes every live particle.</summary>
    public void Stop()
    {
        m_playing = false;
        m_count = 0;
        m_emissionAccumulator = 0f;
        m_randomState = seed == 0 ? 1u : seed;
        AdvanceSimulationRevision();
    }

    /// <inheritdoc />
    protected override void Start()
    {
        Stop();
        if (playOnStart)
            Play();
    }

    /// <inheritdoc />
    protected override void Update()
    {
        if (!m_playing || effect is null)
            return;
        EnsureCapacity();
        float deltaTime = MathF.Max(0f, Time.deltaTime);
        Simulate(deltaTime);
        Emit(deltaTime);
        AdvanceSimulationRevision();
    }

    internal ReadOnlySpan<ParticleState2D> particles => m_particles.AsSpan(0, m_count);
    internal ulong simulationRevision => m_simulationRevision;

    private void AdvanceSimulationRevision()
    {
        m_simulationRevision++;
        if (m_simulationRevision == 0)
            m_simulationRevision = 1;
    }

    private void EnsureCapacity()
    {
        ParticleEffect2DAsset current = effect
            ?? throw new InvalidOperationException("A particle effect is required before playback.");
        current.Validate();
        int capacity = Math.Max(1, current.maximumParticles);
        if (ReferenceEquals(current, m_allocatedEffect) && m_particles.Length == capacity)
            return;
        Array.Resize(ref m_particles, capacity);
        m_count = Math.Min(m_count, capacity);
        m_allocatedEffect = current;
        m_randomState = seed == 0 ? 1u : seed;
    }

    private void Simulate(float deltaTime)
    {
        ParticleEffect2DAsset current = effect!;
        int index = 0;
        while (index < m_count)
        {
            ParticleState2D particle = m_particles[index];
            particle.age += deltaTime;
            if (particle.age >= particle.lifetime)
            {
                m_count--;
                m_particles[index] = m_particles[m_count];
                continue;
            }
            float normalizedAge = particle.age / particle.lifetime;
            float noise = HashNoise(particle.noiseSeed, normalizedAge) * current.noiseStrength;
            particle.velocity += (current.gravity + new Vector2(noise, -noise)) * deltaTime;
            particle.position += particle.velocity * deltaTime;
            particle.rotation += particle.angularVelocity * deltaTime;
            particle.size = MathF.Max(0f, current.sizeOverLifetime.Evaluate(normalizedAge));
            particle.color = current.colorOverLifetime.Evaluate(normalizedAge);
            m_particles[index] = particle;
            index++;
        }
    }

    private void Emit(float deltaTime)
    {
        ParticleEffect2DAsset current = effect!;
        m_emissionAccumulator += current.emissionRate * deltaTime;
        int requested = Math.Min((int)m_emissionAccumulator, m_particles.Length - m_count);
        m_emissionAccumulator -= requested;
        for (int index = 0; index < requested; index++)
            m_particles[m_count++] = CreateParticle(current);
    }

    private ParticleState2D CreateParticle(ParticleEffect2DAsset current)
    {
        float lifetime = Lerp(current.minimumLifetime, current.maximumLifetime, NextFloat());
        float speed = Lerp(current.minimumSpeed, current.maximumSpeed, NextFloat());
        Vector2 position = GetEmissionPosition(current);
        Vector2 direction = GetEmissionDirection(current);
        if (current.simulationSpace == ParticleSimulationSpace2D.World)
        {
            position = TransformPoint(position);
            direction = Vector2.Transform(direction, transform.worldRotation).normalized;
        }
        return new ParticleState2D(
            position,
            direction * speed,
            0f,
            lifetime,
            NextFloat() * MathF.PI * 2f,
            (NextFloat() * 2f - 1f) * MathF.PI,
            MathF.Max(0f, current.sizeOverLifetime.Evaluate(0f)),
            current.colorOverLifetime.Evaluate(0f),
            NextUInt());
    }

    private Vector2 GetEmissionPosition(ParticleEffect2DAsset current)
    {
        if (current.shape == ParticleEmitterShape2D.Box)
        {
            return new Vector2(
                (NextFloat() - 0.5f) * current.shapeSize.x,
                (NextFloat() - 0.5f) * current.shapeSize.y);
        }
        if (current.shape == ParticleEmitterShape2D.Circle)
        {
            float angle = NextFloat() * MathF.PI * 2f;
            float radius = MathF.Sqrt(NextFloat()) * MathF.Max(0f, current.shapeSize.x);
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        return Vector2.ZERO;
    }

    private Vector2 GetEmissionDirection(ParticleEffect2DAsset current)
    {
        if (current.shape is ParticleEmitterShape2D.Circle)
        {
            float angle = NextFloat() * MathF.PI * 2f;
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }
        float halfAngle = current.shape == ParticleEmitterShape2D.Cone
            ? Math.Clamp(current.coneAngle, 0f, 360f) * MathF.PI / 360f
            : MathF.PI;
        float angleOffset = (NextFloat() * 2f - 1f) * halfAngle;
        return new Vector2(-MathF.Sin(angleOffset), MathF.Cos(angleOffset));
    }

    private Vector2 TransformPoint(Vector2 local)
    {
        Vector3 world = transform.TransformPoint(new Vector3(local.x, local.y, 0f));
        return new Vector2(world.x, world.y);
    }

    private uint NextUInt()
    {
        uint value = m_randomState;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        m_randomState = value == 0 ? 1u : value;
        return m_randomState;
    }

    private float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    private static float Lerp(float minimum, float maximum, float amount)
        => minimum + (maximum - minimum) * amount;

    private static float HashNoise(uint seed, float time)
    {
        uint value = seed ^ (uint)MathF.Floor(time * 1024f);
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        return (value >> 8) * (2f / 16777216f) - 1f;
    }
}

internal struct ParticleState2D
{
    internal ParticleState2D(
        Vector2 position,
        Vector2 velocity,
        float age,
        float lifetime,
        float rotation,
        float angularVelocity,
        float size,
        Color color,
        uint noiseSeed)
    {
        this.position = position;
        this.velocity = velocity;
        this.age = age;
        this.lifetime = lifetime;
        this.rotation = rotation;
        this.angularVelocity = angularVelocity;
        this.size = size;
        this.color = color;
        this.noiseSeed = noiseSeed;
    }

    internal Vector2 position { get; set; }
    internal Vector2 velocity { get; set; }
    internal float age { get; set; }
    internal float lifetime { get; set; }
    internal float rotation { get; set; }
    internal float angularVelocity { get; set; }
    internal float size { get; set; }
    internal Color color { get; set; }
    internal uint noiseSeed { get; }
}
