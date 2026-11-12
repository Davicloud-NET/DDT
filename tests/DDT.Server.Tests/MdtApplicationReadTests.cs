// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Import;
using DDT.Server.Import;
using Xunit;
using static DDT.Server.Tests.MdtApplicationShares;

namespace DDT.Server.Tests;

// An MDT share's Applications.xml as DDT reads it: applications, bundles, and the values people write by hand.
public sealed class MdtApplicationReadTests : IDisposable
{
    private readonly string _share = Write(Path.Combine(Path.GetTempPath(), "ddt-mdt-applications-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void ReadsEachEntryWithAGuidOnceInTheFilesOrder()
    {
        IReadOnlyList<MdtShareApplication> read = MdtApplications.Read(_share);

        Assert.Equal(
            ["7-Zip 24.09", "VC++ Runtime", "Notepad++, portable", "Base Bundle", "Inner Bundle", "Cycle One", "Cycle Two", "Empty Bundle", "No Command", "Viewer", "Uses Viewer", "Contoso App", "Wallpaper"],
            read.Select(entry => entry.Name));
        Assert.Equal(
            [BaseBundle, InnerBundle, EmptyBundle],
            read.Where(entry => entry.IsBundle).Select(entry => entry.Id));
    }

    [Fact]
    public void ReadsTheValuesOfAnApplication()
    {
        MdtShareApplication sevenZip = Read(SevenZip);

        Assert.Equal(
            ("24.09", "Igor Pavlov", "Packs files.", "msiexec /i 7z.msi /qn", @".\Applications\7-Zip 24.09", ProductCode, false, true),
            (sevenZip.Version, sevenZip.Publisher, sevenZip.Comments, sevenZip.CommandLine, sevenZip.Source, sevenZip.UninstallKey, sevenZip.Reboot, sevenZip.HasFiles));
    }

    [Fact]
    public void TakesGuidsWithOrWithoutBracesAndTrueInAnyCase()
    {
        MdtShareApplication runtime = Read(Runtime);

        Assert.True(runtime.Reboot);
        Assert.False(runtime.HasFiles);
        Assert.Equal([Runtime, BaseBundle, Missing], Read(Notepad).Dependencies);
    }

    [Fact]
    public void ShowsWhatTheLibraryHasAlready()
    {
        HashSet<string> library = ["VIEWER", "BASE BUNDLE"];

        List<MdtApplication> views = [.. MdtApplications.Read(_share).Select(entry => MdtApplications.View(entry, library))];

        Assert.Equal(["Base Bundle", "Viewer"], views.Where(view => view.InLibrary).Select(view => view.Name));
        Assert.Equal(ApplicationReferenceKind.Bundle, Assert.Single(views, view => view.Id == BaseBundle).Kind);
        Assert.Equal(ApplicationReferenceKind.Application, Assert.Single(views, view => view.Id == NoCommand).Kind);
    }

    [Fact]
    public void ShortensANameLongerThanTheLibraryTakes()
    {
        string control = Path.Combine(_share, "Control", "Applications.xml");
        File.WriteAllText(control, $"<applications><application guid=\"{SevenZip}\"><Name>{new string('n', 150)}</Name></application></applications>");

        Assert.Equal(ApplicationLimits.MaxNameLength, Assert.Single(MdtApplications.Read(_share)).Name.Length);
    }

    [Fact]
    public void ABrokenListHoldsNothing()
    {
        File.WriteAllText(Path.Combine(_share, "Control", "Applications.xml"), "<applications><application");

        Assert.Empty(MdtApplications.Read(_share));
    }

    [Fact]
    public void ABundleStandsForItsApplicationsAndACircleOfBundlesEnds()
    {
        Dictionary<string, MdtShareApplication> entries = MdtApplications.Read(_share).ToDictionary(entry => entry.Id);

        Assert.Equal([Runtime, SevenZip, Missing], MdtApplicationGraph.Flatten(entries[Notepad], entries));
        Assert.Equal([SevenZip, Runtime], MdtApplicationGraph.Flatten(entries[BaseBundle], entries));
        Assert.Equal([Runtime, SevenZip], MdtApplicationGraph.Flatten(entries[InnerBundle], entries));
    }

    [Fact]
    public void DependenciesComeFirstAndACircleIsCut()
    {
        Dictionary<string, MdtShareApplication> entries = MdtApplications.Read(_share).ToDictionary(entry => entry.Id);
        List<MdtShareApplication> applications = [.. entries.Values.Where(entry => !entry.IsBundle)];
        Dictionary<string, IReadOnlyList<string>> dependencies = applications.ToDictionary(entry => entry.Id, entry => MdtApplicationGraph.Flatten(entry, entries));

        MdtApplicationOrder order = MdtApplicationGraph.Order([entries[Notepad], .. applications.Where(entry => entry.Id != Notepad)], dependencies);

        Assert.Equal(
            ["VC++ Runtime", "7-Zip 24.09", "Notepad++, portable", "Cycle Two", "Cycle One", "No Command", "Viewer", "Uses Viewer", "Contoso App", "Wallpaper"],
            order.Applications.Select(entry => entry.Name));
        Assert.Equal([(CycleTwo, CycleOne)], order.Cut);
    }

    public void Dispose() => Directory.Delete(_share, recursive: true);

    private MdtShareApplication Read(string id) => Assert.Single(MdtApplications.Read(_share), entry => entry.Id == id);
}
