using Toolbox.Core.Engines;
using Toolbox.Core.Running;

namespace Toolbox.Core.Tools;

public enum ToolCategory
{
    Video,
    Images,
    Documents,
    TextAndSpeech,
    Files,
}

public static class ToolCategoryNames
{
    public static string DisplayName(this ToolCategory category) => category switch
    {
        ToolCategory.Video => "Video & Audio",
        ToolCategory.Images => "Images",
        ToolCategory.Documents => "PDF",
        ToolCategory.TextAndSpeech => "Text & Speech",
        ToolCategory.Files => "Files",
        _ => category.ToString(),
    };
}

/// <summary>
/// A tool module. Implementations describe their input and settings declaratively so the UI can build
/// each tool's screen, and do their work in <see cref="RunAsync"/>, writing only into the job's output folder.
/// </summary>
public abstract class Tool
{
    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract string Summary { get; }

    public abstract ToolCategory Category { get; }

    /// <summary>A Segoe Fluent Icons code point.</summary>
    public abstract string Glyph { get; }

    public virtual IReadOnlyList<string> Keywords => [];

    public abstract ToolInput Input { get; }

    public virtual IReadOnlyList<ToolOption> Options => [];

    public virtual IReadOnlyList<EngineSpec> RequiredEngines(OptionValues options) => [];

    public abstract Task RunAsync(ToolJob job, CancellationToken cancellationToken);
}

public abstract record ToolInput;

/// <summary>Files, or folders whose top-level files are processed as a batch.</summary>
/// <param name="Extensions">Accepted extensions including the dot, lower case.</param>
/// <param name="Combine">When true all files form a single job (for example merging), otherwise one job per file.</param>
public sealed record FileInput(IReadOnlyList<string> Extensions, string Noun, bool Combine = false, int MinimumFiles = 1) : ToolInput
{
    public bool Accepts(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
}

/// <param name="SplitLines">When true each non-empty line is its own job.</param>
public sealed record TextInput(string Label, string Placeholder, bool SplitLines) : ToolInput;

public static class FileTypes
{
    public static readonly IReadOnlyList<string> Video = [".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".wmv", ".flv", ".mpg", ".mpeg", ".ts", ".3gp"];
    public static readonly IReadOnlyList<string> Audio = [".mp3", ".m4a", ".wav", ".flac", ".ogg", ".opus", ".aac", ".wma", ".aiff"];
    public static readonly IReadOnlyList<string> Media = [.. Video, .. Audio];
    public static readonly IReadOnlyList<string> Image = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic"];
    public static readonly IReadOnlyList<string> Pdf = [".pdf"];
}
