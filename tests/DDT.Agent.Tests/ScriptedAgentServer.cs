// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;

namespace DDT.Agent.Tests;

// Answers from a script. When the register, next or sign-in script runs out it stops the loop, so every test
// ends deterministically without timing; so do the image list, pick, unattend and image calls. Log requests
// succeed unless a scripted action throws, and a report without a script echoes the token it was sent with.
// A deployment's heartbeat calls from another thread, so everything is guarded by one lock, and scripted answers
// run outside it.
internal sealed class ScriptedAgentServer : IAgentServer
{
    private readonly Lock _lock = new();
    private readonly Queue<Func<AgentRegistration, AgentRegistrationResult>> _registrations = new();
    private readonly Queue<Func<string, AgentNextResult>> _nexts = new();
    private readonly Queue<Action<AgentLogBatch>> _logs = new();
    private readonly Queue<Func<AgentSignInRequest, AgentSignInResult>> _signIns = new();
    private readonly Queue<Func<AgentRelease?>> _releases = new();
    private readonly Queue<byte[]> _downloads = new();
    private readonly Queue<Func<IReadOnlyList<AgentImageChoice>>> _images = new();
    private readonly Queue<Func<AgentPickRequest, AgentDeployment>> _picks = new();
    private readonly Dictionary<DeploymentState, Queue<Func<AgentDeploymentReport, AgentDeploymentReportResult>>> _reports = [];
    private readonly Queue<Func<string>> _unattends = new();
    private readonly Queue<Func<long?>> _heads = new();
    private readonly Queue<Func<long, AgentImageStream>> _opens = new();
    private readonly List<string> _calls = [];
    private readonly List<AgentDeploymentReport> _sentReports = [];

    public CancellationTokenSource Stop { get; } = new();

    // A copy, so a test can read it while a deployment still runs.
    public List<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public List<AgentDeploymentReport> Reports
    {
        get
        {
            lock (_lock)
            {
                return [.. _sentReports];
            }
        }
    }

    public List<AgentRegistration> Registrations { get; } = [];

    public List<AgentLogLine> SentLines { get; } = [];

    public List<AgentSignInRequest> SignIns { get; } = [];

    public List<AgentPickRequest> Picks { get; } = [];

    // Answers every report when set, ahead of the scripts: a test can switch it while a deployment runs.
    public Func<AgentDeploymentReport, string, AgentDeploymentReportResult>? AnswerReports { get; set; }

    // Runs for every log request when set, ahead of the scripts, and may throw to refuse it.
    public Action<AgentLogBatch>? AnswerLogs { get; set; }

    public ScriptedAgentServer OnRegister(Func<AgentRegistration, AgentRegistrationResult> response) => Enqueue(_registrations, response);

    public ScriptedAgentServer OnNext(Func<string, AgentNextResult> response) => Enqueue(_nexts, response);

    public ScriptedAgentServer OnLog(Action<AgentLogBatch> action) => Enqueue(_logs, action);

    public ScriptedAgentServer OnSignIn(Func<AgentSignInRequest, AgentSignInResult> response) => Enqueue(_signIns, response);

    public ScriptedAgentServer OnRelease(Func<AgentRelease?> response) => Enqueue(_releases, response);

    public ScriptedAgentServer OnDownload(byte[] content) => Enqueue(_downloads, content);

    public ScriptedAgentServer OnImages(Func<IReadOnlyList<AgentImageChoice>> response) => Enqueue(_images, response);

    public ScriptedAgentServer OnPick(Func<AgentPickRequest, AgentDeployment> response) => Enqueue(_picks, response);

    public ScriptedAgentServer OnUnattend(Func<string> response) => Enqueue(_unattends, response);

    public ScriptedAgentServer OnHeadImage(Func<long?> response) => Enqueue(_heads, response);

    // Receives the offset asked for.
    public ScriptedAgentServer OnOpenImage(Func<long, AgentImageStream> response) => Enqueue(_opens, response);

    // For reports of this state only, so the heartbeat's progress reports do not use up the script.
    public ScriptedAgentServer OnReport(DeploymentState state, Func<AgentDeploymentReport, AgentDeploymentReportResult> response)
    {
        lock (_lock)
        {
            if (!_reports.TryGetValue(state, out Queue<Func<AgentDeploymentReport, AgentDeploymentReportResult>>? queue))
            {
                queue = new Queue<Func<AgentDeploymentReport, AgentDeploymentReportResult>>();
                _reports[state] = queue;
            }

            queue.Enqueue(response);
        }

        return this;
    }

    public Task<AgentRegistrationResult> RegisterAsync(AgentRegistration registration, CancellationToken cancellationToken)
    {
        Func<AgentRegistration, AgentRegistrationResult>? response;

        lock (_lock)
        {
            _calls.Add("register");
            Registrations.Add(registration);
            _registrations.TryDequeue(out response);
        }

        return response is null ? Stopped<AgentRegistrationResult>() : Task.FromResult(response(registration));
    }

    public Task<AgentNextResult> NextAsync(Guid machineId, string token, CancellationToken cancellationToken) =>
        Answer($"next {token}", _nexts, response => response(token));

    public Task SendLogAsync(Guid machineId, string token, AgentLogBatch batch, CancellationToken cancellationToken)
    {
        Action<AgentLogBatch>? action;

        lock (_lock)
        {
            _calls.Add($"log {token}");
            action = AnswerLogs;

            if (action is null)
            {
                _logs.TryDequeue(out action);
            }
        }

        action?.Invoke(batch);

        lock (_lock)
        {
            SentLines.AddRange(batch.Lines);
        }

        return Task.CompletedTask;
    }

    public Task<AgentSignInResult> SignInAsync(Guid machineId, string token, AgentSignInRequest request, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            SignIns.Add(request);
        }

        return Answer($"sign-in {token}", _signIns, response => response(request));
    }

    // Unlike the other calls, running out of answers means the server offers no agent, not the end of a test.
    public Task<AgentRelease?> GetReleaseAsync(CancellationToken cancellationToken)
    {
        Func<AgentRelease?>? response;

        lock (_lock)
        {
            _calls.Add("release");
            _releases.TryDequeue(out response);
        }

        return Task.FromResult(response?.Invoke());
    }

    public async Task DownloadReleaseAsync(Stream destination, CancellationToken cancellationToken)
    {
        byte[] content;

        lock (_lock)
        {
            _calls.Add("download");
            content = _downloads.Dequeue();
        }

        await destination.WriteAsync(content, cancellationToken);
    }

    public Task<IReadOnlyList<AgentImageChoice>> GetImagesAsync(Guid machineId, string token, CancellationToken cancellationToken) =>
        Answer($"images {token}", _images, response => response());

    public Task<AgentDeployment> PickImageAsync(Guid machineId, string token, AgentPickRequest request, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Picks.Add(request);
        }

        return Answer($"pick {token}", _picks, response => response(request));
    }

    public Task<AgentDeploymentReportResult> ReportDeploymentAsync(
        Guid machineId,
        string token,
        Guid deploymentId,
        AgentDeploymentReport report,
        CancellationToken cancellationToken)
    {
        Func<AgentDeploymentReport, AgentDeploymentReportResult>? response = null;
        Func<AgentDeploymentReport, string, AgentDeploymentReportResult>? answer;

        lock (_lock)
        {
            _calls.Add($"report {report.State} {token}");
            _sentReports.Add(report);
            answer = AnswerReports;

            if (answer is null && _reports.TryGetValue(report.State, out Queue<Func<AgentDeploymentReport, AgentDeploymentReportResult>>? queue))
            {
                queue.TryDequeue(out response);
            }
        }

        try
        {
            return Task.FromResult(answer?.Invoke(report, token) ?? response?.Invoke(report) ?? new AgentDeploymentReportResult(token, "resume"));
        }
        catch (Exception exception)
        {
            return Task.FromException<AgentDeploymentReportResult>(exception);
        }
    }

    public Task<string> GetUnattendAsync(Guid machineId, string token, Guid deploymentId, CancellationToken cancellationToken) =>
        Answer($"unattend {token}", _unattends, response => response());

    public Task<long?> HeadImageAsync(Guid machineId, string token, string sha256, CancellationToken cancellationToken) =>
        Answer($"head {token}", _heads, response => response());

    public Task<AgentImageStream> OpenImageAsync(Guid machineId, string token, string sha256, long offset, CancellationToken cancellationToken) =>
        Answer($"open {offset} {token}", _opens, response => response(offset));

    private ScriptedAgentServer Enqueue<T>(Queue<T> queue, T item)
    {
        lock (_lock)
        {
            queue.Enqueue(item);
        }

        return this;
    }

    // A scripted answer may throw, which the caller sees as a failed request.
    private Task<TResult> Answer<TScript, TResult>(string call, Queue<TScript> queue, Func<TScript, TResult> run)
        where TScript : class
    {
        TScript? script;

        lock (_lock)
        {
            _calls.Add(call);
            queue.TryDequeue(out script);
        }

        if (script is null)
        {
            return Stopped<TResult>();
        }

        try
        {
            return Task.FromResult(run(script));
        }
        catch (Exception exception)
        {
            return Task.FromException<TResult>(exception);
        }
    }

    private Task<T> Stopped<T>()
    {
        Stop.Cancel();

        return Task.FromCanceled<T>(Stop.Token);
    }
}
