using System;
using InnoEditor.Rendering;
using InnoEngine.Rendering;

namespace Inno.Rendering2D;

/// <summary>
/// Contributes the active runtime 2D camera stack to the Game viewport.
/// </summary>
[EditorViewportContributorExtension(
    "Inno.Rendering2D." + nameof(Rendering2DGameViewportContributor),
    "inno.editor.viewport.game",
    order: 1000,
    controllerPriority: 100)]
public sealed class Rendering2DGameViewportContributor : EditorViewportContributor
{
    private readonly Rendering2DModel m_model = new();

    /// <summary>
    /// Determines whether the visible content contains at least one scene with an active 2D rendering model.
    /// </summary>
    /// <param name="context">
    /// The current Game viewport context and its explicit content scope.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when at least one scene declares an enabled 2D extraction system.
    /// </returns>
    public override bool CanContribute(EditorViewportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return m_model.CanRender(Session(context));
    }

    /// <summary>
    /// Builds the current runtime 2D camera stack as one ordered viewport contribution.
    /// </summary>
    /// <param name="context">
    /// The current Game viewport context and presentation preferences.
    /// </param>
    /// <returns>
    /// The immutable frame data and 2D pipeline selection for this composition layer.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when participating 2D scenes do not contain an enabled base camera that targets presentation.
    /// </exception>
    public override EditorViewportContribution Build(EditorViewportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RenderModelOutput output = m_model.Build(Session(context));
        return new EditorViewportContribution(output.data, output.pipeline, output.targetFormat);
    }

    private static RenderOutputSession Session(EditorViewportContext context)
        => new(context.viewportId, context.content,
            new RenderViewport(0, 0, context.pixelWidth, context.pixelHeight),
            context.frameIndex, 0f, context.viewContent, context.input);
}
