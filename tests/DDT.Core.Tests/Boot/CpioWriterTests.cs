// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text;
using DDT.Core.Boot;
using Xunit;

namespace DDT.Core.Tests.Boot;

public sealed class CpioWriterTests
{
    private const string Trailer =
        "07070100000000000000000000000000000000000000010000000000000000000000000000000000000000000000000000000B00000000TRAILER!!!\0\0\0\0";

    private static readonly DateTimeOffset s_time = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static string Written(params CpioEntry[] entries) => Encoding.Latin1.GetString(CpioWriter.Write(entries, s_time));

    // The archive build/LinuxBoot/mkcpio.py writes for the same tree at the same time, byte for byte.
    [Fact]
    public void WritesTheBytesTheBuildsOwnWriterGivesForTheSameTree()
    {
        byte[] archive = CpioWriter.Write(
            [
                CpioEntry.File("ddt/b", "12345"u8.ToArray()),
                CpioEntry.Folder("ddt/sub"),
                CpioEntry.File("ddt/agent.json", "{}\n"u8.ToArray()),
                CpioEntry.Folder("ddt"),
            ],
            s_time);

        string expected =
            "07070100000001000041ED0000000000000000000000036553F10000000000000000000000000000000000000000000000000400000000ddt\0\0\0"
            + "07070100000002000081A40000000000000000000000016553F10000000003000000000000000000000000000000000000000F00000000ddt/agent.json\0\0\0\0"
            + "{}\n\0"
            + "07070100000003000081A40000000000000000000000016553F10000000005000000000000000000000000000000000000000600000000ddt/b\0"
            + "12345\0\0\0"
            + "07070100000004000041ED0000000000000000000000026553F10000000000000000000000000000000000000000000000000800000000ddt/sub\0\0\0"
            + Trailer;

        Assert.Equal(expected, Encoding.Latin1.GetString(archive));
        Assert.Equal("839dc1473ba74d0588831433cb328274d4dc14c5a8d0ad2c56c6eb6f7c65f735", Convert.ToHexStringLower(SHA256.HashData(archive)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void PadsEveryNameAndEveryContentToFourBytes(int length)
    {
        byte[] content = [.. Enumerable.Repeat((byte)'x', length)];
        byte[] archive = CpioWriter.Write([CpioEntry.File(new string('n', length + 1), content)], s_time);

        Assert.Equal(0, archive.Length % 4);

        // The header and the name with its zero, then the content, each on a boundary of four.
        int name = (110 + length + 2 + 3) / 4 * 4;
        int padded = (length + 3) / 4 * 4;
        Assert.Equal(name + padded + Trailer.Length, archive.Length);
        Assert.Equal(content, archive[name..(name + length)]);
        Assert.All(archive[(name + length)..(name + padded)], pad => Assert.Equal(0, pad));
    }

    [Fact]
    public void AnArchiveWithoutEntriesIsTheTrailerAlone()
    {
        Assert.Equal(Trailer, Written());
    }

    [Fact]
    public void GivesTheSameBytesWhateverTheOrderOfTheEntries()
    {
        CpioEntry[] entries = [CpioEntry.Folder("b"), CpioEntry.File("b/z", new byte[] { 1 }), CpioEntry.File("a", new byte[] { 2 }), CpioEntry.Folder("b/c")];

        Assert.Equal(CpioWriter.Write(entries, s_time), CpioWriter.Write([.. entries.Reverse()], s_time));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/ddt/agent.json")]
    [InlineData("../agent.json")]
    [InlineData("ddt/../../etc/passwd")]
    [InlineData("ddt//agent.json")]
    [InlineData("ddt/./agent.json")]
    [InlineData("ddt\\agent.json")]
    [InlineData("ddt/agent.json\0")]
    public void RefusesAPathThatIsNotInsideTheArchive(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => CpioWriter.Write([CpioEntry.File(path, new byte[] { 1 })], s_time));
    }
}
