// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Applications.Installers;

// The properties of an MSI that name the product, read from its Property table without msi.dll, so on Linux too.
public sealed record MsiProperties(string? ProductCode, string? ProductName, string? ProductVersion, string? Manufacturer, string? UpgradeCode)
{
    // Real Property tables have a few hundred rows.
    private const int MaxPropertyRows = 100_000;

    private static readonly string[] s_names = ["ProductCode", "ProductName", "ProductVersion", "Manufacturer", "UpgradeCode"];

    // Returns null for a file that isn't a readable MSI.
    public static MsiProperties? Read(Stream msi)
    {
        ArgumentNullException.ThrowIfNull(msi);

        if (!msi.CanRead || !msi.CanSeek)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(msi));
        }

        try
        {
            return ReadDatabase(CompoundFile.Open(msi));
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            return null;
        }
    }

    private static MsiProperties? ReadDatabase(CompoundFile file)
    {
        Dictionary<string, CompoundFileEntry> tables = [];

        foreach (CompoundFileEntry stream in file.Streams)
        {
            tables.TryAdd(MsiStreamName.Decode(stream.Name), stream);
        }

        if (!tables.TryGetValue("!_StringPool", out CompoundFileEntry? pool)
            || !tables.TryGetValue("!_StringData", out CompoundFileEntry? data)
            || !tables.TryGetValue("!Property", out CompoundFileEntry? property))
        {
            return null;
        }

        MsiStringPool strings = new(file.Read(pool), file.Read(data));
        Dictionary<string, string> values = ReadPropertyTable(file.Read(property), strings);

        return new MsiProperties(
            values.GetValueOrDefault("ProductCode"),
            values.GetValueOrDefault("ProductName"),
            values.GetValueOrDefault("ProductVersion"),
            values.GetValueOrDefault("Manufacturer"),
            values.GetValueOrDefault("UpgradeCode"));
    }

    // Property has two string columns, Property and Value. Tables are stored column by column.
    private static Dictionary<string, string> ReadPropertyTable(byte[] table, MsiStringPool strings)
    {
        int size = strings.ReferenceSize;
        int rows = table.Length / (2 * size);

        if (table.Length % (2 * size) != 0 || rows > MaxPropertyRows)
        {
            throw new InvalidDataException("The Property table is damaged.");
        }

        int[] nameIds = new int[rows];
        int[] valueIds = new int[rows];

        for (int row = 0; row < rows; row++)
        {
            nameIds[row] = Reference(table, row * size, size);
            valueIds[row] = Reference(table, (rows + row) * size, size);
        }

        Dictionary<int, string> names = strings.Strings(new HashSet<int>(nameIds));
        Dictionary<string, int> wanted = new(StringComparer.Ordinal);

        for (int row = 0; row < rows; row++)
        {
            if (names.TryGetValue(nameIds[row], out string? name) && s_names.Contains(name, StringComparer.Ordinal))
            {
                wanted.TryAdd(name, valueIds[row]);
            }
        }

        Dictionary<int, string> found = strings.Strings(new HashSet<int>(wanted.Values));

        return wanted
            .Where(pair => found.ContainsKey(pair.Value))
            .ToDictionary(pair => pair.Key, pair => found[pair.Value], StringComparer.Ordinal);
    }

    private static int Reference(byte[] table, int offset, int size) =>
        table[offset] | (table[offset + 1] << 8) | (size == 3 ? table[offset + 2] << 16 : 0);
}
