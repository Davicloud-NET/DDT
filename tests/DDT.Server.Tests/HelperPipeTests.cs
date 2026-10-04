// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using System.Security.Principal;
using DDT.Host.Helper;
using DDT.Server.BootImage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DDT.Server.Tests;

// The pipe between the web server and the helper, with this test's account in place of the DDT service's.
public sealed class HelperPipeTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task TheServerSendsARequestAndReadsTheLinesAndTheEnd()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The helper is a Windows service.");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string name = "DDT.Helper.Test." + Guid.NewGuid().ToString("N");
        List<HelperRequest> requests = [];
        string? problem = null;

        using WindowsIdentity account = WindowsIdentity.GetCurrent();
        using HelperPipeServer server = new(
            name,
            account.User ?? throw new InvalidOperationException("This account has no SID."),
            (request, line, _) =>
            {
                requests.Add(request);
                line("first");
                line("second");

                return Task.FromResult(problem);
            },
            NullLogger<HelperPipeServer>.Instance);
        await server.StartAsync(cancellationToken);

        PipeBootImageHelper helper = new(name);
        Assert.False(new PipeBootImageHelper(name + ".other").Available);

        HelperRequest sent = new() { Kind = HelperRequest.Build, Name = "20261001-100000", Drivers = [new HelperDriver(Guid.NewGuid(), "Network", new string('a', 64))] };
        List<HelperMessage> answered = await helper.RunAsync(sent, cancellationToken).ToListAsync(cancellationToken);

        Assert.Equal(["first", "second", null], answered.Select(message => message.Line));
        Assert.Equal(0, answered[^1].ExitCode);
        Assert.Equal(sent.Drivers, Assert.Single(requests).Drivers);

        // The next request gets a pipe of its own
        problem = "The ADK is not installed.";
        List<HelperMessage> refused = await helper.RunAsync(new HelperRequest { Kind = HelperRequest.InstallAdk }, cancellationToken).ToListAsync(cancellationToken);

        Assert.Equal((1, problem), (refused[^1].ExitCode, refused[^1].Problem));
        Assert.True(helper.Available);

        await server.StopAsync(cancellationToken);
    }
}
