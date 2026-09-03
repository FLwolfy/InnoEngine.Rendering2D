using System;
using System.Collections.Generic;
using System.Linq;
using InnoEngine.Assets;
using InnoEngine.Mathematics;
using InnoEngine.Reflection;
using InnoEngine.Serialization;

namespace Inno.Rendering2D;

/// <summary>Maps one stable tile identity to an atlas region and semantic metadata.</summary>
public struct TileDefinition2D
{
    /// <summary>Gets or sets the non-negative tile identity.</summary>
    public int id { get; set; }

    /// <summary>Gets or sets the atlas region identity.</summary>
    public string spriteId { get; set; }

    /// <summary>Gets or sets a linear per-tile tint.</summary>
    public Color color { get; set; }

    /// <summary>Gets or sets whether gameplay treats the tile as collidable.</summary>
    public bool collidable { get; set; }

    /// <summary>Gets or sets open gameplay metadata without engine interpretation.</summary>
    public string metadata { get; set; }
}

/// <summary>Stores the atlas-backed visual and gameplay definition of stable tile IDs.</summary>
[StableTypeId("193daaa8-f8d5-47aa-a771-f5f58bb880c2")]
public sealed class TileSet2DAsset : AssetObject
{
    private TileDefinition2D[] m_tiles = [];

    /// <summary>Gets or sets the atlas containing every tile region.</summary>
    [SerializableProperty]
    public SpriteAtlas2DAsset? atlas { get; set; }

    /// <summary>Gets or sets tile definitions.</summary>
    [SerializableProperty]
    public TileDefinition2D[] tiles
    {
        get => m_tiles;
        set => m_tiles = value?.ToArray() ?? [];
    }

    /// <summary>Tries to resolve a stable tile identity.</summary>
    /// <param name="id">Tile identity.</param>
    /// <param name="tile">Receives the tile definition.</param>
    /// <returns><see langword="true"/> when a tile is defined.</returns>
    public bool TryGetTile(int id, out TileDefinition2D tile)
    {
        for (int index = 0; index < m_tiles.Length; index++)
        {
            if (m_tiles[index].id != id)
                continue;
            tile = m_tiles[index];
            return true;
        }
        tile = default;
        return false;
    }

    /// <summary>Replaces tile definitions after validating stable identities.</summary>
    /// <param name="tiles">Complete tile definition set.</param>
    /// <exception cref="ArgumentException">Thrown when an identity is negative or duplicated.</exception>
    public void SetTiles(IEnumerable<TileDefinition2D> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        TileDefinition2D[] values = tiles.ToArray();
        if (values.Any(static value => value.id < 0 || string.IsNullOrWhiteSpace(value.spriteId)))
            throw new ArgumentException("Tiles require non-negative IDs and atlas region IDs.", nameof(tiles));
        if (values.Select(static value => value.id).Distinct().Count() != values.Length)
            throw new ArgumentException("Tile IDs must be unique.", nameof(tiles));
        m_tiles = values.OrderBy(static value => value.id).ToArray();
    }
}
