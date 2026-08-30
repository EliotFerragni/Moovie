using System.Runtime.InteropServices;

namespace Moovie.App;

/// <summary>
/// Gives the app somewhere to print on Windows.
///
/// The executable is built as a Windows application, which is what stops a console window
/// flashing up behind the interface when somebody double-clicks it. The cost is that it has no
/// console at all: run from a command prompt with <c>--web</c>, every line it writes — the
/// address it is serving on, or why it could not start — goes nowhere, and it looks as though
/// nothing happened. Attaching to the console that launched it puts the output back in front of
/// the person who typed the command, and makes the process a member of that console so Ctrl+C
/// reaches it.
///
/// Nothing to do anywhere else: every other platform hands a console to a process that wants one.
/// </summary>
internal static class Terminal
{
    private const int ParentProcess = -1;

    public static void Attach()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // A console of our own is the fallback for being started without one at all, so that a
        // shortcut carrying --web still has somewhere to report a failure.
        if (!AttachConsole(ParentProcess))
            AllocConsole();
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();
}
