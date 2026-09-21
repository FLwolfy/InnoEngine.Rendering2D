using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using InnoEngine.Diagnostics;
using InnoEngine.Rendering;

namespace Inno.Rendering2D;

/// <summary>Builds the bounded sprite and tile batch pass owned entirely by the 2D Plugin.</summary>
[RenderPipelineExtension(Rendering2DIds.pipeline)]
public sealed class Rendering2DPipeline : RenderPipeline
{
    private const int C_INSTANCE_STRIDE = 80;
    private PipelineState? m_state;
    private MaterialAsset? m_lightMaterial;
    private MaterialAsset? m_shadowMaterial;
    private MaterialAsset? m_prefilterMaterial;
    private MaterialAsset? m_downsampleMaterial;
    private MaterialAsset? m_upsampleMaterial;
    private MaterialAsset? m_compositeMaterial;

    private PipelineState state => m_state ??= new PipelineState();

    /// <summary>Gets the frame channel consumed by this pipeline.</summary>
    public static RenderDataChannelId frameChannel => new("inno.rendering.2d.frame");

    /// <inheritdoc />
    protected override void OnConfigure(SerializedRenderExtensionState configuration, RenderExtensionStateContext owner)
    {
        var settings = new Rendering2DPipelineSettings();
        configuration.Restore(settings, owner);
        m_lightMaterial = ForProgram(settings.lightAccumulation);
        m_shadowMaterial = ForProgram(settings.shadowStencil);
        m_prefilterMaterial = ForProgram(settings.bloomPrefilter);
        m_downsampleMaterial = ForProgram(settings.bloomDownsample);
        m_upsampleMaterial = ForProgram(settings.bloomUpsample);
        m_compositeMaterial = ForProgram(settings.finalComposite);
    }

    private static MaterialAsset? ForProgram(ShaderAsset? shader)
        => shader is null ? null : new MaterialAsset { shader = shader };

    /// <inheritdoc />
    public override void Build(RenderPipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PipelineState pipelineState = state;
        if (!context.request.data.TryGet(frameChannel, out Rendering2DFrameSequence? sequence)
            || sequence is null
            || sequence.frames.Length == 0)
        {
            context.diagnostics.Publish(new Diagnostic(
                "RENDERING_2D_FRAME_MISSING",
                "The 2D pipeline request does not contain a compatible frame snapshot.",
                DiagnosticSeverity.Error,
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
                context.diagnostics.Publish(new Diagnostic(
                    "RENDERING_2D_FRAME_WARNING",
                    diagnostic,
                    DiagnosticSeverity.Warning,
                    Rendering2DIds.pipeline));
            }

            if (frame.masksUnavailable)
            {
                PublishOutputUnavailable(context, frameIndex, "Mask coverage material or shape");
                continue;
            }

            if (!context.capabilities.Supports(GraphicsCapability.Instancing))
            {
                context.diagnostics.Publish(new Diagnostic(
                    "RENDERING_2D_INSTANCING_UNAVAILABLE",
                    "The active graphics backend cannot render the shared-quad 2D instance stream.",
                    DiagnosticSeverity.Error,
                    Rendering2DIds.pipeline));
                continue;
            }

            PersistentBufferHandle sharedVertices = context.resourceService.AcquireBuffer(
                pipelineState.sharedQuadVertexId,
                revision: 1,
                pipelineState.sharedQuadVertexDescriptor,
                pipelineState.sharedQuadVertices,
                "2D shared quad vertices");
            PersistentBufferHandle sharedIndices = context.resourceService.AcquireBuffer(
                pipelineState.sharedQuadIndexId,
                revision: 1,
                pipelineState.sharedQuadIndexDescriptor,
                pipelineState.sharedQuadIndices,
                "2D shared quad indices");

            PreparedBatch[] prepared = PrepareBatches(context, frame.batches);
            LightingFrameResources lighting = AddLightingPasses(
                context,
                frame,
                frameIndex,
                sharedVertices,
                sharedIndices);
            if (frame.lights.Length != 0 && !lighting.isValid && HasLitDrawables(frame))
            {
                PublishOutputUnavailable(context, frameIndex, "Lighting");
                continue;
            }
            bool usesMasks = false;
            for (int batchIndex = 0; batchIndex < prepared.Length; batchIndex++)
            {
                if (prepared[batchIndex].maskSet != 0
                    && prepared[batchIndex].maskInteraction != SpriteMaskInteraction2D.None)
                {
                    usesMasks = true;
                    break;
                }
            }
            if (usesMasks)
            {
                if (!AddMaskedFrame(
                    context,
                    frame,
                    frameIndex,
                    clearTarget,
                    prepared,
                    lighting,
                    sharedVertices,
                    sharedIndices))
                {
                    PublishOutputUnavailable(context, frameIndex, "Mask composition");
                }
                else ResolveOutputUnavailable(context, frameIndex);
            }
            else if (frame.postProcess is not null)
            {
                if (!AddPostProcessedFrame(
                    context,
                    frame,
                    frameIndex,
                    clearTarget,
                    prepared,
                    lighting,
                    sharedVertices,
                    sharedIndices))
                {
                    PublishOutputUnavailable(context, frameIndex, "Post-process composition");
                }
                else ResolveOutputUnavailable(context, frameIndex);
            }
            else
            {
                AddDirectFrame(
                    context,
                    frame,
                    frameIndex,
                    clearTarget,
                    prepared,
                    lighting,
                    sharedVertices,
                    sharedIndices);
                ResolveOutputUnavailable(context, frameIndex);
            }
        }
    }

    private static void PublishOutputUnavailable(RenderPipelineContext context, int frameIndex, string requirement)
        => context.diagnostics.Publish(new Diagnostic(
            "RENDERING_2D_OUTPUT_UNAVAILABLE",
            $"Camera {frameIndex + 1}: {requirement} is not ready. This output was not replaced by an unmasked or unprocessed render.",
            DiagnosticSeverity.Error,
            OutputDiagnosticId(context, frameIndex)));

    private static void ResolveOutputUnavailable(RenderPipelineContext context, int frameIndex)
        => context.diagnostics.Resolve("RENDERING_2D_OUTPUT_UNAVAILABLE", OutputDiagnosticId(context, frameIndex));

    private static string OutputDiagnosticId(RenderPipelineContext context, int frameIndex)
        => context.request.name + "/camera/" + frameIndex;

    private static bool HasLitDrawables(Rendering2DFrame frame)
    {
        foreach (Rendering2DDrawBatch batch in frame.batches)
            if (batch.lightBlendStyles != 0) return true;
        return false;
    }

    private PreparedBatch[] PrepareBatches(
        RenderPipelineContext context,
        IReadOnlyList<Rendering2DDrawBatch> batches)
    {
        var result = new List<PreparedBatch>(batches.Count);
        for (int index = 0; index < batches.Count; index++)
        {
            if (TryPrepareBatch(context, batches[index], out PreparedBatch? prepared)
                && prepared is not null)
            {
                result.Add(prepared);
            }
        }
        return result.ToArray();
    }

    private bool TryPrepareBatch(
        RenderPipelineContext context,
        Rendering2DDrawBatch batch,
        out PreparedBatch? prepared)
    {
        context.resourceService.PrewarmMaterial(batch.material);
        if (batch.texture.directTexture is not null)
            context.resourceService.PrewarmTexture(batch.texture.directTexture);
        else if (batch.texture.artifact is RenderTextureArtifactReference artifact)
            context.resourceService.PrewarmTextureArtifact(artifact);
        if (!context.resourceService.TryResolveGraphicsMaterial(
                batch.material,
                Rendering2DIds.spriteContract,
                GetRole(batch.blendMode),
                state.vertexLayout,
                state.emptyOverrides,
                out RenderMaterialPass? materialPass)
            || materialPass is null)
        {
            prepared = null;
            return false;
        }
        if (!TryResolveTexture(context, batch.texture, out PersistentTextureHandle texture))
        {
            prepared = null;
            return false;
        }
        PrewarmTexture(context, batch.normalMap);
        PrewarmTexture(context, batch.emissionMap);
        if (!TryResolveOptionalTexture(context, batch.normalMap, GetNeutralNormalTexture(context), out PersistentTextureHandle normalMap)
            || !TryResolveOptionalTexture(context, batch.emissionMap, GetWhiteTexture(context), out PersistentTextureHandle emissionMap))
        {
            prepared = null;
            return false;
        }

        PersistentBufferHandle persistentInstances = default;
        RenderBufferSlice transientInstances = default;
        if (batch.persistentInstanceBuffer is RenderPersistentResourceId persistentId)
        {
            persistentInstances = context.resourceService.AcquireBuffer(
                persistentId,
                batch.instanceRevision,
                new PersistentBufferDescriptor(
                    new RenderBufferDescriptor(
                        batch.instanceCount,
                        state.instanceLayout.stride,
                        RenderBufferUsage.Vertex),
                    state.instanceLayout),
                batch.instanceBytes,
                $"2D tile chunk {persistentId.value}");
        }
        else
        {
            transientInstances = context.uploads.UploadBuffer(
                state.instanceUpload,
                batch.instanceBytes,
                "2D instances");
        }
        prepared = new PreparedBatch(
            materialPass,
            texture,
            normalMap,
            emissionMap,
            CreateSampler(batch.sampling),
            transientInstances,
            persistentInstances,
            batch.instanceCount,
            batch.maskSet,
            batch.maskInteraction,
            CreateMaterialEffect(batch.emissionColor, batch.lightBlendStyles),
            batch.lightingLayer,
            batch.lightBlendStyles);
        return true;
    }

    private bool TryResolveTexture(
        RenderPipelineContext context,
        Rendering2DTextureSource source,
        out PersistentTextureHandle texture)
    {
        if (source.directTexture is TextureAsset directTexture)
            return context.resourceService.TryResolveTexture(directTexture, out texture);
        if (source.artifact is RenderTextureArtifactReference artifact)
            return context.resourceService.TryResolveTextureArtifact(artifact, out texture);
        texture = GetWhiteTexture(context);
        return true;
    }

    private static void PrewarmTexture(RenderPipelineContext context, Rendering2DTextureSource source)
    {
        if (source.directTexture is TextureAsset directTexture)
            context.resourceService.PrewarmTexture(directTexture);
        else if (source.artifact is RenderTextureArtifactReference artifact)
            context.resourceService.PrewarmTextureArtifact(artifact);
    }

    private bool TryResolveOptionalTexture(
        RenderPipelineContext context,
        Rendering2DTextureSource source,
        PersistentTextureHandle fallback,
        out PersistentTextureHandle texture)
    {
        if (source.directTexture is null && source.artifact is null)
        {
            texture = fallback;
            return true;
        }
        return TryResolveTexture(context, source, out texture);
    }

    private PersistentTextureHandle GetWhiteTexture(RenderPipelineContext context)
        => context.resourceService.AcquireTexture(
            state.builtinWhiteTextureId,
            revision: 1,
            state.builtinWhiteTextureDescriptor,
            state.builtinWhiteTextureData,
            "2D built-in white texture");

    private PersistentTextureHandle GetNeutralNormalTexture(RenderPipelineContext context)
        => context.resourceService.AcquireTexture(
            state.builtinNeutralNormalTextureId,
            revision: 1,
            state.builtinWhiteTextureDescriptor,
            state.builtinNeutralNormalTextureData,
            "2D built-in neutral normal texture");

    private LightingFrameResources AddLightingPasses(
        RenderPipelineContext context,
        Rendering2DFrame frame,
        int frameIndex,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices)
    {
        PipelineState pipelineState = state;
        if (frame.lights.Length == 0)
            return default;
        bool lightReady = TryResolveInternalMaterial(
            context,
            m_lightMaterial,
            Rendering2DIds.lightContract,
            Rendering2DIds.additiveRole,
            out RenderMaterialPass? lightMaterial)
            && lightMaterial is not null;
        bool utilityReady = TryResolveInternalMaterial(
            context,
            m_shadowMaterial,
            Rendering2DIds.shadowContract,
            Rendering2DIds.opaqueRole,
            out RenderMaterialPass? utilityMaterial)
            && utilityMaterial is not null;
        if (!lightReady || !utilityReady)
        {
            return default;
        }
        RenderMaterialPass resolvedLightMaterial = lightMaterial!;
        RenderMaterialPass resolvedUtilityMaterial = utilityMaterial!;

        var activeSlots = new bool[32 * 4];
        bool hasLitDrawables = false;
        for (int batchIndex = 0; batchIndex < frame.batches.Length; batchIndex++)
        {
            Rendering2DDrawBatch batch = frame.batches[batchIndex];
            if (batch.lightBlendStyles == 0)
                continue;
            hasLitDrawables = true;
            int layer = Math.Clamp(batch.lightingLayer, 0, 31);
            for (int style = 0; style < 4; style++)
            {
                if ((batch.lightBlendStyles & (1 << style)) != 0)
                    activeSlots[layer * 4 + style] = true;
            }
        }
        if (!hasLitDrawables)
            return default;

        RenderTextureFormat format = GetIntermediateColorFormat(context);
        var colors = new RenderTextureHandle[32 * 4];
        var directions = new RenderTextureHandle[32 * 4];
        for (int slot = 0; slot < activeSlots.Length; slot++)
        {
            if (!activeSlots[slot])
                continue;
            int layer = slot / 4;
            int style = slot % 4;
            colors[slot] = CreateIntermediateColor(
                context,
                frame,
                frameIndex,
                format,
                $"Light Layer {layer} Style {style} Color");
            directions[slot] = CreateIntermediateColor(
                context,
                frame,
                frameIndex,
                format,
                $"Light Layer {layer} Style {style} Direction");
        }
        bool supportsStencil = context.capabilities.SupportsRenderTarget(RenderTextureFormat.Depth24Stencil8);
        bool supportsMrt = context.capabilities.limits.maxColorAttachments >= 2;
        int lightDrawCount = 0;
        int shadowDrawCount = 0;
        RenderTextureHandle depthStencil = supportsStencil
            ? context.graph.CreateTexture(
                $"2D Camera {frameIndex + 1} Light Stencil",
                new RenderTextureDescriptor(
                    frame.pixelWidth,
                    frame.pixelHeight,
                    RenderTextureFormat.Depth24Stencil8,
                    RenderTextureUsage.DepthStencilAttachment))
            : default;
        if (!supportsStencil && frame.shadowCasters.Length > 0)
        {
            context.diagnostics.Publish(new Diagnostic(
                "RENDERING_2D_LIGHT_STENCIL_UNAVAILABLE",
                "The active backend has no D24S8 target; 2D lights remain GPU-accumulated but shadow casters are disabled.",
                DiagnosticSeverity.Warning,
                Rendering2DIds.pipeline));
        }
        if (!supportsMrt)
        {
            context.diagnostics.Publish(new Diagnostic(
                "RENDERING_2D_LIGHT_MRT_UNAVAILABLE",
                "The active backend has fewer than two color attachments; 2D light color remains available but normal-map direction accumulation is disabled.",
                DiagnosticSeverity.Warning,
                Rendering2DIds.pipeline));
        }

        RenderBufferSlice stencilClearInstances = context.uploads.UploadBuffer(
            state.instanceUpload,
            CreateStencilClearInstanceBytes(frame.worldBounds),
            "2D light stencil clear instance");
        var stencilClear = new PreparedBatch(
            resolvedUtilityMaterial,
            GetWhiteTexture(context),
            GetNeutralNormalTexture(context),
            GetWhiteTexture(context),
            RenderSamplerState.linearClamp,
            stencilClearInstances,
            default,
            1,
            0,
            SpriteMaskInteraction2D.None,
            state.materialEffectNone,
            0,
            0,
            BatchBindingKind.Stencil);

        for (int layer = 0; layer < 32; layer++)
        {
            for (int style = 0; style < 4; style++)
            {
                int slot = layer * 4 + style;
                if (!activeSlots[slot])
                    continue;
                LightDrawCommand[] commands = PrepareLightCommands(
                    context,
                    frame,
                    layer,
                    style,
                    supportsStencil,
                    resolvedLightMaterial,
                    resolvedUtilityMaterial);
                lightDrawCount += commands.Length;
                for (int commandIndex = 0; commandIndex < commands.Length; commandIndex++)
                {
                    if (commands[commandIndex].shadow is not null)
                        shadowDrawCount++;
                }
                var passData = new LightPassData(
                    state,
                    commands,
                    stencilClear,
                    supportsStencil,
                    sharedVertices,
                    sharedIndices,
                    new RenderViewport(0, 0, frame.pixelWidth, frame.pixelHeight));
                RasterPassBuilder pass = context.graph.AddRasterPass(
                    $"2D Camera {frameIndex + 1} Light Layer {layer} Style {style}",
                    state.lightPhase,
                    passData,
                    static (data, passContext) => ExecuteLights(data, passContext.commands));
                pass.SetViewTransform(frame.viewMatrix, frame.projectionMatrix);
                pass.UseColorAttachment(
                    colors[slot],
                    0,
                    RenderLoadAction.Clear,
                    RenderStoreAction.Store,
                    default);
                if (supportsMrt)
                {
                    pass.UseColorAttachment(
                        directions[slot],
                        1,
                        RenderLoadAction.Clear,
                        RenderStoreAction.Store,
                        default);
                }
                if (supportsStencil)
                {
                    pass.UseDepthAttachment(
                        depthStencil,
                        RenderLoadAction.Clear,
                        RenderStoreAction.Store,
                        clearDepth: 1f,
                        clearStencil: 0);
                }
            }
        }
        if (!supportsMrt)
        {
            for (int slot = 0; slot < activeSlots.Length; slot++)
            {
                if (!activeSlots[slot])
                    continue;
                int layer = slot / 4;
                int style = slot % 4;
                RasterPassBuilder clearDirection = context.graph.AddRasterPass(
                    $"2D Camera {frameIndex + 1} Light Layer {layer} Style {style} Direction Clear",
                    state.lightPhase,
                    0,
                    static (_, _) => { });
                clearDirection.UseColorAttachment(
                    directions[slot],
                    0,
                    RenderLoadAction.Clear,
                    RenderStoreAction.Store,
                    default);
            }
        }
        return new LightingFrameResources(
            colors,
            directions,
            context.capabilities.originBottomLeft,
            format == RenderTextureFormat.RGBA16Float,
            supportsStencil,
            supportsMrt,
            lightDrawCount,
            shadowDrawCount);
    }

    private LightDrawCommand[] PrepareLightCommands(
        RenderPipelineContext context,
        Rendering2DFrame frame,
        int layer,
        int style,
        bool supportsStencil,
        RenderMaterialPass lightMaterial,
        RenderMaterialPass utilityMaterial)
    {
        var result = new List<LightDrawCommand>();
        for (int lightIndex = 0; lightIndex < frame.lights.Length; lightIndex++)
        {
            Rendering2DLight light = frame.lights[lightIndex];
            if ((int)light.blendStyle != style
                || light.intensity <= 0f
                || !light.layers.Contains(new InnoEngine.Scene.GameLayer(layer)))
                continue;
            bool hasCasters = supportsStencil
                && light.castShadows
                && HasMatchingCaster(frame.shadowCasters, light, style);
            int sampleCount = hasCasters && light.shadowSoftness > 0.001f ? 4 : 1;
            PrewarmTexture(context, light.cookie);
            if (!TryResolveOptionalTexture(
                    context,
                    light.cookie,
                    GetWhiteTexture(context),
                    out PersistentTextureHandle cookie))
            {
                continue;
            }
            byte[] lightBytes = CreateLightInstanceBytes(frame, light, 1f / sampleCount);
            var lightBatch = new PreparedBatch(
                lightMaterial,
                cookie,
                GetNeutralNormalTexture(context),
                GetWhiteTexture(context),
                RenderSamplerState.linearClamp,
                context.uploads.UploadBuffer(state.instanceUpload, lightBytes, $"2D light {lightIndex}"),
                default,
                lightBytes.Length / C_INSTANCE_STRIDE,
                0,
                SpriteMaskInteraction2D.None,
                CreateLightMaterialEffect(light.normalIntensity),
                0,
                0,
                BatchBindingKind.Light);
            for (int sample = 0; sample < sampleCount; sample++)
            {
                PreparedBatch? shadowBatch = null;
                if (hasCasters)
                {
                    byte[] shadowBytes = CreateShadowInstanceBytes(frame, light, style, sample, sampleCount);
                    if (shadowBytes.Length > 0)
                    {
                        shadowBatch = new PreparedBatch(
                            utilityMaterial,
                            GetWhiteTexture(context),
                            GetNeutralNormalTexture(context),
                            GetWhiteTexture(context),
                            RenderSamplerState.linearClamp,
                            context.uploads.UploadBuffer(
                                state.instanceUpload,
                                shadowBytes,
                                $"2D light {lightIndex} shadow sample {sample}"),
                            default,
                            shadowBytes.Length / C_INSTANCE_STRIDE,
                            0,
                            SpriteMaskInteraction2D.None,
                            state.materialEffectNone,
                            0,
                            0,
                            BatchBindingKind.Stencil);
                    }
                }
                result.Add(new LightDrawCommand(lightBatch, shadowBatch));
            }
        }
        return result.ToArray();
    }

    private static bool HasMatchingCaster(
        IReadOnlyList<Rendering2DShadowCaster> casters,
        Rendering2DLight light,
        int style)
    {
        byte styleBit = (byte)(1 << style);
        for (int index = 0; index < casters.Count; index++)
        {
            if ((casters[index].lightBlendStyles & styleBit) != 0
                && light.layers.Contains(casters[index].layer))
            {
                return true;
            }
        }
        return false;
    }

    private static byte[] CreateLightInstanceBytes(
        Rendering2DFrame frame,
        Rendering2DLight light,
        float sampleWeight)
    {
        int count = light.kind == LightKind2D.Freeform && light.shape.Length >= 3
            ? light.shape.Length - 2
            : 1;
        var result = new byte[count * C_INSTANCE_STRIDE];
        if (light.kind == LightKind2D.Freeform && light.shape.Length >= 3)
        {
            for (int index = 0; index < count; index++)
            {
                WriteLightInstance(
                    result,
                    index,
                    light.shape[0],
                    light.shape[index + 1],
                    light.shape[0],
                    light.shape[index + 2],
                    light,
                    sampleWeight);
            }
        }
        else
        {
            InnoEngine.Mathematics.Rect bounds = light.kind == LightKind2D.Global
                ? frame.worldBounds
                : new InnoEngine.Mathematics.Rect(
                    light.position.x - light.range,
                    light.position.y - light.range,
                    light.range * 2f,
                    light.range * 2f);
            WriteLightInstance(
                result,
                0,
                new InnoEngine.Mathematics.Vector2(bounds.left, bounds.top),
                new InnoEngine.Mathematics.Vector2(bounds.right, bounds.top),
                new InnoEngine.Mathematics.Vector2(bounds.left, bounds.bottom),
                new InnoEngine.Mathematics.Vector2(bounds.right, bounds.bottom),
                light,
                sampleWeight);
        }
        return result;
    }

    private static void WriteLightInstance(
        byte[] destination,
        int index,
        InnoEngine.Mathematics.Vector2 corner0,
        InnoEngine.Mathematics.Vector2 corner1,
        InnoEngine.Mathematics.Vector2 corner3,
        InnoEngine.Mathematics.Vector2 corner2,
        Rendering2DLight light,
        float sampleWeight)
    {
        int offset = index * C_INSTANCE_STRIDE;
        WriteVector2(destination, offset, corner0);
        WriteVector2(destination, offset + 8, corner1);
        WriteVector2(destination, offset + 16, corner3);
        WriteVector2(destination, offset + 24, corner2);
        WriteVector2(destination, offset + 32, light.position);
        WriteVector2(destination, offset + 40, light.direction);
        WriteFloat(destination, offset + 48, 0f);
        WriteFloat(destination, offset + 52, (float)light.kind);
        WriteFloat(destination, offset + 56, light.range);
        WriteFloat(destination, offset + 60, light.falloff);
        WriteFloat(destination, offset + 64, light.color.r * light.intensity * sampleWeight);
        WriteFloat(destination, offset + 68, light.color.g * light.intensity * sampleWeight);
        WriteFloat(destination, offset + 72, light.color.b * light.intensity * sampleWeight);
        WriteFloat(destination, offset + 76, MathF.Cos(light.spotAngle * 0.5f));
    }

    private static byte[] CreateShadowInstanceBytes(
        Rendering2DFrame frame,
        Rendering2DLight light,
        int style,
        int sampleIndex,
        int sampleCount)
    {
        byte styleBit = (byte)(1 << style);
        int count = 0;
        for (int index = 0; index < frame.shadowCasters.Length; index++)
        {
            Rendering2DShadowCaster caster = frame.shadowCasters[index];
            if ((caster.lightBlendStyles & styleBit) == 0 || !light.layers.Contains(caster.layer))
                continue;
            count += CountShadowEdges(caster.shape, light, sampleIndex, sampleCount);
            if (caster.selfShadows)
                count += caster.shape.Length - 2;
        }
        if (count == 0)
            return [];

        var result = new byte[count * C_INSTANCE_STRIDE];
        float extent = MathF.Sqrt(
            frame.worldBounds.width * frame.worldBounds.width
            + frame.worldBounds.height * frame.worldBounds.height) + light.range * 2f + 1f;
        InnoEngine.Mathematics.Vector2 sampleOffset = GetShadowSampleOffset(sampleIndex, sampleCount)
            * (light.shadowSoftness * light.range * 0.035f);
        InnoEngine.Mathematics.Vector2 lightPosition = light.position + sampleOffset;
        InnoEngine.Mathematics.Vector2 globalDirection = GetGlobalShadowDirection(
            light,
            sampleIndex,
            sampleCount);
        int outputIndex = 0;
        for (int casterIndex = 0; casterIndex < frame.shadowCasters.Length; casterIndex++)
        {
            Rendering2DShadowCaster caster = frame.shadowCasters[casterIndex];
            if ((caster.lightBlendStyles & styleBit) == 0 || !light.layers.Contains(caster.layer))
                continue;
            InnoEngine.Mathematics.Vector2[] shape = caster.shape;
            float winding = GetSignedArea(shape);
            for (int edgeIndex = 0; edgeIndex < shape.Length; edgeIndex++)
            {
                InnoEngine.Mathematics.Vector2 first = shape[edgeIndex];
                InnoEngine.Mathematics.Vector2 second = shape[(edgeIndex + 1) % shape.Length];
                if (!CastsShadow(first, second, winding, light, lightPosition, globalDirection))
                    continue;
                InnoEngine.Mathematics.Vector2 firstDirection = light.kind == LightKind2D.Global
                    ? -globalDirection
                    : NormalizeOr(first - lightPosition, -light.direction);
                InnoEngine.Mathematics.Vector2 secondDirection = light.kind == LightKind2D.Global
                    ? -globalDirection
                    : NormalizeOr(second - lightPosition, -light.direction);
                WriteShadowInstance(
                    result,
                    outputIndex++,
                    first,
                    second,
                    first + firstDirection * extent,
                    second + secondDirection * extent);
            }
            if (!caster.selfShadows)
                continue;
            for (int triangleIndex = 1; triangleIndex < shape.Length - 1; triangleIndex++)
            {
                WriteShadowInstance(
                    result,
                    outputIndex++,
                    shape[0],
                    shape[triangleIndex],
                    shape[0],
                    shape[triangleIndex + 1]);
            }
        }
        return result;
    }

    private static int CountShadowEdges(
        IReadOnlyList<InnoEngine.Mathematics.Vector2> shape,
        Rendering2DLight light,
        int sampleIndex,
        int sampleCount)
    {
        float winding = GetSignedArea(shape);
        InnoEngine.Mathematics.Vector2 sampleOffset = GetShadowSampleOffset(sampleIndex, sampleCount)
            * (light.shadowSoftness * light.range * 0.035f);
        InnoEngine.Mathematics.Vector2 lightPosition = light.position + sampleOffset;
        InnoEngine.Mathematics.Vector2 globalDirection = GetGlobalShadowDirection(
            light,
            sampleIndex,
            sampleCount);
        int count = 0;
        for (int index = 0; index < shape.Count; index++)
        {
            if (CastsShadow(
                    shape[index],
                    shape[(index + 1) % shape.Count],
                    winding,
                    light,
                    lightPosition,
                    globalDirection))
            {
                count++;
            }
        }
        return count;
    }

    private static bool CastsShadow(
        InnoEngine.Mathematics.Vector2 first,
        InnoEngine.Mathematics.Vector2 second,
        float winding,
        Rendering2DLight light,
        InnoEngine.Mathematics.Vector2 lightPosition,
        InnoEngine.Mathematics.Vector2 globalDirection)
    {
        InnoEngine.Mathematics.Vector2 edge = second - first;
        InnoEngine.Mathematics.Vector2 outward = winding >= 0f
            ? new InnoEngine.Mathematics.Vector2(edge.y, -edge.x)
            : new InnoEngine.Mathematics.Vector2(-edge.y, edge.x);
        InnoEngine.Mathematics.Vector2 towardLight = light.kind == LightKind2D.Global
            ? -globalDirection
            : lightPosition - (first + second) * 0.5f;
        return InnoEngine.Mathematics.Vector2.Dot(outward, towardLight) <= 0f;
    }

    private static float GetSignedArea(IReadOnlyList<InnoEngine.Mathematics.Vector2> shape)
    {
        float twiceArea = 0f;
        for (int index = 0; index < shape.Count; index++)
        {
            InnoEngine.Mathematics.Vector2 first = shape[index];
            InnoEngine.Mathematics.Vector2 second = shape[(index + 1) % shape.Count];
            twiceArea += first.x * second.y - second.x * first.y;
        }
        return twiceArea;
    }

    private static InnoEngine.Mathematics.Vector2 GetGlobalShadowDirection(
        Rendering2DLight light,
        int sampleIndex,
        int sampleCount)
    {
        InnoEngine.Mathematics.Vector2 direction = NormalizeOr(
            light.direction,
            new InnoEngine.Mathematics.Vector2(0f, -1f));
        if (sampleCount <= 1 || light.shadowSoftness <= 0.001f)
            return direction;
        float offset = GetShadowSampleOffset(sampleIndex, sampleCount).x
            * light.shadowSoftness
            * 0.08f;
        InnoEngine.Mathematics.Vector2 perpendicular = new(-direction.y, direction.x);
        return NormalizeOr(direction + perpendicular * offset, direction);
    }

    private static void WriteShadowInstance(
        byte[] destination,
        int index,
        InnoEngine.Mathematics.Vector2 corner0,
        InnoEngine.Mathematics.Vector2 corner1,
        InnoEngine.Mathematics.Vector2 corner3,
        InnoEngine.Mathematics.Vector2 corner2)
    {
        int offset = index * C_INSTANCE_STRIDE;
        WriteVector2(destination, offset, corner0);
        WriteVector2(destination, offset + 8, corner1);
        WriteVector2(destination, offset + 16, corner3);
        WriteVector2(destination, offset + 24, corner2);
    }

    private static InnoEngine.Mathematics.Vector2 GetShadowSampleOffset(int index, int count)
    {
        if (count <= 1)
            return InnoEngine.Mathematics.Vector2.ZERO;
        return index switch
        {
            0 => new InnoEngine.Mathematics.Vector2(-0.70710677f, -0.70710677f),
            1 => new InnoEngine.Mathematics.Vector2(0.70710677f, -0.70710677f),
            2 => new InnoEngine.Mathematics.Vector2(-0.70710677f, 0.70710677f),
            _ => new InnoEngine.Mathematics.Vector2(0.70710677f, 0.70710677f)
        };
    }

    private static InnoEngine.Mathematics.Vector2 NormalizeOr(
        InnoEngine.Mathematics.Vector2 value,
        InnoEngine.Mathematics.Vector2 fallback)
    {
        float length = value.Length();
        return length > 0.0001f ? value / length : fallback;
    }

    private static void WriteVector2(
        byte[] destination,
        int offset,
        InnoEngine.Mathematics.Vector2 value)
    {
        WriteFloat(destination, offset, value.x);
        WriteFloat(destination, offset + sizeof(float), value.y);
    }

    private static void AddLightingReads(RasterPassBuilder pass, LightingFrameResources lighting)
    {
        if (!lighting.isValid)
            return;
        for (int slot = 0; slot < lighting.colors.Length; slot++)
        {
            if (!lighting.colors[slot].isValid)
                continue;
            pass.ReadTexture(lighting.colors[slot]);
            pass.ReadTexture(lighting.directions[slot]);
        }
    }

    private void AddDirectFrame(
        RenderPipelineContext context,
        Rendering2DFrame frame,
        int frameIndex,
        bool clearTarget,
        PreparedBatch[] prepared,
        LightingFrameResources lighting,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices)
    {
        var passData = new PassData(
            state,
            prepared,
            lighting,
            sharedVertices,
            sharedIndices,
            context.request.viewport);
        RasterPassBuilder pass = context.graph.AddRasterPass(
            $"2D Camera {frameIndex + 1}",
            state.phase,
            passData,
            static (data, passContext) => Execute(data, passContext.commands));
        pass.SetViewTransform(frame.viewMatrix, frame.projectionMatrix);
        AddLightingReads(pass, lighting);
        AttachOutput(context, pass, clearTarget, frame.clearColor);
        pass.AllowParallelRecording();
    }

    private bool AddPostProcessedFrame(
        RenderPipelineContext context,
        Rendering2DFrame frame,
        int frameIndex,
        bool clearTarget,
        PreparedBatch[] prepared,
        LightingFrameResources lighting,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices)
    {
        RenderTextureFormat colorFormat = GetIntermediateColorFormat(context);
        RenderTextureHandle sceneColor = CreateIntermediateColor(
            context,
            frame,
            frameIndex,
            colorFormat,
            "HDR Color");
        var passData = new PassData(
            state,
            prepared,
            lighting,
            sharedVertices,
            sharedIndices,
            new RenderViewport(0, 0, frame.pixelWidth, frame.pixelHeight));
        RasterPassBuilder pass = context.graph.AddRasterPass(
            $"2D Camera {frameIndex + 1} HDR",
            state.phase,
            passData,
            static (data, passContext) => Execute(data, passContext.commands));
        pass.SetViewTransform(frame.viewMatrix, frame.projectionMatrix);
        AddLightingReads(pass, lighting);
        pass.UseColorAttachment(
            sceneColor,
            0,
            RenderLoadAction.Clear,
            RenderStoreAction.Store,
            clearTarget ? frame.clearColor : default);
        pass.AllowParallelRecording();
        return AddPostProcessAndOutput(
            context,
            frame,
            frameIndex,
            clearTarget,
            colorFormat,
            sceneColor,
            sharedVertices,
            sharedIndices);
    }

    private bool AddMaskedFrame(
        RenderPipelineContext context,
        Rendering2DFrame frame,
        int frameIndex,
        bool clearTarget,
        PreparedBatch[] prepared,
        LightingFrameResources lighting,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices)
    {
        if (!context.capabilities.SupportsRenderTarget(RenderTextureFormat.Depth24Stencil8))
        {
            context.diagnostics.Publish(new Diagnostic(
                "RENDERING_2D_STENCIL_UNAVAILABLE",
                "The active graphics backend has no D24S8 attachment required by SpriteMask2D.",
                DiagnosticSeverity.Error,
                Rendering2DIds.pipeline));
            return false;
        }
        RenderTextureFormat colorFormat = GetIntermediateColorFormat(context);

        PreparedBatch?[] masks = new PreparedBatch?[frame.maskBatches.Length];
        for (int maskIndex = 0; maskIndex < frame.maskBatches.Length; maskIndex++)
        {
            if (!TryPrepareBatch(context, frame.maskBatches[maskIndex], out masks[maskIndex])) return false;
        }
        if (!TryResolveInternalMaterial(context, m_shadowMaterial, Rendering2DIds.shadowContract, Rendering2DIds.opaqueRole,
                out RenderMaterialPass? utilityMaterial)
            || utilityMaterial is null)
        {
            return false;
        }

        RenderTextureHandle sceneColor = CreateIntermediateColor(
            context,
            frame,
            frameIndex,
            colorFormat,
            "Masked HDR Color");
        RenderTextureHandle depthStencil = context.graph.CreateTexture(
            $"2D Camera {frameIndex + 1} Stencil",
            new RenderTextureDescriptor(
                frame.pixelWidth,
                frame.pixelHeight,
                RenderTextureFormat.Depth24Stencil8,
                RenderTextureUsage.DepthStencilAttachment));
        RenderBufferSlice stencilClearInstances = context.uploads.UploadBuffer(
            state.instanceUpload,
            CreateStencilClearInstanceBytes(frame.worldBounds),
            "2D stencil clear instance");
        var stencilClear = new PreparedBatch(
            utilityMaterial,
            GetWhiteTexture(context),
            GetNeutralNormalTexture(context),
            GetWhiteTexture(context),
            RenderSamplerState.linearClamp,
            stencilClearInstances,
            default,
            1,
            0,
            SpriteMaskInteraction2D.None,
            state.materialEffectNone,
            0,
            0,
            BatchBindingKind.Stencil);
        var maskedData = new MaskedPassData(
            state,
            prepared,
            masks,
            stencilClear,
            lighting,
            sharedVertices,
            sharedIndices,
            new RenderViewport(0, 0, frame.pixelWidth, frame.pixelHeight));
        RasterPassBuilder maskedPass = context.graph.AddRasterPass(
            $"2D Camera {frameIndex + 1} Masked HDR",
            state.phase,
            maskedData,
            static (data, passContext) => ExecuteMasked(data, passContext.commands));
        maskedPass.SetViewTransform(frame.viewMatrix, frame.projectionMatrix);
        AddLightingReads(maskedPass, lighting);
        maskedPass.UseColorAttachment(
            sceneColor,
            0,
            RenderLoadAction.Clear,
            RenderStoreAction.Store,
            clearTarget ? frame.clearColor : default);
        maskedPass.UseDepthAttachment(
            depthStencil,
            RenderLoadAction.Clear,
            RenderStoreAction.Store,
            clearDepth: 1f,
            clearStencil: 0);
        maskedPass.AllowParallelRecording();
        return AddPostProcessAndOutput(
            context,
            frame,
            frameIndex,
            clearTarget,
            colorFormat,
            sceneColor,
            sharedVertices,
            sharedIndices);
    }

    private static RenderTextureFormat GetIntermediateColorFormat(RenderPipelineContext context)
    {
        if (context.capabilities.SupportsRenderTarget(RenderTextureFormat.RGBA16Float)
            && context.capabilities.SupportsSampled(RenderTextureFormat.RGBA16Float))
        {
            return RenderTextureFormat.RGBA16Float;
        }
        context.diagnostics.Publish(new Diagnostic(
            "RENDERING_2D_HDR_FALLBACK",
            "The 2D intermediate uses RGBA8 because RGBA16Float sampling or attachment support is unavailable.",
            DiagnosticSeverity.Warning,
            Rendering2DIds.pipeline));
        return RenderTextureFormat.RGBA8;
    }

    private static RenderTextureHandle CreateIntermediateColor(
        RenderPipelineContext context,
        Rendering2DFrame frame,
        int frameIndex,
        RenderTextureFormat format,
        string role)
        => context.graph.CreateTexture(
            $"2D Camera {frameIndex + 1} {role}",
            new RenderTextureDescriptor(
                frame.pixelWidth,
                frame.pixelHeight,
                format,
                RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled));

    private static RenderTextureHandle CreateIntermediateColor(
        RenderPipelineContext context,
        int frameIndex,
        int width,
        int height,
        RenderTextureFormat format,
        string role)
        => context.graph.CreateTexture(
            $"2D Camera {frameIndex + 1} {role}",
            new RenderTextureDescriptor(
                Math.Max(1, width),
                Math.Max(1, height),
                format,
                RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled));

    private bool AddPostProcessAndOutput(
        RenderPipelineContext context,
        Rendering2DFrame frame,
        int frameIndex,
        bool clearTarget,
        RenderTextureFormat colorFormat,
        RenderTextureHandle sceneColor,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices)
    {
        ShaderPassRoleId finalRole = clearTarget ? Rendering2DIds.opaqueRole : Rendering2DIds.premultipliedRole;
        if (!TryResolveInternalMaterial(context, m_compositeMaterial, Rendering2DIds.postProcessContract, finalRole,
                out RenderMaterialPass? finalMaterial) || finalMaterial is null) return false;
        RenderTextureHandle bloomTexture = default;
        if (frame.postProcess is Rendering2DPostProcessSettings profile && profile.bloomIntensity > 0f)
        {
            if (!TryResolveInternalMaterial(context, m_prefilterMaterial, Rendering2DIds.postProcessContract, Rendering2DIds.opaqueRole, out var prefilter)
                || !TryResolveInternalMaterial(context, m_downsampleMaterial, Rendering2DIds.postProcessContract, Rendering2DIds.opaqueRole, out var downsample)
                || !TryResolveInternalMaterial(context, m_upsampleMaterial, Rendering2DIds.postProcessContract, Rendering2DIds.opaqueRole, out var upsample)
                || prefilter is null || downsample is null || upsample is null) return false;
            int levelCount = Math.Clamp(profile.bloomLevels, 1,
                Math.Max(1, (int)MathF.Floor(MathF.Log2(Math.Max(2, Math.Min(frame.pixelWidth, frame.pixelHeight)))) - 1));
            var pyramid = new RenderTextureHandle[levelCount];
            RenderTextureHandle source = sceneColor;
            int sourceWidth = frame.pixelWidth;
            int sourceHeight = frame.pixelHeight;
            for (int level = 0; level < levelCount; level++)
            {
                int width = Math.Max(1, sourceWidth / 2);
                int height = Math.Max(1, sourceHeight / 2);
                pyramid[level] = CreateIntermediateColor(context, frameIndex, width, height, colorFormat, $"Bloom Down {level + 1}");
                AddEffect($"Bloom Down {level + 1}", level == 0 ? prefilter : downsample, source, default,
                    pyramid[level], width, height, level == 0 ? CompositeKind.Prefilter : CompositeKind.Downsample,
                    Pack(1f / sourceWidth, 1f / sourceHeight), Pack(profile.bloomIntensity, profile.bloomThreshold));
                source = pyramid[level];
                sourceWidth = width;
                sourceHeight = height;
            }
            bloomTexture = pyramid[^1];
            for (int level = levelCount - 2; level >= 0; level--)
            {
                int width = Math.Max(1, frame.pixelWidth >> (level + 1));
                int height = Math.Max(1, frame.pixelHeight >> (level + 1));
                RenderTextureHandle combined = CreateIntermediateColor(context, frameIndex, width, height, colorFormat, $"Bloom Up {level + 1}");
                AddEffect($"Bloom Up {level + 1}", upsample, bloomTexture, pyramid[level], combined, width, height,
                    CompositeKind.Upsample, Pack(1f / Math.Max(1, width / 2), 1f / Math.Max(1, height / 2)), Pack(Math.Clamp(profile.bloomScatter, 0f, 1f)));
                bloomTexture = combined;
            }
        }
        Rendering2DPostProcessSettings? settings = frame.postProcess;
        int pixelation = Math.Max(1, settings?.pixelation ?? 1);
        var compositeData = new CompositePassData(state, finalMaterial, sceneColor, bloomTexture, CompositeKind.Final,
            Pack(context.capabilities.originBottomLeft ? 1f : 0f),
            Pack(pixelation / (float)Math.Max(1, frame.pixelWidth), pixelation / (float)Math.Max(1, frame.pixelHeight)),
            state.materialEffectNone,
            Pack(settings?.exposure ?? 0f, settings?.contrast ?? 1f, settings?.saturation ?? 1f, settings?.vignette ?? 0f),
            Pack(settings?.toneMapping == true ? 1f : 0f, bloomTexture.isValid ? 1f : 0f, pixelation),
            sharedVertices, sharedIndices, context.request.viewport);
        RasterPassBuilder compositePass = context.graph.AddRasterPass(
            $"2D Camera {frameIndex + 1} Final Composite", state.compositePhase, compositeData,
            static (data, passContext) => ExecuteComposite(data, passContext.commands));
        compositePass.ReadTexture(sceneColor);
        if (bloomTexture.isValid) compositePass.ReadTexture(bloomTexture);
        AttachOutput(context, compositePass, clearTarget, frame.clearColor);
        compositePass.AllowParallelRecording();
        return true;

        void AddEffect(string label, RenderMaterialPass material, RenderTextureHandle source, RenderTextureHandle auxiliary,
            RenderTextureHandle output, int width, int height, CompositeKind kind, byte[] texel, byte[] bloom)
        {
            var data = new CompositePassData(state, material, source, auxiliary, kind,
                Pack(context.capabilities.originBottomLeft ? 1f : 0f), texel, bloom,
                state.materialEffectNone, state.materialEffectNone, sharedVertices, sharedIndices, new(0, 0, width, height));
            RasterPassBuilder pass = context.graph.AddRasterPass($"2D Camera {frameIndex + 1} {label}",
                state.compositePhase, data, static (value, passContext) => ExecuteComposite(value, passContext.commands));
            pass.ReadTexture(source);
            if (auxiliary.isValid) pass.ReadTexture(auxiliary);
            pass.UseColorAttachment(output, 0, RenderLoadAction.Clear, RenderStoreAction.Store, default);
            pass.AllowParallelRecording();
        }
    }

    private bool TryResolveInternalMaterial(RenderPipelineContext context, MaterialAsset? material, ShaderContractId contract,
        ShaderPassRoleId role, out RenderMaterialPass? materialPass)
    {
        materialPass = null;
        if (material?.shader is not { isMissing: false }) return false;
        context.resourceService.PrewarmMaterial(material);
        return context.resourceService.TryResolveGraphicsMaterial(material, contract, role, state.vertexLayout, state.emptyOverrides, out materialPass);
    }

    private static void AttachOutput(
        RenderPipelineContext context,
        RasterPassBuilder pass,
        bool clearTarget,
        RenderClearColor clearColor)
    {
        if (context.outputTexture.isValid)
        {
            pass.UseColorAttachment(
                context.outputTexture,
                0,
                clearTarget ? RenderLoadAction.Clear : RenderLoadAction.Load,
                RenderStoreAction.Store,
                clearColor);
            context.graph.MarkOutput(context.outputTexture);
        }
        else
        {
            if (clearTarget)
                pass.ClearPresentationTarget(clearColor);
            pass.HasSideEffect();
        }
    }

    private static void Execute(PassData data, RenderCommandEncoder commands)
    {
        RenderViewport viewport = data.viewport;
        commands.SetViewport(viewport.x, viewport.y, viewport.width, viewport.height);
        foreach (PreparedBatch batch in data.batches)
            DrawBatch(batch, data.sharedVertices, data.sharedIndices, data.state, data.lighting, commands);
    }

    private static void ExecuteLights(LightPassData data, RenderCommandEncoder commands)
    {
        RenderViewport viewport = data.viewport;
        commands.SetViewport(viewport.x, viewport.y, viewport.width, viewport.height);
        for (int index = 0; index < data.commands.Length; index++)
        {
            LightDrawCommand command = data.commands[index];
            if (data.supportsStencil)
            {
                DrawStencilUtility(
                    data.stencilClear,
                    data.state.stencilClear,
                    data.state,
                    data.sharedVertices,
                    data.sharedIndices,
                    commands);
                if (command.shadow is PreparedBatch shadow)
                {
                    DrawStencilUtility(
                        shadow,
                        data.state.stencilWrite,
                        data.state,
                        data.sharedVertices,
                        data.sharedIndices,
                        commands);
                }
            }
            command.light.material.Bind(commands);
            commands.SetStencil(data.supportsStencil
                ? data.state.stencilOutsideTest
                : RenderStencilState.disabled);
            BindGeometry(
                command.light,
                data.sharedVertices,
                data.sharedIndices,
                data.state,
                commands);
        }
    }

    private static void ExecuteMasked(MaskedPassData data, RenderCommandEncoder commands)
    {
        RenderViewport viewport = data.viewport;
        commands.SetViewport(viewport.x, viewport.y, viewport.width, viewport.height);
        ulong preparedMaskSet = 0;
        bool stencilContainsMasks = false;
        foreach (PreparedBatch batch in data.batches)
        {
            RenderStencilState stencil = RenderStencilState.disabled;
            if (batch.maskSet != 0 && batch.maskInteraction != SpriteMaskInteraction2D.None)
            {
                if (!stencilContainsMasks || preparedMaskSet != batch.maskSet)
                {
                    if (stencilContainsMasks)
                        DrawStencilUtility(data.stencilClear, data.state.stencilClear, data, commands);
                    for (int maskIndex = 0; maskIndex < data.masks.Length; maskIndex++)
                    {
                        if ((batch.maskSet & (1UL << maskIndex)) == 0
                            || data.masks[maskIndex] is not PreparedBatch mask)
                        {
                            continue;
                        }
                        DrawStencilUtility(mask, data.state.stencilWrite, data, commands);
                    }
                    preparedMaskSet = batch.maskSet;
                    stencilContainsMasks = true;
                }
                stencil = batch.maskInteraction == SpriteMaskInteraction2D.VisibleInside
                    ? data.state.stencilInsideTest
                    : data.state.stencilOutsideTest;
            }
            batch.material.Bind(commands);
            commands.SetStencil(stencil);
            BindAndDraw(batch, data.sharedVertices, data.sharedIndices, data.state, data.lighting, commands);
        }
    }

    private static void DrawStencilUtility(
        PreparedBatch batch,
        RenderStencilState stencil,
        MaskedPassData data,
        RenderCommandEncoder commands)
    {
        batch.material.Bind(commands);
        commands.SetRasterState(data.state.maskWriteRasterState);
        commands.SetStencil(stencil);
        BindGeometry(batch, data.sharedVertices, data.sharedIndices, data.state, commands);
    }

    private static void DrawStencilUtility(
        PreparedBatch batch,
        RenderStencilState stencil,
        PipelineState state,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices,
        RenderCommandEncoder commands)
    {
        batch.material.Bind(commands);
        commands.SetRasterState(state.maskWriteRasterState);
        commands.SetStencil(stencil);
        BindGeometry(batch, sharedVertices, sharedIndices, state, commands);
    }

    private static void ExecuteComposite(CompositePassData data, RenderCommandEncoder commands)
    {
        RenderViewport viewport = data.viewport;
        commands.SetViewport(viewport.x, viewport.y, viewport.width, viewport.height);
        data.material.Bind(commands);
        commands.SetStencil(RenderStencilState.disabled);
        commands.SetUniform(new RenderBindingId("u_blitGeometry"), data.geometry);
        commands.SetUniform(new RenderBindingId("u_texelSize"), data.texelSize);
        commands.BindTexture(new RenderBindingId("s_source"), data.sceneColor, RenderSamplerState.linearClamp);
        if (data.kind is CompositeKind.Upsample or CompositeKind.Final)
            commands.BindTexture(new RenderBindingId("s_auxiliary"), data.effectTexture.isValid ? data.effectTexture : data.sceneColor, RenderSamplerState.linearClamp);
        if (data.kind is CompositeKind.Prefilter or CompositeKind.Upsample)
            commands.SetUniform(new RenderBindingId("u_bloom"), data.bloom);
        if (data.kind == CompositeKind.Final)
        {
            commands.SetUniform(new RenderBindingId("u_colorAdjustments"), data.colorAdjustments);
            commands.SetUniform(new RenderBindingId("u_composite"), data.options);
        }
        commands.BindVertexBuffer(data.sharedVertices);
        commands.BindIndexBuffer(data.sharedIndices);
        commands.DrawIndexed(6);
    }

    private static void DrawBatch(
        PreparedBatch batch,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices,
        PipelineState state,
        LightingFrameResources lighting,
        RenderCommandEncoder commands)
    {
        batch.material.Bind(commands);
        commands.SetStencil(RenderStencilState.disabled);
        BindAndDraw(batch, sharedVertices, sharedIndices, state, lighting, commands);
    }

    private static void BindAndDraw(
        PreparedBatch batch,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices,
        PipelineState state,
        LightingFrameResources lighting,
        RenderCommandEncoder commands)
    {
        BindLighting(lighting, batch.lightingLayer, batch.lightBlendStyles, state, commands);
        BindGeometry(batch, sharedVertices, sharedIndices, state, commands);
    }

    private static void BindGeometry(
        PreparedBatch batch,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices,
        PipelineState state,
        RenderCommandEncoder commands)
    {
        if (batch.bindingKind == BatchBindingKind.Sprite)
        {
            commands.SetUniform(state.materialEffectBinding, batch.materialEffect);
            commands.BindTexture(state.textureBinding, batch.texture, batch.sampler);
            commands.BindTexture(state.normalTextureBinding, batch.normalMap, batch.sampler);
            commands.BindTexture(state.emissionTextureBinding, batch.emissionMap, batch.sampler);
        }
        else if (batch.bindingKind == BatchBindingKind.Light)
        {
            commands.SetUniform(new RenderBindingId("u_lightParameters"), batch.materialEffect);
            commands.BindTexture(new RenderBindingId("s_cookie"), batch.texture, batch.sampler);
        }
        commands.BindVertexBuffer(sharedVertices);
        commands.BindIndexBuffer(sharedIndices);
        if (batch.persistentInstances.isValid)
            commands.BindInstanceBuffer(batch.persistentInstances, 0, batch.instanceCount);
        else
            commands.BindInstanceBuffer(batch.transientInstances);
        commands.DrawIndexed(6);
    }

    private static void BindLighting(
        LightingFrameResources lighting,
        int layer,
        byte lightBlendStyles,
        PipelineState state,
        RenderCommandEncoder commands)
    {
        if (!lighting.isValid || lightBlendStyles == 0)
            return;
        for (int style = 0; style < 4; style++)
        {
            if ((lightBlendStyles & (1 << style)) == 0)
                continue;
            int slot = Math.Clamp(layer, 0, 31) * 4 + style;
            if (!lighting.colors[slot].isValid)
                continue;
            commands.BindTexture(
                state.lightColorBindings[style],
                lighting.colors[slot],
                RenderSamplerState.linearClamp);
            commands.BindTexture(
                state.lightDirectionBindings[style],
                lighting.directions[slot],
                RenderSamplerState.linearClamp);
        }
        commands.SetUniform(
            state.lightSamplingBinding,
            lighting.flipVerticalUv
                ? state.lightSamplingFlipped
                : state.lightSamplingNormal);
    }

    private static byte[] CreateStencilClearInstanceBytes(InnoEngine.Mathematics.Rect bounds)
    {
        var result = new byte[C_INSTANCE_STRIDE];
        WriteVector2(result, 0, new(bounds.left, bounds.top));
        WriteVector2(result, 8, new(bounds.right, bounds.top));
        WriteVector2(result, 16, new(bounds.left, bounds.bottom));
        WriteVector2(result, 24, new(bounds.right, bounds.bottom));
        return result;
    }

    private static byte[] Pack(float x, float y = 0f, float z = 0f, float w = 0f)
    {
        var result = new byte[16];
        WriteFloat(result, 0, x); WriteFloat(result, 4, y);
        WriteFloat(result, 8, z); WriteFloat(result, 12, w);
        return result;
    }

    private static byte[] CreateMaterialEffect(InnoEngine.Mathematics.Color emission, byte lightBlendStyles)
    {
        var result = new byte[16];
        WriteFloat(result, 0, emission.r);
        WriteFloat(result, 4, emission.g);
        WriteFloat(result, 8, emission.b);
        WriteFloat(result, 12, lightBlendStyles & 0x0f);
        return result;
    }

    private static byte[] CreateLightMaterialEffect(float normalIntensity)
    {
        var result = new byte[16];
        WriteFloat(result, 0, normalIntensity);
        return result;
    }

    private static void WriteFloat(byte[] destination, int offset, float value)
        => BinaryPrimitives.WriteSingleLittleEndian(destination.AsSpan(offset), value);

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

    private sealed class PipelineState
    {
        internal PipelineState()
        {
            vertexLayout = new RenderVertexLayout(
            [
                new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float2)
            ]);
            instanceLayout = new RenderVertexLayout(
            [
                new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate3, RenderVertexFormat.Float4),
                new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate4, RenderVertexFormat.Float4),
                new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate5, RenderVertexFormat.Float4),
                new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate6, RenderVertexFormat.Float4),
                new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate7, RenderVertexFormat.Float4)
            ], C_INSTANCE_STRIDE);
            instanceUpload = new RenderBufferUploadDescriptor(
                instanceLayout.stride,
                RenderBufferUsage.Vertex,
                instanceLayout);
            phase = new RenderPhaseId("inno.rendering.2d.main");
            lightPhase = new RenderPhaseId("inno.rendering.2d.light");
            compositePhase = new RenderPhaseId("inno.rendering.2d.composite");
            textureBinding = new RenderBindingId("s_spriteTexture");
            effectTextureBinding = new RenderBindingId("s_effectTexture");
            normalTextureBinding = new RenderBindingId("s_normalTexture");
            emissionTextureBinding = new RenderBindingId("s_emissionTexture");
            lightColorBindings =
            [
                new RenderBindingId("s_lightColor0"),
                new RenderBindingId("s_lightColor1"),
                new RenderBindingId("s_lightColor2"),
                new RenderBindingId("s_lightColor3")
            ];
            lightDirectionBindings =
            [
                new RenderBindingId("s_lightDirection0"),
                new RenderBindingId("s_lightDirection1"),
                new RenderBindingId("s_lightDirection2"),
                new RenderBindingId("s_lightDirection3")
            ];
            lightSamplingBinding = new RenderBindingId("u_lightSampling");
            materialEffectBinding = new RenderBindingId("u_spriteMaterial");
            lightSamplingNormal = new byte[16];
            lightSamplingFlipped = new byte[16];
            materialEffectNone = new byte[16];
            WriteFloat(lightSamplingFlipped, 0, 1f);
            builtinWhiteTextureId = new RenderPersistentResourceId("inno.rendering.2d.builtin.white");
            builtinNeutralNormalTextureId = new RenderPersistentResourceId("inno.rendering.2d.builtin.neutral-normal");
            sharedQuadVertexId = new RenderPersistentResourceId("inno.rendering.2d.shared-quad.vertices");
            sharedQuadIndexId = new RenderPersistentResourceId("inno.rendering.2d.shared-quad.indices");
            sharedQuadVertexDescriptor = new PersistentBufferDescriptor(
                new RenderBufferDescriptor(4, vertexLayout.stride, RenderBufferUsage.Vertex),
                vertexLayout);
            sharedQuadIndexDescriptor = new PersistentBufferDescriptor(
                new RenderBufferDescriptor(6, sizeof(uint), RenderBufferUsage.Index),
                indexFormat: RenderIndexFormat.UInt32);
            sharedQuadVertices =
            [
                0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 128, 63, 0, 0, 0, 0,
                0, 0, 128, 63, 0, 0, 128, 63,
                0, 0, 0, 0, 0, 0, 128, 63
            ];
            sharedQuadIndices =
            [
                0, 0, 0, 0,
                1, 0, 0, 0,
                2, 0, 0, 0,
                0, 0, 0, 0,
                2, 0, 0, 0,
                3, 0, 0, 0
            ];
            builtinWhiteTextureDescriptor = new RenderTextureDescriptor(
                1,
                1,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.Sampled);
            builtinWhiteTextureData =
            [
                new RenderTextureSubresourceData(0, 0, [255, 255, 255, 255])
            ];
            builtinNeutralNormalTextureData =
            [
                new RenderTextureSubresourceData(0, 0, [128, 128, 255, 255])
            ];
            emptyOverrides = new MaterialPropertyBlock();
            identityMatrix =
            [
                1f, 0f, 0f, 0f,
                0f, 1f, 0f, 0f,
                0f, 0f, 1f, 0f,
                0f, 0f, 0f, 1f
            ];
            maskWriteRasterState = new RenderRasterState(
                RenderCullMode.None,
                RenderFrontFace.CounterClockwise,
                RenderDepthCompare.Always,
                depthWrite: false,
                RenderBlendState.opaque,
                colorWriteMask: 0,
                multisampling: true,
                RenderPrimitiveTopology.TriangleList);
            var replace = new RenderStencilFaceState(
                RenderStencilCompare.Always,
                RenderStencilOperation.Keep,
                RenderStencilOperation.Keep,
                RenderStencilOperation.Replace);
            var inside = new RenderStencilFaceState(
                RenderStencilCompare.NotEqual,
                RenderStencilOperation.Keep,
                RenderStencilOperation.Keep,
                RenderStencilOperation.Keep);
            var outside = new RenderStencilFaceState(
                RenderStencilCompare.Equal,
                RenderStencilOperation.Keep,
                RenderStencilOperation.Keep,
                RenderStencilOperation.Keep);
            stencilClear = new RenderStencilState(
                enabled: true,
                reference: 0,
                byte.MaxValue,
                byte.MaxValue,
                replace,
                replace);
            stencilWrite = new RenderStencilState(
                enabled: true,
                reference: 1,
                byte.MaxValue,
                byte.MaxValue,
                replace,
                replace);
            stencilInsideTest = new RenderStencilState(
                enabled: true,
                reference: 0,
                byte.MaxValue,
                byte.MaxValue,
                inside,
                inside);
            stencilOutsideTest = new RenderStencilState(
                enabled: true,
                reference: 0,
                byte.MaxValue,
                byte.MaxValue,
                outside,
                outside);
        }

        internal RenderVertexLayout vertexLayout { get; }
        internal RenderVertexLayout instanceLayout { get; }
        internal RenderBufferUploadDescriptor instanceUpload { get; }
        internal RenderPhaseId phase { get; }
        internal RenderPhaseId lightPhase { get; }
        internal RenderPhaseId compositePhase { get; }
        internal RenderBindingId textureBinding { get; }
        internal RenderBindingId effectTextureBinding { get; }
        internal RenderBindingId normalTextureBinding { get; }
        internal RenderBindingId emissionTextureBinding { get; }
        internal RenderBindingId[] lightColorBindings { get; }
        internal RenderBindingId[] lightDirectionBindings { get; }
        internal RenderBindingId lightSamplingBinding { get; }
        internal RenderBindingId materialEffectBinding { get; }
        internal byte[] lightSamplingNormal { get; }
        internal byte[] lightSamplingFlipped { get; }
        internal byte[] materialEffectNone { get; }
        internal RenderPersistentResourceId builtinWhiteTextureId { get; }
        internal RenderPersistentResourceId builtinNeutralNormalTextureId { get; }
        internal RenderPersistentResourceId sharedQuadVertexId { get; }
        internal RenderPersistentResourceId sharedQuadIndexId { get; }
        internal PersistentBufferDescriptor sharedQuadVertexDescriptor { get; }
        internal PersistentBufferDescriptor sharedQuadIndexDescriptor { get; }
        internal byte[] sharedQuadVertices { get; }
        internal byte[] sharedQuadIndices { get; }
        internal RenderTextureDescriptor builtinWhiteTextureDescriptor { get; }
        internal RenderTextureSubresourceData[] builtinWhiteTextureData { get; }
        internal RenderTextureSubresourceData[] builtinNeutralNormalTextureData { get; }
        internal MaterialPropertyBlock emptyOverrides { get; }
        internal float[] identityMatrix { get; }
        internal RenderRasterState maskWriteRasterState { get; }
        internal RenderStencilState stencilClear { get; }
        internal RenderStencilState stencilWrite { get; }
        internal RenderStencilState stencilInsideTest { get; }
        internal RenderStencilState stencilOutsideTest { get; }
    }

    private sealed record PassData(
        PipelineState state,
        PreparedBatch[] batches,
        LightingFrameResources lighting,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices,
        RenderViewport viewport);

    private sealed record MaskedPassData(
        PipelineState state,
        PreparedBatch[] batches,
        PreparedBatch?[] masks,
        PreparedBatch stencilClear,
        LightingFrameResources lighting,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices,
        RenderViewport viewport);

    private sealed record CompositePassData(
        PipelineState state,
        RenderMaterialPass material,
        RenderTextureHandle sceneColor,
        RenderTextureHandle effectTexture,
        CompositeKind kind,
        byte[] geometry,
        byte[] texelSize,
        byte[] bloom,
        byte[] colorAdjustments,
        byte[] options,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices,
        RenderViewport viewport);

    private sealed record LightPassData(
        PipelineState state,
        LightDrawCommand[] commands,
        PreparedBatch stencilClear,
        bool supportsStencil,
        PersistentBufferHandle sharedVertices,
        PersistentBufferHandle sharedIndices,
        RenderViewport viewport);

    private sealed record LightDrawCommand(PreparedBatch light, PreparedBatch? shadow);

    private readonly record struct LightingFrameResources(
        RenderTextureHandle[] colors,
        RenderTextureHandle[] directions,
        bool flipVerticalUv,
        bool usesHdr,
        bool supportsStencil,
        bool supportsMrt,
        int lightDrawCount,
        int shadowDrawCount)
    {
        internal bool isValid => colors is { Length: 128 } && directions is { Length: 128 };
    }

    private sealed record PreparedBatch(
        RenderMaterialPass material,
        PersistentTextureHandle texture,
        PersistentTextureHandle normalMap,
        PersistentTextureHandle emissionMap,
        RenderSamplerState sampler,
        RenderBufferSlice transientInstances,
        PersistentBufferHandle persistentInstances,
        int instanceCount,
        ulong maskSet,
        SpriteMaskInteraction2D maskInteraction,
        byte[] materialEffect,
        int lightingLayer,
        byte lightBlendStyles,
        BatchBindingKind bindingKind = BatchBindingKind.Sprite);

    private enum CompositeKind { Prefilter, Downsample, Upsample, Final }

    private enum BatchBindingKind { Sprite, Light, Stencil }
}
