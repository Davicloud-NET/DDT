// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Import;

// An MDT share's applications as DDT needs them: bundles replaced by their applications, and an order with each
// application after those it depends on. The file may hold circles, so both walk on their own stacks and never loop.
public static class MdtApplicationGraph
{
    // The applications an entry's dependencies come to, in order, each once; a bundle stands for its own. A guid the
    // share does not list stays in, so it can be looked up and named. A bundle's own guid is left out of its members.
    public static IReadOnlyList<string> Flatten(MdtShareApplication entry, IReadOnlyDictionary<string, MdtShareApplication> entries)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entries);

        List<string> flat = [];
        HashSet<string> seen = entry.IsBundle ? [entry.Id] : [];
        List<(IReadOnlyList<string> Ids, int Next)> stack = [(entry.Dependencies, 0)];

        while (stack.Count > 0)
        {
            (IReadOnlyList<string> ids, int next) = stack[^1];

            if (next == ids.Count)
            {
                stack.RemoveAt(stack.Count - 1);

                continue;
            }

            stack[^1] = (ids, next + 1);
            string id = ids[next];

            if (!seen.Add(id))
            {
                continue;
            }

            if (entries.TryGetValue(id, out MdtShareApplication? dependency) && dependency.IsBundle)
            {
                stack.Add((dependency.Dependencies, 0));
            }
            else
            {
                flat.Add(id);
            }
        }

        return flat;
    }

    // The applications, each after those of them it depends on, in the given order otherwise. A dependency that would
    // close a circle is cut.
    public static MdtApplicationOrder Order(
        IReadOnlyList<MdtShareApplication> applications,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(dependencies);

        Dictionary<string, MdtShareApplication> byId = applications.ToDictionary(application => application.Id);
        List<MdtShareApplication> order = [];
        HashSet<(string Application, string Dependency)> cut = [];
        HashSet<string> done = [];
        HashSet<string> walking = [];
        List<(MdtShareApplication Application, int Next)> stack = [];

        foreach (MdtShareApplication root in applications.Where(root => !done.Contains(root.Id)))
        {
            walking.Add(root.Id);
            stack.Add((root, 0));

            while (stack.Count > 0)
            {
                (MdtShareApplication application, int next) = stack[^1];
                IReadOnlyList<string> of = dependencies.GetValueOrDefault(application.Id) ?? [];

                if (next == of.Count)
                {
                    stack.RemoveAt(stack.Count - 1);
                    walking.Remove(application.Id);
                    done.Add(application.Id);
                    order.Add(application);

                    continue;
                }

                stack[^1] = (application, next + 1);
                string id = of[next];

                if (walking.Contains(id))
                {
                    cut.Add((application.Id, id));
                }
                else if (!done.Contains(id) && byId.TryGetValue(id, out MdtShareApplication? dependency))
                {
                    walking.Add(id);
                    stack.Add((dependency, 0));
                }
            }
        }

        return new MdtApplicationOrder(order, cut);
    }
}
