using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using InnoEngine.Assets;
using InnoEngine.Core;
using InnoEngine.Logging;
using InnoEngine.Mathematics;
using InnoEngine.References;
using InnoEngine.Rendering;
using InnoEngine.Settings;
using InnoEngine.Scene;

namespace Inno.Rendering2D;

/// <summary>Configures host presentation helpers for one explicitly collected 2D viewport.</summary>
public sealed class Rendering2DViewportOptions
{
    /// <summary>Gets or sets an optional clear-color override for this viewport only.</summary>
    public Color? clearColorOverride { get; set; }

    /// <summary>Gets or sets an optional load/clear override for camera composition.</summary>
    public bool? clearTargetOverride { get; set; }

    /// <summary>Gets or sets diagnostics contributed by camera selection or viewport composition.</summary>
    public IReadOnlyList<string>? additionalDiagnostics { get; set; }

    /// <summary>Gets or sets whether only cameras explicitly targeting the backbuffer participate in stack selection.</summary>
    public bool backbufferOnly { get; set; }

    /// <summary>Gets or sets whether an adaptive world-space grid is drawn behind scene content.</summary>
    public bool drawGrid { get; set; }

    /// <summary>Gets or sets whether world-space X and Y axes are drawn behind scene content.</summary>
    public bool drawAxes { get; set; }
}

/// <summary>
/// Defines an ordered, explicit set of indexed scenes visible to one 2D render operation.
/// </summary>
public sealed class Rendering2DSceneScope
{
    private readonly GameScene[] m_scenes;
    private readonly Rendering2DSceneEntry[] m_entries;
    internal readonly object cacheGate = new();
    internal Rendering2DFrameFingerprint allCameraPlanFingerprint;
    internal Rendering2DFrameFingerprint backbufferCameraPlanFingerprint;
    internal Rendering2DRenderer.CameraPlan? allCameraPlan;
    internal Rendering2DRenderer.CameraPlan? backbufferCameraPlan;
    internal Rendering2DFrameFingerprint stackFrameFingerprint;
    internal Rendering2DViewportFrame? stackFrame;

    /// <summary>
    /// Creates an immutable explicit scene scope containing only scenes that declare a 2D extraction system.
    /// </summary>
    /// <param name="scenes">
    /// Ordered candidate scenes visible to the render operation. Scenes without a 2D extraction system do not
    /// participate and remain available to other rendering models.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a participating scene contains more than one <see cref="Rendering2DSceneSystem"/>.
    /// </exception>
    public Rendering2DSceneScope(IEnumerable<GameScene> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        GameScene[] candidates = scenes
            .Where(static scene => scene is not null && !scene.isDestroyed)
            .Distinct()
            .ToArray();
        var participatingScenes = new List<GameScene>(candidates.Length);
        var entries = new List<Rendering2DSceneEntry>(candidates.Length);
        for (int sceneIndex = 0; sceneIndex < candidates.Length; sceneIndex++)
        {
            GameScene scene = candidates[sceneIndex];
            Rendering2DSceneSystem? extraction = null;
            IReadOnlyList<GameSystem> systems = scene.GetSystems();
            for (int systemIndex = 0; systemIndex < systems.Count; systemIndex++)
            {
                if (systems[systemIndex] is not Rendering2DSceneSystem candidate)
                    continue;
                if (extraction is not null)
                {
                    throw new InvalidOperationException(
                        $"Scene '{scene.name}' contains more than one {nameof(Rendering2DSceneSystem)}.");
                }
                extraction = candidate;
            }
            if (extraction is null)
                continue;
            participatingScenes.Add(scene);
            entries.Add(new Rendering2DSceneEntry(scene, extraction, extraction.Capture()));
        }
        m_scenes = participatingScenes.ToArray();
        m_entries = entries.ToArray();
    }

    /// <summary>
    /// Gets the ordered immutable scene snapshot.
    /// </summary>
    public IReadOnlyList<GameScene> scenes => m_scenes;

    internal IReadOnlyList<Rendering2DSceneEntry> entries => m_entries;

    internal void Refresh()
    {
        for (int index = 0; index < m_entries.Length; index++)
        {
            Rendering2DSceneEntry entry = m_entries[index];
            m_entries[index] = new Rendering2DSceneEntry(
                entry.scene,
                entry.extraction,
                entry.extraction.Capture());
        }
    }
}

internal readonly record struct Rendering2DSceneEntry(
    GameScene scene,
    Rendering2DSceneSystem extraction,
    Rendering2DSceneSnapshot snapshot);

/// <summary>
/// Reuses a 2D scene scope while host-selected content generations and scene system membership remain unchanged.
/// </summary>
public sealed class Rendering2DSceneScopeCache
{
    private Identity[] m_roots = [];
    private IReadOnlyList<GameSystem>?[] m_systems = [];
    private Rendering2DSceneScope? m_scope;

    /// <summary>
    /// Resolves the current content roots and refreshes structure-indexed 2D snapshots without allocating in
    /// the stable-content path.
    /// </summary>
    /// <param name="content">Frame-scoped host content.</param>
    /// <returns>The reusable scope matching the current root generations and system snapshots.</returns>
    public Rendering2DSceneScope Get(ContentReadScope content)
    {
        ArgumentNullException.ThrowIfNull(content);
        IReadOnlyList<Identity> roots = content.contents;
        if (!Matches(roots))
        {
            var scenes = new List<GameScene>(roots.Count);
            var capturedRoots = new Identity[roots.Count];
            var capturedSystems = new IReadOnlyList<GameSystem>?[roots.Count];
            for (int index = 0; index < roots.Count; index++)
            {
                Identity root = roots[index];
                capturedRoots[index] = root;
                GameScene? scene = root.Resolve<GameScene>();
                if (scene is not null && !scene.isDestroyed)
                {
                    capturedSystems[index] = scene.GetSystems();
                    scenes.Add(scene);
                }
            }
            m_roots = capturedRoots;
            m_systems = capturedSystems;
            m_scope = new Rendering2DSceneScope(scenes);
        }
        else
        {
            m_scope!.Refresh();
        }
        return m_scope!;
    }

    private bool Matches(IReadOnlyList<Identity> roots)
    {
        if (m_scope is null || roots.Count != m_roots.Length)
            return false;
        for (int index = 0; index < roots.Count; index++)
        {
            Identity previous = m_roots[index];
            Identity current = roots[index];
            var previousRuntimeIdentity = previous.runtimeIdentity;
            var currentRuntimeIdentity = current.runtimeIdentity;
            if (previous.persistentId != current.persistentId
                || previousRuntimeIdentity.HasValue != currentRuntimeIdentity.HasValue
                || (previousRuntimeIdentity.HasValue
                    && previousRuntimeIdentity.GetValueOrDefault()
                    != currentRuntimeIdentity.GetValueOrDefault()))
            {
                return false;
            }
            GameScene? scene = current.Resolve<GameScene>();
            IReadOnlyList<GameSystem>? systems = scene is not null && !scene.isDestroyed ? scene.GetSystems() : null;
            // GetSystems returns a cached immutable snapshot until membership or order changes.
            // Include non-participating roots so adding the first renderer also invalidates the scope.
            if (!ReferenceEquals(m_systems[index], systems))
                return false;
        }
        return true;
    }
}

/// <summary>Creates explicit 2D requests while keeping camera ownership in the Plugin.</summary>
public static class Rendering2DRenderer
{
    private static readonly ConditionalWeakTable<Camera2D, ViewportFrameCache> S_VIEWPORT_FRAMES = new();
    private static readonly List<PipelineSettingsCache> S_PIPELINE_SETTINGS = [];
    private static readonly object S_SETTINGS_LOCK = new();
    private static Guid s_settingsOwner;
    private static long s_settingsRevision = -1;
    private static Rendering2DProjectSettings? s_settings;
    private static RenderPipelineAsset runtimePipeline => ResolvePipeline();
    internal static RenderPipelineAsset sharedPipeline => runtimePipeline;

    internal static Rendering2DProjectSettings projectSettings
    {
        get
        {
            lock (S_SETTINGS_LOCK)
            {
                Guid owner = Settings.ownerId;
                long revision = Settings.revision;
                if (s_settings is null || s_settingsOwner != owner || s_settingsRevision != revision)
                {
                    s_settings = Settings.Get<Rendering2DProjectSettings>(Rendering2DProjectSettings.id);
                    s_settingsOwner = owner;
                    s_settingsRevision = revision;
                }
                return s_settings;
            }
        }
    }

    /// <summary>Resolves the explicitly configured Pipeline shared by Scene, Game and Player.</summary>
    /// <returns>The canonical Pipeline asset owned by the current settings context.</returns>
    public static RenderPipelineAsset ResolvePipeline()
    {
        RenderPipelineAsset? pipeline = projectSettings.pipeline;
        if (pipeline is null || pipeline.isMissing || pipeline.pipelineTypeId != Rendering2DIds.pipeline)
            throw new InvalidOperationException("Choose a valid 2D Pipeline asset in Project Settings / Rendering / 2D. No transient or alternate Pipeline is substituted.");
        return pipeline;
    }

    internal static Rendering2DPipelineSettings pipelineSettings
    {
        get
        {
            RenderPipelineAsset pipeline = ResolvePipeline();
            lock (S_SETTINGS_LOCK)
            {
                PipelineSettingsCache? cache = null;
                for (int index = S_PIPELINE_SETTINGS.Count - 1; index >= 0; index--)
                {
                    PipelineSettingsCache candidate = S_PIPELINE_SETTINGS[index];
                    RenderPipelineAsset? owner = candidate.identity.Resolve<RenderPipelineAsset>();
                    if (owner is null)
                        S_PIPELINE_SETTINGS.RemoveAt(index);
                    else if (ReferenceEquals(owner, pipeline))
                        cache = candidate;
                }
                if (cache is null)
                {
                    // A host-owned key in a static ephemeron table can keep its collectible value
                    // and the table's defining assembly alive. Resolve ownership through Identity instead.
                    cache = new PipelineSettingsCache(pipeline.identity);
                    S_PIPELINE_SETTINGS.Add(cache);
                }
                if (cache.value is null || cache.version != pipeline.contentVersion)
                {
                    var value = new Rendering2DPipelineSettings();
                    pipeline.RestoreProperties(pipeline.pipelineState.stableTypeId, pipeline.pipelineState.propertyData, value);
                    cache.value = value;
                    cache.version = pipeline.contentVersion;
                }
                return cache.value;
            }
        }
    }

    private sealed class PipelineSettingsCache(Identity identity)
    {
        internal readonly Identity identity = identity;
        internal long version = -1;
        internal Rendering2DPipelineSettings? value;
    }

    /// <summary>Builds immutable frame data for one 2D camera and destination size.</summary>
    /// <param name="scope">Explicit ordered scenes visible to this operation.</param>
    /// <param name="camera">Camera whose scoped view should be collected.</param>
    /// <param name="pixelWidth">Positive target width.</param>
    /// <param name="pixelHeight">Positive target height.</param>
    /// <param name="options">Optional viewport-only presentation helpers.</param>
    /// <returns>Frame-only data accepted by the 2D pipeline.</returns>
    public static RenderFrameData CreateFrameData(
        Rendering2DSceneScope scope,
        Camera2D camera,
        int pixelWidth,
        int pixelHeight,
        Rendering2DViewportOptions? options = null)
    {
        return CreateViewportFrame(scope, camera, pixelWidth, pixelHeight, options).data;
    }

    /// <summary>Builds one composited Base/Overlay frame sequence for an explicit scene scope.</summary>
    /// <param name="scope">Explicit ordered scenes visible to this operation.</param>
    /// <param name="pixelWidth">Positive target width.</param>
    /// <param name="pixelHeight">Positive target height.</param>
    /// <param name="options">Optional viewport-only presentation helpers.</param>
    /// <returns>A frame wrapper containing the deterministic selected camera stack.</returns>
    public static Rendering2DViewportFrame CreateCameraStackFrame(
        Rendering2DSceneScope scope,
        int pixelWidth,
        int pixelHeight,
        Rendering2DViewportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        scope.Refresh();
        bool backbufferOnly = options?.backbufferOnly ?? false;
        Rendering2DFrameFingerprint planFingerprint = ComputeCameraPlanFingerprint(scope, backbufferOnly);
        CameraPlan? plan;
        lock (scope.cacheGate)
        {
            CameraPlan? cached = backbufferOnly ? scope.backbufferCameraPlan : scope.allCameraPlan;
            Rendering2DFrameFingerprint cachedFingerprint = backbufferOnly
                ? scope.backbufferCameraPlanFingerprint
                : scope.allCameraPlanFingerprint;
            plan = cached is not null && cachedFingerprint == planFingerprint
                ? cached
                : null;
        }
        plan ??= BuildCameraPlan(scope, backbufferOnly);
        if (plan.cameras.Length == 0)
            throw new InvalidOperationException("No enabled 2D base camera is available in the explicit scene scope.");
        Rendering2DFrameFingerprint contentFingerprint = Rendering2DFrameCollector.ComputeFingerprint(
            scope,
            plan.cameras[0],
            pixelWidth,
            pixelHeight,
            options);
        var stackFingerprintBuilder = new Rendering2DFingerprintBuilder();
        stackFingerprintBuilder.Add(contentFingerprint.first);
        stackFingerprintBuilder.Add(contentFingerprint.second);
        stackFingerprintBuilder.Add(plan.fingerprint.first);
        stackFingerprintBuilder.Add(plan.fingerprint.second);
        Rendering2DFrameFingerprint stackFingerprint = stackFingerprintBuilder.Build();
        Rendering2DViewportFrame? cachedFrame = null;
        lock (scope.cacheGate)
        {
            if (scope.stackFrame is not null && scope.stackFrameFingerprint == stackFingerprint)
                cachedFrame = scope.stackFrame;
        }
        if (cachedFrame is not null)
            return cachedFrame;

        Rendering2DFrame[] frames = new Rendering2DFrame[plan.cameras.Length];
        string[] firstFrameDiagnostics = CombineDiagnostics(
            plan.diagnostics,
            options?.additionalDiagnostics);
        for (int index = 0; index < plan.cameras.Length; index++)
        {
            Camera2D camera = plan.cameras[index];
            frames[index] = Rendering2DFrameCollector.Collect(
                scope,
                camera,
                pixelWidth,
                pixelHeight,
                new Rendering2DViewportOptions
                {
                    clearColorOverride = options?.clearColorOverride,
                    clearTargetOverride = index == 0
                        ? options?.clearTargetOverride ?? camera.clearTarget
                        : false,
                    additionalDiagnostics = index == 0
                        ? firstFrameDiagnostics
                        : null,
                    backbufferOnly = options?.backbufferOnly ?? false,
                    drawGrid = options?.drawGrid ?? false,
                    drawAxes = options?.drawAxes ?? false
                });
        }
        var result = new Rendering2DViewportFrame(frames);
        lock (scope.cacheGate)
        {
            scope.stackFrameFingerprint = stackFingerprint;
            scope.stackFrame = result;
        }
        return result;
    }

    /// <summary>Collects one immutable 2D viewport frame for rendering and CPU picking.</summary>
    /// <param name="scope">Explicit ordered scenes visible to this operation.</param>
    /// <param name="camera">Camera whose scoped view should be collected.</param>
    /// <param name="pixelWidth">Positive target width.</param>
    /// <param name="pixelHeight">Positive target height.</param>
    /// <param name="options">Optional viewport-only presentation helpers.</param>
    /// <returns>A frame wrapper whose data can be submitted and whose pick method uses the same snapshot.</returns>
    public static Rendering2DViewportFrame CreateViewportFrame(
        Rendering2DSceneScope scope,
        Camera2D camera,
        int pixelWidth,
        int pixelHeight,
        Rendering2DViewportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(camera);
        scope.Refresh();
        Rendering2DFrameFingerprint fingerprint = Rendering2DFrameCollector.ComputeFingerprint(
            scope,
            camera,
            pixelWidth,
            pixelHeight,
            options);
        ViewportFrameCache cache = S_VIEWPORT_FRAMES.GetValue(camera, static _ => new ViewportFrameCache());
        lock (cache)
        {
            if (cache.frame is not null && cache.fingerprint == fingerprint)
                return cache.frame;
            var frame = new Rendering2DViewportFrame(
                Rendering2DFrameCollector.Collect(scope, camera, pixelWidth, pixelHeight, options));
            cache.fingerprint = fingerprint;
            cache.frame = frame;
            return frame;
        }
    }

    /// <summary>Creates one explicit backbuffer or offscreen 2D request.</summary>
    /// <param name="scope">Explicit ordered scenes visible to this operation.</param>
    /// <param name="camera">Camera whose scoped view should be collected.</param>
    /// <param name="target">Backbuffer or offscreen destination.</param>
    /// <param name="viewport">Positive destination viewport.</param>
    /// <param name="options">Optional composition and presentation overrides.</param>
    /// <param name="name">Optional diagnostic name.</param>
    /// <param name="priority">Optional scheduling priority; camera priority is used when omitted.</param>
    /// <returns>An immutable request selecting the 2D pipeline.</returns>
    public static RenderRequest CreateRequest(
        Rendering2DSceneScope scope,
        Camera2D camera,
        RenderTarget target,
        RenderViewport viewport,
        Rendering2DViewportOptions? options = null,
        string? name = null,
        int? priority = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(camera);
        return new RenderRequest(
            name ?? $"2D/{camera.gameObject.name}",
            target,
            viewport,
            runtimePipeline,
            CreateFrameData(scope, camera, viewport.width, viewport.height, options),
            priority ?? Rendering2DIds.presentationOrder + camera.priority);
    }

    /// <summary>Creates one request that composites the selected Base/Overlay camera stack.</summary>
    /// <param name="scope">Explicit ordered scenes visible to this operation.</param>
    /// <param name="target">Backbuffer or offscreen destination.</param>
    /// <param name="viewport">Positive destination viewport.</param>
    /// <param name="options">Optional composition and presentation overrides.</param>
    /// <param name="name">Optional diagnostic name.</param>
    /// <param name="priority">Ascending scheduling priority.</param>
    /// <returns>An immutable request containing the complete selected camera stack.</returns>
    public static RenderRequest CreateCameraStackRequest(
        Rendering2DSceneScope scope,
        RenderTarget target,
        RenderViewport viewport,
        Rendering2DViewportOptions? options = null,
        string? name = null,
        int priority = Rendering2DIds.presentationOrder)
    {
        Rendering2DViewportFrame frame = CreateCameraStackFrame(
            scope,
            viewport.width,
            viewport.height,
            options);
        return new RenderRequest(
            name ?? "2D/CameraStack",
            target,
            viewport,
            runtimePipeline,
            frame.data,
            priority);
    }

    /// <summary>Tries to select one deterministic base camera from an explicit scene scope.</summary>
    /// <param name="scope">Explicit ordered scenes to inspect.</param>
    /// <param name="camera">Receives the preferred camera.</param>
    /// <param name="diagnostics">Receives ambiguity and invalid-stack diagnostics.</param>
    /// <returns><see langword="true"/> when at least one enabled camera exists.</returns>
    public static bool TryFindPrimaryCamera(
        Rendering2DSceneScope scope,
        out Camera2D? camera,
        out IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(scope);
        scope.Refresh();
        CameraPlan plan = BuildCameraPlan(scope, backbufferOnly: false);
        camera = plan.cameras.FirstOrDefault();
        diagnostics = plan.diagnostics;
        return camera is not null;
    }

    internal static IEnumerable<Camera2D> EnumerateCameras(Rendering2DSceneScope scope)
    {
        foreach (Rendering2DSceneEntry entry in scope.entries)
        {
            for (int index = 0; index < entry.snapshot.cameras.Length; index++)
            {
                Camera2D camera = entry.snapshot.cameras[index];
                if (camera.isActiveAndEnabled)
                    yield return camera;
            }
        }
    }

    internal static CameraPlan BuildCameraPlan(Rendering2DSceneScope scope, bool backbufferOnly)
    {
        Rendering2DFrameFingerprint fingerprint = ComputeCameraPlanFingerprint(scope, backbufferOnly);
        CameraPlan? cacheHit = null;
        lock (scope.cacheGate)
        {
            CameraPlan? cached = backbufferOnly ? scope.backbufferCameraPlan : scope.allCameraPlan;
            Rendering2DFrameFingerprint cachedFingerprint = backbufferOnly
                ? scope.backbufferCameraPlanFingerprint
                : scope.allCameraPlanFingerprint;
            if (cached is not null && cachedFingerprint == fingerprint)
                cacheHit = cached;
        }
        if (cacheHit is not null)
            return cacheHit;

        Camera2D[] candidates = EnumerateCameras(scope)
            .Where(camera => !backbufferOnly || camera.renderToBackbuffer)
            .OrderBy(static camera => camera.priority)
            .ThenBy(static camera => camera.gameObject.identity.persistentId)
            .ToArray();
        var diagnostics = new List<string>();
        Camera2D[] bases = candidates
            .Where(static camera => camera.composition == CameraComposition2D.Base)
            .ToArray();
        Camera2D[] primaryBases = bases.Where(static camera => camera.primary).ToArray();
        foreach (Camera2D invalidPrimary in candidates.Where(static camera =>
                     camera.composition == CameraComposition2D.Overlay && camera.primary))
        {
            diagnostics.Add(
                $"Overlay camera '{invalidPrimary.gameObject.name}' is marked primary; only base cameras can be selected as primary.");
        }
        Camera2D? selectedBase = primaryBases.FirstOrDefault() ?? bases.FirstOrDefault();
        if (primaryBases.Length > 1)
        {
            diagnostics.Add(
                $"Multiple primary 2D base cameras target the same output; '{selectedBase!.gameObject.name}' was selected deterministically.");
        }
        else if (primaryBases.Length == 0 && bases.Length > 1)
        {
            diagnostics.Add(
                $"Multiple 2D base cameras target the same output without a primary; '{selectedBase!.gameObject.name}' was selected deterministically.");
        }
        if (selectedBase is null)
        {
            if (candidates.Any(static camera => camera.composition == CameraComposition2D.Overlay))
                diagnostics.Add("2D overlay cameras were ignored because no enabled base camera is available.");
            return CacheCameraPlan(scope, backbufferOnly, new CameraPlan([], diagnostics.ToArray(), fingerprint));
        }

        string selectedStack = NormalizeStackId(selectedBase.stackId);
        Camera2D[] overlays = candidates
            .Where(camera => camera.composition == CameraComposition2D.Overlay
                && string.Equals(NormalizeStackId(camera.stackId), selectedStack, StringComparison.Ordinal))
            .ToArray();
        foreach (Camera2D orphan in candidates.Where(camera =>
                     camera.composition == CameraComposition2D.Overlay
                     && !string.Equals(NormalizeStackId(camera.stackId), selectedStack, StringComparison.Ordinal)))
        {
            diagnostics.Add(
                $"Overlay camera '{orphan.gameObject.name}' references stack '{NormalizeStackId(orphan.stackId)}', but selected base stack is '{selectedStack}'.");
        }
        return CacheCameraPlan(
            scope,
            backbufferOnly,
            new CameraPlan(
                [selectedBase, .. overlays],
                diagnostics.Distinct(StringComparer.Ordinal).ToArray(),
                fingerprint));
    }

    private static Rendering2DFrameFingerprint ComputeCameraPlanFingerprint(
        Rendering2DSceneScope scope,
        bool backbufferOnly)
    {
        var hash = new Rendering2DFingerprintBuilder();
        hash.Add(backbufferOnly);
        hash.Add(scope.entries.Count);
        for (int entryIndex = 0; entryIndex < scope.entries.Count; entryIndex++)
        {
            Camera2D[] cameras = scope.entries[entryIndex].snapshot.cameras;
            hash.Add(cameras.Length);
            for (int cameraIndex = 0; cameraIndex < cameras.Length; cameraIndex++)
            {
                Camera2D camera = cameras[cameraIndex];
                hash.Add(camera.gameObject.identity.persistentId);
                hash.Add(camera.gameObject.name);
                hash.Add(camera.isActiveAndEnabled);
                hash.Add(camera.renderToBackbuffer);
                hash.Add(camera.primary);
                hash.Add((int)camera.composition);
                hash.Add(camera.stackId);
                hash.Add(camera.priority);
            }
        }
        return hash.Build();
    }

    private static CameraPlan CacheCameraPlan(
        Rendering2DSceneScope scope,
        bool backbufferOnly,
        CameraPlan plan)
    {
        lock (scope.cacheGate)
        {
            if (backbufferOnly)
            {
                scope.backbufferCameraPlanFingerprint = plan.fingerprint;
                scope.backbufferCameraPlan = plan;
            }
            else
            {
                scope.allCameraPlanFingerprint = plan.fingerprint;
                scope.allCameraPlan = plan;
            }
        }
        return plan;
    }

    private static string[] CombineDiagnostics(
        IReadOnlyList<string> primary,
        IReadOnlyList<string>? secondary)
    {
        int secondaryCount = secondary?.Count ?? 0;
        if (primary.Count == 0 && secondaryCount == 0)
            return [];
        var result = new string[primary.Count + secondaryCount];
        for (int index = 0; index < primary.Count; index++)
            result[index] = primary[index];
        for (int index = 0; index < secondaryCount; index++)
            result[primary.Count + index] = secondary![index];
        return result;
    }

    private static string NormalizeStackId(string? value)
        => string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();

    internal sealed record CameraPlan(
        Camera2D[] cameras,
        string[] diagnostics,
        Rendering2DFrameFingerprint fingerprint);

    internal static bool HasEnabledBaseCamera(Rendering2DSceneScope scope, bool backbufferOnly)
    {
        for (int entryIndex = 0; entryIndex < scope.entries.Count; entryIndex++)
        {
            Camera2D[] cameras = scope.entries[entryIndex].snapshot.cameras;
            for (int cameraIndex = 0; cameraIndex < cameras.Length; cameraIndex++)
            {
                Camera2D camera = cameras[cameraIndex];
                if (camera.isActiveAndEnabled
                    && camera.composition == CameraComposition2D.Base
                    && (!backbufferOnly || camera.renderToBackbuffer))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private sealed class ViewportFrameCache
    {
        internal Rendering2DFrameFingerprint fingerprint;
        internal Rendering2DViewportFrame? frame;
    }

}

/// <summary>
/// Exposes one immutable 2D render snapshot without leaking the pipeline's batching representation.
/// </summary>
public sealed class Rendering2DViewportFrame
{
    private readonly Rendering2DFrame m_frame;
    private readonly Rendering2DFrame[] m_frames;

    internal Rendering2DViewportFrame(Rendering2DFrame frame)
        : this([frame])
    {
    }

    internal Rendering2DViewportFrame(Rendering2DFrame[] frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Length == 0)
            throw new ArgumentException("At least one 2D frame is required.", nameof(frames));
        m_frames = frames;
        m_frame = frames[0];
        data = new RenderFrameData();
        data.Set(Rendering2DPipeline.frameChannel, new Rendering2DFrameSequence(frames));
        statistics = CalculateStatistics(frames);
    }

    /// <summary>Gets frame-only data accepted by the 2D pipeline.</summary>
    public RenderFrameData data { get; }

    /// <summary>Gets allocation-free aggregate counts for diagnostics and external validation.</summary>
    public Rendering2DFrameStatistics statistics { get; }

    internal IReadOnlyList<string> diagnostics => m_frame.diagnostics;

    internal int primaryCameraPriority => m_frame.camera.priority;

    /// <summary>Gets the exact world-to-view matrix used by this immutable viewport snapshot.</summary>
    public Matrix viewMatrix => m_frame.viewTransform;

    /// <summary>Gets the exact view-to-clip matrix used by this immutable viewport snapshot.</summary>
    public Matrix projectionMatrix => m_frame.projectionTransform;

    /// <summary>Finds the frontmost visible object at normalized viewport coordinates.</summary>
    /// <param name="normalizedX">Horizontal coordinate from zero at the left to one at the right.</param>
    /// <param name="normalizedY">Vertical coordinate from zero at the top to one at the bottom.</param>
    /// <returns>The frontmost visible object, or <see langword="null"/> when no object is hit.</returns>
    public GameObject? Pick(float normalizedX, float normalizedY)
    {
        for (int index = m_frames.Length - 1; index >= 0; index--)
        {
            GameObject? selected = m_frames[index].Pick(normalizedX, normalizedY);
            if (selected is not null)
                return selected;
        }
        return null;
    }

    private static Rendering2DFrameStatistics CalculateStatistics(Rendering2DFrame[] frames)
    {
        int batchCount = 0;
        int instanceCount = 0;
        int litInstanceCount = 0;
        int lightCount = 0;
        int shadowCasterCount = 0;
        int bloomLevels = 0;
        float bloomIntensity = 0f;
        for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            Rendering2DFrame frame = frames[frameIndex];
            batchCount += frame.batches.Length;
            lightCount += frame.lights.Length;
            shadowCasterCount += frame.shadowCasters.Length;
            for (int batchIndex = 0; batchIndex < frame.batches.Length; batchIndex++)
            {
                Rendering2DDrawBatch batch = frame.batches[batchIndex];
                instanceCount += batch.instanceCount;
                if (batch.lightBlendStyles != 0)
                    litInstanceCount += batch.instanceCount;
            }
            if (frame.postProcess is not Rendering2DPostProcessSettings postProcess)
                continue;
            bloomLevels = Math.Max(bloomLevels, postProcess.bloomLevels);
            bloomIntensity = MathF.Max(bloomIntensity, postProcess.bloomIntensity);
        }
        return new Rendering2DFrameStatistics(
            frames.Length,
            batchCount,
            instanceCount,
            litInstanceCount,
            lightCount,
            shadowCasterCount,
            bloomLevels,
            bloomIntensity);
    }
}

/// <summary>
/// Describes allocation-free aggregate counters captured with one immutable 2D viewport frame.
/// </summary>
public readonly struct Rendering2DFrameStatistics
{
    internal Rendering2DFrameStatistics(
        int cameraCount,
        int batchCount,
        int instanceCount,
        int litInstanceCount,
        int lightCount,
        int shadowCasterCount,
        int bloomLevels,
        float bloomIntensity)
    {
        this.cameraCount = cameraCount;
        this.batchCount = batchCount;
        this.instanceCount = instanceCount;
        this.litInstanceCount = litInstanceCount;
        this.lightCount = lightCount;
        this.shadowCasterCount = shadowCasterCount;
        this.bloomLevels = bloomLevels;
        this.bloomIntensity = bloomIntensity;
    }

    /// <summary>Gets the number of composited cameras.</summary>
    public int cameraCount { get; }

    /// <summary>Gets the number of adjacent-state draw batches.</summary>
    public int batchCount { get; }

    /// <summary>Gets the total number of submitted 2D instances.</summary>
    public int instanceCount { get; }

    /// <summary>Gets the number of instances that consume at least one 2D light blend style.</summary>
    public int litInstanceCount { get; }

    /// <summary>Gets the number of visible 2D lights.</summary>
    public int lightCount { get; }

    /// <summary>Gets the number of visible 2D shadow casters.</summary>
    public int shadowCasterCount { get; }

    /// <summary>Gets the greatest configured Bloom pyramid depth.</summary>
    public int bloomLevels { get; }

    /// <summary>Gets the greatest configured Bloom intensity.</summary>
    public float bloomIntensity { get; }
}

/// <summary>
/// Submits the enabled backbuffer 2D camera stack from the host's explicit frame content scope.
/// </summary>
[RenderRequestProviderExtension(Rendering2DIds.requestProvider)]
public sealed class Rendering2DRequestProvider : RenderRequestProvider
{
    private readonly Rendering2DSceneScopeCache m_scopeCache = new();
    private readonly Rendering2DViewportOptions m_options = new() { backbufferOnly = true };
    private IReadOnlyList<string>? m_lastDiagnostics;
    private Rendering2DViewportFrame? m_lastFrame;
    private RenderViewport m_lastViewport;
    private int m_lastPriority;
    private RenderRequest? m_lastRequest;

    /// <inheritdoc />
    public override void Submit(RenderRequestProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Rendering2DSceneScope scope = m_scopeCache.Get(context.content);
        if (!Rendering2DRenderer.HasEnabledBaseCamera(scope, backbufferOnly: true))
            return;
        RenderViewport viewport = context.primaryPresentationViewport;
        Rendering2DViewportFrame frame = Rendering2DRenderer.CreateCameraStackFrame(
            scope,
            viewport.width,
            viewport.height,
            m_options);
        PublishPlanDiagnostics(frame.diagnostics);
        int priority = SaturatingPriority(frame.primaryCameraPriority);
        if (m_lastRequest is null
            || !ReferenceEquals(m_lastFrame, frame)
            || m_lastViewport != viewport
            || m_lastPriority != priority)
        {
            m_lastFrame = frame;
            m_lastViewport = viewport;
            m_lastPriority = priority;
            m_lastRequest = new RenderRequest(
                "2D/CameraStack",
                RenderTarget.backbuffer,
                viewport,
                Rendering2DRenderer.sharedPipeline,
                frame.data,
                priority);
        }
        context.requests.Submit(m_lastRequest!);
    }

    private void PublishPlanDiagnostics(IReadOnlyList<string> diagnostics)
    {
        if (ReferenceEquals(diagnostics, m_lastDiagnostics))
            return;
        m_lastDiagnostics = diagnostics;
        foreach (string diagnostic in diagnostics)
            Log.Warn(diagnostic);
    }

    private static int SaturatingPriority(int cameraPriority)
    {
        long combined = (long)Rendering2DIds.presentationOrder + cameraPriority;
        return (int)Math.Clamp(combined, int.MinValue, int.MaxValue);
    }
}
