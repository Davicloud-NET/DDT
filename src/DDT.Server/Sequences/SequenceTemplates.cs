// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Server.Sequences;

public static class SequenceTemplates
{
    public const string InstallWindowsKey = "install-windows";

    // What DDT did before task sequences. Without a domain the run ends in Windows PE and the machine restarts into
    // Windows setup; with one it continues in Windows to join, and restarts once more for the join to take effect.
    public static IReadOnlyList<SequenceTemplate> All(bool domainConfigured, bool administratorConfigured, Guid imageId)
    {
        List<SequenceStep> steps =
        [
            new PartitionStep { Id = Guid.NewGuid(), Name = "Partition the disk" },
            new ApplyImageStep { Id = Guid.NewGuid(), Name = "Apply the image", ImageId = imageId },
            new InjectDriversStep { Id = Guid.NewGuid(), Name = "Add the drivers for the model" },
            new WriteUnattendStep { Id = Guid.NewGuid(), Name = "Write the answer file", LocalAdministrator = administratorConfigured },
        ];

        if (domainConfigured)
        {
            steps.Add(new JoinDomainStep { Id = Guid.NewGuid(), Name = "Join the domain", RebootAfter = true });
        }

        return
        [
            new SequenceTemplate(
                InstallWindowsKey,
                "Install Windows",
                "Partitions the disk, applies an image, adds the drivers for the machine's model and writes the answer file"
                    + (domainConfigured ? ", then joins the domain in Windows." : "."),
                new SequenceDefinition(SequenceDefinition.CurrentVersion, steps)),
        ];
    }
}
