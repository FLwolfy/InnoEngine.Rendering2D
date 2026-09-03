using System;
using System.Collections.Generic;
using System.Linq;
using InnoEngine.Logging;
using InnoEngine.Mathematics;
using InnoEngine.Rendering;
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
            entries.Add(new Rendering2DSceneEntry(scene, extraction.Capture()));
        }
        m_scenes = participatingScenes.ToArray();
        m_entries = entries.ToArray();
    }

    /// <summary>
    /// Gets the ordered immutable scene snapshot.
    /// </summary>
    public IReadOnlyList<GameScene> scenes => m_scenes;

    internal IReadOnlyList<Rendering2DSceneEntry> entries => m_entries;
}

internal readonly record struct Rendering2DSceneEntry(
    GameScene scene,
    Rendering2DSceneSnapshot snapshot);

/// <summary>Creates explicit 2D requests while keeping camera ownership in the Plugin.</summary>
public static class Rendering2DRenderer
{
    /// <summary>Creates a transient pipeline selection for the active 2D extension generation.</summary>
    /// <returns>A model-neutral pipeline asset selecting the 2D Plugin pipeline.</returns>
    public static RenderPipelineAsset CreatePipelineAsset()
        => new() { pipelineTypeId = Rendering2DIds.pipeline };

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
        CameraPlan plan = BuildCameraPlan(scope, options?.backbufferOnly ?? false);
        if (plan.cameras.Length == 0)
            throw new InvalidOperationException("No enabled 2D base camera is available in the explicit scene scope.");
        Rendering2DFrame[] frames = new Rendering2DFrame[plan.cameras.Length];
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
                        ? plan.diagnostics.Concat(options?.additionalDiagnostics ?? []).ToArray()
                        : null,
                    backbufferOnly = options?.backbufferOnly ?? false,
                    drawGrid = options?.drawGrid ?? false,
                    drawAxes = options?.drawAxes ?? false
                });
        }
        return new Rendering2DViewportFrame(frames);
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
        return new Rendering2DViewportFrame(
            Rendering2DFrameCollector.Collect(scope, camera, pixelWidth, pixelHeight, options));
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
            CreatePipelineAsset(),
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
            CreatePipelineAsset(),
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
            return new CameraPlan([], diagnostics.ToArray());
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
        return new CameraPlan([selectedBase, .. overlays], diagnostics.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string NormalizeStackId(string? value)
        => string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();

    internal sealed record CameraPlan(Camera2D[] cameras, string[] diagnostics);
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
    }

    /// <summary>Gets frame-only data accepted by the 2D pipeline.</summary>
    public RenderFrameData data { get; }

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
}

/// <summary>
/// Submits the enabled backbuffer 2D camera stack from the host's explicit frame content scope.
/// </summary>
[RenderRequestProviderExtension(Rendering2DIds.requestProvider)]
public sealed class Rendering2DRequestProvider : RenderRequestProvider
{
    private string m_lastDiagnosticSignature = string.Empty;

    /// <inheritdoc />
    public override void Submit(RenderRequestProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scope = new Rendering2DSceneScope(context.content.GetValues<GameScene>());
        Rendering2DRenderer.CameraPlan plan = Rendering2DRenderer.BuildCameraPlan(scope, backbufferOnly: true);
        PublishPlanDiagnostics(plan.diagnostics);
        if (plan.cameras.Length == 0)
            return;
        RenderViewport viewport = context.primaryPresentationViewport;
        context.requests.Submit(Rendering2DRenderer.CreateCameraStackRequest(
            scope,
            RenderTarget.backbuffer,
            viewport,
            new Rendering2DViewportOptions
            {
                additionalDiagnostics = plan.diagnostics,
                backbufferOnly = true
            },
            priority: Rendering2DIds.presentationOrder + plan.cameras[0].priority));
    }

    private void PublishPlanDiagnostics(IReadOnlyList<string> diagnostics)
    {
        string signature = string.Join('\n', diagnostics);
        if (string.Equals(signature, m_lastDiagnosticSignature, StringComparison.Ordinal))
            return;
        m_lastDiagnosticSignature = signature;
        foreach (string diagnostic in diagnostics)
            Log.Warn(diagnostic);
    }
}
