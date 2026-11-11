// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Applications;
using Xunit;

namespace DDT.Core.Tests.Applications;

// Applications are stored and sent to the web as JSON, so these strings never change.
public sealed class ApplicationWireFormatTests
{
    private const string Id = "0197a3c0-0000-7000-8000-000000000001";
    private const string Other = "0197a3c0-0000-7000-8000-000000000002";
    private const string Package = "0197a3c0-0000-7000-8000-000000000003";

    private static readonly Guid s_id = Guid.Parse(Id);
    private static readonly Guid s_other = Guid.Parse(Other);
    private static readonly Guid s_package = Guid.Parse(Package);
    private static readonly DateTimeOffset s_updated = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, (ApplicationDetection Detection, string Json)> s_detections = new(StringComparer.Ordinal)
    {
        ["productCode"] = (
            new ProductCodeDetection("{23170F69-40C1-2702-2501-000001000000}"),
            """{"kind":"productCode","productCode":"{23170F69-40C1-2702-2501-000001000000}"}"""),
        ["uninstallKey"] = (
            new UninstallKeyDetection("7-Zip"),
            """{"kind":"uninstallKey","keyName":"7-Zip"}"""),
        ["file"] = (
            new FileDetection(@"%ProgramFiles%\7-Zip\7z.exe", "24.9"),
            """{"kind":"file","path":"%ProgramFiles%\\7-Zip\\7z.exe","minimumVersion":"24.9"}"""),
        ["packageFamily"] = (
            new PackageFamilyDetection("Microsoft.DesktopAppInstaller_8wekyb3d8bbwe"),
            """{"kind":"packageFamily","familyName":"Microsoft.DesktopAppInstaller_8wekyb3d8bbwe"}"""),
    };

    public static TheoryData<string> Detections => [.. s_detections.Keys];

    [Theory]
    [MemberData(nameof(Detections))]
    public void WritesAndReadsEveryDetectionExactly(string kind)
    {
        (ApplicationDetection detection, string json) = s_detections[kind];

        Assert.Equal(json, JsonSerializer.Serialize(detection, DdtJsonContext.Default.ApplicationDetection));
        Assert.Equal(detection, JsonSerializer.Deserialize(json, DdtJsonContext.Default.ApplicationDetection));
    }

    [Fact]
    public void ReadsADetectionWhoseKindIsNotFirst() =>
        Assert.Equal(
            new FileDetection(@"C:\app.exe"),
            JsonSerializer.Deserialize("""{"path":"C:\\app.exe","kind":"file"}""", DdtJsonContext.Default.ApplicationDetection));

    [Fact]
    public void RefusesAnUnknownDetection() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"kind":"wmi","query":"x"}""", DdtJsonContext.Default.ApplicationDetection));

    [Fact]
    public void WritesAnApplication()
    {
        ApplicationView view = new(
            s_id,
            "7-Zip",
            null,
            "Igor Pavlov",
            "24.09",
            ApplicationKind.Command,
            new ApplicationFiles(s_package, "7z2409-x64.msi", 1_617_920),
            "msiexec /i 7z2409-x64.msi /qn",
            [0],
            [3010],
            new UninstallKeyDetection("7-Zip"),
            [s_other],
            null,
            null,
            60,
            true,
            [new ApplicationUse(s_other, "Office", ApplicationUseKind.Bundle)],
            3,
            s_updated,
            "jane");

        Assert.Equal(
            $$"""{"id":"{{Id}}","name":"7-Zip","description":null,"publisher":"Igor Pavlov","version":"24.09","kind":"Command","files":{"packageId":"{{Package}}","name":"7z2409-x64.msi","sizeBytes":1617920},"command":"msiexec /i 7z2409-x64.msi /qn","successExitCodes":[0],"restartExitCodes":[3010],"detection":{"kind":"uninstallKey","keyName":"7-Zip"},"dependsOn":["{{Other}}"],"wingetId":null,"wingetVersion":null,"timeoutMinutes":60,"continueOnFailure":true,"usedBy":[{"id":"{{Other}}","name":"Office","kind":"Bundle"}],"revision":3,"updatedUtc":"2026-11-11T10:00:00+00:00","updatedBy":"jane"}""",
            JsonSerializer.Serialize(view, DdtJsonContext.Default.ApplicationView));
    }

    [Fact]
    public void WritesTheSuggestionOfAnMsixUpload() =>
        Assert.Equal(
            """{"files":{"packageId":"0197a3c0-0000-7000-8000-000000000003","name":"Contoso.App.msix","sizeBytes":2048},"suggestion":{"kind":"Msix","name":"Contoso App","publisher":"Contoso","version":"1.2.3.0","command":null,"successExitCodes":[0],"restartExitCodes":[3010],"detection":{"kind":"packageFamily","familyName":"Contoso.App_8wekyb3d8bbwe"}}}""",
            JsonSerializer.Serialize(
                new ApplicationUpload(
                    new ApplicationFiles(s_package, "Contoso.App.msix", 2048),
                    new ApplicationSuggestion(
                        ApplicationKind.Msix,
                        "Contoso App",
                        "Contoso",
                        "1.2.3.0",
                        null,
                        [0],
                        [3010],
                        new PackageFamilyDetection("Contoso.App_8wekyb3d8bbwe"))),
                DdtJsonContext.Default.ApplicationUpload));

    [Fact]
    public void ReadsTheSaveTheWebSends()
    {
        SaveApplicationRequest? request = JsonSerializer.Deserialize(
            """{"revision":2,"name":"Firefox","description":null,"publisher":null,"version":null,"kind":"Winget","packageId":null,"command":null,"successExitCodes":[0],"restartExitCodes":[],"detection":null,"dependsOn":[],"wingetId":"Mozilla.Firefox","wingetVersion":null,"timeoutMinutes":30,"continueOnFailure":true}""",
            DdtJsonContext.Default.SaveApplicationRequest);

        Assert.NotNull(request);
        Assert.Equal(ApplicationKind.Winget, request.Kind);
        Assert.Equal("Mozilla.Firefox", request.WingetId);
        Assert.Equal(2, request.Revision);
        Assert.True(request.ContinueOnFailure);
    }

    [Fact]
    public void WritesABundle() =>
        Assert.Equal(
            $$"""{"id":"{{Id}}","name":"Office","description":"Every office PC","members":["{{Other}}"],"usedBy":[],"revision":1,"updatedUtc":"2026-11-11T10:00:00+00:00","updatedBy":null}""",
            JsonSerializer.Serialize(
                new ApplicationBundleView(s_id, "Office", "Every office PC", [s_other], [], 1, s_updated, null),
                DdtJsonContext.Default.ApplicationBundleView));

    [Fact]
    public void WritesAReferenceAndTheRemovedEvents()
    {
        Assert.Equal(
            $$"""{"id":"{{Id}}","kind":"Bundle"}""",
            JsonSerializer.Serialize(new ApplicationReference(s_id, ApplicationReferenceKind.Bundle), DdtJsonContext.Default.ApplicationReference));
        Assert.Equal(
            $$"""{"applicationIds":["{{Id}}"]}""",
            JsonSerializer.Serialize(new ApplicationsRemovedEvent([s_id]), DdtJsonContext.Default.ApplicationsRemovedEvent));
        Assert.Equal(
            $$"""{"bundleIds":["{{Id}}"]}""",
            JsonSerializer.Serialize(new ApplicationBundlesRemovedEvent([s_id]), DdtJsonContext.Default.ApplicationBundlesRemovedEvent));
    }
}
