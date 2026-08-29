using Moovie.Core.Localization;
using Moovie.Core.Writing;

namespace Moovie.App.ViewModels;

/// <summary>One row of the "what applying will change" list, ready to bind.</summary>
/// <remarks>
/// Both sides always have something to show: an absent value reads as "(empty)" on the left and
/// "(cleared)" on the right, so a row never looks like a rendering fault.
/// </remarks>
public sealed class MetadataChangeViewModel(MetadataChange change)
{
    public string Label => change.Label;

    public string Current => change.Current ?? Strings.Get("diff.empty");

    public string Pending => change.Pending ?? Strings.Get("diff.cleared");

    /// <summary>Struck through when applying will empty this field.</summary>
    public bool IsCleared => change.Kind == ChangeKind.Cleared;

    /// <summary>Nothing is being replaced, so the left-hand side is a placeholder, not a value.</summary>
    public bool IsAdded => change.Kind == ChangeKind.Added;
}
