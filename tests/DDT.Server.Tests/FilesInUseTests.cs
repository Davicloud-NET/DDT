// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Configuration;
using DDT.Server.Machines;
using Microsoft.Extensions.Options;
using Xunit;

namespace DDT.Server.Tests;

// Linux renames over a file that's being read. Windows refuses, unless every reader lets the file be deleted: then the
// old file steps aside and goes when its last reader closes it. These hold a file open the way a download does.
public sealed class FilesInUseTests : IDisposable
{
    private readonly string _store = Directory.CreateTempSubdirectory("ddt-in-use-").FullName;

    [Fact]
    public async Task AnUploadReplacesAnAgentThatAMachineIsDownloading()
    {
        AgentReleaseStore store = Store();
        byte[] old = Executables.Versioned("0.3.0");
        byte[] replacement = Executables.Versioned("0.4.0");
        await SaveAsync(store, old);

        byte[] downloaded;

        await using (FileStream download = Download(store.BinaryPath))
        {
            Assert.Equal(ReleaseUploadStatus.Saved, await SaveAsync(store, replacement));

            // The machine gets the whole file it started on, and the next one gets the new agent.
            downloaded = new byte[old.Length];
            await download.ReadExactlyAsync(downloaded, TestContext.Current.CancellationToken);
            Assert.Equal(replacement, await File.ReadAllBytesAsync(store.BinaryPath, TestContext.Current.CancellationToken));
        }

        Assert.Equal(old, downloaded);

        // What stepped aside is gone by the next upload at the latest.
        await SaveAsync(store, old);
        Assert.Equal(["ddt-agent.exe"], Directory.GetFiles(Path.GetDirectoryName(store.BinaryPath)!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task AnUploadIsRemovedWhileAMachineDownloadsItAndANewOneTakesItsPlace()
    {
        AgentReleaseStore store = Store();
        byte[] agent = Executables.Versioned("0.3.0");
        await SaveAsync(store, agent);

        await using (FileStream download = Download(store.BinaryPath))
        {
            Assert.True(store.RemoveUpload());
            Assert.False(File.Exists(store.BinaryPath));
            Assert.Null(await store.OfferedAsync(TestContext.Current.CancellationToken));

            // The name is free at once, not only when the download ends.
            Assert.Equal(ReleaseUploadStatus.Saved, await SaveAsync(store, agent));
        }

        Assert.True(store.RemoveUpload());
        Assert.False(store.RemoveUpload());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.BinaryPath)!));
    }

    public void Dispose() => Directory.Delete(_store, recursive: true);

    private AgentReleaseStore Store() =>
        new(Options.Create(new AgentReleaseOptions()), Options.Create(new DdtOptions { StorePath = _store }), new BundledReleases(Path.Combine(_store, "none")));

    private static async Task<ReleaseUploadStatus> SaveAsync(AgentReleaseStore store, byte[] agent)
    {
        using MemoryStream content = new(agent);

        return (await store.SaveAsync(content, TestContext.Current.CancellationToken)).Status;
    }

    // As the server opens a file it sends
    private static FileStream Download(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
