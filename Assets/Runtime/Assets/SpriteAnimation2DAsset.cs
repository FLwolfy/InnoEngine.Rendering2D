using System;
using System.Collections.Generic;
using System.Linq;
using InnoEngine.Assets;
using InnoEngine.Reflection;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Defines one timed atlas-region frame and optional stable gameplay event.</summary>
public struct SpriteAnimationFrame2D
{
    /// <summary>Gets or sets the atlas region identity.</summary>
    public string spriteId { get; set; }

    /// <summary>Gets or sets positive frame duration in seconds.</summary>
    public float duration { get; set; }

    /// <summary>Gets or sets an optional stable event identity exposed by the animator.</summary>
    public string eventId { get; set; }
}

/// <summary>Defines one named animation clip.</summary>
public struct SpriteAnimationClip2D
{
    /// <summary>Gets or sets the stable clip identity.</summary>
    public string id { get; set; }

    /// <summary>Gets or sets whether playback wraps at the end.</summary>
    public bool loop { get; set; }

    /// <summary>Gets or sets ordered timed frames.</summary>
    public SpriteAnimationFrame2D[] frames { get; set; }
}

/// <summary>Stores atlas-backed sprite animation clips without retaining runtime callbacks.</summary>
[StableTypeId("62cce935-3618-46b9-bfc3-795caafad26b")]
public sealed class SpriteAnimation2DAsset : AssetObject
{
    private SpriteAnimationClip2D[] m_clips = [];

    /// <summary>Gets or sets the atlas referenced by every frame.</summary>
    [SerializableProperty]
    public SpriteAtlas2DAsset? atlas { get; set; }

    /// <summary>Gets or sets all stable clips.</summary>
    [SerializableProperty]
    public SpriteAnimationClip2D[] clips
    {
        get => m_clips;
        set => m_clips = value?.ToArray() ?? [];
    }

    /// <summary>Tries to resolve a clip by stable identity.</summary>
    /// <param name="id">Stable clip identity.</param>
    /// <param name="clip">Receives the matching clip.</param>
    /// <returns><see langword="true"/> when a matching clip exists.</returns>
    public bool TryGetClip(string id, out SpriteAnimationClip2D clip)
    {
        for (int index = 0; index < m_clips.Length; index++)
        {
            if (!string.Equals(m_clips[index].id, id, StringComparison.Ordinal))
                continue;
            clip = m_clips[index];
            return true;
        }
        clip = default;
        return false;
    }

    /// <summary>Replaces all clips after validating identities, frames, and durations.</summary>
    /// <param name="clips">Complete clip set.</param>
    /// <exception cref="ArgumentException">Thrown when clip data is invalid.</exception>
    public void SetClips(IEnumerable<SpriteAnimationClip2D> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        SpriteAnimationClip2D[] values = clips.ToArray();
        if (values.Any(static value => string.IsNullOrWhiteSpace(value.id)))
            throw new ArgumentException("Animation clip IDs cannot be empty.", nameof(clips));
        if (values.Select(static value => value.id).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Animation clip IDs must be unique.", nameof(clips));
        if (values.Any(static value => value.frames is null || value.frames.Length == 0
            || value.frames.Any(static frame => string.IsNullOrWhiteSpace(frame.spriteId) || frame.duration <= 0f)))
        {
            throw new ArgumentException("Every animation clip requires valid positive-duration frames.", nameof(clips));
        }
        m_clips = values;
    }
}
