<h1 align="center">
  <img src="src/Moovie.App/Assets/Icon/app-256.png" width="128" alt="">
  <br>
  Moovie
</h1>

<p align="center">
  <a href="https://github.com/EliotFerragni/Video-metadata-filler/actions/workflows/build.yml"><img src="https://github.com/EliotFerragni/Video-metadata-filler/actions/workflows/build.yml/badge.svg?branch=main" alt="build"></a>
  <img src="https://img.shields.io/badge/Claude%20Code-%23D97757.svg?style=for-the-badge&logo=claudecode&logoColor=white" alt="Claude Code">
</p>

Fills the metadata of movie and TV files from
[The Movie Database](https://www.themoviedb.org). Drop files in, review what was matched,
correct anything that is wrong, then write it all in one go.

Runs on **Windows, macOS and Linux** as a single self-contained executable — nothing to
install, no .NET needed on the machine.

---

## What it does

- **Reads what a file already holds** when you add it — its existing tags, its name and its
  video track — so the form opens with the file's own content rather than a blank sheet.
- **Reads the filename** to work out whether a file is a movie or a TV episode, and for
  episodes to pull out the season and episode numbers, filling only what the tags did not.
- **Looks the title up on TMDB** when you ask it to, and picks one automatically when it is
  confident. Adding files never looks anything up on its own.
- **Asks you when it is not.** Ambiguous files are flagged in the list and get a chooser
  with posters, years and overviews.
- **Lets you edit every field**, one file at a time or many at once.
- **Says what will change** before anything is written: the tags the file already carries are
  read back out and shown beside the new ones, cover art included, and either side can be
  picked field by field.
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

Each row carries the artwork the file is going to end up with at its left — the cover already
embedded in the file before any lookup, then whatever the lookup or you chose, and always the
same image the preview pane shows. With show posters chosen,
a run of files from the same show reads as one block and a row that landed on the wrong title
stands out immediately; with episode stills or season posters the rows differ from each other,
which is simply what those kinds are. Landscape artwork is letterboxed in the tile rather than
cropped to a sliver.

The right pane previews and edits the metadata for whatever is selected.

Anything you change by hand is marked: the field is outlined and gets a dot beside its label,
and the same dot appears next to **Type** and the artwork, neither of which is a form field.
**Discard edits** puts the whole selection back to what TMDB returned — fields, type and
artwork alike. It restores a copy kept from the last fetch, so it is instant and needs no
network, unlike **Refetch**, which goes back to TMDB and deliberately *keeps* your edits.

**Selecting several files at once** shows the fields they have in common: a field where they
all agree shows that value, and one where they differ shows `— multiple values —`. Typing
into any field applies it to every selected file. Fields that are inherently per-file — the
episode number, the TMDB id — go read-only rather than stamping one value across a whole
season. The language applies to the whole selection too, reading `— multiple values —` when the
files are not all in the same one.

### What will be written

The pane reads the tags a file already carries and shows them beside what the lookup returned,
so the header line answers the two questions worth asking before overwriting anything — *is
this one already done?* and *what exactly am I about to replace?*

```
WHAT WILL BE WRITTEN    9 field(s) will change
```

Expanding it lists the fields as two columns, **In this file** and **From TMDB**, with the one
that **Apply** will actually write outlined. Click the other to swap them. That is the whole
point of the section: a lookup is right about most fields and wrong about one, and picking
through it field by field beats accepting all of it or none.

- **Taking the file's value counts as a hand edit**, so a refetch keeps it and **Discard edits**
  puts it back. Taking TMDB's value clears that mark, since the field is once again exactly what
  the lookup returned.
- **A field pointed at the file's own value stays listed.** It is no longer a change, so it is
  not counted in the header, but it has to remain on screen or there would be no way back.
- **A field typed in by hand matches neither column**, so neither is outlined and the row spells
  out what will be written underneath.
- **The cover art row shows both images**, the one embedded in the file beside the one TMDB
  returned. A thumbnail is the only way to tell a replacement worth making from one that swaps a
  good cover for a worse one.
- **The HD flag can only be taken from TMDB.** 1080i, 1080p and 1440p all store a 2, so the
  file's side has no resolution to hand back and is dimmed.

A value that applying would empty is struck through and marked *(cleared)*, which is how
switching a file from episode to movie shows the show name and season being wiped rather than
doing it quietly. A file that already holds exactly what would be written says so instead —
which is also what you see after applying, since the pane re-reads the file it just wrote.

Three details keep the comparison honest rather than decorative:

- **Only atoms that actually get written are compared.** A TMDB id has no MP4 atom, so listing
  it would promise a change that never happens.
- **Every column is in its written form.** One `©day` atom holds either a full date or a bare
  year, so the comparison is over the string that reaches the container, not over the two fields
  behind it. The TV fields follow the type currently set, so a value a row offers is always one
  the writer would really put in the file.
- **Cover art is never reported as cleared**, because applying with no new image deliberately
  leaves the embedded one alone. Taking the file's own cover is how you say *leave this one*.

The file is read once per selection; editing a field re-compares against it immediately, so the
diff answers for what is on screen rather than for what TMDB last returned.

---

## Getting started

1. **Get a TMDB API key.** Create a free account at
   [themoviedb.org](https://www.themoviedb.org/signup), then go to
   Settings → API and copy the **API Key (v3 auth)**.
2. Open **Settings…** in the app, paste the key, and press **Test key**.
3. Drop some files onto the left pane, or use **Add files…** / **Add folder…**. Folders are
   searched recursively, so a whole season can go in at once.
4. Press **Look up all**, or select a few files and press **Look up selected**.
5. Review the list. Sort out anything marked `?` or `✕`.
6. Press **Apply to all ready files**.

Adding files does not look them up. Dropping a folder is how files get into the list, not a
decision to spend an API call on every one of them and overwrite whatever they already carry,
so the lookup waits to be asked for.

What needs no network happens on adding. Each file is opened, the tags it already carries are
read, its name is parsed and its video track probed, and the form is filled from all three —
**the file's own tags first**, with the filename supplying only what the file does not have. So
a file tagged by an earlier pass opens showing its own content, right down to its cover art,
and there is something real to review before a single API call. A description that came from the
filename rather than from the file says so with a trailing `?`.

A lookup then replaces those values with TMDB's, keeping anything you typed yourself, and the
diff shows both sides so you can put any field back. Editing a file before looking it up is a
complete path in itself: the edit makes the file ready to apply, and **Discard edits** goes back
to what the file holds rather than needing a lookup to undo against.

The only thing the file's own tags do not win is the resolution. The container stores a coarse
HD flag, not a frame size, so `1080p` always comes from the video track.

Only `.mp4` and `.m4v` files are handled. That is deliberate: MP4 tags can be written in
pure managed code, so the app ships as one file with no helper binaries alongside it.

---

## Running it on a server

The app usually opens a window. It can instead run with no window at all and serve that same
window to a browser:

```bash
Moovie --web                       # every interface, port 8080
Moovie --web --port 9000 /media    # a port of your own, with a folder already loaded
Moovie --web --host 192.168.1.50   # one interface only
```

Then open `http://<that machine>:8080/` and you get the interface below, in a tab.

**The app runs on the server, not in your browser.** That is the whole point of the mode, and it
is what makes it useful for a NAS: the file list, the tag writer and the rename engine all act on
the machine serving the page, so **Add files…** browses *its* disks. A browser's own file dialog
would have been wrong twice over — it offers the wrong machine's files, and it hands back a
stream where the tag writer needs a path — so `--web` has its own file browser instead, which
lists the server's filesystem.

Nothing is duplicated to make this work. The interface is one set of views: a window hosts them on
the desktop, and `--web` hands the very same tree to Avalonia's `RemoteServer`, which renders it
into a frame buffer. The browser receives PNG frames and sends pointer and keyboard events back.
Everything else — the view models, the parser, the diff, the writer — has no idea which is running.

Two consequences worth knowing before you rely on it:

- **It is one session, not one per viewer.** Every tab shows the same app, in the same state, and
  each one can drive it. Two people on it at once will fight over the same cursor.
- **A frame is a picture, so only the changed part of one is sent.** Typing a character costs
  under 2 KB and lands in around 20 ms on a local network; a still window with a blinking caret
  costs a few hundred bytes a second. Scrolling a long list redraws most of the window and is
  genuinely expensive, so this is sized for a LAN rather than for the open internet. Nothing
  authenticates the page either, so it belongs behind whatever already guards the machine.
- **It costs about a tenth of a core to sit there.** That is Avalonia's headless dispatcher, not
  the serving: it is the same with nobody connected, and it is the floor a bare headless Avalonia
  app idles at. Drawing itself is driven by input, so a server nobody has open does no work of its
  own beyond that.

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

Both work across a selection. When a whole show has matched the wrong series, select every
episode, search for the right one, and the title you pick is applied to all of them at once —
each file keeps its own season and episode numbers and fetches its own entry, so a 24-episode
season is one search rather than 24.

---

## Languages

Two separate languages, and deliberately so: wanting a French interface with English metadata,
or the reverse, is a perfectly ordinary thing to want.

**The interface** is English, French, German or Italian, set in Settings, and follows the
operating system by default. It is applied at startup, so changing it asks for a restart —
the setting says as much. A translation missing a phrase falls back to English rather than
leaving a hole.

**The metadata** language is what gets fetched from TMDB. Set a default in Settings. Any single file can be switched to a different
language in the preview pane and refetched — useful when a title has no translation in your
usual language.

Two things make this safe:

- A refetch **keeps every field you edited by hand**. Edited fields are marked with a small
  dot, and a refetch writes over everything except those.
- When TMDB has no translation for a field, it is filled from English and labelled
  *filled in from English*, so a blank-looking translation is never a mystery.

Adding a language means dropping a `<tag>.json` next to
`src/Moovie.Core/Localization/en.json`, translating the values and listing the tag
in `Strings.Available`. A test fails if any translation drifts from the English key set or
loses a `{0}` placeholder. The TMDB language names in the metadata dropdown stay in English.

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
| Short | `{resolution:short}` | `2160p` → `4k` · `4320p` → `8k` |

**Optional parts.** Wrap a section in angle brackets and it disappears when the fields inside
it are empty:

```
{show} - S{season:00}E{episode:00}< - {episodeTitle}>
```

With no episode title on TMDB that gives `Severance - S01E01` rather than a dangling ` - `.
Angle brackets were chosen because no filesystem allows them in a name, which leaves square
brackets free for the common style:

```
{title} ({year})< [{resolution}]>   →   Blade Runner 2049 (2017) [2160p].mp4
```

Write `{{` and `}}` for literal braces.

**You do not have to remember any of this.** Each field in the palette under a template carries
its own vocabulary: hover one and it lists the forms worth writing it in, against what each
would produce for the sample shown below the box.

```
{season}       1          {resolution}         2160p
{season:00}    01         {resolution:short}   4k
{season:000}   001
```

A field is only offered a format that can change its value, which is why the resolution is not
shown three ways of being cased and the year is not shown three ways of being padded — a year
is always four digits, so `{year:00}` is the same row written twice. Anything left out is still
accepted, so no template that already works stops working; it is simply not advertised.

The examples are rendered rather than written down, so they follow the template's own sample —
a movie template's `{title}` shows a film, a TV one's shows an episode — and tests fail if a
sample ever loses a field the palette offers, if the palette offers a format the validator would
reject, or if an offered format leaves the value exactly as it was.

**Multi-episode files** repeat the episode marker, so `E{episode:00}` renders as `E01-E02` —
the form media servers recognise.

**Resolution** is read from the video track rather than from the name. A name only *claims* a
resolution, and stops claiming it the moment the file is renamed by a template that leaves
`{resolution}` out — after which the next pass over the same library would find nothing and
write an SD HD flag over a 4K film. The container is asked instead, so the answer survives
renaming and mislabelled releases alike; the name is still the fallback for a file whose video
track cannot be read.

Height and width are read as two separate opinions and the larger wins, because either alone
is wrong for material that is common. Height under-reports widescreen film, which is cropped
rather than letterboxed: a 2.39:1 transfer is 1920×800, and 800 is not 720p. Width under-reports
4:3 and anamorphic material, where the height is the honest number — 720×576 is `576p`, not
`480p`. Reading both and taking the larger gets each of them right, and a DVD-class frame stays
SD however far it is cropped: 720×406 is `480p`, not the `360p` that scaling 720 by 9/16 would
suggest.

**Not every resolution is worth naming.** Most libraries have a baseline that goes without
saying and only mark what beats it. Settings has **Leave the resolution out**: pick
`576p and below` and anything at or under it renders as nothing, while 720p and up are written
as usual. The direction sits on the choice rather than on the label above it, so it is still
there once the dropdown is closed and only the chosen value shows.

```
{title} ({year})< [{resolution}]>

  2160p  →  Blade Runner 2049 (2017) [2160p].mp4
   576p  →  Blade Runner 2049 (2017).mp4
```

The token comes out empty, so the optional `<…>` section around it takes the brackets and the
space with it. Written as a bare `[{resolution}]` instead, the empty brackets would stay — the
live preview in Settings shows a second line at the threshold whenever one is set, so you can
see which of the two you have written before saving.

`{resolution:short}` writes the two resolutions that have a shorter name — `2160p` as `4k`
and `4320p` as `8k` — and leaves the rest as they are, since `1080p` is already what people
write. `1440p` is deliberately not shortened: "2K" properly means a 1080p-class frame, so
borrowing it here would name the file wrongly.

This is a naming preference and nothing more. The resolution is still read from the video, still
shown in the preview pane, and still decides the HD flag written into the file; only the
filename leaves it out.

Renaming happens in place: only the filename changes, never the folder. Characters that are
illegal on Windows are stripped whatever platform you are on, so a library stays portable, and
name collisions get a ` (2)` suffix.

---

## Artwork

The image at the top of the preview pane is the one that will be embedded, downloaded at the
size set in Settings, so nothing is a stand-in. Episode stills are 16:9 and posters are 2:3;
both are shown whole rather than cropped to a fixed box. The caption underneath names what you
are looking at, e.g. `Season poster · w780`.

**Click the artwork to pick a different one.** That lists everything TMDB has for the title —
episode stills, season posters, show posters and backdrops — with the one in use outlined.
Artwork in your metadata language comes first, then textless art, then whatever TMDB rates
highest. A hand-picked image lasts until the file is refetched or you choose a different match,
at which point the automatic choice takes over again.

Which artwork gets chosen for you is a setting, separately for TV episodes (episode still,
season poster, show poster or show backdrop) and for movies (poster or backdrop). Not every
title has every kind — plenty of older episodes have no still — so a missing kind falls back to
the next best one for that medium rather than leaving the file bare.

Artwork is not a translation, so a kind missing in your language is taken from the original
language instead of being given up on: a season with a French entry but no French poster still
gets the English poster rather than quietly turning into a show poster. Artwork that did come
back in your language always wins, and unlike untranslated text this is not flagged in the
pane, because a poster with no words on it is not a translation gap.

Only the picker costs extra TMDB requests, and only when you open it. The kinds offered by the
setting all arrive with the metadata lookup itself.

---

## What gets written into the file

The iTunes-style MP4 atoms that Plex, Jellyfin, Emby, Infuse and the Apple TV app read:

- `stik` — media type, 9 for a movie and 10 for an episode. This one matters most: without
  it players ignore the TV atoms entirely.
- `©nam` title · `©day` date · `©gen` genres · `desc` and `ldes` descriptions · `covr` cover art
- `tvsh` show · `tvsn` season · `tves` episode · `tven` episode id · `tvnn` network
- `©ART` / `aART` artist · `©alb` album (`Show, Season 1` for episodes) · `©wrt` writer
- `hdvd` HD flag, from the frame size of the video itself
- `iTunMOVI` — an Apple property list holding cast, directors and screenwriters
- `iTunEXTC` — the certification string Apple devices display

Every write is verified by re-reading the file. If it fails, that file is marked and the batch
carries on. Switching a file between movie and episode clears the atoms of the other kind, so
no stale season numbers are left behind.

There is an optional "keep a `.bak` copy" setting for the cautious.

---

## Setting up a dev environment

The only hard requirement is the [.NET 10 SDK](https://dotnet.microsoft.com/download). There
is no `global.json`, so any 10.0.x will do. Everything below is either that, optional editor
tooling, or — on Linux only — desktop libraries a normal desktop install already has.

### Windows

```powershell
winget install Microsoft.DotNet.SDK.9
```

Or run the installer from the [download page](https://dotnet.microsoft.com/download). Open a
new terminal afterwards so `dotnet` is on `PATH`. Nothing else is needed: the Windows build
has no native dependencies beyond what ships with the OS.

### macOS

```bash
brew install --cask dotnet-sdk
```

Or use the `.pkg` from the download page — take the **Arm64** one on Apple Silicon and the
**x64** one on Intel. Xcode is not required; the command line tools are only needed if you
ever want to codesign a build, which the project does not do.

### Linux

Where your distro packages .NET 10, use it:

```bash
sudo apt install dotnet-sdk-10.0     # Debian / Ubuntu, where packaged
sudo dnf install dotnet-sdk-10.0     # Fedora
sudo pacman -S dotnet-sdk            # Arch
```

Distro packages lag a release or two, so on anything that does not have it yet the official
script works everywhere. Note that it installs into `~/.dotnet` and deliberately does not touch your
`PATH`, so you have to do that yourself:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

Put those two exports in your `~/.bashrc` or `~/.zshrc` to make them stick.

Running the GUI also needs the usual X11 and font libraries. Avalonia loads `libX11`,
`libICE`, `libSM`, `libXext`, `libXi`, `libXrandr`, `libXcursor`, `libXfixes` and `libGL`,
Skia needs `fontconfig`, `freetype`, `expat`, `libpng` and `zlib`, and GTK3 is what backs the
native file dialogs:

```bash
# Debian / Ubuntu
sudo apt install libx11-6 libice6 libsm6 libxext6 libxi6 libxrandr2 libxcursor1 libxfixes3                  libgl1 libfontconfig1 libfreetype6 libexpat1 libpng16-16 zlib1g libgtk-3-0

# Fedora
sudo dnf install libX11 libICE libSM libXext libXi libXrandr libXcursor libXfixes                  mesa-libGL fontconfig freetype expat libpng zlib gtk3

# Arch
sudo pacman -S libx11 libice libsm libxext libxi libxrandr libxcursor libxfixes                libglvnd fontconfig freetype2 expat libpng zlib gtk3
```

A desktop machine will have all of these already — the list matters for containers, CI images
and bare WSL. WSL2 with WSLg runs the app fine. You do not need a font package: the app
embeds Inter.

### Checking it works

```bash
dotnet --info                                    # should report 10.0.x
dotnet test                                      # 302 tests
dotnet run --project src/Moovie.App
```

The tests are headless and run fine over SSH or in a container. `dotnet run` opens a window,
so it needs a display. To actually match anything you will need a TMDB API key — see
[Getting started](#getting-started).

### An editor

Any of these work; none is required.

- **VS Code** with the [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)
  extension, plus [Avalonia for VS Code](https://marketplace.visualstudio.com/items?itemName=AvaloniaTeam.vscode-avalonia)
  if you want the XAML previewer.
- **Rider**, which understands Avalonia XAML out of the box.
- **Visual Studio**, a release new enough to carry the .NET 10 SDK. Windows only.

---

## Building it

With a dev environment set up as above, producing a self-contained executable is one command:

```bash
./build/publish.sh              # for the machine you are on
./build/publish.sh win-x64      # for one target
./build/publish.sh all          # every target
```

On Windows use `build/publish.ps1` instead. Output lands in `artifacts/<runtime>/`.

The version lives in one place, `<Version>` in `Directory.Build.props`. The About box reads it
back off the assembly and `publish.sh` stamps it into the macOS bundle's `Info.plist`, so
bumping it there is the whole job. Releasing is then: bump it, commit, tag that commit, and
publish a release from the tag. CI refuses to build a release whose tag disagrees with
`<Version>` — `build/check-version.sh` — because otherwise the release name and the binaries
inside it would drift apart silently.

Targets: `win-x64` `win-arm64` `osx-x64` `osx-arm64` `linux-x64` `linux-arm64`.
Builds are roughly 45–80 MB because they bundle the runtime.

Any target builds from any host — a runtime identifier only picks which runtime pack is
restored, so a Mac build works fine from Linux. Release builds come from CI, where all six
targets are produced on a single `ubuntu-latest` runner. The one thing that would need a
real Mac is codesigning, which is why the macOS builds are unsigned.

### The icon

The artwork lives at `src/Moovie.App/Assets/Icon/`, as four files built from
one master:

| File | Used by |
|---|---|
| `app.png` | the 1024 master everything else is derived from |
| `app.ico` | the Windows executable, via `<ApplicationIcon>` |
| `app.icns` | the macOS bundle, copied into `Contents/Resources` by `publish.sh` |
| `app-256.png` | the window and taskbar at runtime, on every platform |

They are committed so the build needs no image tooling, and regenerated with one command
when the master changes:

```bash
pip install Pillow
python3 build/make-icons.py path/to/new-master.png
```

The script trims the transparent border, squares the canvas and resamples the ladder — and
below 64 pixels it drops the outer glow and crops to the tile. That halo is most of the
charm at 256 and pure overhead at 16, where it spends a ring of pixels on light and leaves
the drawing too small to read; trimming it buys about a tenth more width, which is the
difference between horns and a smudge.

`build/package.sh` turns `artifacts/` into one archive per target under `dist/` — `.tar.gz`
for Linux, `.zip` elsewhere, both of which keep the executable bit that a CI artifact's own
rezipping would drop. CI runs it for every build it hands out, so a binary downloaded from a
manual run or from a release is runnable as it arrives, with no `chmod` needed and the macOS
`.app` still able to open. Release archives are attached to the release itself; a manual run
keeps its archive as a build artifact for three days.

macOS builds are assembled into a `Moovie.app` bundle. They are unsigned, so
Gatekeeper needs persuading once:

```bash
xattr -dr com.apple.quarantine "Moovie.app"
```

Linux builds still rely on the host's usual desktop libraries (fontconfig, X11 or Wayland).

The app also accepts paths on the command line, so it works as an "Open with" target:

```bash
Moovie ~/Videos/Season\ 02
```

---

## Layout

```
src/Moovie.Core/     no UI dependencies, fully unit-tested
  Parsing/     FilenameParser, JunkTokens
  Tmdb/        ITmdbService, TmdbService
  Matching/    TitleScorer, MatchResolver
  Writing/     Mp4TagWriter, Mp4TagReader, MetadataDiff, VideoResolution,
               RenameTemplate, RenameEngine
  Settings/    AppSettings, SettingsStore

src/Moovie.App/      Avalonia UI
  ViewModels/  MainWindowViewModel, PreviewPaneViewModel, FieldEditor
  Views/       MainView, PreviewPane, SettingsView, AboutView, FileBrowserView
               MainWindow, SettingsWindow, AboutWindow  (desktop frames for the views)
               IAppShell, DesktopShell, WebShell        (pickers and dialogs, per host)
  Web/         BrowserTransport, WebHost, FrameEncoder, WebClientPage.html

tests/Moovie.Core.Tests/
```

The split between a `*View` and its `*Window` is what lets `--web` reuse the interface rather than
copy it: the view is the whole of it, and the window is a frame around it. `IAppShell` is the seam
for the two things a view cannot do for itself — asking for files, and opening a dialog — which a
desktop window answers with the platform's pickers and `WebShell` answers inside the single view.

`Core` has no reference to Avalonia, and `ITmdbService` is an interface, so the parser, the
matching policy, the tag writer and the rename engine are all tested without a UI, a network
or an API key. The parser's test corpus is the place to add a filename that gets parsed wrong.

Settings live in `%APPDATA%\Moovie\settings.json` on Windows and
`~/Library/Application Support/Moovie/settings.json` on macOS.

---

## Built with Claude Code

This project was written by [Claude Code](https://claude.com/claude-code), across a series of
sessions: the filename parser, the TMDB client, the MP4 tag writer, the Avalonia UI, the tests,
the packaging scripts and the CI workflow. What to build, which trade-offs to take, and what
counted as broken came from the human side.

Every commit but the very first carries a `Co-Authored-By: Claude` trailer, so the history says
which is which.

---

This product uses the TMDB API but is not endorsed or certified by TMDB.
