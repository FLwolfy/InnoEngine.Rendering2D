using System;
using System.Buffers.Binary;
using InnoEngine.Rendering;
using InnoEditor.Rendering;
using InnoEditor.Shaders;

namespace Inno.Rendering2D;

/// <summary>Provides a neutral unlit Sprite swatch without creating a Scene or altering a Material asset.</summary>
[ShaderPreviewProvider("inno.rendering.2d.sprite")]
public sealed class SpriteShaderPreview : ShaderPreviewProvider
{
    /// <inheritdoc />
    public override EditorViewportLayer CreateLayer(ShaderPreviewContext context)
    {
        var data = new RenderFrameData();
        data.Set(SpritePreviewPipeline.channel, context);
        return new("inno.rendering.2d.sprite-preview", new RenderPipelineAsset
        { pipelineTypeId = SpritePreviewPipeline.pipelineId }, data, 0);
    }
}

/// <summary>Renders the isolated compiled candidate through the ordinary material binder and Render Graph.</summary>
[RenderPipelineExtension(pipelineId)]
public sealed class SpritePreviewPipeline : RenderPipeline
{
    /// <summary>Identifies the Editor-only preview pipeline; runtime packages do not carry this implementation.</summary>
    public const string pipelineId = "inno.rendering.2d.sprite-preview";
    /// <summary>Gets the frame-only domain preview input channel.</summary>
    public static RenderDataChannelId channel => new(pipelineId);

    private readonly RenderVertexLayout m_vertices = new([new(RenderVertexSemantic.Position, RenderVertexFormat.Float2)]);
    private readonly RenderVertexLayout m_instances = new([
        new(RenderVertexSemantic.TextureCoordinate3, RenderVertexFormat.Float4), new(RenderVertexSemantic.TextureCoordinate4, RenderVertexFormat.Float4),
        new(RenderVertexSemantic.TextureCoordinate5, RenderVertexFormat.Float4), new(RenderVertexSemantic.TextureCoordinate6, RenderVertexFormat.Float4),
        new(RenderVertexSemantic.TextureCoordinate7, RenderVertexFormat.Float4)], 80);
    private readonly byte[] m_vertexBytes = Floats([0, 0, 1, 0, 1, 1, 0, 1]);
    private readonly byte[] m_instanceBytes = Floats([-.8f, -.8f, .8f, -.8f, -.8f, .8f, 0, 0, 1, 0, 0, 1, 0, 1, 0, 0, 1, 1, 1, 1]);
    private readonly byte[] m_indices = [0, 0, 1, 0, 2, 0, 0, 0, 2, 0, 3, 0];
    private readonly byte[] m_material = Floats([1, 1, 1, 0]);
    private readonly byte[] m_zero = new byte[16];
    private readonly float[] m_identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    /// <inheritdoc />
    public override void Build(RenderPipelineContext context)
    {
        if (!context.request.data.TryGet(channel, out ShaderPreviewContext? preview) || preview is null)
            throw new InvalidOperationException("Sprite preview input is missing.");
        if (!context.outputTexture.isValid) throw new InvalidOperationException("A preview requires an offscreen output.");
        if (!context.resourceService.TryResolveMaterialArtifact(preview.resourceId, preview.artifact, preview.material,
            Rendering2DIds.spriteContract, Rendering2DIds.alphaRole, ShaderProgramKind.Raster, m_vertices, null,
            preview.diagnostics, out RenderMaterialPass? material) || material is null) return;
        var resources = context.resourceService;
        PersistentBufferHandle vertices = resources.AcquireBuffer(new(pipelineId + "/vertices"), 1,
            new(new(4, 8, RenderBufferUsage.Vertex), m_vertices), m_vertexBytes, "Preview quad vertices");
        PersistentBufferHandle indices = resources.AcquireBuffer(new(pipelineId + "/indices"), 1,
            new(new(6, 2, RenderBufferUsage.Index), indexFormat: RenderIndexFormat.UInt16), m_indices, "Preview quad indices");
        PersistentBufferHandle instances = resources.AcquireBuffer(new(pipelineId + "/instances"), 1,
            new(new(1, 80, RenderBufferUsage.Vertex), m_instances), m_instanceBytes, "Preview Sprite instance");
        PersistentTextureHandle white = Texture("white", [255, 255, 255, 255]);
        PersistentTextureHandle black = Texture("black", [0, 0, 0, 255]);
        PersistentTextureHandle normal = Texture("normal", [128, 128, 255, 255]);
        var data = new DrawData(material, vertices, indices, instances, white, black, normal,
            m_material, m_zero, context.request.viewport);
        RasterPassBuilder pass = context.graph.AddRasterPass("Sprite draft preview", new(pipelineId), data, static (value, render) => Draw(value, render.commands));
        pass.SetViewTransform(m_identity, m_identity);
        pass.UseColorAttachment(context.outputTexture, 0, RenderLoadAction.Clear, RenderStoreAction.Store, new(.055f, .055f, .065f, 1));
        context.graph.MarkOutput(context.outputTexture);

        PersistentTextureHandle Texture(string name, byte[] rgba) => resources.AcquireTexture(new(pipelineId + "/" + name), 1,
            new(1, 1, RenderTextureFormat.RGBA8, RenderTextureUsage.Sampled), [new(0, 0, rgba)], "Preview " + name);
    }

    private static void Draw(DrawData data, RenderCommandEncoder commands)
    {
        commands.SetViewport(0, 0, data.viewport.width, data.viewport.height);
        data.material.Bind(commands);
        commands.SetUniform(new("u_spriteMaterial"), data.parameters);
        commands.SetUniform(new("u_lightSampling"), data.zero);
        commands.BindTexture(new("s_spriteTexture"), data.white, new());
        commands.BindTexture(new("s_normalTexture"), data.normal, new());
        commands.BindTexture(new("s_emissionTexture"), data.black, new());
        for (int index = 0; index < 4; index++)
        {
            commands.BindTexture(new("s_lightColor" + index), data.white, new());
            commands.BindTexture(new("s_lightDirection" + index), data.black, new());
        }
        commands.BindVertexBuffer(data.vertices);
        commands.BindIndexBuffer(data.indices);
        commands.BindInstanceBuffer(data.instances, 0, 1);
        commands.DrawIndexed(6);
    }

    private static byte[] Floats(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int index = 0; index < values.Length; index++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * 4, 4), values[index]);
        return bytes;
    }

    private sealed record DrawData(RenderMaterialPass material, PersistentBufferHandle vertices, PersistentBufferHandle indices,
        PersistentBufferHandle instances, PersistentTextureHandle white, PersistentTextureHandle black, PersistentTextureHandle normal,
        byte[] parameters, byte[] zero, RenderViewport viewport);
}
