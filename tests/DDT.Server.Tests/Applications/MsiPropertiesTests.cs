// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Applications.Installers;
using Xunit;

namespace DDT.Server.Tests.Applications;

public sealed class MsiPropertiesTests
{
    [Fact]
    public void AnMsiThatWindowsInstallerWroteIsRead()
    {
        MsiProperties? properties = Read(TestMsi.Fixture());

        Assert.Equal(
            new MsiProperties(TestMsi.ProductCode, "DDT Prüfpaket Größe", "1.2.3", "Davicloud", TestMsi.UpgradeCode),
            properties);
    }

    [Fact]
    public void StreamsInTheMiniStreamAreRead()
    {
        MsiProperties? properties = Read(TestMsi.Create(new TestMsiOptions { Properties = TestMsi.Product() }));

        Assert.Equal(Expected(), properties);
    }

    [Fact]
    public void StreamsInRegularSectorsAreRead()
    {
        TestMsiOptions options = new() { Properties = TestMsi.Product(new string('r', 5000)), EmptyStringsFirst = 2000 };

        Assert.Equal(Expected() with { ProductName = new string('r', 5000) }, Read(TestMsi.Create(options)));
    }

    [Fact]
    public void AFileWhoseLastSectorEndsWithItsDataIsRead()
    {
        byte[] msi = TestMsi.Create(new TestMsiOptions { Properties = TestMsi.Product(), EmptyStringsFirst = 2000, SectorSize = 4096 });

        Assert.Equal(Expected(), Read(msi[..^100]));
    }

    [Fact]
    public void AVersion4FileWith4096ByteSectorsIsRead()
    {
        TestMsiOptions options = new() { Properties = TestMsi.Product(), SectorSize = 4096 };

        Assert.Equal(Expected(), Read(TestMsi.Create(options)));
        Assert.Equal(Expected(), Read(TestMsi.Create(options with { EmptyStringsFirst = 5000 })));
    }

    [Fact]
    public void ThreeByteStringReferencesReachIdsPast65535()
    {
        TestMsiOptions options = new() { Properties = TestMsi.Product(), LongReferences = true, EmptyStringsFirst = 70_000 };

        Assert.Equal(Expected(), Read(TestMsi.Create(options)));
    }

    [Fact]
    public void AStringLongerThan65535BytesIsReadWithTheStringsAfterIt()
    {
        string description = new('x', 70_000);
        (string, string)[] properties = [("ARPCOMMENTS", description), .. TestMsi.Product()];

        Assert.Equal(Expected(), Read(TestMsi.Create(new TestMsiOptions { Properties = properties })));
    }

    [Fact]
    public void ALongValueIsReadWhole()
    {
        string name = new('n', 70_000);

        Assert.Equal(Expected() with { ProductName = name }, Read(TestMsi.Create(new TestMsiOptions { Properties = TestMsi.Product(name) })));
    }

    [Theory]
    [InlineData(1251, "Приложение")]
    [InlineData(65001, "Anwendung ✓ 応用")]
    [InlineData(1252, "Größe")]
    public void ValuesAreDecodedInTheDatabasesCodePage(int codePage, string name)
    {
        TestMsiOptions options = new() { Properties = TestMsi.Product(name), CodePage = codePage };

        Assert.Equal(name, Read(TestMsi.Create(options))?.ProductName);
    }

    [Fact]
    public void PropertiesWithTheSameValueBothGetIt()
    {
        (string, string)[] properties = [("ProductName", "Same"), ("Manufacturer", "Same")];

        MsiProperties? read = Read(TestMsi.Create(new TestMsiOptions { Properties = properties }));

        Assert.Equal(new MsiProperties(null, "Same", null, "Same", null), read);
    }

    [Fact]
    public void AMissingPropertyIsNull()
    {
        (string, string)[] properties = [("ProductName", "Only a name")];

        Assert.Equal(new MsiProperties(null, "Only a name", null, null, null), Read(TestMsi.Create(new TestMsiOptions { Properties = properties })));
    }

    private static MsiProperties Expected() => new(TestMsi.ProductCode, "Example App", "4.5.6", "Example Ltd", TestMsi.UpgradeCode);

    private static MsiProperties? Read(byte[] msi)
    {
        using MemoryStream stream = new(msi);

        return MsiProperties.Read(stream);
    }
}
