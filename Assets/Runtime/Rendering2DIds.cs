using InnoEngine.Rendering;

namespace Inno.Rendering2D;

/// <summary>Publishes the stable open protocols owned by the Inno 2D rendering Plugin.</summary>
public static class Rendering2DIds
{
    internal const int presentationOrder = 1000;

    /// <summary>Gets the render pipeline extension identity.</summary>
    public const string pipeline = "inno.rendering.2d.pipeline";

    /// <summary>Gets the automatic backbuffer request provider identity.</summary>
    public const string requestProvider = "inno.rendering.2d.request-provider";

    /// <summary>Gets the material contract implemented by sprite-compatible shaders.</summary>
    public static ShaderContractId spriteContract => new("inno.rendering.2d.sprite");

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
