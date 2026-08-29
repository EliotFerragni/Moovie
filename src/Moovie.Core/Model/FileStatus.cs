namespace Moovie.Core.Model;

/// <summary>
/// Lifecycle of a single file in the batch. Drives the status icon in the file list.
/// </summary>
public enum FileStatus
{
    /// <summary>Queued, nothing done yet.</summary>
    Pending,

    /// <summary>Filename parsed and TMDB lookup in flight.</summary>
    Searching,

    /// <summary>A single title was picked automatically with enough confidence.</summary>
    Matched,

    /// <summary>Several plausible titles: the user has to pick one.</summary>
    NeedsChoice,

    /// <summary>TMDB returned nothing usable.</summary>
    NotFound,

    /// <summary>The user changed at least one field (still ready to apply).</summary>
    Edited,

    /// <summary>Tags are being written.</summary>
    Applying,

    /// <summary>Tags written (and renamed, if enabled).</summary>
    Applied,

    /// <summary>Lookup or write failed; see the error message.</summary>
    Failed,
}

public static class FileStatusExtensions
{
    /// <summary>True when the file has metadata resolved and can be written.</summary>
    public static bool IsReadyToApply(this FileStatus status) =>
        status is FileStatus.Matched or FileStatus.Edited;

    /// <summary>True when the app is mid-flight on this file and it should not be edited.</summary>
    public static bool IsBusy(this FileStatus status) =>
        status is FileStatus.Searching or FileStatus.Applying;
}
