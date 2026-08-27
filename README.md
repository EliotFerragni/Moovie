# Video Metadata Filler

A desktop app that fills the metadata of movie and TV files from
[The Movie Database](https://www.themoviedb.org). Drop files in, review what was matched,
correct anything that is wrong, then write it all in one go.

Runs on **Windows, macOS and Linux** as a single self-contained executable — nothing to
install, no .NET needed on the machine.

---

## What it does

- **Reads the filename** to work out whether a file is a movie or a TV episode, and for
  episodes to pull out the season and episode numbers.
- **Looks the title up on TMDB** and picks one automatically when it is confident.
- **Asks you when it is not.** Ambiguous files are flagged in the list and get a chooser
  with posters, years and overviews.
- **Lets you edit every field**, one file at a time or many at once.
- **Writes tags and cover art** into the MP4 container in one batch, and optionally renames
  the files to a pattern you define.

### The window

The left pane lists your files, each with a status icon:

| Icon | Meaning |
|:---:|---|
| ○ | Not looked up yet |
| ✓ | Matched automatically |
| ? | Needs you to choose between candidates |
| ✕ | Nothing on TMDB matches |
| ✎ | Edited by you, ready to apply |
| ✓✓ | Written |
| ⚠ | Something went wrong (the reason is shown in the preview pane) |

The right pane previews and edits the metadata for whatever is selected.

**Selecting several files at once** shows the fields they have in common: a field where they
all agree shows that value, and one where they differ shows `— multiple values —`. Typing
into any field applies it to every selected file. Fields that are inherently per-file — the
episode number, the TMDB id — go read-only rather than stamping one value across a whole
season.

---

## Getting started

1. **Get a TMDB API key.** Create a free account at
   [themoviedb.org](https://www.themoviedb.org/signup), then go to
   Settings → API and copy the **API Key (v3 auth)**.
2. Open **Settings…** in the app, paste the key, and press **Test key**.
3. Drop some files onto the left pane, or use **Add files…** / **Add folder…**. Folders are
   searched recursively, so a whole season can go in at once.
4. Review the list. Sort out anything marked `?` or `✕`.
5. Press **Apply to all ready files**.

Only `.mp4` and `.m4v` files are handled. That is deliberate: MP4 tags can be written in
pure managed code, so the app ships as one file with no helper binaries alongside it.

---

## Filenames it understands

Movies:

```
Inception.2010.1080p.BluRay.x264-SPARKS.mp4     → Inception (2010)
Dune.Part.Two.2024.2160p.WEB-DL.DDP5.1.mp4     → Dune Part Two (2024)
Amelie (2001) [1080p] [YTS.AG].mp4             → Amelie (2001)
Blade.Runner.2049.2017.1080p.mp4               → Blade Runner 2049 (2017)
Movies/Arrival (2016)/video.mp4                → Arrival (2016)
```

TV episodes:

```
Breaking.Bad.S01E01.1080p.BluRay.mp4           → Breaking Bad S01E01
Friends - 3x14 - The One With....mp4           → Friends S03E14
Doctor Who Season 4 Episode 10.mp4             → Doctor Who S04E10
Firefly.S01E01-E02.1080p.mp4                   → Firefly S01E01-E02
The.Daily.Show.2021.03.08.mp4                  → located by air date
Breaking Bad/Season 02/04 - Down.mp4           → Breaking Bad S02E04
Shows/Andor/S01E04.1080p.mp4                   → Andor S01E04
```

Some deliberate details:

- A year in the title is not mistaken for the release year: `Blade Runner 2049` keeps its
  2049, `2012` and `1917` stay whole, and `Star Wars Episode 4 ... 1977` stays a movie.
- Words that are both release markers and real title words survive: `Charlotte's Web`,
  `The Italian Job`, `Uncut Gems`, `Mr. Holland's Opus`, `DC League of Super-Pets`.
- When the filename gives nothing away, the folder tree is consulted: a `Season 02` parent
  settles the season, and a placeholder name like `video.mp4` falls back to the containing
  folder.

If a name still defeats the parser, the preview pane has a free-text TMDB search and a box
for pasting a TMDB id directly.

---

## Languages

Set a default metadata language in Settings. Any single file can be switched to a different
language in the preview pane and refetched — useful when a title has no translation in your
usual language.

Two things make this safe:

- A refetch **keeps every field you edited by hand**. Edited fields are marked with a small
  dot, and a refetch writes over everything except those.
- When TMDB has no translation for a field, it is filled from English and labelled
  *filled in from English*, so a blank-looking translation is never a mystery.

---

## Renaming

Renaming is off by default. Turn it on with the checkbox at the bottom of the window, and the
file list shows `→ new name` under each file before anything happens.

Movies and TV shows get their own template, both editable in Settings with a clickable field
palette, digit spinners and a live preview — so the everyday adjustments never need any
knowledge of the syntax.

**Defaults**

```
Movies:    {title} ({year})
TV shows:  {show} - S{season:00}E{episode:00}< - {episodeTitle}>
```

**Fields**

`{title}` `{originalTitle}` `{year}` `{show}` `{season}` `{episode}` `{episodeTitle}`
`{airDate}` `{releaseDate}` `{genre}` `{studio}` `{network}` `{resolution}` `{tmdbId}`
`{imdbId}` `{ext}`

**Formats**

| Kind | Syntax | Result |
|---|---|---|
| Digits | `{season:0}` `{season:00}` `{episode:000}` | `1` · `01` · `007` |
| Dates | `{airDate:yyyy-MM-dd}` `{releaseDate:yyyy}` | `2021-03-08` · `2017` |
| Text | `{title:upper}` `{title:lower}` `{title:title}` | case conversion |

**Optional parts.** Wrap a section in angle brackets and it disappears when the fields inside
it are empty:

```
{show} - S{season:00}E{episode:00}< - {episodeTitle}>
```

With no episode title on TMDB that gives `Severance - S01E01` rather than a dangling ` - `.
Angle brackets were chosen because no filesystem allows them in a name, which leaves square
brackets free for the common style:

```
{title} ({year}) [{resolution}]   →   Blade Runner 2049 (2017) [2160p].mp4
```

Write `{{` and `}}` for literal braces.

**Multi-episode files** repeat the episode marker, so `E{episode:00}` renders as `E01-E02` —
the form media servers recognise.

Renaming happens in place: only the filename changes, never the folder. Characters that are
illegal on Windows are stripped whatever platform you are on, so a library stays portable, and
name collisions get a ` (2)` suffix.

---

## What gets written into the file

The iTunes-style MP4 atoms that Plex, Jellyfin, Emby, Infuse and the Apple TV app read:

- `stik` — media type, 9 for a movie and 10 for an episode. This one matters most: without
  it players ignore the TV atoms entirely.
- `©nam` title · `©day` date · `©gen` genres · `desc` and `ldes` descriptions · `covr` cover art
- `tvsh` show · `tvsn` season · `tves` episode · `tven` episode id · `tvnn` network
- `©ART` / `aART` artist · `©alb` album (`Show, Season 1` for episodes) · `©wrt` writer
- `hdvd` HD flag, from the resolution in the filename
- `iTunMOVI` — an Apple property list holding cast, directors and screenwriters
- `iTunEXTC` — the certification string Apple devices display

Every write is verified by re-reading the file. If it fails, that file is marked and the batch
carries on. Switching a file between movie and episode clears the atoms of the other kind, so
no stale season numbers are left behind.

There is an optional "keep a `.bak` copy" setting for the cautious.

---

## Building it

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download) and nothing else.

```bash
dotnet test                     # 152 tests
dotnet run --project src/VideoMetadataFiller.App
```

To produce a self-contained executable:

```bash
./build/publish.sh              # for the machine you are on
./build/publish.sh win-x64      # for one target
./build/publish.sh all          # every target
```

On Windows use `build/publish.ps1` instead. Output lands in `artifacts/<runtime>/`.

Targets: `win-x64` `win-arm64` `osx-x64` `osx-arm64` `linux-x64` `linux-arm64`.
Builds are roughly 45–80 MB because they bundle the runtime.

macOS builds are assembled into a `Video Metadata Filler.app` bundle. They are unsigned, so
Gatekeeper needs persuading once:

```bash
xattr -dr com.apple.quarantine "Video Metadata Filler.app"
```

Linux builds still rely on the host's usual desktop libraries (fontconfig, X11 or Wayland).

The app also accepts paths on the command line, so it works as an "Open with" target:

```bash
VideoMetadataFiller ~/Videos/Season\ 02
```

---

## Layout

```
src/VideoMetadataFiller.Core/     no UI dependencies, fully unit-tested
  Parsing/     FilenameParser, JunkTokens
  Tmdb/        ITmdbService, TmdbService
  Matching/    TitleScorer, MatchResolver
  Writing/     Mp4TagWriter, RenameTemplate, RenameEngine
  Settings/    AppSettings, SettingsStore

src/VideoMetadataFiller.App/      Avalonia UI
  ViewModels/  MainWindowViewModel, PreviewPaneViewModel, FieldEditor
  Views/       MainWindow, PreviewPane, SettingsWindow

tests/VideoMetadataFiller.Core.Tests/
```

`Core` has no reference to Avalonia, and `ITmdbService` is an interface, so the parser, the
matching policy, the tag writer and the rename engine are all tested without a UI, a network
or an API key. The parser's test corpus is the place to add a filename that gets parsed wrong.

Settings live in `%APPDATA%\VideoMetadataFiller\settings.json` on Windows and
`~/Library/Application Support/VideoMetadataFiller/settings.json` on macOS.

---

This product uses the TMDB API but is not endorsed or certified by TMDB.
