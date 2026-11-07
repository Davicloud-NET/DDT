// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.LinuxPhase;

namespace DDT.Agent.Tests;

// A folder that stands in for / with cloud-init's files in it, and the agent's wait over it on a clock the test moves.
// Fixtures holds what two real images wrote: Debian 13 with cloud-init 25.1.4 and Ubuntu 24.04 with 26.1.
internal sealed class CloudInitRoot : IDisposable
{
    public const string StartA = "8f2c6f6e-0f5c-4d43-9d1d-4a1f0e0c2a01";

    public const string StartB = "c1a59e44-7b1e-4f0a-8a53-2f3b0e7d9b02";

    public const string Clean = """{ "v1": { "datasource": "DataSourceNoCloud [seed=/dev/nvme0n1p2]", "errors": [] }}""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ddt-cloud-init-{Guid.NewGuid():N}");

    public CloudInitRoot() => Log = new AgentLog(Time, Console);

    public ManualTimeProvider Time { get; } = new();

    public StringWriter Console { get; } = new();

    public AgentLog Log { get; }

    public CloudInitFiles Files => new(_root);

    public string Marker => Path.Combine(_root, "var", "lib", "ddt", "run", "cloud-init-waited");

    public string Note => Path.Combine(_root, "var", "lib", "ddt", "run", "cloud-init-first-start");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        Console.Dispose();
    }

    // What one start of the agent notes of the start of Linux it runs in.
    public FirstStartNote FirstStart() => new(Files, Note, Log);

    // The wait of one start of the agent, which starts at this moment.
    public CloudInitWait Wait() => new(Files, FirstStart(), Marker, Log, Time);

    public void Put(string path, string content = "")
    {
        string file = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    public void Remove(string path)
    {
        if (File.Exists(Path.Combine(_root, path)))
        {
            File.Delete(Path.Combine(_root, path));
        }
    }

    // The run's own wait for cloud-init was over at an earlier start.
    public void WaitWasOver()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Marker)!);
        File.WriteAllText(Marker, string.Empty);
    }

    // Linux starts, with this id, and cloud-init's generator says that it is to run.
    public void Start(string id)
    {
        Put(CloudInitFiles.StartIdPath, id + "\n");
        Put(CloudInitFiles.EnabledPath);
        Remove(CloudInitFiles.ResultPath);
        Remove(CloudInitFiles.StatusPath);
        Remove(CloudInitFiles.ScheduledShutdownPath);
    }

    // A file as a real image wrote it, from the test's fixtures.
    public static string Fixture(string image, string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CloudInit", image, name));

    // What the log got, without each line's time.
    public string[] Lines() =>
        [.. Console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[9..])];

    public void ForgetLines() => Console.GetStringBuilder().Clear();
}
