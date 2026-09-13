using System;
using System.Collections.Generic;
using System.Linq;
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
        await WriteAdditionalArtifactsAsync(context, output, asset, cancellationToken);
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

    /// <summary>Writes asset-specific immutable outputs after the structured runtime payload.</summary>
    /// <param name="context">Current isolated import context.</param>
    /// <param name="output">Candidate output writer.</param>
    /// <param name="asset">Imported managed asset.</param>
    /// <param name="cancellationToken">Cancellation observed before committing additional outputs.</param>
    /// <returns>An operation that completes after every additional output has been staged.</returns>
    protected virtual ValueTask WriteAdditionalArtifactsAsync(
        AssetImportContext context,
        AssetImportWriter<TAsset> output,
        TAsset asset,
        CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}

[AssetImporterExtension]
internal sealed class SpriteAtlas2DImporter : NativeRendering2DAssetImporter<SpriteAtlas2DAsset>
{
    /// <inheritdoc />
    public override string importerId => "inno.rendering.2d.sprite-atlas";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions { get; } = [".ispriteatlas2d"];

    /// <inheritdoc />
    protected override async ValueTask WriteAdditionalArtifactsAsync(
        AssetImportContext context,
        AssetImportWriter<SpriteAtlas2DAsset> output,
        SpriteAtlas2DAsset asset,
        CancellationToken cancellationToken)
    {
        SpriteAtlasCompositionResult2D composition = SpriteAtlasComposer2D.Compose(
            asset,
            texture => context.ReadSourceBytes(texture.assetPath));
        asset.SetPages(composition.pages.Select(static page => page.page));
        asset.SetRegions(composition.regions);
        foreach (SpriteAtlasComposedPage2D page in composition.pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteArtifactAsync(
                page.page.colorSourceOutputName,
                page.colorPngBytes,
                cancellationToken);
            if (page.normalPngBytes is not null)
            {
                await output.WriteArtifactAsync(
                    page.page.normalSourceOutputName,
                    page.normalPngBytes,
                    cancellationToken);
            }
            if (page.emissionPngBytes is not null)
            {
                await output.WriteArtifactAsync(
                    page.page.emissionSourceOutputName,
                    page.emissionPngBytes,
                    cancellationToken);
            }
        }
    }
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

[AssetImporterExtension]
internal sealed class PostProcessProfile2DImporter : NativeRendering2DAssetImporter<PostProcessProfile2DAsset>
{
    /// <inheritdoc />
    public override string importerId => "inno.rendering.2d.post-process";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions { get; } = [".ipostprocess2d"];
}

[AssetImporterExtension]
internal sealed class ParticleEffect2DImporter : NativeRendering2DAssetImporter<ParticleEffect2DAsset>
{
    /// <inheritdoc />
    public override string importerId => "inno.rendering.2d.particle-effect";

    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions { get; } = [".iparticle2d"];
}
