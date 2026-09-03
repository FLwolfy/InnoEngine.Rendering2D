using System;
using InnoEditor.ImGui;
using InnoEditor.Settings;
using InnoEngine.Settings;

namespace Inno.Rendering2D;

/// <summary>Provides the renderer-limit section of the unified 2D Project Settings page.</summary>
[ProjectSettingPath("Project/Rendering/2D/Renderer")]
public sealed class Rendering2DProjectSettingsEditor : ProjectSettingEditor<Rendering2DProjectSettings>
{
    /// <inheritdoc />
    public override ProjectSettingId settingId => Rendering2DProjectSettings.id;

    /// <inheritdoc />
    public override string section => "Renderer";

    /// <inheritdoc />
    public override string description
        => "Configure project-wide pixel density and bounded 2D batching.";

    /// <inheritdoc />
    protected override void OnDraw(Rendering2DProjectSettings setting)
    {
        float pixelsPerUnit = setting.defaultPixelsPerUnit;
        if (ImGui.InputFloat("Default Pixels Per Unit", ref pixelsPerUnit))
            setting.defaultPixelsPerUnit = MathF.Max(0.001f, pixelsPerUnit);
        int frameLimit = setting.maximumQuadsPerFrame;
        if (ImGui.InputInt("Maximum Quads Per Frame", ref frameLimit))
            setting.maximumQuadsPerFrame = Math.Max(1, frameLimit);
        int batchLimit = setting.maximumQuadsPerBatch;
        if (ImGui.InputInt("Maximum Quads Per Batch", ref batchLimit))
            setting.maximumQuadsPerBatch = Math.Max(1, batchLimit);
        int tiledLimit = setting.maximumTiledSpriteQuads;
        if (ImGui.InputInt("Maximum Tiled Sprite Quads", ref tiledLimit))
            setting.maximumTiledSpriteQuads = Math.Max(1, tiledLimit);
    }
}

/// <summary>Provides the sorting-layer section of the unified 2D Project Settings page.</summary>
[ProjectSettingPath("Project/Rendering/2D/Sorting Layers", order: 100)]
public sealed class Rendering2DSortingLayersProjectSettingsEditor
    : ProjectSettingEditor<Rendering2DProjectSettings>
{
    private string m_newId = "inno.rendering.2d.layer";
    private string m_newName = "Layer";
    private int m_newOrder;

    /// <inheritdoc />
    public override ProjectSettingId settingId => Rendering2DProjectSettings.id;

    /// <inheritdoc />
    public override string section => "Sorting Layers";

    /// <inheritdoc />
    public override string description
        => "Configure stable sprite sorting-layer identities and their global order.";

    /// <inheritdoc />
    protected override void OnDraw(Rendering2DProjectSettings setting)
    {
        var layers = new System.Collections.Generic.List<SortingLayer2DDefinition>(setting.sortingLayers);
        bool changed = false;
        for (int index = 0; index < layers.Count; index++)
        {
            SortingLayer2DDefinition layer = layers[index];
            ImGui.PushId(index);
            string id = layer.id ?? string.Empty;
            string name = layer.name ?? string.Empty;
            int order = layer.order;
            changed |= ImGui.InputText("ID", ref id, 256);
            changed |= ImGui.InputText("Name", ref name, 128);
            changed |= ImGui.InputInt("Order", ref order);
            if (ImGui.Button("Remove"))
            {
                layers.RemoveAt(index--);
                changed = true;
            }
            else
            {
                layers[index] = new SortingLayer2DDefinition
                {
                    id = id,
                    name = name,
                    order = order
                };
            }
            ImGui.PopId();
            ImGui.Separator();
        }
        if (changed)
            TryApplyLayers(setting, layers);

        ImGui.InputText("New ID", ref m_newId, 256);
        ImGui.InputText("New Name", ref m_newName, 128);
        ImGui.InputInt("New Order", ref m_newOrder);
        if (ImGui.Button("Add Sorting Layer")
            && !string.IsNullOrWhiteSpace(m_newId)
            && !string.IsNullOrWhiteSpace(m_newName))
        {
            layers.Add(new SortingLayer2DDefinition(m_newId, m_newName, m_newOrder));
            TryApplyLayers(setting, layers);
        }
    }

    private static void TryApplyLayers(
        Rendering2DProjectSettings setting,
        System.Collections.Generic.IEnumerable<SortingLayer2DDefinition> layers)
    {
        try
        {
            setting.SetSortingLayers(layers);
        }
        catch (ArgumentException exception)
        {
            ImGui.Text(exception.Message);
        }
    }
}
