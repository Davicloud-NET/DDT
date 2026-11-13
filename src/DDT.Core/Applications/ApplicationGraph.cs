// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Applications;

// The order a run installs applications in: each after its dependencies, each once, roots in the given order and an
// application's dependencies in their listed order. A depth-first walk on its own stack, so a long chain is safe.
public static class ApplicationGraph
{
    // dependenciesOf returns null for an id that is not in the library.
    public static ApplicationGraphResult Order(IReadOnlyList<Guid> roots, Func<Guid, IReadOnlyList<Guid>?> dependenciesOf)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(dependenciesOf);

        List<Guid> order = [];
        HashSet<Guid> done = [];
        // The applications on the walk's path, by their place on the stack.
        Dictionary<Guid, int> walking = [];
        List<Frame> stack = [];

        foreach (Guid root in roots)
        {
            if (done.Contains(root))
            {
                continue;
            }

            if (Enter(root, stack, walking, dependenciesOf) is { } missing)
            {
                return Failed(missing);
            }

            while (stack.Count > 0)
            {
                Frame top = stack[^1];

                if (top.Next == top.Dependencies.Count)
                {
                    stack.RemoveAt(stack.Count - 1);
                    walking.Remove(top.Id);
                    done.Add(top.Id);
                    order.Add(top.Id);

                    continue;
                }

                Guid next = top.Dependencies[top.Next++];

                if (done.Contains(next))
                {
                    continue;
                }

                if (walking.TryGetValue(next, out int start))
                {
                    return Failed(new ApplicationGraphProblem(
                        ApplicationGraphProblemKind.Cycle,
                        [.. stack.Skip(start).Select(frame => frame.Id), next]));
                }

                if (Enter(next, stack, walking, dependenciesOf) is { } gone)
                {
                    return Failed(gone);
                }
            }
        }

        return new ApplicationGraphResult(order, null);
    }

    // Pushes the application, or returns the problem if it is missing.
    private static ApplicationGraphProblem? Enter(
        Guid id,
        List<Frame> stack,
        Dictionary<Guid, int> walking,
        Func<Guid, IReadOnlyList<Guid>?> dependenciesOf)
    {
        if (dependenciesOf(id) is not { } dependencies)
        {
            return new ApplicationGraphProblem(ApplicationGraphProblemKind.Missing, [.. stack.Select(frame => frame.Id), id]);
        }

        walking[id] = stack.Count;
        stack.Add(new Frame(id, dependencies));

        return null;
    }

    private static ApplicationGraphResult Failed(ApplicationGraphProblem problem) => new([], problem);

    private sealed class Frame(Guid id, IReadOnlyList<Guid> dependencies)
    {
        public Guid Id { get; } = id;

        public IReadOnlyList<Guid> Dependencies { get; } = dependencies;

        public int Next { get; set; }
    }
}
