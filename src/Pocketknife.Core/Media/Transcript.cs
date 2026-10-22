using System.Globalization;
using System.Text;

namespace Pocketknife.Core.Media;

public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);

public static class Transcript
{
    public static string ToText(IEnumerable<TranscriptSegment> segments) =>
        string.Join(Environment.NewLine, segments.Select(s => s.Text.Trim()).Where(t => t.Length > 0)) + Environment.NewLine;

    public static string ToSrt(IEnumerable<TranscriptSegment> segments)
    {
        var builder = new StringBuilder();
        var index = 1;
        foreach (var segment in segments.Where(s => s.Text.Trim().Length > 0))
        {
            builder.Append(index++).Append("\r\n")
                .Append(SrtTime(segment.Start)).Append(" --> ").Append(SrtTime(segment.End)).Append("\r\n")
                .Append(segment.Text.Trim()).Append("\r\n\r\n");
        }

        return builder.ToString();
    }

    public static string SrtTime(TimeSpan time) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}");
}
