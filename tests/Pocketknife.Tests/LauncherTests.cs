using System.Globalization;
using Pocketknife.Core.Launcher;
using Pocketknife.Core.Settings;
using Pocketknife.Core.Tools;
using Pocketknife.Tests.Support;

namespace Pocketknife.Tests;

public class UrlTemplateTests
{
    [Fact]
    public void Encodes_the_query_into_the_placeholder()
    {
        var template = UrlTemplate.Create("https://example.com/search?q={query}&lang=en");

        Assert.Equal("https://example.com/search?q=cats%20%26%20dogs%20%231%2Bmore%2F%3F&lang=en", template.Build("cats & dogs #1+more/?").AbsoluteUri);
        Assert.Equal("https://example.com/search?q=caf%C3%A9%20%E6%97%A5%E6%9C%AC&lang=en", template.Build("café 日本").AbsoluteUri);
        Assert.Equal("https://example.com/", template.Home.AbsoluteUri);
    }

    [Theory]
    [InlineData("", "Enter a web address")]
    [InlineData("https://example.com/search", "{query}")]
    [InlineData("javascript:alert({query})", "Only http and https")]
    [InlineData("file:///C:/{query}", "Only http and https")]
    [InlineData("ftp://example.com/{query}", "Only http and https")]
    [InlineData("https://{query}.example.com/", "can't be part of the site's address")]
    [InlineData("https://user:pass@example.com/?q={query}", "user name or password")]
    [InlineData("https://example.com/?q= {query}", "spaces")]
    [InlineData("not a url {query}", "spaces")]
    [InlineData("example.com/?q={query}", "valid web address")]
    public void Rejects_unsafe_or_broken_templates(string text, string reason)
    {
        Assert.False(UrlTemplate.TryCreate(text, out var template, out var error));
        Assert.Null(template);
        Assert.Contains(reason, error, StringComparison.Ordinal);
    }
}

public class WebSearchTests
{
    private static WebSearch Search(WebSearchSettings? settings = null) => WebSearch.From(settings ?? new WebSearchSettings())!;

    [Fact]
    public void Plain_text_goes_to_the_default_engine()
    {
        var target = Search().Resolve("  how tall is everest  ")!;

        Assert.Equal(("DuckDuckGo", "how tall is everest", false), (target.SiteName, target.Query, target.FromBang));
        Assert.Equal("https://duckduckgo.com/?q=how%20tall%20is%20everest", target.Url.AbsoluteUri);
    }

    [Theory]
    [InlineData("!yt lofi beats", "YouTube", "https://www.youtube.com/results?search_query=lofi%20beats")]
    [InlineData("lofi beats !yt", "YouTube", "https://www.youtube.com/results?search_query=lofi%20beats")]
    [InlineData("lofi !YT beats", "YouTube", "https://www.youtube.com/results?search_query=lofi%20beats")]
    [InlineData("!gh pocketknife", "GitHub", "https://github.com/search?q=pocketknife")]
    [InlineData("!w Ada Lovelace", "Wikipedia", "https://en.wikipedia.org/w/index.php?search=Ada%20Lovelace")]
    [InlineData("!maps coffee near me", "Google Maps", "https://www.google.com/maps/search/coffee%20near%20me")]
    [InlineData("!g a&b", "Google", "https://www.google.com/search?q=a%26b")]
    public void A_bang_anywhere_searches_that_site_with_the_rest(string text, string site, string url)
    {
        var target = Search().Resolve(text)!;

        Assert.True(target.FromBang);
        Assert.Equal((site, url), (target.SiteName, target.Url.AbsoluteUri));
    }

    [Theory]
    [InlineData("!yt", "https://www.youtube.com/")]
    [InlineData("  !w  ", "https://en.wikipedia.org/")]
    [InlineData("!maps", "https://www.google.com/maps")]
    public void A_bare_bang_opens_the_home_page(string text, string url)
    {
        var target = Search().Resolve(text)!;

        Assert.Equal((url, ""), (target.Url.AbsoluteUri, target.Query));
    }

    [Fact]
    public void An_unknown_bang_searches_the_default_engine_with_the_text_unchanged()
    {
        var target = Search().Resolve("!nope cats")!;

        Assert.False(target.FromBang);
        Assert.Equal("https://duckduckgo.com/?q=%21nope%20cats", target.Url.AbsoluteUri);
    }

    [Fact]
    public void Off_means_no_web_results_at_all() =>
        Assert.Null(WebSearch.From(new WebSearchSettings { Enabled = false }));

    [Theory]
    [InlineData("google", "https://www.google.com/search?q=x")]
    [InlineData("bing", "https://www.bing.com/search?q=x")]
    [InlineData("brave", "https://search.brave.com/search?q=x")]
    [InlineData("startpage", "https://www.startpage.com/do/search?query=x")]
    public void The_chosen_engine_is_used(string engine, string url) =>
        Assert.Equal(url, Search(new WebSearchSettings { Engine = engine }).Resolve("x")!.Url.AbsoluteUri);

    [Fact]
    public void A_custom_engine_uses_its_template_and_a_broken_one_falls_back()
    {
        var custom = Search(new WebSearchSettings { Engine = SearchEngines.CustomId, CustomTemplate = "https://search.example.org/?s={query}" });
        var broken = Search(new WebSearchSettings { Engine = SearchEngines.CustomId, CustomTemplate = "javascript:{query}" });

        Assert.Equal("https://search.example.org/?s=x%20y", custom.Resolve("x y")!.Url.AbsoluteUri);
        Assert.Equal("search.example.org", custom.Engine.Name);
        Assert.Equal("DuckDuckGo", broken.Engine.Name);
    }

    [Fact]
    public void Empty_text_resolves_to_nothing() => Assert.Null(Search().Resolve("   "));
}

public class BangListTests
{
    [Fact]
    public void Added_bangs_work_and_can_replace_a_built_in()
    {
        var settings = Bangs.Add(new WebSearchSettings(), new Bang("!MDN", "MDN", "https://developer.mozilla.org/search?q={query}"));
        settings = Bangs.Add(settings, new Bang("yt", "YouTube Music", "https://music.youtube.com/search?q={query}"));
        var search = WebSearch.From(settings)!;

        Assert.Equal("https://developer.mozilla.org/search?q=fetch", search.Resolve("!mdn fetch")!.Url.AbsoluteUri);
        Assert.Equal(("YouTube Music", "https://music.youtube.com/search?q=x"), (search.Resolve("!yt x")!.SiteName, search.Resolve("!yt x")!.Url.AbsoluteUri));
        Assert.Single(Bangs.Effective(settings), b => b.Trigger == "yt");
    }

    [Fact]
    public void Removing_a_built_in_makes_it_plain_text_until_defaults_are_restored()
    {
        var settings = Bangs.Remove(new WebSearchSettings(), "!a");

        Assert.False(WebSearch.From(settings)!.Resolve("!a lamp")!.FromBang);
        Assert.DoesNotContain(Bangs.Effective(settings), b => b.Trigger == "a");
        Assert.True(WebSearch.From(Bangs.RestoreDefaults(settings))!.Resolve("!a lamp")!.FromBang);
    }

    [Theory]
    [InlineData("", "MDN", "https://x.org/?q={query}", "letters, digits")]
    [InlineData("has space", "MDN", "https://x.org/?q={query}", "letters, digits")]
    [InlineData("mdn", " ", "https://x.org/?q={query}", "name")]
    [InlineData("mdn", "MDN", "javascript:{query}", "http and https")]
    public void Invalid_bangs_are_refused_with_a_reason(string trigger, string name, string template, string reason)
    {
        var error = Assert.Throws<ArgumentException>(() => Bangs.Add(new WebSearchSettings(), new Bang(trigger, name, template)));

        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }
}

public class CalculatorTests
{
    private static readonly CultureInfo s_us = CultureInfo.GetCultureInfo("en-US");

    private static string? Eval(string text) => Calculator.TryEvaluate(text, s_us)?.Display;

    [Theory]
    [InlineData("1+2*3", "7")]
    [InlineData("(1+2)*3", "9")]
    [InlineData("10/4", "2.5")]
    [InlineData(".5 + .25", "0.75")]
    [InlineData("0.1+0.2", "0.3")]
    [InlineData("2^3^2", "512")]
    [InlineData("-2^2", "-4")]
    [InlineData("2*-3", "-6")]
    [InlineData("--3+1", "4")]
    [InlineData("2^-1", "0.5")]
    [InlineData("1e3+1", "1,001")]
    [InlineData("12*(3+4)", "84")]
    [InlineData("1234567.891*1", "1,234,567.891")]
    [InlineData("6 × 7 ÷ 2 − 1", "20")]
    [InlineData("2pi", "6.28318530717959")]
    [InlineData("2(3+1)", "8")]
    public void Arithmetic_follows_the_usual_rules(string text, string expected) => Assert.Equal(expected, Eval(text));

    [Theory]
    [InlineData("50%", "0.5")]
    [InlineData("15% of 80", "12")]
    [InlineData("200 + 10%", "220")]
    [InlineData("200 - 10%", "180")]
    [InlineData("50 * 10%", "5")]
    [InlineData("10% of 50 + 2", "7")]
    public void Percentages_work_like_a_desk_calculator(string text, string expected) => Assert.Equal(expected, Eval(text));

    [Theory]
    [InlineData("sqrt(16)", "4")]
    [InlineData("abs(-3.5)", "3.5")]
    [InlineData("round(2.5)", "3")]
    [InlineData("round(3.14159, 2)", "3.14")]
    [InlineData("floor(-1.5)", "-2")]
    [InlineData("ceil(1.2)", "2")]
    [InlineData("min(3, 1, 2)", "1")]
    [InlineData("max(3; 1; 2)", "3")]
    [InlineData("sin(pi/2)", "1")]
    [InlineData("cos(0)", "1")]
    [InlineData("tan(0)", "0")]
    [InlineData("sin(pi)", "0")]
    [InlineData("log(1000)", "3")]
    [InlineData("ln(e)", "1")]
    [InlineData("pi*2", "6.28318530717959")]
    public void Functions_and_constants_evaluate(string text, string expected) => Assert.Equal(expected, Eval(text));

    [Theory]
    [InlineData("0xff", "255")]
    [InlineData("0xff + 1", "256")]
    [InlineData("255 to hex", "0xFF")]
    [InlineData("10 in bin", "0b1010")]
    [InlineData("0b1010 to dec", "10")]
    [InlineData("64 as oct", "0o100")]
    [InlineData("-255 to hex", "-0xFF")]
    public void Hex_binary_and_octal_convert(string text, string expected) => Assert.Equal(expected, Eval(text));

    [Theory]
    [InlineData("hello")]
    [InlineData("compress video")]
    [InlineData("video to gif")]
    [InlineData("pi")]
    [InlineData("e")]
    [InlineData("42")]
    [InlineData("-5")]
    [InlineData("2 3")]
    [InlineData("1/0")]
    [InlineData("5 / (2 - 2)")]
    [InlineData("sqrt(-1)")]
    [InlineData("log(0)")]
    [InlineData("2+")]
    [InlineData("(1+2")]
    [InlineData("sqrt(")]
    [InlineData("1..2")]
    [InlineData("1.2.3")]
    [InlineData("!g 2+2")]
    [InlineData("2+2 !g")]
    [InlineData("1.5 to hex")]
    [InlineData("round(1, 99)")]
    [InlineData("0xfffffffffffffffff")]
    [InlineData("foo(2)")]
    [InlineData("")]
    public void Anything_that_is_not_clearly_math_gives_no_result(string text) => Assert.Null(Eval(text));

    [Fact]
    public void Deep_or_huge_input_is_refused_instead_of_crashing()
    {
        Assert.Null(Eval(new string('(', 500) + "1" + new string(')', 500)));
        Assert.Null(Eval(string.Join("+", Enumerable.Repeat("1", 200))));
        Assert.Null(Eval("10^400"));
    }

    [Fact]
    public void Separators_follow_the_culture_and_copies_have_no_grouping()
    {
        var german = Calculator.TryEvaluate("1234567.5*1", CultureInfo.GetCultureInfo("de-DE"))!;
        var us = Calculator.TryEvaluate("1234567.5*1", s_us)!;

        Assert.Equal(("1.234.567,5", "1234567,5"), (german.Display, german.CopyText));
        Assert.Equal(("1,234,567.5", "1234567.5"), (us.Display, us.CopyText));
        Assert.Equal("1E+20", Eval("10^20"));
    }
}

public class LauncherSearchTests
{
    private static LauncherOptions Options(bool web = true, bool calculator = true) =>
        new(web ? WebSearch.From(new WebSearchSettings()) : null, calculator, CultureInfo.GetCultureInfo("en-US"));

    [Fact]
    public void Tool_matches_rank_above_the_web_search_that_ends_the_list()
    {
        var results = LauncherSearch.Find("gif", ToolRegistry.All, Options());

        Assert.Equal("video-to-gif", Assert.IsType<ToolResult>(results[0]).Tool.Id);
        Assert.Equal("gif", Assert.IsType<WebResult>(results[^1]).Target.Query);
        Assert.Single(results.OfType<WebResult>());
    }

    [Fact]
    public void Text_that_matches_no_tool_offers_only_the_web_search()
    {
        var result = Assert.Single(LauncherSearch.Find("weather tomorrow", ToolRegistry.All, Options()));

        Assert.Equal("DuckDuckGo", Assert.IsType<WebResult>(result).Target.SiteName);
    }

    [Fact]
    public void A_calculation_comes_first()
    {
        var results = LauncherSearch.Find("12*(3+4)", ToolRegistry.All, Options());

        Assert.Equal("84", Assert.IsType<CalculationResult>(results[0]).Calculation.Display);
        Assert.IsType<WebResult>(results[^1]);
    }

    [Fact]
    public void A_bang_shows_its_site_and_nothing_else()
    {
        var result = Assert.Single(LauncherSearch.Find("!yt compress video", ToolRegistry.All, Options()));

        Assert.Equal(("YouTube", true), (((WebResult)result).Target.SiteName, ((WebResult)result).Target.FromBang));
    }

    [Fact]
    public void Turning_features_off_removes_their_rows()
    {
        Assert.Empty(LauncherSearch.Find("weather tomorrow", ToolRegistry.All, Options(web: false)));
        Assert.Empty(LauncherSearch.Find("2+2", ToolRegistry.All, Options(web: false, calculator: false)));
    }

    [Fact]
    public void Empty_text_lists_every_tool() =>
        Assert.Equal(ToolRegistry.All.Count, LauncherSearch.Find("", ToolRegistry.All, Options()).Count);
}

public class HotkeyTests
{
    private static HotkeyGesture Parse(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        return gesture!;
    }

    [Theory]
    [InlineData("Ctrl+Alt+Space", "Ctrl+Alt+Space")]
    [InlineData("alt + ctrl + space", "Ctrl+Alt+Space")]
    [InlineData("Win+Shift+k", "Shift+Win+K")]
    [InlineData("Ctrl+Alt+1", "Ctrl+Alt+1")]
    [InlineData("Control+F5", "Ctrl+F5")]
    public void Gestures_parse_and_print_in_a_standard_order(string text, string expected)
    {
        Assert.Equal(expected, Parse(text).ToString());
    }

    [Fact]
    public void Digits_are_stored_as_key_names() => Assert.Equal("D1", Parse("Ctrl+1").Key);

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+LeftShift")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Al-t")]
    public void Malformed_gestures_do_not_parse(string text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyVerdictLevel.Ok)]
    [InlineData("Ctrl+Shift+K", HotkeyVerdictLevel.Ok)]
    [InlineData("Alt+Space", HotkeyVerdictLevel.Warn)]
    [InlineData("Win+Space", HotkeyVerdictLevel.Warn)]
    [InlineData("Ctrl+Space", HotkeyVerdictLevel.Warn)]
    [InlineData("Win+Alt+Space", HotkeyVerdictLevel.Warn)]
    [InlineData("Win+J", HotkeyVerdictLevel.Warn)]
    [InlineData("Shift+A", HotkeyVerdictLevel.Reject)]
    [InlineData("F5", HotkeyVerdictLevel.Reject)]
    [InlineData("Alt+Tab", HotkeyVerdictLevel.Reject)]
    [InlineData("Alt+F4", HotkeyVerdictLevel.Reject)]
    [InlineData("Win+L", HotkeyVerdictLevel.Reject)]
    [InlineData("Ctrl+Shift+Escape", HotkeyVerdictLevel.Reject)]
    public void Advice_flags_risky_shortcuts(string text, HotkeyVerdictLevel level)
    {
        var verdict = HotkeyAdvice.Check(Parse(text));

        Assert.Equal(level, verdict.Level);
        Assert.Equal(level == HotkeyVerdictLevel.Ok, verdict.Message.Length == 0);
    }

    [Fact]
    public void Alt_space_warning_names_the_window_menu_and_powertoys()
    {
        Assert.Contains("PowerToys Run", HotkeyAdvice.Check(Parse("Alt+Space")).Message, StringComparison.Ordinal);
    }
}

public class LauncherSettingsTests
{
    [Fact]
    public void Web_search_bangs_and_calculator_settings_round_trip()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(Path.Combine(dir.Path, "settings.json"));
        var web = Bangs.Remove(Bangs.Add(new WebSearchSettings { Engine = "brave" }, new Bang("mdn", "MDN", "https://developer.mozilla.org/search?q={query}")), "a");

        store.Save(new AppSettings { WebSearch = web, CalculatorEnabled = false, LauncherHotkey = "Ctrl+Shift+K" });
        var loaded = store.Load();

        Assert.Equal(("brave", false, "Ctrl+Shift+K"), (loaded.WebSearch.Engine, loaded.CalculatorEnabled, loaded.LauncherHotkey));
        Assert.Equal("a", Assert.Single(loaded.WebSearch.RemovedBangs));
        Assert.Equal("mdn", Assert.Single(loaded.WebSearch.UserBangs).Trigger);
    }

    [Fact]
    public void Settings_from_0_1_0_get_the_new_defaults()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """{ "Theme": "Dark", "LauncherHotkey": "Ctrl+Alt+Space" }""");

        var loaded = new SettingsStore(path).Load();

        Assert.True(loaded.WebSearch.Enabled);
        Assert.True(loaded.CalculatorEnabled);
        Assert.Equal(SearchEngines.DefaultId, loaded.WebSearch.Engine);
    }
}
