// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;

namespace DDT.Agent.Consoles;

// The socket the console's user connects to. Its folder lets through whoever knows a name but lists none. What counts
// is who connected, which ConsolePeer asks the kernel.
internal static class LinuxSessionPipe
{
    private const UnixFileMode Passable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private const UnixFileMode Connectable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead
        | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite;

    public static NamedPipeServerStream Create(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The console's socket is Linux's.");
        }

        string folder = Path.GetDirectoryName(path) ?? throw new ArgumentException("The socket needs a folder.", nameof(path));
        Directory.CreateDirectory(folder);
        File.SetUnixFileMode(folder, Passable);

        NamedPipeServerStream pipe = new(
            path,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance);

        try
        {
            File.SetUnixFileMode(path, Connectable);

            return pipe;
        }
        catch
        {
            pipe.Dispose();

            throw;
        }
    }

    // Once the agent ends. Only its own sockets go, so the folder goes when nothing else is in it.
    public static void Remove(string path)
    {
        try
        {
            File.Delete(path);

            if (Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
