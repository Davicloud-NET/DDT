// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DDT.MachineConsole.Machine;

// The command prompt a technician opens with Shift+F10 while the console runs, as in Windows Setup.
public interface ICommandPrompt
{
    void Open();
}

// cmd.exe in a console window of its own, in front of the console, in the folder the console and the agent are in. The
// console fills the screen but is not topmost, so the prompt's window covers it, and when the prompt is closed the
// console is in front again. A development computer opens cmd.exe just the same.
public sealed partial class CommandPrompt : ICommandPrompt
{
    private const int AnyProcess = -1;
    private const uint CreateNewConsole = 0x00000010;
    private const uint UseShowWindow = 0x00000001;
    private const short ShowNormal = 1;
    private static readonly TimeSpan s_waitForWindow = TimeSpan.FromSeconds(3);

    public void Open()
    {
        string cmd = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comSpec && File.Exists(comSpec)
            ? comSpec
            : Path.Combine(Environment.SystemDirectory, "cmd.exe");

        // The console is the foreground window while the key is pressed, which lets it hand the foreground on to
        // whatever shows the prompt's window.
        AllowSetForegroundWindow(AnyProcess);

        StartupInfo startup = new()
        {
            Size = Marshal.SizeOf<StartupInfo>(),
            Flags = UseShowWindow,
            ShowWindow = ShowNormal,
        };

        // A console of its own, and none of the console's handles: the prompt reads and writes its own window, not
        // the agent's text console behind it.
        if (!CreateProcess(cmd, 0, 0, 0, false, CreateNewConsole, 0, AppContext.BaseDirectory, in startup, out ProcessInformation started))
        {
            return;
        }

        CloseHandle(started.Thread);
        CloseHandle(started.Process);

        int processId = started.ProcessId;
        _ = Task.Run(() => BringToFrontAsync(processId));
    }

    // Windows normally puts the new window in front by itself; this makes sure of it, while the console still may.
    private static async Task BringToFrontAsync(int processId)
    {
        long start = Stopwatch.GetTimestamp();

        while (Stopwatch.GetElapsedTime(start) < s_waitForWindow)
        {
            if (WindowOf(processId) is { } window)
            {
                if (GetForegroundWindow() != window)
                {
                    SetForegroundWindow(window);
                }

                return;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    // The console window of that process: conhost draws it, but Windows names the process in it as its owner.
    private static unsafe nint? WindowOf(int processId)
    {
        Search search = new() { ProcessId = processId };
        EnumWindows(&Visit, (nint)(&search));

        return search.Window == 0 ? null : search.Window;
    }

    [UnmanagedCallersOnly]
    private static unsafe int Visit(nint window, nint state)
    {
        Search* search = (Search*)state;
        GetWindowThreadProcessId(window, out int owner);

        if (owner != search->ProcessId || !IsWindowVisible(window))
        {
            return 1;
        }

        search->Window = window;

        return 0;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcess(
        string applicationName,
        nint commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string currentDirectory,
        in StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    [LibraryImport("user32.dll")]
    private static unsafe partial int EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint state);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out int processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    private struct Search
    {
        public int ProcessId;
        public nint Window;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public uint Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }
}
