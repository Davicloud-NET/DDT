// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Unattend;
using Xunit;

namespace DDT.Core.Tests;

public sealed class AnswerFileValueShapeTests
{
    [Theory]
    [InlineData("VK7JG-NPHTM-C97JM-9MPGT-3V66T", true)]
    [InlineData(" vk7jg-nphtm-c97jm-9mpgt-3v66t ", true)]
    [InlineData("VK7JGNPHTMC97JM9MPGT3V66T", false)]
    [InlineData("VK7JG-NPHTM-C97JM-9MPGT", false)]
    [InlineData("VK7JG-NPHTM-C97JM-9MPGT-3V66T-", false)]
    [InlineData("VK7J!-NPHTM-C97JM-9MPGT-3V66T", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TakesAProductKeyOfFiveGroupsOfFive(string? key, bool valid) => Assert.Equal(valid, ProductKey.IsValid(key));

    [Theory]
    [InlineData("de-DE", true)]
    [InlineData("en-US", true)]
    [InlineData("sr-Latn-RS", true)]
    [InlineData("de", false)]
    [InlineData("de_DE", false)]
    [InlineData("de-DE;fr-FR", false)]
    public void TakesALanguageTagByItsShape(string tag, bool valid) => Assert.Equal(valid, WindowsSetupValues.IsUiLanguage(tag));

    [Fact]
    public void ReadsAddressesWithTheirPrefixLength()
    {
        Assert.Equal(["10.0.4.51/24", "10.0.5.1/16"], NetworkValues.ParseAddresses(" 10.0.4.51/24 ; 10.0.5.1/16 "));
    }

    [Theory]
    [InlineData("10.0.4.51")]
    [InlineData("10.0.4.51/0")]
    [InlineData("10.0.4.51/33")]
    [InlineData("10.0.4/24")]
    [InlineData("10.0.4.256/24")]
    [InlineData("fd00::1/64")]
    [InlineData(";")]
    [InlineData("1.1.1.1/8;1.1.1.2/8;1.1.1.3/8;1.1.1.4/8;1.1.1.5/8;1.1.1.6/8;1.1.1.7/8;1.1.1.8/8;1.1.1.9/8")]
    public void RefusesWhatIsNoIpv4AddressWithAPrefix(string text) => Assert.Null(NetworkValues.ParseAddresses(text));

    [Theory]
    [InlineData("10")]
    [InlineData("0x0A000001")]
    [InlineData("10.0.0.1.2")]
    [InlineData("10.0.0.+1")]
    public void TakesOnlyFourDecimalNumbersAsAnAddress(string text) => Assert.Null(NetworkValues.ParseAddress(text));

    [Fact]
    public void ReadsADnsServerList() => Assert.Equal(["10.0.4.2", "1.1.1.1"], NetworkValues.ParseAddressList("10.0.4.2;1.1.1.1"));

    [Theory]
    [InlineData("00155D010203")]
    [InlineData("00:15:5d:01:02:03")]
    [InlineData("00-15-5D-01-02-03")]
    public void NamesTheAdapterByItsMacAddressWithDashes(string mac) => Assert.Equal("00-15-5D-01-02-03", NetworkValues.Identifier(mac));
}
