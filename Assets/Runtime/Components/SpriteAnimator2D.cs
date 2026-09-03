using System;
using InnoEngine.Core;
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Advances stable sprite animation frames through the normal scene behavior lifecycle.</summary>
[StableTypeId("39d67e26-7076-4998-8862-2c65d427f668")]
public sealed class SpriteAnimator2D : GameBehavior
{
    private int m_frameIndex;
    private float m_frameTime;
    private bool m_playing;

    /// <summary>Gets or sets the animation library.</summary>
    [SerializableProperty]
    public SpriteAnimation2DAsset? animation { get; set; }

    /// <summary>Gets or sets the clip selected for playback.</summary>
    [SerializableProperty]
    public string clipId { get; set; } = string.Empty;

    /// <summary>Gets or sets whether playback starts when the behavior starts.</summary>
    [SerializableProperty]
    public bool playOnStart { get; set; } = true;

    /// <summary>Gets or sets non-negative playback speed.</summary>
    [SerializableProperty]
    public float speed { get; set; } = 1f;

    /// <summary>Gets whether playback is advancing.</summary>
    public bool isPlaying => m_playing;

    /// <summary>Gets the current zero-based frame index.</summary>
    public int currentFrameIndex => m_frameIndex;

    /// <summary>Gets the stable event identity of the current frame, or an empty value.</summary>
    public string currentFrameEvent { get; private set; } = string.Empty;

    /// <summary>Starts one clip from its first frame.</summary>
    /// <param name="id">Stable clip identity.</param>
    /// <exception cref="ArgumentException">Thrown when the clip identity is empty.</exception>
    public void Play(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        clipId = id;
        m_frameIndex = 0;
        m_frameTime = 0f;
        m_playing = true;
        ApplyFrame();
    }

    /// <summary>Pauses playback while preserving the current frame and elapsed time.</summary>
    public void Pause() => m_playing = false;

    /// <summary>Resumes the currently selected clip.</summary>
    public void Resume() => m_playing = true;

    /// <summary>Stops playback and rewinds to the first frame.</summary>
    public void Stop()
    {
        m_playing = false;
        m_frameIndex = 0;
        m_frameTime = 0f;
        ApplyFrame();
    }

    /// <inheritdoc />
    protected override void Start()
    {
        if (playOnStart && !string.IsNullOrWhiteSpace(clipId))
            Play(clipId);
        else
            ApplyFrame();
    }

    /// <inheritdoc />
    protected override void Update()
    {
        if (!m_playing || animation is null || speed <= 0f
            || !animation.TryGetClip(clipId, out SpriteAnimationClip2D clip)
            || clip.frames is null || clip.frames.Length == 0)
        {
            return;
        }

        m_frameTime += MathF.Max(0f, Time.deltaTime) * speed;
        int guard = clip.frames.Length + 1;
        while (guard-- > 0 && m_frameTime >= clip.frames[m_frameIndex].duration)
        {
            m_frameTime -= clip.frames[m_frameIndex].duration;
            int next = m_frameIndex + 1;
            if (next >= clip.frames.Length)
            {
                if (!clip.loop)
                {
                    m_frameIndex = clip.frames.Length - 1;
                    m_frameTime = 0f;
                    m_playing = false;
                    break;
                }
                next = 0;
            }
            m_frameIndex = next;
            ApplyFrame();
        }
    }

    private void ApplyFrame()
    {
        currentFrameEvent = string.Empty;
        if (animation is null
            || !animation.TryGetClip(clipId, out SpriteAnimationClip2D clip)
            || clip.frames is null || clip.frames.Length == 0
            || !gameObject.TryGetComponent<SpriteRenderer2D>(out SpriteRenderer2D? renderer)
            || renderer is null)
        {
            return;
        }
        m_frameIndex = Math.Clamp(m_frameIndex, 0, clip.frames.Length - 1);
        SpriteAnimationFrame2D frame = clip.frames[m_frameIndex];
        renderer.atlas = animation.atlas;
        renderer.spriteId = frame.spriteId;
        currentFrameEvent = frame.eventId ?? string.Empty;
    }
}
