// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Server.Images;
using DDT.Server.Packages;
using DDT.Server.Sequences;

namespace DDT.Server.Deployments;

// The Windows packages a Service packages step adds: its list, then with MatchingUpdates every update marked for the
// build of an image the sequence applies. They're frozen with the run, so deletion protection and the disk space check
// cover them. Any branch's image counts, since any may run; DISM leaves out a matched update that doesn't fit.
public static class RunWindowsPackages
{
    public static IEnumerable<DeploymentArtifact> Artifacts(
        Guid runId,
        ServicePackagesStep step,
        SequenceDefinition definition,
        SequenceReferences references)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(references);

        List<Package> listed =
        [
            .. (step.Packages ?? [])
                .Select(id => references.Packages.GetValueOrDefault(id))
                .OfType<Package>()
                .Where(package => package.Kind == PackageKind.WindowsPackage)
                .DistinctBy(package => package.Id),
        ];
        HashSet<Guid> named = [.. listed.Select(package => package.Id)];
        IEnumerable<Package> matched = step.MatchingUpdates ? Matching(definition, references).Where(package => !named.Contains(package.Id)) : [];

        return [.. listed.Concat(matched).Select(package => Artifact(runId, step.Id, package))];
    }

    // Updates marked for the build of an image the sequence applies, for its architecture or none named, lowest KB first.
    public static IReadOnlyList<Package> Matching(SequenceDefinition definition, SequenceReferences references)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(references);

        Image[] images =
        [
            .. SequenceTree.Nodes(definition)
                .OfType<ApplyImageStep>()
                .Select(apply => references.Images.GetValueOrDefault(apply.ImageId))
                .OfType<Image>()
                .Where(image => image.Kind == ImageKind.Wim),
        ];

        return
        [
            .. references.Packages.Values
                .Where(package => package is { Kind: PackageKind.WindowsPackage, Content: WindowsPackageContent.Update }
                    && images.Any(image => Fits(package, image)))
                .OrderBy(HighestKb)
                .ThenBy(package => package.Id),
        ];
    }

    public static int HighestKb(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return WindowsPackageColumns.Numbers(package.KbNumbers).DefaultIfEmpty(0).Max();
    }

    private static bool Fits(Package package, Image image) =>
        WindowsBuilds.Of(image.Version) is { } build
        && WindowsPackageColumns.Numbers(package.Builds).Contains(build)
        && (package.Architecture is null || image.Architecture is null || string.Equals(package.Architecture, image.Architecture, StringComparison.OrdinalIgnoreCase));

    private static DeploymentArtifact Artifact(Guid runId, Guid stepId, Package package) => new()
    {
        DeploymentId = runId,
        StepId = stepId,
        Kind = ArtifactKind.WindowsPackage,
        SourceId = package.Id,
        Name = package.Name,
        Sha256 = package.Sha256,
        SizeBytes = package.SizeBytes,
        ExpandedBytes = package.ExpandedBytes,
    };
}
