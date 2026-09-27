// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Server.Sequences;

public static class SequenceTemplates
{
    public const string InstallWindowsKey = "install-windows";
    public const string InstallLinuxKey = "install-linux";

    // cloud-init runs its first-boot modules once per instance id, and names the machine after local-hostname.
    public const string LinuxMetaData = "instance-id: \"{{SmbiosUuid}}\"\nlocal-hostname: \"{{ComputerName}}\"\n";

    // The image's default user, such as ubuntu or debian, gets the keys listed here.
    public const string LinuxUserData =
        "#cloud-config\n" +
        "# Every viewer of DDT can read this. Put in public keys, and passwords only hashed.\n" +
        "ssh_authorized_keys: []\n";

    // What DDT did before task sequences, and its Linux counterpart. Without a domain the Windows run ends in Windows
    // PE and the machine restarts into Windows setup; with one it continues in Windows to join, and restarts once more
    // for the join to take effect. imageId is the Windows image to apply, if one was chosen.
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
            Template(
                InstallWindowsKey,
                ServerMessages.TemplateInstallWindows.With(),
                (domainConfigured ? ServerMessages.TemplateInstallWindowsJoinDescription : ServerMessages.TemplateInstallWindowsDescription).With(),
                new SequenceDefinition(SequenceDefinition.CurrentVersion, steps).Normalised()),
            Template(
                InstallLinuxKey,
                ServerMessages.TemplateInstallLinux.With(),
                ServerMessages.TemplateInstallLinuxDescription.With(),
                new SequenceDefinition(
                    SequenceDefinition.CurrentVersion,
                    [
                        new WriteRawImageStep { Id = Guid.NewGuid(), Name = "Write the disk image", ImageId = Guid.Empty },
                        new WriteCloudInitSeedStep
                        {
                            Id = Guid.NewGuid(),
                            Name = "Write the cloud-init seed",
                            MetaData = LinuxMetaData,
                            UserData = LinuxUserData,
                        },
                    ]).Normalised()),
        ];
    }

    private static SequenceTemplate Template(string key, ServerMessage name, ServerMessage description, SequenceDefinition definition) =>
        new(key, name.Text, description.Text, definition, name.Code, name.Args, description.Code, description.Args);
}
