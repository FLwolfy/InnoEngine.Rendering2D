using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InnoEngine.Assets;
using InnoEditor.Assets;

namespace Inno.Rendering2D;

internal abstract class NativeRendering2DAssetImporter<TAsset> : AssetImporter<TAsset>
    where TAsset : AssetObject
{
    protected sealed override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<TAsset> output,
        CancellationToken cancellationToken)
    {
        TAsset asset = NativeAssetSourceSerialization.Import<TAsset>(
            context.sourceBytes.Span,
            context.services,
            out IReadOnlyList<AssetDependency> dependencies);
        output.SetAsset(asset);
        foreach (AssetDependency dependency in dependencies)
            output.DependsOnAsset(dependency);
        await output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }

    protected sealed override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        AssetExportContext context,
        TAsset asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(NativeAssetSourceSerialization.Export(
            asset,
            context.services));
    }
}

[AssetImporterExtension]
internal sealed class SpriteAtlas2DImporter : NativeRendering2DAssetImporter<SpriteAtlas2DAsset>
{
    /// <inheritdoc />
    public override string importerId => "inno.rendering.2d.sprite-atlas";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions { get; } = [".ispriteatlas2d"];
}

[AssetImporterExtension]
internal sealed class SpriteAnimation2DImporter : NativeRendering2DAssetImporter<SpriteAnimation2DAsset>
{
    /// <inheritdoc />
    public override string importerId => "inno.rendering.2d.sprite-animation";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions { get; } = [".ispriteanimation2d"];
}

[AssetImporterExtension]
internal sealed class TileSet2DImporter : NativeRendering2DAssetImporter<TileSet2DAsset>
{
    /// <inheritdoc />
    public override string importerId => "inno.rendering.2d.tile-set";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions { get; } = [".itileset2d"];
}

[AssetImporterExtension]
internal sealed class Tilemap2DImporter : NativeRendering2DAssetImporter<Tilemap2DAsset>
{
    /// <inheritdoc />
    public override string importerId => "inno.rendering.2d.tilemap";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions { get; } = [".itilemap2d"];
}
