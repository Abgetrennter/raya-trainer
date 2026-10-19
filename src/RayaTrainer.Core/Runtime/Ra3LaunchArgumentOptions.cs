namespace RayaTrainer.Core.Runtime;

public sealed record Ra3LaunchArgumentOptions(
    bool UseLauncherUi,
    bool Windowed,
    bool Fullscreen,
    string ResolutionX,
    string ResolutionY,
    string WindowPositionX,
    string WindowPositionY,
    bool NoAudio,
    bool NoAudioMusic,
    string ModConfigPath,
    string ReplayGamePath,
    string ExtraArguments)
{
    public static Ra3LaunchArgumentOptions Parse(string? commandLine)
    {
        var useLauncherUi = false;
        var windowed = false;
        var fullscreen = false;
        var resolutionX = string.Empty;
        var resolutionY = string.Empty;
        var windowPositionX = string.Empty;
        var windowPositionY = string.Empty;
        var noAudio = false;
        var noAudioMusic = false;
        var modConfigPath = string.Empty;
        var replayGamePath = string.Empty;
        var extraArguments = new List<string>();
        var arguments = SplitArguments(commandLine);

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            switch (argument.ToLowerInvariant())
            {
                case "-ui":
                    useLauncherUi = true;
                    break;
                case "-win":
                    windowed = true;
                    break;
                case "-fullscreen":
                    fullscreen = true;
                    break;
                case "-xres" when i + 1 < arguments.Count:
                    resolutionX = arguments[++i];
                    break;
                case "-yres" when i + 1 < arguments.Count:
                    resolutionY = arguments[++i];
                    break;
                case "-xpos" when i + 1 < arguments.Count:
                    windowPositionX = arguments[++i];
                    break;
                case "-ypos" when i + 1 < arguments.Count:
                    windowPositionY = arguments[++i];
                    break;
                case "-noaudio":
                    noAudio = true;
                    break;
                case "-noaudiomusic":
                    noAudioMusic = true;
                    break;
                case "-modconfig" when i + 1 < arguments.Count:
                    modConfigPath = arguments[++i];
                    break;
                case "-replaygame" when i + 1 < arguments.Count:
                    replayGamePath = arguments[++i];
                    break;
                default:
                    extraArguments.Add(argument);
                    break;
            }
        }

        return new Ra3LaunchArgumentOptions(
            useLauncherUi,
            windowed,
            fullscreen,
            resolutionX,
            resolutionY,
            windowPositionX,
            windowPositionY,
            noAudio,
            noAudioMusic,
            modConfigPath,
            replayGamePath,
            RebuildArguments(extraArguments));
    }

    public string ToCommandLine() => ToCommandLine(includeLauncherUi: true, includeModConfig: true);

    public string ToDirectGameArguments() => ToCommandLine(includeLauncherUi: false, includeModConfig: false);

    private string ToCommandLine(bool includeLauncherUi, bool includeModConfig)
    {
        var arguments = new List<string>();
        if (includeLauncherUi && UseLauncherUi)
        {
            arguments.Add("-ui");
        }

        if (Windowed)
        {
            arguments.Add("-win");
        }

        if (Fullscreen)
        {
            arguments.Add("-fullscreen");
        }

        if (Windowed)
        {
            AddValueArgument(arguments, "-xres", ResolutionX);
            AddValueArgument(arguments, "-yres", ResolutionY);
            AddValueArgument(arguments, "-xpos", WindowPositionX);
            AddValueArgument(arguments, "-ypos", WindowPositionY);
        }

        if (NoAudio)
        {
            arguments.Add("-noaudio");
        }

        if (NoAudioMusic)
        {
            arguments.Add("-noAudioMusic");
        }

        if (includeModConfig)
        {
            AddValueArgument(arguments, "-modConfig", ModConfigPath, alwaysQuote: true);
        }

        AddValueArgument(arguments, "-replayGame", ReplayGamePath, alwaysQuote: true);

        var extra = (ExtraArguments ?? string.Empty).Trim();
        if (extra.Length > 0)
        {
            arguments.Add(extra);
        }

        return string.Join(" ", arguments);
    }

    private static void AddValueArgument(List<string> arguments, string name, string value, bool alwaysQuote = false)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        arguments.Add(name);
        arguments.Add(RebuildArgument(trimmed, alwaysQuote));
    }

    private static IReadOnlyList<string> SplitArguments(string? commandLine)
    {
        var text = (commandLine ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }

        var arguments = new List<string>();
        var current = new List<char>();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddCurrentArgument(arguments, current);
                continue;
            }

            current.Add(character);
        }

        AddCurrentArgument(arguments, current);
        return arguments;
    }

    private static void AddCurrentArgument(List<string> arguments, List<char> current)
    {
        if (current.Count == 0)
        {
            return;
        }

        arguments.Add(new string(current.ToArray()));
        current.Clear();
    }

    private static string RebuildArguments(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Select(argument => RebuildArgument(argument)));
    }

    private static string RebuildArgument(string argument, bool alwaysQuote = false)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        var needsQuotes = alwaysQuote || argument.Any(char.IsWhiteSpace);
        var builder = new System.Text.StringBuilder();
        var backslashes = 0;

        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            builder.Append(character);
            backslashes = 0;
        }

        if (needsQuotes)
        {
            builder.Append('\\', backslashes * 2);
            return $"\"{builder}\"";
        }

        builder.Append('\\', backslashes);
        return builder.ToString();
    }
}
