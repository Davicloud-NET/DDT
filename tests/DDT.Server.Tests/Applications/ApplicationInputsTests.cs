// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Contracts.Sequences;
using DDT.Server.Applications;
using DDT.Server.Deployments;
using Xunit;

namespace DDT.Server.Tests.Applications;

// An Applications input goes out as a multiple choice of what it offers, so the web, the agent and the console ask it
// like any other, and its answers are checked the same way.
public sealed class ApplicationInputsTests
{
    private static readonly Application s_reader = new() { Id = Guid.NewGuid(), Name = "Reader", NormalizedName = "READER" };
    private static readonly ApplicationBundle s_office = new() { Id = Guid.NewGuid(), Name = "Office", NormalizedName = "OFFICE" };
    private static readonly Guid s_gone = Guid.NewGuid();

    private static readonly InputDeclaration s_extras = new()
    {
        Name = "Extras",
        Label = "Extras",
        Kind = InputKind.Applications,
        Offered =
        [
            new ApplicationReference(s_office.Id, ApplicationReferenceKind.Bundle),
            new ApplicationReference(s_gone, ApplicationReferenceKind.Application),
            new ApplicationReference(s_reader.Id, ApplicationReferenceKind.Application),
        ],
        Ticked = [s_gone, s_reader.Id],
    };

    private static readonly InputDeclaration s_owner = new() { Name = "Owner", Label = "Owner" };

    private static SequenceDefinition Asked(params InputDeclaration[] inputs) => ApplicationInputs.Asked(
        new SequenceDefinition(SequenceDefinition.CurrentVersion, []) { Inputs = inputs },
        new Dictionary<Guid, Application> { [s_reader.Id] = s_reader },
        new Dictionary<Guid, ApplicationBundle> { [s_office.Id] = s_office });

    [Fact]
    public void OffersTheEntriesStillInTheLibraryByIdWithTheirNamesAndTicksThoseThatAre()
    {
        InputDeclaration asked = Assert.Single(Asked(s_extras).Inputs ?? []);

        Assert.Equal(InputKind.MultiChoice, asked.Kind);
        Assert.Equal([new InputChoice(s_office.Id.ToString("D"), "Office"), new InputChoice(s_reader.Id.ToString("D"), "Reader")], asked.Choices);
        Assert.Equal(s_reader.Id.ToString("D"), asked.Default);
        Assert.Null(asked.Offered);
        Assert.Null(asked.Ticked);
    }

    [Fact]
    public void LeavesADefinitionWithoutOneAsItIs()
    {
        SequenceDefinition definition = new(SequenceDefinition.CurrentVersion, []) { Inputs = [s_owner] };

        Assert.Same(definition, ApplicationInputs.Asked(definition, new Dictionary<Guid, Application>(), new Dictionary<Guid, ApplicationBundle>()));
        Assert.Same(s_owner, Asked(s_extras, s_owner).Inputs?[1]);
    }

    [Fact]
    public void TakesIdsOfTheOfferedEntriesAndNothingElse()
    {
        SequenceDefinition asked = Asked(s_extras);
        string office = s_office.Id.ToString("D");

        Assert.Empty(RunValues.Check(asked, [new InputAnswer("Extras", $"{office.ToUpperInvariant()}; {s_reader.Id:D}")], _ => true));
        Assert.Equal(
            ["deployment.answerNotAChoice", "deployment.answerNotAChoice"],
            [
                .. RunValues.Check(asked, [new InputAnswer("Extras", "Reader")], _ => true)
                    .Concat(RunValues.Check(asked, [new InputAnswer("Extras", s_gone.ToString("D"))], _ => true))
                    .Select(problem => problem.Message.Code),
            ]);
    }

    [Fact]
    public void KeepsTheAnswerAsTheChoicesSpellIt()
    {
        RunAnswer answer = Assert.Single(RunValues.Merge(
            Asked(s_extras),
            [],
            [new InputAnswer("Extras", $"{s_reader.Id.ToString("D").ToUpperInvariant()};{s_office.Id:D};{s_reader.Id:D}")],
            new RunAnswerGiver("Technician", true, DateTimeOffset.UnixEpoch)));

        Assert.Equal($"{s_reader.Id:D};{s_office.Id:D}", answer.Value);
    }

    [Fact]
    public void AsksItAtTheMachineWithTheTickedEntriesFilledIn()
    {
        AgentInput input = RunValues.Asked(Assert.Single(Asked(s_extras).Inputs ?? []), null);

        Assert.Equal((InputKind.MultiChoice, s_reader.Id.ToString("D"), 2), (input.Kind, input.Default, input.Choices.Count));
    }
}
