using InnoEditor.Assets;
using InnoEditor.Rendering;
using InnoEngine.Rendering;

namespace Inno.Rendering2D;

/// <summary>Creates an empty authorable Sprite Atlas source.</summary>
[AssetCreationMenu(
    Rendering2DIds.spriteAtlasCreation,
    "Rendering 2D/Sprites/Sprite Atlas",
    ".ispriteatlas2d",
    "New Sprite Atlas",
    groupOrder: 300,
    itemOrder: 100,
    separatorBeforeGroup: true)]
public sealed class SpriteAtlas2DAssetCreationTemplate
    : AssetCreationTemplate<SpriteAtlas2DAsset>;

/// <summary>Creates an empty authorable Sprite Animation source.</summary>
[AssetCreationMenu(
    Rendering2DIds.spriteAnimationCreation,
    "Rendering 2D/Sprites/Sprite Animation",
    ".ispriteanimation2d",
    "New Sprite Animation",
    groupOrder: 300,
    itemOrder: 200,
    separatorBeforeGroup: true)]
public sealed class SpriteAnimation2DAssetCreationTemplate
    : AssetCreationTemplate<SpriteAnimation2DAsset>;

/// <summary>Creates an empty authorable Tile Set source.</summary>
[AssetCreationMenu(
    Rendering2DIds.tileSetCreation,
    "Rendering 2D/World/Tile Set",
    ".itileset2d",
    "New Tile Set",
    groupOrder: 300,
    itemOrder: 100,
    separatorBeforeGroup: true)]
public sealed class TileSet2DAssetCreationTemplate
    : AssetCreationTemplate<TileSet2DAsset>;

/// <summary>Creates an empty authorable Tilemap source.</summary>
[AssetCreationMenu(
    Rendering2DIds.tilemapCreation,
    "Rendering 2D/World/Tilemap",
    ".itilemap2d",
    "New Tilemap",
    groupOrder: 300,
    itemOrder: 200,
    separatorBeforeGroup: true)]
public sealed class Tilemap2DAssetCreationTemplate
    : AssetCreationTemplate<Tilemap2DAsset>;

/// <summary>Creates a Particle Effect source with deterministic defaults.</summary>
[AssetCreationMenu(
    Rendering2DIds.particleEffectCreation,
    "Rendering 2D/Effects/Particle Effect",
    ".iparticle2d",
    "New Particle Effect",
    groupOrder: 300,
    itemOrder: 100,
    separatorBeforeGroup: true)]
public sealed class ParticleEffect2DAssetCreationTemplate
    : AssetCreationTemplate<ParticleEffect2DAsset>;

/// <summary>Creates a Post Process Profile source with deterministic defaults.</summary>
[AssetCreationMenu(
    Rendering2DIds.postProcessCreation,
    "Rendering 2D/Effects/Post Process Profile",
    ".ipostprocess2d",
    "New Post Process Profile",
    groupOrder: 300,
    itemOrder: 200,
    separatorBeforeGroup: true)]
public sealed class PostProcessProfile2DAssetCreationTemplate
    : AssetCreationTemplate<PostProcessProfile2DAsset>;

/// <summary>Creates a Render Pipeline source already configured for Rendering2D.</summary>
[AssetCreationMenu(
    Rendering2DIds.pipelineCreation,
    "Rendering 2D/Render Pipeline",
    ".irenderpipeline",
    "New 2D Render Pipeline",
    groupOrder: 300,
    itemOrder: 300,
    separatorBeforeGroup: true)]
public sealed class Rendering2DPipelineAssetCreationTemplate
    : AssetCreationTemplate<RenderPipelineAsset>
{
    /// <inheritdoc />
    protected override RenderPipelineAsset CreateAsset()
        => new()
        {
            pipelineTypeId = Rendering2DIds.pipeline,
            pipelineState = new SerializedRenderExtensionState(
                EditorAssets.CaptureProperties(new Rendering2DPipelineSettings()))
        };
}
