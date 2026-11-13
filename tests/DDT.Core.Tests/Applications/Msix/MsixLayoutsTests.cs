// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Text;
using DDT.Contracts.Messages;
using DDT.Core.Applications.Msix;
using Xunit;

namespace DDT.Core.Tests.Applications.Msix;

// The same files read from the zip the server stores and from the folder the agent unpacks it into.
public sealed class MsixLayoutsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ddt-msix-").FullName;

    private string Scratch => Path.Combine(_root, "scratch");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static (string Name, byte[] Content) Entry(string name, byte[] content) => (name, content);

    private static (string Name, byte[] Content) Text(string name, string content) => (name, Encoding.UTF8.GetBytes(content));

    private MsixLayoutResult[] Read(params (string Name, byte[] Content)[] files)
    {
        using MemoryStream zip = new(TestMsix.Zip(CompressionLevel.NoCompression, files));
        string folder = Path.Combine(_root, "folder");

        foreach ((string name, byte[] content) in files)
        {
            string path = MsixFolderLayout.FullPath(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? folder);
            File.WriteAllBytes(path, content);
        }

        MsixLayoutResult fromZip = MsixZipLayout.Read(zip, Scratch);
        MsixLayoutResult fromFolder = MsixFolderLayout.Read(folder);
        Directory.Delete(folder, recursive: true);

        return [fromZip, fromFolder];
    }

    private static void AssertProblem(MsixLayoutResult[] results, MessageTemplate template, string text)
    {
        foreach (MsixLayoutResult result in results)
        {
            Assert.Null(result.Layout);
            Assert.Equal((template.Code, text), (result.Problem?.Code, result.Problem?.Text));
        }
    }

    [Fact]
    public void OnePackageIsTheMainOneWithoutDependenciesOrLicence()
    {
        foreach (MsixLayoutResult result in Read(Entry("Contoso.App.msix", TestMsix.Package("Contoso.App")), Text("readme.xml", "<readme/>")))
        {
            MsixLayout layout = Assert.IsType<MsixLayout>(result.Layout);
            Assert.Equal(("Contoso.App.msix", "Contoso.App"), (layout.Main.Path, layout.Main.Manifest.Name));
            Assert.Empty(layout.Dependencies);
            Assert.Null(layout.License);
        }
    }

    [Fact]
    public void ABundleTakesTheFrameworksAnywhereInTheFilesAndTheLicenceAtTheRoot()
    {
        MsixLayoutResult[] results = Read(
            Entry("Dependencies/x64/Microsoft.VCLibs.x64.appx", TestMsix.Package("Microsoft.VCLibs.140.00", framework: true)),
            Entry("Contoso.App.msixbundle", TestMsix.Bundle("Contoso.App")),
            Entry("Dependencies/Microsoft.UI.Xaml.appx", TestMsix.Package("Microsoft.UI.Xaml.2.8", framework: true)),
            Text("Contoso.App_License1.xml", TestMsix.License),
            Text("Dependencies/Other_License.xml", TestMsix.License));

        foreach (MsixLayoutResult result in results)
        {
            MsixLayout layout = Assert.IsType<MsixLayout>(result.Layout);
            Assert.Equal("Contoso.App.msixbundle", layout.Main.Path);
            Assert.True(layout.Main.Manifest.IsBundle);
            Assert.Equal(["Dependencies/Microsoft.UI.Xaml.appx", "Dependencies/x64/Microsoft.VCLibs.x64.appx"], layout.Dependencies.Select(d => d.Path));
            Assert.Equal("Contoso.App_License1.xml", layout.License);
        }
    }

    [Fact]
    public void TwoMainPackagesAreNamed() =>
        AssertProblem(
            Read(Entry("b/Fabrikam.Tool.appx", TestMsix.Package("Fabrikam.Tool")), Entry("a/Contoso.App.msix", TestMsix.Package("Contoso.App"))),
            ServerMessages.ApplicationMsixTwoMains,
            "The files hold more than one main package: a/Contoso.App.msix, b/Fabrikam.Tool.appx. Keep one.");

    [Fact]
    public void FilesWithoutAPackageHoldNone()
    {
        MsixLayoutResult[] results = Read(Entry("setup.exe", "MZ"u8.ToArray()));

        AssertProblem(results, ServerMessages.ApplicationMsixNone, "The files hold no MSIX or Appx package.");
        Assert.All(results, result => Assert.False(result.HasPackages));
    }

    [Fact]
    public void OnlyFrameworksAreNotEnough() =>
        AssertProblem(
            Read(Entry("Microsoft.VCLibs.appx", TestMsix.Package("Microsoft.VCLibs.140.00", framework: true))),
            ServerMessages.ApplicationMsixOnlyFrameworks,
            "The files hold only framework packages. Add the package that uses them.");

    [Fact]
    public void AnUnreadablePackageIsNamed() =>
        AssertProblem(
            Read(Entry("Contoso.App.msix", TestMsix.Package("Contoso.App")), Entry("deps/Broken.appx", "not a zip"u8.ToArray())),
            ServerMessages.ApplicationMsixUnreadable,
            "deps/Broken.appx is no MSIX or Appx package that can be read.");

    [Fact]
    public void TwoLicencesAreNamed() =>
        AssertProblem(
            Read(Entry("Contoso.App.msix", TestMsix.Package("Contoso.App")), Text("b.xml", TestMsix.License), Text("a.xml", TestMsix.License)),
            ServerMessages.ApplicationMsixTwoLicenses,
            "The files hold more than one licence: a.xml, b.xml. Keep one.");

    [Fact]
    public void MoreThanSixtyFourPackagesAreRefusedBeforeAnyIsRead()
    {
        (string, byte[])[] files = [.. Enumerable.Range(0, MsixLayouts.MaxPackages + 1).Select(index => Entry($"p{index}.appx", []))];

        AssertProblem(Read(files), ServerMessages.ApplicationMsixTooMany, "The files hold more than 64 packages.");
    }

    [Fact]
    public void ACompressedPackageIsReadFromAScratchCopyThatIsGoneAfterwards()
    {
        using MemoryStream zip = new(TestMsix.Zip(CompressionLevel.Optimal, Entry("Contoso.App.msix", TestMsix.Package("Contoso.App"))));

        MsixLayoutResult result = MsixZipLayout.Read(zip, Scratch);

        Assert.Equal("Contoso.App", result.Layout?.Main.Manifest.Name);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Scratch));
    }
}
