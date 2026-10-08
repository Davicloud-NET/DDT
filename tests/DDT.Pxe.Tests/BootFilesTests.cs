// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DDT.Pxe.Tests;

// TFTP and the HTTP boot port are open to everyone on the network, so what a name can reach is pinned here.
public sealed class BootFilesTests : IDisposable
{
    private const string Root = "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n";

    private static readonly string[] s_listed = ["shimx64.efi", "grubx64.efi", "vmlinuz", "base.img", "ddt.img"];
    private static readonly BootRequest s_request = new(IPAddress.Parse("10.77.0.1"), IPAddress.Parse("10.77.0.128"));

    private readonly string _root = Directory.CreateTempSubdirectory("ddt-boot-files-").FullName;
    private readonly string _boot;
    private readonly string _bundle;
    private readonly FakeLinuxBootSite _site = new();
    private readonly BootFiles _files;

    public BootFilesTests()
    {
        _boot = Directory.CreateDirectory(Path.Combine(_root, "boot")).FullName;
        _bundle = Directory.CreateDirectory(Path.Combine(_root, "linux-boot")).FullName;

        Directory.CreateDirectory(Path.Combine(_boot, "x64"));
        File.WriteAllText(Path.Combine(_boot, "x64", "bootmgfw.efi"), "windows boot manager");

        foreach (string name in s_listed)
        {
            File.WriteAllText(Path.Combine(_bundle, name), "bundle " + name);
        }

        // Beside the listed files, as a release leaves them, and never to be served.
        File.WriteAllText(Path.Combine(_bundle, "ddt-linux-boot.json"), "{}");
        File.WriteAllText(Path.Combine(_bundle, "notes.txt"), "not listed");
        File.WriteAllText(Path.Combine(_root, "secret.txt"), "outside");

        _site.Current = new LinuxBootBundle(_bundle, s_listed.ToFrozenSet(StringComparer.Ordinal), new Uri("https://ddt-server.example.test:8443"), Root)
        {
            Keymap = "de",
            KeyboardLayout = "German (de)",
            KernelArguments = "console=ttyS0,115200",
        };
        _files = new BootFiles(new BootFileResolver(_boot), _site, 8080, new TestClock(DateTimeOffset.FromUnixTimeSeconds(1_791_460_800)));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private BootContent? Find(string requested, BootRequest? request = null) =>
        _files.TryFind(requested, request ?? s_request, out BootContent? content) ? content : null;

    private static string TextOf(BootContent? content) => Encoding.UTF8.GetString(Assert.IsType<MemoryBootContent>(content).Bytes.Span);

    private string PathOf(BootContent? content) => Path.GetRelativePath(_root, Assert.IsType<FileBootContent>(content).File.FullName).Replace('\\', '/');

    [Theory]
    [InlineData("grub/grub.cfg")]
    [InlineData("/grub/grub.cfg")]
    [InlineData(@"\grub\grub.cfg")]
    [InlineData("//grub/grub.cfg")]
    public void GeneratesGrubsConfigurationForItsExactName(string requested)
    {
        string text = TextOf(Find(requested));

        Assert.StartsWith("set timeout=0\nif linux (http,10.77.0.1:8080)/boot/linux/vmlinuz rdinit=/init ", text, StringComparison.Ordinal);
        Assert.Contains(" ddt.time=1791460800 ddt.keymap=de ddt.hostname=ddt-server.example.test ddt.hostip=10.77.0.1 console=ttyS0,115200 ; then\n", text, StringComparison.Ordinal);
        Assert.Contains("\nif linux (tftp,10.77.0.1)/linux/vmlinuz ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesTheAddressTheRequestArrivedOnAsTheServer()
    {
        string text = TextOf(Find("grub/grub.cfg", new BootRequest(IPAddress.Parse("192.168.50.7"), IPAddress.Parse("192.168.50.99"))));

        Assert.Contains("(http,192.168.50.7:8080)/boot/linux/base.img", text, StringComparison.Ordinal);
        Assert.Contains("(tftp,192.168.50.7)/linux/site.cpio", text, StringComparison.Ordinal);
        Assert.Contains(" ddt.hostip=192.168.50.7 ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("10.77.0.1", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("grub/GRUB.CFG")]
    [InlineData("GRUB/grub.cfg")]
    [InlineData("grub/x86_64-efi/grub.cfg")]
    [InlineData("grub/grub.cfg/")]
    [InlineData("grub//grub.cfg")]
    [InlineData("./grub/grub.cfg")]
    [InlineData("grub/./grub.cfg")]
    [InlineData("grub/../grub/grub.cfg")]
    [InlineData("grub/grub.cfg ")]
    [InlineData("grub/grub.cfg\0")]
    [InlineData("grub")]
    [InlineData("grub/")]
    public void AnythingButTheExactNameIsNotGenerated(string requested)
    {
        Assert.Null(Find(requested));
    }

    [Fact]
    public void GrubsConfigurationIsNotGeneratedForAnAddressGrubCannotName()
    {
        Assert.Null(Find("grub/grub.cfg", new BootRequest(IPAddress.IPv6Loopback, IPAddress.IPv6Loopback)));
    }

    [Theory]
    [InlineData("linux/vmlinuz", "linux-boot/vmlinuz")]
    [InlineData("/linux/vmlinuz", "linux-boot/vmlinuz")]
    [InlineData(@"\linux\base.img", "linux-boot/base.img")]
    [InlineData("linux/shimx64.efi", "linux-boot/shimx64.efi")]
    [InlineData("linux/grubx64.efi", "linux-boot/grubx64.efi")]
    [InlineData("linux/ddt.img", "linux-boot/ddt.img")]
    public void ServesAListedFileOfTheBundle(string requested, string expected)
    {
        Assert.Equal(expected, PathOf(Find(requested)));
    }

    [Theory]
    [InlineData("linux/ddt-linux-boot.json")]
    [InlineData("linux/notes.txt")]
    [InlineData("linux/VMLINUZ")]
    [InlineData("LINUX/vmlinuz")]
    [InlineData("linux/vmlinuz/")]
    [InlineData("linux//vmlinuz")]
    [InlineData("linux/./vmlinuz")]
    [InlineData("linux/../linux/vmlinuz")]
    [InlineData("linux/../linux-boot/vmlinuz")]
    [InlineData("linux/../secret.txt")]
    [InlineData("linux/../../secret.txt")]
    [InlineData(@"linux\..\..\secret.txt")]
    [InlineData("../secret.txt")]
    [InlineData(@"..\secret.txt")]
    [InlineData("linux/..")]
    [InlineData("linux/")]
    [InlineData("linux")]
    [InlineData("linux/a/b")]
    [InlineData("linux/vmlinuz:stream")]
    [InlineData("linux-boot/vmlinuz")]
    [InlineData("")]
    public void ServesNothingElseOfTheBundleAndNothingOutsideIt(string requested)
    {
        Assert.Null(Find(requested));
    }

    [Fact]
    public void RefusesAnAbsolutePathToAFileThatExists()
    {
        string secret = Path.Combine(_root, "secret.txt");

        Assert.True(File.Exists(secret));
        Assert.Null(Find(secret));
        Assert.Null(Find("linux/" + secret));
        Assert.Null(Find(Path.Combine(_bundle, "vmlinuz")));
    }

    [Fact]
    public void RefusesANameLongerThanAnyBootFileHas()
    {
        Assert.Null(Find("linux/" + new string('a', 600)));
        Assert.Null(BootFiles.Normalized(new string('/', 513)));
    }

    // A listed name that is a link could point anywhere on the volume, so it is not followed.
    [Fact]
    public void RefusesAListedNameThatIsALink()
    {
        string link = Path.Combine(_bundle, "vmlinuz");
        File.Delete(link);

        try
        {
            File.CreateSymbolicLink(link, Path.Combine(_root, "secret.txt"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("This account may not create a symbolic link: " + exception.Message);
        }

        Assert.Equal("outside", File.ReadAllText(link));
        Assert.Null(Find("linux/vmlinuz"));
        Assert.Equal("linux-boot/base.img", PathOf(Find("linux/base.img")));
    }

    [Fact]
    public void GeneratesTheSiteArchiveWithTheAgentsConfiguration()
    {
        byte[] archive = Assert.IsType<MemoryBootContent>(Find("linux/site.cpio")).Bytes.ToArray();
        List<(string Path, uint Mode, byte[] Content)> entries = CpioArchive.Entries(archive);

        Assert.Equal(["ddt", "ddt/agent.json"], entries.Select(entry => entry.Path));

        using JsonDocument agent = JsonDocument.Parse(entries[1].Content);
        Assert.Equal("https://ddt-server.example.test:8443", agent.RootElement.GetProperty("serverUrl").GetString());
        Assert.Equal(Root, agent.RootElement.GetProperty("rootCertificate").GetString());
        Assert.Equal("German (de)", agent.RootElement.GetProperty("keyboardLayout").GetString());
    }

    // The generated names win over a file left in the boot directory, which could be older than the server's values.
    [Fact]
    public void AFileOfTheSameNameInTheBootDirectoryDoesNotReplaceWhatIsGenerated()
    {
        Directory.CreateDirectory(Path.Combine(_boot, "grub"));
        Directory.CreateDirectory(Path.Combine(_boot, "linux"));
        File.WriteAllText(Path.Combine(_boot, "grub", "grub.cfg"), "made by hand");
        File.WriteAllText(Path.Combine(_boot, "linux", "site.cpio"), "made by hand");
        File.WriteAllText(Path.Combine(_boot, "linux", "vmlinuz"), "made by hand");
        File.WriteAllText(Path.Combine(_boot, "linux", "other.img"), "made by hand");

        Assert.StartsWith("set timeout=0\n", TextOf(Find("grub/grub.cfg")), StringComparison.Ordinal);
        Assert.IsType<MemoryBootContent>(Find("linux/site.cpio"));
        Assert.Equal("linux-boot/vmlinuz", PathOf(Find("linux/vmlinuz")));

        // What the bundle does not have still comes from the boot directory.
        Assert.Equal("boot/linux/other.img", PathOf(Find("linux/other.img")));
    }

    [Fact]
    public void WithoutABundleEveryNameFallsThroughToTheBootDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_boot, "grub"));
        Directory.CreateDirectory(Path.Combine(_boot, "linux"));
        File.WriteAllText(Path.Combine(_boot, "grub", "grub.cfg"), "made by hand");
        File.WriteAllText(Path.Combine(_boot, "linux", "shimx64.efi"), "made by hand");
        _site.Current = null;

        Assert.False(_files.LinuxAvailable);
        Assert.Equal("boot/grub/grub.cfg", PathOf(Find("/grub/grub.cfg")));
        Assert.Equal("boot/linux/shimx64.efi", PathOf(Find("linux/shimx64.efi")));
        Assert.Null(Find("linux/vmlinuz"));
        Assert.Null(Find("linux/site.cpio"));
        Assert.Null(Find("grubx64.efi"));
    }

    [Fact]
    public void AHostWithoutALinuxSiteServesTheBootDirectoryAlone()
    {
        BootFiles files = new(new BootFileResolver(_boot), null, 8080, TimeProvider.System);

        Assert.False(files.LinuxAvailable);
        Assert.True(files.TryFind("x64/bootmgfw.efi", s_request, out _));
        Assert.False(files.TryFind("grub/grub.cfg", s_request, out _));
        Assert.False(files.TryFind("linux/vmlinuz", s_request, out _));
    }

    // A DHCP server that names the boot file only in option 67 makes shim ask for GRUB beside nothing.
    [Theory]
    [InlineData("grubx64.efi")]
    [InlineData("/grubx64.efi")]
    public void AnswersGrubAtTheRootToo(string requested)
    {
        Assert.Equal("linux-boot/grubx64.efi", PathOf(Find(requested)));
    }

    [Fact]
    public void TheBootDirectorysOwnGrubAtTheRootStaysTheOneThatIsServed()
    {
        File.WriteAllText(Path.Combine(_boot, "grubx64.efi"), "an administrator's own");

        Assert.Equal("boot/grubx64.efi", PathOf(Find("grubx64.efi")));
    }

    [Theory]
    [InlineData("shimx64.efi")]
    [InlineData("vmlinuz")]
    [InlineData("base.img")]
    [InlineData("site.cpio")]
    [InlineData("grub.cfg")]
    [InlineData("GRUBX64.EFI")]
    public void NothingElseOfTheBundleIsAnsweredAtTheRoot(string requested)
    {
        Assert.Null(Find(requested));
    }

    [Theory]
    [InlineData("x64/bootmgfw.efi", "boot/x64/bootmgfw.efi")]
    [InlineData(@"\X64\BOOTMGFW.EFI", "boot/x64/bootmgfw.efi")]
    public void EverythingElseComesFromTheBootDirectoryAsBefore(string requested, string expected)
    {
        Assert.Equal(expected, PathOf(Find(requested)));
    }

    [Theory]
    [InlineData("linux/revocations_sku.efi")]
    [InlineData("linux/revocations_sbat.efi")]
    [InlineData("linux/shim_certificate_0.efi")]
    [InlineData("revocations_sku.efi")]
    [InlineData("revocations_sbat.efi")]
    [InlineData("shim_certificate_0.efi")]
    [InlineData("grub/x86_64-efi/command.lst")]
    [InlineData("grub/x86_64-efi/fs.lst")]
    [InlineData("grub/x86_64-efi/crypto.lst")]
    [InlineData("grub/x86_64-efi/terminal.lst")]
    public void TheFilesShimAndGrubOnlyLookForAreKnownAndRefused(string requested)
    {
        Assert.Null(Find(requested));
        Assert.Null(Find("/" + requested));
        Assert.True(BootProbes.IsProbe(requested));
        Assert.True(BootProbes.IsProbe(BootFiles.Normalized("/" + requested)!));
    }

    [Theory]
    [InlineData("linux/grubx64.efi")]
    [InlineData("linux/shimx64.efi")]
    [InlineData("grubx64.efi")]
    [InlineData("grub/grub.cfg")]
    [InlineData("grub/x86_64-efi/grub.cfg")]
    [InlineData("grub/x86_64-efi/sub/command.lst")]
    [InlineData("grub/fonts/unicode.pf2")]
    [InlineData("x64/bootmgfw.efi")]
    [InlineData("x64/revocations_sbat.efi")]
    [InlineData("linux/revocations_sbat.efi/x")]
    [InlineData("linux/sub/shim_certificate_0.efi")]
    [InlineData("Boot/BCD")]
    [InlineData("../certs/ddt-key.pem")]
    public void ANameThatIsNoProbeIsReportedAsMissing(string requested)
    {
        Assert.False(BootProbes.IsProbe(requested));
    }

    [Fact]
    public void AProbeNameThatTheBootDirectoryHoldsIsServedFromThere()
    {
        File.WriteAllText(Path.Combine(_boot, "revocations_sbat.efi"), "an administrator's own");

        Assert.Equal("boot/revocations_sbat.efi", PathOf(Find("revocations_sbat.efi")));
    }
}
