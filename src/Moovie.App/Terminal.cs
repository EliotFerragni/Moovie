using System.Runtime.InteropServices;

namespace Moovie.App;

/// <summary>
/// Gives the app somewhere to print on Windows.
///
/// Building the executable as a Windows application is what stops a console window flashing up
/// when somebody double-clicks it, at the cost of having no console at all: run with <c>--web</c>
/// from a command prompt, every line it writes goes nowhere. Attaching to the console that
/// launched it puts the output back in front of whoever typed the command, and makes the process
/// a member of that console so Ctrl+C reaches it.
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
