using System.Net;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ImageDownloaderTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");
    private static readonly TimeSpan s_tokenWait = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("ddt-download-").FullName;
    private readonly TestImage _image = new(5000);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PartPath => Path.Combine(_directory, "image.part");

    private string FinalPath => Path.Combine(_directory, "image.wim");

    [Fact]
    public async Task DownloadsTheWholeImage()
    {
        ScriptedAgentServer server = new ScriptedAgentServer().OnOpenImage(_image.From);

        await DownloadAsync(server, new ImmediateTimeProvider());

        Assert.Equal(["open 0 session"], server.Calls);
        Assert.Equal(_image.Content, await File.ReadAllBytesAsync(FinalPath, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(PartPath));
    }

    [Fact]
    public async Task ResumesFromAPartialFile()
    {
        await File.WriteAllBytesAsync(PartPath, _image.Content[..1200], TestContext.Current.CancellationToken);
        ScriptedAgentServer server = new ScriptedAgentServer().OnOpenImage(_image.From);

        await DownloadAsync(server, new ImmediateTimeProvider());

        Assert.Equal(["open 1200 session"], server.Calls);
        Assert.Equal(_image.Content, await File.ReadAllBytesAsync(FinalPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResumesWhereAnEarlyEndLeftOff()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnOpenImage(_ => new AgentImageStream(new MemoryStream(_image.Content[..2000]), 0, _image.Content.Length))
            .OnOpenImage(_image.From);
        ImmediateTimeProvider time = new();

        await DownloadAsync(server, time);

        Assert.Equal(["open 0 session", "open 2000 session"], server.Calls);
        Assert.Equal(_image.Content, await File.ReadAllBytesAsync(FinalPath, TestContext.Current.CancellationToken));
        Assert.Equal([AgentLimits.RetryDelay(1)], time.Delays);
    }

    [Fact]
    public async Task StartsOverWhenTheServerIgnoresTheRange()
    {
        await File.WriteAllBytesAsync(PartPath, _image.Content[..1200], TestContext.Current.CancellationToken);
        ScriptedAgentServer server = new ScriptedAgentServer().OnOpenImage(_ => _image.From(0));

        await DownloadAsync(server, new ImmediateTimeProvider());

        Assert.Equal(["open 1200 session"], server.Calls);
        Assert.Equal(_image.Content, await File.ReadAllBytesAsync(FinalPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StartsOverWhenTheServerCannotSatisfyTheRange()
    {
        await File.WriteAllBytesAsync(PartPath, new byte[1200], TestContext.Current.CancellationToken);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnOpenImage(_ => throw new HttpRequestException("416", null, HttpStatusCode.RequestedRangeNotSatisfiable))
            .OnOpenImage(_image.From);

        await DownloadAsync(server, new ImmediateTimeProvider());

        Assert.Equal(["open 1200 session", "open 0 session"], server.Calls);
        Assert.Equal(_image.Content, await File.ReadAllBytesAsync(FinalPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnImageThatIsAlreadyCompleteIsNotDownloadedAgain()
    {
        await File.WriteAllBytesAsync(PartPath, _image.Content, TestContext.Current.CancellationToken);
        ScriptedAgentServer server = new();

        await DownloadAsync(server, new ImmediateTimeProvider());

        Assert.Empty(server.Calls);
        Assert.True(File.Exists(FinalPath));
    }

    [Fact]
    public async Task AHashMismatchDeletesThePartAndFailsTheStep()
    {
        byte[] damaged = [.. _image.Content];
        damaged[4000] ^= 0xFF;
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnOpenImage(offset => new AgentImageStream(new MemoryStream(damaged[(int)offset..]), offset, damaged.Length));

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(() => DownloadAsync(server, new ImmediateTimeProvider()));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(PartPath));
        Assert.False(File.Exists(FinalPath));
    }

    [Fact]
    public async Task APartFromAnotherOffsetFailsTheStepAndKeepsWhatWasThere()
    {
        await File.WriteAllBytesAsync(PartPath, _image.Content[..1200], TestContext.Current.CancellationToken);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnOpenImage(_ => new AgentImageStream(new MemoryStream(_image.Content[600..]), 600, _image.Content.Length));

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(() => DownloadAsync(server, new ImmediateTimeProvider()));

        Assert.Equal("The server sent the image from byte 600 when byte 1200 was asked for.", exception.Message);
        Assert.Equal(_image.Content[..1200], await File.ReadAllBytesAsync(PartPath, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(FinalPath));
    }

    [Fact]
    public async Task AServerFileOfAnotherSizeFailsTheStep()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnOpenImage(offset => new AgentImageStream(new MemoryStream(_image.Content[(int)offset..]), offset, _image.Content.Length + 1000));

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(() => DownloadAsync(server, new ImmediateTimeProvider()));

        Assert.StartsWith("The server's image file holds 6000 bytes, but the deployment expects 5000.", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(FinalPath));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ARefusalFailsTheStepWithTheServersReason(HttpStatusCode status)
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnOpenImage(_ => throw new AgentRequestException("refused", "This machine may not download this image.", status));

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(() => DownloadAsync(server, new ImmediateTimeProvider()));

        Assert.Equal("This machine may not download this image.", exception.Message);
    }

    [Fact]
    public async Task RetriesAServerErrorAndABusyServer()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnOpenImage(_ => throw new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable))
            .OnOpenImage(_ => throw new HttpRequestException("429", null, HttpStatusCode.TooManyRequests))
            .OnOpenImage(_image.From);
        ImmediateTimeProvider time = new();

        await DownloadAsync(server, time);

        Assert.Equal(3, server.Calls.Count);
        Assert.Equal([AgentLimits.RetryDelay(1), AgentLimits.RetryDelay(2)], time.Delays);
    }

    [Fact]
    public async Task ResumesAfterAStall()
    {
        StallingStream stalling = new(_image.Content[..1000]);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnOpenImage(_ => new AgentImageStream(stalling, 0, _image.Content.Length))
            .OnOpenImage(_image.From);
        ManualTimeProvider time = new();

        Task download = DownloadAsync(server, time);
        await stalling.Stalled.WaitAsync(TestContext.Current.CancellationToken);

        // Nothing happens before the watchdog's minute is up.
        time.Advance(ImageDownloader.StallTimeout - TimeSpan.FromSeconds(1));
        Assert.Single(server.Calls);

        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(1), () => download.IsCompleted);
        await download;

        Assert.Equal(["open 0 session", "open 1000 session"], server.Calls);
        Assert.Equal(_image.Content, await File.ReadAllBytesAsync(FinalPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitsForTheHeartbeatsNextTokenAfterA401()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnOpenImage(_ => throw new AgentTokenRejectedException())
            .OnOpenImage(_image.From);
        DeploymentTokens tokens = new("session-1", "resume-1");
        ManualTimeProvider time = new();

        Task download = DownloadAsync(server, time, tokens);
        await time.AdvanceUntilAsync(TimeSpan.Zero, () => server.Calls.Count == 1);
        tokens.Update("session-2", "resume-2");
        await download.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["open 0 session-1", "open 0 session-2"], server.Calls);
    }

    [Fact]
    public async Task EndsTheRunWhenNoNewTokenComesWithinABeat()
    {
        ScriptedAgentServer server = new ScriptedAgentServer().OnOpenImage(_ => throw new AgentTokenRejectedException());
        ManualTimeProvider time = new();

        Task download = DownloadAsync(server, time);
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(5), () => download.IsCompleted);

        await Assert.ThrowsAsync<AgentTokenRejectedException>(() => download);
        Assert.Equal(["open 0 session"], server.Calls);
    }

    [Fact]
    public async Task EndsTheRunWhenTheNewTokenIsRefusedToo()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnOpenImage(_ => throw new AgentTokenRejectedException())
            .OnOpenImage(_ => throw new AgentTokenRejectedException());
        DeploymentTokens tokens = new("session-1", "resume-1");
        ManualTimeProvider time = new();

        Task download = DownloadAsync(server, time, tokens);
        await time.AdvanceUntilAsync(TimeSpan.Zero, () => server.Calls.Count == 1);
        tokens.Update("session-2", "resume-2");

        await Assert.ThrowsAsync<AgentTokenRejectedException>(() => download.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["open 0 session-1", "open 0 session-2"], server.Calls);
    }

    private Task DownloadAsync(ScriptedAgentServer server, TimeProvider time, DeploymentTokens? tokens = null)
    {
        AgentLog log = new(time, TextWriter.Null);
        ImageDownloader downloader = new(server, tokens ?? new DeploymentTokens("session", "resume"), log, time, s_tokenWait);

        return downloader.DownloadAsync(
            s_machineId,
            _image.Sha256,
            _image.Content.Length,
            PartPath,
            FinalPath,
            new Progress<int>(),
            TestContext.Current.CancellationToken);
    }
}
