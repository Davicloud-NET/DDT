// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Core.Applications;

namespace DDT.Server.Applications;

// A save's values as they are stored: trimmed, empty text as null, the product code in Windows' form.
internal sealed record ApplicationFields(
    string Name,
    string? Description,
    string? Publisher,
    string? Version,
    ApplicationKind Kind,
    Guid? PackageId,
    string? Command,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> RestartExitCodes,
    ApplicationDetection? Detection,
    IReadOnlyList<Guid> DependsOn,
    string? WingetId,
    string? WingetVersion,
    int TimeoutMinutes,
    bool ContinueOnFailure)
{
    // Call only for a request that passed ApplicationRules.
    public static ApplicationFields From(SaveApplicationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ApplicationFields(
            ApplicationNames.Clean(request.Name) ?? "",
            Optional(request.Description),
            Optional(request.Publisher),
            Optional(request.Version),
            request.Kind,
            request.PackageId,
            Optional(request.Command),
            [.. request.SuccessExitCodes.Distinct()],
            [.. (request.RestartExitCodes ?? []).Distinct()],
            Cleaned(request.Detection),
            [.. request.DependsOn ?? []],
            Optional(request.WingetId),
            Optional(request.WingetVersion),
            request.TimeoutMinutes,
            request.ContinueOnFailure);
    }

    public void ApplyTo(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.Name = Name;
        application.NormalizedName = ApplicationNames.Normalize(Name) ?? "";
        application.Description = Description;
        application.Publisher = Publisher;
        application.Version = Version;
        application.Kind = Kind;
        application.PackageId = PackageId;
        application.Command = Command;
        application.SuccessExitCodes = ApplicationColumns.WriteCodes(SuccessExitCodes);
        application.RestartExitCodes = ApplicationColumns.WriteCodes(RestartExitCodes);
        application.Detection = ApplicationColumns.WriteDetection(Detection);
        application.DependsOn = ApplicationColumns.WriteIds(DependsOn);
        application.WingetId = WingetId;
        application.WingetVersion = WingetVersion;
        application.TimeoutMinutes = TimeoutMinutes;
        application.ContinueOnFailure = ContinueOnFailure;
    }

    public static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ApplicationDetection? Cleaned(ApplicationDetection? detection) => detection switch
    {
        ProductCodeDetection product => new ProductCodeDetection(ApplicationDetectionRules.NormalizedProductCode(product.ProductCode) ?? product.ProductCode),
        UninstallKeyDetection key => new UninstallKeyDetection(key.KeyName.Trim()),
        FileDetection file => new FileDetection(file.Path.Trim(), Optional(file.MinimumVersion)),
        _ => null,
    };
}
