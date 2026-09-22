// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Server.Machines;

namespace DDT.Server.Packages;

// The hardware models a driver package is for, stored as JSON text in Package.Targets.
public static class PackageTargets
{
    public static IReadOnlyList<HardwareModel> Read(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return JsonSerializer.Deserialize(package.Targets, DdtJsonContext.Default.IReadOnlyListHardwareModel) ?? [];
    }

    public static string Write(IReadOnlyList<HardwareModel> targets) =>
        JsonSerializer.Serialize(targets, DdtJsonContext.Default.IReadOnlyListHardwareModel);

    // Cleaned as HardwareModels cleans them, in the order given.
    public static IReadOnlyList<HardwareModel> Clean(IReadOnlyList<HardwareModel> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        return [.. targets.Select(t => new HardwareModel(HardwareModels.Clean(t.Manufacturer), HardwareModels.Clean(t.Model) ?? ""))];
    }

    // What is wrong with the targets a request sets, or null. A Files package is chosen by the steps that name it.
    public static string? Problem(PackageKind kind, IReadOnlyList<HardwareModel?>? targets)
    {
        if (targets is null)
        {
            return "Send the list of targets, empty for none.";
        }

        if (targets.Count > 0 && kind == PackageKind.Files)
        {
            return "A Files package is unpacked for the Run script steps that name it, not by the machine's model, so it has no targets.";
        }

        if (targets.Count > PackageLimits.MaxTargets)
        {
            return $"A package can have at most {PackageLimits.MaxTargets} targets.";
        }

        HashSet<(string?, string?)> seen = [];

        foreach (HardwareModel? target in targets)
        {
            if (target is null)
            {
                return "A target is empty.";
            }

            if (HardwareModels.Problem(target.Manufacturer, required: false, wildcard: false) is { } manufacturer)
            {
                return manufacturer;
            }

            if (HardwareModels.Problem(target.Model, required: true, wildcard: true) is { } model)
            {
                return model;
            }

            if (!seen.Add((HardwareModels.Normalize(target.Manufacturer), HardwareModels.Normalize(target.Model))))
            {
                return $"{target.Model} is a target twice.";
            }
        }

        return null;
    }

    public static bool Matches(IReadOnlyList<HardwareModel> targets, string? manufacturer, string? model)
    {
        ArgumentNullException.ThrowIfNull(targets);

        return targets.Any(t => HardwareModels.Matches(t.Manufacturer, manufacturer) && HardwareModels.Matches(t.Model, model));
    }
}
