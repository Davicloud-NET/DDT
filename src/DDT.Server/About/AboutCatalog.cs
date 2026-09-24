// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics.CodeAnalysis;
using DDT.Contracts.About;

namespace DDT.Server.About;

// The legal documents are the files the build put into the legal folder, listed once at start-up. They are served
// only by an exact match on that list, so no part of a request path ever reaches the file system.
public sealed class AboutCatalog
{
    public const string Product = "DDT";
    public const string Attribution = "DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud.";
    public const string License = "GPL-3.0-or-later";
    public const string SourceUrl = "https://github.com/Davicloud-NET/DDT";

    private readonly Dictionary<string, string> _documents;

    private AboutCatalog(string version, Dictionary<string, string> documents)
    {
        _documents = documents;
        Info = new AboutInfo(Product, version, Attribution, License, SourceUrl, [.. documents.Keys.Order(StringComparer.Ordinal)]);
    }

    public AboutInfo Info { get; }

    public static AboutCatalog Load(string version, string legalDirectory)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(legalDirectory);

        Dictionary<string, string> documents = new(StringComparer.Ordinal);

        if (Directory.Exists(legalDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(legalDirectory, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetRelativePath(legalDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
                documents.Add(name, Path.GetFullPath(file));
            }
        }

        return new AboutCatalog(version, documents);
    }

    public bool TryGetDocument(string? name, [NotNullWhen(true)] out string? path)
    {
        path = null;

        return name is not null && _documents.TryGetValue(name, out path);
    }
}
