using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using InnoEngine.Assets;
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
    internal required Rendering2DDrawBatch[] batches { get; init; }
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

internal sealed record Rendering2DDrawBatch(
    MaterialAsset material,
    TextureAsset? texture,
    SpriteBlendMode2D blendMode,
    SpriteSamplingMode2D sampling,
    byte[] vertexBytes,
    byte[] indexBytes,
    int indexCount);

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
    TextureAsset? texture,
    SpriteBlendMode2D blendMode,
    SpriteSamplingMode2D sampling,
    Rendering2DSortKey sortKey,
    GameObject? owner,
    Rendering2DVertex bottomLeft,
    Rendering2DVertex bottomRight,
    Rendering2DVertex topRight,
    Rendering2DVertex topLeft)
{
    internal Rect GetBounds()
    {
        float minimumX = MathF.Min(MathF.Min(bottomLeft.x, bottomRight.x), MathF.Min(topRight.x, topLeft.x));
        float minimumY = MathF.Min(MathF.Min(bottomLeft.y, bottomRight.y), MathF.Min(topRight.y, topLeft.y));
        float maximumX = MathF.Max(MathF.Max(bottomLeft.x, bottomRight.x), MathF.Max(topRight.x, topLeft.x));
        float maximumY = MathF.Max(MathF.Max(bottomLeft.y, bottomRight.y), MathF.Max(topRight.y, topLeft.y));
        return new Rect(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
    }
}

internal static class Rendering2DFrameCollector
{
    private const int C_VERTEX_STRIDE = 28;
    private static readonly AssetPath S_DEFAULT_MATERIAL_PATH = Assets.LocalPath(
        "Materials/DefaultSprite.imaterial");

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
        if (!Assets.TryLoad<MaterialAsset>(S_DEFAULT_MATERIAL_PATH, out MaterialAsset? defaultMaterial)
            || defaultMaterial is null)
        {
            diagnostics.Add("The default 2D sprite material is unavailable.");
            return Empty(
                camera,
                pixelWidth,
                pixelHeight,
                settings.defaultPixelsPerUnit,
                diagnostics,
                options);
        }

        CameraState cameraState = CreateCameraState(
            camera,
            pixelWidth,
            pixelHeight,
            settings.defaultPixelsPerUnit);
        List<LightSnapshot> lights = CollectLights(scope, camera);
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
                        defaultMaterial,
                        settings,
                        cameraState.bounds,
                        lights,
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
            batches = BuildBatches(quads, Math.Max(1, settings.maximumQuadsPerBatch)),
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
            batches = [],
            pickRecords = [],
            diagnostics = diagnostics.ToArray()
        };
    }

    private static Rendering2DProjectSettings GetSettings()
    {
        return Settings.TryGet<Rendering2DProjectSettings>(
            Rendering2DProjectSettings.id,
            out Rendering2DProjectSettings settings)
            && settings is not null
                ? settings
                : new Rendering2DProjectSettings();
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

    private static List<LightSnapshot> CollectLights(Rendering2DSceneScope scope, Camera2D camera)
    {
        var lights = new List<LightSnapshot>();
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
                lights.Add(new LightSnapshot(
                    light.kind,
                    new Vector2(position.x, position.y),
                    direction,
                    light.color,
                    MathF.Max(0f, light.intensity),
                    MathF.Max(0.0001f, light.range),
                    Math.Clamp(light.spotAngle, 0.1f, 179.9f) * MathF.PI / 180f,
                    MathF.Max(0.01f, light.falloff),
                    light.cullingMask));
            }
        }
        return lights;
    }

    private static void CollectViewportGuides(
        CameraState camera,
        int pixelHeight,
        MaterialAsset material,
        Rendering2DViewportOptions? options,
        List<Rendering2DQuad> output,
        ref int sequence)
    {
        if (options is null || (!options.drawGrid && !options.drawAxes))
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
            null,
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
        MaterialAsset defaultMaterial,
        Rendering2DProjectSettings settings,
        Rect cameraBounds,
        IReadOnlyList<LightSnapshot> lights,
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
            size.y));
        if (!coarseBounds.Overlaps(cameraBounds))
            return;

        Color tint = ApplyLighting(
            Multiply(sprite.color, Color.WHITE),
            owner.transform.worldPosition,
            owner.layer,
            sprite.receiveLighting,
            lights);
        MaterialAsset material = sprite.material ?? defaultMaterial;
        Rendering2DSortKey sortKey = new(
            1,
            settings.GetSortingLayerOrder(sprite.sortingLayerId),
            sprite.orderInLayer,
            owner.transform.worldPosition.z,
            sequence++);
        int before = output.Count;
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
                    Math.Max(1, settings.maximumTiledSpriteQuads),
                    output);
                break;
        }
        if (output.Count > outputLimit)
            output.RemoveRange(outputLimit, output.Count - outputLimit);
        if (output.Count == before)
            diagnostics.Add($"Sprite '{owner.name}' generated no visible geometry.");
    }

    private static void CollectTilemap(
        GameObject owner,
        TilemapRenderer2D renderer,
        MaterialAsset defaultMaterial,
        Rendering2DProjectSettings settings,
        Rect cameraBounds,
        IReadOnlyList<LightSnapshot> lights,
        List<Rendering2DQuad> output,
        List<string> diagnostics,
        int outputLimit,
        ref int sequence)
    {
        Tilemap2DAsset? map = renderer.tilemap;
        TileSet2DAsset? tileSet = map?.tileSet;
        SpriteAtlas2DAsset? atlas = tileSet?.atlas;
        if (map is null || tileSet is null || atlas?.texture is null)
        {
            diagnostics.Add($"Tilemap '{owner.name}' has no complete Tilemap, TileSet, Atlas, and Texture chain.");
            return;
        }
        MaterialAsset material = renderer.material ?? defaultMaterial;
        Vector2 cellSize = new(MathF.Max(0.0001f, map.cellSize.x), MathF.Max(0.0001f, map.cellSize.y));
        foreach (TilemapCell2D cell in map.cells)
        {
            if (output.Count >= outputLimit)
                break;
            if (!tileSet.TryGetTile(cell.tileId, out TileDefinition2D tile)
                || !atlas.TryGetRegion(tile.spriteId, out SpriteRegion2D region))
            {
                diagnostics.Add($"Tilemap '{owner.name}' references undefined tile or sprite data.");
                continue;
            }
            Vector2 localMinimum = new(cell.x * cellSize.x, cell.y * cellSize.y);
            Rect worldBounds = GetTransformedBounds(
                owner.transform,
                new Rect(localMinimum.x, localMinimum.y, cellSize.x, cellSize.y));
            if (!worldBounds.Overlaps(cameraBounds))
                continue;
            Vector2 localCenter = localMinimum + cellSize * 0.5f;
            Vector2 worldCenter = TransformPoint(owner.transform, localCenter);
            Color tint = Multiply(Multiply(renderer.color, tile.color), cell.color);
            tint = ApplyLighting(
                tint,
                new Vector3(worldCenter.x, worldCenter.y, owner.transform.worldPosition.z),
                owner.layer,
                renderer.receiveLighting,
                lights);
            Rendering2DSortKey sortKey = new(
                1,
                settings.GetSortingLayerOrder(renderer.sortingLayerId),
                renderer.orderInLayer + cell.layer,
                owner.transform.worldPosition.z,
                sequence++);
            AddQuad(
                owner,
                atlas.texture,
                SpritePrimitive2D.None,
                material,
                renderer.blendMode,
                renderer.sampling,
                sortKey,
                localMinimum,
                localMinimum + cellSize,
                region,
                cell.flipX,
                cell.flipY,
                cell.quarterTurns,
                tint,
                output);
        }
    }

    private static bool TryResolveSprite(SpriteRenderer2D sprite, out SpriteSource source)
    {
        if (sprite.atlas?.texture is TextureAsset atlasTexture
            && sprite.atlas.TryGetRegion(sprite.spriteId, out SpriteRegion2D region))
        {
            source = new SpriteSource(atlasTexture, region, SpritePrimitive2D.None);
            return true;
        }
        if (sprite.texture is TextureAsset texture && texture.width > 0 && texture.height > 0)
        {
            source = new SpriteSource(texture, new SpriteRegion2D
            {
                id = "direct",
                uvRect = new Rect(0f, 0f, 1f, 1f),
                sourceSizePixels = new Vector2(texture.width, texture.height),
                trimmedSizePixels = new Vector2(texture.width, texture.height),
                trimOffsetPixels = Vector2.ZERO,
                pivot = sprite.pivot,
                borderPixels = default
            }, SpritePrimitive2D.None);
            return true;
        }
        if (sprite.primitive != SpritePrimitive2D.None)
        {
            source = new SpriteSource(null, new SpriteRegion2D
            {
                id = $"builtin:{sprite.primitive}",
                uvRect = new Rect(0f, 0f, 1f, 1f),
                sourceSizePixels = Vector2.ONE,
                trimmedSizePixels = Vector2.ONE,
                trimOffsetPixels = Vector2.ZERO,
                pivot = sprite.pivot,
                borderPixels = default
            }, sprite.primitive);
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
        TextureAsset? texture,
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

    private static Rect GetTransformedBounds(Transform transform, Rect local)
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
        return new Rect(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
    }

    private static Vector2 TransformPoint(Transform transform, Vector2 local)
    {
        Vector3 world = transform.TransformPoint(new Vector3(local.x, local.y, 0f));
        return new Vector2(world.x, world.y);
    }

    private static Color ApplyLighting(
        Color source,
        Vector3 worldPosition,
        GameLayer layer,
        bool receiveLighting,
        IReadOnlyList<LightSnapshot> lights)
    {
        if (!receiveLighting || lights.Count == 0)
            return source;
        float red = 0f;
        float green = 0f;
        float blue = 0f;
        Vector2 point = new(worldPosition.x, worldPosition.y);
        foreach (LightSnapshot light in lights)
        {
            if (!light.layers.Contains(layer))
                continue;
            float contribution = light.intensity;
            if (light.kind != LightKind2D.Global)
            {
                Vector2 delta = point - light.position;
                float distance = delta.Length();
                if (distance >= light.range)
                    continue;
                contribution *= MathF.Pow(1f - distance / light.range, light.falloff);
                if (light.kind == LightKind2D.Spot && distance > 0.0001f)
                {
                    float cosine = Vector2.Dot(delta / distance, light.direction);
                    float edge = MathF.Cos(light.spotAngle * 0.5f);
                    if (cosine <= edge)
                        continue;
                    contribution *= (cosine - edge) / MathF.Max(0.0001f, 1f - edge);
                }
            }
            red += light.color.r * contribution;
            green += light.color.g * contribution;
            blue += light.color.b * contribution;
        }
        return new Color(source.r * red, source.g * green, source.b * blue, source.a);
    }

    private static Color Multiply(Color left, Color right)
        => new(left.r * right.r, left.g * right.g, left.b * right.b, left.a * right.a);

    private static Rendering2DDrawBatch[] BuildBatches(
        IReadOnlyList<Rendering2DQuad> quads,
        int maximumQuadsPerBatch)
    {
        var result = new List<Rendering2DDrawBatch>();
        int start = 0;
        while (start < quads.Count)
        {
            Rendering2DQuad first = quads[start];
            int count = 1;
            while (start + count < quads.Count
                   && count < maximumQuadsPerBatch
                   && ReferenceEquals(first.material, quads[start + count].material)
                   && ReferenceEquals(first.texture, quads[start + count].texture)
                   && first.blendMode == quads[start + count].blendMode
                   && first.sampling == quads[start + count].sampling)
            {
                count++;
            }
            byte[] vertexBytes = new byte[count * 4 * C_VERTEX_STRIDE];
            byte[] indexBytes = new byte[count * 6 * sizeof(uint)];
            for (int quadIndex = 0; quadIndex < count; quadIndex++)
            {
                Rendering2DQuad quad = quads[start + quadIndex];
                int vertexBase = quadIndex * 4;
                WriteVertex(vertexBytes, vertexBase, quad.bottomLeft);
                WriteVertex(vertexBytes, vertexBase + 1, quad.bottomRight);
                WriteVertex(vertexBytes, vertexBase + 2, quad.topRight);
                WriteVertex(vertexBytes, vertexBase + 3, quad.topLeft);
                int indexOffset = quadIndex * 6 * sizeof(uint);
                WriteIndex(indexBytes, indexOffset, (uint)vertexBase);
                WriteIndex(indexBytes, indexOffset + 4, (uint)(vertexBase + 1));
                WriteIndex(indexBytes, indexOffset + 8, (uint)(vertexBase + 2));
                WriteIndex(indexBytes, indexOffset + 12, (uint)vertexBase);
                WriteIndex(indexBytes, indexOffset + 16, (uint)(vertexBase + 2));
                WriteIndex(indexBytes, indexOffset + 20, (uint)(vertexBase + 3));
            }
            result.Add(new Rendering2DDrawBatch(
                first.material,
                first.texture,
                first.blendMode,
                first.sampling,
                vertexBytes,
                indexBytes,
                count * 6));
            start += count;
        }
        return result.ToArray();
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

    private static void WriteVertex(byte[] destination, int index, Rendering2DVertex value)
    {
        Span<byte> bytes = destination.AsSpan(index * C_VERTEX_STRIDE, C_VERTEX_STRIDE);
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value.x);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[4..], value.y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[8..], value.z);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[12..], value.u);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[16..], value.v);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[20..], value.color);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[24..], value.shape);
    }

    private static void WriteIndex(byte[] destination, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset, sizeof(uint)), value);

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
        TextureAsset? texture,
        SpriteRegion2D region,
        SpritePrimitive2D primitive);
    private readonly record struct LightSnapshot(
        LightKind2D kind,
        Vector2 position,
        Vector2 direction,
        Color color,
        float intensity,
        float range,
        float spotAngle,
        float falloff,
        GameLayerMask layers);
}
