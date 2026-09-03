using System;
using System.Collections.Generic;

using InnoEngine.Reflection;
using InnoEngine.Scene;

namespace Inno.Rendering2D;

/// <summary>
/// Owns the structure-indexed extraction cache consumed by every 2D camera in one scene.
/// </summary>
/// <remarks>
/// Add exactly one instance to each scene rendered by the 2D Plugin. The system rescans scene structure only
/// after objects or components change; camera frames read current component values from the retained index.
/// </remarks>
[StableTypeId("0dc78d2a-01c8-46e4-a097-01b9c5ef14db")]
public sealed class Rendering2DSceneSystem : GameSystem
{
    private IReadOnlyList<GameObject>? m_indexedObjects;
    private Rendering2DSceneSnapshot m_snapshot = Rendering2DSceneSnapshot.empty;

    /// <summary>
    /// Captures the current structure-indexed scene view when this system is active.
    /// </summary>
    /// <returns>
    /// The reusable extraction snapshot, or an empty snapshot while the system is inactive.
    /// </returns>
    internal Rendering2DSceneSnapshot Capture()
    {
        if (!isActiveAndEnabled)
        {
            Clear();
            return Rendering2DSceneSnapshot.empty;
        }

        IReadOnlyList<GameObject> objects = GetObjects();
        if (ReferenceEquals(m_indexedObjects, objects))
            return m_snapshot;

        var cameras = new List<Camera2D>();
        var drawables = new List<Rendering2DDrawable>();
        var lights = new List<Light2D>();
        for (int index = 0; index < objects.Count; index++)
        {
            GameObject gameObject = objects[index];
            _ = gameObject.TryGetComponent<Camera2D>(out Camera2D? camera);
            _ = gameObject.TryGetComponent<SpriteRenderer2D>(out SpriteRenderer2D? sprite);
            _ = gameObject.TryGetComponent<TilemapRenderer2D>(out TilemapRenderer2D? tilemap);
            _ = gameObject.TryGetComponent<Light2D>(out Light2D? light);
            if (camera is not null)
                cameras.Add(camera);
            if (sprite is not null || tilemap is not null)
                drawables.Add(new Rendering2DDrawable(gameObject, sprite, tilemap));
            if (light is not null)
                lights.Add(light);
        }

        m_indexedObjects = objects;
        m_snapshot = new Rendering2DSceneSnapshot(
            [.. cameras],
            [.. drawables],
            [.. lights]);
        return m_snapshot;
    }

    /// <summary>
    /// Rebuilds the extraction index during scene update when structure changed since the previous frame.
    /// </summary>
    protected override void OnUpdate()
        => _ = Capture();

    /// <summary>
    /// Releases all component references before this system or its Plugin generation becomes inactive.
    /// </summary>
    protected override void OnDisable()
        => Clear();

    /// <summary>
    /// Releases all component references before this system is destroyed.
    /// </summary>
    protected override void OnDestroy()
        => Clear();

    private void Clear()
    {
        m_indexedObjects = null;
        m_snapshot = Rendering2DSceneSnapshot.empty;
    }
}

internal readonly record struct Rendering2DSceneSnapshot(
    Camera2D[] cameras,
    Rendering2DDrawable[] drawables,
    Light2D[] lights)
{
    internal static Rendering2DSceneSnapshot empty { get; } = new([], [], []);
}

internal readonly record struct Rendering2DDrawable(
    GameObject owner,
    SpriteRenderer2D? sprite,
    TilemapRenderer2D? tilemap);
