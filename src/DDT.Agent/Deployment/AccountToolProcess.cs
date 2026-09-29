// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using static DDT.Agent.Deployment.AccountNativeMethods;

namespace DDT.Agent.Deployment;

// A tool started suspended with CreateProcessAsUserW, inside a job that kills its whole tree when the job handle
// closes.
[SupportedOSPlatform("windows")]
internal sealed class AccountToolProcess(AgentLog log) : IToolProcess
{
    private AccountDesktop? _desktop;
    private FileStream? _output;
    private FileStream? _error;
    private SafeKernelHandle? _process;
    private SafeKernelHandle? _thread;
    private SafeKernelHandle? _job;
    private int _exitCode;

    public Stream StandardOutput => _output ?? throw new InvalidOperationException("The process has not started.");

    public Stream StandardError => _error ?? throw new InvalidOperationException("The process has not started.");

    public int ExitCode => _exitCode;

    public unsafe void Start(WindowsAccountSession account, string commandLine, string? workingDirectory, IReadOnlyDictionary<string, string>? environment)
    {
        AccountDesktop desktop = AccountDesktop.Create(account.LogonSid.Value);
        _desktop = desktop;
        AccountToolPipes pipes = AccountToolPipes.Create();
        char* environmentBlock = AccountEnvironment.Build(account.Token, environment);

        try
        {
            fixed (char* desktopName = desktop.Name)
            {
                StartupInfoEx startup = pipes.StartupInfo(desktopName);
                (_process, _thread) = StartSuspended(account, commandLine, workingDirectory, environmentBlock, &startup);
            }

            _job = KillOnCloseJob();
            Assign(_job, _process, account.UserName);
            (_output, _error) = pipes.TakeReadEnds();

            if (ResumeThread(_thread) == ResumeFailed)
            {
                throw DeploymentStepException.ForLastWin32Error($"The script could not be resumed after it started as {account.UserName}");
            }
        }
        catch
        {
            pipes.Dispose();

            throw;
        }
        finally
        {
            pipes.FreeHandleList();
            AccountEnvironment.Free(environmentBlock);
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

    // Closing the job kills the tree too, but this ends it right away, so the wait returns without waiting for the
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

        // Kills the tree, because of the job's kill-on-close limit.
        _job?.Dispose();
        _thread?.Dispose();
        _process?.Dispose();
        _desktop?.Dispose();
    }

    // The command line has to be writable.
    private static unsafe (SafeKernelHandle Process, SafeKernelHandle Thread) StartSuspended(
        WindowsAccountSession account,
        string commandLine,
        string? workingDirectory,
        char* environmentBlock,
        StartupInfoEx* startup)
    {
        char[] command = [.. commandLine, '\0'];
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
                startup,
                out info))
            {
                throw DeploymentStepException.ForLastWin32Error($"The script could not be started as {account.UserName}");
            }
        }

        return (new SafeKernelHandle(info.Process), new SafeKernelHandle(info.Thread));
    }

    private static unsafe SafeKernelHandle KillOnCloseJob()
    {
        SafeKernelHandle job = CreateJobObject(0, null);

        if (job.IsInvalid)
        {
            DeploymentStepException failure = DeploymentStepException.ForLastWin32Error("A job for the script could not be created");
            job.Dispose();

            throw failure;
        }

        JobExtendedLimits limits = new() { LimitFlags = JobObjectLimitKillOnJobClose };

        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JobExtendedLimits)))
        {
            DeploymentStepException failure = DeploymentStepException.ForLastWin32Error("A job for the script could not be set up");
            job.Dispose();

            throw failure;
        }

        return job;
    }

    private static void Assign(SafeKernelHandle job, SafeKernelHandle process, string userName)
    {
        if (!AssignProcessToJobObject(job, process))
        {
            throw DeploymentStepException.ForLastWin32Error($"The script started as {userName} could not be put in its job");
        }
    }
}
