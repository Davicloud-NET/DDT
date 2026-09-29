// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Core.Wim;

namespace DDT.Agent.Deployment;

// Applies with wimlib in strict mode. An image whose ACLs or links can't be written exactly fails, instead of booting
// into a subtly broken Windows. wimlib only prints its warnings, so its error file is copied into the log.
public sealed class WimImageApplier(AgentLog log, string libraryDirectory, string errorLogPath) : IImageApplier
{
    // A damaged image can warn about every file. The rest stays in the file on the machine.
    private const int MaxForwardedLines = 200;

    private const char ByteOrderMark = (char)0xFEFF;

    private WimLibrary? _library;
    private long _errorLogPosition;

    public void Prepare()
    {
        if (_library is not null)
        {
            return;
        }

        try
        {
            WimLibraryInUse library = WimLibraryFile.EnsureExtracted(libraryDirectory);

            if (!library.IsCarriedCopy)
            {
                log.Warning(
                    $"Using the {WimLibraryFile.FileName} at {library.Path}, SHA-256 {library.Sha256}, instead of the agent's own copy, " +
                    $"SHA-256 {library.CarriedSha256}. Delete that file to make the agent use its own copy.");
            }

            _errorLogPosition = ErrorLogLength();
            _library = new WimLibrary(strict: true, errorLogPath);
        }
        catch (Exception exception) when (exception is WimLibraryException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new DeploymentStepException(
                $"wimlib cannot be used, so no image can be applied. The agent has to run as an administrator in Windows PE. {exception.Message}",
                exception);
        }
        finally
        {
            ForwardErrorLog();
        }
    }

    public async Task ApplyAsync(string wimPath, int index, string targetRoot, IProgress<int> percent, CancellationToken cancellationToken)
    {
        Prepare();

        try
        {
            await _library!.ApplyAsync(wimPath, index, targetRoot, new WimPercentProgress(percent), cancellationToken).ConfigureAwait(false);
        }
        catch (WimLibraryException exception)
        {
            throw new DeploymentStepException(exception.Message, exception);
        }
        finally
        {
            ForwardErrorLog();
        }
    }

    private long ErrorLogLength()
    {
        FileInfo file = new(errorLogPath);

        return file.Exists ? file.Length : 0;
    }

    // wimlib keeps the file open and writes UTF-16 LE to it.
    private void ForwardErrorLog()
    {
        string text;

        try
        {
            if (!File.Exists(errorLogPath))
            {
                return;
            }

            using FileStream file = new(errorLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (file.Length <= _errorLogPosition)
            {
                return;
            }

            byte[] bytes = new byte[(file.Length - _errorLogPosition) & ~1L];
            file.Position = _errorLogPosition;
            file.ReadExactly(bytes);
            _errorLogPosition += bytes.Length;
            text = Encoding.Unicode.GetString(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"The wimlib error file {errorLogPath} cannot be read ({exception.Message}).");

            return;
        }

        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Trim(ByteOrderMark))
            .Where(line => line.Length > 0)
            .ToArray();

        foreach (string line in lines.Take(MaxForwardedLines))
        {
            log.Warning($"wimlib: {line}");
        }

        if (lines.Length > MaxForwardedLines)
        {
            log.Warning($"wimlib wrote {lines.Length - MaxForwardedLines} more lines, which are in {errorLogPath} on this machine.");
        }
    }
}
