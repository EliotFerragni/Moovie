using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Moovie.Core.Localization;
using Moovie.Core.Model;

namespace Moovie.App.ViewModels;

/// <summary>One entry in the candidate chooser: a title the user can pick for the selected file.</summary>
public sealed partial class CandidateViewModel(Candidate candidate) : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _poster;

    public Candidate Candidate { get; } = candidate;

    public string Title => Candidate.Title;

    public string Year => Candidate.Year?.ToString() ?? "—";

    public string Kind => Candidate.Kind == MediaKind.TvEpisode ? Strings.Get("pane.tvShow") : Strings.Get("pane.movie");

    /// <summary>Original title, shown only when it differs from the displayed one.</summary>
    public string? OriginalTitle =>
        string.IsNullOrWhiteSpace(Candidate.OriginalTitle)
        || string.Equals(Candidate.OriginalTitle, Candidate.Title, StringComparison.Ordinal)
            ? null
            : Candidate.OriginalTitle;

    public bool HasOriginalTitle => OriginalTitle is not null;

    public string Overview => string.IsNullOrWhiteSpace(Candidate.Overview)
        ? Strings.Get("pane.noOverview")
        : Candidate.Overview;

    /// <summary>Match confidence as a percentage, so the user can see why this was ambiguous.</summary>
    public string ConfidenceText => $"{Candidate.Score * 100:0}% match";

    public string TmdbReference => $"TMDB {Candidate.TmdbId}";
}
