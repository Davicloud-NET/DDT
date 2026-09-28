// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Assignment rules, and the models that rules and driver packages match.

    public static readonly MessageTemplate RuleChooseKind = Define("rule.chooseKind", "Choose a rule by MAC address or by model.");

    public static readonly MessageTemplate RuleSequenceGone = Define(
        "rule.sequenceGone",
        "The sequence no longer exists. Choose another one.");

    public static readonly MessageTemplate RuleExists = Define(
        "rule.exists",
        "There is a rule for {rule} already. It chooses {sequence}; change that rule instead.");

    // The ordered rules, the machine roles, and the values both of them set. A rule is saved with its problems, which
    // keep it from matching until they're fixed. A machine role with a problem is refused.

    public static readonly MessageTemplate RuleTooMany = Define(
        "rule.tooMany",
        "There can be at most {max} rules. Delete one before adding another.");

    public static readonly MessageTemplate RuleConditionTooLarge = Define(
        "rule.conditionTooLarge",
        "A rule's condition can have at most {tests} tests, with groups nested at most {depth} deep.");

    public static readonly MessageTemplate RuleTooManyRoles = Define("rule.tooManyRoles", "A rule can give at most {max} machine roles.");

    public static readonly MessageTemplate RuleOrderRepeats = Define("rule.orderRepeats", "The order names a rule more than once.");

    public static readonly MessageTemplate RuleConditionUnreadable = Define(
        "rule.conditionUnreadable",
        "DDT cannot read this rule's condition. Build it again.");

    public static readonly MessageTemplate RuleConditionChooseName = Define("rule.conditionChooseName", "Choose a fact or a value to test.");

    public static readonly MessageTemplate RuleConditionUnknownName = Define(
        "rule.conditionUnknownName",
        "{name} is not a fact of the machine or a value that a rule or a machine role sets. Check the spelling.");

    public static readonly MessageTemplate RuleConditionRunVariable = Define(
        "rule.conditionRunVariable",
        "{name} has a value only while a run goes on, so a rule cannot test it.");

    public static readonly MessageTemplate RuleConditionOperatorType = Define(
        "rule.conditionOperatorType",
        "This comparison does not fit {name}, which holds {type, select, number {a number} yesNo {yes or no} ipv4 {an IPv4 address} " +
        "mac {a MAC address} other {any text}}.");

    public static readonly MessageTemplate RuleConditionNumber = Define("rule.conditionNumber", "Enter a number, such as 8192.");

    public static readonly MessageTemplate RuleConditionYesNo = Define("rule.conditionYesNo", "Enter yes or no.");

    public static readonly MessageTemplate RuleConditionAddress = Define(
        "rule.conditionAddress",
        "Enter an IPv4 address, such as 10.0.0.1.");

    public static readonly MessageTemplate RuleConditionSubnet = Define(
        "rule.conditionSubnet",
        "Write the network as an address and a prefix length, such as 10.0.0.0/24.");

    public static readonly MessageTemplate RuleRoleGone = Define(
        "rule.roleGone",
        "A machine role this rule gives no longer exists. Take it out of the rule.");

    public static readonly MessageTemplate NamedValueTooMany = Define("namedValue.tooMany", "Set at most {max} values here.");

    public static readonly MessageTemplate NamedValueTooLong = Define(
        "namedValue.tooLong",
        "A value's name can have at most {name} characters, and its text at most {text}.");

    public static readonly MessageTemplate NamedValueNameEmpty = Define("namedValue.nameEmpty", "Enter the value's name.");

    public static readonly MessageTemplate NamedValueNameInvalid = Define(
        "namedValue.nameInvalid",
        "''{name}'' cannot name a value. Start with a letter, and use only letters, digits and _, at most {max} characters.");

    public static readonly MessageTemplate NamedValueReserved = Define(
        "namedValue.reserved",
        "Names that start with ddt are DDT's own. Choose another name.");

    public static readonly MessageTemplate NamedValueRepeated = Define("namedValue.repeated", "{name} is set more than once here. Keep one.");

    public static readonly MessageTemplate MachineRoleNameTaken = Define(
        "machineRole.nameTaken",
        "Another machine role is already called {name}. Choose another name.");

    public static readonly MessageTemplate MachineRoleTooMany = Define(
        "machineRole.tooMany",
        "There can be at most {max} machine roles. Delete one before adding another.");

    public static readonly MessageTemplate MachineRoleGivenByRules = Define(
        "machineRole.givenByRules",
        "{count, plural, one {A rule gives this machine role. Take it out of the rule, then delete the role.} " +
        "other {# rules give this machine role. Take it out of them, then delete the role.}}");

    public static readonly MessageTemplate ResolutionRuleNumbered = Define(
        "resolution.ruleNumbered",
        "Rule {number}, {rule}, chooses {sequence}. A rule only chooses: the machine still needs an approval on the web, or someone " +
        "who signs in at it, where the sequence is offered.");

    public static readonly MessageTemplate ResolutionNoRuleChooses = Define(
        "resolution.noRuleChooses",
        "No rule chooses a sequence for this machine, so an operator chooses its sequence.");

    public static readonly MessageTemplate ModelEnter = Define("model.enter", "Enter the model as the machine reports it.");

    public static readonly MessageTemplate ModelNameLength = Define(
        "model.nameLength",
        "A name here has at most {max} characters and no control characters.");

    public static readonly MessageTemplate ModelWildcardLast = Define("model.wildcardLast", "Only the last character can be {wildcard}.");

    public static readonly MessageTemplate ModelManufacturerWhole = Define(
        "model.manufacturerWhole",
        "A manufacturer is matched whole, without {wildcard}.");

    public static readonly MessageTemplate ModelWildcardPrefix = Define(
        "model.wildcardPrefix",
        "Put at least {min} characters before {wildcard}, so it matches only one family of models.");

    public static readonly MessageTemplate ModelPlaceholder = Define(
        "model.placeholder",
        "{value} is what firmware reports when the field was never filled in. It says nothing about the machine.");
}
