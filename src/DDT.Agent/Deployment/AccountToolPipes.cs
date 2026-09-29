// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using static DDT.Agent.Deployment.AccountNativeMethods;

namespace DDT.Agent.Deployment;

// The two output pipes of a tool started as an account, and NUL as its input. These are the only handles it inherits.
[SupportedOSPlatform("windows")]
internal sealed unsafe class AccountToolPipes : IDisposable
{
    private readonly SafeFileHandle _outputRead;
    private readonly SafeFileHandle _outputWrite;
    private readonly SafeFileHandle _errorRead;
    private readonly SafeFileHandle _errorWrite;
    private readonly SafeFileHandle _input;
    private void* _handleList;

    private AccountToolPipes(SafeFileHandle outputRead, SafeFileHandle outputWrite, SafeFileHandle errorRead, SafeFileHandle errorWrite, SafeFileHandle input)
    {
        _outputRead = outputRead;
        _outputWrite = outputWrite;
        _errorRead = errorRead;
        _errorWrite = errorWrite;
        _input = input;
    }

    public static AccountToolPipes Create()
    {
        SecurityAttributes inheritable = new() { Length = (uint)sizeof(SecurityAttributes), InheritHandle = 1 };
        (SafeFileHandle outputRead, SafeFileHandle outputWrite) = Pipe(&inheritable);
        (SafeFileHandle errorRead, SafeFileHandle errorWrite) = Pipe(&inheritable);
        SafeFileHandle input = NulForReading();

        return new AccountToolPipes(outputRead, outputWrite, errorRead, errorWrite, input);
    }

    // Sets up the child's ends, and the attribute list that lets it inherit only those, whatever else the agent holds
    // open.
    public StartupInfoEx StartupInfo(char* desktop)
    {
        _handleList = HandleList(_outputWrite, _errorWrite, _input);

        return new StartupInfoEx
        {
            Size = (uint)sizeof(StartupInfoEx),
            Flags = StartfUseStdHandles,
            Desktop = desktop,
            StandardInput = _input.DangerousGetHandle(),
            StandardOutput = _outputWrite.DangerousGetHandle(),
            StandardError = _errorWrite.DangerousGetHandle(),
            AttributeList = _handleList,
        };
    }

    public void FreeHandleList()
    {
        if (_handleList is not null)
        {
            DeleteProcThreadAttributeList(_handleList);
            NativeMemory.Free(_handleList);
            _handleList = null;
        }
    }

    // Closes the parent's copies of the child's ends. Then only the child holds the write ends, and a read ends once
    // the child closes them.
    public (FileStream Output, FileStream Error) TakeReadEnds()
    {
        _outputWrite.Dispose();
        _errorWrite.Dispose();
        _input.Dispose();

        return (new FileStream(_outputRead, FileAccess.Read), new FileStream(_errorRead, FileAccess.Read));
    }

    // For a start that failed. After TakeReadEnds the streams own the read ends.
    public void Dispose()
    {
        _outputWrite.Dispose();
        _errorWrite.Dispose();
        _input.Dispose();
        _outputRead.Dispose();
        _errorRead.Dispose();
    }

    private static (SafeFileHandle Read, SafeFileHandle Write) Pipe(SecurityAttributes* inheritable)
    {
        if (!CreatePipe(out SafeFileHandle read, out SafeFileHandle write, inheritable, 0))
        {
            throw DeploymentStepException.ForLastWin32Error("An output pipe for the script could not be created");
        }

        // The parent's end of the pipe is never handed to the child.
        if (!SetHandleInformation(read, HandleFlagInherit, 0))
        {
            DeploymentStepException failure = DeploymentStepException.ForLastWin32Error("An output pipe for the script could not be set up");
            read.Dispose();
            write.Dispose();

            throw failure;
        }

        return (read, write);
    }

    private static SafeFileHandle NulForReading()
    {
        SafeFileHandle handle = File.OpenHandle("NUL", FileMode.Open, FileAccess.Read);

        if (!SetHandleInformation(handle, HandleFlagInherit, HandleFlagInherit))
        {
            DeploymentStepException failure = DeploymentStepException.ForLastWin32Error("The script's input could not be set up");
            handle.Dispose();

            throw failure;
        }

        return handle;
    }

    private static void* HandleList(SafeFileHandle output, SafeFileHandle error, SafeFileHandle input)
    {
        nuint size = 0;
        _ = InitializeProcThreadAttributeList(null, 1, 0, ref size);
        void* list = NativeMemory.Alloc(size);

        if (!InitializeProcThreadAttributeList(list, 1, 0, ref size))
        {
            DeploymentStepException failure = DeploymentStepException.ForLastWin32Error("The script's handles could not be prepared");
            NativeMemory.Free(list);

            throw failure;
        }

        nint* handles = stackalloc nint[3];
        handles[0] = output.DangerousGetHandle();
        handles[1] = error.DangerousGetHandle();
        handles[2] = input.DangerousGetHandle();

        if (!UpdateProcThreadAttribute(list, 0, ProcThreadAttributeHandleList, handles, (nuint)(sizeof(nint) * 3), null, null))
        {
            DeploymentStepException failure = DeploymentStepException.ForLastWin32Error("The script's handles could not be prepared");
            DeleteProcThreadAttributeList(list);
            NativeMemory.Free(list);

            throw failure;
        }

        return list;
    }
}
