// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Pxe.Tests;

public sealed class KernelArgumentsTests
{
    [Theory]
    [InlineData("console=ttyS0,115200")]
    [InlineData("quiet")]
    [InlineData("module_blacklist=nouveau,amdgpu")]
    [InlineData("pci=noaer")]
    [InlineData("video=efifb:off")]
    [InlineData("ip=10.0.0.5::10.0.0.1:255.255.255.0")]
    [InlineData("root=/dev/ram0")]
    [InlineData("a+b@c")]
    [InlineData("ddt.shell")]
    public void AWordOfTheAllowedCharactersMayBeAdded(string word)
    {
        Assert.True(KernelArguments.IsWord(word));
        Assert.False(KernelArguments.IsReserved(word));
        Assert.Equal([word], KernelArguments.Allowed(word));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-quiet")]
    [InlineData("--")]
    [InlineData("a b")]
    [InlineData("a;b")]
    [InlineData("a$b")]
    [InlineData("a\"b")]
    [InlineData("a'b")]
    [InlineData("a\\b")]
    [InlineData("a{b")]
    [InlineData("a(b")]
    [InlineData("a\nb")]
    [InlineData("aä")]
    public void AWordWithAnythingElseIsNone(string word)
    {
        Assert.False(KernelArguments.IsWord(word));
    }

    // DDT says which program starts in the initramfs and what the start scripts read. An added word may not.
    [Theory]
    [InlineData("init=/bin/sh")]
    [InlineData("rdinit=/bin/sh")]
    [InlineData("rdinit")]
    [InlineData("initrd=other.img")]
    [InlineData("BOOT_IMAGE=x")]
    [InlineData("ddt.time=0")]
    [InlineData("ddt.bootif=00:00:00:00:00:00")]
    [InlineData("ddt.keymap=fr")]
    [InlineData("ddt.hostname=evil")]
    [InlineData("ddt.hostip=10.0.0.9")]
    [InlineData("ddt.hook=x")]
    [InlineData("DDT.hostip=10.0.0.9")]
    [InlineData("ddt.shell=1")]
    public void AWordThatSetsWhatDdtSetsIsReserved(string word)
    {
        Assert.True(KernelArguments.IsReserved(word));
        Assert.Empty(KernelArguments.Allowed("quiet " + word));
    }

    [Fact]
    public void SplitsAtAnyWhiteSpace()
    {
        Assert.Equal(["a=1", "b", "c=2"], KernelArguments.Split("  a=1\tb \r\n c=2 "));
        Assert.Empty(KernelArguments.Split(null));
        Assert.Empty(KernelArguments.Split("   "));
    }

    [Fact]
    public void OneWordThatIsNotAllowedLeavesNoneOfTheText()
    {
        Assert.Empty(KernelArguments.Allowed("quiet a;b console=ttyS0"));
    }

    [Fact]
    public void ATextLongerThanTheLimitLeavesNone()
    {
        string longest = string.Join(' ', Enumerable.Repeat("abcdefg", 32));
        Assert.Equal(KernelArguments.MaxLength - 1, longest.Length);

        Assert.Equal(32, KernelArguments.Allowed(longest).Count);
        Assert.Empty(KernelArguments.Allowed(longest + " x"));
    }
}
