using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Moovie.Core.Model;
using Moovie.Core.Localization;

namespace Moovie.Core.Writing;

/// <summary>The outcome of checking a template, ready to show next to the editor.</summary>
public sealed record TemplateValidation(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static readonly TemplateValidation Ok = new([]);
}

/// <summary>
/// A parsed, reusable rename template.
/// </summary>
/// <remarks>
/// Grammar:
/// <list type="bullet">
/// <item><c>{name}</c> or <c>{name:format}</c>: a value from <see cref="RenameTokens"/>.
/// Numbers take digit padding (<c>{season:00}</c> → <c>01</c>), dates take .NET format strings
/// (<c>{airDate:yyyy-MM-dd}</c>), text takes <c>upper</c>, <c>lower</c> or <c>title</c>, and
/// <c>{resolution:short}</c> writes 2160p as <c>4k</c>.</item>
/// <item><c>&lt; … &gt;</c>: an optional segment, dropped entirely when every token inside it
/// is empty. This is what keeps <c>&lt; - {episodeTitle}&gt;</c> from leaving a dangling
/// separator. Angle brackets were chosen because no filesystem allows them in a name, so
/// square brackets stay free for the common <c>Movie (2019) [2160p]</c> style.</item>
/// <item><c>{{</c> and <c>}}</c>: literal braces.</item>
/// </list>
/// </remarks>
public sealed class RenameTemplate
{
    private abstract record Node;

    private sealed record TextNode(string Text) : Node;

    private sealed record TokenNode(RenameToken Token, string? Format) : Node;

    private sealed record OptionalNode(IReadOnlyList<Node> Children) : Node;

    private static readonly Regex NumberFormat = new(@"^(?:[0#]{1,6}|[Dd]\d{0,2})$", RegexOptions.Compiled);

    private static readonly DateTime SampleDate = new(2021, 3, 8);

    private readonly IReadOnlyList<Node> _nodes;

    public string Text { get; }

    public TemplateValidation Validation { get; }

    /// <summary>True when the template places the extension itself, so it must not also be appended.</summary>
    public bool UsesExtension { get; }

    private RenameTemplate(string text, IReadOnlyList<Node> nodes, TemplateValidation validation)
    {
        Text = text;
        _nodes = nodes;
        Validation = validation;
        UsesExtension = ContainsExtensionToken(nodes);
    }

    private static bool ContainsExtensionToken(IEnumerable<Node> nodes) =>
        nodes.Any(node => node switch
        {
            TokenNode token => token.Token.Name == "ext",
            OptionalNode optional => ContainsExtensionToken(optional.Children),
            _ => false,
        });

    /// <summary>Parses <paramref name="text"/>, collecting any problems into <see cref="Validation"/>.</summary>
    public static RenameTemplate Parse(string? text)
    {
        text ??= string.Empty;
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
            errors.Add(Strings.Get("template.empty"));
        if (text.Contains('/') || text.Contains('\\'))
            errors.Add(Strings.Get("template.pathSeparator"));

        var nodes = ParseNodes(text, errors);
        return new RenameTemplate(text, nodes, new TemplateValidation(errors));
    }

    /// <summary>Convenience for callers that only care whether a template is usable.</summary>
    public static TemplateValidation Validate(string? text) => Parse(text).Validation;

    private static List<Node> ParseNodes(string text, List<string> errors)
    {
        var root = new List<Node>();
        var stack = new Stack<List<Node>>();
        var current = root;
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length == 0)
                return;
            current.Add(new TextNode(literal.ToString()));
            literal.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            // Doubled braces are literals.
            if (c is '{' or '}' && i + 1 < text.Length && text[i + 1] == c)
            {
                literal.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '{':
                {
                    var end = text.IndexOf('}', i + 1);
                    if (end < 0)
                    {
                        errors.Add(Strings.Format("template.unclosedBrace", i + 1));
                        literal.Append(c);
                        break;
                    }

                    FlushLiteral();
                    var body = text[(i + 1)..end];
                    current.Add(ParseToken(body, errors));
                    i = end;
                    break;
                }

                case '<':
                    FlushLiteral();
                    stack.Push(current);
                    current = [];
                    break;

                case '>':
                    if (stack.Count == 0)
                    {
                        errors.Add(Strings.Format("template.unmatchedOptionalClose", i + 1));
                        literal.Append(c);
                        break;
                    }

                    FlushLiteral();
                    var children = current;
                    current = stack.Pop();
                    current.Add(new OptionalNode(children));
                    break;

                case '}':
                    errors.Add(Strings.Format("template.unmatchedBrace", i + 1));
                    literal.Append(c);
                    break;

                default:
                    literal.Append(c);
                    break;
            }
        }

        FlushLiteral();

        while (stack.Count > 0)
        {
            errors.Add(Strings.Get("template.unclosedOptional"));
            var children = current;
            current = stack.Pop();
            current.Add(new OptionalNode(children));
        }

        return current;
    }

    private static Node ParseToken(string body, List<string> errors)
    {
        var colon = body.IndexOf(':');
        var name = (colon < 0 ? body : body[..colon]).Trim();
        var format = colon < 0 ? null : body[(colon + 1)..];

        var token = RenameTokens.Find(name);
        if (token is null)
        {
            errors.Add(Strings.Format("template.unknownToken", "{" + name + "}"));
            return new TextNode(string.Empty);
        }

        if (!string.IsNullOrEmpty(format) && !IsFormatValid(token, format, out var reason))
            errors.Add(Strings.Format("template.invalidFormat", "{" + name + ":" + format + "}", reason));

        return new TokenNode(token, string.IsNullOrEmpty(format) ? null : format);
    }

    private static bool IsFormatValid(RenameToken token, string format, out string reason)
    {
        if (token.ExtraFormats.Contains(format, StringComparer.Ordinal))
        {
            reason = string.Empty;
            return true;
        }

        switch (token.Kind)
        {
            case TokenValueKind.Number:
                if (NumberFormat.IsMatch(format))
                {
                    reason = string.Empty;
                    return true;
                }

                reason = Strings.Get("template.numberFormats");
                return false;

            case TokenValueKind.Date:
                try
                {
                    _ = SampleDate.ToString(format, CultureInfo.InvariantCulture);
                    reason = string.Empty;
                    return true;
                }
                catch (FormatException)
                {
                    reason = Strings.Get("template.dateFormats");
                    return false;
                }

            default:
                if (format is "upper" or "lower" or "title")
                {
                    reason = string.Empty;
                    return true;
                }

                reason = token.ExtraFormats.Count == 0
                    ? Strings.Get("template.textFormats")
                    : Strings.Format("template.textFormatsExtra", string.Join(", ", token.ExtraFormats));
                return false;
        }
    }

    /// <summary>
    /// Renders the template for <paramref name="metadata"/>. Returns the bare stem: no extension,
    /// no separator substitution, no sanitisation; <see cref="RenameEngine"/> does those.
    /// </summary>
    /// <param name="omitResolutionAtOrBelow">
    /// A resolution at or below which <c>{resolution}</c> renders empty, for the ordinary ones
    /// not worth naming. Null or empty writes every resolution. Wrap the token in an optional
    /// <c>&lt;…&gt;</c> group and the brackets around it go with it.
    /// </param>
    public string Render(
        MediaMetadata metadata, string? extension = null, string? omitResolutionAtOrBelow = null)
    {
        var output = new StringBuilder();
        RenderNodes(_nodes, metadata, extension, omitResolutionAtOrBelow, output);
        return output.ToString();
    }

    /// <returns>True when at least one token in <paramref name="nodes"/> produced a value.</returns>
    private static bool RenderNodes(
        IReadOnlyList<Node> nodes, MediaMetadata metadata, string? extension,
        string? omitResolutionAtOrBelow, StringBuilder output)
    {
        var sawToken = false;
        var producedValue = false;

        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    output.Append(text.Text);
                    break;

                case TokenNode tokenNode:
                {
                    sawToken = true;
                    var value = Resolve(tokenNode, metadata, extension, omitResolutionAtOrBelow, output);
                    if (!string.IsNullOrEmpty(value))
                    {
                        producedValue = true;
                        output.Append(value);
                    }

                    break;
                }

                case OptionalNode optional:
                {
                    var start = output.Length;
                    var inner = RenderNodes(
                        optional.Children, metadata, extension, omitResolutionAtOrBelow, output);
                    if (!inner)
                        output.Length = start; // Every token inside was empty: drop the segment whole.
                    else
                        producedValue = true;
                    sawToken = true;
                    break;
                }
            }
        }

        return producedValue || !sawToken;
    }

    private static string? Resolve(
        TokenNode node, MediaMetadata m, string? extension,
        string? omitResolutionAtOrBelow, StringBuilder output) =>
        node.Token.Name switch
        {
            "title" => FormatText(m.Title, node.Format),
            "originalTitle" => FormatText(m.OriginalTitle, node.Format),
            "year" => FormatNumber(m.EffectiveYear, node.Format),
            "show" => FormatText(m.ShowName, node.Format),
            "season" => FormatNumber(m.Season, node.Format),
            "episode" => FormatEpisodes(m.Episodes, node.Format, output),
            "episodeTitle" => m.Kind == MediaKind.TvEpisode ? FormatText(m.Title, node.Format) : null,
            "airDate" => FormatDate(m.ReleaseDate, node.Format),
            "releaseDate" => FormatDate(m.ReleaseDate, node.Format),
            "genre" => FormatText(m.Genres.FirstOrDefault(), node.Format),
            "studio" => FormatText(m.Studio, node.Format),
            "network" => FormatText(m.Network, node.Format),
            // An ordinary resolution renders as nothing, so an optional group around it vanishes
            // rather than leaving empty brackets behind.
            "resolution" => VideoResolution.IsAtOrBelow(m.Resolution, omitResolutionAtOrBelow)
                ? null
                : FormatResolution(m.Resolution, node.Format),
            "tmdbId" => FormatNumber(m.TmdbId, node.Format),
            "imdbId" => FormatText(m.ImdbId, node.Format),
            "ext" => FormatText(extension, node.Format),
            _ => null,
        };

    /// <summary>
    /// Text formatting plus <c>short</c>, which writes 2160p as <c>4k</c>. Shortening replaces
    /// the value rather than decorating it, so it is not combined with a case format.
    /// </summary>
    private static string? FormatResolution(string? value, string? format) =>
        format == "short"
            ? FormatText(VideoResolution.Shorten(value), null)
            : FormatText(value, format);

    private static string? FormatText(string? value, string? format)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return format switch
        {
            "upper" => value.ToUpperInvariant(),
            "lower" => value.ToLowerInvariant(),
            "title" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant()),
            _ => value,
        };
    }

    private static string? FormatNumber(int? value, string? format) =>
        value is null ? null : value.Value.ToString(format ?? "0", CultureInfo.InvariantCulture);

    private static string? FormatDate(DateTime? value, string? format) =>
        value is null ? null : value.Value.ToString(format ?? "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders one or more episode numbers. When the template put an episode marker right before
    /// the token, a multi-episode file repeats it: <c>E{episode:00}</c> yields <c>E01-E02</c>,
    /// the form media servers recognise. Any other prefix (the <c>x</c> of <c>1x01</c>) joins
    /// bare, as <c>1x01-02</c>.
    /// </summary>
    private static string? FormatEpisodes(List<int> episodes, string? format, StringBuilder output)
    {
        if (episodes.Count == 0)
            return null;

        var rendered = episodes.Select(e => e.ToString(format ?? "0", CultureInfo.InvariantCulture)).ToList();
        if (rendered.Count == 1)
            return rendered[0];

        var prefix = EpisodeMarkerAlreadyWritten(output);
        return string.Join('-', rendered.Select((value, index) => index == 0 ? value : prefix + value));
    }

    /// <summary>The "E" of an "S01E" already emitted, or empty when the template used something else.</summary>
    private static string EpisodeMarkerAlreadyWritten(StringBuilder output)
    {
        if (output.Length == 0)
            return string.Empty;
        var last = output[^1];
        return last is 'e' or 'E' ? last.ToString() : string.Empty;
    }
}
