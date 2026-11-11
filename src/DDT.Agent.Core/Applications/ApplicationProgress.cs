// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Agents;

namespace DDT.Agent.Applications;

// What an Install applications step has done so far, by application id: the content of its checkpoint. An application
// without an entry is pending.
public sealed class ApplicationProgress
{
    private readonly Dictionary<Guid, ApplicationMark> _marks;

    private ApplicationProgress(Dictionary<Guid, ApplicationMark> marks) => _marks = marks;

    // Keeps only the applications in the list. A checkpoint that cannot be read counts as none.
    public static ApplicationProgress Read(string? saved, IReadOnlyList<AgentApplication> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);

        Dictionary<Guid, ApplicationMark>? marks = null;

        try
        {
            marks = string.IsNullOrEmpty(saved) ? null : JsonSerializer.Deserialize(saved, ApplicationProgressJsonContext.Default.DictionaryGuidApplicationMark);
        }
        catch (JsonException)
        {
        }

        HashSet<Guid> listed = [.. applications.Select(application => application.Id)];

        return new ApplicationProgress(new Dictionary<Guid, ApplicationMark>(
            (marks ?? []).Where(mark => listed.Contains(mark.Key) && mark.Value is not null && mark.Value.State != ApplicationState.Pending)));
    }

    public ApplicationState StateOf(Guid id) => _marks.TryGetValue(id, out ApplicationMark? mark) ? mark.State : ApplicationState.Pending;

    // How often its installer was started, for an application still Started.
    public int AttemptsOf(Guid id) => _marks.TryGetValue(id, out ApplicationMark? mark) && mark.State == ApplicationState.Started ? mark.Attempts : 0;

    public bool IsDecided(Guid id) => StateOf(id) is not (ApplicationState.Pending or ApplicationState.Started);

    public int Count(ApplicationState state) => _marks.Values.Count(mark => mark.State == state);

    public void Start(Guid id, int attempt) => _marks[id] = new ApplicationMark(ApplicationState.Started, attempt);

    public void Decide(Guid id, ApplicationState state) => _marks[id] = new ApplicationMark(state);

    public string Write() => JsonSerializer.Serialize(_marks, ApplicationProgressJsonContext.Default.DictionaryGuidApplicationMark);
}
