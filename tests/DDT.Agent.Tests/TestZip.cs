// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DDT.Agent.Tests;

// A zip made in memory from entry names and their text, and its SHA-256. A name ending in / is a directory.
internal sealed class TestZip
{
    public TestZip(params (string Name, string Text)[] entries)
    {
        using MemoryStream zip = new();

        using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);

                if (!name.EndsWith('/'))
                {
                    using Stream content = entry.Open();
                    content.Write(Encoding.UTF8.GetBytes(text));
                }
            }
        }

        Content = zip.ToArray();
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(Content));
    }

    public byte[] Content { get; }

    public string Sha256 { get; }
}
