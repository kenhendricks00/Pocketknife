namespace Pocketknife.Core.Launcher;

/// <summary>
/// A search address with a <c>{query}</c> placeholder. Only http and https are allowed, and the placeholder can't
/// be part of the site's address, so a query can never change which site is opened.
/// </summary>
public sealed class UrlTemplate
{
    public const string Placeholder = "{query}";
    private const string Marker = "pocketknife-query-marker";

    private UrlTemplate(string pattern, Uri home)
    {
        Pattern = pattern;
        Home = home;
    }

    public string Pattern { get; }

    /// <summary>The site's front page, for example https://www.youtube.com/.</summary>
    public Uri Home { get; }

    public static bool TryCreate(string? text, out UrlTemplate? template, out string error)
    {
        template = null;
        var pattern = text?.Trim() ?? "";
        error = pattern switch
        {
            "" => "Enter a web address.",
            { Length: > 2000 } => "That address is too long.",
            _ when pattern.Any(char.IsWhiteSpace) => "Web addresses can't contain spaces.",
            _ when !pattern.Contains(Placeholder, StringComparison.OrdinalIgnoreCase) =>
                $"Add {Placeholder} where the search words go, for example https://example.com/search?q={Placeholder}.",
            _ => "",
        };
        if (error.Length > 0)
        {
            return false;
        }

        if (!Uri.TryCreate(pattern.Replace(Placeholder, Marker, StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out var uri))
        {
            error = "That isn't a valid web address.";
            return false;
        }

        if (!IsWeb(uri))
        {
            error = "Only http and https addresses are allowed.";
            return false;
        }

        if (uri.UserInfo.Length > 0)
        {
            error = "Addresses with a user name or password aren't allowed.";
            return false;
        }

        if (uri.Host.Length == 0 || uri.Authority.Contains(Marker, StringComparison.OrdinalIgnoreCase))
        {
            error = $"{Placeholder} can't be part of the site's address.";
            return false;
        }

        template = new UrlTemplate(pattern, new Uri($"{uri.Scheme}://{uri.Authority}/"));
        return true;
    }

    public static UrlTemplate Create(string text) =>
        TryCreate(text, out var template, out var error) ? template! : throw new ArgumentException(error, nameof(text));

    /// <summary>The address with <paramref name="query"/> percent-encoded into every placeholder.</summary>
    public Uri Build(string query) =>
        new(Pattern.Replace(Placeholder, Uri.EscapeDataString(query), StringComparison.OrdinalIgnoreCase));

    public static bool IsWeb(Uri uri) =>
        uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
