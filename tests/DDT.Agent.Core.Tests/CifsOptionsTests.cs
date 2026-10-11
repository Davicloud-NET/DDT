// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// The option string of a CIFS mount, as the kernels of two cloud images took it from the mount call.
public sealed class CifsOptionsTests
{
    private static readonly IPAddress s_address = IPAddress.Parse("10.0.2.2");

    // After a comma that is not doubled, the kernel logs the rest of the password as an unknown option. Nothing else
    // in a password means anything to it: not a backslash, a space, an equals sign or a quote.
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "a,,b")]
    [InlineData(",lead and trail,", ",,lead and trail,,")]
    [InlineData("two,,in a row", "two,,,,in a row")]
    [InlineData(@"back\slash, sp ace,=sec=none,""q'", @"back\slash,, sp ace,,=sec=none,,""q'")]
    public void DoublesEveryCommaOfAPasswordAndChangesNothingElse(string password, string escaped) =>
        Assert.Equal(
            $"ip=10.0.2.2,username=ddt,password={escaped},sec=ntlmssp,file_mode=0700,dir_mode=0700",
            CifsOptions.For(s_address, "ddt", password));

    [Theory]
    [InlineData(@"CORP\ddt-read", "username=ddt-read,domain=CORP")]
    [InlineData(@".\local", "username=local")]
    [InlineData("ddt@corp.example.test", "username=ddt@corp.example.test")]
    [InlineData(@"CO,RP\d,dt", "username=d,,dt,domain=CO,,RP")]
    public void NamesTheDomainOfAnAccountApart(string userName, string account) =>
        Assert.Equal($"ip=10.0.2.2,{account},password=pw,sec=ntlmssp,file_mode=0700,dir_mode=0700", CifsOptions.For(s_address, userName, "pw"));

    [Fact]
    public void GivesNoOptionsForAnAccountTheMountCallCannotCarry()
    {
        Assert.Null(CifsOptions.For(s_address, "ddt", "cut\0here"));
        Assert.Null(CifsOptions.For(s_address, "ddt", new string(',', 2100)));
        Assert.NotNull(CifsOptions.For(IPAddress.Parse("fd00::17"), "ddt", new string('x', 3000)));
    }

    [Theory]
    [InlineData(@"\\files.example.com\drivers", "files.example.com", "//files.example.com/drivers", @"\\files.example.com\drivers")]
    [InlineData(@" \\10.0.100.200\Deploy Share\Linux\tools\ ", "10.0.100.200", "//10.0.100.200/Deploy Share/Linux/tools", @"\\10.0.100.200\Deploy Share\Linux\tools")]
    [InlineData("//files/drivers/x", "files", "//files/drivers/x", @"\\files\drivers\x")]
    public void ReadsASharesPathAsWindowsWritesItForTheKernel(string path, string host, string source, string unc)
    {
        SharePath share = SharePath.Parse(path);

        Assert.Equal((host, source, unc), (share.Host, share.Source, share.Unc));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"C:\share")]
    [InlineData(@"\\files")]
    [InlineData(@"\\\drivers")]
    [InlineData(@"\\?\C:\x")]
    [InlineData(@"\\files\drivers\..\other")]
    [InlineData(@"\\files\drivers\\x")]
    public void RefusesWhatIsNotTheFullPathOfAShare(string path) =>
        Assert.Equal(
            $"{path} is not the path of a share, such as \\\\files.example.com\\drivers.",
            Assert.Throws<DeploymentStepException>(() => SharePath.Parse(path)).Message);
}
