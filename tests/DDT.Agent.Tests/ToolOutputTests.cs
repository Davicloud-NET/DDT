// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ToolOutputTests
{
    private static async Task<List<string>> LinesOfAsync(byte[] output)
    {
        List<string> lines = [];
        await ToolOutput.ForwardAsync(new MemoryStream(output), lines.Add);

        return lines;
    }

    [Fact]
    public async Task ReadsUtf8AndOemLinesOfOneOutputEachInTheirOwnEncoding()
    {
        // "für" as tree writes it in code page 437 or 850, both of which put ü at 0x81, after a UTF-8 line.
        byte[] output = [.. Encoding.UTF8.GetBytes("Grüße aus dem Skript\r\n"), 0x66, 0x81, 0x72, (byte)'\r', (byte)'\n'];

        Assert.Equal(["Grüße aus dem Skript", "für"], await LinesOfAsync(output));
    }

    [Fact]
    public async Task EndsALineAtEitherBreakAndSkipsEmptyAndBlankLines()
    {
        byte[] output = Encoding.UTF8.GetBytes("one\r\n\r\ntwo\n   \nthree 10%\rthree 20%  \rfour");

        Assert.Equal(["one", "two", "three 10%", "three 20%", "four"], await LinesOfAsync(output));
    }

    [Fact]
    public async Task KeepsALineWholeAcrossReads()
    {
        string longLine = new('ä', 5000);

        Assert.Equal([longLine, "end"], await LinesOfAsync(Encoding.UTF8.GetBytes($"{longLine}\nend\n")));
    }

    [Fact]
    public void PlainAsciiReadsTheSameEitherWay()
    {
        Assert.Equal("C:.", ToolOutput.Decode("C:."u8));
    }
}
