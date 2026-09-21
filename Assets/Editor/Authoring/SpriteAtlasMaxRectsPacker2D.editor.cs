using System;
using System.Collections.Generic;
using System.Linq;
using InnoEngine.Mathematics;

namespace Inno.Rendering2D;

/// <summary>Describes one stable pixel rectangle supplied to deterministic atlas packing.</summary>
public readonly record struct SpriteAtlasPackInput2D
{
    /// <summary>Creates one atlas packing input.</summary>
    /// <param name="id">Unique stable source identity.</param>
    /// <param name="width">Positive source width in pixels.</param>
    /// <param name="height">Positive source height in pixels.</param>
    public SpriteAtlasPackInput2D(string id, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        this.id = id;
        this.width = width;
        this.height = height;
    }

    /// <summary>Gets the stable source identity.</summary>
    public string id { get; }

    /// <summary>Gets the source width in pixels.</summary>
    public int width { get; }

    /// <summary>Gets the source height in pixels.</summary>
    public int height { get; }
}

/// <summary>Describes one deterministic packed source placement.</summary>
public readonly record struct SpriteAtlasPlacement2D(
    string inputId,
    int pageIndex,
    Rect pixelRect,
    bool rotatedClockwise);

/// <summary>Describes one allocated atlas page and its occupied pixel extent.</summary>
public readonly record struct SpriteAtlasPackPage2D(
    int width,
    int height,
    int occupiedWidth,
    int occupiedHeight);

/// <summary>Contains immutable pages and placements emitted by atlas packing.</summary>
public sealed class SpriteAtlasPackResult2D
{
    internal SpriteAtlasPackResult2D(
        SpriteAtlasPackPage2D[] pages,
        SpriteAtlasPlacement2D[] placements)
    {
        this.pages = pages;
        this.placements = placements;
    }

    /// <summary>Gets pages in allocation order.</summary>
    public IReadOnlyList<SpriteAtlasPackPage2D> pages { get; }

    /// <summary>Gets placements in stable input identity order.</summary>
    public IReadOnlyList<SpriteAtlasPlacement2D> placements { get; }
}

/// <summary>Packs source rectangles with a deterministic best-short-side-fit MaxRects policy.</summary>
public static class SpriteAtlasMaxRectsPacker2D
{
    /// <summary>Packs a complete source set into the minimum pages found by the deterministic heuristic.</summary>
    /// <param name="inputs">Complete unique source rectangles.</param>
    /// <param name="settings">Page limits, padding, extrusion, and rotation policy.</param>
    /// <returns>Allocated pages and stable placements.</returns>
    /// <exception cref="ArgumentException">Thrown when inputs or settings are invalid.</exception>
    public static SpriteAtlasPackResult2D Pack(
        IEnumerable<SpriteAtlasPackInput2D> inputs,
        SpriteAtlasPackingSettings2D settings)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (settings.maximumWidth <= 0 || settings.maximumHeight <= 0
            || settings.padding < 0 || settings.extrude < 0)
        {
            throw new ArgumentException("Atlas dimensions must be positive and spacing cannot be negative.", nameof(settings));
        }
        SpriteAtlasPackInput2D[] ordered = inputs
            .OrderByDescending(static input => Math.Max(input.width, input.height))
            .ThenByDescending(static input => (long)input.width * input.height)
            .ThenBy(static input => input.id, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Select(static input => input.id).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new ArgumentException("Atlas packing input IDs must be unique.", nameof(inputs));

        int margin = checked(settings.padding + settings.extrude);
        var pages = new List<Page>();
        var placements = new List<SpriteAtlasPlacement2D>(ordered.Length);
        foreach (SpriteAtlasPackInput2D input in ordered)
        {
            int packedWidth = checked(input.width + margin * 2);
            int packedHeight = checked(input.height + margin * 2);
            if (!FitsPage(packedWidth, packedHeight, settings))
            {
                throw new ArgumentException(
                    $"Atlas input '{input.id}' does not fit within the configured page dimensions.",
                    nameof(inputs));
            }

            Candidate best = default;
            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                Candidate candidate = FindCandidate(
                    pages[pageIndex],
                    pageIndex,
                    packedWidth,
                    packedHeight,
                    settings.allowRotation);
                if (candidate.isValid && (!best.isValid || candidate.CompareTo(best) < 0))
                    best = candidate;
            }
            if (!best.isValid)
            {
                pages.Add(new Page(settings.maximumWidth, settings.maximumHeight));
                best = FindCandidate(
                    pages[^1],
                    pages.Count - 1,
                    packedWidth,
                    packedHeight,
                    settings.allowRotation);
            }

            Page page = pages[best.pageIndex];
            IntRect occupied = new(best.x, best.y, best.width, best.height);
            page.Place(occupied);
            int contentWidth = best.rotated ? input.height : input.width;
            int contentHeight = best.rotated ? input.width : input.height;
            placements.Add(new SpriteAtlasPlacement2D(
                input.id,
                best.pageIndex,
                new Rect(best.x + margin, best.y + margin, contentWidth, contentHeight),
                best.rotated));
        }

        SpriteAtlasPackPage2D[] packedPages = pages
            .Select(static page => new SpriteAtlasPackPage2D(
                page.width,
                page.height,
                page.occupiedWidth,
                page.occupiedHeight))
            .ToArray();
        return new SpriteAtlasPackResult2D(
            packedPages,
            placements.OrderBy(static placement => placement.inputId, StringComparer.Ordinal).ToArray());
    }

    private static bool FitsPage(
        int width,
        int height,
        SpriteAtlasPackingSettings2D settings)
        => width <= settings.maximumWidth && height <= settings.maximumHeight
           || settings.allowRotation
           && height <= settings.maximumWidth && width <= settings.maximumHeight;

    private static Candidate FindCandidate(
        Page page,
        int pageIndex,
        int width,
        int height,
        bool allowRotation)
    {
        Candidate best = default;
        for (int index = 0; index < page.free.Count; index++)
        {
            IntRect free = page.free[index];
            Consider(free, pageIndex, width, height, false, ref best);
            if (allowRotation && width != height)
                Consider(free, pageIndex, height, width, true, ref best);
        }
        return best;
    }

    private static void Consider(
        IntRect free,
        int pageIndex,
        int width,
        int height,
        bool rotated,
        ref Candidate best)
    {
        if (width > free.width || height > free.height)
            return;
        int leftoverHorizontal = free.width - width;
        int leftoverVertical = free.height - height;
        var candidate = new Candidate(
            true,
            pageIndex,
            free.x,
            free.y,
            width,
            height,
            rotated,
            Math.Min(leftoverHorizontal, leftoverVertical),
            Math.Max(leftoverHorizontal, leftoverVertical));
        if (!best.isValid || candidate.CompareTo(best) < 0)
            best = candidate;
    }

    private sealed class Page
    {
        internal Page(int width, int height)
        {
            this.width = width;
            this.height = height;
            free.Add(new IntRect(0, 0, width, height));
        }

        internal int width { get; }
        internal int height { get; }
        internal int occupiedWidth { get; private set; }
        internal int occupiedHeight { get; private set; }
        internal List<IntRect> free { get; } = [];

        internal void Place(IntRect used)
        {
            for (int index = free.Count - 1; index >= 0; index--)
            {
                IntRect available = free[index];
                if (!available.Overlaps(used))
                    continue;
                free.RemoveAt(index);
                AddRemainders(available, used);
            }
            Prune();
            free.Sort(static (left, right) => left.CompareTo(right));
            occupiedWidth = Math.Max(occupiedWidth, used.right);
            occupiedHeight = Math.Max(occupiedHeight, used.bottom);
        }

        private void AddRemainders(IntRect available, IntRect used)
        {
            if (used.x > available.x)
                free.Add(new IntRect(available.x, available.y, used.x - available.x, available.height));
            if (used.right < available.right)
                free.Add(new IntRect(used.right, available.y, available.right - used.right, available.height));
            if (used.y > available.y)
                free.Add(new IntRect(available.x, available.y, available.width, used.y - available.y));
            if (used.bottom < available.bottom)
                free.Add(new IntRect(available.x, used.bottom, available.width, available.bottom - used.bottom));
        }

        private void Prune()
        {
            for (int first = free.Count - 1; first >= 0; first--)
            {
                bool removed = false;
                for (int second = free.Count - 1; second >= 0; second--)
                {
                    if (first == second || !free[second].Contains(free[first]))
                        continue;
                    free.RemoveAt(first);
                    removed = true;
                    break;
                }
                if (removed)
                    continue;
            }
        }
    }

    private readonly record struct IntRect(int x, int y, int width, int height) : IComparable<IntRect>
    {
        internal int right => x + width;
        internal int bottom => y + height;

        internal bool Overlaps(IntRect other)
            => x < other.right && right > other.x && y < other.bottom && bottom > other.y;

        internal bool Contains(IntRect other)
            => other.x >= x && other.y >= y && other.right <= right && other.bottom <= bottom;

        public int CompareTo(IntRect other)
        {
            int result = y.CompareTo(other.y);
            if (result != 0)
                return result;
            result = x.CompareTo(other.x);
            if (result != 0)
                return result;
            result = width.CompareTo(other.width);
            return result != 0 ? result : height.CompareTo(other.height);
        }
    }

    private readonly record struct Candidate(
        bool isValid,
        int pageIndex,
        int x,
        int y,
        int width,
        int height,
        bool rotated,
        int shortSide,
        int longSide) : IComparable<Candidate>
    {
        public int CompareTo(Candidate other)
        {
            int result = shortSide.CompareTo(other.shortSide);
            if (result != 0)
                return result;
            result = longSide.CompareTo(other.longSide);
            if (result != 0)
                return result;
            result = pageIndex.CompareTo(other.pageIndex);
            if (result != 0)
                return result;
            result = y.CompareTo(other.y);
            if (result != 0)
                return result;
            result = x.CompareTo(other.x);
            if (result != 0)
                return result;
            return rotated.CompareTo(other.rotated);
        }
    }
}
