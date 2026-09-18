# Developing Moovie

Build, test and internals. For what the app does and how to use it, see the
[README](README.md).

## Setting up

The only hard requirement is the [.NET 10 SDK](https://dotnet.microsoft.com/download). There
is no `global.json`, so any 10.0.x will do.

**Windows**: `winget install Microsoft.DotNet.SDK.10`, or the installer from the download
page. Open a new terminal afterwards so `dotnet` is on `PATH`. Nothing else is needed.

**macOS**: `brew install --cask dotnet-sdk`, or the `.pkg` (Arm64 on Apple Silicon, x64 on
Intel). Xcode is not required; the project does not codesign.

**Linux**: use the distro package where it exists (`dotnet-sdk-10.0` on Debian/Ubuntu and
Fedora, `dotnet-sdk` on Arch). Distro packages lag a release or two, so elsewhere use the
official script, which installs into `~/.dotnet` and deliberately leaves `PATH` alone:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

Running the GUI on Linux also needs the usual X11 and font libraries, which any desktop
install already has. The list matters only for containers, CI images and bare WSL:

```bash
# Debian / Ubuntu
sudo apt install libx11-6 libice6 libsm6 libxext6 libxi6 libxrandr2 libxcursor1 \
                 libxfixes3 libgl1 libfontconfig1 libfreetype6 libexpat1 \
                 libpng16-16 zlib1g libgtk-3-0
```

Avalonia needs the X11 set, Skia needs fontconfig and friends, and GTK3 backs the native file
dialogs. Fedora and Arch want the same libraries under their own names. No font package is
needed: the app embeds Inter. WSL2 with WSLg runs the app fine.

### Checking it works

```bash
dotnet --info                          # should report 10.0.x
dotnet test                            # 365 tests
dotnet run --project src/Moovie.App
```

The tests are headless and run fine over SSH or in a container. `dotnet run` opens a window, so
it needs a display, and needs a TMDB API key to match anything.

### An editor

Any of these work, none is required: **VS Code** with the
[C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) and
[Avalonia for VS Code](https://marketplace.visualstudio.com/items?itemName=AvaloniaTeam.vscode-avalonia)
for the XAML previewer, **Rider**, or **Visual Studio** new enough to carry the .NET 10 SDK.

## Building

```bash
./build/publish.sh              # for the machine you are on
./build/publish.sh win-x64      # for one target
./build/publish.sh all          # every target
```

On Windows use `build/publish.ps1`. Output lands in `artifacts/<runtime>/`. Targets are
`win-x64` `win-arm64` `osx-x64` `osx-arm64` `linux-x64` `linux-arm64`, and any of them builds
from any host: a runtime identifier only picks which runtime pack is restored. Builds are
roughly 45-80 MB because they bundle the runtime. Release builds come from CI, all six on one
`ubuntu-latest` runner; the only thing needing a real Mac is codesigning, which is why the
macOS builds are unsigned.

`build/package.sh` turns `artifacts/` into one archive per target under `dist/`: `.tar.gz` for
Linux, `.zip` elsewhere. Both preserve the executable bit that a CI artifact's own rezipping
would drop, so a downloaded binary runs as it arrives and the macOS `.app` still opens. CI runs
it for every build; release archives are attached to the release, manual runs keep theirs for
three days.

### Versioning and releases

The version lives in one place: `<Version>` in `Directory.Build.props`. The About box reads it
back off the assembly and `publish.sh` stamps it into the macOS bundle's `Info.plist`.
Releasing is: bump it, commit, tag that commit, publish a release from the tag. CI
(`build/check-version.sh`) refuses to build a release whose tag disagrees with `<Version>`,
since otherwise the release name and the binaries inside it would drift apart silently.

### The Docker image

`build/package-docker.sh [arch]` publishes the binary, builds an image around it and writes
`dist/moovie-<version>-<arch>.docker.tar.gz` (about 90 MB). No registry is involved. See
[Running it on a server](README.md#running-it-on-a-server) for the deploy side.

A release ships both architectures, each built on a runner of its own kind. The binaries
cross-compile but the image cannot: the Dockerfile runs `apt-get`, so an arm64 image on an x64
runner would go through QEMU. Arm runners are free here because the repository is public.

The image carries no X11 and no desktop, because `--web` draws into a frame buffer and needs
neither. On top of what .NET itself requires it needs only fontconfig and one font, without
which Skia will not load and Avalonia has no default font family to fall back on.

### The icon

`src/Moovie.App/Assets/Icon/` holds four files built from one master:

| File | Used by |
|---|---|
| `app.png` | the 1024 master everything else is derived from |
| `app.ico` | the Windows executable, via `<ApplicationIcon>` |
| `app.icns` | the macOS bundle, copied into `Contents/Resources` by `publish.sh` |
| `app-256.png` | the window and taskbar at runtime, on every platform |

They are committed so the build needs no image tooling, and regenerated when the master changes:

```bash
pip install Pillow
python3 build/make-icons.py path/to/new-master.png
```

The script trims the transparent border, squares the canvas and resamples the ladder. Below 64
pixels it drops the outer glow and crops to the tile: that halo is most of the charm at 256 and
pure overhead at 16, where it spends a ring of pixels on light and leaves the drawing too small
to read. Trimming it buys about a tenth more width, which is the difference between horns and a
smudge.

## Layout

```
src/Moovie.Core/     no UI dependencies, fully unit-tested
  Files/       MediaFiles
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
               ClipboardBridge                            (copy and paste across the wire)
               DrawingClock, QuietDispatcher              (what the app draws and runs by)

tests/Moovie.Core.Tests/
```

`Core` has no reference to Avalonia and `ITmdbService` is an interface, so the parser, the
matching policy, the tag writer and the rename engine are all tested without a UI, a network or
an API key. The parser's test corpus is the place to add a filename that gets parsed wrong.

Settings live in `%APPDATA%\Moovie\settings.json` on Windows and
`~/Library/Application Support/Moovie/settings.json` on macOS.

## How `--web` reuses the interface

Nothing is duplicated. The interface is one set of views: a window hosts them on the desktop,
and `--web` hands the very same tree to Avalonia's `RemoteServer`, which renders it into a
frame buffer. The browser receives PNG frames and sends pointer and keyboard events back.
Everything else (view models, parser, diff, writer) has no idea which is running.

A pointer message carries which buttons are held as well as where the cursor is: the browser's
own `buttons` bitmask, translated into the modifiers the remote protocol carries them in. That is
not a detail. A text box asks a move whether the left button is down before it will extend its
selection, so while the page sent only the position, dragging across text in a field selected
nothing, and copy and cut, which take the selection, had nothing to take. It looked like a broken
clipboard and was a broken selection.

That is what the `*View` / `*Window` split is for: the view is the whole of the interface and
the window is a frame around it. `IAppShell` is the seam for the two things a view cannot do
for itself, asking for files and opening a dialog, which a desktop window answers with the
platform's pickers and `WebShell` answers inside the single view. A browser's own file dialog
would have been wrong twice over: it offers the wrong machine's files, and it hands back a
stream where the tag writer needs a path.

### The clipboard is the browser's, and is met halfway

Avalonia's text boxes copy and paste through `TopLevel.Clipboard`, and the remote top level has
none: the platform behind `RemoteServer` offers no clipboard feature, so that property is null and
every copy, cut and paste in the app silently does nothing — from the keyboard and from the box's
own context menu alike. `ClipboardBridge` answers the three cancellable events Avalonia raises
before it would touch the clipboard (`CopyingToClipboard`, `CuttingToClipboard`,
`PastingFromClipboard`), which is the one hook that covers both, since the context menu's items
call the very methods that raise them. A paste goes in as a text input event rather than through
the text property, so it replaces the selection and the box's own read-only and length limits
still apply.

That fixes the app's half. The other half is that the clipboard worth having is the *browser's*,
since that is the one the rest of the user's machine shares, and a page may only put text on it
from inside a gesture the browser has just handled or over a secure connection — and a NAS on a
LAN is not a secure connection, so `navigator.clipboard` does not exist there at all. A message
arriving from a server is not a gesture either, so the app cannot simply ask.

So the page does not ask. The app's selected text is pushed to it as it changes — and again
whenever a tab arrives, which knows nothing it has not been told since — and kept selected in a
hidden field, which also holds the keyboard's focus; the user's own Ctrl+C and Ctrl+X are
then carried out by the browser itself, on exactly the right text, with no permission asked and
nothing to fail. The page keeps the three shortcuts to itself — not preventing their default is
the whole trick — and tells the app what happened: a cut so it can delete the selection, a copy
so it remembers what to paste back, a paste with the text the browser handed over.

Two traps in that, both paid for. The default action of a `mousedown` is to focus whatever is
under the cursor, and the canvas is focusable so that a touch device has somewhere to put the
focus: left alone, every click took the focus off the hidden field and the browser then had
nothing to copy from, which looked exactly like copy being broken while paste worked. The canvas
cancels `mousedown` for that reason. And the focus is put back on the field, with the selection in
it, on the keypress itself, since a key event is proof of a keyboard whatever the last pointer was.

What is left best-effort is a copy the app starts itself, from its own context menu: the page is
told and tries `document.execCommand('copy')`, which a browser allows only while it still counts a
gesture as recent. Firefox 136 allows it a moment after the click that asked for it, tested, and
Chrome's window for this is five seconds. Where a browser refuses, the text is still in the field,
so the user's Ctrl+C takes it. The app's *Paste* item can only paste what the page has told it
about, since nothing may read the clipboard unasked.

A masked box is never read: `SelectedText` hands back the cleartext of a `PasswordChar` box, and
the app's one masked box holds the user's TMDB key, so `Copyable` refuses it — both for the
selection pushed to the page and for a copy. That matches what Avalonia does on the desktop,
where `CanCopy` is false for such a box.

Costs, measured: typing a character sends under 2 KB and lands in around 20 ms on a LAN, since
only the changed part of a frame goes out; a still window with a blinking caret costs a few
hundred bytes a second; scrolling a long list redraws most of the window and is genuinely
expensive.

Idle CPU with nobody connected is **0.2 to 0.5 ms a second, waking about once every ten
seconds**. Getting there took two takeovers, because two separate things were keeping the
machine awake for an app nobody had open.

`DrawingClock` was the first and much the larger: the headless platform draws sixty times a
second forever, and that alone was 9% of a core. `QuietDispatcher` is the second. What it fixes
is not the loop waiting, which costs nothing, but a busy-wait the loop pays per timer:
`ManagedDispatcherImpl.RunLoop` declines to wait out the last millisecond before any timer
(`if (!(timeout.TotalMilliseconds < 1.0))`) and spins it instead, and Avalonia's compositor keeps
a one-second `DispatcherTimer` per `BatchStreamPool` to trim the pool, so the spin was paid a few
times a second for ever.

Measured, idle, nobody connected:

| | cpu | wakes/s | cpu per wake |
|---|---|---|---|
| platform clock and loop | 93 ms/s | | |
| `DrawingClock` only | 4.75 ms/s | 2.0 | 2300 us |
| and `QuietDispatcher` | 2.25 ms/s | 4.8 | 460 us |
| and coalescing while idle | 0.2-0.5 ms/s | 0.1 | |

The middle row is the important one to read properly. Its cpu is only halved, but the loop went
from *rarely sleeping and burning a 2.3 ms busy-loop between sleeps* to *sleeping for every timer
and waking briefly*. Burst length is what costs power rather than cpu time: a couple of
milliseconds at full tilt is enough to ramp a core's clock and hold a machine out of its deep
idle states, and the ramp-down hysteresis outlasts the burst by far. This was worth doing because
it was 5 W measured at the wall on a NAS, against about 45 W at rest, for an app with nobody
connected.

The last row coalesces timers while nobody is connected: `QuietDispatcher.CoalesceTimersWhile`
puts a floor under how long a timer may be waited for, and `WebHost` sets that to 30 seconds
while `HasViewers` is false. **Only timers go slow, never work.** A signal wakes the loop at once
whatever it was waiting for, and everything arriving from another thread arrives as a signal, so
an apply started before the last tab closed keeps writing at full speed: measured, five files
taking 500 ms of work finish in 510 ms with a 30-second floor in force. A browser connecting is a
signal too, so a page opened against a fully idle server draws its first frame in 137 ms.

That is also the reason this is not simply *stopping* the loop while nobody is connected, which
would be the obvious thing to try and is a trap. `ApplyAsync` awaits one file at a time, so the
progress of a batch is a dispatcher continuation like any other; stop the loop and a hundred-file
apply freezes on the file it is on the moment the last tab closes, which is an ordinary thing to
do on a NAS. Coalescing gets the same idle machine without giving that up.

What coalescing does cost is that any `DispatcherTimer` is up to 30 seconds late while nobody is
connected. That is safe today because the app owns exactly one, the heartbeat in `WebHost`, which
stops itself when the last viewer leaves, and because `Task.Delay` resumes through a signal
rather than a dispatcher timer. **A timer added later that has to tick while nobody is watching
would be delayed**, and belongs outside the dispatcher.

Both takeovers are optional and verified before they commit: neither is attempted unless the
interface is exactly the shape it knows, nothing Avalonia can observe is changed until every
check has passed, and a refusal prints one line and leaves the app running the way it did
before. `IDispatcherImpl` is blocked from outside implementation by a synthetic member in
Avalonia's *reference* assembly only, so a `DispatchProxy` built at runtime satisfies it
honestly. Getting it installed needs one more trick than the clock did: the platform binds its
own loop to a constant while setting itself up, so there is no slot to leave open, but a
`Dispatcher` resolves its loop once on first use and keeps it, so `TakeOver` binds and then
immediately touches `Dispatcher.UIThread`, which makes the platform's later overwrite land on
something nothing reads again.

Interactive cost is unchanged by all of it: hover to frame is 4 to 21 ms, median 6, and a page
open with nothing focused costs 5.6 ms/s.

## Design notes

Rules that are not obvious from the code, and the reasoning behind them.

**The diff compares only what gets written.** A TMDB id has no MP4 atom, so listing it would
promise a change that never happens. Every column is in its written form, too: one `©day` atom
holds either a full date or a bare year, so the comparison is over the string that reaches the
container rather than over the two fields behind it, and the TV fields follow the type currently
set. Cover art is never reported as cleared, because applying with no new image deliberately
leaves the embedded one alone.

**Taking the file's own value counts as a hand edit**, so a refetch keeps it and *Discard edits*
puts it back; taking TMDB's value clears that mark. A field pointed at the file's own value
stays listed even though it is no longer a change, or there would be no way back.

**The file is read once per selection.** Editing a field re-compares against that snapshot, so
the diff answers for what is on screen rather than for what TMDB last returned.

**The HD flag can only come from TMDB's side of the diff.** 1080i, 1080p and 1440p all store a
2, so the file has no resolution to hand back.

**Resolution is read from the video track, not the name.** A name only *claims* a resolution,
and stops claiming it the moment a template without `{resolution}` renames the file, after
which the next pass over the library would write an SD HD flag over a 4K film. The name is the
fallback for a file whose video track cannot be read.

Height and width are read as two separate opinions and the larger wins, because either alone is
wrong for common material. Height under-reports widescreen film, which is cropped rather than
letterboxed: a 2.39:1 transfer is 1920×800, and 800 is not 720p. Width under-reports 4:3 and
anamorphic material, where the height is the honest number: 720×576 is `576p`, not `480p`.
Taking the larger gets both right, and a DVD-class frame stays SD however far it is cropped:
720×406 is `480p`, not the `360p` that scaling 720 by 9/16 would suggest.

`1440p` is deliberately not shortened by `{resolution:short}`: "2K" properly means a 1080p-class
frame, so borrowing it would name the file wrongly.

**A colon is not governed by the unsupported-character setting.** Every other character Windows
forbids is written as whatever the setting says, but a colon nearly always separates a title
from its subtitle, and `Mission_ Impossible` would be the odd one out however the rest are
handled. It becomes a dash, and the space after it is absorbed rather than doubled.

**A name typed into the file list belongs to the file, not to the template.** It wins over what
the template renders, survives a refetch, and is dropped only when the user reverts it or the
file is applied and carrying it. It goes through the same character rules a rendered name does,
which is why the view commits it through the view model rather than storing the raw text: only
the view model knows which characters the settings ask for. The separator is the one rule it
escapes, since spaces typed on purpose are not the template's business.

**Angle brackets delimit optional template sections** because no filesystem allows them in a
name, which leaves square brackets free for the common `[{resolution}]` style.

**Artwork is not a translation.** A kind missing in your language is taken from the original
language rather than given up on, so a season with a French entry but no French poster still
gets the English poster instead of quietly turning into a show poster. Artwork that did come
back in your language always wins, and unlike untranslated text this is not flagged in the
pane: a poster with no words on it is not a translation gap.

**Only the artwork picker costs extra TMDB requests**, and only when opened. Every kind the
settings can choose arrives with the metadata lookup itself.

**Theme changes apply on save, unlike the interface language**, because colours are resolved
continuously whereas the language is read once as a window loads.

**Only `.mp4` and `.m4v` are handled**, so that MP4 tags can be written in pure managed code and
the app ships as one file with no helper binaries alongside it.

## Adding a language

Drop a `<tag>.json` next to `src/Moovie.Core/Localization/en.json`, translate the values, and
list the tag in `Strings.Available`. A missing phrase falls back to English rather than leaving
a hole. The TMDB language names in the metadata dropdown stay in English.

Three tests guard translations: one fails if a translation drifts from the English key set or
loses a `{0}` placeholder, and one fails if a message the app can actually show comes back
identical in English and in translation, which is how a sentence hardcoded in the code rather
than looked up gets caught.

## Built with Claude Code

This project was written by [Claude Code](https://claude.com/claude-code) across a series of
sessions: the filename parser, the TMDB client, the MP4 tag writer, the Avalonia UI, the tests,
the packaging scripts and the CI workflow. What to build, which trade-offs to take and what
counted as broken came from the human side. Every commit written by Claude carries a
`Co-Authored-By: Claude` trailer, so the history says which is which.
