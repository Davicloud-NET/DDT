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

    // The kernel copies 4095 bytes, and a letter outside ASCII is more than one of them.
    [Fact]
    public void CountsTheLimitInBytesNotInCharacters()
    {
        int rest = CifsOptions.MaxBytes - System.Text.Encoding.UTF8.GetByteCount(CifsOptions.For(s_address, "ddt", "x") ?? "");

        Assert.NotNull(CifsOptions.For(s_address, "ddt", "x" + new string('ä', rest / 2)));
        Assert.Null(CifsOptions.For(s_address, "ddt", "x" + new string('ä', (rest / 2) + 1)));
        Assert.True(rest / 2 < 2100);
    }

    // SELinux reads these after any comma of the options, also one that the doubling made of a value's own.
    [Theory]
    [InlineData("ddt", "a,context=b", "password")]
    [InlineData("ddt", "a,fscontext=b", "password")]
    [InlineData("ddt", "a,defcontext=b", "password")]
    [InlineData("ddt", "a,rootcontext=b", "password")]
    [InlineData("ddt", "a,seclabel", "password")]
    [InlineData("ddt", "a,seclabel,b", "password")]
    [InlineData("ddt", "a,,,context=b", "password")]
    [InlineData("dd,context=t", "a", "user name")]
    [InlineData(@"CO,seclabel\ddt", "a", "domain")]
    [InlineData(@".\dd,seclabel", "a", "user name")]
    public void FindsThePartOfAnAccountThatHoldsAnOptionOfSeLinux(string userName, string password, string part) =>
        Assert.Equal(part, CifsOptions.TakenBySecurityModule(userName, password));

    // The words alone mean nothing to it: only at the start of an option, and the flag only as a whole one.
    [Theory]
    [InlineData("context=b")]
    [InlineData("a,my context=b")]
    [InlineData("a, context=b")]
    [InlineData("a,contextb")]
    [InlineData("a,seclabels")]
    [InlineData("a,seclabel=b")]
    [InlineData("a,CONTEXT=b")]
    [InlineData("a;context=b,c")]
    public void TakesAnAccountThatOnlyLooksLikeOne(string password)
    {
        Assert.Null(CifsOptions.TakenBySecurityModule("ddt", password));
        Assert.Null(CifsOptions.TakenBySecurityModule(password, "a"));
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
