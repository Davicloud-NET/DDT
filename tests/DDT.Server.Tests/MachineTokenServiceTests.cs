using DDT.Server.Machines;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class MachineTokenServiceTests
{
    private static readonly MachineTokenService s_tokens = new(new EphemeralDataProtectionProvider());

    private static Machine NewMachine(int generation = 1) => new()
    {
        Id = Guid.Parse("7b0f7a4e-6a1e-4f0e-9a1b-1d2c3e4f5a6b"),
        SmbiosUuid = "4C4C4544-0031-3010-8054-B7C04F503332",
        PrimaryMac = "00:15:5D:01:02:03",
        TokenGeneration = generation,
    };

    [Fact]
    public void IssuedTokenRoundTripsWithTheMachineIdentity()
    {
        Machine machine = NewMachine();

        MachineTokenPayload? payload = s_tokens.Validate(
            s_tokens.Issue(machine, MachineTokenPurpose.Session),
            MachineTokenPurpose.Session);

        Assert.NotNull(payload);
        Assert.Equal(machine.Id, payload.MachineId);
        Assert.Equal(machine.SmbiosUuid, payload.SmbiosUuid);
        Assert.Equal(machine.PrimaryMac, payload.PrimaryMac);
        Assert.Equal(machine.TokenGeneration, payload.TokenGeneration);
    }

    [Theory]
    [InlineData(MachineTokenPurpose.Poll, MachineTokenPurpose.Session)]
    [InlineData(MachineTokenPurpose.Session, MachineTokenPurpose.Resume)]
    [InlineData(MachineTokenPurpose.Resume, MachineTokenPurpose.Session)]
    public void ATokenIsNotValidForADifferentPurpose(MachineTokenPurpose issued, MachineTokenPurpose presented)
    {
        string token = s_tokens.Issue(NewMachine(), issued);

        Assert.Null(s_tokens.Validate(token, presented));
    }

    [Fact]
    public void ATamperedTokenIsRejected()
    {
        string token = s_tokens.Issue(NewMachine(), MachineTokenPurpose.Session);
        string tampered = token[..^4] + (token.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        Assert.Null(s_tokens.Validate(tampered, MachineTokenPurpose.Session));
    }

    [Fact]
    public void TokenGenerationTravelsWithTheTokenSoItCanBeRevoked()
    {
        string issuedAtGenerationOne = s_tokens.Issue(NewMachine(generation: 1), MachineTokenPurpose.Session);

        MachineTokenPayload? payload = s_tokens.Validate(issuedAtGenerationOne, MachineTokenPurpose.Session);

        // The handler compares this against the machine row, so bumping the row's generation
        // invalidates every token already issued for that machine.
        Assert.NotNull(payload);
        Assert.Equal(1, payload.TokenGeneration);
        Assert.NotEqual(NewMachine(generation: 2).TokenGeneration, payload.TokenGeneration);
    }

    [Fact]
    public void OnlyThePollSessionAndResumePurposesExist()
    {
        // Image and secret grants were never used: the machine's own session token and its running deployment
        // decide what it may download and read.
        Assert.Equal(
            [MachineTokenPurpose.Poll, MachineTokenPurpose.Session, MachineTokenPurpose.Resume],
            Enum.GetValues<MachineTokenPurpose>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-token")]
    public void GarbageIsRejectedRatherThanThrowing(string token)
    {
        Assert.Null(s_tokens.Validate(token, MachineTokenPurpose.Session));
    }
}
