// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Core.Applications;

namespace DDT.Server.Import;

// Which library entry each of a share's applications became during an import, and the library's names, so a dependency
// links to an application imported now or earlier. Applications and bundles share one namespace.
public sealed class MdtApplicationLinks(
    IReadOnlyDictionary<string, MdtShareApplication> entries,
    IReadOnlySet<(string Application, string Dependency)> cut)
{
    private readonly Dictionary<string, (Guid Id, bool IsApplication)> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _imported = new(StringComparer.Ordinal);

    // The library entry of that name, by ApplicationNames.Normalize.
    public (Guid Id, bool IsApplication)? Named(string name) =>
        _names.TryGetValue(ApplicationNames.Normalize(name) ?? "", out (Guid Id, bool IsApplication) entry) ? entry : null;

    public void AddLibrary(string name, Guid id, bool isApplication) => _names.TryAdd(ApplicationNames.Normalize(name) ?? "", (id, isApplication));

    // The share's entry is the library's application of that id, imported now or found by its name.
    public void Link(MdtShareApplication entry, Guid id)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _imported[entry.Id] = id;
    }

    // The library's applications the dependencies come to, each once. What is left out goes into notes.
    public IReadOnlyList<Guid> Resolve(MdtShareApplication from, IReadOnlyList<string> dependencies, List<ServerMessage> notes)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(notes);

        List<Guid> resolved = [];

        foreach (string dependency in dependencies)
        {
            string name = entries.GetValueOrDefault(dependency)?.Name ?? dependency;

            if (dependency == from.Id || cut.Contains((from.Id, dependency)))
            {
                notes.Add(ServerMessages.ImportDependencyCycle.With("dependency", name));
            }
            else if (IdOf(dependency) is { } id)
            {
                if (!resolved.Contains(id))
                {
                    resolved.Add(id);
                }
            }
            else
            {
                notes.Add(ServerMessages.ImportDependencyMissing.With("dependency", name));
            }
        }

        return resolved;
    }

    // Imported in this run or, for one of the share's applications, in the library by its name.
    private Guid? IdOf(string dependency)
    {
        if (_imported.TryGetValue(dependency, out Guid id))
        {
            return id;
        }

        return entries.GetValueOrDefault(dependency) is { IsBundle: false } entry && Named(entry.Name) is { IsApplication: true } named ? named.Id : null;
    }
}
