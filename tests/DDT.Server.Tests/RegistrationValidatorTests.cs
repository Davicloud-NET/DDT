using DDT.Contracts.Agents;
using DDT.Server.Machines;
using Xunit;

namespace DDT.Server.Tests;

public sealed class RegistrationValidatorTests
{
    [Fact]
    public void NormalisesMacsAndUuidAndPutsThePrimaryInTheList()
    {
        AgentRegistration registration = new(
            "44454C4C-5700-1038-8036-B7C04F5A344A",
            "00:15:5d:01:02:03",
            ["00-15-5D-01-02-03", "00155D010204", "00155d010204"],
            "  Dell Inc.  ",
            "",
            null,
            "1.0.0");

        Assert.True(RegistrationValidator.TryNormalise(registration, out NormalisedRegistration? normalised, out _));

        Assert.Equal("44454c4c-5700-1038-8036-b7c04f5a344a", normalised!.SmbiosUuid);
        Assert.Equal("00155D010203", normalised.PrimaryMac);
        Assert.Equal(["00155D010203", "00155D010204"], normalised.MacAddresses);
        Assert.Equal("Dell Inc.", normalised.Manufacturer);
        Assert.Null(normalised.Model);
    }

    [Fact]
    public void RefusesAPrimaryMacThatIsNotAmongTheAddresses()
    {
        AgentRegistration registration = new(Guid.NewGuid().ToString(), "00155D010203", ["00155D010204"], null, null, null, "1");

        Assert.False(RegistrationValidator.TryNormalise(registration, out _, out string error));
        Assert.Contains("primaryMac", error, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundsFreeText()
    {
        AgentRegistration registration = new(Guid.NewGuid().ToString(), "00155D010203", ["00155D010203"], new string('m', 500), null, null, new string('v', 100));

        Assert.True(RegistrationValidator.TryNormalise(registration, out NormalisedRegistration? normalised, out _));
        Assert.Equal(128, normalised!.Manufacturer!.Length);
        Assert.Equal(32, normalised.AgentVersion.Length);
    }
}
