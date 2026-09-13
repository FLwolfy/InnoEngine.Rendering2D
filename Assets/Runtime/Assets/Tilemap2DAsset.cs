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

/// <summary>Describes one independently ordered tilemap layer.</summary>
public struct TilemapLayer2D
{
    /// <summary>Gets or sets the stable non-negative layer identity.</summary>
    public int id { get; set; }

    /// <summary>Gets or sets the author-facing layer name.</summary>
    public string name { get; set; }

    /// <summary>Gets or sets whether the layer contributes visible cells.</summary>
    public bool visible { get; set; }

    /// <summary>Gets or sets the layer tint.</summary>
    public Color color { get; set; }

    /// <summary>Gets or sets relative painter order.</summary>
    public int order { get; set; }

    /// <summary>Creates the default visible tilemap layer.</summary>
    /// <returns>A white layer with stable identity zero.</returns>
    public static TilemapLayer2D CreateDefault() => new()
    {
        id = 0,
        name = "Ground",
        visible = true,
        color = Color.WHITE,
        order = 0
    };
}

/// <summary>Stores one deterministic sparse chunk of tile cells.</summary>
public struct TilemapChunk2D
{
    /// <summary>Gets or sets the owning layer identity.</summary>
    public int layerId { get; set; }

    /// <summary>Gets or sets the horizontal chunk coordinate.</summary>
    public int x { get; set; }

    /// <summary>Gets or sets the vertical chunk coordinate.</summary>
    public int y { get; set; }

    /// <summary>Gets or sets cells in deterministic Y then X order.</summary>
    public TilemapCell2D[] cells { get; set; }
}

/// <summary>Describes one batched sparse-cell write without using sentinel tile identifiers.</summary>
public readonly record struct TilemapCellWrite2D
{
    private TilemapCellWrite2D(int layerId, int x, int y, bool hasValue, TilemapCell2D value)
    {
        this.layerId = layerId;
        this.x = x;
        this.y = y;
        this.hasValue = hasValue;
        this.value = value;
    }

    /// <summary>Gets the stable destination layer identity.</summary>
    public int layerId { get; }

    /// <summary>Gets the horizontal destination coordinate.</summary>
    public int x { get; }

    /// <summary>Gets the vertical destination coordinate.</summary>
    public int y { get; }

    /// <summary>Gets whether this write stores a cell instead of removing it.</summary>
    public bool hasValue { get; }

    /// <summary>Gets the value stored when <see cref="hasValue"/> is true.</summary>
    public TilemapCell2D value { get; }

    /// <summary>Creates a write that stores a cell at its own coordinates.</summary>
    /// <param name="layerId">Stable destination layer identity.</param>
    /// <param name="value">Complete cell value.</param>
    /// <returns>A store operation.</returns>
    public static TilemapCellWrite2D Set(int layerId, TilemapCell2D value)
        => new(layerId, value.x, value.y, true, value);

    /// <summary>Creates a write that removes a cell when present.</summary>
    /// <param name="layerId">Stable destination layer identity.</param>
    /// <param name="x">Horizontal cell coordinate.</param>
    /// <param name="y">Vertical cell coordinate.</param>
    /// <returns>A remove operation.</returns>
    public static TilemapCellWrite2D Remove(int layerId, int x, int y)
        => new(layerId, x, y, false, default);
}

/// <summary>Stores a sparse, layered, chunked tilemap without expanding empty cells.</summary>
[StableTypeId("8bb5f309-b750-4555-b26d-63ca793e5ae6")]
public sealed class Tilemap2DAsset : AssetObject
{
    private TilemapLayer2D[] m_layers = [TilemapLayer2D.CreateDefault()];
    private TilemapChunk2D[] m_chunks = [];
    private readonly Dictionary<ChunkKey, ulong> m_chunkRevisions = [];
    private TileSet2DAsset? m_tileSet;
    private Vector2 m_cellSize = Vector2.ONE;
    private int m_chunkSize = 32;
    private ulong m_revision = 1;

    /// <summary>Gets or sets the TileSet used to resolve cell visuals and metadata.</summary>
    [SerializableProperty]
    public TileSet2DAsset? tileSet
    {
        get => m_tileSet;
        set
        {
            if (ReferenceEquals(m_tileSet, value))
                return;
            m_tileSet = value;
            InvalidateAllChunks();
        }
    }

    /// <summary>Gets or sets world dimensions of one cell.</summary>
    [SerializableProperty]
    public Vector2 cellSize
    {
        get => m_cellSize;
        set
        {
            if (m_cellSize.Equals(value))
                return;
            m_cellSize = value;
            InvalidateAllChunks();
        }
    }

    /// <summary>Gets or sets the positive square chunk dimension in cells.</summary>
    [SerializableProperty]
    public int chunkSize
    {
        get => m_chunkSize;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Tilemap chunk size must be positive.");
            if (m_chunkSize == value)
                return;
            m_chunkSize = value;
            RepartitionChunks();
        }
    }

    /// <summary>Gets or sets ordered authoring layers.</summary>
    [SerializableProperty]
    public TilemapLayer2D[] layers
    {
        get => m_layers;
        set
        {
            m_layers = value?.OrderBy(static layer => layer.order).ThenBy(static layer => layer.id).ToArray() ?? [];
            InvalidateAllChunks();
        }
    }

    /// <summary>Gets or sets sparse chunks in deterministic layer, Y, X order.</summary>
    [SerializableProperty]
    public TilemapChunk2D[] chunks
    {
        get => m_chunks;
        set
        {
            m_chunks = SortChunks(value ?? []);
            InvalidateAllChunks();
        }
    }

    /// <summary>Gets the monotonic runtime revision of all tilemap content and layout.</summary>
    public ulong revision => m_revision;

    /// <summary>Gets the last runtime revision that changed one sparse chunk.</summary>
    /// <param name="layerId">Stable layer identity.</param>
    /// <param name="chunkX">Horizontal chunk coordinate.</param>
    /// <param name="chunkY">Vertical chunk coordinate.</param>
    /// <returns>The non-zero revision, or zero when the chunk has never existed in this generation.</returns>
    public ulong GetChunkRevision(int layerId, int chunkX, int chunkY)
        => m_chunkRevisions.TryGetValue(new ChunkKey(layerId, chunkX, chunkY), out ulong value)
            ? value
            : 0;

    /// <summary>Replaces all layers after validating stable identities.</summary>
    /// <param name="layers">Complete layer set.</param>
    /// <exception cref="ArgumentException">Thrown when layer IDs or names are invalid.</exception>
    public void SetLayers(IEnumerable<TilemapLayer2D> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        TilemapLayer2D[] values = layers.ToArray();
        if (values.Length == 0 || values.Any(static layer => layer.id < 0 || string.IsNullOrWhiteSpace(layer.name)))
            throw new ArgumentException("A tilemap requires at least one named layer with a non-negative ID.", nameof(layers));
        if (values.Select(static layer => layer.id).Distinct().Count() != values.Length)
            throw new ArgumentException("Tilemap layer IDs must be unique.", nameof(layers));
        this.layers = values;
    }

    /// <summary>Creates or replaces one sparse cell.</summary>
    /// <param name="layerId">Stable destination layer identity.</param>
    /// <param name="cell">Cell value to store.</param>
    /// <exception cref="InvalidOperationException">Thrown when the layer is undefined.</exception>
    public void SetCell(int layerId, TilemapCell2D cell)
    {
        _ = ApplyCells([TilemapCellWrite2D.Set(layerId, cell)]);
    }

    /// <summary>Removes one sparse cell and its chunk when the chunk becomes empty.</summary>
    /// <param name="x">Horizontal cell coordinate.</param>
    /// <param name="y">Vertical cell coordinate.</param>
    /// <param name="layerId">Stable layer identity.</param>
    /// <returns><see langword="true"/> when a cell was removed.</returns>
    public bool RemoveCell(int x, int y, int layerId = 0)
    {
        return ApplyCells([TilemapCellWrite2D.Remove(layerId, x, y)]) != 0;
    }

    /// <summary>Applies a batch of sparse writes while rebuilding each affected chunk at most once.</summary>
    /// <param name="writes">Writes whose last value wins for duplicate coordinates.</param>
    /// <returns>The number of cells whose stored value or occupancy changed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a destination layer is undefined.</exception>
    public int ApplyCells(IEnumerable<TilemapCellWrite2D> writes)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ValidateChunkSize();
        var grouped = new Dictionary<ChunkKey, Dictionary<CellKey, TilemapCellWrite2D>>();
        foreach (TilemapCellWrite2D write in writes)
        {
            if (!m_layers.Any(layer => layer.id == write.layerId))
                throw new InvalidOperationException($"Tilemap layer '{write.layerId}' is not defined.");
            (int chunkX, int chunkY) = GetChunkCoordinates(write.x, write.y);
            var chunkKey = new ChunkKey(write.layerId, chunkX, chunkY);
            if (!grouped.TryGetValue(chunkKey, out Dictionary<CellKey, TilemapCellWrite2D>? chunkWrites))
                grouped.Add(chunkKey, chunkWrites = []);
            chunkWrites[new CellKey(write.x, write.y)] = write;
        }

        int changed = 0;
        foreach ((ChunkKey key, Dictionary<CellKey, TilemapCellWrite2D> chunkWrites) in grouped)
        {
            int chunkIndex = FindChunk(key);
            TilemapChunk2D existing = chunkIndex >= 0
                ? m_chunks[chunkIndex]
                : new TilemapChunk2D { layerId = key.layerId, x = key.x, y = key.y, cells = [] };
            var cells = new Dictionary<CellKey, TilemapCell2D>();
            foreach (TilemapCell2D cell in existing.cells ?? [])
                cells[new CellKey(cell.x, cell.y)] = cell;

            int chunkChanges = 0;
            foreach ((CellKey cellKey, TilemapCellWrite2D write) in chunkWrites)
            {
                if (!write.hasValue)
                {
                    if (cells.Remove(cellKey))
                        chunkChanges++;
                    continue;
                }
                TilemapCell2D normalized = write.value;
                normalized.x = write.x;
                normalized.y = write.y;
                normalized.quarterTurns = ((normalized.quarterTurns % 4) + 4) % 4;
                if (cells.TryGetValue(cellKey, out TilemapCell2D current)
                    && EqualityComparer<TilemapCell2D>.Default.Equals(current, normalized))
                {
                    continue;
                }
                cells[cellKey] = normalized;
                chunkChanges++;
            }
            if (chunkChanges == 0)
                continue;

            changed += chunkChanges;
            if (cells.Count == 0)
            {
                if (chunkIndex >= 0)
                    m_chunks = m_chunks.Where((_, index) => index != chunkIndex).ToArray();
            }
            else
            {
                existing.cells = SortCells(cells.Values);
                if (chunkIndex >= 0)
                    m_chunks[chunkIndex] = existing;
                else
                {
                    Array.Resize(ref m_chunks, m_chunks.Length + 1);
                    m_chunks[^1] = existing;
                    m_chunks = SortChunks(m_chunks);
                }
            }
            MarkChunkChanged(key);
        }
        return changed;
    }

    /// <summary>Tries to resolve one sparse cell.</summary>
    /// <param name="x">Horizontal cell coordinate.</param>
    /// <param name="y">Vertical cell coordinate.</param>
    /// <param name="layerId">Stable layer identity.</param>
    /// <param name="cell">Receives the matching cell.</param>
    /// <returns><see langword="true"/> when a cell exists.</returns>
    public bool TryGetCell(int x, int y, int layerId, out TilemapCell2D cell)
    {
        ValidateChunkSize();
        (int chunkX, int chunkY) = GetChunkCoordinates(x, y);
        int chunkIndex = Array.FindIndex(m_chunks, candidate =>
            candidate.layerId == layerId && candidate.x == chunkX && candidate.y == chunkY);
        if (chunkIndex >= 0)
        {
            TilemapCell2D[] cells = m_chunks[chunkIndex].cells ?? [];
            int cellIndex = Array.FindIndex(cells, candidate => candidate.x == x && candidate.y == y);
            if (cellIndex >= 0)
            {
                cell = cells[cellIndex];
                return true;
            }
        }
        cell = default;
        return false;
    }

    /// <summary>Gets the layer descriptor for a stable identity.</summary>
    /// <param name="layerId">Stable layer identity.</param>
    /// <param name="layer">Receives the matching layer.</param>
    /// <returns><see langword="true"/> when the layer exists.</returns>
    public bool TryGetLayer(int layerId, out TilemapLayer2D layer)
    {
        int index = Array.FindIndex(m_layers, candidate => candidate.id == layerId);
        layer = index >= 0 ? m_layers[index] : default;
        return index >= 0;
    }

    private void ValidateChunkSize()
    {
        if (chunkSize <= 0)
            throw new InvalidOperationException("Tilemap chunk size must be positive.");
    }

    private (int x, int y) GetChunkCoordinates(int x, int y)
        => (FloorDivide(x, chunkSize), FloorDivide(y, chunkSize));

    private int FindChunk(ChunkKey key)
        => Array.FindIndex(m_chunks, candidate =>
            candidate.layerId == key.layerId && candidate.x == key.x && candidate.y == key.y);

    private void RepartitionChunks()
    {
        TilemapCellWrite2D[] writes = m_chunks
            .SelectMany(static chunk => (chunk.cells ?? [])
                .Select(cell => TilemapCellWrite2D.Set(chunk.layerId, cell)))
            .ToArray();
        m_chunks = [];
        m_chunkRevisions.Clear();
        if (writes.Length == 0)
        {
            BumpRevision();
            return;
        }
        _ = ApplyCells(writes);
    }

    private void InvalidateAllChunks()
    {
        ulong value = BumpRevision();
        m_chunkRevisions.Clear();
        for (int index = 0; index < m_chunks.Length; index++)
        {
            TilemapChunk2D chunk = m_chunks[index];
            m_chunkRevisions[new ChunkKey(chunk.layerId, chunk.x, chunk.y)] = value;
        }
    }

    private void MarkChunkChanged(ChunkKey key)
        => m_chunkRevisions[key] = BumpRevision();

    private ulong BumpRevision()
    {
        m_revision++;
        if (m_revision == 0)
            m_revision = 1;
        return m_revision;
    }

    private static int FloorDivide(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static TilemapCell2D[] SortCells(IEnumerable<TilemapCell2D> cells)
        => cells.OrderBy(static cell => cell.y).ThenBy(static cell => cell.x).ToArray();

    private static TilemapChunk2D[] SortChunks(IEnumerable<TilemapChunk2D> chunks)
        => chunks.OrderBy(static chunk => chunk.layerId)
            .ThenBy(static chunk => chunk.y)
            .ThenBy(static chunk => chunk.x)
            .ToArray();

    private readonly record struct ChunkKey(int layerId, int x, int y);

    private readonly record struct CellKey(int x, int y);
}
