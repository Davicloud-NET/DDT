// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Applications;
using Xunit;

namespace DDT.Core.Tests.Applications;

public sealed class ApplicationGraphTests
{
    private const int ChainLength = 10_000;

    private static readonly Guid s_a = Id(1);
    private static readonly Guid s_b = Id(2);
    private static readonly Guid s_c = Id(3);
    private static readonly Guid s_d = Id(4);
    private static readonly Guid s_e = Id(5);

    [Fact]
    public void KeepsTheRootsInTheirOrderWithoutDependencies()
    {
        ApplicationGraphResult result = Order([s_c, s_a, s_b], new() { [s_a] = [], [s_b] = [], [s_c] = [] });

        Assert.Null(result.Problem);
        Assert.Equal([s_c, s_a, s_b], result.Order);
    }

    [Fact]
    public void InstallsDependenciesFirstInTheirListedOrder()
    {
        ApplicationGraphResult result = Order([s_a], new() { [s_a] = [s_c, s_b], [s_b] = [s_d], [s_c] = [], [s_d] = [] });

        Assert.Equal([s_c, s_d, s_b, s_a], result.Order);
    }

    // a and b both need c, which needs d: c and d install once, before a.
    [Fact]
    public void InstallsASharedDependencyOnce()
    {
        ApplicationGraphResult result = Order([s_a, s_b], new() { [s_a] = [s_c], [s_b] = [s_c], [s_c] = [s_d], [s_d] = [] });

        Assert.Equal([s_d, s_c, s_a, s_b], result.Order);
    }

    [Fact]
    public void InstallsARootOnceWhenAnotherRootNeedsItFirst()
    {
        ApplicationGraphResult result = Order([s_a, s_b, s_a], new() { [s_a] = [s_b], [s_b] = [] });

        Assert.Equal([s_b, s_a], result.Order);
    }

    [Fact]
    public void FindsACycleWithItsPath()
    {
        ApplicationGraphResult result = Order([s_a], new() { [s_a] = [s_b], [s_b] = [s_c], [s_c] = [s_d, s_b], [s_d] = [] });

        Assert.Empty(result.Order);
        Assert.Equal(ApplicationGraphProblemKind.Cycle, result.Problem?.Kind);
        Assert.Equal([s_b, s_c, s_b], result.Problem?.Path);
    }

    [Fact]
    public void FindsAnApplicationThatNeedsItself()
    {
        ApplicationGraphResult result = Order([s_a], new() { [s_a] = [s_a] });

        Assert.Equal([s_a, s_a], result.Problem?.Path);
    }

    [Fact]
    public void SaysWhoNamesAMissingDependency()
    {
        ApplicationGraphResult result = Order([s_a], new() { [s_a] = [s_b], [s_b] = [s_e] });

        Assert.Equal(ApplicationGraphProblemKind.Missing, result.Problem?.Kind);
        Assert.Equal([s_a, s_b, s_e], result.Problem?.Path);
        Assert.Equal(s_b, result.Problem?.NamedBy);
    }

    [Fact]
    public void SaysWhenARootIsMissing()
    {
        ApplicationGraphResult result = Order([s_a, s_e], new() { [s_a] = [] });

        Assert.Equal([s_e], result.Problem?.Path);
        Assert.Null(result.Problem?.NamedBy);
    }

    [Fact]
    public void WalksALongChainWithoutRunningOutOfStack()
    {
        Guid[] chain = [.. Enumerable.Range(1, ChainLength).Select(Id)];
        Dictionary<Guid, IReadOnlyList<Guid>> dependencies = [];

        for (int index = 0; index < ChainLength; index++)
        {
            dependencies[chain[index]] = index + 1 < ChainLength ? [chain[index + 1]] : [];
        }

        ApplicationGraphResult result = Order([chain[0]], dependencies);

        Assert.Equal(Enumerable.Reverse(chain), result.Order);
    }

    [Fact]
    public void FindsACycleAtTheEndOfALongChain()
    {
        Guid[] chain = [.. Enumerable.Range(1, ChainLength).Select(Id)];
        Dictionary<Guid, IReadOnlyList<Guid>> dependencies = [];

        for (int index = 0; index < ChainLength; index++)
        {
            dependencies[chain[index]] = [chain[(index + 1) % ChainLength]];
        }

        ApplicationGraphResult result = Order([chain[0]], dependencies);

        Assert.Equal(ChainLength + 1, result.Problem?.Path.Count);
    }

    private static ApplicationGraphResult Order(IReadOnlyList<Guid> roots, Dictionary<Guid, IReadOnlyList<Guid>> dependencies) =>
        ApplicationGraph.Order(roots, id => dependencies.GetValueOrDefault(id));

    private static Guid Id(int number) => new(number, 0, 0x7000, 0x80, 0, 0, 0, 0, 0, 0, 0);
}
