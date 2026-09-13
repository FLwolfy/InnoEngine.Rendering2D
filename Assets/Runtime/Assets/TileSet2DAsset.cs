using System;
using System.Collections.Generic;
using System.Linq;
using InnoEngine.Assets;
using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Selects how one neighboring cell participates in a rule-tile match.</summary>
public enum TileNeighborRule2D
{
    /// <summary>The neighbor is ignored.</summary>
    Any,
    /// <summary>The neighbor must contain the same tile identity.</summary>
    Same,
    /// <summary>The neighbor must contain another tile identity or be empty.</summary>
    Different,
    /// <summary>The neighbor must be empty.</summary>
    Empty,
    /// <summary>The neighbor must contain any tile.</summary>
    Occupied
}

/// <summary>Defines one relative condition in a deterministic rule-tile pattern.</summary>
public struct TileNeighborCondition2D
{
    /// <summary>Gets or sets the horizontal cell offset.</summary>
    public int x { get; set; }

    /// <summary>Gets or sets the vertical cell offset.</summary>
    public int y { get; set; }

    /// <summary>Gets or sets the required neighbor relationship.</summary>
    public TileNeighborRule2D rule { get; set; }
}

/// <summary>Defines a rule-tile visual selected from neighboring cell occupancy.</summary>
public struct TileVisualRule2D
{
    /// <summary>Gets or sets descending selection priority.</summary>
    public int priority { get; set; }

    /// <summary>Gets or sets the visual used when every condition matches.</summary>
    public SpriteReference2D sprite { get; set; }

    /// <summary>Gets or sets all relative neighbor conditions.</summary>
    public TileNeighborCondition2D[] conditions { get; set; }
}

/// <summary>Defines one frame in an animated tile visual.</summary>
public struct TileAnimationFrame2D
{
    /// <summary>Gets or sets the frame sprite.</summary>
    public SpriteReference2D sprite { get; set; }

    /// <summary>Gets or sets positive frame duration in seconds.</summary>
    public float duration { get; set; }
}

/// <summary>Maps one stable tile identity to visuals and open gameplay metadata.</summary>
public struct TileDefinition2D
{
    /// <summary>Gets or sets the non-negative tile identity.</summary>
    public int id { get; set; }

    /// <summary>Gets or sets the default stable sprite reference.</summary>
    public SpriteReference2D sprite { get; set; }

    /// <summary>Gets or sets ordered animated visual frames.</summary>
    public TileAnimationFrame2D[] animation { get; set; }

    /// <summary>Gets or sets deterministic rule-tile visuals.</summary>
    public TileVisualRule2D[] rules { get; set; }

    /// <summary>Gets or sets a linear per-tile tint.</summary>
    public Color color { get; set; }

    /// <summary>Gets or sets open gameplay metadata without engine interpretation.</summary>
    public string metadata { get; set; }
}

/// <summary>Stores stable ordinary, animated, and rule-tile definitions.</summary>
[StableTypeId("193daaa8-f8d5-47aa-a771-f5f58bb880c2")]
public sealed class TileSet2DAsset : AssetObject
{
    private TileDefinition2D[] m_tiles = [];

    /// <summary>Gets or sets tile definitions in stable identity order.</summary>
    [SerializableProperty]
    public TileDefinition2D[] tiles
    {
        get => m_tiles;
        set => m_tiles = value?.OrderBy(static candidate => candidate.id).ToArray() ?? [];
    }

    /// <summary>Tries to resolve a tile by stable identity.</summary>
    /// <param name="id">Tile identity.</param>
    /// <param name="tile">Receives the matching tile definition.</param>
    /// <returns><see langword="true"/> when a tile is defined.</returns>
    public bool TryGetTile(int id, out TileDefinition2D tile)
    {
        int low = 0;
        int high = m_tiles.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            int comparison = m_tiles[middle].id.CompareTo(id);
            if (comparison == 0)
            {
                tile = m_tiles[middle];
                return true;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        tile = default;
        return false;
    }

    /// <summary>Replaces tile definitions after validating identities and visual data.</summary>
    /// <param name="tiles">Complete tile definition set.</param>
    /// <exception cref="ArgumentException">Thrown when an identity, visual, animation, or rule is invalid.</exception>
    public void SetTiles(IEnumerable<TileDefinition2D> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        TileDefinition2D[] values = tiles.ToArray();
        if (values.Any(static value => value.id < 0 || !HasVisual(value)))
            throw new ArgumentException("Tiles require non-negative IDs and at least one assigned visual.", nameof(tiles));
        if (values.Select(static value => value.id).Distinct().Count() != values.Length)
            throw new ArgumentException("Tile IDs must be unique.", nameof(tiles));
        if (values.Any(static value => value.animation is not null
            && value.animation.Any(static frame => !frame.sprite.isAssigned || frame.duration <= 0f)))
        {
            throw new ArgumentException("Animated tile frames require assigned sprites and positive durations.", nameof(tiles));
        }
        if (values.Any(static value => value.rules is not null
            && value.rules.Any(static rule => !rule.sprite.isAssigned)))
        {
            throw new ArgumentException("Rule-tile results require assigned sprites.", nameof(tiles));
        }
        m_tiles = values.OrderBy(static value => value.id).ToArray();
    }

    private static bool HasVisual(TileDefinition2D tile)
        => tile.sprite.isAssigned
           || tile.animation is { Length: > 0 }
           || tile.rules is { Length: > 0 };
}
