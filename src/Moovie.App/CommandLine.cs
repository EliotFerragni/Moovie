namespace Moovie.App;

/// <summary>How the app was asked to run: as a window, or as a page served to a browser.</summary>
public sealed record CommandLine(bool Web, bool Help, string Host, int Port, IReadOnlyList<string> Paths)
{
    public const string Usage = """
        Moovie: fills the metadata of movie and TV files from TMDB.

          Moovie [files or folders...]
          Moovie --web [--host <address>] [--port <number>] [files or folders...]
          Moovie --help

        With --web the app runs here, with no window, and serves its interface to a browser.
        That is the mode for a machine holding the media but no screen, such as a NAS: the
        page is only a view, and every file it touches is this machine's.

          --host   address to listen on (default: every interface)
          --port   port to listen on (default: 8080)
        """;

    public static CommandLine Parse(string[] args)
    {
        var web = false;
        var help = false;
        var host = "+";
        var port = 8080;
        var paths = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--web":
                    web = true;
                    break;
                case "--help":
                case "-h":
                    help = true;
                    break;
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out port))
                        throw new ArgumentException($"'{args[i]}' is not a port number.");
                    break;
                default:
                    if (args[i].StartsWith('-'))
                        throw new ArgumentException($"Unknown option '{args[i]}'.");
                    paths.Add(args[i]);
                    break;
            }
        }

        return new CommandLine(web, help, host, port, paths);
    }
}
