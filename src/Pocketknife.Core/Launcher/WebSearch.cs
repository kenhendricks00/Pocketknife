using System.Text.RegularExpressions;

namespace Pocketknife.Core.Launcher;

public sealed record SearchEngine(string Id, string Name, UrlTemplate Template);

public static class SearchEngines
{
    public const string CustomId = "custom";
    public const string DefaultId = "duckduckgo";

    public static IReadOnlyList<SearchEngine> BuiltIn { get; } =
    [
        new("google", "Google", UrlTemplate.Create("https://www.google.com/search?q={query}")),
        new("bing", "Bing", UrlTemplate.Create("https://www.bing.com/search?q={query}")),
        new("duckduckgo", "DuckDuckGo", UrlTemplate.Create("https://duckduckgo.com/?q={query}")),
        new("brave", "Brave", UrlTemplate.Create("https://search.brave.com/search?q={query}")),
        new("startpage", "Startpage", UrlTemplate.Create("https://www.startpage.com/do/search?query={query}")),
    ];
}

/// <summary>A DuckDuckGo-style shortcut: <c>!yt cats</c> searches YouTube for "cats".</summary>
/// <param name="Home">The page a bare <c>!trigger</c> opens. When empty, the template's site front page.</param>
public sealed record Bang(string Trigger, string Name, string Template, string? Home = null);

public static partial class Bangs
{
    public static IReadOnlyList<Bang> Defaults { get; } =
    [
        new("g", "Google", "https://www.google.com/search?q={query}"),
        new("ddg", "DuckDuckGo", "https://duckduckgo.com/?q={query}"),
        new("b", "Bing", "https://www.bing.com/search?q={query}"),
        new("yt", "YouTube", "https://www.youtube.com/results?search_query={query}"),
        new("gh", "GitHub", "https://github.com/search?q={query}"),
        new("w", "Wikipedia", "https://en.wikipedia.org/w/index.php?search={query}", "https://en.wikipedia.org/"),
        new("r", "Reddit", "https://www.reddit.com/search/?q={query}"),
        new("a", "Amazon", "https://www.amazon.com/s?k={query}"),
        new("maps", "Google Maps", "https://www.google.com/maps/search/{query}", "https://www.google.com/maps"),
    ];

    /// <summary>Built-in bangs the person kept, with their own bangs added or replacing built-ins of the same trigger.</summary>
    public static IReadOnlyList<Bang> Effective(WebSearchSettings settings)
    {
        var removed = settings.RemovedBangs.Select(Normalize).ToHashSet();
        var user = settings.UserBangs.ToDictionary(b => Normalize(b.Trigger));
        return [.. Defaults.Where(b => !removed.Contains(b.Trigger) && !user.ContainsKey(b.Trigger)).Concat(user.Values)];
    }

    public static bool TryValidate(Bang bang, out string error)
    {
        var trigger = Normalize(bang.Trigger);
        error = "";
        if (!TriggerPattern().IsMatch(trigger))
        {
            error = "Shortcuts use 1 to 20 letters, digits, dots, dashes or underscores, for example yt.";
        }
        else if (string.IsNullOrWhiteSpace(bang.Name) || bang.Name.Trim().Length > 40)
        {
            error = "Give the site a name of up to 40 characters.";
        }
        else if (!UrlTemplate.TryCreate(bang.Template, out _, out var templateError))
        {
            error = templateError;
        }
        else if (bang.Home is { Length: > 0 } home && !(Uri.TryCreate(home, UriKind.Absolute, out var uri) && UrlTemplate.IsWeb(uri)))
        {
            error = "The home page must be an http or https address.";
        }

        return error.Length == 0;
    }

    /// <summary>Adds a bang, or replaces the one with the same trigger.</summary>
    public static WebSearchSettings Add(WebSearchSettings settings, Bang bang)
    {
        if (!TryValidate(bang, out var error))
        {
            throw new ArgumentException(error, nameof(bang));
        }

        var trigger = Normalize(bang.Trigger);
        var clean = bang with { Trigger = trigger, Name = bang.Name.Trim(), Template = bang.Template.Trim() };
        return settings with
        {
            UserBangs = [.. settings.UserBangs.Where(b => Normalize(b.Trigger) != trigger), clean],
            RemovedBangs = [.. settings.RemovedBangs.Where(t => Normalize(t) != trigger)],
        };
    }

    public static WebSearchSettings Remove(WebSearchSettings settings, string trigger)
    {
        trigger = Normalize(trigger);
        var isDefault = Defaults.Any(b => b.Trigger == trigger);
        return settings with
        {
            UserBangs = [.. settings.UserBangs.Where(b => Normalize(b.Trigger) != trigger)],
            RemovedBangs = isDefault && !settings.RemovedBangs.Any(t => Normalize(t) == trigger)
                ? [.. settings.RemovedBangs, trigger]
                : settings.RemovedBangs,
        };
    }

    public static WebSearchSettings RestoreDefaults(WebSearchSettings settings) =>
        settings with { UserBangs = [], RemovedBangs = [] };

    internal static string Normalize(string trigger) => trigger.Trim().TrimStart('!').ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9._-]{1,20}$")]
    private static partial Regex TriggerPattern();
}

public sealed record WebSearchSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>A built-in engine id, or <see cref="SearchEngines.CustomId"/> to use <see cref="CustomTemplate"/>.</summary>
    public string Engine { get; init; } = SearchEngines.DefaultId;

    public string CustomTemplate { get; init; } = "";

    public List<Bang> UserBangs { get; init; } = [];

    /// <summary>Triggers of built-in bangs the person removed.</summary>
    public List<string> RemovedBangs { get; init; } = [];
}

/// <summary>Where a launcher query goes on the web.</summary>
/// <param name="Query">The words searched for, or empty when a bare bang opens the site's home page.</param>
public sealed record WebTarget(string SiteName, Uri Url, string Query, bool FromBang);

/// <summary>Turns launcher text into a web address. Pure: no network access and no side effects.</summary>
public sealed class WebSearch
{
    private readonly Dictionary<string, (string Name, UrlTemplate Template, Uri Home)> _bangs;

    public WebSearch(SearchEngine engine, IEnumerable<Bang> bangs)
    {
        Engine = engine;
        _bangs = [];
        foreach (var bang in bangs)
        {
            if (Bangs.TryValidate(bang, out _))
            {
                var template = UrlTemplate.Create(bang.Template);
                var home = bang.Home is { Length: > 0 } h ? new Uri(h) : template.Home;
                _bangs[Bangs.Normalize(bang.Trigger)] = (bang.Name.Trim(), template, home);
            }
        }
    }

    public SearchEngine Engine { get; }

    /// <summary>The configured search, or null when web search is off. An invalid custom engine falls back to the default.</summary>
    public static WebSearch? From(WebSearchSettings settings)
    {
        if (!settings.Enabled)
        {
            return null;
        }

        var fallback = SearchEngines.BuiltIn.First(e => e.Id == SearchEngines.DefaultId);
        var engine = settings.Engine == SearchEngines.CustomId
            ? UrlTemplate.TryCreate(settings.CustomTemplate, out var custom, out _)
                ? new SearchEngine(SearchEngines.CustomId, custom!.Home.Host, custom)
                : fallback
            : SearchEngines.BuiltIn.FirstOrDefault(e => e.Id == settings.Engine) ?? fallback;
        return new WebSearch(engine, Bangs.Effective(settings));
    }

    /// <summary>
    /// A known <c>!bang</c> anywhere in the text sends the rest of the text to that site, or opens its home page
    /// when nothing else was typed. Anything else, including unknown bangs, goes to the default engine as typed.
    /// </summary>
    public WebTarget? Resolve(string text)
    {
        var query = text.Trim();
        if (query.Length == 0)
        {
            return null;
        }

        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 1 && words[i][0] == '!' && _bangs.TryGetValue(words[i][1..].ToLowerInvariant(), out var bang))
            {
                var rest = string.Join(' ', words.Where((_, j) => j != i));
                return rest.Length == 0
                    ? new WebTarget(bang.Name, bang.Home, "", true)
                    : new WebTarget(bang.Name, bang.Template.Build(rest), rest, true);
            }
        }

        return new WebTarget(Engine.Name, Engine.Template.Build(query), query, false);
    }
}
