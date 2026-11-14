// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.BitLocker;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// manage-bde's answers as the spike saw them on English and German Windows 11, with made-up passwords.
public sealed class ManageBdeBitLockerTests
{
    private const string ManageBde = @"C:\Windows\System32\manage-bde.exe";
    private const string PowerShell = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    private const string Password = "111111-222222-333333-444444-555555-666666-707707-121121";
    private const string Id = "{748DC9C3-3024-4646-9069-B9EFBEED68E4}";

    private static readonly string[] s_english =
    [
        "BitLocker Drive Encryption: Configuration Tool version 10.0.26100",
        "Copyright (C) 2013 Microsoft Corporation. All rights reserved.",
        "",
        "Key Protectors Added:",
        "",
        "    Numerical Password:",
        $"      ID: {Id}",
        "      Password:",
        $"        {Password}",
        "",
        "ACTIONS REQUIRED:",
        "",
        "    1. Save this numerical recovery password in a secure location away from",
        "    your computer:",
        "",
        $"    {Password}",
    ];

    private static readonly string[] s_german =
    [
        "BitLocker-Laufwerkverschlüsselung: Konfigurationstool, Version 10.0.26100",
        "Hinzugefügte Schlüsselschutzvorrichtungen:",
        "    Numerisches Kennwort:",
        $"      ID: {Id}",
        "      Kennwort:",
        $"        {Password}",
        "ERFORDERLICHE AKTIONEN:",
        "    1. Bewahren Sie dieses numerische Wiederherstellungskennwort an einem sicheren Ort auf:",
        $"    {Password}",
    ];

    private readonly RecordingToolRunner _tools = new();

    private ManageBdeBitLocker BitLocker => new(_tools, ManageBde, PowerShell);

    public static TheoryData<string> Languages => ["English", "German"];

    private void Answer(int exitCode, params string[] lines) =>
        _tools.AnswerExitCode = (_, _, options) =>
        {
            foreach (string line in lines)
            {
                options.Private?.Add(line);
            }

            return exitCode;
        };

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task ReadsTheNewPasswordAndItsProtectorWithoutLoggingThem(string language)
    {
        Answer(0, language == "German" ? s_german : s_english);

        RecoveryPassword? added = await BitLocker.AddRecoveryPasswordAsync("C:", TestContext.Current.CancellationToken);

        Assert.Equal(new RecoveryPassword(Guid.Parse(Id), Password), added);
        Assert.Equal([$"{ManageBde} -protectors -add C: -recoverypassword"], _tools.Calls);
        Assert.NotNull(Assert.Single(_tools.Options).Private);
        Assert.DoesNotContain(Password, added?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedAddNamesTheExitCodeAndNoPassword()
    {
        Answer(5, s_english);

        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(() => BitLocker.AddRecoveryPasswordAsync("C:", TestContext.Current.CancellationToken));

        Assert.Equal("manage-bde could not add a recovery password on C: (exit code 0x00000005).", failure.Message);
    }

    [Fact]
    public async Task AnAnswerWithTwoPasswordsIsNotGuessedAt()
    {
        Answer(0, [.. s_english, "    222222-111111-333333-444444-555555-666666-707707-121121"]);

        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(() => BitLocker.AddRecoveryPasswordAsync("C:", TestContext.Current.CancellationToken));

        Assert.DoesNotContain("111111", failure.Message, StringComparison.Ordinal);
        Assert.Contains("2 passwords and 1 protector IDs", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(unchecked((int)0x8028400F))]
    [InlineData(unchecked((int)0x80284008))]
    public async Task NoTpmAddsNothing(int exitCode)
    {
        Answer(exitCode);

        Assert.Null(await BitLocker.AddRecoveryPasswordAsync("C:", TestContext.Current.CancellationToken));
        Assert.False(await BitLocker.AddTpmProtectorAsync("C:", TestContext.Current.CancellationToken));
        Assert.False(await BitLocker.PreprovisionAsync("W:", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListsTheRecoveryPasswordsByIdWithoutLoggingThem()
    {
        Answer(0, "Volume C: []", "    Numerical Password:", $"      ID: {Id}", "      Password:", $"        {Password}");

        IReadOnlyList<Guid> ids = await BitLocker.RecoveryPasswordIdsAsync("C:", TestContext.Current.CancellationToken);

        Assert.Equal([Guid.Parse(Id)], ids);
        Assert.NotNull(Assert.Single(_tools.Options).Private);
    }

    [Fact]
    public async Task NoProtectorEndsWithMinusOne()
    {
        Answer(-1);

        Assert.Empty(await BitLocker.RecoveryPasswordIdsAsync("C:", TestContext.Current.CancellationToken));
        Assert.False(await BitLocker.HasTpmProtectorAsync("C:", TestContext.Current.CancellationToken));
    }

    // The other commands print no password, and a line that looks like one is dropped all the same.
    [Fact]
    public async Task TheLoggedCommandsDropALineThatLooksLikeAPassword()
    {
        await BitLocker.TurnOnAsync("C:", TestContext.Current.CancellationToken);

        Func<string, string?> lines = Assert.IsType<Func<string, string?>>(Assert.Single(_tools.Options).Lines);
        Assert.Null(lines($"    {Password}"));
        Assert.Equal("NOTE: Encryption is already in progress.", lines("NOTE: Encryption is already in progress."));
        Assert.Null(Assert.Single(_tools.Options).Private);
    }

    [Fact]
    public async Task RunsEachCommandAsWritten()
    {
        Guid id = Guid.Parse(Id);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await BitLocker.PreprovisionAsync("W:", cancellationToken);
        await BitLocker.HasTpmProtectorAsync("C:", cancellationToken);
        await BitLocker.DeleteProtectorAsync("C:", id, cancellationToken);
        await BitLocker.AddTpmProtectorAsync("C:", cancellationToken);
        await BitLocker.BackupToActiveDirectoryAsync("C:", id, cancellationToken);
        await BitLocker.TurnOnAsync("C:", cancellationToken);

        Assert.Equal(
            [
                $"{ManageBde} -on W: -used",
                $"{ManageBde} -protectors -get C: -type TPM",
                $"{ManageBde} -protectors -delete C: -id {Id}",
                $"{ManageBde} -protectors -add C: -tpm",
                $"{ManageBde} -protectors -adbackup C: -id {Id}",
                $"{ManageBde} -on C: -used -skiphardwaretest",
            ],
            _tools.Calls);
    }

    [Fact]
    public async Task AFailedCommandNamesTheExitCode()
    {
        Answer(unchecked((int)0x8031006A));

        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(
            () => BitLocker.BackupToActiveDirectoryAsync("C:", Guid.Parse(Id), TestContext.Current.CancellationToken));

        Assert.Equal(
            "manage-bde could not back up the recovery password to Active Directory on C: (exit code 0x8031006A). Its output is in the machine log.",
            failure.Message);
    }

    [Fact]
    public async Task ReadsTheStateAsNumbers()
    {
        Answer(0, "status:2 74 0");

        VolumeStatus status = await BitLocker.StatusAsync("C:", TestContext.Current.CancellationToken);

        Assert.Equal(new VolumeStatus(2, 74, false), status);
        Assert.StartsWith($"{PowerShell} -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command", Assert.Single(_tools.Calls), StringComparison.Ordinal);
        Assert.Contains("DriveLetter='C:'", ManageBdeBitLocker.StatusArguments("C:")[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("status:1 100 1", true)]
    [InlineData("status:1 100 0", false)]
    [InlineData("status:2 99 1", false)]
    public void IsDoneWhenFullyEncryptedAndProtected(string line, bool done) =>
        Assert.Equal(done, ManageBdeBitLocker.ParseStatus([line])?.IsDone);

    [Fact]
    public void AStateWithoutItsNumbersIsNone() =>
        Assert.Null(ManageBdeBitLocker.ParseStatus(["status:2 seventy 0"]));
}
