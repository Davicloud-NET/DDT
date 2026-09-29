// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Task sequences as trees. This covers the paths through groups, IFs and repeats, conditions, the sequence's
    // variables and inputs, templates, accounts and shares. In the messages, name is the name of a variable, an input
    // or a fact.

    public static readonly MessageTemplate SequenceNodeCount = Define(
        "sequence.nodeCount",
        "A sequence can have at most {max} steps, groups, IFs and repeats together.");

    public static readonly MessageTemplate SequenceTooDeep = Define(
        "sequence.tooDeep",
        "Groups, IFs and repeats can be nested at most {max} levels deep.");

    public static readonly MessageTemplate SequenceWindowsNeedsImageOnEveryPath = Define(
        "sequence.windowsNeedsImageOnEveryPath",
        "A step in Windows needs the image applied before it on every path through the sequence, and on some path no step applies it.");

    public static readonly MessageTemplate SequenceIfNeedsTest = Define("sequence.ifNeedsTest", "Enter the condition the IF tests.");

    public static readonly MessageTemplate SequenceIfOnlyTest = Define(
        "sequence.ifOnlyTest",
        "An IF decides by its test alone. Move this condition into the test.");

    public static readonly MessageTemplate SequenceRepeatNeedsUntil = Define(
        "sequence.repeatNeedsUntil",
        "Enter the condition that ends the repeat.");

    public static readonly MessageTemplate SequenceRepeatTimes = Define("sequence.repeatTimes", "A repeat runs 1 to {max} times.");

    public static readonly MessageTemplate SequenceOnceInRepeat = Define(
        "sequence.onceInRepeat",
        "This step can run only once in a run, so it cannot be inside a repeat.");

    public static readonly MessageTemplate SequencePhaseChangeInRepeat = Define(
        "sequence.phaseChangeInRepeat",
        "A run cannot change between Windows PE and Windows inside a repeat. Move this step, or the steps before it, out of the repeat.");

    public static readonly MessageTemplate SequenceConditionTooDeep = Define(
        "sequence.conditionTooDeep",
        "A condition can nest groups at most {max} levels deep.");

    public static readonly MessageTemplate SequenceTooManyTests = Define(
        "sequence.tooManyTests",
        "The conditions of a step can have at most {max} tests together.");

    public static readonly MessageTemplate SequenceConditionName = Define(
        "sequence.conditionName",
        "Choose a machine fact or a value to test.");

    public static readonly MessageTemplate SequenceOperatorDoesNotFit = Define(
        "sequence.operatorDoesNotFit",
        "This operator does not fit {name}, which holds {type, select, Number {a number} YesNo {yes or no} IPv4 {an IPv4 address} " +
        "Mac {a MAC address} other {plain text}}.");

    public static readonly MessageTemplate SequenceConditionNumber = Define(
        "sequence.conditionNumber",
        "{list, select, yes {Enter numbers separated by semicolons, such as 8192;16384.} other {Enter a number, such as 8192.}}");

    public static readonly MessageTemplate SequenceConditionYesNo = Define("sequence.conditionYesNo", "Enter true or false.");

    public static readonly MessageTemplate SequenceConditionIPv4 = Define(
        "sequence.conditionIPv4",
        "{list, select, yes {Enter IPv4 addresses separated by semicolons, such as 10.0.0.1;10.0.0.2.} " +
        "other {Enter an IPv4 address, such as 10.0.0.1.}}");

    public static readonly MessageTemplate SequenceConditionSubnet = Define(
        "sequence.conditionSubnet",
        "Enter a network as an address and the length of its prefix, such as 10.0.0.0/24.");

    public static readonly MessageTemplate SequenceDeclarationEmpty = Define("sequence.declarationEmpty", "The declaration is empty.");

    public static readonly MessageTemplate SequenceValueName = Define(
        "sequence.valueName",
        "A name starts with a letter and has at most {max} letters, digits and underscores.");

    public static readonly MessageTemplate SequenceValueNameReserved = Define(
        "sequence.valueNameReserved",
        "Names that start with DDT are kept for DDT's own values. Choose another name.");

    public static readonly MessageTemplate SequenceValueNameRepeated = Define(
        "sequence.valueNameRepeated",
        "{name} is declared already. Names ignore case.");

    public static readonly MessageTemplate SequenceTooManyVariables = Define(
        "sequence.tooManyVariables",
        "A sequence can have at most {max} variables.");

    public static readonly MessageTemplate SequenceTooManyInputs = Define(
        "sequence.tooManyInputs",
        "A sequence can ask at most {max} inputs.");

    public static readonly MessageTemplate SequenceInputLabel = Define("sequence.inputLabel", "Enter a label of 1 to {max} characters.");

    public static readonly MessageTemplate SequenceInputKind = Define("sequence.inputKind", "Choose the kind of answer.");

    public static readonly MessageTemplate SequenceInputAskAt = Define(
        "sequence.inputAskAt",
        "Choose where the input is asked: on the web, at the machine, or both.");

    public static readonly MessageTemplate SequenceInputChoices = Define("sequence.inputChoices", "Enter 1 to {max} choices.");

    public static readonly MessageTemplate SequenceInputChoiceEmpty = Define("sequence.inputChoiceEmpty", "Enter the value of the choice.");

    public static readonly MessageTemplate SequenceInputChoiceRepeated = Define(
        "sequence.inputChoiceRepeated",
        "Another choice already has the value {value}.");

    public static readonly MessageTemplate SequenceInputChoiceSemicolon = Define(
        "sequence.inputChoiceSemicolon",
        "A choice of an input with several answers cannot hold a semicolon, which separates the answers.");

    public static readonly MessageTemplate SequenceInputDefaultNotChoice = Define(
        "sequence.inputDefaultNotChoice",
        "The default has to be one of the choices.");

    public static readonly MessageTemplate SequenceInputMaxLength = Define(
        "sequence.inputMaxLength",
        "The longest answer can have 1 to {max} characters.");

    public static readonly MessageTemplate SequenceAccountInputAsValue = Define(
        "sequence.accountInputAsValue",
        "{name} is an Account input. Its answer holds a password, which DDT never uses as a value.");

    public static readonly MessageTemplate SequenceSetVariableChoose = Define("sequence.setVariableChoose", "Choose the variable to set.");

    public static readonly MessageTemplate SequenceVariableNotDeclared = Define(
        "sequence.variableNotDeclared",
        "{name} is not a variable of this sequence. Declare it first.");

    public static readonly MessageTemplate SequenceVariableNotSetBySteps = Define(
        "sequence.variableNotSetBySteps",
        "Steps cannot set {name}. Let steps set it where the variable is declared.");

    public static readonly MessageTemplate SequenceAccountChoose = Define(
        "sequence.accountChoose",
        "Choose a stored account or an Account input.");

    public static readonly MessageTemplate SequenceAccountInputUnknown = Define(
        "sequence.accountInputUnknown",
        "{name} is not an Account input of this sequence.");

    public static readonly MessageTemplate SequenceRunAsWindowsOnly = Define(
        "sequence.runAsWindowsOnly",
        "A script runs as an account only in Windows. In Windows PE it runs as SYSTEM.");

    public static readonly MessageTemplate SequenceTooManyShares = Define(
        "sequence.tooManyShares",
        "A step can connect at most {max} shares.");

    public static readonly MessageTemplate SequenceSharePath = Define(
        "sequence.sharePath",
        "Enter the share as \\\\host\\share, such as \\\\files.example.com\\drivers.");

    public static readonly MessageTemplate SequenceShareHostFixed = Define(
        "sequence.shareHostFixed",
        "The host of a share can be made only of values fixed when the run starts, and {name} can change while it runs.");

    public static readonly MessageTemplate SequenceSharesOnlyOnSteps = Define(
        "sequence.sharesOnlyOnSteps",
        "Only a step connects shares, for as long as it runs. Give them to the steps inside that need them.");

    public static readonly MessageTemplate SequencePauseMinutes = Define(
        "sequence.pauseMinutes",
        "A pause can go on by itself after 1 to {max} minutes.");

    public static readonly MessageTemplate SequenceNamesMachineWithValue = Define(
        "sequence.namesMachineWithValue",
        "The sequence names the machine by its ComputerName variable.");

    public static readonly MessageTemplate SequencePauseBeforePartitionWarning = Define(
        "sequence.pauseBeforePartitionWarning",
        "The run's state is kept only in memory here, so the run ends if the machine restarts while it is paused.");

    public static readonly MessageTemplate SequenceEmptyContainerWarning = Define(
        "sequence.emptyContainerWarning",
        "{kind, select, if {This IF has no steps in Then or Else} repeat {This repeat has no steps} other {This group has no steps}}, " +
        "so it does nothing.");

    public static readonly MessageTemplate SequenceSecretValueWarning = Define(
        "sequence.secretValueWarning",
        "{name} looks like a password or another secret, and everyone who can sign in to DDT can read the values of sequences " +
        "and runs. Keep it in an account, stored or asked for the run, which only the step that uses it gets.");

    public static readonly MessageTemplate SequenceShareHostAddressWarning = Define(
        "sequence.shareHostAddressWarning",
        "{host} is an IP address, with which Windows cannot use Kerberos, so the account signs in with NTLM, which another " +
        "machine on the network can relay. Name the server instead, best by its full DNS name.");

    // Value templates, such as PC-{{SerialNumber|alnum|right:12}}. Placeholder is the whole placeholder, including the
    // braces.

    public static readonly MessageTemplate ValueTemplateUnknownName = Define(
        "valueTemplate.unknownName",
        "{placeholder} uses {name}, which is not a machine fact or a declared value. Check the spelling.");

    public static readonly MessageTemplate ValueTemplateUnknownFilter = Define(
        "valueTemplate.unknownFilter",
        "{placeholder} uses the filter ''{filter}'', which DDT does not have. The filters are {filters}.");

    public static readonly MessageTemplate ValueTemplateFilterNeedsCount = Define(
        "valueTemplate.filterNeedsCount",
        "In {placeholder}, {filter} needs a number of characters from 1 to {max}, such as {filter}:12.");

    public static readonly MessageTemplate ValueTemplateFilterTakesNoCount = Define(
        "valueTemplate.filterTakesNoCount",
        "In {placeholder}, {filter} takes no number. Remove the colon and what follows it.");

    public static readonly MessageTemplate ValueTemplateNoValue = Define(
        "valueTemplate.noValue",
        "The machine has no value for {placeholder}.");

    // The values of a run, worked out when it starts. Name is the value's name, such as ComputerName.

    public static readonly MessageTemplate ValuesCannotWorkOut = Define(
        "values.cannotWorkOut",
        "{name} cannot be worked out. {problem}");

    public static readonly MessageTemplate ValuesCycle = Define(
        "values.cycle",
        "{name} cannot be worked out, because it is made from itself: {path}.");

    public static readonly MessageTemplate ValuesComputerName = Define(
        "values.computerName",
        "The computer name ''{value}'' cannot be used. {problem}");

    public static readonly MessageTemplate ValuesInputRequired = Define(
        "values.inputRequired",
        "{label} needs an answer before the run can start.");

    public static readonly MessageTemplate ValuesFact = Define(
        "values.fact",
        "{name} is a fact of the machine, which a value cannot set.");
}
