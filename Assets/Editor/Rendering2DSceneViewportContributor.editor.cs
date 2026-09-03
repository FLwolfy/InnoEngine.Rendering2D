using System;
using InnoEditor.Rendering;
using InnoEngine.Mathematics;
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
    private GameScene? m_editorScene;
    private Camera2D? m_camera;
    private Rendering2DViewportFrame? m_latestFrame;
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
        m_latestFrame = Rendering2DRenderer.CreateViewportFrame(
            scope,
            camera,
            context.pixelWidth,
            context.pixelHeight,
            new Rendering2DViewportOptions
            {
                clearColorOverride = context.presentation.backgroundColor,
                drawGrid = true,
                drawAxes = true
            });
        return new EditorViewportContribution(
            m_latestFrame.data,
            Rendering2DRenderer.CreatePipelineAsset(),
            manipulationSpace: new EditorViewportManipulationSpace(
                m_latestFrame.viewMatrix,
                m_latestFrame.projectionMatrix,
                isOrthographic: true));
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

    private static Rendering2DSceneScope CreateScope(EditorViewportContext context)
        => new(context.content.GetValues<GameScene>());

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
