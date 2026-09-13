using System;
using InnoEngine.Core;
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEditor.Annotations;

namespace Inno.Rendering2D;

/// <summary>Advances stable sprite animation frames through the normal scene behavior lifecycle.</summary>
[StableTypeId("39d67e26-7076-4998-8862-2c65d427f668")]
public sealed class SpriteAnimator2D : GameBehavior
{
    private int m_frameIndex;
    private float m_frameTime;
    private bool m_playing;
    private float m_crossFadeDuration;
    private float m_crossFadeElapsed;
    private float m_speed = 1f;

    /// <summary>Gets or sets the animation library.</summary>
    [SerializableProperty]
    [Header("Animation", "Assign a frame-animation library before selecting its stable clip identifier.")]
    [HelpBox(
        "Clip selection becomes available after an Animation 2D asset is assigned.",
        nameof(animation),
        InspectorCondition.Null)]
    public SpriteAnimation2DAsset? animation { get; set; }

    /// <summary>Gets or sets the clip selected for playback.</summary>
    [SerializableProperty]
    [ShowIf(nameof(animation), InspectorCondition.NotNull)]
    [InspectorName("Clip ID")]
    public string clipId { get; set; } = string.Empty;

    /// <summary>Gets or sets whether playback starts when the behavior starts.</summary>
    [SerializableProperty]
    [Header("Playback", "Playback updates the SpriteRenderer2D on the same GameObject.")]
    public bool playOnStart { get; set; } = true;

    /// <summary>Gets or sets non-negative playback speed.</summary>
    [SerializableProperty]
    public float speed
    {
        get => m_speed;
        set => m_speed = float.IsFinite(value) ? MathF.Max(0f, value) : 0f;
    }

    /// <summary>Gets whether playback is advancing.</summary>
    public bool isPlaying => m_playing;

    /// <summary>Gets the current zero-based frame index.</summary>
    public int currentFrameIndex => m_frameIndex;

    /// <summary>Gets the stable event identity of the current frame, or an empty value.</summary>
    public string currentFrameEvent { get; private set; } = string.Empty;

    /// <summary>Occurs when playback enters a frame containing a non-empty stable event identity.</summary>
    public event Action<string>? frameEvent;

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
        ClearCrossFade();
        ApplyFrame();
    }

    /// <summary>Starts one clip while fading from the currently displayed sprite.</summary>
    /// <param name="id">Stable destination clip identity.</param>
    /// <param name="duration">Non-negative transition duration in seconds.</param>
    public void CrossFade(string id, float duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegative(duration);
        if (duration <= 0f
            || !gameObject.TryGetComponent<SpriteRenderer2D>(out SpriteRenderer2D? renderer)
            || renderer is null)
        {
            Play(id);
            return;
        }
        SpriteReference2D source = renderer.sprite;
        clipId = id;
        m_frameIndex = 0;
        m_frameTime = 0f;
        m_playing = true;
        m_crossFadeDuration = duration;
        m_crossFadeElapsed = 0f;
        ApplyFrame();
        renderer.SetCrossFade(source, source.isAssigned ? 1f : 0f);
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
        ClearCrossFade();
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
        UpdateCrossFade();
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
        renderer.sprite = frame.sprite;
        currentFrameEvent = frame.eventId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(currentFrameEvent))
            frameEvent?.Invoke(currentFrameEvent);
    }

    private void UpdateCrossFade()
    {
        if (m_crossFadeDuration <= 0f
            || !gameObject.TryGetComponent<SpriteRenderer2D>(out SpriteRenderer2D? renderer)
            || renderer is null)
        {
            return;
        }
        m_crossFadeElapsed += MathF.Max(0f, Time.deltaTime);
        float remaining = 1f - Math.Clamp(m_crossFadeElapsed / m_crossFadeDuration, 0f, 1f);
        renderer.SetCrossFade(renderer.crossFadeSprite, remaining);
        if (remaining <= 0f)
            ClearCrossFade();
    }

    private void ClearCrossFade()
    {
        m_crossFadeDuration = 0f;
        m_crossFadeElapsed = 0f;
        if (gameObject.TryGetComponent<SpriteRenderer2D>(out SpriteRenderer2D? renderer)
            && renderer is not null)
        {
            renderer.SetCrossFade(default, 0f);
        }
    }
}
