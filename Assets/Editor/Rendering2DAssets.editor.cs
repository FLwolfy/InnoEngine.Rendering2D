using System;
using InnoEngine.Assets;
using InnoEngine.Rendering;
using InnoEditor.Assets;

namespace Inno.Rendering2D;

/// <summary>Provides concise creation and save workflows for editable native 2D assets.</summary>
public static class Rendering2DAssets
{
    /// <summary>Creates a native 2D Pipeline configuration with automatically tracked resource references.</summary>
    /// <param name="path">Writable project path using the <c>.irenderpipeline</c> extension.</param>
    /// <param name="settings">Complete typed 2D Pipeline resource configuration.</param>
    /// <returns>Whether the source was committed and imported successfully.</returns>
    public static bool SavePipeline(AssetPath path, Rendering2DPipelineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Save(path, new RenderPipelineAsset
        {
            pipelineTypeId = Rendering2DIds.pipeline,
            pipelineState = new SerializedRenderExtensionState(EditorAssets.CaptureProperties(settings))
        }, ".irenderpipeline");
    }

    /// <summary>Creates or replaces an editable sprite atlas source.</summary>
    /// <param name="path">Writable project path using the <c>.ispriteatlas2d</c> extension.</param>
    /// <param name="atlas">Complete atlas value.</param>
    /// <returns><see langword="true"/> when the source was saved.</returns>
    public static bool SaveAtlas(AssetPath path, SpriteAtlas2DAsset atlas)
        => Save(path, atlas, ".ispriteatlas2d");

    /// <summary>Creates or replaces an editable sprite animation source.</summary>
    /// <param name="path">Writable project path using the <c>.ispriteanimation2d</c> extension.</param>
    /// <param name="animation">Complete animation value.</param>
    /// <returns><see langword="true"/> when the source was saved.</returns>
    public static bool SaveAnimation(AssetPath path, SpriteAnimation2DAsset animation)
        => Save(path, animation, ".ispriteanimation2d");

    /// <summary>Creates or replaces an editable tile-set source.</summary>
    /// <param name="path">Writable project path using the <c>.itileset2d</c> extension.</param>
    /// <param name="tileSet">Complete tile-set value.</param>
    /// <returns><see langword="true"/> when the source was saved.</returns>
    public static bool SaveTileSet(AssetPath path, TileSet2DAsset tileSet)
        => Save(path, tileSet, ".itileset2d");

    /// <summary>Creates or replaces an editable tilemap source.</summary>
    /// <param name="path">Writable project path using the <c>.itilemap2d</c> extension.</param>
    /// <param name="tilemap">Complete tilemap value.</param>
    /// <returns><see langword="true"/> when the source was saved.</returns>
    public static bool SaveTilemap(AssetPath path, Tilemap2DAsset tilemap)
        => Save(path, tilemap, ".itilemap2d");

    /// <summary>Creates or replaces an editable 2D post-process profile source.</summary>
    /// <param name="path">Writable project path using the <c>.ipostprocess2d</c> extension.</param>
    /// <param name="profile">Complete post-process profile value.</param>
    /// <returns><see langword="true"/> when the source was saved.</returns>
    public static bool SavePostProcess(AssetPath path, PostProcessProfile2DAsset profile)
        => Save(path, profile, ".ipostprocess2d");

    /// <summary>Creates or replaces an editable deterministic particle-effect source.</summary>
    /// <param name="path">Writable project path using the <c>.iparticle2d</c> extension.</param>
    /// <param name="effect">Complete particle-effect value.</param>
    /// <returns><see langword="true"/> when the source was saved.</returns>
    public static bool SaveParticleEffect(AssetPath path, ParticleEffect2DAsset effect)
        => Save(path, effect, ".iparticle2d");

    private static bool Save<TAsset>(AssetPath path, TAsset asset, string extension)
        where TAsset : AssetObject
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (path.source != AssetSourceId.project)
            throw new InvalidOperationException("Installed Plugin sources are read-only; save 2D assets under project Assets.");
        if (!path.localPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"The asset path must use the '{extension}' extension.", nameof(path));
        return EditorAssets.Save(path, asset);
    }
}
