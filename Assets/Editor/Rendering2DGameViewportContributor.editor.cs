using System;
using System.Collections.Generic;
using InnoEngine.Logging;
using InnoEditor.Rendering;
using InnoEngine.Rendering;
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
    private const int C_RESIZE_CHURN_FRAME_COUNT = 96;

    private RenderPipelineAsset pipeline => Rendering2DRenderer.ResolvePipeline();
    private readonly Rendering2DSceneScopeCache m_scopeCache = new();
    private readonly Rendering2DViewportOptions m_viewportOptions = new()
    {
        backbufferOnly = true
    };
    private readonly bool m_runPerformanceGate = string.Equals(
        Environment.GetEnvironmentVariable("INNO_RENDERING2D_RUN_PERFORMANCE_GATE"),
        "1",
        StringComparison.Ordinal);
    private readonly bool m_runResizeChurnGate = string.Equals(
        Environment.GetEnvironmentVariable("INNO_RENDERING2D_RUN_GAME_VIEW_RESIZE_GATE"),
        "1",
        StringComparison.Ordinal);
    private bool m_performanceGateCompleted;
    private int m_resizeChurnFrame;
    private bool m_resizeChurnGateCompleted;
    private ulong m_resizeSubmittedAfterFrame;
    private RenderDeviceAllocationCounters? m_resizePreviousAllocations;

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
        m_viewportOptions.clearColorOverride = context.presentation.backgroundColor;
        int pixelWidth = context.pixelWidth;
        int pixelHeight = context.pixelHeight;
        RenderFrameStatistics? completed = GraphicsSettings.frameStatistics;
        bool rendered = completed is { viewCount: > 0, drawCount: >= 32, allocationCounters: not null };
        if (m_runResizeChurnGate && !m_resizeChurnGateCompleted && rendered
            && completed!.frameIndex != m_resizeSubmittedAfterFrame)
        {
            if (m_resizePreviousAllocations is RenderDeviceAllocationCounters previous)
            {
                RenderDeviceAllocationCounters current = completed.allocationCounters!.Value;
                if (current.deviceGeneration != previous.deviceGeneration || current.frameBufferAllocations <= previous.frameBufferAllocations)
                    throw new InvalidOperationException("Game View resize acceptance did not observe a completed framebuffer rebuild.");
            }
            if (m_resizeChurnFrame == C_RESIZE_CHURN_FRAME_COUNT)
            {
                m_resizeChurnGateCompleted = true;
                Rendering2DGpuAcceptanceProgress.resizePending = false;
                string backend = GraphicsSettings.capabilities?.backend.ToString() ?? "active backend";
                Log.Info($"Rendering2D Editor Game View resize gate passed: {C_RESIZE_CHURN_FRAME_COUNT} consecutive {backend} render-target rebuilds completed.");
            }
            else
            {
                int phase = m_resizeChurnFrame++;
                pixelWidth = 320 + phase * 53 % 901;
                pixelHeight = 180 + phase * 37 % 601;
                m_resizeSubmittedAfterFrame = completed.frameIndex;
                m_resizePreviousAllocations = completed.allocationCounters;
            }
        }
        Rendering2DViewportFrame frame = Rendering2DRenderer.CreateCameraStackFrame(
            scope,
            pixelWidth,
            pixelHeight,
            m_viewportOptions);
        if (m_runPerformanceGate && !m_performanceGateCompleted)
        {
            Rendering2DPerformanceGateResult result = Rendering2DPerformanceGate.MeasureStableCameraStack(
                scope,
                context.pixelWidth,
                context.pixelHeight,
                m_viewportOptions);
            m_performanceGateCompleted = true;
            Log.Info(
                $"Rendering2D camera-stack performance gate passed: {result.allocatedBytes} managed bytes, " +
                $"P95 {result.p95Milliseconds:F3} ms over {result.sampleCount} stable extractions.");
        }
        return new EditorViewportContribution(
            frame.data,
            pipeline);
    }

    private Rendering2DSceneScope CreateScope(EditorViewportContext context)
        => m_scopeCache.Get(context.content);
}
