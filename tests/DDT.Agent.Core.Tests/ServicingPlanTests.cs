// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using DDT.Agent.Servicing;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ServicingPlanTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ddt-servicing-").FullName;

    [Fact]
    public void SplitsTheCabinetsSoNoCommandLineGetsTooLong()
    {
        string[] names =
        [
            .. Enumerable.Range(0, 80).Select(index =>
                string.Create(CultureInfo.InvariantCulture, $"Microsoft-Windows-Feature{index:D2}-FoD-Package~31bf3856ad364e35~amd64~~.cab")),
        ];
        UnpackedPackage package = Unpacked("Features", names);

        IReadOnlyList<ServicingCall> calls = ServicingPlan.Of([package]);

        Assert.True(calls.Count > 1);
        Assert.All(calls, call => Assert.True(call.Files.Sum(file => " /PackagePath:".Length + file.Length) <= ServicingPlan.MaxPackagePathCharacters));
        Assert.Equal(names, calls.SelectMany(call => call.Files).Select(Path.GetFileName));
        Assert.All(calls, call => Assert.False(call.IsUpdate));
    }

    [Fact]
    public void AnUpdateIsAddedByItsNewestMsuAlone()
    {
        UnpackedPackage package = Unpacked("Update", "windows11.0-kb5132061-x64.msu", "windows11.0-kb5043080-x64.msu", "readme.txt");

        ServicingCall call = Assert.Single(ServicingPlan.Of([package]));

        Assert.True(call.IsUpdate);
        Assert.Equal(["windows11.0-kb5132061-x64.msu"], call.Files.Select(Path.GetFileName));
    }

    [Fact]
    public async Task TakesTheProgressFromDismsBarAndKeepsItOutOfTheLog()
    {
        RecordingToolRunner tools = new();
        RecordingProgress percent = new();
        DismOfflineServicing dism = new(tools, "dism.exe", new AgentLog(new ImmediateTimeProvider(), TextWriter.Null));

        await dism.AddAsync(new OfflineImage(@"W:\", @"W:\DDT\scratch", @"W:\DDT\dism-1.log"), [@"W:\a.cab"], percent, TestContext.Current.CancellationToken);
        Func<string, string?> lines = Assert.Single(tools.Options).Lines ?? throw new InvalidOperationException("No line filter.");

        Assert.Null(lines("[=========================  45.3%                          ]"));
        Assert.Equal("Processing 1 of 1 - Adding package", lines("Processing 1 of 1 - Adding package"));
        Assert.Equal([45], percent.Values);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private UnpackedPackage Unpacked(string name, params string[] files)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

        foreach (string file in files)
        {
            File.WriteAllText(Path.Combine(folder, file), file);
        }

        return new UnpackedPackage(new AgentRunPackage(Guid.Empty, name, new string('0', 64), 1), folder);
    }
}
