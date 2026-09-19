using System.Text.RegularExpressions;

namespace DDT.Agent.Deployment;

// Reads the listing of "bcdedit /enum firmware". Labels are translated in a localized Windows PE, so an entry is
// recognised by its values: an identifier in braces, then the description bcdboot gives the Windows entry.
public static partial class FirmwareBootEntries
{
    public const string WindowsBootManager = "Windows Boot Manager";

    public static string? FindWindowsBootManager(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        string? identifier = null;

        foreach (string line in lines)
        {
            Match setting = Setting().Match(line);

            if (!setting.Success)
            {
                // An entry's title, its underline or a blank line starts the next entry; an indented line continues
                // a list such as displayorder.
                if (string.IsNullOrWhiteSpace(line) || !char.IsWhiteSpace(line[0]))
                {
                    identifier = null;
                }

                continue;
            }

            string value = setting.Groups["value"].Value;

            // The identifier comes first in every entry; later values in braces, such as displayorder, are not it.
            if (identifier is null && value.StartsWith('{') && value.EndsWith('}'))
            {
                identifier = value;
            }
            else if (identifier is not null && value == WindowsBootManager)
            {
                return identifier;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^\S+\s{2,}(?<value>\S.*?)\s*$")]
    private static partial Regex Setting();
}
