// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;
using DDT.Core.Templates;
using DDT.Core.Unattend;

namespace DDT.Core.Values;

// Works out a run's values when it starts, and their preview on the web. For each name, the first source in
// ValueSources order wins and the later ones show as overridden. Names ignore case, like in templates.
public static class ValueResolver
{
    // Goes between the names in a loop of values, so a person can follow the loop.
    public const string PathSeparator = " > ";

    public static ValueResolution Resolve(ValueSources sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        List<ValueProblem> problems = [];
        (Dictionary<string, List<Candidate>> byName, List<string> order) = Group(sources, problems);
        Resolution resolution = new(
            order.ToDictionary(name => name, name => byName[name], StringComparer.OrdinalIgnoreCase),
            sources.Facts,
            problems);
        List<ResolvedValue> values = [];
        Dictionary<string, string> effective = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in order)
        {
            if (resolution.Value(name) is { } used)
            {
                effective[name] = used;
            }
        }

        foreach (string name in order)
        {
            List<Candidate> named = byName[name];

            values.Add(Resolved(name, named[0], effective.GetValueOrDefault(name) ?? named[0].Text, overridden: false));
            values.AddRange(named.Skip(1).Select(candidate => Resolved(name, candidate, resolution.Rendered(candidate), overridden: true)));
        }

        if (effective.TryGetValue(MachineVariableNames.ComputerName, out string? computerName)
            && ComputerNames.Problem(computerName) is { } refused)
        {
            problems.Add(new ValueProblem(
                MachineVariableNames.ComputerName,
                ServerMessages.ValuesComputerName.With("problem", refused, "value", computerName)));
        }

        List<ResolvedValue> inputDefaults = InputDefaults(sources, byName, effective, resolution, problems);

        return new ValueResolution(values, effective, [.. problems.Distinct()], inputDefaults);
    }

    // Groups the values by name. A name is spelled as the sequence declares it, or else as the first source that sets
    // it. Names that no source sets are dropped.
    private static (Dictionary<string, List<Candidate>> ByName, List<string> Order) Group(ValueSources sources, List<ValueProblem> problems)
    {
        Dictionary<string, List<Candidate>> byName = new(StringComparer.OrdinalIgnoreCase);
        List<string> order = [];

        foreach (string name in Declared(sources.Sequence))
        {
            if (byName.TryAdd(name, []))
            {
                order.Add(name);
            }
        }

        foreach (Candidate candidate in Candidates(sources, problems))
        {
            if (!byName.TryGetValue(candidate.Name, out List<Candidate>? named))
            {
                byName[candidate.Name] = named = [];
                order.Add(candidate.Name);
            }

            named.Add(candidate);
        }

        order.RemoveAll(name => byName[name].Count == 0);

        return (byName, order);
    }

    // A required input needs an answer, or a value to start its question with.
    private static List<ResolvedValue> InputDefaults(
        ValueSources sources,
        Dictionary<string, List<Candidate>> byName,
        Dictionary<string, string> effective,
        Resolution resolution,
        List<ValueProblem> problems)
    {
        List<ResolvedValue> inputDefaults = [];

        foreach (InputDeclaration input in Inputs(sources.Sequence))
        {
            bool answered = Answer(sources.Answers, input.Name) is not null;
            ResolvedValue? prefill = Prefill(input, byName.GetValueOrDefault(input.Name) ?? [], effective, resolution, answered);

            if (prefill is not null)
            {
                inputDefaults.Add(prefill);
            }
            else if (input.Required && !answered)
            {
                problems.Add(new ValueProblem(input.Name, ServerMessages.ValuesInputRequired.With("label", input.Label ?? input.Name)));
            }
        }

        return inputDefaults;
    }

    // The machine's, a rule's or a role's value for the input's name, as the run would use it without an answer.
    // Otherwise it's the input's Default. Either one answers a required input.
    private static ResolvedValue? Prefill(
        InputDeclaration input,
        List<Candidate> named,
        Dictionary<string, string> effective,
        Resolution resolution,
        bool answered)
    {
        int index = named.FindIndex(candidate => candidate.Source is ValueSource.Machine or ValueSource.Rule or ValueSource.Role);

        if (index >= 0)
        {
            Candidate given = named[index];
            string value = index == 0 && effective.TryGetValue(input.Name, out string? used) ? used : resolution.Rendered(given);

            return new ResolvedValue(input.Name, value, given.Source, given.SourceId, given.SourceName, answered);
        }

        return string.IsNullOrEmpty(input.Default)
            ? null
            : new ResolvedValue(input.Name, input.Default, ValueSource.SequenceDefault, null, null, answered);
    }

    // Only the machine or the run sets a name from the catalogue, so a source that sets one is a problem. ComputerName
    // is the exception, because the machine's name comes from that value.
    private static List<Candidate> Candidates(ValueSources sources, List<ValueProblem> problems)
    {
        List<Candidate> candidates = [];

        foreach (Candidate candidate in Offered(sources).OfType<Candidate>())
        {
            if (IsFact(candidate.Name))
            {
                problems.Add(new ValueProblem(candidate.Name, ServerMessages.ValuesFact.With("name", candidate.Name)));
            }
            else
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    // Every value the sources set, in the order they win. Null where a source sets no value.
    private static IEnumerable<Candidate?> Offered(ValueSources sources)
    {
        List<InputDeclaration> inputs = Inputs(sources.Sequence);

        foreach (InputDeclaration input in inputs)
        {
            yield return Offer(input.Name, Answer(sources.Answers, input.Name), template: false, ValueSource.Input);
        }

        foreach (NamedValue? value in sources.Machine ?? [])
        {
            yield return Offer(value?.Name, value?.Value, template: false, ValueSource.Machine);
        }

        foreach ((IReadOnlyList<ValueSet>? sets, ValueSource source) in SetsInOrder(sources))
        {
            foreach (ValueSet? set in sets ?? [])
            {
                foreach (NamedValue? value in set?.Values ?? [])
                {
                    yield return Offer(value?.Name, value?.Value, template: true, source, set);
                }
            }
        }

        foreach (InputDeclaration input in inputs.Where(input => !string.IsNullOrEmpty(input.Default)))
        {
            yield return Offer(input.Name, input.Default, template: false, ValueSource.SequenceDefault);
        }

        foreach (VariableDeclaration? variable in sources.Sequence?.Variables ?? [])
        {
            yield return Offer(variable?.Name, variable?.Default, template: true, ValueSource.SequenceDefault);
        }

        foreach (NamedValue? value in sources.DeploymentDefaults ?? [])
        {
            yield return Offer(value?.Name, value?.Value, template: false, ValueSource.DeploymentDefault);
        }
    }

    private static Candidate? Offer(string? name, string? text, bool template, ValueSource source, ValueSet? set = null) =>
        string.IsNullOrEmpty(name) || text is null ? null : new Candidate(name, text, template, source, set?.Id, set?.Name);

    private static (IReadOnlyList<ValueSet>? Sets, ValueSource Source)[] SetsInOrder(ValueSources sources) =>
        [(sources.Rules, ValueSource.Rule), (sources.Roles, ValueSource.Role)];

    // The names the sequence declares, in its order, so they come first and keep the sequence's spelling.
    private static List<string> Declared(SequenceDefinition? sequence) =>
    [
        .. (sequence?.Variables ?? []).Select(variable => variable?.Name).OfType<string>().Where(name => name.Length > 0),
        .. Inputs(sequence).Select(input => input.Name),
    ];

    // The inputs whose answers are values. An Account input's answer is a credential, which only the run gets.
    private static List<InputDeclaration> Inputs(SequenceDefinition? sequence) =>
    [
        .. (sequence?.Inputs ?? [])
            .OfType<InputDeclaration>()
            .Where(input => input.Kind != InputKind.Account && !string.IsNullOrEmpty(input.Name)),
    ];

    private static string? Answer(IReadOnlyDictionary<string, string>? answers, string name) =>
        answers is not null && ValueTemplate.Lookup(answers)(name) is { Length: > 0 } answer ? answer : null;

    private static bool IsFact(string name) =>
        !string.Equals(name, MachineVariableNames.ComputerName, StringComparison.OrdinalIgnoreCase)
        && MachineVariableNames.Catalogue.Keys.Any(fact => string.Equals(fact, name, StringComparison.OrdinalIgnoreCase));

    private static ResolvedValue Resolved(string name, Candidate candidate, string value, bool overridden) =>
        new(name, value, candidate.Source, candidate.SourceId, candidate.SourceName, overridden);

    private sealed record Candidate(string Name, string Text, bool Template, ValueSource Source, Guid? SourceId, string? SourceName);

    // Works out the values used, each once and on demand. It's a depth-first walk, and a loop shows up as a name it's
    // still working on. byName is keyed by each name as the page spells it, ignoring case.
    private sealed class Resolution(Dictionary<string, List<Candidate>> byName, MachineVariables? facts, List<ValueProblem> problems)
    {
        private readonly Dictionary<string, string?> _done = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _working = [];

        // Null when the value couldn't be worked out. Its problem is reported once, at the name that caused it.
        public string? Value(string name)
        {
            if (_done.TryGetValue(name, out string? done))
            {
                return done;
            }

            string spelled = byName.Keys.First(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
            int loop = _working.IndexOf(spelled);

            if (loop >= 0)
            {
                string path = string.Join(PathSeparator, [.. _working.Skip(loop), spelled]);
                problems.Add(new ValueProblem(spelled, ServerMessages.ValuesCycle.With("name", spelled, "path", path)));

                return null;
            }

            Candidate used = byName[spelled][0];
            _working.Add(spelled);
            string? value = used.Template ? Render(spelled, used.Text) : used.Text;
            _working.RemoveAt(_working.Count - 1);
            _done[spelled] = value;

            return value;
        }

        // What an overridden value would have been, for the page to show. It's the text as written when that can't be
        // worked out.
        public string Rendered(Candidate candidate) =>
            candidate.Template && ValueTemplate.TryRender(candidate.Text, Known, out string rendered, out _) ? rendered : candidate.Text;

        private string? Render(string name, string text)
        {
            // Every value the template uses is worked out first. A loop, or a value that couldn't be worked out, stops
            // this one without adding another problem.
            if (ValueTemplate.Parse(text).Names.Where(byName.ContainsKey).Any(used => Value(used) is null))
            {
                return null;
            }

            if (ValueTemplate.TryRender(text, Known, out string rendered, out TemplateProblem? problem))
            {
                return rendered;
            }

            problems.Add(new ValueProblem(name, ServerMessages.ValuesCannotWorkOut.With("name", name, "problem", problem!.Message())));

            return null;
        }

        // A value already worked out, or else a fact of the machine.
        private string? Known(string name) => byName.ContainsKey(name) ? _done.GetValueOrDefault(name) : facts?.Value(name);
    }
}
