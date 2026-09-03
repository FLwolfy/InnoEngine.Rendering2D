using System;
using System.Collections.Generic;
using System.Linq;
using InnoEngine.Assets;
using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Stores one sparse tilemap cell with transform flags and tint.</summary>
public struct TilemapCell2D
{
    /// <summary>Gets or sets integer horizontal cell position.</summary>
    public int x { get; set; }

    /// <summary>Gets or sets integer vertical cell position.</summary>
    public int y { get; set; }

    /// <summary>Gets or sets independent tilemap layer.</summary>
    public int layer { get; set; }

    /// <summary>Gets or sets the stable TileSet identity.</summary>
    public int tileId { get; set; }

    /// <summary>Gets or sets cell tint multiplied with TileSet and renderer tints.</summary>
    public Color color { get; set; }

    /// <summary>Gets or sets horizontal mirroring.</summary>
    public bool flipX { get; set; }

    /// <summary>Gets or sets vertical mirroring.</summary>
    public bool flipY { get; set; }

    /// <summary>Gets or sets clockwise quarter turns in the range zero through three.</summary>
    public int quarterTurns { get; set; }
}

/// <summary>Stores sparse, layered tile cells in deterministic coordinate order.</summary>
[StableTypeId("8bb5f309-b750-4555-b26d-63ca793e5ae6")]
public sealed class Tilemap2DAsset : AssetObject
{
    private TilemapCell2D[] m_cells = [];

    /// <summary>Gets or sets the TileSet used to resolve cell visuals and metadata.</summary>
    [SerializableProperty]
    public TileSet2DAsset? tileSet { get; set; }

    /// <summary>Gets or sets world dimensions of one cell.</summary>
    [SerializableProperty]
    public Vector2 cellSize { get; set; } = Vector2.ONE;

    /// <summary>Gets or sets sparse cells in deterministic layer, Y, X order.</summary>
    [SerializableProperty]
    public TilemapCell2D[] cells
    {
        get => m_cells;
        set => m_cells = Sort(value ?? []);
    }

    /// <summary>Creates or replaces one sparse cell.</summary>
    /// <param name="cell">Cell value to store.</param>
    public void SetCell(TilemapCell2D cell)
    {
        cell.quarterTurns = ((cell.quarterTurns % 4) + 4) % 4;
        int index = Array.FindIndex(m_cells, candidate =>
            candidate.x == cell.x && candidate.y == cell.y && candidate.layer == cell.layer);
        if (index < 0)
        {
            Array.Resize(ref m_cells, m_cells.Length + 1);
            m_cells[^1] = cell;
        }
        else
        {
            m_cells[index] = cell;
        }
        m_cells = Sort(m_cells);
    }

    /// <summary>Removes one sparse cell.</summary>
    /// <param name="x">Horizontal coordinate.</param>
    /// <param name="y">Vertical coordinate.</param>
    /// <param name="layer">Tilemap layer.</param>
    /// <returns><see langword="true"/> when a cell was removed.</returns>
    public bool RemoveCell(int x, int y, int layer = 0)
    {
        int index = Array.FindIndex(m_cells, candidate =>
            candidate.x == x && candidate.y == y && candidate.layer == layer);
        if (index < 0)
            return false;
        m_cells = m_cells.Where((_, candidateIndex) => candidateIndex != index).ToArray();
        return true;
    }

    /// <summary>Tries to resolve one sparse cell.</summary>
    /// <param name="x">Horizontal coordinate.</param>
    /// <param name="y">Vertical coordinate.</param>
    /// <param name="layer">Tilemap layer.</param>
    /// <param name="cell">Receives the matching cell.</param>
    /// <returns><see langword="true"/> when a cell exists.</returns>
    public bool TryGetCell(int x, int y, int layer, out TilemapCell2D cell)
    {
        int index = Array.FindIndex(m_cells, candidate =>
            candidate.x == x && candidate.y == y && candidate.layer == layer);
        cell = index < 0 ? default : m_cells[index];
        return index >= 0;
    }

    private static TilemapCell2D[] Sort(IEnumerable<TilemapCell2D> cells)
        => cells.OrderBy(static cell => cell.layer)
            .ThenBy(static cell => cell.y)
            .ThenBy(static cell => cell.x)
            .ToArray();
}
