namespace Moovie.Core.Settings;

/// <summary>
/// The settings that shape a rendered filename, passed as one value because every step of
/// renaming needs all of them and two of the three are strings that would otherwise sit next to
/// each other in a positional argument list.
/// </summary>
/// <param name="Separator">What goes between words.</param>
/// <param name="OmitResolutionAtOrBelow">
/// A resolution at or below which <c>{resolution}</c> renders empty. Null or empty writes them all.
/// </param>
/// <param name="IllegalCharacterReplacement">
/// What takes the place of a character no Windows filename may hold. Empty drops them.
/// </param>
public sealed record NamingRules(
    SeparatorStyle Separator = SeparatorStyle.Space,
    string? OmitResolutionAtOrBelow = null,
    string IllegalCharacterReplacement = "")
{
    /// <summary>Spaces, every resolution written, forbidden characters dropped.</summary>
    public static readonly NamingRules Default = new();
}
