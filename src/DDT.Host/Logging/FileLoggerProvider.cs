// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace DDT.Host.Logging;

// A rolling log file for a Windows service, which has no console: one file a day, a new one when it grows large, and
// only the newest are kept. Entries are queued and written in the background, so logging never waits for the disk,
// and under a flood the oldest queued ones are dropped. The Log levels settings apply to it like to any provider.
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const long MaxFileBytes = 20L * 1024 * 1024;
    public const int MaxFiles = 20;

    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Channel<(DateTimeOffset Written, string Text)> _entries =
        Channel.CreateBounded<(DateTimeOffset, string)>(new BoundedChannelOptions(8192) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private readonly string _folder;
    private readonly TimeProvider _timeProvider;
    private readonly long _maxFileBytes;
    private readonly Task _writer;

    public FileLoggerProvider(string folder, TimeProvider timeProvider, long maxFileBytes = MaxFileBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _folder = folder;
        _timeProvider = timeProvider;
        _maxFileBytes = maxFileBytes;
        _writer = Task.Run(WriteAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    // Writes what's queued, for up to five seconds
    public void Dispose()
    {
        _entries.Writer.TryComplete();
        _writer.Wait(TimeSpan.FromSeconds(5));
    }

    // Ends once everything queued is written, however long Dispose waited for it
    public Task Completion => _writer;

    internal DateTimeOffset Now => _timeProvider.GetLocalNow();

    internal void Write(DateTimeOffset written, string text) => _entries.Writer.TryWrite((written, text));

    private async Task WriteAsync()
    {
        ChannelReader<(DateTimeOffset Written, string Text)> reader = _entries.Reader;

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            try
            {
                await WriteQueuedAsync(reader).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The disk is full or the folder is out of reach. That entry is lost; the event log still gets warnings.
            }
        }
    }

    // One open file for the entries of one day that are queued now
    private async Task WriteQueuedAsync(ChannelReader<(DateTimeOffset Written, string Text)> reader)
    {
        if (!reader.TryRead(out (DateTimeOffset Written, string Text) entry))
        {
            return;
        }

        string day = Day(entry.Written);
        FileStream file = new(FileFor(day), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        await using (file.ConfigureAwait(false))
        {
            StreamWriter writer = new(file, s_utf8);

            await using (writer.ConfigureAwait(false))
            {
                await writer.WriteAsync(entry.Text).ConfigureAwait(false);

                while (reader.TryPeek(out (DateTimeOffset Written, string Text) next) && Day(next.Written) == day && reader.TryRead(out entry))
                {
                    await writer.WriteAsync(entry.Text).ConfigureAwait(false);
                }
            }
        }
    }

    // ddt-20261001.log, then ddt-20261001-1.log once that is full
    private string FileFor(string day)
    {
        Directory.CreateDirectory(_folder);

        for (int part = 0; ; part++)
        {
            FileInfo file = new(Path.Combine(_folder, part == 0 ? $"ddt-{day}.log" : $"ddt-{day}-{part}.log"));

            if (!file.Exists)
            {
                Prune();

                return file.FullName;
            }

            if (file.Length < _maxFileBytes)
            {
                return file.FullName;
            }
        }
    }

    // Before a new file: with it, MaxFiles are left
    private void Prune()
    {
        foreach (FileInfo old in new DirectoryInfo(_folder).EnumerateFiles("ddt-*.log").OrderByDescending(file => file.LastWriteTimeUtc).Skip(MaxFiles - 1))
        {
            try
            {
                old.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Being downloaded. The next new file tries again.
            }
        }
    }

    private static string Day(DateTimeOffset written) => written.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
}
