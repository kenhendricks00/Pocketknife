using System.Globalization;
using Pocketknife.Core.Running;

namespace Pocketknife.Core.Pdf;

/// <summary>An inclusive, 1-based page range.</summary>
public readonly record struct PageRange(int First, int Last)
{
    public override string ToString() => First == Last ? $"p{First}" : $"p{First}-{Last}";
}

public static class PageRanges
{
    /// <summary>Parses text like "1-3, 5, 8-" against a document with <paramref name="pageCount"/> pages.</summary>
    public static IReadOnlyList<PageRange> Parse(string text, int pageCount)
    {
        var ranges = new List<PageRange>();
        foreach (var raw in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length > 2)
            {
                throw new ToolException($"'{raw}' is not a page range. Use forms like 3, 1-4 or 8-.");
            }

            var first = parts[0].Length == 0 ? 1 : Page(parts[0], raw);
            var last = parts.Length == 1 ? first : parts[1].Length == 0 ? pageCount : Page(parts[1], raw);
            if (first > last)
            {
                throw new ToolException($"'{raw}' goes backwards.");
            }

            if (last > pageCount)
            {
                throw new ToolException($"'{raw}' asks for page {last}, but the document has {pageCount} pages.");
            }

            ranges.Add(new PageRange(first, last));
        }

        return ranges.Count > 0 ? ranges : throw new ToolException("Enter the pages to keep, for example 1-3, 5, 8-.");
    }

    public static IReadOnlyList<PageRange> Chunks(int pageCount, int size)
    {
        var ranges = new List<PageRange>();
        for (var first = 1; first <= pageCount; first += size)
        {
            ranges.Add(new PageRange(first, Math.Min(pageCount, first + size - 1)));
        }

        return ranges;
    }

    private static int Page(string value, string raw) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page >= 1
            ? page
            : throw new ToolException($"'{raw}' is not a page range. Use forms like 3, 1-4 or 8-.");
}
