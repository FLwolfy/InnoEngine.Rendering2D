using System;
using System.Collections.Generic;
using System.Diagnostics;
using InnoEngine.Logging;
using InnoEditor.Rendering;
using InnoEngine.Mathematics;
using InnoEngine.References;
using InnoEngine.Rendering;
using InnoEngine.Scene;

namespace Inno.Rendering2D;

/// <summary>
/// Contributes an independently navigable 2D model layer and picking controller to the Scene viewport.
/// </summary>
[EditorViewportContributorExtension(
    "Inno.Rendering2D." + nameof(Rendering2DSceneViewportContributor),
    "inno.editor.viewport.scene",
    order: 1000,
    controllerPriority: 100)]
public sealed class Rendering2DSceneViewportContributor : EditorViewportContributor, IDisposable
{
    private const int C_GPU_ACCEPTANCE_WIDTH = 1920;
    private const int C_GPU_ACCEPTANCE_HEIGHT = 1080;

    private RenderPipelineAsset pipeline => Rendering2DRenderer.ResolvePipeline();
    private readonly Rendering2DSceneScopeCache m_scopeCache = new();
    private readonly Rendering2DViewportOptions m_viewportOptions = new()
    {
        drawGrid = true,
        drawAxes = true
    };
    private readonly Rendering2DViewportOptions m_cameraStackOptions = new()
    {
        backbufferOnly = true
    };
    private GameScene? m_editorScene;
    private Camera2D? m_camera;
    private Rendering2DViewportFrame? m_latestFrame;
    private readonly bool m_runPerformanceGate = string.Equals(
        Environment.GetEnvironmentVariable("INNO_RENDERING2D_RUN_PERFORMANCE_GATE"),
        "1",
        StringComparison.Ordinal);
    private readonly bool m_runGpuAcceptance = string.Equals(
        Environment.GetEnvironmentVariable("INNO_RENDERING2D_RUN_GPU_ACCEPTANCE"),
        "1",
        StringComparison.Ordinal);
    private readonly bool m_runScaleGate = string.Equals(
        Environment.GetEnvironmentVariable("INNO_RENDERING2D_RUN_SCALE_GATE"),
        "1",
        StringComparison.Ordinal);
    private bool m_performanceGateCompleted;
    private bool m_gpuAcceptanceFramePublished;
    private bool m_gpuResourceGateArmed;
    private int m_gpuAcceptanceStableFrameCount;
    private ulong m_gpuGateFrame;
    private RenderDeviceAllocationCounters? m_gpuGateAllocations;
    private int m_lastGpuAcceptanceInstanceCount = -1;
    private int m_lastGpuAcceptanceLitInstanceCount = -1;
    private int m_lastGpuAcceptanceLightCount = -1;
    private int m_lastGpuAcceptanceShadowCasterCount = -1;
    private int m_lastGpuAcceptanceBloomLevels = -1;
    private float m_lastGpuAcceptanceBloomIntensity = float.NaN;
    private bool m_disposed;

    /// <summary>
    /// Creates a reload-safe contributor without touching candidate scene component registrations.
    /// </summary>
    public Rendering2DSceneViewportContributor()
    {
    }

    /// <summary>
    /// Determines whether the visible content contains at least one scene that selected the 2D rendering model.
    /// </summary>
    /// <param name="context">
    /// The current Scene viewport context and its explicit content scope.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when at least one scene declares a 2D extraction system.
    /// </returns>
    public override bool CanContribute(EditorViewportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CreateScope(context).scenes.Count != 0;
    }

    /// <summary>
    /// Configures planar navigation from the current 2D camera model and selected object bounds.
    /// </summary>
    /// <param name="context">
    /// The current Scene viewport context and host-owned navigation state.
    /// </param>
    /// <returns>
    /// A planar navigation profile owned by this contributor while it is the selected controller.
    /// </returns>
    public override EditorViewportNavigationProfile ConfigureNavigation(EditorViewportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Rendering2DSceneScope scope = CreateScope(context);
        InitializeNavigation(context.navigation, scope);
        return new EditorViewportNavigationProfile(
            new EditorViewportNavigationProfileId("inno.rendering.2d.navigation.scene"),
            EditorViewportNavigationCapabilities.Planar
                | EditorViewportNavigationCapabilities.FrameSelection,
            EditorViewportNavigationMode.Planar)
        {
            worldUp = Vector3.UP,
            focusBounds = TryGetSelectionBounds(context),
            minimumOrthographicSize = 0.001f,
            maximumOrthographicSize = 100000f
        };
    }

    /// <summary>
    /// Builds the current 2D Scene viewport layer from an isolated Editor camera.
    /// </summary>
    /// <param name="context">
    /// The current Scene viewport context, navigation, and presentation preferences.
    /// </param>
    /// <returns>
    /// The immutable 2D frame contribution and matching transform-manipulation space.
    /// </returns>
    public override EditorViewportContribution Build(EditorViewportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Rendering2DSceneScope scope = CreateScope(context);
        Camera2D camera = GetOrCreateCamera();
        InitializeNavigation(context.navigation, scope);
        camera.transform.worldPosition = context.navigation.position;
        camera.transform.worldRotation = context.navigation.rotation;
        camera.orthographicSize = context.navigation.orthographicSize;
        camera.pixelPerfect = false;
        camera.postProcess = m_runGpuAcceptance
            && Rendering2DRenderer.TryFindPrimaryCamera(scope, out Camera2D? acceptanceCamera, out _)
            && acceptanceCamera is not null
                ? acceptanceCamera.postProcess
                : null;
        m_viewportOptions.clearColorOverride = context.presentation.backgroundColor;
        m_latestFrame = Rendering2DRenderer.CreateViewportFrame(
            scope,
            camera,
            context.pixelWidth,
            context.pixelHeight,
            m_viewportOptions);
        if (m_runGpuAcceptance && !m_gpuAcceptanceFramePublished)
        {
            // Acceptance must observe the runtime camera stack. The Scene viewport camera is
            // user-navigable and persisted, so its frustum is deliberately unrelated to the
            // Game View/Player result and cannot provide deterministic visible-light counts.
            // A fixed 16:9 target also keeps the result independent of the user's dock layout.
            Rendering2DViewportFrame acceptanceFrame = Rendering2DRenderer.CreateCameraStackFrame(
                scope,
                C_GPU_ACCEPTANCE_WIDTH,
                C_GPU_ACCEPTANCE_HEIGHT,
                m_cameraStackOptions);
            Rendering2DFrameStatistics statistics = acceptanceFrame.statistics;
            PublishGpuAcceptanceProgress(statistics);
            if (statistics.litInstanceCount > 0
                && statistics.lightCount >= 32
                && statistics.shadowCasterCount > 0
                && statistics.bloomIntensity > 0f
                && statistics.bloomLevels >= 2)
            {
                m_gpuAcceptanceFramePublished = true;
                Log.Info(
                    $"Rendering2D GPU acceptance frame prepared: {statistics.instanceCount} instances, " +
                    $"{statistics.litInstanceCount} lit instances, {statistics.lightCount} lights, " +
                    $"{statistics.shadowCasterCount} shadow casters, {statistics.bloomLevels}-level Bloom.");
            }
        }
        if (m_runGpuAcceptance && m_gpuAcceptanceFramePublished && !m_gpuResourceGateArmed
            && !Rendering2DGpuAcceptanceProgress.resizePending
            && GraphicsSettings.frameStatistics is RenderFrameStatistics completed && completed.frameIndex != m_gpuGateFrame)
        {
            m_gpuGateFrame = completed.frameIndex;
            bool rendered = completed.viewCount > 0 && completed.drawCount >= 32
                && completed.allocationCounters.HasValue;
            m_gpuAcceptanceStableFrameCount = rendered && completed.allocationCounters.Equals(m_gpuGateAllocations)
                ? m_gpuAcceptanceStableFrameCount + 1 : 0;
            m_gpuGateAllocations = completed.allocationCounters;
            if (m_gpuAcceptanceStableFrameCount >= 120)
            {
                m_gpuResourceGateArmed = true;
                Log.Info("Rendering2D steady-state GPU resource gate armed after 120 rendered frames.");
            }
        }
        if (m_runPerformanceGate && !m_performanceGateCompleted)
        {
            // A failed acceptance probe must remain a single deterministic failure. Re-running
            // hundreds of measured samples every Editor frame obscures the original diagnostic.
            m_performanceGateCompleted = true;
            Rendering2DPerformanceGateResult result = Rendering2DPerformanceGate.MeasureStableExtraction(
                scope,
                camera,
                context.pixelWidth,
                context.pixelHeight,
                m_viewportOptions);
            Log.Info(
                $"Rendering2D performance gate passed: {result.allocatedBytes} managed bytes, " +
                $"P95 {result.p95Milliseconds:F3} ms over {result.sampleCount} stable extractions.");
            if (!Rendering2DRenderer.TryFindPrimaryCamera(scope, out Camera2D? runtimeCamera, out _)
                || runtimeCamera is null)
            {
                throw new InvalidOperationException(
                    "Rendering2D performance validation requires an enabled runtime base camera.");
            }
            Rendering2DPerformanceGateResult runtimeCameraResult = Rendering2DPerformanceGate.MeasureStableExtraction(
                scope,
                runtimeCamera,
                context.pixelWidth,
                context.pixelHeight,
                m_cameraStackOptions);
            Log.Info(
                $"Rendering2D runtime-camera performance gate passed: {runtimeCameraResult.allocatedBytes} managed bytes, " +
                $"P95 {runtimeCameraResult.p95Milliseconds:F3} ms over {runtimeCameraResult.sampleCount} stable extractions.");
            Rendering2DPerformanceGateResult stackResult;
            try
            {
                stackResult = Rendering2DPerformanceGate.MeasureStableCameraStack(
                    scope,
                    context.pixelWidth,
                    context.pixelHeight,
                    m_cameraStackOptions);
            }
            catch (Exception exception)
            {
                Log.Error($"Rendering2D camera-stack performance gate failed: {exception}");
                throw;
            }
            Log.Info(
                $"Rendering2D camera-stack performance gate passed: {stackResult.allocatedBytes} managed bytes, " +
                $"P95 {stackResult.p95Milliseconds:F3} ms over {stackResult.sampleCount} stable extractions.");
            GraphicsCapabilities capabilities = GraphicsSettings.capabilities
                ?? throw new InvalidOperationException(
                    "Rendering2D request-submission validation requires an active rendering device.");
            Rendering2DPerformanceGateResult submissionResult;
            try
            {
                submissionResult = Rendering2DPerformanceGate.MeasureStableRequestSubmission(
                    context.content,
                    capabilities,
                    context.pixelWidth,
                    context.pixelHeight);
            }
            catch (Exception exception)
            {
                Log.Error($"Rendering2D request-submission performance gate failed: {exception}");
                throw;
            }
            Log.Info(
                $"Rendering2D request-submission performance gate passed: " +
                $"{submissionResult.allocatedBytes} managed bytes, " +
                $"P95 {submissionResult.p95Milliseconds:F3} ms over " +
                $"{submissionResult.sampleCount} stable submissions.");
            if (m_runScaleGate)
            {
                Rendering2DPerformanceGateResult scaleResult;
                try
                {
                    scaleResult = Rendering2DScalePerformanceGate.Run(
                        context.pixelWidth,
                        context.pixelHeight);
                }
                catch (Exception exception)
                {
                    Log.Error($"Rendering2D scale gate failed: {exception}");
                    throw;
                }
                Log.Info(
                    $"Rendering2D scale gate passed: 100000 visible tiles in a 1000000-cell sparse domain, " +
                    $"32 lights, {scaleResult.allocatedBytes} managed bytes, " +
                    $"P95 {scaleResult.p95Milliseconds:F3} ms over {scaleResult.sampleCount} stable extractions.");
            }
        }
        return new EditorViewportContribution(
            m_latestFrame.data,
            pipeline,
            manipulationSpace: new EditorViewportManipulationSpace(
                m_latestFrame.viewMatrix,
                m_latestFrame.projectionMatrix,
                isOrthographic: true));
    }

    private void PublishGpuAcceptanceProgress(Rendering2DFrameStatistics statistics)
    {
        if (statistics.instanceCount == m_lastGpuAcceptanceInstanceCount
            && statistics.litInstanceCount == m_lastGpuAcceptanceLitInstanceCount
            && statistics.lightCount == m_lastGpuAcceptanceLightCount
            && statistics.shadowCasterCount == m_lastGpuAcceptanceShadowCasterCount
            && statistics.bloomLevels == m_lastGpuAcceptanceBloomLevels
            && statistics.bloomIntensity == m_lastGpuAcceptanceBloomIntensity)
        {
            return;
        }
        m_lastGpuAcceptanceInstanceCount = statistics.instanceCount;
        m_lastGpuAcceptanceLitInstanceCount = statistics.litInstanceCount;
        m_lastGpuAcceptanceLightCount = statistics.lightCount;
        m_lastGpuAcceptanceShadowCasterCount = statistics.shadowCasterCount;
        m_lastGpuAcceptanceBloomLevels = statistics.bloomLevels;
        m_lastGpuAcceptanceBloomIntensity = statistics.bloomIntensity;
        Log.Info(
            $"Rendering2D GPU acceptance state: {statistics.instanceCount} instances, " +
            $"{statistics.litInstanceCount} lit instances, {statistics.lightCount} lights, " +
            $"{statistics.shadowCasterCount} shadow casters, {statistics.bloomLevels}-level Bloom, " +
            $"intensity {statistics.bloomIntensity:F3}.");
    }

    /// <summary>
    /// Resolves a primary-button click against the latest immutable 2D picking snapshot.
    /// </summary>
    /// <param name="context">
    /// Normalized pointer input and the owning viewport context.
    /// </param>
    public override void HandlePointer(EditorViewportPointerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.button != 0 || m_latestFrame is null)
            return;
        context.viewport.interactions.SetSelection(m_latestFrame.Pick(context.x, context.y)!);
    }

    /// <summary>
    /// Releases the isolated Editor-only scene before the Plugin generation unloads.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        if (m_editorScene is not null && m_camera is not null && !m_camera.gameObject.isDestroyed)
            m_editorScene.DestroyObject(m_camera.gameObject);
        m_camera = null;
        m_editorScene = null;
        m_latestFrame = null;
        GC.SuppressFinalize(this);
    }

    private Camera2D GetOrCreateCamera()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_camera is not null)
            return m_camera;
        var scene = new GameScene("2D Editor View");
        GameObject cameraObject = scene.CreateObject("2D Editor Camera");
        Camera2D camera = cameraObject.AddComponent<Camera2D>();
        camera.primary = false;
        camera.renderToBackbuffer = false;
        camera.composition = CameraComposition2D.Base;
        camera.clearTarget = true;
        camera.clearColor = Color.DARKGRAY;
        camera.pixelPerfect = false;
        camera.cullingMask = GameLayerMask.everything;
        m_editorScene = scene;
        m_camera = camera;
        return camera;
    }

    private Rendering2DSceneScope CreateScope(EditorViewportContext context)
        => m_scopeCache.Get(context.content);

    private static void InitializeNavigation(
        EditorViewportNavigationState navigation,
        Rendering2DSceneScope scope)
    {
        if (navigation.isInitialized)
            return;
        if (!Rendering2DRenderer.TryFindPrimaryCamera(
                scope,
                out Camera2D? runtime,
                out _)
            || runtime is null)
        {
            navigation.ConfigureOrthographic(
                new Vector3(0f, 0f, -10f),
                Quaternion.identity,
                5f);
            navigation.pivot = Vector3.ZERO;
            navigation.focusDistance = 10f;
            return;
        }
        navigation.ConfigureOrthographic(
            runtime.transform.worldPosition,
            runtime.transform.worldRotation,
            MathF.Max(0.01f, runtime.orthographicSize));
        navigation.focusDistance = 10f;
        navigation.pivot = navigation.position
            + Vector3.Transform(Vector3.FORWARD, navigation.rotation) * navigation.focusDistance;
    }

    private static EditorViewportFocusBounds? TryGetSelectionBounds(EditorViewportContext context)
    {
        Transform? transform = context.interactions.selection.selectedTarget switch
        {
            GameObject gameObject when !gameObject.isDestroyed => gameObject.transform,
            Transform selectedTransform when !selectedTransform.isDestroyed => selectedTransform,
            GameComponent component when !component.isDestroyed => component.transform,
            _ => null
        };
        if (transform is null)
            return null;
        Vector3 scale = transform.worldScale;
        float radius = MathF.Max(0.5f, MathF.Max(MathF.Abs(scale.x), MathF.Abs(scale.y)) * 0.5f);
        return new EditorViewportFocusBounds(transform.worldPosition, radius);
    }
}

internal static class Rendering2DScalePerformanceGate
{
    private const int C_DOMAIN_WIDTH = 1000;
    private const int C_DOMAIN_HEIGHT = 1000;
    private const int C_OCCUPIED_CELL_COUNT = 100_000;
    private const int C_LIGHT_COUNT = 32;

    internal static Rendering2DPerformanceGateResult Run(int pixelWidth, int pixelHeight)
    {
        var scene = new GameScene("Rendering2D Scale Acceptance");
        scene.AddSystem<Rendering2DSceneSystem>();

        GameObject cameraObject = scene.CreateObject("Scale Camera");
        Camera2D camera = cameraObject.AddComponent<Camera2D>();
        camera.primary = true;
        camera.renderToBackbuffer = false;
        camera.pixelPerfect = false;
        float viewportAspect = Math.Max(1, pixelWidth) / (float)Math.Max(1, pixelHeight);
        const float domainHalfExtent = 125f;
        const float cullingMargin = 1f;
        camera.orthographicSize = MathF.Max(
            domainHalfExtent + cullingMargin,
            (domainHalfExtent + cullingMargin) / viewportAspect);
        cameraObject.transform.worldPosition = new Vector3(0f, 0f, -10f);

        var texture = new TextureAsset(1, 1, TextureColorSpace.Srgb, "png");
        var tileSet = new TileSet2DAsset();
        tileSet.SetTiles(
        [
            new TileDefinition2D
            {
                id = 1,
                sprite = new SpriteReference2D { texture = texture },
                animation = [],
                rules = [],
                color = Color.WHITE,
                metadata = string.Empty
            }
        ]);
        var tilemap = new Tilemap2DAsset
        {
            tileSet = tileSet,
            cellSize = new Vector2(0.25f, 0.25f),
            chunks = CreateSparseChunks()
        };
        GameObject tilemapObject = scene.CreateObject("100k Visible Sparse Tiles");
        tilemapObject.transform.worldPosition = new Vector3(-125f, -125f, 0f);
        TilemapRenderer2D tilemapRenderer = tilemapObject.AddComponent<TilemapRenderer2D>();
        tilemapRenderer.tilemap = tilemap;
        tilemapRenderer.sampling = SpriteSamplingMode2D.PointClamp;

        for (int index = 0; index < C_LIGHT_COUNT; index++)
        {
            GameObject lightObject = scene.CreateObject($"Scale Light {index + 1:D2}");
            int column = index % 8;
            int row = index / 8;
            lightObject.transform.worldPosition = new Vector3(
                -105f + column * 30f,
                -45f + row * 30f,
                0f);
            Light2D light = lightObject.AddComponent<Light2D>();
            light.kind = LightKind2D.Point;
            light.intensity = 1f;
            light.range = 35f;
            light.blendStyle = (LightBlendStyle2D)(index % 4);
        }

        SceneManager.LoadSceneAdditive(scene, makeActive: false);
        try
        {
            var scope = new Rendering2DSceneScope([scene]);
            var options = new Rendering2DViewportOptions();
            Rendering2DViewportFrame frame = Rendering2DRenderer.CreateViewportFrame(
                scope,
                camera,
                pixelWidth,
                pixelHeight,
                options);
            Rendering2DFrameStatistics statistics = frame.statistics;
            if (statistics.instanceCount != C_OCCUPIED_CELL_COUNT || statistics.lightCount != C_LIGHT_COUNT)
            {
                throw new InvalidOperationException(
                    $"Rendering2D scale frame produced {statistics.instanceCount} instances and " +
                    $"{statistics.lightCount} lights; expected {C_OCCUPIED_CELL_COUNT} and {C_LIGHT_COUNT}.");
            }
            return Rendering2DPerformanceGate.MeasureStableExtraction(
                scope,
                camera,
                pixelWidth,
                pixelHeight,
                options);
        }
        finally
        {
            SceneManager.UnloadScene(scene);
        }
    }

    private static TilemapChunk2D[] CreateSparseChunks()
    {
        const int chunkSize = 32;
        int chunkColumns = (C_DOMAIN_WIDTH + chunkSize - 1) / chunkSize;
        int chunkRows = (C_DOMAIN_HEIGHT + chunkSize - 1) / chunkSize;
        var chunks = new List<TilemapChunk2D>(chunkColumns * chunkRows);
        int occupied = 0;
        for (int chunkY = 0; chunkY < chunkRows; chunkY++)
        {
            int minimumY = chunkY * chunkSize;
            int maximumY = Math.Min(C_DOMAIN_HEIGHT, minimumY + chunkSize);
            for (int chunkX = 0; chunkX < chunkColumns; chunkX++)
            {
                int minimumX = chunkX * chunkSize;
                int maximumX = Math.Min(C_DOMAIN_WIDTH, minimumX + chunkSize);
                var cells = new List<TilemapCell2D>();
                for (int y = minimumY; y < maximumY; y++)
                {
                    for (int x = minimumX; x < maximumX; x++)
                    {
                        if (x % 10 != 0)
                            continue;
                        cells.Add(new TilemapCell2D
                        {
                            x = x,
                            y = y,
                            tileId = 1,
                            color = Color.WHITE
                        });
                        occupied++;
                    }
                }
                if (cells.Count == 0)
                    continue;
                chunks.Add(new TilemapChunk2D
                {
                    layerId = 0,
                    x = chunkX,
                    y = chunkY,
                    cells = cells.ToArray()
                });
            }
        }
        if (occupied != C_OCCUPIED_CELL_COUNT)
            throw new InvalidOperationException($"Scale fixture created {occupied} occupied cells.");
        return chunks.ToArray();
    }
}

internal readonly record struct Rendering2DPerformanceGateResult(
    long allocatedBytes,
    double p95Milliseconds,
    int sampleCount);

internal static class Rendering2DPerformanceGate
{
    private const int C_WARMUP_COUNT = 32;
    private const int C_SAMPLE_COUNT = 240;
    private const long C_MAX_ALLOCATED_BYTES = 0;
    private const double C_MAX_P95_MILLISECONDS = 5d;

    internal static Rendering2DPerformanceGateResult MeasureStableExtraction(
        Rendering2DSceneScope scope,
        Camera2D camera,
        int pixelWidth,
        int pixelHeight,
        Rendering2DViewportOptions options)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(camera);
        for (int index = 0; index < C_WARMUP_COUNT; index++)
            _ = Rendering2DRenderer.CreateViewportFrame(scope, camera, pixelWidth, pixelHeight, options);

        var elapsedTicks = new long[C_SAMPLE_COUNT];
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < elapsedTicks.Length; index++)
        {
            long started = Stopwatch.GetTimestamp();
            _ = Rendering2DRenderer.CreateViewportFrame(scope, camera, pixelWidth, pixelHeight, options);
            elapsedTicks[index] = Stopwatch.GetTimestamp() - started;
        }
        return Validate(elapsedTicks, allocatedBefore, "extraction");
    }

    internal static Rendering2DPerformanceGateResult MeasureStableCameraStack(
        Rendering2DSceneScope scope,
        int pixelWidth,
        int pixelHeight,
        Rendering2DViewportOptions options)
    {
        ArgumentNullException.ThrowIfNull(scope);
        for (int index = 0; index < C_WARMUP_COUNT; index++)
            _ = Rendering2DRenderer.CreateCameraStackFrame(scope, pixelWidth, pixelHeight, options);

        var elapsedTicks = new long[C_SAMPLE_COUNT];
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < elapsedTicks.Length; index++)
        {
            long started = Stopwatch.GetTimestamp();
            _ = Rendering2DRenderer.CreateCameraStackFrame(scope, pixelWidth, pixelHeight, options);
            elapsedTicks[index] = Stopwatch.GetTimestamp() - started;
        }
        return Validate(elapsedTicks, allocatedBefore, "camera-stack extraction");
    }

    internal static Rendering2DPerformanceGateResult MeasureStableRequestSubmission(
        ContentReadScope content,
        GraphicsCapabilities capabilities,
        int pixelWidth,
        int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(capabilities);
        var sink = new StableRequestSink();
        using var provider = new Rendering2DRequestProvider();
        var context = new RenderRequestProviderContext(
            sink,
            content,
            capabilities,
            new RenderPresentationSize(pixelWidth, pixelHeight),
            new RenderViewport(0, 0, pixelWidth, pixelHeight),
            frameIndex: 1,
            deltaTime: 1f / 60f);
        for (int index = 0; index < C_WARMUP_COUNT; index++)
            provider.Submit(context);

        var elapsedTicks = new long[C_SAMPLE_COUNT];
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < elapsedTicks.Length; index++)
        {
            long started = Stopwatch.GetTimestamp();
            provider.Submit(context);
            elapsedTicks[index] = Stopwatch.GetTimestamp() - started;
        }
        if (sink.submissionCount != C_WARMUP_COUNT + C_SAMPLE_COUNT)
        {
            throw new InvalidOperationException(
                $"Rendering2D stable request provider submitted {sink.submissionCount} requests; " +
                $"expected {C_WARMUP_COUNT + C_SAMPLE_COUNT}.");
        }
        return Validate(elapsedTicks, allocatedBefore, "request extraction/submission");
    }

    private static Rendering2DPerformanceGateResult Validate(
        long[] elapsedTicks,
        long allocatedBefore,
        string operation)
    {
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Array.Sort(elapsedTicks);
        int p95Index = Math.Clamp(
            (int)Math.Ceiling(elapsedTicks.Length * 0.95d) - 1,
            0,
            elapsedTicks.Length - 1);
        double p95Milliseconds = elapsedTicks[p95Index] * 1000d / Stopwatch.Frequency;
        if (allocatedBytes > C_MAX_ALLOCATED_BYTES)
        {
            throw new InvalidOperationException(
                $"Rendering2D stable {operation} allocated {allocatedBytes} managed bytes; " +
                $"the enforced budget is {C_MAX_ALLOCATED_BYTES} bytes.");
        }
        if (p95Milliseconds > C_MAX_P95_MILLISECONDS)
        {
            throw new InvalidOperationException(
                $"Rendering2D stable {operation} P95 was {p95Milliseconds:F3} ms; " +
                $"the enforced budget is {C_MAX_P95_MILLISECONDS:F3} ms.");
        }
        return new Rendering2DPerformanceGateResult(
            allocatedBytes,
            p95Milliseconds,
            elapsedTicks.Length);
    }

    private sealed class StableRequestSink : IRenderRequestSink
    {
        internal int submissionCount { get; private set; }

        public void Submit(RenderRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            submissionCount++;
        }
    }
}
