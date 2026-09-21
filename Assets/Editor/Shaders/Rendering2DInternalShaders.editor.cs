using System;
using System.Collections.Generic;
using System.Linq;
using InnoEngine.Graphs;
using InnoEngine.Rendering;
using InnoEngine.Serialization;
using InnoEditor.Rendering.Shaders;
using InnoEditor.Rendering.Assets;

namespace Inno.Rendering2D;

/// <summary>Creates Pipeline-owned programs through the same graph and source-function compiler used by user shaders.</summary>
public static class Rendering2DInternalShaders
{
    /// <summary>Creates one independent fullscreen effect graph, without a Sprite material or instance interface.</summary>
    /// <param name="effect">BloomPrefilter, BloomDownsample, BloomUpsample or FinalComposite.</param>
    /// <param name="vertex">The fullscreen vertex function asset.</param>
    /// <param name="fragment">The matching effect function asset.</param>
    /// <param name="serialization">Current authoring converters.</param>
    /// <param name="references">Complete authoring reference context.</param>
    /// <returns>A detached graph ready to save as a native Shader source.</returns>
    public static GraphDocument CreateFullscreen(string effect, ShaderFunctionAsset vertex, ShaderFunctionAsset fragment,
        SerializationRegistry serialization, SerializationContext references)
    {
        if (effect is not ("BloomPrefilter" or "BloomDownsample" or "BloomUpsample" or "FinalComposite"))
            throw new ArgumentException("Unknown fullscreen effect.", nameof(effect));
        var builder = new Builder(effect, Rendering2DIds.postProcessContract, serialization, references);
        builder.Stage(ShaderStage.Vertex, vertex,
            [Input("a_position", "float2", ShaderIrInputKind.VertexAttribute, "position"), Uniform("u_blitGeometry")],
            [("position", "position", ShaderIrOutputKind.ClipPosition, "", 0), ("uv", "uv", ShaderIrOutputKind.Varying, "texcoord", 0)]);
        var inputs = new List<ShaderGraphInputSettings> { Varying("uv", "float2", 0), Texture("s_source", 0), Uniform("u_texelSize") };
        if (effect is "BloomUpsample" or "FinalComposite") inputs.Add(Texture("s_auxiliary", 1));
        if (effect is "BloomPrefilter" or "BloomUpsample") inputs.Add(Uniform("u_bloom"));
        if (effect == "FinalComposite") { inputs.Add(Uniform("u_colorAdjustments")); inputs.Add(Uniform("u_composite")); }
        builder.Stage(ShaderStage.Fragment, fragment, inputs.ToArray(), [("return", "color", ShaderIrOutputKind.Color, "", 0)]);
        return builder.Finish(effect == "FinalComposite"
            ? [("Opaque", Rendering2DIds.opaqueRole, RenderBlendState.opaque), ("Premultiplied", Rendering2DIds.premultipliedRole, RenderBlendState.premultiplied)]
            : [("Opaque", Rendering2DIds.opaqueRole, RenderBlendState.opaque)]);
    }

    /// <summary>Creates the independent MRT light accumulation graph with explicit instance data and cookie bindings.</summary>
    /// <param name="vertex">Light vertex function.</param>
    /// <param name="fragment">Light accumulation function.</param>
    /// <param name="serialization">Current converters.</param>
    /// <param name="references">Complete owner reference context.</param>
    /// <returns>A detached graph implementing the Pipeline light contract.</returns>
    public static GraphDocument CreateLighting(ShaderFunctionAsset vertex, ShaderFunctionAsset fragment,
        SerializationRegistry serialization, SerializationContext references)
    {
        var builder = new Builder("Light Accumulation", Rendering2DIds.lightContract, serialization, references);
        builder.Stage(ShaderStage.Vertex, vertex, GeometryInputs(5),
            [("position", "position", ShaderIrOutputKind.ClipPosition, "", 0),
             ("uv", "uv", ShaderIrOutputKind.Varying, "texcoord", 0),
             ("worldPosition", "worldPosition", ShaderIrOutputKind.Varying, "texcoord", 1),
             ("originDirection", "originDirection", ShaderIrOutputKind.Varying, "texcoord", 2),
             ("kindRangeFalloff", "kindRangeFalloff", ShaderIrOutputKind.Varying, "texcoord", 3),
             ("radianceCone", "radianceCone", ShaderIrOutputKind.Varying, "texcoord", 4)]);
        builder.Stage(ShaderStage.Fragment, fragment,
            [Varying("uv", "float2", 0), Varying("worldPosition", "float2", 1), Varying("originDirection", "float4", 2),
             Varying("kindRangeFalloff", "float4", 3), Varying("radianceCone", "float4", 4), Texture("s_cookie", 0), Uniform("u_lightParameters")],
            [("color0", "color0", ShaderIrOutputKind.Color, "", 0), ("color1", "color1", ShaderIrOutputKind.Color, "", 1)]);
        return builder.Finish([("Additive", Rendering2DIds.additiveRole, RenderBlendState.additive)]);
    }

    /// <summary>Creates the independent stencil utility graph for shadow volumes and stencil clears.</summary>
    /// <param name="vertex">Independent-corner vertex function.</param>
    /// <param name="fragment">Stencil fragment function.</param>
    /// <param name="serialization">Current converters.</param>
    /// <param name="references">Complete owner reference context.</param>
    /// <returns>A detached graph implementing the Pipeline stencil contract.</returns>
    public static GraphDocument CreateShadow(ShaderFunctionAsset vertex, ShaderFunctionAsset fragment,
        SerializationRegistry serialization, SerializationContext references)
    {
        var builder = new Builder("Shadow Stencil", Rendering2DIds.shadowContract, serialization, references);
        builder.Stage(ShaderStage.Vertex, vertex, GeometryInputs(2), [("return", "position", ShaderIrOutputKind.ClipPosition, "", 0)]);
        builder.Stage(ShaderStage.Fragment, fragment, [], [("return", "color", ShaderIrOutputKind.Color, "", 0)]);
        return builder.Finish([("Opaque", Rendering2DIds.opaqueRole, RenderBlendState.opaque)]);
    }

    internal static ShaderGraphInputSettings[] GeometryInputs(int count)
        => new[] { Input("a_position", "float2", ShaderIrInputKind.VertexAttribute, "position") }
            .Concat(Enumerable.Range(0, count).Select(index => Input("i_data" + index, "float4", ShaderIrInputKind.VertexAttribute, "instance-data", index)))
            .Append(Input("viewProjection", "float4x4", ShaderIrInputKind.Builtin, "view-projection")).ToArray();
    internal static ShaderGraphInputSettings Input(string id, string type, ShaderIrInputKind kind, string semantic = "", int location = 0)
        => new() { id = id, type = new() { id = type }, kind = kind, semantic = semantic, location = location };
    internal static ShaderGraphInputSettings Uniform(string id) => Input(id, "float4", ShaderIrInputKind.Uniform);
    internal static ShaderGraphInputSettings Texture(string id, int slot) => Input(id, "sampled-texture2d", ShaderIrInputKind.SampledTexture, location: slot);
    internal static ShaderGraphInputSettings Varying(string id, string type, int slot) => Input(id, type, ShaderIrInputKind.Varying, "texcoord", slot);

    internal sealed class Builder(string name, ShaderContractId contract, SerializationRegistry serialization, SerializationContext references)
    {
        private readonly GraphDocument m_graph = ShaderGraphDocument.Create(new ShaderDefinition(name, [], [], []), serialization, references);
        private readonly List<ShaderPropertyDefinition> m_properties = [];
        private readonly List<GraphNodeId> m_stages = [];
        internal GraphDocument graph => m_graph;

        internal GraphNodeRecord Stage(ShaderStage stage, ShaderFunctionAsset function, ShaderGraphInputSettings[] inputs,
            (string source, string id, ShaderIrOutputKind kind, string semantic, int location)[] outputs)
        {
            if (function.isMissing || function.identity.persistentId == Guid.Empty) throw new ArgumentException("Choose an imported source function.", nameof(function));
            string prefix = stage.ToString();
            GraphNodeRecord output = Node(prefix, ShaderGraphDocument.outputDefinitionId);
            Set(output, "settings", new ShaderGraphStageSettings { stage = stage, outputs = outputs.Select(value => new ShaderGraphOutput
                { id = value.id, kind = value.kind, semantic = value.semantic, location = value.location }).ToArray() });
            m_stages.Add(output.id);
            GraphNodeRecord call = Call(stage, prefix + ".function", function);
            foreach (var input in inputs)
                Connect(GetInput(stage, input), "value", call, "input." + input.id);
            foreach (var port in outputs) Connect(call, port.source == "return" ? "return" : "output." + port.source, output, port.id);
            return call;
        }

        internal GraphNodeRecord Call(ShaderStage stage, string id, ShaderFunctionAsset function)
        {
            GraphNodeRecord call = Node(id, "inno.shader.source");
            Set(call, "stage", stage.ToString());
            Set(call, "sourceId", function.identity.persistentId);
            Set(call, "sourcePath", function.assetPath.ToString());
            Set(call, "function", function.exports.Single());
            return call;
        }

        internal GraphNodeRecord GetInput(ShaderStage stage, ShaderGraphInputSettings input)
        {
            string id = stage + "." + input.id;
            if (m_graph.FindNode(new(id)) is { } existing) return existing;
            GraphNodeRecord node = Node(id, "inno.shader.stage-input");
            Set(node, "stage", stage.ToString()); Set(node, "settings", input);
            if (input.kind is ShaderIrInputKind.Uniform or ShaderIrInputKind.SampledTexture)
                m_properties.Add(new(new(input.id), input.id, input.kind == ShaderIrInputKind.Uniform ? ShaderPropertyType.Vector4 : ShaderPropertyType.Texture2D,
                    stage, default, bindingOwner: ShaderPropertyBindingOwner.RenderPass));
            return node;
        }

        internal GraphDocument Finish((string name, ShaderPassRoleId role, RenderBlendState blend)[] passes, ShaderDefinition? authored = null)
        {
            ShaderPropertyDefinition[] properties = MergeProperties(m_properties, authored?.properties ?? []);
            var definition = new ShaderDefinition(name, properties, authored?.keywords ?? [], passes.Select(pass => new ShaderPassDefinition(pass.name, ShaderProgramKind.Raster,
                renderState: new() { cull = ShaderCullMode.None, depthCompare = ShaderCompareFunction.Always, depthWrite = false, blend = pass.blend, colorWriteMask = 15 })),
                techniques: [new(new("default"), contract, passes.Select(pass => new ShaderTechniquePass(pass.role, pass.name)))]);
            m_graph.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(serialization.Serialize(definition, references), serialization, references));
            Layout();
            GraphDocument result = m_graph;
            foreach (var pass in passes) result = ShaderGraphPrograms.Bind(result, pass.name, m_stages, serialization, references);
            return result;
        }

        private static ShaderPropertyDefinition[] MergeProperties(
            IEnumerable<ShaderPropertyDefinition> generated,
            IEnumerable<ShaderPropertyDefinition> authored)
        {
            var properties = new List<ShaderPropertyDefinition>();
            foreach (ShaderPropertyDefinition generatedProperty in generated)
            {
                if (properties.Any(value => value.id == generatedProperty.id))
                    throw new InvalidOperationException(
                        $"Shader property '{generatedProperty.id.value}' was generated more than once.");
                properties.Add(generatedProperty);
            }

            foreach (ShaderPropertyDefinition authoredPropertyValue in authored)
            {
                ShaderPropertyDefinition authoredProperty = authoredPropertyValue;
                int index = properties.FindIndex(value => value.id == authoredProperty.id);
                if (index < 0)
                {
                    properties.Add(authoredProperty);
                    continue;
                }

                ShaderPropertyDefinition generatedProperty = properties[index];
                bool equivalentType = generatedProperty.type == authoredProperty.type
                    || generatedProperty.type == ShaderPropertyType.Vector4 && authoredProperty.type == ShaderPropertyType.Color
                    || generatedProperty.type == ShaderPropertyType.Color && authoredProperty.type == ShaderPropertyType.Vector4;
                if (!equivalentType
                    || generatedProperty.bindingKind != authoredProperty.bindingKind
                    || generatedProperty.storageAccess != authoredProperty.storageAccess)
                    throw new InvalidOperationException(
                        $"Shader property '{authoredProperty.id.value}' has conflicting generated and authored declarations.");

                // The authored declaration owns user-facing metadata, defaults and binding scope.
                // Target-generated stage inputs contribute only the stages that consume that property.
                authoredProperty.stages |= generatedProperty.stages;
                properties[index] = authoredProperty;
            }
            return properties.ToArray();
        }

        private void Layout()
        {
            float lane = 60f;
            foreach (GraphNodeRecord output in m_graph.nodes.Where(node => node.definitionId == ShaderGraphDocument.outputDefinitionId)
                         .OrderBy(node => ShaderGraphDocument.Read(node, "settings", new ShaderGraphStageSettings(), serialization, references).stage))
            {
                GraphNodeRecord[] nodes = m_graph.nodes.Where(node => node.id == output.id
                    || ShaderGraphDocument.Read(node, "stage", "", serialization, references) == output.id.value).ToArray();
                GraphNodeRecord[] inputs = nodes.Where(node => node.definitionId == "inno.shader.stage-input").ToArray();
                GraphNodeRecord[] functions = nodes.Where(node => node.definitionId == "inno.shader.source").ToArray();
                for (int i = 0; i < inputs.Length; i++) inputs[i].position = new(40f, lane + i * 180f);
                for (int i = 0; i < functions.Length; i++) functions[i].position = new(430f, lane + i * 220f);
                output.position = new(800f, lane);
                lane += Math.Max(1, Math.Max(inputs.Length, functions.Length)) * 220f + 120f;
            }
        }

        internal GraphNodeRecord Node(string id, string type)
        {
            var node = new GraphNodeRecord(new(id), type) { position = new((m_graph.nodes.Count % 4) * 300, (m_graph.nodes.Count / 4) * 220) };
            m_graph.AddNode(node); return node;
        }
        internal void Set<T>(GraphNodeRecord node, string key, T value) => node.SetValue(key, ShaderGraphDocument.Encode(value, serialization, references));
        internal void Connect(GraphNodeRecord from, string output, GraphNodeRecord to, string input)
            => m_graph.AddEdge(new(new(from.id.value + "." + output + "->" + to.id.value + "." + input), new(from.id, new(output)), new(to.id, new(input))));
    }
}
