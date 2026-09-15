using InnoEngine.Rendering;

namespace Inno.Rendering2D;

/// <summary>Publishes the stable open protocols owned by the Inno 2D rendering Plugin.</summary>
public static class Rendering2DIds
{
    internal const int presentationOrder = 1000;

    /// <summary>Gets the render pipeline extension identity.</summary>
    public const string pipeline = "inno.rendering.2d.pipeline";

    /// <summary>Gets the project-wide Rendering2D settings protocol identity.</summary>
    public const string projectSettings = "inno.rendering.2d";

    /// <summary>Gets the automatic backbuffer request provider identity.</summary>
    public const string requestProvider = "inno.rendering.2d.request-provider";

    /// <summary>Gets the Plugin-owned Sprite surface authoring target identity.</summary>
    public const string spriteShaderTarget = "inno.rendering.2d.sprite-surface";

    /// <summary>Gets the artist-facing Sprite surface output node identity.</summary>
    public const string spriteSurfaceOutputNode = "inno.rendering.2d.sprite-surface-output";

    /// <summary>Gets the per-instance Sprite texture convenience node identity.</summary>
    public const string spriteTextureNode = "inno.rendering.2d.sprite-texture";

    /// <summary>Gets the default Sprite Shader graph template identity.</summary>
    public const string spriteShaderTemplate = "inno.rendering.2d.sprite-template";

    /// <summary>Gets the Sprite Atlas asset creation template identity.</summary>
    public const string spriteAtlasCreation = "inno.rendering.2d.asset-create.sprite-atlas";

    /// <summary>Gets the Sprite Animation asset creation template identity.</summary>
    public const string spriteAnimationCreation = "inno.rendering.2d.asset-create.sprite-animation";

    /// <summary>Gets the Tile Set asset creation template identity.</summary>
    public const string tileSetCreation = "inno.rendering.2d.asset-create.tile-set";

    /// <summary>Gets the Tilemap asset creation template identity.</summary>
    public const string tilemapCreation = "inno.rendering.2d.asset-create.tilemap";

    /// <summary>Gets the Particle Effect asset creation template identity.</summary>
    public const string particleEffectCreation = "inno.rendering.2d.asset-create.particle-effect";

    /// <summary>Gets the Post Process Profile asset creation template identity.</summary>
    public const string postProcessCreation = "inno.rendering.2d.asset-create.post-process";

    /// <summary>Gets the configured 2D Render Pipeline asset creation template identity.</summary>
    public const string pipelineCreation = "inno.rendering.2d.asset-create.pipeline";

    /// <summary>Gets the native 2D asset draft document provider identity.</summary>
    public const string assetDocumentProvider = "inno.rendering.2d.asset-documents";

    /// <summary>Gets the reload-safe native 2D asset draft History protocol identity.</summary>
    public const string assetDraftHistory = "inno.rendering.2d.asset-draft-history";

    /// <summary>Gets the Sprite Atlas importer protocol identity.</summary>
    public const string spriteAtlasImporter = "inno.rendering.2d.sprite-atlas";

    /// <summary>Gets the Sprite Animation importer protocol identity.</summary>
    public const string spriteAnimationImporter = "inno.rendering.2d.sprite-animation";

    /// <summary>Gets the Tile Set importer protocol identity.</summary>
    public const string tileSetImporter = "inno.rendering.2d.tile-set";

    /// <summary>Gets the Tilemap importer protocol identity.</summary>
    public const string tilemapImporter = "inno.rendering.2d.tilemap";

    /// <summary>Gets the Post Process Profile importer protocol identity.</summary>
    public const string postProcessImporter = "inno.rendering.2d.post-process";

    /// <summary>Gets the Particle Effect importer protocol identity.</summary>
    public const string particleEffectImporter = "inno.rendering.2d.particle-effect";

    /// <summary>Gets the material contract implemented by sprite-compatible shaders.</summary>
    public static ShaderContractId spriteContract => new("inno.rendering.2d.sprite");

    /// <summary>Gets the Pipeline-owned fullscreen processing contract.</summary>
    public static ShaderContractId postProcessContract => new("inno.rendering.2d.post-process");

    /// <summary>Gets the Pipeline-owned MRT light accumulation contract.</summary>
    public static ShaderContractId lightContract => new("inno.rendering.2d.light");

    /// <summary>Gets the independent shadow-volume and stencil utility contract.</summary>
    public static ShaderContractId shadowContract => new("inno.rendering.2d.shadow");

    /// <summary>Gets the straight-alpha pass role.</summary>
    public static ShaderPassRoleId alphaRole => new("inno.rendering.2d.alpha");

    /// <summary>Gets the premultiplied-alpha pass role.</summary>
    public static ShaderPassRoleId premultipliedRole => new("inno.rendering.2d.premultiplied");

    /// <summary>Gets the additive pass role.</summary>
    public static ShaderPassRoleId additiveRole => new("inno.rendering.2d.additive");

    /// <summary>Gets the multiply pass role.</summary>
    public static ShaderPassRoleId multiplyRole => new("inno.rendering.2d.multiply");

    /// <summary>Gets the opaque pass role.</summary>
    public static ShaderPassRoleId opaqueRole => new("inno.rendering.2d.opaque");
}
