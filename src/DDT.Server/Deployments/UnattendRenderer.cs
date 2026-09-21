// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Unattend;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.Extensions.Options;

namespace DDT.Server.Deployments;

public sealed class UnattendRenderer(IOptions<DeploymentOptions> options)
{
    // Only x64 images can be deployed, so every component is the amd64 one.
    private const string ProcessorArchitecture = "amd64";
    private const string FallbackLanguage = "en-US";

    // Setup generates a name for "*".
    private const string GeneratedComputerName = "*";

    public string Render(Machine machine, Image? image)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return UnattendWriter.Write(Settings(machine.AssignedName, image?.Language));
    }

    public UnattendSettings Settings(string? assignedName, string? imageLanguage)
    {
        DeploymentOptions deployment = options.Value;
        string uiLanguage = Value(imageLanguage) ?? FallbackLanguage;
        string locale = Value(deployment.Locale) ?? uiLanguage;

        LocalAdministrator? administrator = string.IsNullOrEmpty(deployment.LocalAdministrator.Password)
            ? null
            : new LocalAdministrator(deployment.LocalAdministrator.Name.Trim(), deployment.LocalAdministrator.Password);

        DomainJoin? join = Value(deployment.Domain.Name) is { } domain
            ? new DomainJoin(domain, Value(deployment.Domain.OrganizationalUnit), deployment.Domain.UserName ?? string.Empty, deployment.Domain.Password ?? string.Empty)
            : null;

        return new UnattendSettings(
            ProcessorArchitecture,
            Value(assignedName) ?? GeneratedComputerName,
            Value(deployment.TimeZone),
            uiLanguage,
            locale,
            Value(deployment.Keyboard) ?? locale,
            administrator,
            join);
    }

    private static string? Value(string? setting) => string.IsNullOrWhiteSpace(setting) ? null : setting.Trim();
}
