using System.Globalization;
using Pocketknife.Core.Tools;

namespace Pocketknife.Core.Launcher;

public abstract record LauncherResult;

public sealed record ToolResult(Tool Tool) : LauncherResult;

public sealed record CalculationResult(Calculation Calculation) : LauncherResult;

public sealed record WebResult(WebTarget Target) : LauncherResult;

/// <param name="Web">The configured web search, or null when it is turned off.</param>
public sealed record LauncherOptions(WebSearch? Web, bool Calculator, CultureInfo Culture);

/// <summary>
/// Orders what the launcher shows for some text: a calculation first, then a bang's site, then matching tools,
/// and a search of the default engine last.
/// </summary>
public static class LauncherSearch
{
    public static IReadOnlyList<LauncherResult> Find(string query, IReadOnlyList<Tool> tools, LauncherOptions options)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [.. tools.Select(t => new ToolResult(t))];
        }

        var results = new List<LauncherResult>();
        if (options.Calculator && Calculator.TryEvaluate(query, options.Culture) is { } calculation)
        {
            results.Add(new CalculationResult(calculation));
        }

        var web = options.Web?.Resolve(query);
        if (web is { FromBang: true })
        {
            results.Add(new WebResult(web));
            return results;
        }

        results.AddRange(ToolSearch.Rank(tools, query).Select(t => new ToolResult(t)));
        if (web is not null)
        {
            results.Add(new WebResult(web));
        }

        return results;
    }
}
