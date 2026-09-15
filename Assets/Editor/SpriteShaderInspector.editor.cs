using InnoEditor.ImGui;
using InnoEditor.Rendering.Assets;
using InnoEditor.Shaders;

namespace Inno.Rendering2D;

/// <summary>Explains the surface contract and exposes replaceable source modules using shared asset controls.</summary>
[ShaderNodeDrawer(Rendering2DIds.spriteSurfaceOutputNode, "Sprite Surface Output", "Domain Outputs/Rendering 2D", 800, separatorBefore: true)]
public sealed class SpriteSurfaceInspector : ShaderNodeDrawer
{
    /// <inheritdoc />
    public override void Draw(ShaderNodeDrawContext context)
    {
        ImGui.Text("Color: linear RGBA. Normal: tangent-space XYZ.");
        ImGui.Text("Emission: linear HDR RGB, before per-instance strength.");
        ImGui.Text("Unconnected inputs inherit the Sprite's texture bindings.");
        ImGui.Text("Vertex Offset: world-space XYZ, zero when unconnected.");
        if (ImGui.CollapsingHeader("Target Function Library"))
        {
            context.DrawProperty<ShaderFunctionAsset?>("vertexSource", "Default Vertex", null);
            context.DrawProperty<ShaderFunctionAsset?>("surfaceSource", "Surface Composition", null);
            context.DrawProperty<ShaderFunctionAsset?>("sampleSource", "Sprite Sampling", null);
        }
    }
}

/// <summary>Explains which per-instance textures the convenience node samples.</summary>
[ShaderNodeDrawer(Rendering2DIds.spriteTextureNode, "Sprite Texture", "Domain/Rendering 2D", 850, separatorBefore: true)]
public sealed class SpriteTextureInspector : ShaderNodeDrawer
{
    /// <inheritdoc />
    public override void Draw(ShaderNodeDrawContext context)
    {
        ImGui.Text("Uses the Sprite/atlas texture, normal map and emission map.");
        ImGui.Text("Texture ownership stays with each instance, enabling batching.");
        ImGui.Text("For a Material-owned texture, use an exposed Texture parameter.");
    }
}
