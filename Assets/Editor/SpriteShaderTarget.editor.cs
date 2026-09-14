using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InnoEngine.Assets;
using InnoEngine.Graphs;
using InnoEngine.Mathematics;
using InnoEngine.Rendering;
using InnoEngine.Serialization;
using InnoEditor.Rendering.Assets;
using InnoEditor.Rendering.Shaders;
using static Inno.Rendering2D.Rendering2DInternalShaders;

namespace Inno.Rendering2D;

/// <summary>Expands authored Sprite surfaces into the shared typed compilation chain entirely inside the Plugin.</summary>
public sealed class SpriteShaderTarget : ShaderTarget
{
    /// <summary>Identifies the Plugin-owned Sprite surface authoring target.</summary>
    public const string targetId = "inno.rendering.2d.sprite-surface";
    /// <summary>Identifies the artist-facing Sprite surface output node.</summary>
    public const string surfaceNode = "inno.rendering.2d.sprite-surface-output";
    /// <summary>Identifies the per-instance Sprite texture convenience node.</summary>
    public const string textureNode = "inno.rendering.2d.sprite-texture";
    /// <inheritdoc />
    public override string id => targetId;

    /// <inheritdoc />
    public override GraphDocument Expand(ShaderTargetContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GraphDocument authored = context.document;
        GraphNodeRecord[] outputs = authored.nodes.Where(node => node.definitionId == surfaceNode).ToArray();
        if (outputs.Length != 1) throw new InvalidOperationException("A Sprite Shader requires exactly one Sprite Surface Output.");
        GraphNodeRecord output = outputs[0];
        ShaderFunctionAsset vertex = Source("vertexSource");
        ShaderFunctionAsset surface = Source("surfaceSource");
        ShaderFunctionAsset sample = Source("sampleSource");
        ShaderDefinition declaration = ShaderGraphDocument.ReadDefinition(authored, context.serialization, context.references);
        var builder = new Builder(declaration.name, Rendering2DIds.spriteContract, context.serialization, context.references);
        GraphNodeRecord vertexCall = builder.Stage(ShaderStage.Vertex, vertex, GeometryInputs(5),
            [("position", "position", ShaderIrOutputKind.ClipPosition, "", 0),
             ("v_texcoord0", "v_texcoord0", ShaderIrOutputKind.Varying, "texcoord", 0),
             ("v_color0", "v_color0", ShaderIrOutputKind.Varying, "color", 0),
             ("v_shape", "v_shape", ShaderIrOutputKind.Varying, "texcoord", 1),
             ("v_effectStep", "v_effectStep", ShaderIrOutputKind.Varying, "texcoord", 2)]);
        GraphEdgeRecord[] vertexConnections = authored.edges.Where(edge => edge.input.nodeId == output.id && edge.input.portId.value == "vertexOffset").ToArray();
        if (vertexConnections.Length > 1) throw new InvalidOperationException("The Sprite vertex offset is connected more than once.");
        var vertexNodes = new Dictionary<GraphNodeId, GraphNodeRecord>();
        if (vertexConnections.Length == 1)
        {
            GraphEndpoint source = vertexConnections[0].output;
            builder.Connect(VertexNode(source.nodeId), source.portId.value, vertexCall, "input.vertexOffset");
        }
        else
        {
            GraphSerializedValue literal = output.TryGetValue(ShaderGraphDocument.inputDefaultPrefix + "vertexOffset", out var stored)
                ? stored! : ShaderGraphDocument.Encode(ShaderGraphLiteral.Zero(ShaderSourceType.Atomic("float3")), context.serialization, context.references);
            vertexCall.SetValue(ShaderGraphDocument.inputDefaultPrefix + "input.vertexOffset", literal);
        }
        var surfaceInputs = new List<ShaderGraphInputSettings>
        {
            Varying("v_texcoord0", "float2", 0), Input("v_color0", "float4", ShaderIrInputKind.Varying, "color"),
            Varying("v_shape", "float", 1), Varying("v_effectStep", "float2", 2),
            Uniform("u_lightSampling"), Uniform("u_spriteMaterial"),
            Input("fragmentCoordinate", "float4", ShaderIrInputKind.Builtin, "fragment-coordinate"),
            Input("viewRectangle", "float4", ShaderIrInputKind.Builtin, "view-rectangle"),
            Input("viewTexel", "float4", ShaderIrInputKind.Builtin, "view-texel")
        };
        for (int index = 0; index < 4; index++) surfaceInputs.Add(Texture("s_lightColor" + index, 3 + index));
        for (int index = 0; index < 4; index++) surfaceInputs.Add(Texture("s_lightDirection" + index, 7 + index));
        GraphNodeRecord surfaceCall = builder.Stage(ShaderStage.Fragment, surface, surfaceInputs.ToArray(), [("color0", "color0", ShaderIrOutputKind.Color, "", 0)]);
        GraphNodeRecord defaultSample = Sample(authored.nodes.FirstOrDefault(node => node.definitionId == textureNode)?.id.value ?? "Sprite.DefaultSample");
        var samples = new HashSet<GraphNodeId>();
        foreach (GraphNodeRecord node in authored.nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.id == output.id) continue;
            if (node.definitionId == textureNode)
            {
                if (node.id != defaultSample.id) Sample(node.id.value);
                samples.Add(node.id); continue;
            }
            if (node.definitionId == ShaderGraphDocument.outputDefinitionId)
                throw new InvalidOperationException("Use explicit-stage authoring for additional programs; a Sprite surface cannot contain a second stage output.");
            var copy = builder.Node(node.id.value, node.definitionId);
            foreach (var value in node.values) copy.SetValue(value.Key, value.Value);
            builder.Set(copy, "stage", ShaderStage.Fragment.ToString());
        }
        var connected = new HashSet<string>(StringComparer.Ordinal);
        foreach (GraphEdgeRecord edge in authored.edges)
        {
            if (edge.input.nodeId == output.id && edge.input.portId.value == "vertexOffset") continue;
            GraphEndpoint from = samples.Contains(edge.output.nodeId)
                ? new(edge.output.nodeId, new("output." + edge.output.portId.value)) : edge.output;
            GraphEndpoint to = edge.input;
            if (to.nodeId == output.id)
            {
                string port = to.portId.value;
                string parameter = Parameter(port);
                if (!connected.Add(port)) throw new InvalidOperationException("The Sprite surface input is connected more than once: " + port);
                to = new(surfaceCall.id, new("input." + parameter));
            }
            builder.graph.AddEdge(new(edge.id, from, to));
        }
        foreach (string port in new[] { "color", "normal", "emission" })
        {
            if (connected.Contains(port)) continue;
            string parameter = Parameter(port);
            if (output.TryGetValue(ShaderGraphDocument.inputDefaultPrefix + port, out var literal))
                surfaceCall.SetValue(ShaderGraphDocument.inputDefaultPrefix + "input." + parameter, literal!);
            else builder.Connect(defaultSample, "output." + port, surfaceCall, "input." + parameter);
        }
        return builder.Finish([
            ("Alpha", Rendering2DIds.alphaRole, RenderBlendState.alpha),
            ("Premultiplied", Rendering2DIds.premultipliedRole, RenderBlendState.premultiplied),
            ("Additive", Rendering2DIds.additiveRole, RenderBlendState.additive),
            ("Multiply", Rendering2DIds.multiplyRole, new RenderBlendState { enabled = true,
                colorSource = RenderBlendFactor.DestinationColor, colorDestination = RenderBlendFactor.Zero,
                alphaSource = RenderBlendFactor.Zero, alphaDestination = RenderBlendFactor.One }),
            ("Opaque", Rendering2DIds.opaqueRole, RenderBlendState.opaque)], declaration);

        ShaderFunctionAsset Source(string key)
        {
            var value = ShaderGraphDocument.Read<ShaderFunctionAsset?>(output, key, null, context.serialization, context.references);
            return value is { isMissing: false } ? value : throw new InvalidOperationException("The Sprite Target function is unavailable: " + key);
        }
        GraphNodeRecord VertexNode(GraphNodeId id)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (vertexNodes.TryGetValue(id, out GraphNodeRecord? existing)) return existing;
            GraphNodeRecord original = authored.FindNode(id) ?? throw new InvalidOperationException("The vertex offset references a missing node: " + id.value);
            if (original.definitionId is surfaceNode or textureNode || original.definitionId == ShaderGraphDocument.outputDefinitionId)
                throw new InvalidOperationException("A vertex offset cannot consume fragment-only Sprite sampling or a surface output. Use explicit vertex-stage inputs and functions.");
            GraphNodeRecord copy = builder.Node("Sprite.Vertex/" + original.id.value, original.definitionId);
            vertexNodes.Add(id, copy);
            foreach (var value in original.values) copy.SetValue(value.Key, value.Value);
            builder.Set(copy, "stage", ShaderStage.Vertex.ToString());
            if (original.definitionId == "inno.shader.stage-input")
            {
                ShaderGraphInputSettings input = ShaderGraphDocument.Read(original, "settings", new ShaderGraphInputSettings(), context.serialization, context.references);
                for (int index = 0; index < declaration.properties.Length; index++)
                    if (declaration.properties[index].id.value == input.id)
                    {
                        ShaderPropertyDefinition property = declaration.properties[index];
                        property.stages |= ShaderStage.Vertex;
                        declaration.properties[index] = property;
                    }
            }
            foreach (GraphEdgeRecord edge in authored.edges.Where(edge => edge.input.nodeId == id))
                builder.Connect(VertexNode(edge.output.nodeId), edge.output.portId.value, copy, edge.input.portId.value);
            return copy;
        }
        GraphNodeRecord Sample(string name)
        {
            GraphNodeRecord call = builder.Call(ShaderStage.Fragment, name, sample);
            builder.Connect(builder.GetInput(ShaderStage.Fragment, Varying("v_texcoord0", "float2", 0)), "value", call, "input.uv");
            builder.Connect(builder.GetInput(ShaderStage.Fragment, Varying("v_shape", "float", 1)), "value", call, "input.primitive");
            int slot = 0;
            foreach (string texture in new[] { "s_spriteTexture", "s_normalTexture", "s_emissionTexture" })
                builder.Connect(builder.GetInput(ShaderStage.Fragment, Texture(texture, slot++)), "value", call, "input." + texture);
            return call;
        }
    }

    private static string Parameter(string port) => port switch
    { "color" => "surfaceColor", "normal" => "surfaceNormal", "emission" => "surfaceEmission", _ => throw new InvalidOperationException("Unknown Sprite surface input: " + port) };
}

/// <summary>Describes the high-level surface interface independently of its Target expansion.</summary>
public sealed class SpriteSurfaceNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => SpriteShaderTarget.surfaceNode;
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
        => [new("color", ShaderSourceType.Atomic("float4"), GraphPortDirection.Input, false),
            new("normal", ShaderSourceType.Atomic("float3"), GraphPortDirection.Input, false),
            new("emission", ShaderSourceType.Atomic("float3"), GraphPortDirection.Input, false),
            new("vertexOffset", ShaderSourceType.Atomic("float3"), GraphPortDirection.Input, false)];
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => throw new InvalidOperationException("Sprite Surface Output requires the Sprite Target.");
}

/// <summary>Describes the convenience sample node; its implementation is supplied by the Sprite Target.</summary>
public sealed class SpriteTextureNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => SpriteShaderTarget.textureNode;
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
        => [new("color", ShaderSourceType.Atomic("float4"), GraphPortDirection.Output),
            new("normal", ShaderSourceType.Atomic("float3"), GraphPortDirection.Output),
            new("emission", ShaderSourceType.Atomic("float3"), GraphPortDirection.Output)];
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => throw new InvalidOperationException("Sprite Texture requires the Sprite Target.");
}

/// <summary>Contributes a four-node Sprite template to the unified File Browser create menu.</summary>
public sealed class SpriteShaderTemplate : ShaderGraphTemplate
{
    /// <inheritdoc />
    public override string id => "inno.rendering.2d.sprite-template";
    /// <inheritdoc />
    public override string displayName => "2D / Sprite Surface";
    /// <inheritdoc />
    public override GraphDocument Create(SerializationRegistry serialization, SerializationContext context)
        => Create(Assets.Load<ShaderFunctionAsset>(Assets.LocalPath("Shaders/SpriteVertex.ishadersource")),
            Assets.Load<ShaderFunctionAsset>(Assets.LocalPath("Shaders/SpriteSurface.ishadersource")),
            Assets.Load<ShaderFunctionAsset>(Assets.LocalPath("Shaders/SpriteSample.ishadersource")), serialization, context);

    /// <summary>Creates the same template with explicitly resolved function assets for tooling and alternate libraries.</summary>
    /// <param name="vertex">Default instance vertex function.</param>
    /// <param name="surface">Surface composition function.</param>
    /// <param name="sample">Sprite sample function.</param>
    /// <param name="serialization">Current authoring converters.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <returns>A detached four-node authoring graph.</returns>
    public static GraphDocument Create(ShaderFunctionAsset vertex, ShaderFunctionAsset surface, ShaderFunctionAsset sample,
        SerializationRegistry serialization, SerializationContext context)
    {
        var definition = new ShaderDefinition("Sprite Surface", [new(new("tint"), "Tint", ShaderPropertyType.Color,
            ShaderStage.Fragment, MaterialValue.FromColor(new Color(1f, 1f, 1f, 1f)))], [], []);
        GraphDocument graph = ShaderGraphDocument.Create(definition, serialization, context);
        ShaderGraphDocument.SetTarget(graph, SpriteShaderTarget.targetId, serialization, context);
        GraphNodeRecord output = Node("surface", SpriteShaderTarget.surfaceNode, 650, 110);
        Set(output, "vertexSource", vertex); Set(output, "surfaceSource", surface); Set(output, "sampleSource", sample);
        GraphNodeRecord texture = Node("sprite-texture", SpriteShaderTarget.textureNode, 30, 40);
        GraphNodeRecord tint = Node("tint", "inno.shader.stage-input", 30, 320);
        Set(tint, "settings", new ShaderGraphInputSettings { id = "tint", type = new() { id = "float4" }, kind = ShaderIrInputKind.Uniform, semantic = "" });
        GraphNodeRecord multiply = Node("tint-multiply", "inno.shader.binary", 350, 100);
        Set(multiply, "type", "float4"); Set(multiply, "operation", "multiply");
        Connect(texture, "color", multiply, "left"); Connect(tint, "value", multiply, "right"); Connect(multiply, "value", output, "color");
        return graph;
        GraphNodeRecord Node(string id, string type, float x, float y)
        {
            var node = new GraphNodeRecord(new(id), type) { position = new(x, y) };
            graph.AddNode(node); Set(node, "stage", "surface"); return node;
        }
        void Set<T>(GraphNodeRecord node, string key, T value) => node.SetValue(key, ShaderGraphDocument.Encode(value, serialization, context));
        void Connect(GraphNodeRecord from, string fromPort, GraphNodeRecord to, string toPort)
            => graph.AddEdge(new(new(from.id.value + "." + fromPort + "->" + to.id.value + "." + toPort), new(from.id, new(fromPort)), new(to.id, new(toPort))));
    }
}
