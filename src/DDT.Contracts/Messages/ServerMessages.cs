// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

// Every sentence the server says to a person on the web, by a code that is part of the API: a sentence that says
// something else gets a new code. The web's catalog is written from All; logs, audits and the agent stay English.
public static partial class ServerMessages
{
    // Keeps the type from being beforefieldinit, so Find and All load every part first. ServerMessage.Text calls Find
    // on messages read from JSON, before anything else may have touched this class.
    static ServerMessages()
    {
    }

    public static IReadOnlyList<MessageTemplate> All => Catalog.Templates;

    public static MessageTemplate? Find(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return Catalog.ByCode.GetValueOrDefault(code);
    }

    // People and things named within other sentences.

    public static readonly MessageTemplate SomeOperator = Define("person.someOperator", "An operator");

    // A machine's state and a person's role, as the server names them within a sentence.

    public static readonly MessageTemplate MachineStatePending = Define("machineState.pending", "Pending");

    public static readonly MessageTemplate MachineStateApproved = Define("machineState.approved", "Approved");

    public static readonly MessageTemplate MachineStateDeploying = Define("machineState.deploying", "Deploying");

    public static readonly MessageTemplate MachineStateDone = Define("machineState.done", "Done");

    public static readonly MessageTemplate MachineStateFailed = Define("machineState.failed", "Failed");

    public static readonly MessageTemplate MachineStateRejected = Define("machineState.rejected", "Rejected");

    public static readonly MessageTemplate MachineStateRetired = Define("machineState.retired", "Retired");

    public static readonly MessageTemplate RoleAdministrator = Define("role.administrator", "Administrator");

    public static readonly MessageTemplate RoleOperator = Define("role.operator", "Operator");

    public static readonly MessageTemplate RoleViewer = Define("role.viewer", "Viewer");

    public static readonly MessageTemplate RoleAnAdministrator = Define("role.anAdministrator", "an Administrator");

    public static readonly MessageTemplate RoleAnOperator = Define("role.anOperator", "an Operator");

    public static readonly MessageTemplate RoleAViewer = Define("role.aViewer", "a Viewer");

    public static readonly MessageTemplate RuleForMac = Define("rule.forMac", "MAC address {mac}");

    public static readonly MessageTemplate RuleForModel = Define("rule.forModel", "model {manufacturer} {model}");

    public static readonly MessageTemplate RuleForModelOfAnyMaker = Define("rule.forModelOfAnyMaker", "model {model} of any maker");

    public static readonly MessageTemplate MicrosoftUefiCaName = Define(
        "uefiCa.name",
        "{cas, select, ca2011 {Microsoft's third-party UEFI CA 2011} ca2023 {Microsoft's third-party UEFI CA 2023} " +
        "both {Microsoft's third-party UEFI CAs 2011 and 2023} other {Microsoft's third-party UEFI CA}}");

    // Names and descriptions of the things in the library.

    public static readonly MessageTemplate NameLength = Define(
        "common.nameLength",
        "The name must have 1 to {max} characters and no control characters.");

    public static readonly MessageTemplate DescriptionLength = Define(
        "common.descriptionLength",
        "The description can have at most {max} characters.");

    public static readonly MessageTemplate MacEnterFull = Define(
        "mac.enterFull",
        "Enter a MAC address of 12 hex digits, such as 00:15:5D:01:02:03.");

    public static readonly MessageTemplate MacEnterPart = Define(
        "mac.enterPart",
        "Enter 1 to 12 hex digits of a MAC address, such as 00:15:5D.");

    // Several sentences said as one, such as the problems of a settings section within a sentence that lists them, and
    // a problem after the field it is about. See Sentences.

    public static readonly MessageTemplate CommonSentences = Define("common.sentences", "{first} {rest}");

    public static readonly MessageTemplate SettingsFieldProblem = Define("settings.fieldProblem", "{field}: {problem}");

    // The sentences one after another, as one message, which the web says in the person's language as a whole: the
    // first, then the rest as a message of its own.
    public static ServerMessage Sentences(IReadOnlyList<ServerMessage> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        ArgumentOutOfRangeException.ThrowIfZero(sentences.Count);

        ServerMessage said = sentences[^1];

        for (int index = sentences.Count - 2; index >= 0; index--)
        {
            said = CommonSentences.With("first", sentences[index], "rest", said);
        }

        return said;
    }

    private static MessageTemplate Define(string code, string english)
    {
        MessageTemplate template = new(code, english);
        Catalog.ByCode.Add(code, template);
        Catalog.Templates.Add(template);

        return template;
    }

    // Parts initialise in no set order, so the lists live here and exist before any part's first Define.
    private static class Catalog
    {
        public static readonly Dictionary<string, MessageTemplate> ByCode = new(StringComparer.Ordinal);

        public static readonly List<MessageTemplate> Templates = [];
    }
}
