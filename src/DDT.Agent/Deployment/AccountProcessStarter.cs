// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using static DDT.Agent.Deployment.AccountNativeMethods;

namespace DDT.Agent.Deployment;

// Starts a tool as an account, which .NET's Process cannot: Process runs another account through CreateProcessWithLogonW,
// which LocalSystem, the account the agent runs as, may not call. This uses CreateProcessAsUserW with the account's
// already signed-in token instead. The child gets the account's profile environment plus DDT's variables, a window
// station and desktop of its own that admit the account's logon SID (without one an interactive process fails to start
// with 0xC0000142), and only the two output pipes as inherited handles. It starts suspended and joins a job that is
// killed when the job handle closes, so a timeout ends the whole process tree. Every handle is closed in the end.
[SupportedOSPlatform("windows")]
public sealed class AccountProcessStarter(AgentLog log) : IAccountProcessStarter
{
    // Setting the process's window station is process-wide, so only one start does it at a time.
    private static readonly Lock s_windowStationLock = new();

    public IToolProcess Start(
        IAccountSession account,
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        if (account is not WindowsAccountSession windows)
        {
            throw new DeploymentStepException("This account cannot start a process on this computer.");
        }

        AccountToolProcess? started = null;

        try
        {
            started = new AccountToolProcess(log);
            started.Start(windows, WindowsCommandLine.Build(fileName, arguments), workingDirectory, environment);

            return started;
        }
        catch
        {
            started?.Dispose();

            throw;
        }
    }

    // Owns the window station, desktop, pipes, environment block, and the process, thread and job handles.
    private sealed class AccountToolProcess(AgentLog log) : IToolProcess
    {
        private FileStream? _output;
        private FileStream? _error;
        private SafeKernelHandle? _process;
        private SafeKernelHandle? _thread;
        private SafeKernelHandle? _job;
        private nint _windowStation;
        private nint _desktop;
        private int _exitCode;

        public Stream StandardOutput => _output ?? throw new InvalidOperationException("The process has not started.");

        public Stream StandardError => _error ?? throw new InvalidOperationException("The process has not started.");

        public int ExitCode => _exitCode;

        public unsafe void Start(WindowsAccountSession account, string commandLine, string? workingDirectory, IReadOnlyDictionary<string, string>? environment)
        {
            string sid = account.LogonSid.Value;
            (_windowStation, _desktop, string stationName, string desktopName) = CreateStation(sid);

            SecurityAttributes inheritable = new() { Length = (uint)sizeof(SecurityAttributes), InheritHandle = 1 };
            (SafeFileHandle outRead, SafeFileHandle outWrite) = Pipe(&inheritable);
            (SafeFileHandle errRead, SafeFileHandle errWrite) = Pipe(&inheritable);
            SafeFileHandle input = NulForReading(&inheritable);

            char* environmentBlock = BuildEnvironment(account.Token, environment);
            char[] command = [.. commandLine, '\0'];
            void* attributeList = null;

            try
            {
                attributeList = HandleListAttribute(outWrite, errWrite, input);

                fixed (char* desktop = $@"{stationName}\{desktopName}")
                {
                    StartupInfoEx startup = new()
                    {
                        Size = (uint)sizeof(StartupInfoEx),
                        Flags = StartfUseStdHandles,
                        Desktop = desktop,
                        StandardInput = input.DangerousGetHandle(),
                        StandardOutput = outWrite.DangerousGetHandle(),
                        StandardError = errWrite.DangerousGetHandle(),
                        AttributeList = attributeList,
                    };

                    ProcessInformation info;

                    fixed (char* command0 = command)
                    {
                        if (!CreateProcessAsUser(
                            account.Token,
                            null,
                            command0,
                            0,
                            0,
                            inheritHandles: true,
                            CreateSuspended | CreateUnicodeEnvironment | CreateNoWindow | ExtendedStartupInfoPresent,
                            environmentBlock,
                            workingDirectory,
                            &startup,
                            out info))
                        {
                            int error = Marshal.GetLastPInvokeError();

                            throw new DeploymentStepException(
                                $"The script could not be started as {account.UserName} (error {error}): {new Win32Exception(error).Message}");
                        }

                        _process = new SafeKernelHandle(info.Process);
                        _thread = new SafeKernelHandle(info.Thread);
                    }
                }

                _job = KillOnCloseJob();
                Assign(_job, _process, account.UserName);

                // The parent's copies of the child's ends are closed, so a read ends when the child alone still holds
                // the write end.
                outWrite.Dispose();
                errWrite.Dispose();
                input.Dispose();

                _output = new FileStream(outRead, FileAccess.Read);
                _error = new FileStream(errRead, FileAccess.Read);

                if (ResumeThread(_thread) == ResumeFailed)
                {
                    int error = Marshal.GetLastPInvokeError();

                    throw new DeploymentStepException(
                        $"The script could not be resumed after it started as {account.UserName} (error {error}): {new Win32Exception(error).Message}");
                }
            }
            catch
            {
                outWrite.Dispose();
                errWrite.Dispose();
                input.Dispose();
                outRead.Dispose();
                errRead.Dispose();

                throw;
            }
            finally
            {
                if (attributeList is not null)
                {
                    DeleteProcThreadAttributeList(attributeList);
                    NativeMemory.Free(attributeList);
                }

                if (environmentBlock is not null)
                {
                    Free(environmentBlock);
                }
            }
        }

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            SafeKernelHandle process = _process ?? throw new InvalidOperationException("The process has not started.");
            using ManualResetEvent ended = new(false) { SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false) };
            TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RegisteredWaitHandle? registration = null;

            registration = ThreadPool.RegisterWaitForSingleObject(
                ended,
                (_, _) => finished.TrySetResult(),
                null,
                Timeout.Infinite,
                executeOnlyOnce: true);

            try
            {
                await finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                registration.Unregister(null);
            }

            _exitCode = GetExitCodeProcess(process, out uint code) ? unchecked((int)code) : 0;
        }

        // Closing the job kills the tree too, but this ends it at once so the wait returns without waiting for the
        // handles to close.
        public void Kill()
        {
            try
            {
                if (_job is { IsInvalid: false })
                {
                    _ = TerminateJobObject(_job, 1);
                }
                else if (_process is { IsInvalid: false })
                {
                    _ = TerminateProcess(_process, 1);
                }
            }
            catch (Exception exception)
            {
                log.Warning($"The script could not be stopped ({exception.Message}).");
            }
        }

        public void Dispose()
        {
            _output?.Dispose();
            _error?.Dispose();

            // Kills the tree, as the job limit says.
            _job?.Dispose();
            _thread?.Dispose();
            _process?.Dispose();

            if (_desktop != 0)
            {
                _ = CloseDesktop(_desktop);
            }

            if (_windowStation != 0)
            {
                _ = CloseWindowStation(_windowStation);
            }
        }

        private static unsafe (nint WindowStation, nint Desktop, string StationName, string DesktopName) CreateStation(string logonSid)
        {
            string stationName = $"ddt-{Guid.NewGuid():N}";
            const string desktopName = "default";
            nint stationDescriptor = SecurityDescriptor($"D:(A;;0x{WindowStationAllAccess:X};;;SY)(A;;0x{WindowStationAllAccess:X};;;{logonSid})");
            nint desktopDescriptor = SecurityDescriptor($"D:(A;;0x{DesktopAllAccess:X};;;SY)(A;;0x{DesktopAllAccess:X};;;{logonSid})");

            try
            {
                SecurityAttributes stationSecurity = new() { Length = (uint)sizeof(SecurityAttributes), SecurityDescriptor = stationDescriptor };
                nint station = CreateWindowStation(stationName, 0, WindowStationAllAccess, &stationSecurity);

                if (station == 0)
                {
                    int error = Marshal.GetLastPInvokeError();

                    throw new DeploymentStepException($"A window station for the script could not be created (error {error}): {new Win32Exception(error).Message}");
                }

                try
                {
                    nint desktop = DesktopOn(station, desktopName, desktopDescriptor);

                    return (station, desktop, stationName, desktopName);
                }
                catch
                {
                    _ = CloseWindowStation(station);

                    throw;
                }
            }
            finally
            {
                if (stationDescriptor != 0)
                {
                    _ = LocalFree(stationDescriptor);
                }

                if (desktopDescriptor != 0)
                {
                    _ = LocalFree(desktopDescriptor);
                }
            }
        }

        // A desktop is created on the process's own window station, so the station is swapped in around the call, which
        // is why one start does this at a time.
        private static unsafe nint DesktopOn(nint station, string desktopName, nint descriptor)
        {
            SecurityAttributes desktopSecurity = new() { Length = (uint)sizeof(SecurityAttributes), SecurityDescriptor = descriptor };

            lock (s_windowStationLock)
            {
                nint previous = GetProcessWindowStation();

                if (!SetProcessWindowStation(station))
                {
                    int error = Marshal.GetLastPInvokeError();

                    throw new DeploymentStepException($"The script's window station could not be entered (error {error}): {new Win32Exception(error).Message}");
                }

                try
                {
                    nint desktop = CreateDesktop(desktopName, 0, 0, 0, DesktopAllAccess, &desktopSecurity);

                    if (desktop == 0)
                    {
                        int error = Marshal.GetLastPInvokeError();

                        throw new DeploymentStepException($"A desktop for the script could not be created (error {error}): {new Win32Exception(error).Message}");
                    }

                    return desktop;
                }
                finally
                {
                    _ = SetProcessWindowStation(previous);
                }
            }
        }

        private static nint SecurityDescriptor(string sddl)
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, SddlRevision, out nint descriptor, out _))
            {
                int error = Marshal.GetLastPInvokeError();

                throw new DeploymentStepException($"A security descriptor for the script could not be built (error {error}): {new Win32Exception(error).Message}");
            }

            return descriptor;
        }

        private static unsafe (SafeFileHandle Read, SafeFileHandle Write) Pipe(SecurityAttributes* inheritable)
        {
            if (!CreatePipe(out SafeFileHandle read, out SafeFileHandle write, inheritable, 0))
            {
                int error = Marshal.GetLastPInvokeError();

                throw new DeploymentStepException($"An output pipe for the script could not be created (error {error}): {new Win32Exception(error).Message}");
            }

            // The parent's end of the pipe is never handed to the child.
            if (!SetHandleInformation(read, HandleFlagInherit, 0))
            {
                int error = Marshal.GetLastPInvokeError();
                read.Dispose();
                write.Dispose();

                throw new DeploymentStepException($"An output pipe for the script could not be set up (error {error}): {new Win32Exception(error).Message}");
            }

            return (read, write);
        }

        private static unsafe SafeFileHandle NulForReading(SecurityAttributes* inheritable)
        {
            SafeFileHandle handle = File.OpenHandle("NUL", FileMode.Open, FileAccess.Read);

            if (!SetHandleInformation(handle, HandleFlagInherit, HandleFlagInherit))
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();

                throw new DeploymentStepException($"The script's input could not be set up (error {error}): {new Win32Exception(error).Message}");
            }

            return handle;
        }

        // Only these three handles are inherited, whatever else the agent holds open, so no other handle leaks into the
        // account's process.
        private static unsafe void* HandleListAttribute(SafeFileHandle output, SafeFileHandle error, SafeFileHandle input)
        {
            nuint size = 0;
            _ = InitializeProcThreadAttributeList(null, 1, 0, ref size);
            void* list = NativeMemory.Alloc(size);

            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size))
            {
                int code = Marshal.GetLastPInvokeError();
                NativeMemory.Free(list);

                throw new DeploymentStepException($"The script's handles could not be prepared (error {code}): {new Win32Exception(code).Message}");
            }

            nint* handles = stackalloc nint[3];
            handles[0] = output.DangerousGetHandle();
            handles[1] = error.DangerousGetHandle();
            handles[2] = input.DangerousGetHandle();

            if (!UpdateProcThreadAttribute(list, 0, ProcThreadAttributeHandleList, handles, (nuint)(sizeof(nint) * 3), null, null))
            {
                int code = Marshal.GetLastPInvokeError();
                DeleteProcThreadAttributeList(list);
                NativeMemory.Free(list);

                throw new DeploymentStepException($"The script's handles could not be prepared (error {code}): {new Win32Exception(code).Message}");
            }

            return list;
        }

        private static unsafe SafeKernelHandle KillOnCloseJob()
        {
            SafeKernelHandle job = CreateJobObject(0, null);

            if (job.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                job.Dispose();

                throw new DeploymentStepException($"A job for the script could not be created (error {error}): {new Win32Exception(error).Message}");
            }

            JobExtendedLimits limits = new() { LimitFlags = JobObjectLimitKillOnJobClose };

            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JobExtendedLimits)))
            {
                int error = Marshal.GetLastPInvokeError();
                job.Dispose();

                throw new DeploymentStepException($"A job for the script could not be set up (error {error}): {new Win32Exception(error).Message}");
            }

            return job;
        }

        private static void Assign(SafeKernelHandle job, SafeKernelHandle process, string userName)
        {
            if (!AssignProcessToJobObject(job, process))
            {
                int error = Marshal.GetLastPInvokeError();

                throw new DeploymentStepException(
                    $"The script started as {userName} could not be put in its job (error {error}): {new Win32Exception(error).Message}");
            }
        }

        // The account's own environment, then DDT's variables over it, as a sorted double-null-terminated block.
        private static unsafe char* BuildEnvironment(SafeKernelHandle token, IReadOnlyDictionary<string, string>? added)
        {
            if (!CreateEnvironmentBlock(out char* profile, token, inherit: false))
            {
                int error = Marshal.GetLastPInvokeError();

                throw new DeploymentStepException($"The script's environment could not be built (error {error}): {new Win32Exception(error).Message}");
            }

            try
            {
                SortedDictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);
                Read(profile, variables);

                if (added is not null)
                {
                    foreach ((string name, string value) in added)
                    {
                        variables[name] = value;
                    }
                }

                return Block(variables);
            }
            finally
            {
                _ = DestroyEnvironmentBlock(profile);
            }
        }

        private static unsafe void Read(char* block, SortedDictionary<string, string> variables)
        {
            char* cursor = block;

            while (*cursor != '\0')
            {
                ReadOnlySpan<char> entry = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(cursor);
                cursor += entry.Length + 1;
                int equals = entry.IndexOf('=');

                // A name that starts with '=' is a drive's current directory, such as "=C:"; its '=' is not the split.
                if (equals > 0)
                {
                    variables[entry[..equals].ToString()] = entry[(equals + 1)..].ToString();
                }
            }
        }

        private static unsafe char* Block(SortedDictionary<string, string> variables)
        {
            int length = 1;

            foreach ((string name, string value) in variables)
            {
                length += name.Length + 1 + value.Length + 1;
            }

            char* block = (char*)NativeMemory.Alloc((nuint)length, sizeof(char));
            int index = 0;

            foreach ((string name, string value) in variables)
            {
                foreach (char character in name)
                {
                    block[index++] = character;
                }

                block[index++] = '=';

                foreach (char character in value)
                {
                    block[index++] = character;
                }

                block[index++] = '\0';
            }

            block[index] = '\0';

            return block;
        }

        private static unsafe void Free(char* block) => NativeMemory.Free(block);
    }
}
