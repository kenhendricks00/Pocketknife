using Toolbox.Core.Tools.Documents;
using Toolbox.Core.Tools.Files;
using Toolbox.Core.Tools.Images;
using Toolbox.Core.Tools.Text;
using Toolbox.Core.Tools.Video;

namespace Toolbox.Core.Tools;

/// <summary>Every tool in the app. Adding a tool means writing a <see cref="Tool"/> subclass and listing it here.</summary>
public static class ToolRegistry
{
    public static IReadOnlyList<Tool> All { get; } =
    [
        new DownloadVideoTool(),
        new CompressVideoTool(),
        new ConvertMediaTool(),
        new VideoToGifTool(),
        new RemoveBackgroundTool(),
        new MergePdfsTool(),
        new SplitPdfTool(),
        new RedactPdfTool(),
        new TranscribeTool(),
        new MakeQrCodeTool(),
        new ReadQrCodeTool(),
    ];

    public static Tool? Find(string id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}

public static class ToolSearch
{
    /// <summary>Ranks tools for a launcher query. Every word of the query must match the tool somewhere.</summary>
    public static IReadOnlyList<Tool> Rank(IEnumerable<Tool> tools, string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [.. tools];
        }

        return [.. tools
            .Select((tool, index) => (tool, index, score: Score(tool, words)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)
            .Select(x => x.tool)];
    }

    private static int Score(Tool tool, string[] words)
    {
        var total = 0;
        foreach (var word in words)
        {
            var best = WordScore(tool, word);
            if (best == 0)
            {
                return 0;
            }

            total += best;
        }

        return total;
    }

    private static int WordScore(Tool tool, string word)
    {
        const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;
        var nameWords = tool.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tool.Name.StartsWith(word, ignoreCase))
        {
            return 100;
        }

        if (nameWords.Any(w => w.StartsWith(word, ignoreCase)))
        {
            return 80;
        }

        var initials = new string([.. nameWords.Select(w => w[0])]);
        if (word.Length > 1 && initials.StartsWith(word, ignoreCase))
        {
            return 70;
        }

        if (tool.Name.Contains(word, ignoreCase))
        {
            return 50;
        }

        if (tool.Keywords.Any(k => k.StartsWith(word, ignoreCase)))
        {
            return 40;
        }

        if (tool.Keywords.Any(k => k.Contains(word, ignoreCase)) || tool.Category.DisplayName().Contains(word, ignoreCase))
        {
            return 25;
        }

        return tool.Summary.Contains(word, ignoreCase) ? 10 : 0;
    }
}
