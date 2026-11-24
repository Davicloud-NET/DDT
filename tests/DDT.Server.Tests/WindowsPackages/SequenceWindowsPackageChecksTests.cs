// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Server.Images;
using DDT.Server.Packages;
using DDT.Server.Sequences;
using Xunit;

namespace DDT.Server.Tests.WindowsPackages;

// A Service packages step names Windows packages that are in the library.
public sealed class SequenceWindowsPackageChecksTests
{
    [Fact]
    public void AGoneOrOtherPackageIsAProblemAtItsPlaceInTheList()
    {
        Package windows = Package("Update", PackageKind.WindowsPackage);
        Package files = Package("Scripts", PackageKind.Files);
        ServicePackagesStep step = new() { Id = Guid.NewGuid(), Name = "Service packages", Packages = [windows.Id, Guid.NewGuid(), files.Id] };
        SequenceReferences references = new(new Dictionary<Guid, Image>(), new[] { windows, files }.ToDictionary(package => package.Id), false, false);
        List<SequenceProblem> problems = [];

        SequenceWindowsPackageChecks.Check(new SequenceDefinition(SequenceDefinition.CurrentVersion, [step]), references, problems);

        Assert.Equal(
            [(step.Id, "packages[1]", "sequence.servicingPackageGone"), (step.Id, "packages[2]", "sequence.servicingNotWindows")],
            problems.Select(problem => (problem.StepId, problem.Field, problem.Code)));
    }

    private static Package Package(string name, PackageKind kind) => new() { Id = Guid.NewGuid(), Name = name, Kind = kind, Sha256 = new string('c', 64) };
}
