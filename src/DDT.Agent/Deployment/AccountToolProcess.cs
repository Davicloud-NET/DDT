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
    private Stream? _output;
    private Stream? _error;
    private SafeKernelHandle? _process;
    private SafeKernelHandle? _thread;
    private SafeKernelHandle? _job;
    private int _exitCode;
    private string _tool = "The tool";

    public Stream StandardOutput => _output ?? throw new InvalidOperationException("The process has not started.");

    public Stream StandardError => _error ?? throw new InvalidOperationException("The process has not started.");

    public int ExitCode => _exitCode;

    public unsafe void Start(WindowsAccountSession account, string fileName, IReadOnlyList<string> arguments, ToolRunOptions options)
    {
        _tool = Path.GetFileName(fileName);
        string commandLine = WindowsCommandLine.Build(fileName, arguments);

        if (account.Desktop is { } sessionDesktop)
        {
            StartInSession(account, sessionDesktop, commandLine, options);

            return;
        }

        _desktop = AccountDesktop.Create(account.LogonSid.Value);
        AccountToolPipes pipes = AccountToolPipes.Create();
        char* environmentBlock = AccountEnvironment.Build(account.Token, options.Environment);

        try
        {
            fixed (char* desktop = _desktop.Name)
            {
                StartupInfoEx startup = pipes.StartupInfo(desktop, handleList: !options.WithoutHandleList);
                (_process, _thread) = StartSuspended(account, commandLine, options.WorkingDirectory, environmentBlock, &startup);
            }

            _job = KillOnCloseJob();
            Assign(_job, _process, account.UserName);
            (_output, _error) = pipes.TakeReadEnds();
            Resume(_thread, account.UserName);
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
    public Task KillAsync()
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
            log.Warning($"{_tool} could not be stopped ({exception.Message}).");
        }

        return Task.CompletedTask;
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

    // A handle cannot be inherited into another session, so this start passes none: no pipes, no attribute list. The
    // output streams stay empty, and the tool reports through a file.
    private unsafe void StartInSession(WindowsAccountSession account, string sessionDesktop, string commandLine, ToolRunOptions options)
    {
        char* environmentBlock = AccountEnvironment.Build(account.Token, options.Environment);
        (SafeKernelHandle Process, SafeKernelHandle Thread) started;

        try
        {
            fixed (char* desktop = sessionDesktop)
            {
                // STARTUPINFOW is STARTUPINFOEXW without the attribute list at its end.
                StartupInfoEx startup = new() { Size = (uint)(sizeof(StartupInfoEx) - sizeof(void*)), Desktop = desktop };
                started = StartSuspended(account, commandLine, options.WorkingDirectory, environmentBlock, &startup);
            }
        }
        finally
        {
            AccountEnvironment.Free(environmentBlock);
        }

        (_process, _thread) = started;
        _output = Stream.Null;
        _error = Stream.Null;

        try
        {
            _job = KillOnCloseJob();
            Assign(_job, started.Process, account.UserName);
            Resume(started.Thread, account.UserName);
        }
        catch
        {
            // Not in the job yet, so closing it would leave the suspended process behind.
            _ = TerminateProcess(started.Process, 1);

            throw;
        }
    }

    private void Resume(SafeKernelHandle thread, string userName)
    {
        if (ResumeThread(thread) == ResumeFailed)
        {
            throw DeploymentStepException.ForLastWin32Error($"{_tool} could not be resumed after it started as {userName}");
        }
    }

    // The command line has to be writable. A start in the account's session inherits nothing, one on a desktop of its
    // own inherits its list's handles, or every inheritable one without a list.
    private unsafe (SafeKernelHandle Process, SafeKernelHandle Thread) StartSuspended(
        WindowsAccountSession account,
        string commandLine,
        string? workingDirectory,
        char* environmentBlock,
        StartupInfoEx* startup)
    {
        bool inSession = account.Desktop is not null;
        bool listed = startup->AttributeList is not null;
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
                inheritHandles: !inSession,
                CreateSuspended | CreateUnicodeEnvironment | CreateNoWindow | (listed ? ExtendedStartupInfoPresent : 0),
                environmentBlock,
                workingDirectory,
                startup,
                out info))
            {
                DeploymentStepException failure = DeploymentStepException.ForLastWin32Error(
                    inSession ? $"{_tool} could not be started in the session of {account.UserName}" : $"{_tool} could not be started as {account.UserName}");
                log.Information(
                    $"CreateProcessAsUser refused {_tool} {(inSession ? "in the session, with no handles" : listed ? "with a handle list" : "without a handle list")}.");

                throw failure;
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

    private void Assign(SafeKernelHandle job, SafeKernelHandle process, string userName)
    {
        if (!AssignProcessToJobObject(job, process))
        {
            throw DeploymentStepException.ForLastWin32Error($"{_tool} started as {userName} could not be put in its job");
        }
    }
}
