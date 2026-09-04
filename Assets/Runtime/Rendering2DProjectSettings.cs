using System;
using System.Collections.Generic;
using System.Linq;

using InnoEngine.Reflection;
using InnoEngine.Serialization;
using InnoEngine.Settings;

namespace Inno.Rendering2D;

/// <summary>Defines one project-local 2D sorting layer.</summary>
public struct SortingLayer2DDefinition
{
    [SerializableProperty]
    private string m_localId;

    /// <summary>Creates a sorting layer and derives its stable local identity from the name.</summary>
    /// <param name="name">The user-facing layer name.</param>
    /// <param name="order">The global sorting order.</param>
    public SortingLayer2DDefinition(string name, int order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        m_localId = CreateLocalId(name);
        this.name = name.Trim();
        this.order = order;
    }

    /// <summary>Gets the stable project-independent identity.</summary>
    public string localId => m_localId ?? string.Empty;

    /// <summary>Gets or sets the user-facing layer name.</summary>
    [SerializableProperty]
    public string name { get; set; }

    /// <summary>Gets or sets the global sorting order.</summary>
    [SerializableProperty]
    public int order { get; set; }

    /// <summary>Creates an edited copy while preserving the generated local identity.</summary>
    /// <param name="name">The new display name.</param>
    /// <param name="order">The new global order.</param>
    /// <returns>The edited definition.</returns>
    public SortingLayer2DDefinition With(string name, int order)
        => new(m_localId, name, order, preserveLocalId: true);

    internal void Validate()
    {
        _ = new SortingLayer2DDefinition(m_localId, name, order, preserveLocalId: true);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Sorting-layer names must be trimmed.", nameof(name));
    }

    private SortingLayer2DDefinition(string localId, string name, int order, bool preserveLocalId)
    {
        _ = preserveLocalId;
        ArgumentException.ThrowIfNullOrWhiteSpace(localId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidateLocalId(localId);
        m_localId = localId;
        this.name = name.Trim();
        this.order = order;
    }

    private static string CreateLocalId(string name)
    {
        string normalized = name.Trim().ToLowerInvariant();
        var characters = new List<char>(normalized.Length);
        bool separator = false;
        foreach (char character in normalized)
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (separator && characters.Count > 0)
                    characters.Add('-');
                characters.Add(character);
                separator = false;
            }
            else if (character is ' ' or '\t' or '.' or '-' or '_')
            {
                separator = characters.Count > 0;
            }
            else
            {
                throw new ArgumentException(
                    "A sorting-layer name must use ASCII letters, digits, spaces, dots, hyphens, or underscores.",
                    nameof(name));
            }
        }
        if (characters.Count == 0)
            throw new ArgumentException("A sorting-layer name must contain a letter or digit.", nameof(name));
        string result = new(characters.ToArray());
        ValidateLocalId(result);
        return result;
    }

    private static void ValidateLocalId(string value)
    {
        if (value.Length > 128)
            throw new ArgumentException("A sorting-layer local identity cannot exceed 128 characters.", nameof(value));
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
                throw new ArgumentException("A sorting-layer local identity is not portable.", nameof(value));
        }
        if (value[0] == '-' || value[^1] == '-')
            throw new ArgumentException("A sorting-layer local identity must begin and end with a letter or digit.", nameof(value));
    }
}

/// <summary>Stores project-wide limits and ordering definitions for the 2D renderer.</summary>
[StableTypeId("26a75fc8-54bc-4558-9e12-a8035b9176d6")]
[ProjectSettingDefinition("inno.rendering.2d")]
public sealed class Rendering2DProjectSettings : ISerializable
{
    private SortingLayer2DDefinition[] m_sortingLayers =
    [
        new SortingLayer2DDefinition("Default", 0)
    ];

    /// <summary>Gets the stable project setting protocol identity.</summary>
    public static ProjectSettingId id => new("inno.rendering.2d");

    /// <summary>Gets or sets the project-wide pixels represented by one world unit.</summary>
    [SerializableProperty]
    public float defaultPixelsPerUnit { get; set; } = 100f;

    /// <summary>Gets or sets the maximum generated quads accepted in one request.</summary>
    [SerializableProperty]
    public int maximumQuadsPerFrame { get; set; } = 262_144;

    /// <summary>Gets or sets the maximum adjacent quads grouped into one draw batch.</summary>
    [SerializableProperty]
    public int maximumQuadsPerBatch { get; set; } = 16_384;

    /// <summary>Gets or sets the maximum quads generated by one tiled sprite.</summary>
    [SerializableProperty]
    public int maximumTiledSpriteQuads { get; set; } = 4_096;

    /// <summary>Gets or sets stable sorting layers in arbitrary authoring order.</summary>
    [SerializableProperty]
    public SortingLayer2DDefinition[] sortingLayers
    {
        get => m_sortingLayers;
        set => SetSortingLayers(value ?? []);
    }

    /// <summary>Gets the effective order of a project-local sorting layer.</summary>
    /// <param name="localId">The stable project-independent identity.</param>
    /// <returns>The configured order, or zero when the layer is undefined.</returns>
    public int GetSortingLayerOrder(string localId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localId);
        string normalized = localId.Trim();
        foreach (SortingLayer2DDefinition candidate in m_sortingLayers)
        {
            if (string.Equals(candidate.localId, normalized, StringComparison.Ordinal))
                return candidate.order;
        }
        return 0;
    }

    /// <summary>Gets a sorting layer definition by its display name.</summary>
    /// <param name="name">The user-facing name.</param>
    /// <returns>The matching definition.</returns>
    public SortingLayer2DDefinition GetSortingLayer(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (SortingLayer2DDefinition candidate in m_sortingLayers)
        {
            if (string.Equals(candidate.name, name.Trim(), StringComparison.Ordinal))
                return candidate;
        }
        throw new KeyNotFoundException($"Sorting layer '{name}' is not defined.");
    }

    /// <summary>Replaces all sorting layers after validating automatic local identities.</summary>
    /// <param name="definitions">The complete sorting-layer set.</param>
    public void SetSortingLayers(IEnumerable<SortingLayer2DDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        SortingLayer2DDefinition[] values = definitions.ToArray();
        foreach (SortingLayer2DDefinition value in values)
            value.Validate();
        if (values.Select(static value => value.localId).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Sorting-layer local identities must be unique.", nameof(definitions));
        if (values.Select(static value => value.name).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Sorting-layer names must be unique.", nameof(definitions));
        if (values.Length == 0 || !string.Equals(values[0].localId, "default", StringComparison.Ordinal))
            throw new ArgumentException("The built-in Default sorting layer must remain first.", nameof(definitions));
        m_sortingLayers = values;
    }
}
