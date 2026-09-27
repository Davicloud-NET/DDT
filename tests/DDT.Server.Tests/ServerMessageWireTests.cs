// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text.Json.Nodes;
using DDT.Contracts.Authentication;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using Xunit;

namespace DDT.Server.Tests;

// What the web reads to say the server's messages in the person's language: a code and its values beside the English.
public sealed class ServerMessageWireTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static async Task<JsonObject> JsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();

    [Fact]
    public async Task AProblemCarriesItsCodeAndValuesBesideItsTitle()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, RuleRequests.UniqueModel()));

        HttpResponseMessage refused = await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id}");
        JsonObject problem = await JsonAsync(refused);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            "A rule chooses this sequence. Delete the rule or let it choose another sequence, then delete this one.",
            (string?)problem["title"]);
        Assert.Equal("sequence.chosenByRules", (string?)problem["code"]);
        Assert.Equal("""{"count":1}""", problem["args"]!.ToJsonString());
    }

    [Fact]
    public async Task AValidationProblemCarriesTheCodesOfItsErrorsByField()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage refused = await administrator.CreateSequenceAsync(SequenceRequests.ScriptOnly(), name: " ");
        JsonObject problem = await JsonAsync(refused);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(
            """{"name":["The name must have 1 to 128 characters and no control characters."]}""",
            problem["errors"]!.ToJsonString());
        Assert.Equal(
            """{"name":[{"code":"common.nameLength","args":{"max":128}}]}""",
            problem["errorCodes"]!.ToJsonString());
    }

    [Fact]
    public async Task ASequenceProblemCarriesItsCodeAndValues()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceDefinition definition = SequenceRequests.Minimal(Guid.Empty);

        HttpResponseMessage validated = await administrator.PostAsync($"{SequenceRequests.Sequences}/validate", definition);
        JsonObject problem = (await JsonAsync(validated))["problems"]!.AsArray().Single()!.AsObject();

        Assert.Equal("Choose the image to apply.", (string?)problem["message"]);
        Assert.Equal("sequence.chooseImage", (string?)problem["code"]);
        Assert.Equal("{}", problem["args"]!.ToJsonString());
    }

    // The explanation names the rule in a message of its own, which the web says in the same language.
    [Fact]
    public async Task TheResolutionCarriesItsExplanationAsCodes()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        string model = RuleRequests.UniqueModel();
        await administrator.CreatedRuleAsync(RuleRequests.ModelRule(sequence.Id, model));
        using RegisteredMachine machine = await application.RegisterModelAsync(null, model);

        JsonObject resolution = await JsonAsync(await administrator.GetAsync($"/api/machines/{machine.Id}/sequence"));

        Assert.Equal(nameof(SequenceResolutionSource.ModelRule), (string?)resolution["source"]);
        Assert.StartsWith(
            $"The rule for model {model} of any maker chooses {sequence.Name}.",
            (string?)resolution["explanation"],
            StringComparison.Ordinal);
        Assert.Equal("resolution.ruleChooses", (string?)resolution["explanationCode"]);
        Assert.Equal(
            new JsonObject
            {
                ["rule"] = new JsonObject { ["code"] = "rule.forModelOfAnyMaker", ["args"] = new JsonObject { ["model"] = model } },
                ["sequence"] = sequence.Name,
            }.ToJsonString(),
            resolution["explanationArgs"]!.ToJsonString());
    }

    // Identity's own refusals get codes too, by Identity's code as the field.
    [Fact]
    public async Task APasswordIdentityRefusesCarriesItsCode()
    {
        using SignedInClient user = await application.SignInAsync(DdtRoleNames.Viewer);

        HttpResponseMessage refused = await user.PostAsync(
            "/api/auth/password",
            new ChangePasswordRequest(DdtApplication.Password, "short"));
        JsonObject problem = await JsonAsync(refused);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("identity.passwordTooShort", (string?)problem["errorCodes"]!["PasswordTooShort"]![0]!["code"]);
        Assert.Equal("""{"length":12}""", problem["errorCodes"]!["PasswordTooShort"]![0]!["args"]!.ToJsonString());
        Assert.Equal("Passwords must be at least 12 characters.", (string?)problem["errors"]!["PasswordTooShort"]![0]);
    }
}
