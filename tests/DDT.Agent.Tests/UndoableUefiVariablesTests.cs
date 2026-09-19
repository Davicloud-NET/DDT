using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class UndoableUefiVariablesTests
{
    private readonly FakeUefiVariables _variables = new();

    [Fact]
    public void PutsBackTheValueFromBeforeTheFirstChange()
    {
        byte[] original = [1, 2];
        _variables.Values["Boot0001"] = original;
        UndoableUefiVariables changes = new(_variables);

        changes.Write("Boot0001", [3]);
        changes.Write("Boot0001", [4]);
        changes.Delete("Boot0001");

        Assert.True(changes.Undo());
        Assert.Same(original, _variables.Values["Boot0001"]);
    }

    [Fact]
    public void UndoesNothingWhenNothingChanged()
    {
        UndoableUefiVariables changes = new(_variables);

        Assert.Null(changes.Read("BootOrder"));
        Assert.False(changes.Undo());
        Assert.Empty(_variables.Writes);
        Assert.Empty(_variables.Deletes);
    }
}
