// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// DDT's session file in the Windows at windowsRoot. A file that can't be read counts as none, and the run continues
// without the session.
internal sealed class DeploySessionStore(string windowsRoot, AgentLog log)
{
    private string FilePath => DeploySession.FilePathIn(windowsRoot);

    public async Task<DeploySessionFile?> LoadAsync(CancellationToken cancellationToken) =>
        File.Exists(FilePath) ? Parse(await File.ReadAllBytesAsync(FilePath, cancellationToken).ConfigureAwait(false)) : null;

    // For the callers that hold the session's lock.
    public DeploySessionFile? Load() => File.Exists(FilePath) ? Parse(File.ReadAllBytes(FilePath)) : null;

    public async Task SaveAsync(DeploySessionFile file, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(windowsRoot, "DDT"));
        await File.WriteAllBytesAsync(FilePath, JsonSerializer.SerializeToUtf8Bytes(file, DeploySessionFileJsonContext.Default.DeploySessionFile), cancellationToken)
            .ConfigureAwait(false);
    }

    public void Save(DeploySessionFile file) =>
        File.WriteAllBytes(FilePath, JsonSerializer.SerializeToUtf8Bytes(file, DeploySessionFileJsonContext.Default.DeploySessionFile));

    public void Delete() => Leftovers.Delete(FilePath, log);

    private DeploySessionFile? Parse(byte[] json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, DeploySessionFileJsonContext.Default.DeploySessionFile);
        }
        catch (JsonException exception)
        {
            log.Warning($"{FilePath} cannot be read ({exception.Message}), so the run goes on without DDT's session.");

            return null;
        }
    }
}
