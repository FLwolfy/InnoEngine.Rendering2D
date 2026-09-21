using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using InnoEngine.Assets;
using InnoEngine.Core;
using InnoEngine.Mathematics;
using InnoEngine.Rendering;
using InnoEngine.Scene;
using InnoEngine.Settings;

namespace Inno.Rendering2D;

internal sealed class Rendering2DFrame
{
    internal required Camera2D camera { get; init; }
    internal required int pixelWidth { get; init; }
    internal required int pixelHeight { get; init; }
    internal required Matrix viewTransform { get; init; }
    internal required Matrix projectionTransform { get; init; }
    internal required float[] viewMatrix { get; init; }
    internal required float[] projectionMatrix { get; init; }
    internal required Rect worldBounds { get; init; }
    internal required Vector2 viewCenter { get; init; }
    internal required float viewHalfWidth { get; init; }
    internal required float viewHalfHeight { get; init; }
    internal required Quaternion viewRotation { get; init; }
    internal required RenderClearColor clearColor { get; init; }
    internal required bool clearTarget { get; init; }
    internal required MaterialAsset? defaultMaterial { get; init; }
    internal required Rendering2DPostProcessSettings? postProcess { get; init; }
    internal required Rendering2DLight[] lights { get; init; }
    internal required Rendering2DShadowCaster[] shadowCasters { get; init; }
    internal required Rendering2DDrawBatch[] batches { get; init; }
    internal required Rendering2DDrawBatch[] maskBatches { get; init; }
    internal required bool masksUnavailable { get; init; }
    internal required Rendering2DPickRecord[] pickRecords { get; init; }
    internal required string[] diagnostics { get; init; }

    internal GameObject? Pick(float normalizedX, float normalizedY)
    {
        float localX = (normalizedX * 2f - 1f) * viewHalfWidth;
        float localY = (1f - normalizedY * 2f) * viewHalfHeight;
        Vector2 rotated = Vector2.Transform(
            new Vector2(localX, localY),
            viewRotation);
        Vector2 world = viewCenter + rotated;
        for (int index = pickRecords.Length - 1; index >= 0; index--)
        {
            Rendering2DPickRecord record = pickRecords[index];
            if (record.bounds.Contains(world))
                return record.gameObject;
        }
        return null;
    }
}

internal sealed record Rendering2DFrameSequence(Rendering2DFrame[] frames);

internal readonly record struct Rendering2DPostProcessSettings(
    float exposure,
    float contrast,
    float saturation,
    bool toneMapping,
    float bloomIntensity,
    float bloomThreshold,
    int bloomLevels,
    float bloomScatter,
    float vignette,
    int pixelation);

internal sealed record Rendering2DDrawBatch(
    MaterialAsset material,
    Rendering2DTextureSource texture,
    Rendering2DTextureSource normalMap,
    Rendering2DTextureSource emissionMap,
    SpriteBlendMode2D blendMode,
    SpriteSamplingMode2D sampling,
    byte[] instanceBytes,
    int instanceCount,
    RenderPersistentResourceId? persistentInstanceBuffer,
    long instanceRevision,
    ulong maskSet,
    SpriteMaskInteraction2D maskInteraction,
    Color emissionColor,
    int lightingLayer,
    byte lightBlendStyles);

internal sealed record Rendering2DPickRecord(GameObject gameObject, Rect bounds, Rendering2DSortKey sortKey);

internal readonly record struct Rendering2DSortKey(int domain, int layer, int order, float depth, int sequence)
    : IComparable<Rendering2DSortKey>
{
    public int CompareTo(Rendering2DSortKey other)
    {
        int result = domain.CompareTo(other.domain);
        if (result != 0)
            return result;
        result = layer.CompareTo(other.layer);
        if (result != 0)
            return result;
        result = order.CompareTo(other.order);
        if (result != 0)
            return result;
        result = depth.CompareTo(other.depth);
        return result != 0 ? result : sequence.CompareTo(other.sequence);
    }
}

internal readonly record struct Rendering2DVertex(
    float x,
    float y,
    float z,
    float u,
    float v,
    uint color,
    float shape);

internal sealed record Rendering2DQuad(
    MaterialAsset material,
    Rendering2DTextureSource texture,
    SpriteBlendMode2D blendMode,
    SpriteSamplingMode2D sampling,
    Rendering2DSortKey sortKey,
    GameObject? owner,
    Rendering2DVertex bottomLeft,
    Rendering2DVertex bottomRight,
    Rendering2DVertex topRight,
    Rendering2DVertex topLeft)
{
    internal string? persistentGroupId { get; init; }
    internal float alphaCutoff { get; init; } = 0.001f;
    internal ulong maskSet { get; init; }
    internal SpriteMaskInteraction2D maskInteraction { get; init; }
    internal Rendering2DTextureSource normalMap { get; init; }
    internal Rendering2DTextureSource emissionMap { get; init; }
    internal Color emissionColor { get; init; } = Color.BLACK;
    internal byte lightBlendStyles { get; init; }
    internal int lightingLayer { get; init; }

    internal Rect GetBounds()
    {
        float minimumX = MathF.Min(MathF.Min(bottomLeft.x, bottomRight.x), MathF.Min(topRight.x, topLeft.x));
        float minimumY = MathF.Min(MathF.Min(bottomLeft.y, bottomRight.y), MathF.Min(topRight.y, topLeft.y));
        float maximumX = MathF.Max(MathF.Max(bottomLeft.x, bottomRight.x), MathF.Max(topRight.x, topLeft.x));
        float maximumY = MathF.Max(MathF.Max(bottomLeft.y, bottomRight.y), MathF.Max(topRight.y, topLeft.y));
        return new Rect(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
    }
}

internal readonly record struct Rendering2DTextureSource(
    TextureAsset? directTexture,
    RenderTextureArtifactReference? artifact)
{
    internal static Rendering2DTextureSource none => default;

    internal static Rendering2DTextureSource FromTexture(TextureAsset texture)
        => new(texture, null);

    internal static Rendering2DTextureSource FromArtifact(RenderTextureArtifactReference artifact)
        => new(null, artifact);
}

internal readonly record struct Rendering2DLight(
    LightKind2D kind,
    Vector2 position,
    Vector2 direction,
    Color color,
    float intensity,
    float range,
    float spotAngle,
    float falloff,
    LightBlendStyle2D blendStyle,
    Rendering2DTextureSource cookie,
    Vector2[] shape,
    bool castShadows,
    float shadowSoftness,
    float normalIntensity,
    GameLayerMask layers);

internal readonly record struct Rendering2DShadowCaster(
    Vector2[] shape,
    byte lightBlendStyles,
    bool selfShadows,
    GameLayer layer);

internal static class Rendering2DFrameCollector
{
    private const int C_INSTANCE_STRIDE = 80;
    internal static Rendering2DFrameFingerprint ComputeFingerprint(
        Rendering2DSceneScope scope,
        Camera2D camera,
        int pixelWidth,
        int pixelHeight,
        Rendering2DViewportOptions? options)
    {
        var hash = new Rendering2DFingerprintBuilder();
        hash.Add(pixelWidth);
        hash.Add(pixelHeight);
        hash.Add(Settings.revision);
        hash.Add(Settings.ownerId);
        AddAsset(ref hash, Rendering2DRenderer.ResolvePipeline());
        AddAsset(ref hash, Rendering2DRenderer.pipelineSettings.defaultSpriteMaterial);
        AddCamera(ref hash, camera);
        hash.Add(options?.clearTargetOverride);
        hash.Add(options?.clearColorOverride);
        hash.Add(options?.backbufferOnly ?? false);
        hash.Add(options?.drawGrid ?? false);
        hash.Add(options?.drawAxes ?? false);
        IReadOnlyList<string>? optionDiagnostics = options?.additionalDiagnostics;
        hash.Add(optionDiagnostics?.Count ?? 0);
        if (optionDiagnostics is not null)
        {
            for (int index = 0; index < optionDiagnostics.Count; index++)
                hash.Add(optionDiagnostics[index]);
        }

        hash.Add(scope.entries.Count);
        for (int entryIndex = 0; entryIndex < scope.entries.Count; entryIndex++)
        {
            Rendering2DSceneEntry entry = scope.entries[entryIndex];
            hash.Add(entry.scene.identity.persistentId);
            Rendering2DSceneSnapshot snapshot = entry.snapshot;
            hash.Add(snapshot.cameras.Length);
            for (int index = 0; index < snapshot.cameras.Length; index++)
                AddCamera(ref hash, snapshot.cameras[index]);
            hash.Add(snapshot.drawables.Length);
            for (int index = 0; index < snapshot.drawables.Length; index++)
                AddDrawable(ref hash, snapshot.drawables[index]);
            hash.Add(snapshot.lights.Length);
            for (int index = 0; index < snapshot.lights.Length; index++)
                AddLight(ref hash, snapshot.lights[index]);
            hash.Add(snapshot.shadowCasters.Length);
            for (int index = 0; index < snapshot.shadowCasters.Length; index++)
                AddShadowCaster(ref hash, snapshot.shadowCasters[index]);
            hash.Add(snapshot.masks.Length);
            for (int index = 0; index < snapshot.masks.Length; index++)
                AddMask(ref hash, snapshot.masks[index]);
        }
        return hash.Build();
    }

    private static void AddCamera(ref Rendering2DFingerprintBuilder hash, Camera2D camera)
    {
        hash.Add(camera.gameObject.identity.persistentId);
        hash.Add(camera.isActiveAndEnabled);
        AddTransform(ref hash, camera.transform);
        hash.Add(camera.primary);
        hash.Add(camera.renderToBackbuffer);
        hash.Add((int)camera.composition);
        hash.Add(camera.stackId);
        hash.Add(camera.clearTarget);
        hash.Add(camera.clearColor);
        hash.Add(camera.orthographicSize);
        hash.Add(camera.pixelPerfect);
        hash.Add(camera.integerScale);
        hash.Add(camera.referenceResolution);
        hash.Add(camera.viewport);
        hash.Add(camera.cullingMask.GetHashCode());
        hash.Add(camera.priority);
        AddAsset(ref hash, camera.postProcess);
        if (camera.postProcess is PostProcessProfile2DAsset post)
        {
            hash.Add(post.exposure);
            hash.Add(post.contrast);
            hash.Add(post.saturation);
            hash.Add(post.toneMapping);
            hash.Add(post.bloomIntensity);
            hash.Add(post.bloomThreshold);
            hash.Add(post.bloomLevels);
            hash.Add(post.bloomScatter);
            hash.Add(post.vignette);
            hash.Add(post.pixelation);
        }
    }

    private static void AddDrawable(ref Rendering2DFingerprintBuilder hash, Rendering2DDrawable drawable)
    {
        GameObject owner = drawable.owner;
        hash.Add(owner.identity.persistentId);
        hash.Add(owner.activeInHierarchy);
        hash.Add(owner.layer.index);
        AddTransform(ref hash, owner.transform);
        if (drawable.sprite is SpriteRenderer2D sprite)
        {
            hash.Add(true);
            hash.Add(sprite.isActiveAndEnabled);
            AddSpriteReference(ref hash, sprite.sprite);
            AddSpriteReference(ref hash, sprite.crossFadeSprite);
            hash.Add(sprite.crossFadeWeight);
            hash.Add((int)sprite.primitive);
            AddAsset(ref hash, sprite.material);
            hash.Add(sprite.color);
            hash.Add(sprite.size);
            hash.Add(sprite.boundsPadding);
            hash.Add(sprite.pivot);
            hash.Add(sprite.pixelsPerUnit);
            hash.Add(sprite.flipX);
            hash.Add(sprite.flipY);
            hash.Add(sprite.sortingLayer);
            hash.Add(sprite.orderInLayer);
            hash.Add((int)sprite.blendMode);
            hash.Add((int)sprite.drawMode);
            hash.Add((int)sprite.sampling);
            hash.Add(sprite.receiveLighting);
            AddAsset(ref hash, sprite.normalMap);
            AddAsset(ref hash, sprite.emissionMap);
            hash.Add(sprite.emissionColor);
            hash.Add((int)sprite.maskInteraction);
            hash.Add(sprite.lightBlendStyles);
        }
        else
        {
            hash.Add(false);
        }
        if (drawable.tilemap is TilemapRenderer2D tilemap)
        {
            hash.Add(true);
            hash.Add(tilemap.isActiveAndEnabled);
            AddAsset(ref hash, tilemap.tilemap);
            hash.Add(tilemap.tilemap?.revision ?? 0UL);
            AddAsset(ref hash, tilemap.tilemap?.tileSet);
            AddAsset(ref hash, tilemap.material);
            hash.Add(tilemap.color);
            hash.Add(tilemap.sortingLayer);
            hash.Add(tilemap.orderInLayer);
            hash.Add((int)tilemap.blendMode);
            hash.Add((int)tilemap.sampling);
            hash.Add(tilemap.receiveLighting);
            AddAsset(ref hash, tilemap.normalMap);
            AddAsset(ref hash, tilemap.emissionMap);
            hash.Add(tilemap.emissionColor);
            hash.Add(tilemap.lightBlendStyles);
        }
        else
        {
            hash.Add(false);
        }
        if (drawable.particles is ParticleSystem2D particles)
        {
            hash.Add(true);
            hash.Add(particles.isActiveAndEnabled);
            hash.Add(particles.simulationRevision);
            AddAsset(ref hash, particles.effect);
            hash.Add(particles.sortingLayer);
            hash.Add(particles.orderInLayer);
        }
        else
        {
            hash.Add(false);
        }
    }

    private static void AddLight(ref Rendering2DFingerprintBuilder hash, Light2D light)
    {
        hash.Add(light.gameObject.identity.persistentId);
        hash.Add(light.isActiveAndEnabled);
        AddTransform(ref hash, light.transform);
        hash.Add((int)light.kind);
        hash.Add(light.color);
        hash.Add(light.intensity);
        hash.Add(light.range);
        hash.Add(light.spotAngle);
        hash.Add(light.falloff);
        hash.Add((int)light.blendStyle);
        AddAsset(ref hash, light.cookie);
        AddPoints(ref hash, light.shape);
        hash.Add(light.castShadows);
        hash.Add(light.shadowSoftness);
        hash.Add(light.normalIntensity);
        hash.Add(light.cullingMask.GetHashCode());
    }

    private static void AddShadowCaster(ref Rendering2DFingerprintBuilder hash, ShadowCaster2D caster)
    {
        hash.Add(caster.gameObject.identity.persistentId);
        hash.Add(caster.isActiveAndEnabled);
        hash.Add(caster.gameObject.layer.index);
        AddTransform(ref hash, caster.transform);
        AddPoints(ref hash, caster.shape);
        hash.Add(caster.lightBlendStyles);
        hash.Add(caster.selfShadows);
    }

    private static void AddMask(ref Rendering2DFingerprintBuilder hash, Rendering2DMask snapshot)
    {
        SpriteMask2D mask = snapshot.mask;
        hash.Add(snapshot.owner.identity.persistentId);
        hash.Add(snapshot.owner.activeInHierarchy);
        hash.Add(snapshot.owner.layer.index);
        hash.Add(mask.isActiveAndEnabled);
        AddTransform(ref hash, snapshot.owner.transform);
        AddSpriteReference(ref hash, mask.sprite);
        AddAsset(ref hash, mask.material);
        hash.Add((int)mask.primitive);
        hash.Add(mask.size);
        hash.Add(mask.boundsPadding);
        hash.Add(mask.pivot);
        hash.Add(mask.pixelsPerUnit);
        hash.Add(mask.flipX);
        hash.Add(mask.flipY);
        hash.Add((int)mask.sampling);
        hash.Add(mask.sortingLayer);
        hash.Add(mask.frontOrder);
        hash.Add(mask.backOrder);
        hash.Add(mask.alphaCutoff);
    }

    private static void AddSpriteReference(ref Rendering2DFingerprintBuilder hash, SpriteReference2D sprite)
    {
        AddAsset(ref hash, sprite.atlas);
        hash.Add(sprite.regionId.value);
        AddAsset(ref hash, sprite.texture);
    }

    private static void AddAsset(ref Rendering2DFingerprintBuilder hash, AssetObject? asset)
    {
        if (asset is null)
        {
            hash.Add(false);
            return;
        }
        hash.Add(true);
        hash.Add(asset.identity.persistentId);
        hash.Add(asset.contentVersion);
    }

    private static void AddPoints(
        ref Rendering2DFingerprintBuilder hash,
        IReadOnlyList<Vector2>? points)
    {
        hash.Add(points?.Count ?? 0);
        if (points is null)
            return;
        for (int index = 0; index < points.Count; index++)
            hash.Add(points[index]);
    }

    private static void AddTransform(ref Rendering2DFingerprintBuilder hash, Transform transform)
    {
        Matrix matrix = transform.localToWorldMatrix;
        hash.Add(matrix.m11); hash.Add(matrix.m12); hash.Add(matrix.m13); hash.Add(matrix.m14);
        hash.Add(matrix.m21); hash.Add(matrix.m22); hash.Add(matrix.m23); hash.Add(matrix.m24);
        hash.Add(matrix.m31); hash.Add(matrix.m32); hash.Add(matrix.m33); hash.Add(matrix.m34);
        hash.Add(matrix.m41); hash.Add(matrix.m42); hash.Add(matrix.m43); hash.Add(matrix.m44);
    }

    internal static Rendering2DFrame Collect(
        Rendering2DSceneScope scope,
        Camera2D camera,
        int pixelWidth,
        int pixelHeight,
        Rendering2DViewportOptions? options)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        Rendering2DProjectSettings settings = GetSettings();
        var diagnostics = new List<string>();
        if (options?.additionalDiagnostics is not null)
            diagnostics.AddRange(options.additionalDiagnostics);
        MaterialAsset? defaultMaterial = Rendering2DRenderer.pipelineSettings.defaultSpriteMaterial;
        if (defaultMaterial is null || defaultMaterial.isMissing)
        {
            defaultMaterial = null;
        }

        CameraState cameraState = CreateCameraState(
            camera,
            pixelWidth,
            pixelHeight,
            settings.defaultPixelsPerUnit);
        Rendering2DLight[] lights = CollectLights(scope, camera, cameraState.bounds);
        Rendering2DShadowCaster[] shadowCasters = CollectShadowCasters(scope, camera, cameraState.bounds);
        List<MaskSnapshot> masks = CollectMasks(
            scope,
            camera,
            settings,
            cameraState.bounds,
            defaultMaterial,
            diagnostics,
            out bool masksUnavailable);
        var quads = new List<Rendering2DQuad>();
        int sequence = 0;
        CollectViewportGuides(
            cameraState,
            pixelHeight,
            defaultMaterial,
            options,
            quads,
            ref sequence);
        int contentStart = quads.Count;
        int outputLimit = contentStart >= int.MaxValue - settings.maximumQuadsPerFrame
            ? int.MaxValue
            : contentStart + settings.maximumQuadsPerFrame;
        foreach (Rendering2DSceneEntry entry in scope.entries)
        {
            Rendering2DDrawable[] drawables = entry.snapshot.drawables;
            for (int drawableIndex = 0; drawableIndex < drawables.Length; drawableIndex++)
            {
                Rendering2DDrawable drawable = drawables[drawableIndex];
                GameObject gameObject = drawable.owner;
                if (!gameObject.activeInHierarchy || !camera.cullingMask.Contains(gameObject.layer))
                    continue;
                if (drawable.sprite is { isActiveAndEnabled: true } sprite)
                {
                    CollectSprite(
                        gameObject,
                        sprite,
                        settings,
                        cameraState.bounds,
                        lights,
                        masks,
                        quads,
                        diagnostics,
                        outputLimit,
                        ref sequence);
                }
                if (drawable.tilemap is { isActiveAndEnabled: true } tilemap)
                {
                    CollectTilemap(
                        gameObject,
                        tilemap,
                        defaultMaterial,
                        settings,
                        cameraState.bounds,
                        lights,
                        quads,
                        diagnostics,
                        outputLimit,
                        ref sequence);
                }
                if (drawable.particles is { isActiveAndEnabled: true } particles)
                {
                    CollectParticles(
                        gameObject,
                        particles,
                        defaultMaterial,
                        settings,
                        cameraState.bounds,
                        quads,
                        diagnostics,
                        outputLimit,
                        ref sequence);
                }
                if (quads.Count >= outputLimit)
                    break;
            }
            if (quads.Count >= outputLimit)
                break;
        }
        if (quads.Count >= outputLimit)
            diagnostics.Add($"2D request reached the configured {settings.maximumQuadsPerFrame} quad limit.");

        quads.Sort(static (left, right) => left.sortKey.CompareTo(right.sortKey));
        return new Rendering2DFrame
        {
            camera = camera,
            pixelWidth = pixelWidth,
            pixelHeight = pixelHeight,
            viewTransform = cameraState.view,
            projectionTransform = cameraState.projection,
            viewMatrix = ToColumnMajor(cameraState.view),
            projectionMatrix = ToColumnMajor(cameraState.projection),
            worldBounds = cameraState.bounds,
            viewCenter = cameraState.center,
            viewHalfWidth = cameraState.halfWidth,
            viewHalfHeight = cameraState.halfHeight,
            viewRotation = cameraState.rotation,
            clearTarget = options?.clearTargetOverride ?? camera.clearTarget,
            clearColor = ToRenderClearColor(options?.clearColorOverride ?? camera.clearColor),
            defaultMaterial = defaultMaterial,
            postProcess = CapturePostProcess(camera.postProcess),
            lights = lights,
            shadowCasters = shadowCasters,
            batches = BuildBatches(quads, Math.Max(1, settings.maximumQuadsPerBatch)),
            maskBatches = BuildMaskBatches(masks),
            masksUnavailable = masksUnavailable,
            pickRecords = BuildPickRecords(quads),
            diagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static Rendering2DFrame Empty(
        Camera2D camera,
        int pixelWidth,
        int pixelHeight,
        float pixelsPerUnit,
        List<string> diagnostics,
        Rendering2DViewportOptions? options)
    {
        CameraState state = CreateCameraState(camera, pixelWidth, pixelHeight, pixelsPerUnit);
        return new Rendering2DFrame
        {
            camera = camera,
            pixelWidth = pixelWidth,
            pixelHeight = pixelHeight,
            viewTransform = state.view,
            projectionTransform = state.projection,
            viewMatrix = ToColumnMajor(state.view),
            projectionMatrix = ToColumnMajor(state.projection),
            worldBounds = state.bounds,
            viewCenter = state.center,
            viewHalfWidth = state.halfWidth,
            viewHalfHeight = state.halfHeight,
            viewRotation = state.rotation,
            clearTarget = options?.clearTargetOverride ?? camera.clearTarget,
            clearColor = ToRenderClearColor(options?.clearColorOverride ?? camera.clearColor),
            defaultMaterial = null,
            postProcess = CapturePostProcess(camera.postProcess),
            lights = [],
            shadowCasters = [],
            batches = [],
            maskBatches = [],
            masksUnavailable = false,
            pickRecords = [],
            diagnostics = diagnostics.ToArray()
        };
    }

    private static Rendering2DProjectSettings GetSettings()
        => Rendering2DRenderer.projectSettings;

    private static Rendering2DPostProcessSettings? CapturePostProcess(PostProcessProfile2DAsset? profile)
    {
        if (profile is null)
            return null;
        return new Rendering2DPostProcessSettings(
            float.IsFinite(profile.exposure) ? profile.exposure : 0f,
            float.IsFinite(profile.contrast) ? MathF.Max(0f, profile.contrast) : 1f,
            float.IsFinite(profile.saturation) ? MathF.Max(0f, profile.saturation) : 1f,
            profile.toneMapping,
            float.IsFinite(profile.bloomIntensity) ? MathF.Max(0f, profile.bloomIntensity) : 0f,
            float.IsFinite(profile.bloomThreshold) ? MathF.Max(0f, profile.bloomThreshold) : 1f,
            Math.Clamp(profile.bloomLevels, 1, 8),
            float.IsFinite(profile.bloomScatter) ? Math.Clamp(profile.bloomScatter, 0f, 1f) : 0.7f,
            float.IsFinite(profile.vignette) ? Math.Clamp(profile.vignette, 0f, 1f) : 0f,
            Math.Max(1, profile.pixelation));
    }

    private static CameraState CreateCameraState(
        Camera2D camera,
        int width,
        int height,
        float projectPixelsPerUnit)
    {
        float pixelsPerUnit = MathF.Max(0.001f, projectPixelsPerUnit);
        float halfHeight = MathF.Max(0.001f, camera.orthographicSize);
        Vector3 cameraWorld = camera.transform.worldPosition;
        if (camera.pixelPerfect)
        {
            halfHeight = height / (2f * pixelsPerUnit);
            cameraWorld.x = MathF.Round(cameraWorld.x * pixelsPerUnit) / pixelsPerUnit;
            cameraWorld.y = MathF.Round(cameraWorld.y * pixelsPerUnit) / pixelsPerUnit;
        }
        float halfWidth = halfHeight * width / height;
        float angle = camera.transform.worldRotation.ToEulerAnglesZYX().z;
        Quaternion viewRotation = Quaternion.CreateFromAxisAngle(Vector3.FORWARD, angle);
        Matrix view = Matrix.CreateRotationZ(-angle)
            * Matrix.CreateTranslation(-cameraWorld.x, -cameraWorld.y, 0f);
        Matrix projection = new(
            1f / halfWidth, 0f, 0f, 0f,
            0f, 1f / halfHeight, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f);
        Vector2[] corners =
        [
            new(-halfWidth, -halfHeight),
            new(halfWidth, -halfHeight),
            new(halfWidth, halfHeight),
            new(-halfWidth, halfHeight)
        ];
        for (int index = 0; index < corners.Length; index++)
        {
            corners[index] = Vector2.Transform(corners[index], viewRotation)
                + new Vector2(cameraWorld.x, cameraWorld.y);
        }
        float minimumX = corners.Min(static value => value.x);
        float minimumY = corners.Min(static value => value.y);
        float maximumX = corners.Max(static value => value.x);
        float maximumY = corners.Max(static value => value.y);
        return new CameraState(
            view,
            projection,
            new Rect(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY),
            new Vector2(cameraWorld.x, cameraWorld.y),
            halfWidth,
            halfHeight,
            viewRotation);
    }

    private static Rendering2DLight[] CollectLights(
        Rendering2DSceneScope scope,
        Camera2D camera,
        Rect cameraBounds)
    {
        var lights = new List<Rendering2DLight>();
        foreach (Rendering2DSceneEntry entry in scope.entries)
        {
            Light2D[] indexedLights = entry.snapshot.lights;
            for (int lightIndex = 0; lightIndex < indexedLights.Length; lightIndex++)
            {
                Light2D light = indexedLights[lightIndex];
                if (!light.gameObject.activeInHierarchy || !light.isActiveAndEnabled)
                {
                    continue;
                }
                Vector3 position = light.transform.worldPosition;
                Vector2 direction = Vector2.Transform(Vector2.UNIT_Y, light.transform.worldRotation).normalized;
                float range = MathF.Max(0.0001f, light.range);
                Vector2[] shape = CaptureLightShape(light, range);
                if (light.kind != LightKind2D.Global
                    && !GetLightBounds(light.kind, new Vector2(position.x, position.y), range, shape)
                        .Overlaps(cameraBounds))
                {
                    continue;
                }
                lights.Add(new Rendering2DLight(
                    light.kind,
                    new Vector2(position.x, position.y),
                    direction,
                    light.color,
                    MathF.Max(0f, light.intensity),
                    range,
                    Math.Clamp(light.spotAngle, 0.1f, 179.9f) * MathF.PI / 180f,
                    MathF.Max(0.01f, light.falloff),
                    light.blendStyle,
                    light.cookie is TextureAsset cookie
                        ? Rendering2DTextureSource.FromTexture(cookie)
                        : Rendering2DTextureSource.none,
                    shape,
                    light.castShadows,
                    Math.Clamp(light.shadowSoftness, 0f, 1f),
                    MathF.Max(0f, light.normalIntensity),
                    light.cullingMask));
            }
        }
        return lights.ToArray();
    }

    private static Rendering2DShadowCaster[] CollectShadowCasters(
        Rendering2DSceneScope scope,
        Camera2D camera,
        Rect cameraBounds)
    {
        var result = new List<Rendering2DShadowCaster>();
        foreach (Rendering2DSceneEntry entry in scope.entries)
        {
            ShadowCaster2D[] indexedCasters = entry.snapshot.shadowCasters;
            for (int casterIndex = 0; casterIndex < indexedCasters.Length; casterIndex++)
            {
                ShadowCaster2D caster = indexedCasters[casterIndex];
                if (!caster.gameObject.activeInHierarchy
                    || !caster.isActiveAndEnabled
                    || !camera.cullingMask.Contains(caster.gameObject.layer)
                    || caster.shape is not { Length: >= 3 })
                {
                    continue;
                }
                var shape = new Vector2[caster.shape.Length];
                for (int pointIndex = 0; pointIndex < shape.Length; pointIndex++)
                    shape[pointIndex] = TransformPoint(caster.transform, caster.shape[pointIndex]);
                if (!GetPolygonBounds(shape).Overlaps(cameraBounds))
                    continue;
                result.Add(new Rendering2DShadowCaster(
                    shape,
                    (byte)(caster.lightBlendStyles & 0x0f),
                    caster.selfShadows,
                    caster.gameObject.layer));
            }
        }
        return result.ToArray();
    }

    private static Vector2[] CaptureLightShape(Light2D light, float range)
    {
        if (light.kind != LightKind2D.Freeform || light.shape is not { Length: >= 3 })
            return [];
        var result = new Vector2[light.shape.Length];
        for (int index = 0; index < result.Length; index++)
            result[index] = TransformPoint(light.transform, light.shape[index] * range);
        return result;
    }

    private static Rect GetLightBounds(
        LightKind2D kind,
        Vector2 position,
        float range,
        IReadOnlyList<Vector2> shape)
        => kind == LightKind2D.Freeform && shape.Count >= 3
            ? GetPolygonBounds(shape)
            : new Rect(position.x - range, position.y - range, range * 2f, range * 2f);

    private static Rect GetPolygonBounds(IReadOnlyList<Vector2> shape)
    {
        float minimumX = shape[0].x;
        float minimumY = shape[0].y;
        float maximumX = minimumX;
        float maximumY = minimumY;
        for (int index = 1; index < shape.Count; index++)
        {
            minimumX = MathF.Min(minimumX, shape[index].x);
            minimumY = MathF.Min(minimumY, shape[index].y);
            maximumX = MathF.Max(maximumX, shape[index].x);
            maximumY = MathF.Max(maximumY, shape[index].y);
        }
        return new Rect(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
    }

    private static List<MaskSnapshot> CollectMasks(
        Rendering2DSceneScope scope,
        Camera2D camera,
        Rendering2DProjectSettings settings,
        Rect cameraBounds,
        MaterialAsset? defaultMaterial,
        List<string> diagnostics,
        out bool unavailable)
    {
        unavailable = false;
        var result = new List<MaskSnapshot>();
        Dictionary<string, List<int>> consumers = CollectMaskConsumers(scope, camera, settings, cameraBounds);
        if (consumers.Count == 0) return result;
        foreach (Rendering2DSceneEntry entry in scope.entries)
        {
            Rendering2DMask[] indexedMasks = entry.snapshot.masks;
            for (int maskIndex = 0; maskIndex < indexedMasks.Length; maskIndex++)
            {
                Rendering2DMask indexed = indexedMasks[maskIndex];
                GameObject owner = indexed.owner;
                SpriteMask2D mask = indexed.mask;
                if (!owner.activeInHierarchy
                    || !mask.isActiveAndEnabled
                    || !camera.cullingMask.Contains(owner.layer))
                {
                    continue;
                }
                if (!consumers.TryGetValue(mask.sortingLayer, out List<int>? orders)) continue;
                int first = orders.BinarySearch(mask.frontOrder);
                if (first < 0) first = ~first;
                if (first == orders.Count || orders[first] > mask.backOrder) continue;
                if (!TryResolveMask(mask, out SpriteSource source))
                {
                    diagnostics.Add($"Sprite mask '{owner.name}' has no valid texture or atlas region.");
                    unavailable = true;
                    continue;
                }
                float pixelsPerUnit = mask.pixelsPerUnit > 0f
                    ? mask.pixelsPerUnit
                    : MathF.Max(0.001f, settings.defaultPixelsPerUnit);
                Vector2 naturalSize = source.primitive == SpritePrimitive2D.None
                    ? source.region.sourceSizePixels / pixelsPerUnit
                    : Vector2.ONE;
                Vector2 size = new(
                    mask.size.x > 0f ? mask.size.x : naturalSize.x,
                    mask.size.y > 0f ? mask.size.y : naturalSize.y);
                Rect localBounds = new(
                    -source.region.pivot.x * size.x,
                    -source.region.pivot.y * size.y,
                    size.x,
                    size.y);
                if (!GetTransformedBounds(owner.transform, localBounds, mask.boundsPadding).Overlaps(cameraBounds))
                    continue;

                if (result.Count >= 64)
                {
                    diagnostics.Add("A 2D camera accepts at most 64 visible sprite masks.");
                    unavailable = true;
                    return result;
                }

                MaterialAsset? material = mask.material ?? defaultMaterial;
                if (material is null || material.isMissing)
                {
                    diagnostics.Add($"Sprite mask '{owner.name}' requires an available coverage Material; the masked output cannot be composed.");
                    unavailable = true;
                    continue;
                }

                Vector2 sourceSize = source.region.sourceSizePixels;
                float scaleX = size.x / sourceSize.x;
                float scaleY = size.y / sourceSize.y;
                Vector2 minimum = new(
                    (-source.region.pivot.x * sourceSize.x + source.region.trimOffsetPixels.x) * scaleX,
                    (-source.region.pivot.y * sourceSize.y + source.region.trimOffsetPixels.y) * scaleY);
                Vector2 maximum = minimum + new Vector2(
                    source.region.trimmedSizePixels.x * scaleX,
                    source.region.trimmedSizePixels.y * scaleY);
                var writers = new List<Rendering2DQuad>(1);
                AddQuad(
                    owner,
                    source.texture,
                    source.primitive,
                    material,
                    SpriteBlendMode2D.Alpha,
                    mask.sampling,
                    new Rendering2DSortKey(
                        -1,
                        settings.GetSortingLayerOrder(mask.sortingLayer),
                        Math.Min(mask.frontOrder, mask.backOrder),
                        owner.transform.worldPosition.z,
                        result.Count),
                    minimum,
                    maximum,
                    source.region,
                    mask.flipX,
                    mask.flipY,
                    0,
                    Color.WHITE,
                    writers);
                writers[0] = writers[0] with
                {
                    alphaCutoff = Math.Clamp(mask.alphaCutoff, 0f, 1f)
                };
                result.Add(new MaskSnapshot(
                    mask.sortingLayer ?? string.Empty,
                    Math.Min(mask.frontOrder, mask.backOrder),
                    Math.Max(mask.frontOrder, mask.backOrder),
                    writers[0]));
            }
        }
        return result;
    }

    private static ulong GetMaskSet(
        string sortingLayer,
        int orderInLayer,
        IReadOnlyList<MaskSnapshot> masks)
    {
        ulong result = 0;
        for (int index = 0; index < masks.Count; index++)
        {
            MaskSnapshot mask = masks[index];
            if (string.Equals(mask.sortingLayer, sortingLayer, StringComparison.Ordinal)
                && orderInLayer >= mask.frontOrder
                && orderInLayer <= mask.backOrder)
            {
                result |= 1UL << index;
            }
        }
        return result;
    }

    private static void CollectViewportGuides(
        CameraState camera,
        int pixelHeight,
        MaterialAsset? material,
        Rendering2DViewportOptions? options,
        List<Rendering2DQuad> output,
        ref int sequence)
    {
        if (material is null || options is null || (!options.drawGrid && !options.drawAxes))
            return;

        Rect bounds = camera.bounds;
        float worldPerPixel = camera.halfHeight * 2f / Math.Max(1, pixelHeight);
        if (options.drawGrid)
        {
            float step = ChooseGridStep(worldPerPixel * 56f);
            float lineWidth = MathF.Max(worldPerPixel, step * 0.001f);
            int firstX = (int)MathF.Floor(bounds.left / step);
            int lastX = (int)MathF.Ceiling(bounds.right / step);
            int firstY = (int)MathF.Floor(bounds.top / step);
            int lastY = (int)MathF.Ceiling(bounds.bottom / step);
            var gridColor = new Color(0.48f, 0.51f, 0.56f, 0.18f);
            for (int index = firstX; index <= lastX; index++)
            {
                float x = index * step;
                AddWorldQuad(
                    material,
                    new Vector2(x - lineWidth * 0.5f, bounds.top),
                    new Vector2(x + lineWidth * 0.5f, bounds.bottom),
                    gridColor,
                    new Rendering2DSortKey(0, 0, 0, 0f, sequence++),
                    output);
            }
            for (int index = firstY; index <= lastY; index++)
            {
                float y = index * step;
                AddWorldQuad(
                    material,
                    new Vector2(bounds.left, y - lineWidth * 0.5f),
                    new Vector2(bounds.right, y + lineWidth * 0.5f),
                    gridColor,
                    new Rendering2DSortKey(0, 0, 0, 0f, sequence++),
                    output);
            }
        }

        if (!options.drawAxes)
            return;
        float axisWidth = MathF.Max(worldPerPixel * 2f, 0.001f);
        if (bounds.top <= 0f && bounds.bottom >= 0f)
        {
            AddWorldQuad(
                material,
                new Vector2(bounds.left, -axisWidth * 0.5f),
                new Vector2(bounds.right, axisWidth * 0.5f),
                new Color(0.88f, 0.25f, 0.24f, 0.8f),
                new Rendering2DSortKey(0, 1, 0, 0f, sequence++),
                output);
        }
        if (bounds.left <= 0f && bounds.right >= 0f)
        {
            AddWorldQuad(
                material,
                new Vector2(-axisWidth * 0.5f, bounds.top),
                new Vector2(axisWidth * 0.5f, bounds.bottom),
                new Color(0.26f, 0.82f, 0.38f, 0.8f),
                new Rendering2DSortKey(0, 1, 1, 0f, sequence++),
                output);
        }
    }

    private static float ChooseGridStep(float targetWorldSpacing)
    {
        float safeTarget = MathF.Max(0.000001f, targetWorldSpacing);
        float magnitude = MathF.Pow(10f, MathF.Floor(MathF.Log10(safeTarget)));
        float normalized = safeTarget / magnitude;
        float multiple = normalized <= 1f ? 1f : normalized <= 2f ? 2f : normalized <= 5f ? 5f : 10f;
        return magnitude * multiple;
    }

    private static void AddWorldQuad(
        MaterialAsset material,
        Vector2 minimum,
        Vector2 maximum,
        Color color,
        Rendering2DSortKey sortKey,
        List<Rendering2DQuad> output)
    {
        uint packed = PackColor(color);
        float shape = (float)SpritePrimitive2D.Square;
        output.Add(new Rendering2DQuad(
            material,
            Rendering2DTextureSource.none,
            SpriteBlendMode2D.Alpha,
            SpriteSamplingMode2D.LinearClamp,
            sortKey,
            null,
            new Rendering2DVertex(minimum.x, minimum.y, 0f, 0f, 1f, packed, shape),
            new Rendering2DVertex(maximum.x, minimum.y, 0f, 1f, 1f, packed, shape),
            new Rendering2DVertex(maximum.x, maximum.y, 0f, 1f, 0f, packed, shape),
            new Rendering2DVertex(minimum.x, maximum.y, 0f, 0f, 0f, packed, shape)));
    }

    private static RenderClearColor ToRenderClearColor(Color color)
        => new(color.r, color.g, color.b, color.a);

    private static void CollectSprite(
        GameObject owner,
        SpriteRenderer2D sprite,
        Rendering2DProjectSettings settings,
        Rect cameraBounds,
        IReadOnlyList<Rendering2DLight> lights,
        IReadOnlyList<MaskSnapshot> masks,
        List<Rendering2DQuad> output,
        List<string> diagnostics,
        int outputLimit,
        ref int sequence)
    {
        if (!TryResolveSprite(sprite, out SpriteSource source))
        {
            diagnostics.Add($"Sprite '{owner.name}' has no valid texture or atlas region.");
            return;
        }
        float pixelsPerUnit = sprite.pixelsPerUnit > 0f
            ? sprite.pixelsPerUnit
            : MathF.Max(0.001f, settings.defaultPixelsPerUnit);
        Vector2 naturalSize = source.primitive == SpritePrimitive2D.None
            ? source.region.sourceSizePixels / pixelsPerUnit
            : Vector2.ONE;
        Vector2 size = new(
            sprite.size.x > 0f ? sprite.size.x : naturalSize.x,
            sprite.size.y > 0f ? sprite.size.y : naturalSize.y);
        Rect coarseBounds = GetTransformedBounds(owner.transform, new Rect(
            -source.region.pivot.x * size.x,
            -source.region.pivot.y * size.y,
            size.x,
            size.y), sprite.boundsPadding);
        if (!coarseBounds.Overlaps(cameraBounds))
            return;

        ulong maskSet = GetMaskSet(sprite.sortingLayer, sprite.orderInLayer, masks);
        if (sprite.maskInteraction == SpriteMaskInteraction2D.VisibleInside && maskSet == 0)
            return;

        Color tint = sprite.color;
        MaterialAsset? material = sprite.material;
        if (material is null || material.isMissing)
        { diagnostics.Add($"Sprite '{owner.name}' has no explicitly assigned Material."); return; }
        int before = output.Count;
        bool supportsCrossFade = sprite.blendMode is SpriteBlendMode2D.Alpha
            or SpriteBlendMode2D.Premultiplied
            or SpriteBlendMode2D.Additive;
        float fadeWeight = Math.Clamp(sprite.crossFadeWeight, 0f, 1f);
        if (supportsCrossFade
            && fadeWeight > 0f
            && TryResolveSprite(sprite.crossFadeSprite, sprite.pivot, out SpriteSource fadeSource))
        {
            AddSpriteGeometry(
                owner,
                fadeSource,
                size,
                pixelsPerUnit,
                sprite,
                material,
                WithCrossFadeWeight(tint, fadeWeight, sprite.blendMode),
                CreateSpriteSortKey(owner, sprite, settings, sequence++),
                Math.Max(1, settings.maximumTiledSpriteQuads),
                output);
            AddSpriteGeometry(
                owner,
                source,
                size,
                pixelsPerUnit,
                sprite,
                material,
                WithCrossFadeWeight(tint, 1f - fadeWeight, sprite.blendMode),
                CreateSpriteSortKey(owner, sprite, settings, sequence++),
                Math.Max(1, settings.maximumTiledSpriteQuads),
                output);
        }
        else
        {
            AddSpriteGeometry(
                owner,
                fadeWeight >= 0.5f
                    && TryResolveSprite(sprite.crossFadeSprite, sprite.pivot, out SpriteSource hardFadeSource)
                        ? hardFadeSource
                        : source,
                size,
                pixelsPerUnit,
                sprite,
                material,
                tint,
                CreateSpriteSortKey(owner, sprite, settings, sequence++),
                Math.Max(1, settings.maximumTiledSpriteQuads),
                output);
        }
        for (int index = before; index < output.Count; index++)
        {
            output[index] = output[index] with
            {
                normalMap = sprite.normalMap is TextureAsset normalMap
                    ? Rendering2DTextureSource.FromTexture(normalMap)
                    : source.normalMap,
                emissionMap = sprite.emissionMap is TextureAsset emissionMap
                    ? Rendering2DTextureSource.FromTexture(emissionMap)
                    : source.emissionMap,
                emissionColor = sprite.emissionColor,
                lightBlendStyles = sprite.receiveLighting && lights.Count > 0
                    ? (byte)(sprite.lightBlendStyles & 0x0f)
                    : (byte)0,
                lightingLayer = owner.layer.index
            };
        }
        if (sprite.maskInteraction != SpriteMaskInteraction2D.None && maskSet != 0)
        {
            for (int index = before; index < output.Count; index++)
            {
                output[index] = output[index] with
                {
                    maskSet = maskSet,
                    maskInteraction = sprite.maskInteraction
                };
            }
        }
        if (output.Count > outputLimit)
            output.RemoveRange(outputLimit, output.Count - outputLimit);
        if (output.Count == before)
            diagnostics.Add($"Sprite '{owner.name}' generated no visible geometry.");
    }

    private static Rendering2DSortKey CreateSpriteSortKey(
        GameObject owner,
        SpriteRenderer2D sprite,
        Rendering2DProjectSettings settings,
        int sequence)
        => new(
            1,
            settings.GetSortingLayerOrder(sprite.sortingLayer),
            sprite.orderInLayer,
            owner.transform.worldPosition.z,
            sequence);

    private static void AddSpriteGeometry(
        GameObject owner,
        SpriteSource source,
        Vector2 size,
        float pixelsPerUnit,
        SpriteRenderer2D sprite,
        MaterialAsset material,
        Color tint,
        Rendering2DSortKey sortKey,
        int maximumTiledSpriteQuads,
        List<Rendering2DQuad> output)
    {
        switch (source.primitive != SpritePrimitive2D.None ? SpriteDrawMode2D.Simple : sprite.drawMode)
        {
            case SpriteDrawMode2D.Simple:
                AddSimpleSprite(owner, source, size, sprite, material, tint, sortKey, output);
                break;
            case SpriteDrawMode2D.Sliced:
                AddSlicedSprite(owner, source, size, sprite, material, tint, sortKey, output);
                break;
            case SpriteDrawMode2D.Tiled:
                AddTiledSprite(
                    owner,
                    source,
                    size,
                    pixelsPerUnit,
                    sprite,
                    material,
                    tint,
                    sortKey,
                    maximumTiledSpriteQuads,
                    output);
                break;
        }
    }

    private static Color WithCrossFadeWeight(Color color, float weight, SpriteBlendMode2D blendMode)
    {
        float value = Math.Clamp(weight, 0f, 1f);
        return blendMode is SpriteBlendMode2D.Premultiplied or SpriteBlendMode2D.Additive
            ? new Color(color.r * value, color.g * value, color.b * value, color.a * value)
            : new Color(color.r, color.g, color.b, color.a * value);
    }

    private static void CollectTilemap(
        GameObject owner,
        TilemapRenderer2D renderer,
        MaterialAsset? defaultMaterial,
        Rendering2DProjectSettings settings,
        Rect cameraBounds,
        IReadOnlyList<Rendering2DLight> lights,
        List<Rendering2DQuad> output,
        List<string> diagnostics,
        int outputLimit,
        ref int sequence)
    {
        Tilemap2DAsset? map = renderer.tilemap;
        TileSet2DAsset? tileSet = map?.tileSet;
        if (map is null || tileSet is null)
        {
            diagnostics.Add($"Tilemap '{owner.name}' has no complete Tilemap and TileSet chain.");
            return;
        }
        MaterialAsset? material = renderer.material ?? defaultMaterial;
        if (material is null || material.isMissing)
        { diagnostics.Add($"Tilemap '{owner.name}' has no available material."); return; }
        Vector2 cellSize = new(MathF.Max(0.0001f, map.cellSize.x), MathF.Max(0.0001f, map.cellSize.y));
        foreach (TilemapChunk2D chunk in map.chunks)
        {
            if (!map.TryGetLayer(chunk.layerId, out TilemapLayer2D layer) || !layer.visible)
                continue;
            Rect localChunkBounds = new(
                chunk.x * map.chunkSize * cellSize.x,
                chunk.y * map.chunkSize * cellSize.y,
                map.chunkSize * cellSize.x,
                map.chunkSize * cellSize.y);
            if (!GetTransformedBounds(owner.transform, localChunkBounds).Overlaps(cameraBounds))
                continue;
            string? persistentGroup = CanPersistChunk(owner, renderer, map, tileSet, chunk)
                ? $"inno.rendering2d.tilemap/{map.identity.persistentId:N}/{owner.identity.persistentId:N}/{chunk.layerId}/{chunk.x}/{chunk.y}"
                : null;
            int chunkQuadIndex = 0;
            foreach (TilemapCell2D cell in chunk.cells ?? [])
            {
                if (output.Count >= outputLimit)
                    break;
                if (!tileSet.TryGetTile(cell.tileId, out TileDefinition2D tile)
                    || !TryResolveSprite(SelectTileSprite(map, chunk.layerId, cell, tile), default, out SpriteSource source))
                {
                    diagnostics.Add($"Tilemap '{owner.name}' references undefined tile or sprite data.");
                    continue;
                }
                Vector2 localMinimum = new(cell.x * cellSize.x, cell.y * cellSize.y);
                if (persistentGroup is null
                    && !GetTransformedBounds(
                        owner.transform,
                        new Rect(localMinimum.x, localMinimum.y, cellSize.x, cellSize.y)).Overlaps(cameraBounds))
                {
                    continue;
                }
                Color tint = Multiply(Multiply(Multiply(renderer.color, layer.color), tile.color), cell.color);
                Rendering2DSortKey sortKey = new(
                    1,
                    settings.GetSortingLayerOrder(renderer.sortingLayer),
                    renderer.orderInLayer + layer.order,
                    owner.transform.worldPosition.z,
                    sequence++);
                int before = output.Count;
                AddQuad(
                    owner,
                    source.texture,
                    SpritePrimitive2D.None,
                    material,
                    renderer.blendMode,
                    renderer.sampling,
                    sortKey,
                    localMinimum,
                    localMinimum + cellSize,
                    source.region,
                    cell.flipX,
                    cell.flipY,
                    cell.quarterTurns,
                    tint,
                    output);
                if (persistentGroup is not null && output.Count != before)
                {
                    int part = chunkQuadIndex / Math.Max(1, settings.maximumQuadsPerBatch);
                    output[^1] = output[^1] with { persistentGroupId = $"{persistentGroup}/{part}" };
                    chunkQuadIndex++;
                }
                if (output.Count != before)
                {
                    output[^1] = output[^1] with
                    {
                        normalMap = renderer.normalMap is TextureAsset normalMap
                            ? Rendering2DTextureSource.FromTexture(normalMap)
                            : source.normalMap,
                        emissionMap = renderer.emissionMap is TextureAsset emissionMap
                            ? Rendering2DTextureSource.FromTexture(emissionMap)
                            : source.emissionMap,
                        emissionColor = renderer.emissionColor,
                        lightBlendStyles = renderer.receiveLighting && lights.Count > 0
                            ? (byte)(renderer.lightBlendStyles & 0x0f)
                            : (byte)0,
                        lightingLayer = owner.layer.index
                    };
                }
            }
            if (output.Count >= outputLimit)
                break;
        }
    }

    private static bool CanPersistChunk(
        GameObject owner,
        TilemapRenderer2D renderer,
        Tilemap2DAsset map,
        TileSet2DAsset tileSet,
        TilemapChunk2D chunk)
    {
        if (map.identity.persistentId == Guid.Empty
            || owner.identity.persistentId == Guid.Empty)
        {
            return false;
        }
        foreach (TilemapCell2D cell in chunk.cells ?? [])
        {
            if (!tileSet.TryGetTile(cell.tileId, out TileDefinition2D tile)
                || tile.animation is { Length: > 0 }
                || tile.rules is { Length: > 0 })
            {
                return false;
            }
        }
        return true;
    }

    private static SpriteReference2D SelectTileSprite(
        Tilemap2DAsset map,
        int layerId,
        TilemapCell2D cell,
        TileDefinition2D tile)
    {
        TileVisualRule2D[] rules = tile.rules ?? [];
        int bestPriority = int.MinValue;
        SpriteReference2D best = default;
        for (int index = 0; index < rules.Length; index++)
        {
            TileVisualRule2D candidate = rules[index];
            if (candidate.priority < bestPriority
                || !MatchesRule(map, layerId, cell, candidate.conditions ?? []))
            {
                continue;
            }
            bestPriority = candidate.priority;
            best = candidate.sprite;
        }
        if (best.isAssigned)
            return best;

        TileAnimationFrame2D[] animation = tile.animation ?? [];
        if (animation.Length == 0)
            return tile.sprite;
        float totalDuration = 0f;
        for (int index = 0; index < animation.Length; index++)
            totalDuration += MathF.Max(0.000001f, animation[index].duration);
        float time = Time.time % totalDuration;
        for (int index = 0; index < animation.Length; index++)
        {
            time -= MathF.Max(0.000001f, animation[index].duration);
            if (time <= 0f)
                return animation[index].sprite;
        }
        return animation[^1].sprite;
    }

    private static bool MatchesRule(
        Tilemap2DAsset map,
        int layerId,
        TilemapCell2D cell,
        TileNeighborCondition2D[] conditions)
    {
        for (int index = 0; index < conditions.Length; index++)
        {
            TileNeighborCondition2D condition = conditions[index];
            bool occupied = map.TryGetCell(cell.x + condition.x, cell.y + condition.y, layerId, out TilemapCell2D neighbor);
            bool matches = condition.rule switch
            {
                TileNeighborRule2D.Any => true,
                TileNeighborRule2D.Same => occupied && neighbor.tileId == cell.tileId,
                TileNeighborRule2D.Different => !occupied || neighbor.tileId != cell.tileId,
                TileNeighborRule2D.Empty => !occupied,
                TileNeighborRule2D.Occupied => occupied,
                _ => false
            };
            if (!matches)
                return false;
        }
        return true;
    }

    private static void CollectParticles(
        GameObject owner,
        ParticleSystem2D system,
        MaterialAsset? defaultMaterial,
        Rendering2DProjectSettings settings,
        Rect cameraBounds,
        List<Rendering2DQuad> output,
        List<string> diagnostics,
        int outputLimit,
        ref int sequence)
    {
        ParticleEffect2DAsset? effect = system.effect;
        if (effect is null)
            return;
        ReadOnlySpan<ParticleState2D> particles = system.particles;
        MaterialAsset? material = effect.material ?? defaultMaterial;
        if (material is null || material.isMissing)
        { diagnostics.Add($"Particle system '{owner.name}' has no available material."); return; }
        for (int index = 0; index < particles.Length && output.Count < outputLimit; index++)
        {
            ParticleState2D particle = particles[index];
            SpriteReference2D sprite = SelectParticleSprite(effect, particle);
            if (!TryResolveSprite(sprite, new Vector2(0.5f, 0.5f), out SpriteSource source))
            {
                diagnostics.Add($"Particle system '{owner.name}' references undefined sprite data.");
                return;
            }
            Vector2 center = effect.simulationSpace == ParticleSimulationSpace2D.Local
                ? TransformPoint(owner.transform, particle.position)
                : particle.position;
            float size = MathF.Max(0f, particle.size);
            Rect bounds = new(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            if (!bounds.Overlaps(cameraBounds))
                continue;
            Rendering2DSortKey sortKey = new(
                1,
                settings.GetSortingLayerOrder(system.sortingLayer),
                system.orderInLayer,
                owner.transform.worldPosition.z,
                sequence++);
            AddParticleQuad(owner, center, size, particle.rotation, source, material, effect, particle.color, sortKey, output);
        }
    }

    private static SpriteReference2D SelectParticleSprite(ParticleEffect2DAsset effect, ParticleState2D particle)
    {
        SpriteReference2D[] frames = effect.flipbookFrames ?? [];
        if (frames.Length == 0 || effect.flipbookFramesPerSecond <= 0f)
            return effect.sprite;
        int frame = (int)MathF.Floor(particle.age * effect.flipbookFramesPerSecond) % (frames.Length + 1);
        return frame == 0 ? effect.sprite : frames[frame - 1];
    }

    private static void AddParticleQuad(
        GameObject owner,
        Vector2 center,
        float size,
        float rotation,
        SpriteSource source,
        MaterialAsset material,
        ParticleEffect2DAsset effect,
        Color tint,
        Rendering2DSortKey sortKey,
        List<Rendering2DQuad> output)
    {
        float half = size * 0.5f;
        float cosine = MathF.Cos(rotation);
        float sine = MathF.Sin(rotation);
        Vector2 Rotate(float x, float y) => center + new Vector2(x * cosine - y * sine, x * sine + y * cosine);
        Vector2 bottomLeft = Rotate(-half, -half);
        Vector2 bottomRight = Rotate(half, -half);
        Vector2 topRight = Rotate(half, half);
        Vector2 topLeft = Rotate(-half, half);
        Vector2 uvBottomLeft = GetUv(source.region, 0f, 0f, false, false, 0);
        Vector2 uvBottomRight = GetUv(source.region, 1f, 0f, false, false, 0);
        Vector2 uvTopRight = GetUv(source.region, 1f, 1f, false, false, 0);
        Vector2 uvTopLeft = GetUv(source.region, 0f, 1f, false, false, 0);
        uint packed = PackColor(tint);
        float z = owner.transform.worldPosition.z;
        output.Add(new Rendering2DQuad(
            material,
            source.texture,
            effect.blendMode,
            effect.sampling,
            sortKey,
            owner,
            new Rendering2DVertex(bottomLeft.x, bottomLeft.y, z, uvBottomLeft.x, uvBottomLeft.y, packed, 0f),
            new Rendering2DVertex(bottomRight.x, bottomRight.y, z, uvBottomRight.x, uvBottomRight.y, packed, 0f),
            new Rendering2DVertex(topRight.x, topRight.y, z, uvTopRight.x, uvTopRight.y, packed, 0f),
            new Rendering2DVertex(topLeft.x, topLeft.y, z, uvTopLeft.x, uvTopLeft.y, packed, 0f)));
    }

    private static bool TryResolveSprite(SpriteRenderer2D sprite, out SpriteSource source)
    {
        if (TryResolveSprite(sprite.sprite, sprite.pivot, out source))
            return true;
        if (sprite.primitive != SpritePrimitive2D.None)
        {
            source = new SpriteSource(
                Rendering2DTextureSource.none,
                Rendering2DTextureSource.none,
                Rendering2DTextureSource.none,
                new SpriteRegion2D
            {
                id = new SpriteRegionId($"builtin:{sprite.primitive}"),
                name = $"Built-in {sprite.primitive}",
                uvRect = new Rect(0f, 0f, 1f, 1f),
                sourceSizePixels = Vector2.ONE,
                trimmedSizePixels = Vector2.ONE,
                trimOffsetPixels = Vector2.ZERO,
                pivot = sprite.pivot,
                borderPixels = default,
                outline = []
                },
                sprite.primitive);
            return true;
        }
        source = default;
        return false;
    }

    private static bool TryResolveMask(SpriteMask2D mask, out SpriteSource source)
    {
        if (TryResolveSprite(mask.sprite, mask.pivot, out source))
            return true;
        if (mask.primitive != SpritePrimitive2D.None)
        {
            source = new SpriteSource(
                Rendering2DTextureSource.none,
                Rendering2DTextureSource.none,
                Rendering2DTextureSource.none,
                new SpriteRegion2D
            {
                id = new SpriteRegionId($"builtin:mask:{mask.primitive}"),
                name = $"Built-in Mask {mask.primitive}",
                uvRect = new Rect(0f, 0f, 1f, 1f),
                sourceSizePixels = Vector2.ONE,
                trimmedSizePixels = Vector2.ONE,
                trimOffsetPixels = Vector2.ZERO,
                pivot = mask.pivot,
                borderPixels = default,
                outline = []
                },
                mask.primitive);
            return true;
        }
        source = default;
        return false;
    }

    private static bool TryResolveSprite(
        SpriteReference2D sprite,
        Vector2 fallbackPivot,
        out SpriteSource source)
    {
        if (sprite.atlas is SpriteAtlas2DAsset atlas
            && atlas.TryGetRegion(sprite.regionId, out SpriteRegion2D region))
        {
            try
            {
                source = new SpriteSource(
                    Rendering2DTextureSource.FromArtifact(atlas.GetPageTexture(region.pageIndex)),
                    atlas.TryGetPageNormalTexture(region.pageIndex, out RenderTextureArtifactReference normal)
                        ? Rendering2DTextureSource.FromArtifact(normal)
                        : Rendering2DTextureSource.none,
                    atlas.TryGetPageEmissionTexture(region.pageIndex, out RenderTextureArtifactReference emission)
                        ? Rendering2DTextureSource.FromArtifact(emission)
                        : Rendering2DTextureSource.none,
                    region,
                    SpritePrimitive2D.None);
                return true;
            }
            catch (InvalidOperationException)
            {
                source = default;
                return false;
            }
        }
        if (sprite.texture is TextureAsset texture && texture.width > 0 && texture.height > 0)
        {
            source = new SpriteSource(
                Rendering2DTextureSource.FromTexture(texture),
                Rendering2DTextureSource.none,
                Rendering2DTextureSource.none,
                new SpriteRegion2D
            {
                id = new SpriteRegionId("direct"),
                name = "Standalone Texture",
                uvRect = new Rect(0f, 0f, 1f, 1f),
                sourceSizePixels = new Vector2(texture.width, texture.height),
                trimmedSizePixels = new Vector2(texture.width, texture.height),
                trimOffsetPixels = Vector2.ZERO,
                pivot = fallbackPivot,
                borderPixels = default,
                outline = []
                },
                SpritePrimitive2D.None);
            return true;
        }
        source = default;
        return false;
    }

    private static void AddSimpleSprite(
        GameObject owner,
        SpriteSource source,
        Vector2 size,
        SpriteRenderer2D sprite,
        MaterialAsset material,
        Color tint,
        Rendering2DSortKey sortKey,
        List<Rendering2DQuad> output)
    {
        SpriteRegion2D region = source.region;
        Vector2 sourceSize = region.sourceSizePixels;
        float scaleX = size.x / sourceSize.x;
        float scaleY = size.y / sourceSize.y;
        Vector2 minimum = new(
            (-region.pivot.x * sourceSize.x + region.trimOffsetPixels.x) * scaleX,
            (-region.pivot.y * sourceSize.y + region.trimOffsetPixels.y) * scaleY);
        Vector2 maximum = minimum + new Vector2(
            region.trimmedSizePixels.x * scaleX,
            region.trimmedSizePixels.y * scaleY);
        AddQuad(
            owner,
            source.texture,
            source.primitive,
            material,
            sprite.blendMode,
            sprite.sampling,
            sortKey,
            minimum,
            maximum,
            region,
            sprite.flipX,
            sprite.flipY,
            0,
            tint,
            output);
    }

    private static void AddSlicedSprite(
        GameObject owner,
        SpriteSource source,
        Vector2 size,
        SpriteRenderer2D sprite,
        MaterialAsset material,
        Color tint,
        Rendering2DSortKey sortKey,
        List<Rendering2DQuad> output)
    {
        SpriteRegion2D region = source.region;
        System.Numerics.Vector4 border = region.borderPixels;
        if (border.X + border.Z <= 0f || border.Y + border.W <= 0f)
        {
            AddSimpleSprite(owner, source, size, sprite, material, tint, sortKey, output);
            return;
        }
        float sourceWidth = MathF.Max(1f, region.sourceSizePixels.x);
        float sourceHeight = MathF.Max(1f, region.sourceSizePixels.y);
        float left = MathF.Min(size.x, border.X / sourceWidth * size.x);
        float right = MathF.Min(size.x - left, border.Z / sourceWidth * size.x);
        float bottom = MathF.Min(size.y, border.Y / sourceHeight * size.y);
        float top = MathF.Min(size.y - bottom, border.W / sourceHeight * size.y);
        float originX = -region.pivot.x * size.x;
        float originY = -region.pivot.y * size.y;
        float[] xs = [originX, originX + left, originX + size.x - right, originX + size.x];
        float[] ys = [originY, originY + bottom, originY + size.y - top, originY + size.y];
        float[] us = [0f, border.X / sourceWidth, 1f - border.Z / sourceWidth, 1f];
        float[] vs = [0f, border.Y / sourceHeight, 1f - border.W / sourceHeight, 1f];
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                if (xs[x + 1] <= xs[x] || ys[y + 1] <= ys[y])
                    continue;
                AddQuad(
                    owner,
                    source.texture,
                    source.primitive,
                    material,
                    sprite.blendMode,
                    sprite.sampling,
                    sortKey,
                    new Vector2(xs[x], ys[y]),
                    new Vector2(xs[x + 1], ys[y + 1]),
                    region,
                    sprite.flipX,
                    sprite.flipY,
                    0,
                    tint,
                    output,
                    new Rect(us[x], vs[y], us[x + 1] - us[x], vs[y + 1] - vs[y]));
            }
        }
    }

    private static void AddTiledSprite(
        GameObject owner,
        SpriteSource source,
        Vector2 size,
        float pixelsPerUnit,
        SpriteRenderer2D sprite,
        MaterialAsset material,
        Color tint,
        Rendering2DSortKey sortKey,
        int maximumQuads,
        List<Rendering2DQuad> output)
    {
        Vector2 tileSize = source.region.sourceSizePixels / pixelsPerUnit;
        tileSize = new Vector2(MathF.Max(0.0001f, tileSize.x), MathF.Max(0.0001f, tileSize.y));
        Vector2 origin = new(-source.region.pivot.x * size.x, -source.region.pivot.y * size.y);
        int countX = Math.Max(1, (int)MathF.Ceiling(size.x / tileSize.x));
        int countY = Math.Max(1, (int)MathF.Ceiling(size.y / tileSize.y));
        int generated = 0;
        for (int y = 0; y < countY && generated < maximumQuads; y++)
        {
            for (int x = 0; x < countX && generated < maximumQuads; x++)
            {
                Vector2 minimum = origin + new Vector2(x * tileSize.x, y * tileSize.y);
                Vector2 extent = new(
                    MathF.Min(tileSize.x, origin.x + size.x - minimum.x),
                    MathF.Min(tileSize.y, origin.y + size.y - minimum.y));
                if (extent.x <= 0f || extent.y <= 0f)
                    continue;
                AddQuad(
                    owner,
                    source.texture,
                    source.primitive,
                    material,
                    sprite.blendMode,
                    sprite.sampling,
                    sortKey,
                    minimum,
                    minimum + extent,
                    source.region,
                    sprite.flipX,
                    sprite.flipY,
                    0,
                    tint,
                    output,
                    new Rect(0f, 0f, extent.x / tileSize.x, extent.y / tileSize.y));
                generated++;
            }
        }
    }

    private static void AddQuad(
        GameObject owner,
        Rendering2DTextureSource texture,
        SpritePrimitive2D primitive,
        MaterialAsset material,
        SpriteBlendMode2D blendMode,
        SpriteSamplingMode2D sampling,
        Rendering2DSortKey sortKey,
        Vector2 localMinimum,
        Vector2 localMaximum,
        SpriteRegion2D region,
        bool flipX,
        bool flipY,
        int quarterTurns,
        Color tint,
        List<Rendering2DQuad> output,
        Rect? sourceSubset = null)
    {
        Vector2 localBottomLeft = new(localMinimum.x, localMinimum.y);
        Vector2 localBottomRight = new(localMaximum.x, localMinimum.y);
        Vector2 localTopRight = new(localMaximum.x, localMaximum.y);
        Vector2 localTopLeft = new(localMinimum.x, localMaximum.y);
        Vector2 bottomLeft = TransformPoint(owner.transform, localBottomLeft);
        Vector2 bottomRight = TransformPoint(owner.transform, localBottomRight);
        Vector2 topRight = TransformPoint(owner.transform, localTopRight);
        Vector2 topLeft = TransformPoint(owner.transform, localTopLeft);
        Rect subset = sourceSubset ?? new Rect(0f, 0f, 1f, 1f);
        Vector2 uvBottomLeft = GetUv(region, subset.x, subset.y, flipX, flipY, quarterTurns);
        Vector2 uvBottomRight = GetUv(region, subset.x + subset.width, subset.y, flipX, flipY, quarterTurns);
        Vector2 uvTopRight = GetUv(region, subset.x + subset.width, subset.y + subset.height, flipX, flipY, quarterTurns);
        Vector2 uvTopLeft = GetUv(region, subset.x, subset.y + subset.height, flipX, flipY, quarterTurns);
        uint packed = PackColor(tint);
        float z = owner.transform.worldPosition.z;
        output.Add(new Rendering2DQuad(
            material,
            texture,
            blendMode,
            sampling,
            sortKey,
            owner,
            new Rendering2DVertex(bottomLeft.x, bottomLeft.y, z, uvBottomLeft.x, uvBottomLeft.y, packed, (float)primitive),
            new Rendering2DVertex(bottomRight.x, bottomRight.y, z, uvBottomRight.x, uvBottomRight.y, packed, (float)primitive),
            new Rendering2DVertex(topRight.x, topRight.y, z, uvTopRight.x, uvTopRight.y, packed, (float)primitive),
            new Rendering2DVertex(topLeft.x, topLeft.y, z, uvTopLeft.x, uvTopLeft.y, packed, (float)primitive)));
    }

    private static Vector2 GetUv(
        SpriteRegion2D region,
        float sourceX,
        float sourceY,
        bool flipX,
        bool flipY,
        int quarterTurns)
    {
        float x = flipX ? 1f - sourceX : sourceX;
        float y = flipY ? 1f - sourceY : sourceY;
        int turns = ((quarterTurns % 4) + 4) % 4;
        for (int index = 0; index < turns; index++)
            (x, y) = (y, 1f - x);
        float topY = 1f - y;
        if (region.rotatedClockwise)
        {
            return new Vector2(
                region.uvRect.x + (1f - topY) * region.uvRect.width,
                region.uvRect.y + x * region.uvRect.height);
        }
        return new Vector2(
            region.uvRect.x + x * region.uvRect.width,
            region.uvRect.y + topY * region.uvRect.height);
    }

    private static Dictionary<string, List<int>> CollectMaskConsumers(Rendering2DSceneScope scope, Camera2D camera,
        Rendering2DProjectSettings settings, Rect cameraBounds)
    {
        var consumers = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (Rendering2DSceneEntry entry in scope.entries)
        foreach (Rendering2DDrawable drawable in entry.snapshot.drawables)
        {
            if (!drawable.owner.activeInHierarchy || !camera.cullingMask.Contains(drawable.owner.layer)
                || drawable.sprite is not { isActiveAndEnabled: true } sprite
                || sprite.maskInteraction == SpriteMaskInteraction2D.None
                || sprite.material is not { isMissing: false }
                || !TryResolveSprite(sprite, out SpriteSource source)) continue;
            float pixelsPerUnit = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : MathF.Max(0.001f, settings.defaultPixelsPerUnit);
            Vector2 naturalSize = source.primitive == SpritePrimitive2D.None ? source.region.sourceSizePixels / pixelsPerUnit : Vector2.ONE;
            Vector2 size = new(sprite.size.x > 0f ? sprite.size.x : naturalSize.x, sprite.size.y > 0f ? sprite.size.y : naturalSize.y);
            if (!GetTransformedBounds(drawable.owner.transform, new Rect(-source.region.pivot.x * size.x,
                    -source.region.pivot.y * size.y, size.x, size.y), sprite.boundsPadding).Overlaps(cameraBounds)) continue;
            if (!consumers.TryGetValue(sprite.sortingLayer, out List<int>? orders))
                consumers.Add(sprite.sortingLayer, orders = new());
            orders.Add(sprite.orderInLayer);
        }
        foreach (List<int> orders in consumers.Values) orders.Sort();
        return consumers;
    }

    private static Rect GetTransformedBounds(Transform transform, Rect local, float padding = 0f)
    {
        Vector2[] corners =
        [
            TransformPoint(transform, new Vector2(local.left, local.top)),
            TransformPoint(transform, new Vector2(local.right, local.top)),
            TransformPoint(transform, new Vector2(local.right, local.bottom)),
            TransformPoint(transform, new Vector2(local.left, local.bottom))
        ];
        float minimumX = corners.Min(static value => value.x);
        float minimumY = corners.Min(static value => value.y);
        float maximumX = corners.Max(static value => value.x);
        float maximumY = corners.Max(static value => value.y);
        return new Rect(minimumX - padding, minimumY - padding,
            maximumX - minimumX + 2f * padding, maximumY - minimumY + 2f * padding);
    }

    private static Vector2 TransformPoint(Transform transform, Vector2 local)
    {
        Vector3 world = transform.TransformPoint(new Vector3(local.x, local.y, 0f));
        return new Vector2(world.x, world.y);
    }

    private static Color Multiply(Color left, Color right)
        => new(left.r * right.r, left.g * right.g, left.b * right.b, left.a * right.a);

    private static Rendering2DDrawBatch[] BuildBatches(
        IReadOnlyList<Rendering2DQuad> quads,
        int maximumQuadsPerBatch)
    {
        var result = new List<Rendering2DDrawBatch>();
        var persistentSegments = new Dictionary<string, int>(StringComparer.Ordinal);
        int start = 0;
        while (start < quads.Count)
        {
            Rendering2DQuad first = quads[start];
            int count = 1;
            while (start + count < quads.Count
                   && count < maximumQuadsPerBatch
                   && ReferenceEquals(first.material, quads[start + count].material)
                   && first.texture == quads[start + count].texture
                   && first.normalMap == quads[start + count].normalMap
                   && first.emissionMap == quads[start + count].emissionMap
                   && first.emissionColor == quads[start + count].emissionColor
                   && first.lightingLayer == quads[start + count].lightingLayer
                   && first.lightBlendStyles == quads[start + count].lightBlendStyles
                   && first.blendMode == quads[start + count].blendMode
                   && first.sampling == quads[start + count].sampling
                   && first.maskSet == quads[start + count].maskSet
                   && first.maskInteraction == quads[start + count].maskInteraction
                   && string.Equals(
                       first.persistentGroupId,
                       quads[start + count].persistentGroupId,
                       StringComparison.Ordinal))
            {
                count++;
            }
            byte[] instanceBytes = new byte[count * C_INSTANCE_STRIDE];
            for (int quadIndex = 0; quadIndex < count; quadIndex++)
                WriteInstance(instanceBytes, quadIndex, quads[start + quadIndex]);
            RenderPersistentResourceId? persistentBuffer = null;
            if (first.persistentGroupId is string groupId)
            {
                persistentSegments.TryGetValue(groupId, out int segment);
                persistentSegments[groupId] = segment + 1;
                persistentBuffer = new RenderPersistentResourceId($"{groupId}/state-{segment}");
            }
            result.Add(new Rendering2DDrawBatch(
                first.material,
                first.texture,
                first.normalMap,
                first.emissionMap,
                first.blendMode,
                first.sampling,
                instanceBytes,
                count,
                persistentBuffer,
                persistentBuffer is null ? 0 : ComputeInstanceRevision(instanceBytes),
                first.maskSet,
                first.maskInteraction,
                first.emissionColor,
                first.lightingLayer,
                first.lightBlendStyles));
            start += count;
        }
        return result.ToArray();
    }

    private static Rendering2DDrawBatch[] BuildMaskBatches(IReadOnlyList<MaskSnapshot> masks)
    {
        var result = new Rendering2DDrawBatch[masks.Count];
        for (int index = 0; index < masks.Count; index++)
            result[index] = BuildBatches([masks[index].writer], 1)[0];
        return result;
    }

    private static long ComputeInstanceRevision(ReadOnlySpan<byte> bytes)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        unchecked
        {
            ulong hash = offset;
            for (int index = 0; index < bytes.Length; index++)
            {
                hash ^= bytes[index];
                hash *= prime;
            }
            return (long)hash;
        }
    }

    private static Rendering2DPickRecord[] BuildPickRecords(IReadOnlyList<Rendering2DQuad> quads)
    {
        var records = new List<Rendering2DPickRecord>();
        foreach (IGrouping<GameObject, Rendering2DQuad> group in quads
                     .Where(static quad => quad.owner is not null)
                     .GroupBy(static quad => quad.owner!))
        {
            Rect bounds = group.First().GetBounds();
            Rendering2DSortKey sortKey = group.First().sortKey;
            foreach (Rendering2DQuad quad in group.Skip(1))
            {
                bounds = Rect.Union(bounds, quad.GetBounds());
                if (quad.sortKey.CompareTo(sortKey) > 0)
                    sortKey = quad.sortKey;
            }
            records.Add(new Rendering2DPickRecord(group.Key, bounds, sortKey));
        }
        return records.OrderBy(static record => record.sortKey).ToArray();
    }

    private static void WriteInstance(byte[] destination, int index, Rendering2DQuad quad)
    {
        Span<byte> bytes = destination.AsSpan(index * C_INSTANCE_STRIDE, C_INSTANCE_STRIDE);
        WriteVector2(bytes, 0, quad.bottomLeft.x, quad.bottomLeft.y);
        WriteVector2(bytes, 8, quad.bottomRight.x, quad.bottomRight.y);
        WriteVector2(bytes, 16, quad.topLeft.x, quad.topLeft.y);
        WriteVector2(bytes, 24, quad.bottomLeft.u, quad.bottomLeft.v);
        WriteVector2(bytes, 32, quad.bottomRight.u, quad.bottomRight.v);
        WriteVector2(bytes, 40, quad.topLeft.u, quad.topLeft.v);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[48..], quad.bottomLeft.z);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[52..], quad.bottomLeft.shape);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[56..], quad.alphaCutoff);
        uint color = quad.bottomLeft.color;
        BinaryPrimitives.WriteSingleLittleEndian(bytes[64..], (color & 0xffu) / 255f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[68..], ((color >> 8) & 0xffu) / 255f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[72..], ((color >> 16) & 0xffu) / 255f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[76..], ((color >> 24) & 0xffu) / 255f);
    }

    private static void WriteVector2(Span<byte> destination, int offset, float x, float y)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], x);
        BinaryPrimitives.WriteSingleLittleEndian(destination[(offset + sizeof(float))..], y);
    }

    private static uint PackColor(Color color)
    {
        byte red = PackColorChannel(color.r);
        byte green = PackColorChannel(color.g);
        byte blue = PackColorChannel(color.b);
        byte alpha = PackColorChannel(color.a);
        return (uint)red
             | (uint)green << 8
             | (uint)blue << 16
             | (uint)alpha << 24;
    }

    private static byte PackColorChannel(float value)
    {
        if (!float.IsFinite(value))
            return 0;

        return (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * byte.MaxValue);
    }

    private static float[] ToColumnMajor(Matrix matrix)
        =>
        [
            matrix.m11, matrix.m21, matrix.m31, matrix.m41,
            matrix.m12, matrix.m22, matrix.m32, matrix.m42,
            matrix.m13, matrix.m23, matrix.m33, matrix.m43,
            matrix.m14, matrix.m24, matrix.m34, matrix.m44
        ];

    private readonly record struct CameraState(
        Matrix view,
        Matrix projection,
        Rect bounds,
        Vector2 center,
        float halfWidth,
        float halfHeight,
        Quaternion rotation);
    private readonly record struct SpriteSource(
        Rendering2DTextureSource texture,
        Rendering2DTextureSource normalMap,
        Rendering2DTextureSource emissionMap,
        SpriteRegion2D region,
        SpritePrimitive2D primitive);
    private readonly record struct MaskSnapshot(
        string sortingLayer,
        int frontOrder,
        int backOrder,
        Rendering2DQuad writer);
}

internal readonly record struct Rendering2DFrameFingerprint(ulong first, ulong second);

internal struct Rendering2DFingerprintBuilder
{
    private ulong m_first = 14695981039346656037UL;
    private ulong m_second = 1099511628211UL;

    public Rendering2DFingerprintBuilder()
    {
    }

    internal void Add(bool value) => Add(value ? 1UL : 0UL);

    internal void Add(bool? value)
    {
        Add(value.HasValue);
        if (value.HasValue)
            Add(value.Value);
    }

    internal void Add(int value) => Add(unchecked((ulong)(long)value));
    internal void Add(long value) => Add(unchecked((ulong)value));

    internal void Add(ulong value)
    {
        for (int index = 0; index < sizeof(ulong); index++)
            Mix((byte)((value >> (index * 8)) & byte.MaxValue));
    }

    internal void Add(float value) => Add(BitConverter.SingleToUInt32Bits(value));

    internal void Add(string? value)
    {
        if (value is null)
        {
            Add(-1);
            return;
        }
        Add(value.Length);
        for (int index = 0; index < value.Length; index++)
            Add((int)value[index]);
    }

    internal void Add(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = value.TryWriteBytes(bytes);
        for (int index = 0; index < bytes.Length; index++)
            Mix(bytes[index]);
    }

    internal void Add(Vector2 value)
    {
        Add(value.x);
        Add(value.y);
    }

    internal void Add(Rect value)
    {
        Add(value.x);
        Add(value.y);
        Add(value.width);
        Add(value.height);
    }

    internal void Add(Color value)
    {
        Add(value.r);
        Add(value.g);
        Add(value.b);
        Add(value.a);
    }

    internal void Add(Color? value)
    {
        Add(value.HasValue);
        if (value.HasValue)
            Add(value.Value);
    }

    private void Mix(byte value)
    {
        m_first = RotateLeft(m_first, 5) ^ value ^ 0x9e3779b97f4a7c15UL;
        m_second = RotateLeft(m_second, 13)
            ^ ((ulong)value << 32)
            ^ 0xc2b2ae3d27d4eb4fUL;
    }

    private static ulong RotateLeft(ulong value, int offset)
        => (value << offset) | (value >> (64 - offset));

    internal readonly Rendering2DFrameFingerprint Build() => new(m_first, m_second);
}
