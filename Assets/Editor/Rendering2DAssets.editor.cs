using System;
using InnoEngine.Assets;
using InnoEditor.Assets;

namespace Inno.Rendering2D;

/// <summary>Provides concise creation and save workflows for editable native 2D assets.</summary>
public static class Rendering2DAssets
{
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
