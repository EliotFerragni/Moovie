using Avalonia.Markup.Xaml;
using VideoMetadataFiller.Core.Localization;

namespace VideoMetadataFiller.App.Localization;

/// <summary>
/// Looks a string up for XAML: <c>Text="{loc:T pane.refetch}"</c>.
/// </summary>
/// <remarks>
/// Resolved once, when the XAML is loaded, which is why changing the language asks for a restart.
/// Making it live would mean every one of these becoming an observable binding.
/// </remarks>
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
