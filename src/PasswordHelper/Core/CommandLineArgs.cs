using System;
using System.Collections.Generic;

namespace PasswordHelper.Core;

/// <summary>Parsed command-line options. Unknown arguments are ignored so shortcuts stay forward-compatible.</summary>
internal sealed class CommandLineArgs
{
    public string? DataDirectory { get; private set; }

    /// <summary>True when launched by the Windows "Run" entry (used to honour "start minimized").</summary>
    public bool LaunchedAtStartup { get; private set; }

    /// <summary>A command forwarded to an already-running instance, if this launch is a signalling one.</summary>
    public InstanceCommand? Command { get; private set; }

    /// <summary>DEBUG-only: seed a few demo vault entries and exit (used for development screenshots/tests).</summary>
    public bool SeedDemo { get; private set; }

    /// <summary>DEBUG-only: realize each themed control and log which one fails, then exit.</summary>
    public bool ThemeProbe { get; private set; }

    public static CommandLineArgs Parse(IReadOnlyList<string> args)
    {
        var result = new CommandLineArgs();
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--startup":
                    result.LaunchedAtStartup = true;
                    break;
                case "--data-dir":
                case "--data-directory":
                    if (i + 1 < args.Count) result.DataDirectory = args[++i];
                    break;
                case "--show":
                    result.Command = InstanceCommand.Show;
                    break;
                case "--settings":
                    result.Command = InstanceCommand.Settings;
                    break;
                case "--exit":
                case "--quit":
                    result.Command = InstanceCommand.Exit;
                    break;
                case "--seed-demo":
                    result.SeedDemo = true;
                    break;
                case "--theme-probe":
                    result.ThemeProbe = true;
                    break;
            }
        }

        return result;
    }
}
