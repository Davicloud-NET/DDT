// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;
using DDT.Contracts.Applications;
using DDT.Contracts.Messages;

namespace DDT.Core.Applications;

// What an application or a bundle must look like before the server stores it. The server checks the rest: unique
// names, that the files and dependencies exist, and that the dependencies make no circle. The request comes from
// outside, so members declared non-null can still be null.
public static partial class ApplicationRules
{
    // id is the application's own, or null for a new one.
    public static IReadOnlyList<ApplicationProblem> Check(SaveApplicationRequest request, Guid? id)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<ApplicationProblem> problems = [];
        void Add(string field, ServerMessage message) => problems.Add(new ApplicationProblem(field, message));

        CheckNameAndDescription(request.Name, request.Description, Add);
        CheckShortText(request.Publisher, ApplicationLimits.MaxPublisherLength, "publisher", ServerMessages.ApplicationPublisherLength, Add);
        CheckShortText(request.Version, ApplicationLimits.MaxVersionLength, "version", ServerMessages.ApplicationVersionLength, Add);

        if (Enum.IsDefined(request.Kind))
        {
            CheckKind(request, Add);
        }
        else
        {
            Add("kind", ServerMessages.ApplicationKindChoose.With());
        }

        CheckExitCodes(request.SuccessExitCodes, request.RestartExitCodes, Add);
        ApplicationDetectionRules.Check(request.Detection, Add);
        CheckDependencies(request.DependsOn ?? [], id, Add);

        if (request.TimeoutMinutes is < ApplicationLimits.MinTimeoutMinutes or > ApplicationLimits.MaxTimeoutMinutes)
        {
            Add("timeoutMinutes", ServerMessages.SequenceScriptTimeout.With("max", ApplicationLimits.MaxTimeoutMinutes));
        }

        return problems;
    }

    public static IReadOnlyList<ApplicationProblem> Check(SaveApplicationBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<ApplicationProblem> problems = [];
        void Add(string field, ServerMessage message) => problems.Add(new ApplicationProblem(field, message));

        CheckNameAndDescription(request.Name, request.Description, Add);

        IReadOnlyList<Guid> members = request.Members ?? [];

        if (members.Count is 0 or > ApplicationLimits.MaxBundleMembers)
        {
            Add("members", ServerMessages.ApplicationBundleMembers.With("max", ApplicationLimits.MaxBundleMembers));
        }
        else
        {
            CheckList(members, "members", null, Add);
        }

        return problems;
    }

    public static bool IsWingetId(string? value) =>
        value is { Length: > 0 and <= ApplicationLimits.MaxWingetIdLength } && WingetId().IsMatch(value);

    private static void CheckNameAndDescription(string? name, string? description, Action<string, ServerMessage> add)
    {
        if (ApplicationNames.Problem(name) is { } problem)
        {
            add("name", problem);
        }

        if (description?.Trim().Length > ApplicationLimits.MaxDescriptionLength)
        {
            add("description", ServerMessages.DescriptionLength.With("max", ApplicationLimits.MaxDescriptionLength));
        }
    }

    private static void CheckShortText(string? value, int max, string field, MessageTemplate message, Action<string, ServerMessage> add)
    {
        if (value is not null && (value.Trim().Length > max || value.Any(char.IsControl)))
        {
            add(field, message.With("max", max));
        }
    }

    // A command runs in the files' folder, so a Command application may have files or not. MSIX needs its package and
    // winget nothing but the ID.
    private static void CheckKind(SaveApplicationRequest request, Action<string, ServerMessage> add)
    {
        bool hasCommand = !string.IsNullOrWhiteSpace(request.Command);
        bool hasFiles = request.PackageId is { } package && package != Guid.Empty;

        if (request.Kind == ApplicationKind.Command)
        {
            CheckCommand(request.Command, add);
        }
        else if (hasCommand)
        {
            add("command", ServerMessages.ApplicationCommandNotUsed.With("kind", request.Kind));
        }

        if (request.Kind == ApplicationKind.Msix && !hasFiles)
        {
            add("packageId", ServerMessages.ApplicationFilesRequired.With());
        }
        else if (request.Kind == ApplicationKind.Winget && request.PackageId is not null)
        {
            add("packageId", ServerMessages.ApplicationFilesNotUsed.With());
        }

        if (request.Kind == ApplicationKind.Winget)
        {
            CheckWinget(request.WingetId, request.WingetVersion, add);
        }
        else if (!string.IsNullOrWhiteSpace(request.WingetId) || !string.IsNullOrWhiteSpace(request.WingetVersion))
        {
            add("wingetId", ServerMessages.ApplicationWingetNotUsed.With());
        }
    }

    // One line, because the agent starts it as one command line. A tab is allowed.
    private static void CheckCommand(string? command, Action<string, ServerMessage> add)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            add("command", ServerMessages.ApplicationCommandRequired.With());
        }
        else if (command.Trim().Length > ApplicationLimits.MaxCommandLength || command.Any(character => character != '\t' && char.IsControl(character)))
        {
            add("command", ServerMessages.ApplicationCommandLength.With("max", ApplicationLimits.MaxCommandLength));
        }
    }

    private static void CheckWinget(string? id, string? version, Action<string, ServerMessage> add)
    {
        if (!IsWingetId(id?.Trim()))
        {
            add("wingetId", ServerMessages.ApplicationWingetId.With("max", ApplicationLimits.MaxWingetIdLength));
        }

        if (version is not null
            && version.Trim() is { Length: > 0 } trimmed
            && (trimmed.Length > ApplicationLimits.MaxWingetVersionLength || trimmed.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))))
        {
            add("wingetVersion", ServerMessages.ApplicationWingetVersion.With("max", ApplicationLimits.MaxWingetVersionLength));
        }
    }

    private static void CheckExitCodes(IReadOnlyList<int>? success, IReadOnlyList<int>? restart, Action<string, ServerMessage> add)
    {
        if (success is not { Count: >= 1 and <= ApplicationLimits.MaxExitCodes })
        {
            add("successExitCodes", ServerMessages.SequenceSuccessExitCodes.With("max", ApplicationLimits.MaxExitCodes));
        }

        if (restart is not { Count: <= ApplicationLimits.MaxExitCodes })
        {
            add("restartExitCodes", ServerMessages.SequenceRebootExitCodes.With("max", ApplicationLimits.MaxExitCodes));

            return;
        }

        if (success is not null && success.Intersect(restart).ToArray() is { Length: > 0 } both)
        {
            add("restartExitCodes", ServerMessages.SequenceExitCodeMeansBoth.With("codes", string.Join(", ", both)));
        }
    }

    private static void CheckDependencies(IReadOnlyList<Guid> dependencies, Guid? id, Action<string, ServerMessage> add)
    {
        if (dependencies.Count > ApplicationLimits.MaxDependencies)
        {
            add("dependsOn", ServerMessages.ApplicationTooManyDependencies.With("max", ApplicationLimits.MaxDependencies));

            return;
        }

        CheckList(dependencies, "dependsOn", id, add);
    }

    // Each id once. An empty id names nothing in the library.
    private static void CheckList(IReadOnlyList<Guid> ids, string field, Guid? self, Action<string, ServerMessage> add)
    {
        HashSet<Guid> seen = [];

        for (int index = 0; index < ids.Count; index++)
        {
            string at = string.Create(CultureInfo.InvariantCulture, $"{field}[{index}]");
            Guid id = ids[index];

            if (id == Guid.Empty)
            {
                add(at, ServerMessages.ApplicationGone.With());
            }
            else if (id == self)
            {
                add(at, ServerMessages.ApplicationDependsOnItself.With());
            }
            else if (!seen.Add(id))
            {
                add(at, ServerMessages.ApplicationListedTwice.With());
            }
        }
    }

    // winget's PackageIdentifier: 1 to 8 parts of up to 32 characters, split by dots. One part is a Microsoft Store ID
    // such as 9NBLGGH4NNS1.
    [GeneratedRegex(@"^[^.\s\\/:*?""<>|\p{Cc}]{1,32}(?:\.[^.\s\\/:*?""<>|\p{Cc}]{1,32}){0,7}\z", RegexOptions.CultureInvariant)]
    private static partial Regex WingetId();
}
