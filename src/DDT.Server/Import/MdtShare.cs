// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Xml;
using System.Xml.Linq;
using DDT.Contracts.Import;

namespace DDT.Server.Import;

// Reads an MDT deployment share by the lists in its Control folder: the operating systems' image files, and the
// folders of Out-of-Box Drivers as groups with the drivers in each. It only reads; the share stays as it is.
public static class MdtShare
{
    // A folder whose makers DDT knows by name, so "Dell Inc.\Latitude 7440" is a model and "WinPE x64\Net" is none
    private static readonly string[] s_makers =
    [
        "Acer", "ASUS", "Dell", "Dynabook", "Framework", "Fujitsu", "Getac", "Gigabyte", "Hewlett", "HP", "Huawei", "innotek", "Intel", "Lenovo", "LG",
        "Micro-Star", "Microsoft", "MSI", "Panasonic", "Parallels", "QEMU", "Razer", "Samsung", "Sony", "Toshiba", "VMware",
    ];

    public static bool IsShare(string folder) => File.Exists(Path.Combine(folder, "Control", "OperatingSystems.xml"));

    public static MdtShareView Read(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        return new MdtShareView(
            root,
            ImageFiles(root),
            [.. Groups(root).Select(group => View(group.Id, group.Name, group.Folders))],
            new MdtNotImported(
                Names(root, "Applications.xml", "application"),
                Names(root, "TaskSequences.xml", "ts"),
                Sections(Path.Combine(root, "Control", "CustomSettings.ini"))));
    }

    // The folders of a group's drivers, or none for an identifier the share has no group for.
    public static IReadOnlyList<string> DriverFolders(string root, string groupId) =>
        Groups(root).FirstOrDefault(group => group.Id == groupId).Folders ?? [];

    // A path the share's lists give, such as ".\Operating Systems\W11\sources\install.wim". Null when it leaves the share.
    public static string? Resolve(string root, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        string trimmed = relative.Replace('\\', '/').TrimStart('.').TrimStart('/');
        string full = Path.GetFullPath(Path.Combine(root, trimmed.Replace('/', Path.DirectorySeparatorChar)));

        return ImportFolders.Within(root, full) ? full : null;
    }

    private static List<MdtImageFile> ImageFiles(string root) =>
    [
        .. Elements(root, "OperatingSystems.xml", "os")
            .Select(os => (File: os.Element("ImageFile")?.Value.Trim(), Name: os.Element("Description")?.Value ?? os.Element("Name")?.Value ?? string.Empty))
            .Where(os => !string.IsNullOrEmpty(os.File) && ImportFolders.IsImage(os.File))
            .GroupBy(os => os.File ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(file =>
            {
                FileInfo? found = Resolve(root, file.Key) is { } path && File.Exists(path) ? new FileInfo(path) : null;

                return new MdtImageFile(file.Key, found?.Length ?? 0, found is not null, [.. file.Select(os => os.Name).Distinct()]);
            }),
    ];

    // Each group with the folders of its drivers. A folder without drivers of its own is no group here.
    private static List<(string Id, string Name, IReadOnlyList<string> Folders)> Groups(string root)
    {
        Dictionary<string, string> drivers = [];

        foreach (XElement driver in Elements(root, "Drivers.xml", "driver"))
        {
            // MDT names the driver's .inf file
            string? source = Resolve(root, driver.Element("Source")?.Value);
            string? folder = source is not null && Path.HasExtension(source) ? Path.GetDirectoryName(source) : source;

            if (driver.Attribute("guid")?.Value is { } id && folder is not null && Directory.Exists(folder))
            {
                drivers[id] = folder;
            }
        }

        return
        [
            .. Elements(root, "DriverGroups.xml", "group")
                .Select(group => (
                    Id: group.Attribute("guid")?.Value ?? string.Empty,
                    Name: group.Element("Name")?.Value.Trim() ?? string.Empty,
                    Folders: (IReadOnlyList<string>)[.. group.Elements("Member").Select(member => drivers.GetValueOrDefault(member.Value.Trim())).OfType<string>().Distinct()]))
                .Where(group => group.Id.Length > 0 && group.Name.Length > 0 && group.Folders.Count > 0)
                .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static MdtDriverGroup View(string id, string name, IReadOnlyList<string> folders)
    {
        string[] parts = name.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool model = parts.Length >= 3 && Array.Exists(s_makers, maker => parts[^2].Contains(maker, StringComparison.OrdinalIgnoreCase));
        long size = folders.Sum(folder => new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length));

        return new MdtDriverGroup(id, name, folders.Count, size, model ? parts[^2] : null, model ? parts[^1] : null);
    }

    private static List<string> Names(string root, string list, string element) =>
        [.. Elements(root, list, element).Select(entry => entry.Element("Name")?.Value.Trim()).OfType<string>().Where(name => name.Length > 0)];

    // A list in the share's Control folder. One that is missing or broken holds nothing to import.
    private static IEnumerable<XElement> Elements(string root, string list, string element)
    {
        string path = Path.Combine(root, "Control", list);

        try
        {
            return File.Exists(path) ? [.. XDocument.Load(path).Descendants(element)] : [];
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static List<string> Sections(string path)
    {
        try
        {
            return File.Exists(path)
                ? [.. File.ReadLines(path).Select(line => line.Trim()).Where(line => line.StartsWith('[') && line.EndsWith(']')).Select(line => line[1..^1])]
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
