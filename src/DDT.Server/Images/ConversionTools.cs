// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Text;

namespace DDT.Server.Images;

// The programs that turn a disk image DDT cannot read itself into a raw disk: qemu-img for qcow2 and xz for .xz. They
// are found on PATH, and on Windows also where QEMU's installer puts qemu-img. The container image installs both.
// find stands in for the search in tests.
public sealed class ConversionTools(Func<string, string?> find)
{
    public const string QemuImg = "qemu-img";
    public const string Xz = "xz";

    // A tool that writes more than this to its error output has said all that helps.
    private const int MaxErrorCharacters = 4096;

    public ConversionTools()
        : this(FindOnPath)
    {
    }

    // The full path of tool, or null when it is not installed.
    public string? Find(string tool) => find(tool);

    // Runs the tool and waits for it. With standardOutput, what it writes to its output goes into that file. Throws
    // ConversionFailedException with the tool's last error line when it fails.
    public async Task RunAsync(string path, IReadOnlyList<string> arguments, string? standardOutput, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessStartInfo start = new(path)
        {
            RedirectStandardOutput = standardOutput is not null,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new ConversionFailedException($"{Path.GetFileName(path)} could not be started.");
        process.StandardInput.Close();
        StringBuilder errors = new();
        Task reading = ReadErrorsAsync(process.StandardError, errors);

        try
        {
            if (standardOutput is not null)
            {
                await using FileStream output = new(standardOutput, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
                await process.StandardOutput.BaseStream.CopyToAsync(output, 1024 * 1024, cancellationToken).ConfigureAwait(false);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await reading.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            throw;
        }

        if (process.ExitCode != 0)
        {
            string last = errors.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
                ?? $"exit code {process.ExitCode}";

            throw new ConversionFailedException($"{Path.GetFileName(path)} failed: {last}");
        }
    }

    private static async Task ReadErrorsAsync(StreamReader error, StringBuilder errors)
    {
        char[] buffer = new char[1024];
        int read;

        while ((read = await error.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (errors.Length < MaxErrorCharacters)
            {
                errors.Append(buffer, 0, Math.Min(read, MaxErrorCharacters - errors.Length));
            }
        }
    }

    private static string? FindOnPath(string tool)
    {
        string name = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        IEnumerable<string> folders = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (OperatingSystem.IsWindows())
        {
            folders = folders.Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "qemu"));
        }

        return folders.Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
    }
}
