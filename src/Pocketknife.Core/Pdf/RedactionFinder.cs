using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Pocketknife.Core.Pdf;

/// <summary>A rectangle on the rendered page in top-down points, after crop box and rotation are applied.</summary>
public readonly record struct PageRect(double X, double Y, double Width, double Height);

public static class RedactionFinder
{
    public const string EmailPattern = @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}";
    public const string PhonePattern = @"(?<!\d)(?:\+?\d{1,3}[\s.-]?)?(?:\(\d{3}\)|\d{3})[\s.-]?\d{3}[\s.-]?\d{4}(?!\d)";

    public static Regex? BuildPattern(IEnumerable<string> terms, bool emails, bool phones, bool matchCase)
    {
        var parts = terms
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Select(t => string.Join(@"\s+", t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)))
            .ToList();
        if (emails)
        {
            parts.Add(EmailPattern);
        }

        if (phones)
        {
            parts.Add(PhonePattern);
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var options = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        return new Regex(string.Join("|", parts.Select(p => $"(?:{p})")), options, TimeSpan.FromSeconds(5));
    }

    /// <summary>Finds every match of <paramref name="pattern"/> on the page and returns the areas to black out.</summary>
    public static IReadOnlyList<PageRect> Find(Page page, Regex pattern)
    {
        var text = new StringBuilder();
        var owners = new List<Letter?>();
        foreach (var word in page.GetWords())
        {
            if (text.Length > 0)
            {
                text.Append(' ');
                owners.Add(null);
            }

            foreach (var letter in word.Letters)
            {
                text.Append(letter.Value);
                for (var i = 0; i < letter.Value.Length; i++)
                {
                    owners.Add(letter);
                }
            }
        }

        var boxes = new List<PdfRectangle>();
        foreach (Match match in pattern.Matches(text.ToString()))
        {
            var letters = owners.Skip(match.Index).Take(match.Length).OfType<Letter>().Distinct().ToList();
            boxes.AddRange(MergeLines(letters.Select(LetterBox)));
        }

        return [.. boxes.Select(b => ToPage(page, b))];
    }

    private static PdfRectangle LetterBox(Letter letter)
    {
        var glyph = letter.BoundingBox;
        var size = letter.PointSize > 0 ? letter.PointSize : glyph.Height;
        var baseline = letter.StartBaseLine.Y;
        var left = Math.Min(glyph.Left, letter.StartBaseLine.X);
        var right = Math.Max(glyph.Right, letter.EndBaseLine.X);
        var bottom = Math.Min(glyph.Bottom, baseline - (0.25 * size));
        var top = Math.Max(glyph.Top, baseline + (0.85 * size));
        const double pad = 1;
        return new PdfRectangle(left - pad, bottom - pad, right + pad, top + pad);
    }

    /// <summary>Unions boxes that sit on the same line so the gaps between matched words are covered too.</summary>
    private static List<PdfRectangle> MergeLines(IEnumerable<PdfRectangle> boxes)
    {
        var merged = new List<PdfRectangle>();
        foreach (var box in boxes)
        {
            var index = merged.FindIndex(m => Math.Min(m.Top, box.Top) - Math.Max(m.Bottom, box.Bottom) > 0.5 * Math.Min(m.Height, box.Height));
            if (index < 0)
            {
                merged.Add(box);
            }
            else
            {
                var m = merged[index];
                merged[index] = new PdfRectangle(Math.Min(m.Left, box.Left), Math.Min(m.Bottom, box.Bottom), Math.Max(m.Right, box.Right), Math.Max(m.Top, box.Top));
            }
        }

        return merged;
    }

    private static PageRect ToPage(Page page, PdfRectangle box)
    {
        var crop = page.CropBox.Bounds;
        var width = crop.Width;
        var height = crop.Height;
        var x0 = box.Left - crop.Left;
        var x1 = box.Right - crop.Left;
        var y0 = crop.Top - box.Top;
        var y1 = crop.Top - box.Bottom;
        return (((page.Rotation.Value % 360) + 360) % 360) switch
        {
            90 => new PageRect(height - y1, x0, y1 - y0, x1 - x0),
            180 => new PageRect(width - x1, height - y1, x1 - x0, y1 - y0),
            270 => new PageRect(y0, width - x1, y1 - y0, x1 - x0),
            _ => new PageRect(x0, y0, x1 - x0, y1 - y0),
        };
    }
}
