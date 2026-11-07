// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DDT.Pxe;

// Writes the grub.cfg that loads the Linux boot environment: kernel and initrds over HTTP, then over TFTP when an open
// fails. GRUB runs the text as a script, so every value is checked here before it goes in.
public static class GrubConfig
{
    public const string DefaultKeymap = "us";

    public const string Kernel = "vmlinuz";

    private const int MaxKeymapLength = 16;
    private const int MaxHostNameLength = 253;

    // In the order the kernel unpacks them: Debian's parts, the agent, then what this server says.
    private static readonly string[] s_initrds = ["base.img", "ddt.img", BootFiles.SiteArchiveName];

    public static string Write(GrubConfigValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Server.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("GRUB is given an IPv4 address.", nameof(values));
        }

        string server = values.Server.ToString();
        string arguments = Arguments(values, server);
        StringBuilder text = new();

        text.Append("set timeout=0\n");

        if (values.HttpPort is { } port)
        {
            Load(text, string.Create(CultureInfo.InvariantCulture, $"(http,{server}:{port})/boot/{BootFiles.LinuxFolder}/"), arguments);
        }

        Load(text, $"(tftp,{server})/{BootFiles.LinuxFolder}/", arguments);

        // exit hands the machine back to its firmware, which goes on with the next boot entry.
        text.Append(CultureInfo.InvariantCulture, $"echo \"DDT: the Linux boot environment did not load from {server}.\"\n");
        text.Append("sleep 10\n");
        text.Append("exit\n");

        return text.ToString();
    }

    // GRUB only opens the initrds here, and a kernel started without them panics. So boot needs both to succeed.
    private static void Load(StringBuilder text, string from, string arguments)
    {
        text.Append(CultureInfo.InvariantCulture, $"if linux {from}{Kernel} {arguments} ; then\n");
        text.Append(CultureInfo.InvariantCulture, $"  if initrd {string.Join(' ', s_initrds.Select(name => from + name))} ; then\n");
        text.Append("    boot\n");
        text.Append("  fi\n");
        text.Append("fi\n");
    }

    // The words the start scripts read. GRUB fills in the adapter it booted from.
    private static string Arguments(GrubConfigValues values, string server)
    {
        List<string> words =
        [
            "rdinit=/init",
            "loglevel=4",
            "ddt.bootif=${net_default_mac}",
            string.Create(CultureInfo.InvariantCulture, $"ddt.time={values.Now.ToUnixTimeSeconds()}"),
            "ddt.keymap=" + (IsKeymap(values.Keymap) ? values.Keymap : DefaultKeymap),
        ];

        if (HostName(values.HostName) is { } name)
        {
            words.Add("ddt.hostname=" + name);
            words.Add("ddt.hostip=" + server);
        }

        words.AddRange(KernelArguments.Allowed(values.KernelArguments));

        return string.Join(' ', words);
    }

    // The name of a keymap file in the environment: lower case letters, digits and hyphens.
    public static bool IsKeymap(string? keymap) =>
        keymap is { Length: > 0 and <= MaxKeymapLength }
        && keymap.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');

    // In lower case, as the agent asks for it. An address needs no entry in the hosts file.
    private static string? HostName(string? name)
    {
        string? lower = name?.ToLowerInvariant();

        return lower is { Length: > 0 and <= MaxHostNameLength }
            && !IPAddress.TryParse(lower, out _)
            && lower.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '-')
                ? lower
                : null;
    }
}
