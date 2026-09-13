using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using InnoEngine.Mathematics;
using InnoEngine.Rendering;

namespace Inno.Rendering2D;

/// <summary>Contains one composed atlas page and its portable color and optional lighting-map artifacts.</summary>
public sealed record SpriteAtlasComposedPage2D(
    SpriteAtlasPage2D page,
    byte[] colorPngBytes,
    byte[]? normalPngBytes,
    byte[]? emissionPngBytes);

/// <summary>Contains generated pages and stable runtime regions for one deterministic atlas import.</summary>
public sealed record SpriteAtlasCompositionResult2D(
    IReadOnlyList<SpriteAtlasComposedPage2D> pages,
    IReadOnlyList<SpriteRegion2D> regions);

/// <summary>Trims and composes authored PNG or TGA slices into deterministic multi-page atlas artifacts.</summary>
public static class SpriteAtlasComposer2D
{
    /// <summary>Composes a complete atlas without retaining decoder or runtime GPU objects.</summary>
    /// <param name="atlas">Authored atlas settings, sources, and optional explicit slices.</param>
    /// <param name="readSource">Reads immutable bytes for one source texture.</param>
    /// <returns>Generated page metadata, PNG bytes, and stable regions.</returns>
    public static SpriteAtlasCompositionResult2D Compose(
        SpriteAtlas2DAsset atlas,
        Func<TextureAsset, ReadOnlyMemory<byte>> readSource)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(readSource);
        if (atlas.sources.Length == 0)
            return new SpriteAtlasCompositionResult2D([], []);

        var rasters = new AtlasRaster2D[atlas.sources.Length];
        bool hasNormalMaps = atlas.sources.Any(static source => source.normalMap is not null);
        bool hasEmissionMaps = atlas.sources.Any(static source => source.emissionMap is not null);
        AtlasRaster2D[]? normalRasters = hasNormalMaps ? new AtlasRaster2D[atlas.sources.Length] : null;
        AtlasRaster2D[]? emissionRasters = hasEmissionMaps ? new AtlasRaster2D[atlas.sources.Length] : null;
        for (int sourceIndex = 0; sourceIndex < atlas.sources.Length; sourceIndex++)
        {
            SpriteAtlasSource2D source = atlas.sources[sourceIndex];
            TextureAsset texture = source.texture
                ?? throw new InvalidOperationException($"Atlas source {sourceIndex} has no texture.");
            rasters[sourceIndex] = AtlasImageCodec2D.Decode(readSource(texture).Span, texture.sourceFormat);
            if (rasters[sourceIndex].width != texture.width || rasters[sourceIndex].height != texture.height)
            {
                throw new InvalidDataException(
                    $"Atlas source '{texture.assetPath}' decoded dimensions do not match its imported metadata.");
            }
            if (normalRasters is not null)
            {
                normalRasters[sourceIndex] = DecodeCompanion(
                    source.normalMap,
                    texture,
                    readSource,
                    128,
                    128,
                    255,
                    "normal");
            }
            if (emissionRasters is not null)
            {
                emissionRasters[sourceIndex] = DecodeCompanion(
                    source.emissionMap,
                    texture,
                    readSource,
                    0,
                    0,
                    0,
                    "emission");
            }
        }

        SpriteAtlasSlice2D[] slices = atlas.slices.Length == 0
            ? CreateWholeSourceSlices(atlas.sources, rasters)
            : atlas.slices.ToArray();
        ValidateSlices(slices, rasters);
        TrimmedSlice[] trimmed = slices
            .Select(slice => Trim(slice, rasters[slice.sourceIndex], atlas.packing.trimTransparent))
            .ToArray();
        SpriteAtlasPackResult2D packing = SpriteAtlasMaxRectsPacker2D.Pack(
            trimmed.Select(static value => new SpriteAtlasPackInput2D(
                value.slice.id.value,
                value.width,
                value.height)),
            atlas.packing);
        var placementById = packing.placements.ToDictionary(
            static placement => placement.inputId,
            StringComparer.Ordinal);

        var pageRasters = new AtlasRaster2D[packing.pages.Count];
        AtlasRaster2D[]? pageNormalRasters = hasNormalMaps ? new AtlasRaster2D[packing.pages.Count] : null;
        AtlasRaster2D[]? pageEmissionRasters = hasEmissionMaps ? new AtlasRaster2D[packing.pages.Count] : null;
        var pageMetadata = new SpriteAtlasPage2D[packing.pages.Count];
        for (int pageIndex = 0; pageIndex < packing.pages.Count; pageIndex++)
        {
            SpriteAtlasPackPage2D packedPage = packing.pages[pageIndex];
            int width = Math.Max(1, packedPage.occupiedWidth);
            int height = Math.Max(1, packedPage.occupiedHeight);
            pageRasters[pageIndex] = new AtlasRaster2D(width, height);
            if (pageNormalRasters is not null)
            {
                pageNormalRasters[pageIndex] = new AtlasRaster2D(width, height);
                pageNormalRasters[pageIndex].Fill(128, 128, 255, 255);
            }
            if (pageEmissionRasters is not null)
            {
                pageEmissionRasters[pageIndex] = new AtlasRaster2D(width, height);
                pageEmissionRasters[pageIndex].Fill(0, 0, 0, 255);
            }
            pageMetadata[pageIndex] = new SpriteAtlasPage2D
            {
                id = $"page-{pageIndex:D3}",
                width = width,
                height = height,
                hasNormalMap = hasNormalMaps,
                hasEmissionMap = hasEmissionMaps
            };
        }

        var regions = new SpriteRegion2D[trimmed.Length];
        for (int index = 0; index < trimmed.Length; index++)
        {
            TrimmedSlice value = trimmed[index];
            SpriteAtlasPlacement2D placement = placementById[value.slice.id.value];
            AtlasRaster2D page = pageRasters[placement.pageIndex];
            CopyWithExtrusion(
                rasters[value.slice.sourceIndex],
                value,
                page,
                placement,
                atlas.packing.extrude);
            if (pageNormalRasters is not null && normalRasters is not null)
            {
                CopyWithExtrusion(
                    normalRasters[value.slice.sourceIndex],
                    value,
                    pageNormalRasters[placement.pageIndex],
                    placement,
                    atlas.packing.extrude);
            }
            if (pageEmissionRasters is not null && emissionRasters is not null)
            {
                CopyWithExtrusion(
                    emissionRasters[value.slice.sourceIndex],
                    value,
                    pageEmissionRasters[placement.pageIndex],
                    placement,
                    atlas.packing.extrude);
            }
            regions[index] = new SpriteRegion2D
            {
                id = value.slice.id,
                name = value.slice.name,
                pageIndex = placement.pageIndex,
                uvRect = new Rect(
                    placement.pixelRect.x / page.width,
                    placement.pixelRect.y / page.height,
                    placement.pixelRect.width / page.width,
                    placement.pixelRect.height / page.height),
                sourceSizePixels = new Vector2(value.sourceWidth, value.sourceHeight),
                trimmedSizePixels = new Vector2(value.width, value.height),
                trimOffsetPixels = new Vector2(
                    value.trimX,
                    value.sourceHeight - value.trimY - value.height),
                pivot = value.slice.pivot,
                borderPixels = value.slice.borderPixels,
                rotatedClockwise = placement.rotatedClockwise,
                outline = value.slice.outline is { Length: > 0 }
                    ? value.slice.outline.ToArray()
                    : BuildAutomaticOutline(value, rasters[value.slice.sourceIndex])
            };
        }

        var pages = new SpriteAtlasComposedPage2D[pageRasters.Length];
        for (int pageIndex = 0; pageIndex < pageRasters.Length; pageIndex++)
        {
            pages[pageIndex] = new SpriteAtlasComposedPage2D(
                pageMetadata[pageIndex],
                AtlasImageCodec2D.EncodePng(pageRasters[pageIndex]),
                pageNormalRasters is null ? null : AtlasImageCodec2D.EncodePng(pageNormalRasters[pageIndex]),
                pageEmissionRasters is null ? null : AtlasImageCodec2D.EncodePng(pageEmissionRasters[pageIndex]));
        }
        return new SpriteAtlasCompositionResult2D(
            pages,
            regions.OrderBy(static region => region.id.value, StringComparer.Ordinal).ToArray());
    }

    private static AtlasRaster2D DecodeCompanion(
        TextureAsset? companion,
        TextureAsset color,
        Func<TextureAsset, ReadOnlyMemory<byte>> readSource,
        byte red,
        byte green,
        byte blue,
        string role)
    {
        if (companion is null)
        {
            var fallback = new AtlasRaster2D(color.width, color.height);
            fallback.Fill(red, green, blue, 255);
            return fallback;
        }
        AtlasRaster2D raster = AtlasImageCodec2D.Decode(
            readSource(companion).Span,
            companion.sourceFormat);
        if (raster.width != color.width || raster.height != color.height)
        {
            throw new InvalidDataException(
                $"Atlas {role} source '{companion.assetPath}' dimensions must match color source '{color.assetPath}'.");
        }
        return raster;
    }

    private static SpriteAtlasSlice2D[] CreateWholeSourceSlices(
        SpriteAtlasSource2D[] sources,
        AtlasRaster2D[] rasters)
    {
        var result = new SpriteAtlasSlice2D[sources.Length];
        for (int index = 0; index < sources.Length; index++)
        {
            TextureAsset texture = sources[index].texture!;
            string label = string.IsNullOrWhiteSpace(sources[index].name)
                ? Path.GetFileNameWithoutExtension(texture.assetPath.localPath)
                : sources[index].name.Trim();
            result[index] = new SpriteAtlasSlice2D
            {
                id = new SpriteRegionId($"source-{index:D4}-{texture.identity.persistentId:N}"),
                name = label,
                sourceIndex = index,
                pixelRect = new Rect(0f, 0f, rasters[index].width, rasters[index].height),
                pivot = new Vector2(0.5f, 0.5f),
                outline = []
            };
        }
        return result;
    }

    private static void ValidateSlices(SpriteAtlasSlice2D[] slices, AtlasRaster2D[] rasters)
    {
        if (slices.Select(static slice => slice.id).Distinct().Count() != slices.Length)
            throw new InvalidDataException("Atlas slice identities must be unique.");
        foreach (SpriteAtlasSlice2D slice in slices)
        {
            if (!slice.id.isValid || string.IsNullOrWhiteSpace(slice.name)
                || slice.sourceIndex < 0 || slice.sourceIndex >= rasters.Length)
            {
                throw new InvalidDataException("Atlas slices require stable IDs, names, and valid source indices.");
            }
            if (!IsInteger(slice.pixelRect.x)
                || !IsInteger(slice.pixelRect.y)
                || !IsInteger(slice.pixelRect.width)
                || !IsInteger(slice.pixelRect.height))
            {
                throw new InvalidDataException($"Atlas slice '{slice.name}' must use integer pixel coordinates.");
            }
            AtlasRaster2D source = rasters[slice.sourceIndex];
            if (slice.pixelRect.x < 0f || slice.pixelRect.y < 0f
                || slice.pixelRect.width <= 0f || slice.pixelRect.height <= 0f
                || slice.pixelRect.right > source.width || slice.pixelRect.bottom > source.height)
            {
                throw new InvalidDataException($"Atlas slice '{slice.name}' is outside its decoded source.");
            }
        }
    }

    private static bool IsInteger(float value)
        => float.IsFinite(value) && value == MathF.Truncate(value);

    private static TrimmedSlice Trim(SpriteAtlasSlice2D slice, AtlasRaster2D source, bool trimTransparent)
    {
        int sourceX = checked((int)slice.pixelRect.x);
        int sourceY = checked((int)slice.pixelRect.y);
        int sourceWidth = checked((int)slice.pixelRect.width);
        int sourceHeight = checked((int)slice.pixelRect.height);
        if (!trimTransparent)
            return new TrimmedSlice(slice, sourceX, sourceY, sourceWidth, sourceHeight, 0, 0, sourceWidth, sourceHeight);

        int minimumX = sourceWidth;
        int minimumY = sourceHeight;
        int maximumX = -1;
        int maximumY = -1;
        for (int y = 0; y < sourceHeight; y++)
        {
            for (int x = 0; x < sourceWidth; x++)
            {
                if (source.GetAlpha(sourceX + x, sourceY + y) == 0)
                    continue;
                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
            }
        }
        if (maximumX < minimumX || maximumY < minimumY)
            return new TrimmedSlice(slice, sourceX, sourceY, sourceWidth, sourceHeight, 0, 0, 1, 1);
        return new TrimmedSlice(
            slice,
            sourceX,
            sourceY,
            sourceWidth,
            sourceHeight,
            minimumX,
            minimumY,
            maximumX - minimumX + 1,
            maximumY - minimumY + 1);
    }

    private static void CopyWithExtrusion(
        AtlasRaster2D source,
        TrimmedSlice slice,
        AtlasRaster2D destination,
        SpriteAtlasPlacement2D placement,
        int extrude)
    {
        int targetX = checked((int)placement.pixelRect.x);
        int targetY = checked((int)placement.pixelRect.y);
        int packedWidth = checked((int)placement.pixelRect.width);
        int packedHeight = checked((int)placement.pixelRect.height);
        for (int y = -extrude; y < packedHeight + extrude; y++)
        {
            for (int x = -extrude; x < packedWidth + extrude; x++)
            {
                int packedX = Math.Clamp(x, 0, packedWidth - 1);
                int packedY = Math.Clamp(y, 0, packedHeight - 1);
                int sourceLocalX;
                int sourceLocalY;
                if (placement.rotatedClockwise)
                {
                    sourceLocalX = packedY;
                    sourceLocalY = slice.height - 1 - packedX;
                }
                else
                {
                    sourceLocalX = packedX;
                    sourceLocalY = packedY;
                }
                destination.SetPixel(
                    targetX + x,
                    targetY + y,
                    source,
                    slice.sourceX + slice.trimX + sourceLocalX,
                    slice.sourceY + slice.trimY + sourceLocalY);
            }
        }
    }

    private static Vector2[] BuildAutomaticOutline(TrimmedSlice slice, AtlasRaster2D source)
    {
        var points = new List<PixelPoint>(checked(slice.height * 4));
        for (int localY = slice.trimY; localY < slice.trimY + slice.height; localY++)
        {
            int minimumX = slice.sourceWidth;
            int maximumX = -1;
            for (int localX = slice.trimX; localX < slice.trimX + slice.width; localX++)
            {
                if (source.GetAlpha(slice.sourceX + localX, slice.sourceY + localY) == 0)
                    continue;
                minimumX = Math.Min(minimumX, localX);
                maximumX = Math.Max(maximumX, localX);
            }
            if (maximumX < minimumX)
                continue;
            points.Add(new PixelPoint(minimumX, localY));
            points.Add(new PixelPoint(maximumX + 1, localY));
            points.Add(new PixelPoint(maximumX + 1, localY + 1));
            points.Add(new PixelPoint(minimumX, localY + 1));
        }
        if (points.Count == 0)
            return RectangleOutline();

        PixelPoint[] ordered = points.Distinct().Order().ToArray();
        if (ordered.Length < 3)
            return RectangleOutline();
        var hull = new PixelPoint[ordered.Length * 2];
        int count = 0;
        for (int index = 0; index < ordered.Length; index++)
        {
            while (count >= 2 && Cross(hull[count - 2], hull[count - 1], ordered[index]) <= 0)
                count--;
            hull[count++] = ordered[index];
        }
        int lowerCount = count;
        for (int index = ordered.Length - 2; index >= 0; index--)
        {
            while (count > lowerCount && Cross(hull[count - 2], hull[count - 1], ordered[index]) <= 0)
                count--;
            hull[count++] = ordered[index];
        }
        count--;
        if (count < 3)
            return RectangleOutline();

        var result = new Vector2[count];
        for (int index = 0; index < count; index++)
        {
            result[index] = new Vector2(
                hull[index].x / (float)slice.sourceWidth,
                hull[index].y / (float)slice.sourceHeight);
        }
        return result;
    }

    private static long Cross(PixelPoint origin, PixelPoint first, PixelPoint second)
        => (long)(first.x - origin.x) * (second.y - origin.y)
           - (long)(first.y - origin.y) * (second.x - origin.x);

    private static Vector2[] RectangleOutline()
        => [Vector2.ZERO, new Vector2(1f, 0f), Vector2.ONE, new Vector2(0f, 1f)];

    private sealed record TrimmedSlice(
        SpriteAtlasSlice2D slice,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int trimX,
        int trimY,
        int width,
        int height);

    private readonly record struct PixelPoint(int x, int y) : IComparable<PixelPoint>
    {
        public int CompareTo(PixelPoint other)
        {
            int result = x.CompareTo(other.x);
            return result != 0 ? result : y.CompareTo(other.y);
        }
    }
}

internal sealed class AtlasRaster2D
{
    internal AtlasRaster2D(int width, int height)
        : this(width, height, new byte[checked(width * height * 4)])
    {
    }

    internal AtlasRaster2D(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA pixel length does not match the raster dimensions.", nameof(pixels));
        this.width = width;
        this.height = height;
        this.pixels = pixels;
    }

    internal int width { get; }
    internal int height { get; }
    internal byte[] pixels { get; }

    internal byte GetAlpha(int x, int y) => pixels[(y * width + x) * 4 + 3];

    internal void Fill(byte red, byte green, byte blue, byte alpha)
    {
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = red;
            pixels[offset + 1] = green;
            pixels[offset + 2] = blue;
            pixels[offset + 3] = alpha;
        }
    }

    internal void SetPixel(int x, int y, AtlasRaster2D source, int sourceX, int sourceY)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            throw new InvalidDataException("Atlas extrusion exceeded its allocated page bounds.");
        int destinationOffset = (y * width + x) * 4;
        int sourceOffset = (sourceY * source.width + sourceX) * 4;
        source.pixels.AsSpan(sourceOffset, 4).CopyTo(pixels.AsSpan(destinationOffset, 4));
    }
}

internal static class AtlasImageCodec2D
{
    private static readonly byte[] S_PNG_SIGNATURE = [137, 80, 78, 71, 13, 10, 26, 10];

    internal static AtlasRaster2D Decode(ReadOnlySpan<byte> bytes, string sourceFormat)
        => sourceFormat.ToLowerInvariant() switch
        {
            "png" => DecodePng(bytes),
            "tga" => DecodeTga(bytes),
            _ => throw new InvalidDataException(
                $"Atlas composition supports PNG and TGA sources; '{sourceFormat}' remains available as a standalone texture.")
        };

    internal static byte[] EncodePng(AtlasRaster2D raster)
    {
        using var output = new MemoryStream();
        output.Write(S_PNG_SIGNATURE);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)raster.width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)raster.height));
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR", header);

        using var filtered = new MemoryStream();
        using (var compressor = new ZLibStream(filtered, CompressionLevel.Optimal, leaveOpen: true))
        {
            int rowBytes = checked(raster.width * 4);
            for (int y = 0; y < raster.height; y++)
            {
                compressor.WriteByte(0);
                compressor.Write(raster.pixels, y * rowBytes, rowBytes);
            }
        }
        WriteChunk(output, "IDAT", filtered.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static AtlasRaster2D DecodePng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < S_PNG_SIGNATURE.Length || !bytes[..8].SequenceEqual(S_PNG_SIGNATURE))
            throw new InvalidDataException("PNG signature is invalid.");
        int width = 0;
        int height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        byte interlace = 0;
        byte[] palette = [];
        byte[] transparency = [];
        using var compressed = new MemoryStream();
        int offset = 8;
        while (offset < bytes.Length)
        {
            if (offset > bytes.Length - 12)
                throw new InvalidDataException("PNG chunk header is incomplete.");
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4)));
            offset += 4;
            ReadOnlySpan<byte> type = bytes.Slice(offset, 4);
            offset += 4;
            if (length < 0 || offset > bytes.Length - length - 4)
                throw new InvalidDataException("PNG chunk length is invalid.");
            ReadOnlySpan<byte> data = bytes.Slice(offset, length);
            uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + length, 4));
            if (ComputeCrc(type, data) != expectedCrc)
                throw new InvalidDataException("PNG chunk CRC is invalid.");
            offset += length + 4;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (length != 13)
                    throw new InvalidDataException("PNG IHDR length is invalid.");
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
                bitDepth = data[8];
                colorType = data[9];
                interlace = data[12];
                if (data[10] != 0 || data[11] != 0)
                    throw new InvalidDataException("PNG compression or filter method is unsupported.");
            }
            else if (type.SequenceEqual("PLTE"u8))
                palette = data.ToArray();
            else if (type.SequenceEqual("tRNS"u8))
                transparency = data.ToArray();
            else if (type.SequenceEqual("IDAT"u8))
                compressed.Write(data);
            else if (type.SequenceEqual("IEND"u8))
                break;
        }
        if (width <= 0 || height <= 0 || bitDepth != 8 || interlace != 0)
            throw new InvalidDataException("Atlas PNG sources require non-interlaced 8-bit pixels.");
        int channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException($"PNG color type {colorType} is unsupported for atlas composition.")
        };
        if (colorType == 3 && (palette.Length == 0 || palette.Length % 3 != 0))
            throw new InvalidDataException("Indexed PNG source has no valid palette.");
        int rowBytes = checked(width * channels);
        int expectedLength = checked((rowBytes + 1) * height);
        byte[] filtered = new byte[expectedLength];
        compressed.Position = 0;
        using (var decompressor = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            int read = 0;
            while (read < filtered.Length)
            {
                int count = decompressor.Read(filtered, read, filtered.Length - read);
                if (count == 0)
                    break;
                read += count;
            }
            if (read != filtered.Length || decompressor.ReadByte() != -1)
                throw new InvalidDataException("PNG decompressed byte length is invalid.");
        }
        byte[] scanlines = Unfilter(filtered, width, height, channels);
        byte[] rgba = new byte[checked(width * height * 4)];
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            int source = pixel * channels;
            int destination = pixel * 4;
            switch (colorType)
            {
                case 0:
                    rgba[destination] = scanlines[source];
                    rgba[destination + 1] = scanlines[source];
                    rgba[destination + 2] = scanlines[source];
                    rgba[destination + 3] = 255;
                    break;
                case 2:
                    rgba[destination] = scanlines[source];
                    rgba[destination + 1] = scanlines[source + 1];
                    rgba[destination + 2] = scanlines[source + 2];
                    rgba[destination + 3] = 255;
                    break;
                case 3:
                    int paletteIndex = scanlines[source];
                    if (paletteIndex * 3 + 2 >= palette.Length)
                        throw new InvalidDataException("PNG palette index is outside PLTE.");
                    rgba[destination] = palette[paletteIndex * 3];
                    rgba[destination + 1] = palette[paletteIndex * 3 + 1];
                    rgba[destination + 2] = palette[paletteIndex * 3 + 2];
                    rgba[destination + 3] = paletteIndex < transparency.Length ? transparency[paletteIndex] : (byte)255;
                    break;
                case 4:
                    rgba[destination] = scanlines[source];
                    rgba[destination + 1] = scanlines[source];
                    rgba[destination + 2] = scanlines[source];
                    rgba[destination + 3] = scanlines[source + 1];
                    break;
                case 6:
                    scanlines.AsSpan(source, 4).CopyTo(rgba.AsSpan(destination, 4));
                    break;
            }
        }
        return new AtlasRaster2D(width, height, rgba);
    }

    private static byte[] Unfilter(byte[] filtered, int width, int height, int bytesPerPixel)
    {
        int rowBytes = checked(width * bytesPerPixel);
        byte[] result = new byte[checked(rowBytes * height)];
        int sourceOffset = 0;
        for (int y = 0; y < height; y++)
        {
            byte filter = filtered[sourceOffset++];
            int rowOffset = y * rowBytes;
            int previousOffset = rowOffset - rowBytes;
            for (int x = 0; x < rowBytes; x++)
            {
                byte raw = filtered[sourceOffset++];
                int left = x >= bytesPerPixel ? result[rowOffset + x - bytesPerPixel] : 0;
                int up = y > 0 ? result[previousOffset + x] : 0;
                int upperLeft = y > 0 && x >= bytesPerPixel
                    ? result[previousOffset + x - bytesPerPixel]
                    : 0;
                result[rowOffset + x] = filter switch
                {
                    0 => raw,
                    1 => unchecked((byte)(raw + left)),
                    2 => unchecked((byte)(raw + up)),
                    3 => unchecked((byte)(raw + ((left + up) >> 1))),
                    4 => unchecked((byte)(raw + Paeth(left, up, upperLeft))),
                    _ => throw new InvalidDataException($"PNG filter {filter} is unsupported.")
                };
            }
        }
        return result;
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        int estimate = left + up - upperLeft;
        int leftDistance = Math.Abs(estimate - left);
        int upDistance = Math.Abs(estimate - up);
        int diagonalDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= upDistance && leftDistance <= diagonalDistance
            ? left
            : upDistance <= diagonalDistance ? up : upperLeft;
    }

    private static AtlasRaster2D DecodeTga(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 18)
            throw new InvalidDataException("TGA header is incomplete.");
        int idLength = bytes[0];
        int colorMapType = bytes[1];
        int imageType = bytes[2];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]);
        int bitsPerPixel = bytes[16];
        bool grayscale = imageType is 3 or 11;
        bool rle = imageType is 10 or 11;
        if (colorMapType != 0 || imageType is not (2 or 3 or 10 or 11)
            || width <= 0 || height <= 0
            || grayscale && bitsPerPixel != 8
            || !grayscale && bitsPerPixel is not (24 or 32))
        {
            throw new InvalidDataException("Atlas TGA sources require unpaletted 8-bit grayscale or 24/32-bit true color pixels.");
        }
        int sourceChannels = bitsPerPixel / 8;
        int offset = checked(18 + idLength);
        var filePixels = new byte[checked(width * height * sourceChannels)];
        int target = 0;
        while (target < filePixels.Length)
        {
            int run = 1;
            bool repeat = false;
            if (rle)
            {
                if (offset >= bytes.Length)
                    throw new InvalidDataException("TGA RLE packet is incomplete.");
                byte header = bytes[offset++];
                repeat = (header & 0x80) != 0;
                run = (header & 0x7f) + 1;
            }
            if (repeat)
            {
                int packetBytes = checked(run * sourceChannels);
                if (offset > bytes.Length - sourceChannels || target > filePixels.Length - packetBytes)
                    throw new InvalidDataException("TGA RLE pixel is incomplete.");
                for (int count = 0; count < run; count++)
                {
                    bytes.Slice(offset, sourceChannels).CopyTo(filePixels.AsSpan(target, sourceChannels));
                    target += sourceChannels;
                }
                offset += sourceChannels;
            }
            else
            {
                int packetBytes = checked(run * sourceChannels);
                if (offset > bytes.Length - packetBytes || target > filePixels.Length - packetBytes)
                    throw new InvalidDataException("TGA pixel packet is incomplete.");
                bytes.Slice(offset, packetBytes).CopyTo(filePixels.AsSpan(target, packetBytes));
                offset += packetBytes;
                target += packetBytes;
            }
        }
        byte[] rgba = new byte[checked(width * height * 4)];
        bool topOrigin = (bytes[17] & 0x20) != 0;
        bool rightOrigin = (bytes[17] & 0x10) != 0;
        for (int fileY = 0; fileY < height; fileY++)
        {
            int y = topOrigin ? fileY : height - 1 - fileY;
            for (int fileX = 0; fileX < width; fileX++)
            {
                int x = rightOrigin ? width - 1 - fileX : fileX;
                int source = (fileY * width + fileX) * sourceChannels;
                int destination = (y * width + x) * 4;
                if (grayscale)
                {
                    rgba[destination] = filePixels[source];
                    rgba[destination + 1] = filePixels[source];
                    rgba[destination + 2] = filePixels[source];
                    rgba[destination + 3] = 255;
                }
                else
                {
                    rgba[destination] = filePixels[source + 2];
                    rgba[destination + 1] = filePixels[source + 1];
                    rgba[destination + 2] = filePixels[source];
                    rgba[destination + 3] = sourceChannels == 4 ? filePixels[source + 3] : (byte)255;
                }
            }
        }
        return new AtlasRaster2D(width, height, rgba);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, ComputeCrc(typeBytes, data));
        output.Write(crc);
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in type)
            crc = UpdateCrc(crc, value);
        foreach (byte value in data)
            crc = UpdateCrc(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0 ? crc >> 1 ^ 0xedb88320u : crc >> 1;
        return crc;
    }
}
