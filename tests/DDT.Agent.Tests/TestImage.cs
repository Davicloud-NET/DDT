// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

// A small image file and what the server says about it.
internal sealed class TestImage
{
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
    {
        Content = RandomNumberGenerator.GetBytes(length);
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(Content));
    }

    public byte[] Content { get; }

    public string Sha256 { get; }

    public static Guid ImageId { get; } = Guid.Parse("0193a4b2-0000-7000-8000-00000000a001");

    // Image 1 of the file, which takes 10 000 bytes installed.
    public AgentRunImage RunImage => new(ImageId, "Windows 11 Pro", Sha256, Content.Length, 1, 10_000);

    // The rest of the file from offset, as a 206 answer or, from 0, a 200.
    public AgentImageStream From(long offset) =>
        new(new MemoryStream(Content[(int)offset..]), offset, Content.Length);

    // Serves the image as a run file, and the answer file once, which is all a run needs from the server apart from
    // the reports, which echo by default.
    public ScriptedAgentServer Serve(ScriptedAgentServer server) =>
        server
            .ServeFile(Sha256, Content)
            .OnRunUnattend(_ => Unattend);
}
