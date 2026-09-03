using System;
using System.Collections.Generic;
using InnoEngine.Rendering;

namespace Inno.Rendering2D;

/// <summary>Builds the bounded sprite and tile batch pass owned entirely by the 2D Plugin.</summary>
[RenderPipelineExtension(Rendering2DIds.pipeline)]
public sealed class Rendering2DPipeline : RenderPipeline
{
    private static readonly RenderVertexLayout S_VERTEX_LAYOUT = new(
    [
        new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3),
        new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate0, RenderVertexFormat.Float2),
        new RenderVertexAttribute(RenderVertexSemantic.Color0, RenderVertexFormat.UInt8Normalized4),
        new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate1, RenderVertexFormat.Float1)
    ]);
    private static readonly RenderBufferUploadDescriptor S_VERTEX_UPLOAD = new(
        S_VERTEX_LAYOUT.stride,
        RenderBufferUsage.Vertex,
        S_VERTEX_LAYOUT);
    private static readonly RenderBufferUploadDescriptor S_INDEX_UPLOAD = new(
        sizeof(uint),
        RenderBufferUsage.Index,
        indexFormat: RenderIndexFormat.UInt32);
    private static readonly RenderPhaseId S_PHASE = new("inno.rendering.2d.main");
    private static readonly RenderBindingId S_TEXTURE_BINDING = new("s_spriteTexture");
    private static readonly RenderPersistentResourceId S_BUILTIN_WHITE_TEXTURE_ID = new(
        "inno.rendering.2d.builtin.white");
    private static readonly RenderTextureDescriptor S_BUILTIN_WHITE_TEXTURE_DESCRIPTOR = new(
        1,
        1,
        RenderTextureFormat.RGBA8,
        RenderTextureUsage.Sampled);
    private static readonly RenderTextureSubresourceData[] S_BUILTIN_WHITE_TEXTURE_DATA =
    [
        new RenderTextureSubresourceData(0, 0, [255, 255, 255, 255])
    ];
    private static readonly MaterialPropertyBlock S_EMPTY_OVERRIDES = new();

    /// <summary>Gets the frame channel consumed by this pipeline.</summary>
    public static RenderDataChannelId frameChannel => new("inno.rendering.2d.frame");

    /// <inheritdoc />
    public override void Build(RenderPipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.request.data.TryGet(frameChannel, out Rendering2DFrameSequence? sequence)
            || sequence is null
            || sequence.frames.Length == 0)
        {
            context.diagnostics.Publish(new RenderDiagnostic(
                "RENDERING_2D_FRAME_MISSING",
                "The 2D pipeline request does not contain a compatible frame snapshot.",
                RenderDiagnosticSeverity.Error,
                Rendering2DIds.pipeline));
            return;
        }
        for (int frameIndex = 0; frameIndex < sequence.frames.Length; frameIndex++)
        {
            Rendering2DFrame frame = sequence.frames[frameIndex];
            bool clearTarget = frame.clearTarget
                               && !context.preservePresentationTarget;
            foreach (string diagnostic in frame.diagnostics)
            {
                context.diagnostics.Publish(new RenderDiagnostic(
                    "RENDERING_2D_FRAME_WARNING",
                    diagnostic,
                    RenderDiagnosticSeverity.Warning,
                    Rendering2DIds.pipeline));
            }

            var prepared = new List<PreparedBatch>(frame.batches.Length);
            foreach (Rendering2DDrawBatch batch in frame.batches)
            {
                context.resourceService.PrewarmMaterial(batch.material);
                if (batch.texture is not null)
                    context.resourceService.PrewarmTexture(batch.texture);
                ShaderPassRoleId role = GetRole(batch.blendMode);
                if (!context.resourceService.TryResolveGraphicsMaterial(
                        batch.material,
                        Rendering2DIds.spriteContract,
                        role,
                        S_VERTEX_LAYOUT,
                        S_EMPTY_OVERRIDES,
                        out RenderMaterialPass? materialPass)
                    || materialPass is null)
                {
                    continue;
                }
                PersistentTextureHandle texture;
                if (batch.texture is not null)
                {
                    if (!context.resourceService.TryResolveTexture(batch.texture, out texture))
                        continue;
                }
                else
                {
                    texture = context.resourceService.AcquireTexture(
                        S_BUILTIN_WHITE_TEXTURE_ID,
                        revision: 1,
                        S_BUILTIN_WHITE_TEXTURE_DESCRIPTOR,
                        S_BUILTIN_WHITE_TEXTURE_DATA,
                        "2D built-in white texture");
                }
                RenderBufferSlice vertices = context.uploads.UploadBuffer(
                    S_VERTEX_UPLOAD,
                    batch.vertexBytes,
                    "2D vertices");
                RenderBufferSlice indices = context.uploads.UploadBuffer(
                    S_INDEX_UPLOAD,
                    batch.indexBytes,
                    "2D indices");
                prepared.Add(new PreparedBatch(
                    materialPass,
                    texture,
                    CreateSampler(batch.sampling),
                    vertices,
                    indices,
                    batch.indexCount));
            }

            var passData = new PassData(prepared.ToArray(), context.request.viewport);
            RasterPassBuilder pass = context.graph.AddRasterPass(
                $"2D Camera {frameIndex + 1}",
                S_PHASE,
                passData,
                static (data, passContext) => Execute(data, passContext.commands));
            pass.SetViewTransform(frame.viewMatrix, frame.projectionMatrix);
            if (context.outputTexture.isValid)
            {
                pass.UseColorAttachment(
                    context.outputTexture,
                    0,
                    clearTarget ? RenderLoadAction.Clear : RenderLoadAction.Load,
                    RenderStoreAction.Store,
                    frame.clearColor);
                context.graph.MarkOutput(context.outputTexture);
            }
            else
            {
                if (clearTarget)
                    pass.ClearPresentationTarget(frame.clearColor);
                pass.HasSideEffect();
            }
            pass.AllowParallelRecording();
        }
    }

    private static void Execute(PassData data, RenderCommandEncoder commands)
    {
        RenderViewport viewport = data.viewport;
        commands.SetViewport(viewport.x, viewport.y, viewport.width, viewport.height);
        foreach (PreparedBatch batch in data.batches)
        {
            batch.material.Bind(commands);
            commands.BindTexture(S_TEXTURE_BINDING, batch.texture, batch.sampler);
            commands.BindVertexBuffer(batch.vertices);
            commands.BindIndexBuffer(batch.indices);
            commands.DrawIndexed(batch.indexCount);
        }
    }

    private static ShaderPassRoleId GetRole(SpriteBlendMode2D blendMode)
        => blendMode switch
        {
            SpriteBlendMode2D.Alpha => Rendering2DIds.alphaRole,
            SpriteBlendMode2D.Premultiplied => Rendering2DIds.premultipliedRole,
            SpriteBlendMode2D.Additive => Rendering2DIds.additiveRole,
            SpriteBlendMode2D.Multiply => Rendering2DIds.multiplyRole,
            SpriteBlendMode2D.Opaque => Rendering2DIds.opaqueRole,
            _ => Rendering2DIds.alphaRole
        };

    private static RenderSamplerState CreateSampler(SpriteSamplingMode2D sampling)
    {
        RenderSamplerFilter filter = sampling is SpriteSamplingMode2D.PointClamp or SpriteSamplingMode2D.PointRepeat
            ? RenderSamplerFilter.Point
            : RenderSamplerFilter.Linear;
        RenderSamplerAddressMode address = sampling is SpriteSamplingMode2D.PointRepeat or SpriteSamplingMode2D.LinearRepeat
            ? RenderSamplerAddressMode.Repeat
            : RenderSamplerAddressMode.Clamp;
        return new RenderSamplerState(filter, address, address, RenderSamplerAddressMode.Clamp);
    }

    private sealed record PassData(
        PreparedBatch[] batches,
        RenderViewport viewport);

    private sealed record PreparedBatch(
        RenderMaterialPass material,
        PersistentTextureHandle texture,
        RenderSamplerState sampler,
        RenderBufferSlice vertices,
        RenderBufferSlice indices,
        int indexCount);
}
