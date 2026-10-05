// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Host.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DDT.Server.Tests;

// The log file a Windows service writes, since it has no console. Disposing the provider writes what's queued.
public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ddt-logs-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _clock = new();

    [Fact]
    public void AnEntryNamesItsTimeLevelCategoryAndEventAndCarriesTheException()
    {
        FileLoggerProvider provider = new(_folder, _clock);
        ILogger logger = provider.CreateLogger("DDT.Pxe.PxeHost");
        logger.Log(LogLevel.Warning, new EventId(500), "Answering PXE on Ethernet", null, (state, _) => state);
        logger.Log(LogLevel.Error, new EventId(7), "It broke", new InvalidOperationException("the reason"), (state, _) => state);
        Close(provider);

        string[] lines = File.ReadAllLines(Assert.Single(Files()));
        string time = _clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);

        Assert.Equal($"{time} warn DDT.Pxe.PxeHost[500] Answering PXE on Ethernet", lines[0]);
        Assert.Equal($"{time} fail DDT.Pxe.PxeHost[7] It broke", lines[1]);
        Assert.Equal("System.InvalidOperationException: the reason", lines[2]);
    }

    [Fact]
    public void EachDayHasItsOwnFile()
    {
        string first = Day();

        FileLoggerProvider provider = new(_folder, _clock);
        ILogger logger = provider.CreateLogger("DDT");
        logger.LogInformation("today");
        _clock.Advance(TimeSpan.FromDays(1));
        logger.LogInformation("tomorrow");
        Close(provider);

        Assert.Equal([$"ddt-{first}.log", $"ddt-{Day()}.log"], Names());
        Assert.Contains("today", File.ReadAllText(Path.Combine(_folder, $"ddt-{first}.log")), StringComparison.Ordinal);
        Assert.Contains("tomorrow", File.ReadAllText(Path.Combine(_folder, $"ddt-{Day()}.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void AFullFileIsFollowedByAnotherOfTheSameDay()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, $"ddt-{Day()}.log"), new byte[100]);

        FileLoggerProvider provider = new(_folder, _clock, maxFileBytes: 100);
        provider.CreateLogger("DDT").LogInformation("over the limit");
        Close(provider);

        Assert.Equal([$"ddt-{Day()}-1.log", $"ddt-{Day()}.log"], Names());
        Assert.Contains("over the limit", File.ReadAllText(Path.Combine(_folder, $"ddt-{Day()}-1.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheNewestFilesAreKept()
    {
        Directory.CreateDirectory(_folder);
        DateTime written = DateTime.UtcNow.AddDays(-100);

        for (int day = 1; day <= FileLoggerProvider.MaxFiles + 5; day++)
        {
            string old = Path.Combine(_folder, string.Create(CultureInfo.InvariantCulture, $"ddt-200001{day:00}.log"));
            File.WriteAllText(old, "old");
            File.SetLastWriteTimeUtc(old, written.AddDays(day));
        }

        FileLoggerProvider provider = new(_folder, _clock);
        provider.CreateLogger("DDT").LogInformation("new");
        Close(provider);

        string[] names = Names();
        Assert.Equal(FileLoggerProvider.MaxFiles, names.Length);
        Assert.Contains($"ddt-{Day()}.log", names);
        Assert.DoesNotContain("ddt-20000106.log", names);
        Assert.Contains("ddt-20000107.log", names);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // Dispose gives the writer five seconds, which a busy computer can use up. The files are read once it is done.
    private static void Close(FileLoggerProvider provider)
    {
        provider.Dispose();
        Assert.True(provider.Completion.Wait(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken), "The log's writer did not finish.");
    }

    private string Day() => _clock.GetLocalNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private string[] Files() => Directory.GetFiles(_folder);

    private string[] Names() => [.. Files().Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
}
