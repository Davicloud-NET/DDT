using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class FirmwareBootEntriesTests
{
    // As the Hyper-V test machine lists it in Windows PE, before anything was deployed.
    private static readonly string[] s_beforeDeployment =
    [
        "Firmware Boot Manager",
        "---------------------",
        "identifier              {fwbootmgr}",
        "displayorder            {568b7492-b49e-11f1-9cf8-02155d0d0d01}",
        "                        {568b7494-b49e-11f1-9cf8-02155d0d0d01}",
        "timeout                 0",
        "",
        "Firmware Application (101fffff)",
        "-------------------------------",
        "identifier              {568b7492-b49e-11f1-9cf8-02155d0d0d01}",
        "description             EFI Network",
        "",
        "Firmware Application (101fffff)",
        "-------------------------------",
        "identifier              {568b7493-b49e-11f1-9cf8-02155d0d0d01}",
        "description             FrontPage",
        "",
        "Firmware Application (101fffff)",
        "-------------------------------",
        "identifier              {568b7494-b49e-11f1-9cf8-02155d0d0d01}",
        "description             EFI SCSI Device",
    ];

    private static readonly string[] s_windowsEntry =
    [
        "",
        "Windows Boot Manager",
        "--------------------",
        "identifier              {bootmgr}",
        "device                  partition=\\Device\\HarddiskVolume1",
        "path                    \\EFI\\Microsoft\\Boot\\bootmgfw.efi",
        "description             Windows Boot Manager",
        "locale                  de-DE",
        "inherit                 {globalsettings}",
        "default                 {current}",
    ];

    [Fact]
    public void FindsNothingBeforeWindowsIsDeployed()
    {
        Assert.Null(FirmwareBootEntries.FindWindowsBootManager(s_beforeDeployment));
    }

    [Fact]
    public void FindsTheEntryBcdbootAdded()
    {
        Assert.Equal("{bootmgr}", FirmwareBootEntries.FindWindowsBootManager([.. s_beforeDeployment, .. s_windowsEntry]));
    }

    [Fact]
    public void KeepsEntriesApartWithoutBlankLines()
    {
        string[] lines = [.. s_beforeDeployment, .. s_windowsEntry];

        // ToolRunner hands on only the lines that hold text.
        Assert.Equal("{bootmgr}", FirmwareBootEntries.FindWindowsBootManager(lines.Where(line => line.Length > 0)));
    }

    [Fact]
    public void TakesTheIdentifierOfTheEntryNotALaterValueInBraces()
    {
        string[] lines =
        [
            "Firmware Application (101fffff)",
            "identifier              {9dea862c-5cdd-4e70-acc1-f32b344d4795}",
            "resumeobject            {11111111-2222-3333-4444-555555555555}",
            "description             Windows Boot Manager",
        ];

        Assert.Equal("{9dea862c-5cdd-4e70-acc1-f32b344d4795}", FirmwareBootEntries.FindWindowsBootManager(lines));
    }

    [Fact]
    public void ReadsTranslatedLabels()
    {
        string[] lines =
        [
            "Windows-Start-Manager",
            "Bezeichner              {bootmgr}",
            "Beschreibung            Windows Boot Manager",
        ];

        Assert.Equal("{bootmgr}", FirmwareBootEntries.FindWindowsBootManager(lines));
    }

    [Fact]
    public void DoesNotTakeTheTitleOfAnEntryForItsDescription()
    {
        string[] lines =
        [
            "identifier              {568b7492-b49e-11f1-9cf8-02155d0d0d01}",
            "description             EFI Network",
            "",
            "Windows Boot Manager",
            "--------------------",
        ];

        Assert.Null(FirmwareBootEntries.FindWindowsBootManager(lines));
    }
}
