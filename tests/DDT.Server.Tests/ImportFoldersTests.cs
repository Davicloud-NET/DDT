// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Import;
using DDT.Server.Configuration;
using DDT.Server.Import;
using Microsoft.Extensions.Options;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ImportFoldersTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ddt-import-folders-").FullName;

    [Fact]
    public void AFileInAnImportFolderWithinAnotherIsListedOnce()
    {
        string downloads = Path.Combine(_root, "downloads");
        string iso = Path.Combine(Directory.CreateDirectory(Path.Combine(downloads, "lof-iso")).FullName, "languages.iso");
        File.WriteAllText(iso, "iso");
        ImportFolders folders = new(Options.Create(new DdtOptions
        {
            StorePath = Path.Combine(_root, "store"),
            ImportFolders = [downloads, Path.Combine(downloads, "lof-iso")],
        }));

        IReadOnlyList<ImportFile> files = folders.Files();

        Assert.Equal([iso], files.Select(file => file.Path));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
