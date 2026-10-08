// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using Xunit;

namespace DDT.Pxe.Tests;

// GRUB runs the text as a script on every machine that netboots Linux, so the whole text is pinned.
public sealed class GrubConfigTests
{
    private const string Words =
        "rdinit=/init loglevel=4 ddt.bootif=${net_default_mac} ddt.time=1791460800 ddt.keymap=de "
        + "ddt.hostname=ddt-server.example.test ddt.hostip=10.77.0.1 console=ttyS0,115200";

    private static readonly IPAddress s_server = IPAddress.Parse("10.77.0.1");
    private static readonly DateTimeOffset s_now = DateTimeOffset.FromUnixTimeSeconds(1_791_460_800);

    private static GrubConfigValues Values(int? httpPort = 8080) => new(s_server, httpPort, s_now)
    {
        Keymap = "de",
        HostName = "DDT-Server.Example.Test",
        KernelArguments = "console=ttyS0,115200",
    };

    [Fact]
    public void LoadsOverHttpFirstAndOverTftpWhenAnOpenFails()
    {
        string expected =
            "set timeout=0\n"
            + $"if linux (http,10.77.0.1:8080)/boot/linux/vmlinuz {Words} ; then\n"
            + "  if initrd (http,10.77.0.1:8080)/boot/linux/base.img (http,10.77.0.1:8080)/boot/linux/ddt.img (http,10.77.0.1:8080)/boot/linux/site.cpio ; then\n"
            + "    boot\n"
            + "  fi\n"
            + "fi\n"
            + $"if linux (tftp,10.77.0.1)/linux/vmlinuz {Words} ; then\n"
            + "  if initrd (tftp,10.77.0.1)/linux/base.img (tftp,10.77.0.1)/linux/ddt.img (tftp,10.77.0.1)/linux/site.cpio ; then\n"
            + "    boot\n"
            + "  fi\n"
            + "fi\n"
            + "echo \"DDT: the Linux boot environment did not load from 10.77.0.1.\"\n"
            + "sleep 10\n"
            + "exit\n";

        Assert.Equal(expected, GrubConfig.Write(Values()));
    }

    [Fact]
    public void LoadsOverTftpAloneWhenHttpIsLeftOut()
    {
        string expected =
            "set timeout=0\n"
            + $"if linux (tftp,10.77.0.1)/linux/vmlinuz {Words} ; then\n"
            + "  if initrd (tftp,10.77.0.1)/linux/base.img (tftp,10.77.0.1)/linux/ddt.img (tftp,10.77.0.1)/linux/site.cpio ; then\n"
            + "    boot\n"
            + "  fi\n"
            + "fi\n"
            + "echo \"DDT: the Linux boot environment did not load from 10.77.0.1.\"\n"
            + "sleep 10\n"
            + "exit\n";

        Assert.Equal(expected, GrubConfig.Write(Values(httpPort: null)));
    }

    // boot is reached only inside both tests, and the script ends by handing the machine back to its firmware.
    [Fact]
    public void BootsOnlyAfterKernelAndInitrdsBothLoadedAndEndsWithExit()
    {
        string[] lines = GrubConfig.Write(Values()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].Trim() == "boot")
            {
                Assert.StartsWith("  if initrd ", lines[index - 1], StringComparison.Ordinal);
                Assert.StartsWith("if linux ", lines[index - 2], StringComparison.Ordinal);
            }
        }

        Assert.Equal(2, lines.Count(line => line.Trim() == "boot"));
        Assert.Equal("exit", lines[^1]);
    }

    [Theory]
    [InlineData("quiet; reboot")]
    [InlineData("a=$(halt)")]
    [InlineData("a=${prefix}")]
    [InlineData("a=`halt`")]
    [InlineData("a=\"b\"")]
    [InlineData("a='b'")]
    [InlineData("a=b\\")]
    [InlineData("a={b}")]
    [InlineData("a=b|c")]
    [InlineData("a=b&c")]
    [InlineData("a=b#c")]
    [InlineData("a=<b>")]
    [InlineData("a=(tftp,10.0.0.9)/x")]
    [InlineData("a=b*")]
    [InlineData("a=b?")]
    [InlineData("a=[b]")]
    [InlineData("a=~b")]
    [InlineData("a=b!c")]
    [InlineData("a=b%c")]
    [InlineData("quiet -- extra")]
    [InlineData("quiet rdinit=/bin/sh")]
    [InlineData("quiet init=/bin/sh")]
    [InlineData("quiet ddt.hostip=10.0.0.9")]
    [InlineData("ddt.time=1")]
    [InlineData("quiet ümlaut=1")]
    public void ArgumentsThatAreNotAllowedNeverReachTheText(string arguments)
    {
        string text = GrubConfig.Write(Values() with { KernelArguments = arguments });

        Assert.Equal(GrubConfig.Write(Values() with { KernelArguments = null }), text);
        Assert.DoesNotContain("quiet", text, StringComparison.Ordinal);
    }

    // A line break only separates two words, so nothing after it becomes a line of the script.
    [Fact]
    public void ALineBreakInTheArgumentsIsASpaceInTheText()
    {
        string text = GrubConfig.Write(Values() with { KernelArguments = "quiet\r\nreboot" });

        Assert.Equal(GrubConfig.Write(Values() with { KernelArguments = "quiet reboot" }), text);
        Assert.DoesNotContain("\nreboot", text, StringComparison.Ordinal);
    }

    // GRUB's own words mean something only where a command begins. After the kernel's name each is one more argument
    // of the linux command, so each must reach the file as it stands: not quoted, not dropped, and on no line of its own.
    [Fact]
    public void GrubsOwnWordsAmongTheArgumentsReachTheFileAsPlainWordsOfTheKernelsLine()
    {
        const string Keywords =
            "if then else elif fi for in do done while until case esac select function time menuentry submenu boot exit linux set echo sleep true false";
        string[] plain = GrubConfig.Write(Values() with { KernelArguments = null }).Split('\n');

        string[] lines = GrubConfig.Write(Values() with { KernelArguments = Keywords }).Split('\n');

        Assert.All(KernelArguments.Split(Keywords), word => Assert.True(KernelArguments.IsWord(word) && !KernelArguments.IsReserved(word), word));
        Assert.Equal(plain.Length, lines.Length);

        for (int index = 0; index < lines.Length; index++)
        {
            bool kernel = plain[index].StartsWith("if linux ", StringComparison.Ordinal);

            // Every other line is the same script as without them, so none of the words began a command.
            Assert.Equal(kernel ? plain[index].Replace(" ; then", $" {Keywords} ; then", StringComparison.Ordinal) : plain[index], lines[index]);
        }

        Assert.Equal(2, lines.Count(line => line.EndsWith($" ddt.hostip=10.77.0.1 {Keywords} ; then", StringComparison.Ordinal)));
    }

    [Fact]
    public void WritesTheAdministratorsWordsAfterItsOwn()
    {
        string text = GrubConfig.Write(Values() with { KernelArguments = "  console=ttyS0,115200   ddt.shell\tmodule_blacklist=nouveau " });

        Assert.Contains("ddt.hostip=10.77.0.1 console=ttyS0,115200 ddt.shell module_blacklist=nouveau ; then\n", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de\nreboot")]
    [InlineData("de; reboot")]
    [InlineData("DE")]
    [InlineData("../de")]
    [InlineData("")]
    [InlineData("a-keymap-with-a-name-far-too-long")]
    public void AKeymapThatIsNotANameBecomesTheDefault(string keymap)
    {
        string text = GrubConfig.Write(Values() with { Keymap = keymap });

        Assert.Contains(" ddt.keymap=us ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("reboot", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10.77.0.1")]
    [InlineData("ddt server")]
    [InlineData("ddt;reboot")]
    [InlineData("ddt$name")]
    [InlineData("")]
    [InlineData(null)]
    public void AHostThatIsNoNameGetsNoHostsEntry(string? host)
    {
        string text = GrubConfig.Write(Values() with { HostName = host });

        Assert.DoesNotContain("ddt.hostname", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ddt.hostip", text, StringComparison.Ordinal);
        Assert.DoesNotContain("reboot", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheServerIsAnIPv4AddressBecauseGrubsDevicesTakeNoOther()
    {
        Assert.Throws<ArgumentException>(() => GrubConfig.Write(Values() with { Server = IPAddress.IPv6Loopback }));
    }

    // Everything outside the words is fixed text: digits, dots and the port are all a request can add to it.
    [Fact]
    public void HoldsOnlyAsciiAndNoCarriageReturn()
    {
        string text = GrubConfig.Write(Values());

        Assert.True(System.Text.Ascii.IsValid(text));
        Assert.DoesNotContain('\r', text);
    }
}
