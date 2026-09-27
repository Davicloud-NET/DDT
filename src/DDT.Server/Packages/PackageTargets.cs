// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
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
    public static ServerMessage? Problem(PackageKind kind, IReadOnlyList<HardwareModel?>? targets)
    {
        if (targets is null)
        {
            return ServerMessages.PackageTargetsMissing.With();
        }

        if (targets.Count > 0 && kind == PackageKind.Files)
        {
            return ServerMessages.PackageFilesHaveNoTargets.With();
        }

        if (targets.Count > PackageLimits.MaxTargets)
        {
            return ServerMessages.PackageTooManyTargets.With("max", PackageLimits.MaxTargets);
        }

        HashSet<(string?, string?)> seen = [];

        foreach (HardwareModel? target in targets)
        {
            if (target is null)
            {
                return ServerMessages.PackageTargetEmpty.With();
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
                return ServerMessages.PackageTargetTwice.With("model", target.Model);
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
