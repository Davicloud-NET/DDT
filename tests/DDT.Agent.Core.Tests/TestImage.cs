// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

// A small image file and what the server says about it.
internal sealed class TestImage
{
    private const int WimHeaderLength = 208;

    public const string Unattend = """
        <?xml version="1.0" encoding="utf-8"?>
        <unattend xmlns="urn:schemas-microsoft-com:unattend">
          <settings pass="specialize">
            <component name="Microsoft-Windows-Shell-Setup">
              <ComputerName>PC-042</ComputerName>
            </component>
          </settings>
          <settings pass="oobeSystem">
            <component name="Microsoft-Windows-International-Core">
              <InputLocale>de-DE</InputLocale>
              <UILanguage>en-US</UILanguage>
              <UserLocale>de-DE</UserLocale>
            </component>
            <component name="Microsoft-Windows-Shell-Setup">
              <UserAccounts>
                <LocalAccounts>
                  <LocalAccount>
                    <Name>Admin</Name>
                    <Password><Value>c2VjcmV0UGFzc3dvcmQ=</Value><PlainText>false</PlainText></Password>
                  </LocalAccount>
                </LocalAccounts>
              </UserAccounts>
            </component>
          </settings>
        </unattend>
        """;

    public TestImage(int length = 3000)
        : this(RandomNumberGenerator.GetBytes(length))
    {
    }

    private TestImage(byte[] content)
    {
        Content = content;
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(Content));
    }

    // As much of a WIM file as a dry run reads to check that it holds image 1: the 208-byte header and the image list,
    // uncompressed UTF-16 LE with a byte order mark.
    public static TestImage Wim()
    {
        byte[] list = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("<WIM><IMAGE INDEX=\"1\"><NAME>Windows 11 Pro</NAME></IMAGE></WIM>")];
        byte[] file = new byte[WimHeaderLength + list.Length];
        Span<byte> header = file.AsSpan(0, WimHeaderLength);

        "MSWIM\0\0\0"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], WimHeaderLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], 0x10D00);
        BinaryPrimitives.WriteUInt16LittleEndian(header[42..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], 1);

        // The image list's resource: its size in the file without flags, its offset and its size.
        BinaryPrimitives.WriteUInt64LittleEndian(header[72..], (ulong)list.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header[80..], WimHeaderLength);
        BinaryPrimitives.WriteUInt64LittleEndian(header[88..], (ulong)list.Length);
        list.CopyTo(file, WimHeaderLength);

        return new TestImage(file);
    }

    public byte[] Content { get; }

    public string Sha256 { get; }

    public static Guid ImageId { get; } = Guid.Parse("0193a4b2-0000-7000-8000-00000000a001");

    // Image 1 of the file, which takes 10 000 bytes installed.
    public AgentRunImage RunImage => new(ImageId, "Windows 11 Pro", Sha256, Content.Length, 1, 10_000);

    // The rest of the file from offset, as a 206 answer, or as a 200 from offset 0.
    public AgentImageStream From(long offset) =>
        new(new MemoryStream(Content[(int)offset..]), offset, Content.Length);

    // Serves the image as a run file and the answer file once. Apart from the reports, which echo by default, that's
    // all a run needs from the server.
    public ScriptedAgentServer Serve(ScriptedAgentServer server) =>
        server
            .ServeFile(Sha256, Content)
            .OnRunUnattend(_ => Unattend);
}
