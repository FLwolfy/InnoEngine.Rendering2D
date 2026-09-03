using System;
using System.Collections.Generic;
using InnoEditor.Rendering;
using InnoEngine.Scene;

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
        Rendering2DSceneScope scope = CreateScope(context);
        for (int sceneIndex = 0; sceneIndex < scope.scenes.Count; sceneIndex++)
        {
            IReadOnlyList<GameSystem> systems = scope.scenes[sceneIndex].GetSystems();
            for (int systemIndex = 0; systemIndex < systems.Count; systemIndex++)
            {
                if (systems[systemIndex] is Rendering2DSceneSystem { isActiveAndEnabled: true })
                    return true;
            }
        }
        return false;
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
        Rendering2DSceneScope scope = CreateScope(context);
        Rendering2DViewportFrame frame = Rendering2DRenderer.CreateCameraStackFrame(
            scope,
            context.pixelWidth,
            context.pixelHeight,
            new Rendering2DViewportOptions
            {
                clearColorOverride = context.presentation.backgroundColor,
                backbufferOnly = true
            });
        return new EditorViewportContribution(
            frame.data,
            Rendering2DRenderer.CreatePipelineAsset());
    }

    private static Rendering2DSceneScope CreateScope(EditorViewportContext context)
        => new(context.content.GetValues<GameScene>());
}
