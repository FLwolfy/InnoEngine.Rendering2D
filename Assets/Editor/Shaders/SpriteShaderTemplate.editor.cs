using System;
using System.Linq;
using InnoEngine.Assets;
using InnoEngine.Graphs;
using InnoEngine.Mathematics;
using InnoEngine.Rendering;
using InnoEngine.Serialization;
using InnoEditor.Rendering.Assets;
using InnoEditor.Rendering.Shaders;

namespace Inno.Rendering2D;

/// <summary>Creates a complete explicit-stage Sprite Shader from graph-authored reusable nodes.</summary>
[ShaderGraphTemplate(Rendering2DIds.spriteShaderTemplate, "2D / Sprite")]
public sealed class SpriteShaderTemplate : ShaderGraphTemplate
{
    private const string vertexNodePath = "Shaders/Sprite/Nodes/SpriteVertex.ishader";
    private const string textureNodePath = "Shaders/Sprite/Nodes/SpriteTexture.ishader";
    private const string surfaceNodePath = "Shaders/Sprite/Nodes/SpriteSurface.ishader";

    /// <inheritdoc />
    public override GraphDocument Create(SerializationRegistry serialization, SerializationContext context)
        => Create(
            Assets.Load<ShaderAsset>(Assets.LocalPath(vertexNodePath)),
            Assets.Load<ShaderAsset>(Assets.LocalPath(textureNodePath)),
            Assets.Load<ShaderAsset>(Assets.LocalPath(surfaceNodePath)),
            serialization,
            context);

    /// <summary>Creates the Sprite template with explicitly resolved graph-node assets.</summary>
    public static GraphDocument Create(
        ShaderAsset vertexNode,
        ShaderAsset textureNode,
        ShaderAsset surfaceNode,
        SerializationRegistry serialization,
        SerializationContext context)
    {
        Validate(vertexNode, nameof(vertexNode));
        Validate(textureNode, nameof(textureNode));
        Validate(surfaceNode, nameof(surfaceNode));

        (string name, ShaderPassRoleId role, RenderBlendState blend)[] passes =
        [
            ("Alpha", Rendering2DIds.alphaRole, RenderBlendState.alpha),
            ("Premultiplied", Rendering2DIds.premultipliedRole, RenderBlendState.premultiplied),
            ("Additive", Rendering2DIds.additiveRole, RenderBlendState.additive),
            ("Multiply", Rendering2DIds.multiplyRole, new RenderBlendState
            {
                enabled = true,
                colorSource = RenderBlendFactor.DestinationColor,
                colorDestination = RenderBlendFactor.Zero,
                alphaSource = RenderBlendFactor.Zero,
                alphaDestination = RenderBlendFactor.One
            }),
            ("Opaque", Rendering2DIds.opaqueRole, RenderBlendState.opaque)
        ];
        var definition = new ShaderDefinition(
            "Sprite",
            [new(new("tint"), "Tint", ShaderPropertyType.Color, ShaderStage.Fragment,
                MaterialValue.FromColor(new Color(1f, 1f, 1f, 1f)))],
            [],
            passes.Select(static pass => new ShaderPassDefinition(
                pass.name,
                ShaderProgramKind.Raster,
                renderState: new()
                {
                    cull = ShaderCullMode.None,
                    depthCompare = ShaderCompareFunction.Always,
                    depthWrite = false,
                    blend = pass.blend,
                    colorWriteMask = 15
                })),
            techniques:
            [
                new(
                    new("default"),
                    Rendering2DIds.spriteContract,
                    passes.Select(static pass => new ShaderTechniquePass(pass.role, pass.name)))
            ]);
        GraphDocument graph = ShaderGraphDocument.Create(definition, serialization, context);

        GraphNodeRecord vertexOutput = StageOutput(
            "Vertex",
            ShaderStage.Vertex,
            [Output("position", ShaderIrOutputKind.ClipPosition)],
            1160,
            40);
        GraphNodeRecord zero = Node(
            "vertex-offset-zero",
            "inno.shader.constant",
            vertexOutput.id.value,
            40,
            40);
        Set(zero, "type", "float");
        Set(zero, "value", 0f);
        GraphNodeRecord vertexOffset = Node(
            "vertex-offset",
            "inno.shader.construct",
            vertexOutput.id.value,
            300,
            40);
        Set(vertexOffset, "type", "float3");
        GraphNodeRecord vertex = GraphNode(
            "sprite-vertex",
            vertexNode,
            vertexNodePath,
            VertexInterface(),
            vertexOutput.id.value,
            720,
            40);
        Connect(zero, "value", vertexOffset, "component.0");
        Connect(zero, "value", vertexOffset, "component.1");
        Connect(zero, "value", vertexOffset, "component.2");
        Connect(vertexOffset, "value", vertex, "vertexOffset");
        Connect(vertex, "position", vertexOutput, "position");

        GraphNodeRecord fragmentOutput = StageOutput(
            "Fragment",
            ShaderStage.Fragment,
            [Output("color", ShaderIrOutputKind.Color)],
            1080,
            360);
        GraphNodeRecord texture = GraphNode(
            "sprite-texture",
            textureNode,
            textureNodePath,
            TextureInterface(),
            fragmentOutput.id.value,
            360,
            300);
        GraphNodeRecord tint = Node("tint", "inno.shader.stage-input", fragmentOutput.id.value, 40, 600);
        Set(tint, ShaderGraphDocument.settingsKey, new ShaderGraphInputSettings
        {
            id = "tint",
            type = new() { id = "float4" },
            kind = ShaderIrInputKind.Uniform
        });
        GraphNodeRecord multiply = Node("tint-multiply", "inno.shader.binary", fragmentOutput.id.value, 550, 440);
        Set(multiply, "type", "float4");
        Set(multiply, "operation", "multiply");
        GraphNodeRecord surface = GraphNode(
            "sprite-surface",
            surfaceNode,
            surfaceNodePath,
            SurfaceInterface(),
            fragmentOutput.id.value,
            760,
            360);
        Connect(vertex, "uv", texture, "uv");
        Connect(vertex, "shape", texture, "shape");
        Connect(texture, "color", multiply, "left");
        Connect(tint, "value", multiply, "right");
        Connect(multiply, "value", surface, "color");
        Connect(texture, "normal", surface, "normal");
        Connect(texture, "emission", surface, "emission");
        Connect(vertex, "vertexColor", surface, "vertexColor");
        Connect(vertex, "shape", surface, "shape");
        Connect(vertex, "effectStep", surface, "effectStep");
        Connect(vertex, "uv", surface, "uv");
        Connect(surface, "color", fragmentOutput, "color");

        foreach ((string name, _, _) in passes)
            graph = ShaderGraphPrograms.Bind(graph, name, [vertexOutput.id, fragmentOutput.id], serialization, context);
        return graph;

        GraphNodeRecord StageOutput(
            string id,
            ShaderStage stage,
            ShaderGraphOutput[] outputs,
            float x,
            float y)
        {
            GraphNodeRecord node = Node(id, ShaderGraphDocument.outputDefinitionId, id, x, y);
            Set(node, ShaderGraphDocument.settingsKey, new ShaderGraphStageSettings { stage = stage, outputs = outputs });
            return node;
        }

        GraphNodeRecord Node(string id, string definitionId, string stage, float x, float y)
        {
            var node = new GraphNodeRecord(new(id), definitionId) { position = new(x, y) };
            graph.AddNode(node);
            Set(node, ShaderGraphDocument.stageKey, stage);
            return node;
        }

        GraphNodeRecord GraphNode(
            string id,
            ShaderAsset asset,
            string path,
            ShaderGraphNodeInterface nodeInterface,
            string stage,
            float x,
            float y)
        {
            GraphNodeRecord node = Node(id, ShaderGraphNodes.callDefinitionId, stage, x, y);
            Set(node, "sourceId", asset.identity.persistentId);
            Set(node, "sourcePath", path);
            Set(node, ShaderGraphNodes.interfaceKey, nodeInterface);
            return node;
        }

        void Set<T>(GraphNodeRecord node, string key, T value)
            => node.SetValue(key, ShaderGraphDocument.Encode(value, serialization, context));

        void Connect(GraphNodeRecord from, string fromPort, GraphNodeRecord to, string toPort)
            => graph.AddEdge(new(
                new(from.id.value + "." + fromPort + "->" + to.id.value + "." + toPort),
                new(from.id, new(fromPort)),
                new(to.id, new(toPort))));
    }

    private static ShaderGraphNodeInterface VertexInterface()
        => new()
        {
            displayName = "Sprite Vertex",
            createPath = "Rendering 2D/Sprite",
            createOrder = 90,
            kind = ShaderGraphNodeKind.Function,
            effect = ShaderGraphNodeEffect.Pure,
            inputs = [Port("vertexOffset", "float3", false)],
            outputs =
            [
                Port("position", "float4"),
                Port("uv", "float2"),
                Port("vertexColor", "float4"),
                Port("shape", "float"),
                Port("effectStep", "float2")
            ]
        };

    private static ShaderGraphNodeInterface TextureInterface()
        => new()
        {
            displayName = "Sprite Texture",
            createPath = "Rendering 2D/Sprite",
            createOrder = 100,
            kind = ShaderGraphNodeKind.Function,
            effect = ShaderGraphNodeEffect.Pure,
            inputs =
            [
                Port("uv", "float2"),
                Port("shape", "float")
            ],
            outputs =
            [
                Port("color", "float4"),
                Port("normal", "float3"),
                Port("emission", "float3")
            ]
        };

    private static ShaderGraphNodeInterface SurfaceInterface()
        => new()
        {
            displayName = "Sprite Surface",
            createPath = "Rendering 2D/Sprite",
            createOrder = 110,
            kind = ShaderGraphNodeKind.Function,
            effect = ShaderGraphNodeEffect.SideEffect,
            inputs =
            [
                Port("color", "float4"),
                Port("normal", "float3"),
                Port("emission", "float3"),
                Port("vertexColor", "float4"),
                Port("shape", "float"),
                Port("effectStep", "float2"),
                Port("uv", "float2")
            ],
            outputs = [Port("color", "float4")]
        };

    private static ShaderGraphNodePortDefinition Port(string id, string type, bool required = true)
        => new() { id = id, type = new() { id = type }, required = required };

    private static ShaderGraphOutput Output(
        string id,
        ShaderIrOutputKind kind,
        string semantic = "",
        int location = 0)
        => new() { id = id, kind = kind, semantic = semantic, location = location };

    private static void Validate(ShaderAsset asset, string parameter)
    {
        ArgumentNullException.ThrowIfNull(asset, parameter);
        if (asset.isMissing || asset.identity.persistentId == Guid.Empty)
            throw new ArgumentException("Choose an imported graph-node Shader.", parameter);
    }
}
