using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AgentSignInTests
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private static AgentRegistrationResult Pending() =>
        new(s_machineId, MachineState.Pending, "poll-0", "resume", 10, null);

    private static AgentNextResult Next(MachineState state, string token, string? signedInBy = null) =>
        new(state, token, "resume", 10, signedInBy);

    private static AgentSignInResult Answer(AgentSignInStatus status) => new(status);

    private static AgentLoop Create(ScriptedAgentServer server, ScriptedSignInPrompt prompt)
    {
        ImmediateTimeProvider time = new();

        return TestAgents.Loop(server, prompt, new FakeDeploymentTools(), new AgentLog(time, TextWriter.Null), time);
    }

    [Fact]
    public async Task SigningInAuthorizesTheMachine()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll-1"))
            .OnNext(_ => Next(MachineState.Pending, "poll-2"))
            .OnSignIn(_ => Answer(AgentSignInStatus.Succeeded))
            .OnNext(_ => Next(MachineState.Approved, "session-1", "bob"));
        ScriptedSignInPrompt prompt = new("bob", "secret");

        Assert.Equal(AgentExitCodes.Stopped, await Create(server, prompt).RunAsync(server.Stop.Token));

        // Every field is followed by a poll, and the sign in by one more that picks up the session token.
        Assert.Equal(
            ["register", "next poll-0", "next poll-1", "sign-in poll-2", "next poll-2", "log session-1", "next session-1"],
            server.Calls);
        Assert.Equal([new AgentSignInRequest("bob", "secret", null)], server.SignIns);
        Assert.Equal(["User name", "Password for bob (hidden)"], prompt.Labels);
    }

    [Fact]
    public async Task AWrongPasswordAsksOnlyForThePasswordAgain()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.Failed))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.Succeeded))
            .OnNext(_ => Next(MachineState.Approved, "session", "bob"));
        ScriptedSignInPrompt prompt = new("bob", "wrong", "right");

        await Create(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal([new AgentSignInRequest("bob", "wrong", null), new AgentSignInRequest("bob", "right", null)], server.SignIns);
        Assert.Equal(["User name", "Password for bob (hidden)", "Password for bob (hidden)"], prompt.Labels);
    }

    [Fact]
    public async Task ATwoFactorAccountIsAskedForItsCode()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.RequiresTwoFactor))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.Succeeded))
            .OnNext(_ => Next(MachineState.Approved, "session", "bob"));
        ScriptedSignInPrompt prompt = new("bob", "secret", "123 456");

        await Create(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(
            [new AgentSignInRequest("bob", "secret", null), new AgentSignInRequest("bob", "secret", "123 456")],
            server.SignIns);
        Assert.Equal(["User name", "Password for bob (hidden)", "Authenticator code"], prompt.Labels);
    }

    [Fact]
    public async Task AWrongCodeAsksOnlyForTheCodeAgain()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.RequiresTwoFactor))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.Failed))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.Succeeded))
            .OnNext(_ => Next(MachineState.Approved, "session", "bob"));
        ScriptedSignInPrompt prompt = new("bob", "secret", "111111", "222222");

        await Create(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(
            [
                new AgentSignInRequest("bob", "secret", null),
                new AgentSignInRequest("bob", "secret", "111111"),
                new AgentSignInRequest("bob", "secret", "222222"),
            ],
            server.SignIns);
        Assert.Equal(["User name", "Password for bob (hidden)", "Authenticator code", "Authenticator code"], prompt.Labels);
    }

    [Fact]
    public async Task AnAccountThatMayNotAuthorizeStartsOver()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.NotPermitted))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.Succeeded))
            .OnNext(_ => Next(MachineState.Approved, "session", "bob"));
        ScriptedSignInPrompt prompt = new("viewer", "secret", "bob", "secret");

        await Create(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(["User name", "Password for viewer (hidden)", "User name", "Password for bob (hidden)"], prompt.Labels);
        Assert.Equal("bob", server.SignIns[^1].UserName);
    }

    [Fact]
    public async Task AnEmptyPasswordStartsOverWithAnotherAccount()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.Succeeded))
            .OnNext(_ => Next(MachineState.Approved, "session", "alice"));
        ScriptedSignInPrompt prompt = new("bob", string.Empty, "alice", "secret");

        await Create(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal([new AgentSignInRequest("alice", "secret", null)], server.SignIns);
        Assert.Equal(["User name", "Password for bob (hidden)", "User name", "Password for alice (hidden)"], prompt.Labels);
    }

    [Fact]
    public async Task AnApprovalOnTheWebTakesThePromptAway()
    {
        ScriptedSignInPrompt prompt = new();
        int cancelledWhenApproved = 0;
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Approved, "session"))
            .OnLog(_ => cancelledWhenApproved = prompt.Cancelled);

        await Create(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(1, cancelledWhenApproved);
        Assert.Empty(server.SignIns);
    }

    [Fact]
    public async Task WaitsForTheWebApprovalOnceSomeoneHasSignedIn()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.Succeeded))
            .OnNext(_ => Next(MachineState.Pending, "poll", "bob"))
            .OnNext(_ => Next(MachineState.Pending, "poll", "bob"))
            .OnNext(_ => Next(MachineState.Approved, "session", "bob"));
        ScriptedSignInPrompt prompt = new("bob", "secret");

        await Create(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(["User name", "Password for bob (hidden)"], prompt.Labels);
        Assert.Equal(0, prompt.Cancelled);
    }

    [Fact]
    public async Task AsksAgainWhenTheSignInDoesNotGetThrough()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => throw new HttpRequestException("Too many requests.", null, System.Net.HttpStatusCode.TooManyRequests))
            .OnNext(_ => Next(MachineState.Pending, "poll"))
            .OnSignIn(_ => Answer(AgentSignInStatus.Succeeded))
            .OnNext(_ => Next(MachineState.Approved, "session", "bob"));
        ScriptedSignInPrompt prompt = new("bob", "first", "second");

        await Create(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal([new AgentSignInRequest("bob", "first", null), new AgentSignInRequest("bob", "second", null)], server.SignIns);
        Assert.Contains("log session", server.Calls);
    }

    [Fact]
    public async Task RegistersAgainWhenTheSignInTokenIsRefused()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll-1"))
            .OnNext(_ => Next(MachineState.Pending, "poll-2"))
            .OnSignIn(_ => throw new AgentTokenRejectedException());
        ScriptedSignInPrompt prompt = new("bob", "secret");

        await Create(server, prompt).RunAsync(server.Stop.Token);

        Assert.Equal(["sign-in poll-2", "register"], server.Calls[^2..]);
    }

    [Fact]
    public async Task DoesNotAskWithoutAKeyboard()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Pending())
            .OnNext(_ => Next(MachineState.Pending, "poll"));
        ScriptedSignInPrompt prompt = new() { IsAvailable = false };

        await Create(server, prompt).RunAsync(server.Stop.Token);

        Assert.Empty(prompt.Labels);
        Assert.Equal(["register", "next poll-0", "next poll"], server.Calls);
    }
}
