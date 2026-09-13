using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using InnoEditor.Interactions;
using InnoEngine.Assets;
using InnoEngine.Mathematics;

namespace Inno.Rendering2D;

/// <summary>Identifies one integer tilemap coordinate.</summary>
public readonly record struct TilemapPosition2D(int x, int y);

/// <summary>Defines an inclusive finite rectangle used by tilemap authoring operations.</summary>
public readonly record struct TilemapSelection2D
{
    /// <summary>Creates a normalized inclusive selection rectangle.</summary>
    public TilemapSelection2D(int firstX, int firstY, int secondX, int secondY)
    {
        minimumX = Math.Min(firstX, secondX);
        minimumY = Math.Min(firstY, secondY);
        maximumX = Math.Max(firstX, secondX);
        maximumY = Math.Max(firstY, secondY);
    }

    /// <summary>Gets the inclusive minimum horizontal coordinate.</summary>
    public int minimumX { get; }

    /// <summary>Gets the inclusive minimum vertical coordinate.</summary>
    public int minimumY { get; }

    /// <summary>Gets the inclusive maximum horizontal coordinate.</summary>
    public int maximumX { get; }

    /// <summary>Gets the inclusive maximum vertical coordinate.</summary>
    public int maximumY { get; }

    /// <summary>Gets whether the selection contains a coordinate.</summary>
    public bool Contains(int x, int y)
        => x >= minimumX && x <= maximumX && y >= minimumY && y <= maximumY;
}

/// <summary>Stores one relative cell used by the reusable stamp tool.</summary>
public readonly record struct TilemapStampCell2D(int x, int y, TilemapCell2D value);

/// <summary>Stores optional occupancy without reserving a tile identifier as an empty sentinel.</summary>
public readonly record struct TilemapCellSnapshot2D(bool hasValue, TilemapCell2D value);

/// <summary>Stores the before and after value for exactly one affected tilemap cell.</summary>
public readonly record struct TilemapCellChange2D(
    int layerId,
    int x,
    int y,
    TilemapCellSnapshot2D before,
    TilemapCellSnapshot2D after);

/// <summary>Owns the compact, deterministic result of one complete tilemap gesture.</summary>
public sealed class TilemapStroke2D
{
    internal TilemapStroke2D(TilemapCellChange2D[] changes)
        => this.changes = changes;

    /// <summary>Gets affected cells in stable layer, Y, X order.</summary>
    public IReadOnlyList<TilemapCellChange2D> changes { get; }

    /// <summary>Gets whether the gesture changed at least one cell.</summary>
    public bool hasChanges => changes.Count != 0;

    /// <summary>Restores every affected cell to its value before the gesture.</summary>
    public void Undo(Tilemap2DAsset tilemap) => Apply(tilemap, useAfter: false);

    /// <summary>Reapplies every affected cell's value after the gesture.</summary>
    public void Redo(Tilemap2DAsset tilemap) => Apply(tilemap, useAfter: true);

    private void Apply(Tilemap2DAsset tilemap, bool useAfter)
    {
        ArgumentNullException.ThrowIfNull(tilemap);
        var writes = new TilemapCellWrite2D[changes.Count];
        for (int index = 0; index < changes.Count; index++)
        {
            TilemapCellChange2D change = changes[index];
            TilemapCellSnapshot2D snapshot = useAfter ? change.after : change.before;
            writes[index] = snapshot.hasValue
                ? TilemapCellWrite2D.Set(change.layerId, WithPosition(snapshot.value, change.x, change.y))
                : TilemapCellWrite2D.Remove(change.layerId, change.x, change.y);
        }
        _ = tilemap.ApplyCells(writes);
    }

    internal static TilemapCell2D WithPosition(TilemapCell2D value, int x, int y)
    {
        value.x = x;
        value.y = y;
        return value;
    }
}

/// <summary>Provides deterministic Tile Palette operations that emit one compact change set per gesture.</summary>
public static class TilemapEditing2D
{
    /// <summary>Paints one cell.</summary>
    public static TilemapStroke2D Brush(
        Tilemap2DAsset tilemap,
        int layerId,
        TilemapPosition2D position,
        TilemapCell2D value)
        => PaintStroke(tilemap, layerId, [position], value);

    /// <summary>Paints a freehand sequence, coalescing repeated coordinates into one change.</summary>
    public static TilemapStroke2D PaintStroke(
        Tilemap2DAsset tilemap,
        int layerId,
        IEnumerable<TilemapPosition2D> positions,
        TilemapCell2D value)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var builder = new StrokeBuilder(tilemap);
        foreach (TilemapPosition2D position in positions)
            builder.Set(layerId, position.x, position.y, new TilemapCellSnapshot2D(true, value));
        return builder.Commit();
    }

    /// <summary>Erases one freehand sequence, coalescing repeated coordinates.</summary>
    public static TilemapStroke2D Erase(
        Tilemap2DAsset tilemap,
        int layerId,
        IEnumerable<TilemapPosition2D> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var builder = new StrokeBuilder(tilemap);
        foreach (TilemapPosition2D position in positions)
            builder.Set(layerId, position.x, position.y, default);
        return builder.Commit();
    }

    /// <summary>Paints every cell inside an inclusive box.</summary>
    public static TilemapStroke2D Box(
        Tilemap2DAsset tilemap,
        int layerId,
        TilemapSelection2D bounds,
        TilemapCell2D value)
    {
        var builder = new StrokeBuilder(tilemap);
        for (int y = bounds.minimumY; y <= bounds.maximumY; y++)
        {
            for (int x = bounds.minimumX; x <= bounds.maximumX; x++)
                builder.Set(layerId, x, y, new TilemapCellSnapshot2D(true, value));
        }
        return builder.Commit();
    }

    /// <summary>Flood-fills an exact connected value within finite inclusive bounds.</summary>
    public static TilemapStroke2D Fill(
        Tilemap2DAsset tilemap,
        int layerId,
        TilemapPosition2D origin,
        TilemapSelection2D bounds,
        TilemapCell2D value)
    {
        ArgumentNullException.ThrowIfNull(tilemap);
        if (!bounds.Contains(origin.x, origin.y))
            throw new ArgumentOutOfRangeException(nameof(origin), "Fill origin must be inside its finite bounds.");
        var builder = new StrokeBuilder(tilemap);
        TilemapCellSnapshot2D target = builder.Read(layerId, origin.x, origin.y);
        TilemapCellSnapshot2D replacement = new(true, value);
        if (Equivalent(target, replacement))
            return builder.Commit();

        var pending = new Queue<TilemapPosition2D>();
        var visited = new HashSet<TilemapPosition2D>();
        pending.Enqueue(origin);
        while (pending.Count != 0)
        {
            TilemapPosition2D position = pending.Dequeue();
            if (!bounds.Contains(position.x, position.y)
                || !visited.Add(position)
                || !Equivalent(builder.Read(layerId, position.x, position.y), target))
            {
                continue;
            }
            builder.Set(layerId, position.x, position.y, replacement);
            pending.Enqueue(new TilemapPosition2D(position.x - 1, position.y));
            pending.Enqueue(new TilemapPosition2D(position.x + 1, position.y));
            pending.Enqueue(new TilemapPosition2D(position.x, position.y - 1));
            pending.Enqueue(new TilemapPosition2D(position.x, position.y + 1));
        }
        return builder.Commit();
    }

    /// <summary>Returns occupied cells inside an inclusive selection in stable Y then X order.</summary>
    public static IReadOnlyList<TilemapCell2D> Select(
        Tilemap2DAsset tilemap,
        int layerId,
        TilemapSelection2D bounds)
    {
        ArgumentNullException.ThrowIfNull(tilemap);
        return tilemap.chunks
            .Where(chunk => chunk.layerId == layerId)
            .SelectMany(static chunk => chunk.cells ?? [])
            .Where(cell => bounds.Contains(cell.x, cell.y))
            .OrderBy(static cell => cell.y)
            .ThenBy(static cell => cell.x)
            .ToArray();
    }

    /// <summary>Moves occupied cells in a selection by an integer offset as one overlap-safe gesture.</summary>
    public static TilemapStroke2D Move(
        Tilemap2DAsset tilemap,
        int layerId,
        TilemapSelection2D selection,
        int offsetX,
        int offsetY)
    {
        IReadOnlyList<TilemapCell2D> selected = Select(tilemap, layerId, selection);
        var builder = new StrokeBuilder(tilemap);
        for (int index = 0; index < selected.Count; index++)
            builder.Set(layerId, selected[index].x, selected[index].y, default);
        for (int index = 0; index < selected.Count; index++)
        {
            TilemapCell2D cell = selected[index];
            builder.Set(layerId, cell.x + offsetX, cell.y + offsetY, new TilemapCellSnapshot2D(true, cell));
        }
        return builder.Commit();
    }

    /// <summary>Places a reusable sparse stamp at an integer origin.</summary>
    public static TilemapStroke2D Stamp(
        Tilemap2DAsset tilemap,
        int layerId,
        TilemapPosition2D origin,
        IEnumerable<TilemapStampCell2D> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var builder = new StrokeBuilder(tilemap);
        foreach (TilemapStampCell2D cell in cells)
        {
            builder.Set(
                layerId,
                origin.x + cell.x,
                origin.y + cell.y,
                new TilemapCellSnapshot2D(true, cell.value));
        }
        return builder.Commit();
    }

    private static bool Equivalent(TilemapCellSnapshot2D left, TilemapCellSnapshot2D right)
        => left.hasValue == right.hasValue
           && (!left.hasValue || EqualityComparer<TilemapCell2D>.Default.Equals(left.value, right.value));

    private sealed class StrokeBuilder
    {
        private readonly Tilemap2DAsset m_tilemap;
        private readonly Dictionary<CellKey, MutableChange> m_changes = [];

        internal StrokeBuilder(Tilemap2DAsset tilemap)
            => m_tilemap = tilemap ?? throw new ArgumentNullException(nameof(tilemap));

        internal TilemapCellSnapshot2D Read(int layerId, int x, int y)
        {
            var key = new CellKey(layerId, x, y);
            if (m_changes.TryGetValue(key, out MutableChange? change))
                return change.after;
            return m_tilemap.TryGetCell(x, y, layerId, out TilemapCell2D value)
                ? new TilemapCellSnapshot2D(true, value)
                : default;
        }

        internal void Set(int layerId, int x, int y, TilemapCellSnapshot2D after)
        {
            var key = new CellKey(layerId, x, y);
            if (!m_changes.TryGetValue(key, out MutableChange? change))
            {
                TilemapCellSnapshot2D before = m_tilemap.TryGetCell(x, y, layerId, out TilemapCell2D value)
                    ? new TilemapCellSnapshot2D(true, value)
                    : default;
                change = new MutableChange(before, after);
                m_changes.Add(key, change);
            }
            else
            {
                change.after = after;
            }
            if (Equivalent(change.before, change.after))
                m_changes.Remove(key);
        }

        internal TilemapStroke2D Commit()
        {
            TilemapCellChange2D[] changes = m_changes
                .Select(static pair => new TilemapCellChange2D(
                    pair.Key.layerId,
                    pair.Key.x,
                    pair.Key.y,
                    pair.Value.before,
                    pair.Value.after))
                .OrderBy(static change => change.layerId)
                .ThenBy(static change => change.y)
                .ThenBy(static change => change.x)
                .ToArray();
            var writes = new TilemapCellWrite2D[changes.Length];
            for (int index = 0; index < changes.Length; index++)
            {
                TilemapCellChange2D change = changes[index];
                writes[index] = change.after.hasValue
                    ? TilemapCellWrite2D.Set(
                        change.layerId,
                        TilemapStroke2D.WithPosition(change.after.value, change.x, change.y))
                    : TilemapCellWrite2D.Remove(change.layerId, change.x, change.y);
            }
            _ = m_tilemap.ApplyCells(writes);
            return new TilemapStroke2D(changes);
        }
    }

    private sealed class MutableChange(TilemapCellSnapshot2D before, TilemapCellSnapshot2D after)
    {
        internal TilemapCellSnapshot2D before { get; } = before;
        internal TilemapCellSnapshot2D after { get; set; } = after;
    }

    private readonly record struct CellKey(int layerId, int x, int y);
}

/// <summary>Encodes and records reload-safe tilemap gestures in the shared Editor history.</summary>
public static class TilemapHistory2D
{
    /// <summary>Gets the stable history protocol handled by the 2D Plugin.</summary>
    public const string kind = "inno.rendering2d.tilemap-stroke.v1";

    /// <summary>Records an already applied gesture as exactly one Editor history transaction.</summary>
    public static void RecordApplied(
        IEditorHistory history,
        string name,
        AssetPath path,
        Tilemap2DAsset tilemap,
        TilemapStroke2D stroke)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tilemap);
        ArgumentNullException.ThrowIfNull(stroke);
        if (!stroke.hasChanges)
            return;
        using EditorHistoryTransaction transaction = history.BeginTransaction(name);
        history.RecordApplied(
            name,
            new EditorHistoryChange(
                kind,
                EditorHistoryPayload.FromBytes(Encode(path, tilemap.identity.persistentId, stroke))));
        transaction.Commit();
    }

    internal static byte[] Encode(AssetPath path, Guid assetId, TilemapStroke2D stroke)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1);
        writer.Write(assetId.ToByteArray());
        writer.Write(path.ToString());
        writer.Write(stroke.changes.Count);
        for (int index = 0; index < stroke.changes.Count; index++)
        {
            TilemapCellChange2D change = stroke.changes[index];
            writer.Write(change.layerId);
            writer.Write(change.x);
            writer.Write(change.y);
            WriteSnapshot(writer, change.before);
            WriteSnapshot(writer, change.after);
        }
        writer.Flush();
        return stream.ToArray();
    }

    internal static (Guid assetId, string path, TilemapStroke2D stroke) Decode(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadInt32() != 1)
            throw new InvalidDataException("Unsupported tilemap history payload version.");
        Guid assetId = new(reader.ReadBytes(16));
        string path = reader.ReadString();
        int count = reader.ReadInt32();
        if (count < 0 || count > 16_777_216)
            throw new InvalidDataException("Tilemap history cell count is invalid.");
        var changes = new TilemapCellChange2D[count];
        for (int index = 0; index < count; index++)
        {
            int layerId = reader.ReadInt32();
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            changes[index] = new TilemapCellChange2D(
                layerId,
                x,
                y,
                ReadSnapshot(reader, x, y),
                ReadSnapshot(reader, x, y));
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Tilemap history payload contains trailing data.");
        return (assetId, path, new TilemapStroke2D(changes));
    }

    private static void WriteSnapshot(BinaryWriter writer, TilemapCellSnapshot2D snapshot)
    {
        writer.Write(snapshot.hasValue);
        if (!snapshot.hasValue)
            return;
        TilemapCell2D cell = snapshot.value;
        writer.Write(cell.tileId);
        writer.Write(cell.color.r);
        writer.Write(cell.color.g);
        writer.Write(cell.color.b);
        writer.Write(cell.color.a);
        writer.Write(cell.flipX);
        writer.Write(cell.flipY);
        writer.Write(cell.quarterTurns);
    }

    private static TilemapCellSnapshot2D ReadSnapshot(BinaryReader reader, int x, int y)
    {
        if (!reader.ReadBoolean())
            return default;
        return new TilemapCellSnapshot2D(true, new TilemapCell2D
        {
            x = x,
            y = y,
            tileId = reader.ReadInt32(),
            color = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            flipX = reader.ReadBoolean(),
            flipY = reader.ReadBoolean(),
            quarterTurns = reader.ReadInt32()
        });
    }
}

/// <summary>Applies the current generation of the compact tilemap stroke protocol.</summary>
[EditorHistoryHandler(TilemapHistory2D.kind)]
public sealed class TilemapHistoryHandler2D : EditorHistoryHandler
{
    /// <inheritdoc />
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            (Guid assetId, string path, _) = TilemapHistory2D.Decode(change.payload.ReadBytes());
            return TryLoad(assetId, path, out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Tilemap '{path}' is not available.");
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or IOException)
        {
            return EditorHistoryAvailability.Unavailable(exception.Message);
        }
    }

    /// <inheritdoc />
    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            (Guid assetId, string path, TilemapStroke2D stroke) = TilemapHistory2D.Decode(change.payload.ReadBytes());
            if (!TryLoad(assetId, path, out Tilemap2DAsset? tilemap) || tilemap is null)
                return EditorHistoryResult.Failure($"Tilemap '{path}' is not available.");
            if (direction == EditorHistoryDirection.Undo)
                stroke.Undo(tilemap);
            else
                stroke.Redo(tilemap);
            foreach (EditorDocumentContext document in context.interactions.documents.documents)
            {
                if (document.assetId == assetId
                    || string.Equals(document.assetPath, path, StringComparison.Ordinal))
                {
                    context.interactions.documents.SetDirty(document.documentId);
                }
            }
            return EditorHistoryResult.Success();
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or IOException)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    private static bool TryLoad(Guid assetId, string path, out Tilemap2DAsset? tilemap)
    {
        if (assetId != Guid.Empty
            && Assets.TryLoad(assetId, out Tilemap2DAsset? identified)
            && identified is not null)
        {
            tilemap = identified;
            return true;
        }
        if (Assets.TryLoad(AssetPath.Parse(path), out Tilemap2DAsset? located)
            && located is not null)
        {
            tilemap = located;
            return true;
        }
        tilemap = null;
        return false;
    }
}
