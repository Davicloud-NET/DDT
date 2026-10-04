// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DDT.Host.Helper;

// Runs a program without a window and hands on what it writes, line by line, as it writes it.
public static class ScriptProcess
{
    public static async Task<int> RunAsync(ProcessStartInfo start, Action<string> line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(line);

        // Windows PowerShell writes in the system's OEM code page when its output is redirected
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = oem;
        start.StandardErrorEncoding = oem;

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{start.FileName} didn't start.");
        Task output = RelayAsync(process.StandardOutput, line);
        Task errors = RelayAsync(process.StandardError, line);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(output, errors).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            throw;
        }

        return process.ExitCode;
    }

    private static async Task RelayAsync(StreamReader reader, Action<string> line)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } text)
        {
            line(text);
        }
    }
}
