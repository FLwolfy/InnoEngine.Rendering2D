using System;
using System.Collections.Generic;
using InnoEngine.Assets;
using InnoEngine.Logging;
using InnoEngine.Mathematics;
using InnoEngine.Scene;
using InnoEditor.Core;
using InnoEditor.Interactions;
using InnoEditor.Scene;

namespace Inno.Rendering2D;

internal static class Rendering2DGpuAcceptanceProgress
{
    internal static bool resizePending { get; set; } = string.Equals(
        Environment.GetEnvironmentVariable("INNO_RENDERING2D_RUN_GAME_VIEW_RESIZE_GATE"), "1", StringComparison.Ordinal);
}

[EditorModule("rendering2d.gpu-acceptance", order: 450)]
internal sealed class Rendering2DGpuAcceptanceSampleModule(
    IEditorSceneWorkspace workspace) : EditorModule
{
    private bool m_pending;

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_pending = string.Equals(
            Environment.GetEnvironmentVariable("INNO_RENDERING2D_REBUILD_GPU_ACCEPTANCE"),
            "1",
            StringComparison.Ordinal);
    }

    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!m_pending)
            return;
        m_pending = false;
        Rendering2DGpuAcceptanceSample.Rebuild(workspace);
    }
}

internal static class Rendering2DGpuAcceptanceSample
{
    private const string C_SCENE_PATH = "~Samples/SampleScene.iscene";
    private static AssetPath postProcessPath
        => AssetPath.Project("~Samples/GpuAcceptance.ipostprocess2d");

    internal static void Rebuild(IEditorSceneWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        GameScene scene = workspace.Open(C_SCENE_PATH);
        PostProcessProfile2DAsset profile = GetOrCreateProfile();
        ConfigureCamera(scene, profile);
        ConfigureLights(scene);
        ConfigureShadowCaster(scene);
        ConfigureLitReceiver(scene);
        ConfigureBloomEmitter(scene);
        Organize(scene);
        string savedPath = workspace.Save(scene, "~Samples");
        Log.Info($"Rebuilt the Rendering2D GPU acceptance sample at '{savedPath}'.");
    }

    private static PostProcessProfile2DAsset GetOrCreateProfile()
    {
        PostProcessProfile2DAsset profile = Assets.TryLoad(
            postProcessPath,
            out PostProcessProfile2DAsset? existing)
            && existing is not null
                ? existing
                : new PostProcessProfile2DAsset();
        profile.exposure = 0f;
        profile.contrast = 1.04f;
        profile.saturation = 1.05f;
        profile.toneMapping = true;
        profile.bloomIntensity = 1.1f;
        profile.bloomThreshold = 0.9f;
        profile.bloomLevels = 5;
        profile.bloomScatter = 0.72f;
        profile.vignette = 0.12f;
        profile.pixelation = 1;
        if (!Rendering2DAssets.SavePostProcess(postProcessPath, profile))
            throw new InvalidOperationException("The post-process profile importer rejected the acceptance profile.");
        return profile;
    }

    private static void ConfigureCamera(GameScene scene, PostProcessProfile2DAsset profile)
    {
        var cameras = new List<Camera2D>();
        foreach (GameObject gameObject in scene.GetObjects())
        {
            if (!gameObject.TryGetComponent(out Camera2D? camera) || camera is null)
                continue;
            cameras.Add(camera);
        }

        cameras.Sort(static (left, right) =>
            left.gameObject.identity.persistentId.CompareTo(right.gameObject.identity.persistentId));
        Camera2D? selected = cameras.Count == 0 ? null : cameras[0];
        if (selected is null)
        {
            GameObject cameraObject = scene.CreateObject("Camera");
            selected = cameraObject.AddComponent<Camera2D>();
        }

        foreach (Camera2D camera in cameras)
        {
            camera.primary = false;
            camera.renderToBackbuffer = false;
        }

        selected.composition = CameraComposition2D.Base;
        selected.primary = true;
        selected.renderToBackbuffer = true;
        selected.clearTarget = true;
        selected.orthographicSize = 5f;
        selected.gameObject.transform.worldPosition = Vector3.ZERO;
        selected.postProcess = profile;
    }

    private static void ConfigureLights(GameScene scene)
    {
        for (int index = 0; index < 32; index++)
        {
            string name = index == 0
                ? "GPU Shadow Light"
                : $"GPU Dynamic Light {index + 1:D2}";
            GameObject lightObject = GetOrCreateObject(scene, name);
            Light2D light = GetOrAddComponent<Light2D>(lightObject);
            int column = index % 8;
            int row = index / 8;
            lightObject.transform.worldPosition = index == 0
                ? new Vector3(-2.6f, 2.2f, 0f)
                : new Vector3(-7f + column * 2f, -3f + row * 2f, 0f);
            light.kind = LightKind2D.Point;
            light.color = (index % 3) switch
            {
                0 => new Color(1f, 0.72f, 0.42f, 1f),
                1 => new Color(0.34f, 0.58f, 1f, 1f),
                _ => new Color(0.78f, 0.38f, 1f, 1f)
            };
            light.intensity = index == 0 ? 4f : 0.85f;
            light.range = index == 0 ? 8f : 2.8f;
            light.falloff = index == 0 ? 1.6f : 2.1f;
            light.blendStyle = (LightBlendStyle2D)(index % 4);
            light.castShadows = index == 0;
            light.shadowSoftness = index == 0 ? 0.45f : 0f;
            light.normalIntensity = 1f;
        }
    }

    private static void ConfigureShadowCaster(GameScene scene)
    {
        GameObject casterObject = GetOrCreateObject(scene, "GPU Shadow Caster");
        ShadowCaster2D caster = GetOrAddComponent<ShadowCaster2D>(casterObject);
        casterObject.transform.worldPosition = new Vector3(0f, 0.25f, 0f);
        caster.shape =
        [
            new Vector2(-0.8f, -0.55f),
            new Vector2(0.8f, -0.55f),
            new Vector2(0.8f, 0.55f),
            new Vector2(-0.8f, 0.55f)
        ];
        caster.lightBlendStyles = 0x0f;
        caster.selfShadows = true;
    }

    private static void ConfigureBloomEmitter(GameScene scene)
    {
        GameObject emitterObject = GetOrCreateObject(scene, "GPU Bloom Emitter");
        SpriteRenderer2D sprite = GetOrAddComponent<SpriteRenderer2D>(emitterObject);
        emitterObject.transform.worldPosition = new Vector3(2.5f, -1.8f, 0f);
        sprite.primitive = SpritePrimitive2D.Circle;
        sprite.size = new Vector2(1.1f, 1.1f);
        sprite.color = new Color(1f, 0.55f, 0.18f, 1f);
        sprite.emissionColor = new Color(5f, 1.4f, 0.25f, 1f);
        sprite.receiveLighting = false;
        sprite.orderInLayer = 50;
    }

    private static void ConfigureLitReceiver(GameScene scene)
    {
        GameObject receiverObject = GetOrCreateObject(scene, "GPU Lit Receiver");
        SpriteRenderer2D sprite = GetOrAddComponent<SpriteRenderer2D>(receiverObject);
        receiverObject.transform.worldPosition = new Vector3(0f, -0.95f, 0f);
        sprite.primitive = SpritePrimitive2D.Square;
        sprite.size = new Vector2(6f, 2.4f);
        sprite.color = new Color(0.72f, 0.76f, 0.84f, 1f);
        sprite.emissionColor = Color.BLACK;
        sprite.receiveLighting = true;
        sprite.lightBlendStyles = 0x0f;
        sprite.orderInLayer = -20;
    }

    private static GameObject GetOrCreateObject(GameScene scene, string name)
        => scene.FindObject(name) ?? scene.CreateObject(name);

    private static TComponent GetOrAddComponent<TComponent>(GameObject gameObject)
        where TComponent : GameComponent
        => gameObject.TryGetComponent(out TComponent? component) && component is not null
            ? component
            : gameObject.AddComponent<TComponent>();

    // Preserve transforms while placing the sample under domain-specific grouping objects.
    internal static void Organize(GameScene scene)
    {
        string[] obsoleteExamples = ["Square", "Circle", "Triangle", "Mask Showcase - Outside", "Mask Showcase - Inside", "Mask Showcase - Circle Mask"];
        foreach (string name in obsoleteExamples)
        {
            GameObject? example = scene.FindObject(name);
            if (example is not null)
                scene.DestroyObject(example);
        }
        GameObject cameras = GetOrCreateObject(scene, "========== CAMERAS ==========");
        GameObject lights = GetOrCreateObject(scene, "========== LIGHTINGS ==========");
        GameObject objects = GetOrCreateObject(scene, "========== OBJECTS ==========");
        GameObject shadows = GetOrCreateObject(scene, "========== SHADOWS ==========");
        GameObject[] groups = [cameras, lights, objects, shadows];
        for (int index = 0; index < groups.Length; index++)
        {
            groups[index].transform.SetParent(null);
            groups[index].transform.SetSiblingIndex(index);
        }
        var entries = new List<GameObject>(scene.GetObjects());
        foreach (GameObject entry in entries)
        {
            if (Array.IndexOf(groups, entry) >= 0)
                continue;
            GameObject group = entry.TryGetComponent(out Camera2D? camera) && camera is not null ? cameras
                : entry.TryGetComponent(out Light2D? light) && light is not null ? lights
                : entry.TryGetComponent(out ShadowCaster2D? caster) && caster is not null ? shadows
                : objects;
            entry.transform.SetParent(group.transform);
        }
    }
}

[EditorModule("rendering2d.organize-sample", order: 451)]
internal sealed class Rendering2DOrganizeSampleModule(IEditorSceneWorkspace workspace) : EditorModule
{
    private bool m_pending;
    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
        => m_pending = Environment.GetEnvironmentVariable("INNO_RENDERING2D_ORGANIZE_SAMPLE") == "1";
    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context)
    {
        if (!m_pending) return;
        m_pending = false;
        GameScene scene = workspace.Open("~Samples/SampleScene.iscene");
        Rendering2DGpuAcceptanceSample.Organize(scene);
        workspace.Save(scene, "~Samples");
        Log.Info("Rendering2D sample hierarchy saved under Cameras, Lighting, Objects, and Shadows. Procedural demo objects removed; remaining world transforms preserved.");
    }
}
