// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tests;

// An MDT deployment share with applications and bundles, Applications.xml as the Deployment Workbench writes it, with
// some values the way people edit them by hand.
internal static class MdtApplicationShares
{
    public const string SevenZip = "{a0000000-0000-0000-0000-000000000001}";
    public const string Runtime = "{a0000000-0000-0000-0000-000000000002}";
    public const string Notepad = "{a0000000-0000-0000-0000-000000000003}";
    public const string BaseBundle = "{a0000000-0000-0000-0000-000000000004}";
    public const string InnerBundle = "{a0000000-0000-0000-0000-000000000005}";
    public const string CycleOne = "{a0000000-0000-0000-0000-000000000006}";
    public const string CycleTwo = "{a0000000-0000-0000-0000-000000000007}";
    public const string EmptyBundle = "{a0000000-0000-0000-0000-000000000008}";
    public const string NoCommand = "{a0000000-0000-0000-0000-000000000009}";
    public const string Viewer = "{a0000000-0000-0000-0000-00000000000a}";
    public const string UsesViewer = "{a0000000-0000-0000-0000-00000000000b}";
    public const string Missing = "{ffffffff-0000-0000-0000-000000000000}";

    public const string ProductCode = "{23170F69-40C1-2702-2409-000001000000}";

    // Returns the share's path.
    public static string Write(string share)
    {
        string control = Directory.CreateDirectory(Path.Combine(share, "Control")).FullName;
        File.WriteAllText(Path.Combine(control, "OperatingSystems.xml"), "<oss />");
        Files(share, "7-Zip 24.09", ("7z.msi", "msi"), (Path.Combine("docs", "readme.txt"), "read me"));
        Files(share, "Notepad", ("npp.exe", "exe"));
        Files(share, "Viewer", ("viewer.exe", "exe"));

        File.WriteAllText(Path.Combine(control, "Applications.xml"), $"""
            <?xml version="1.0"?>
            <applications>
              <application guid="{SevenZip}" enable="True">
                <Name>7-Zip 24.09</Name><ShortName>7-Zip</ShortName><Version>24.09</Version><Publisher>Igor Pavlov</Publisher>
                <Language /><Comments>Packs files.</Comments><CommandLine>msiexec /i 7z.msi /qn</CommandLine>
                <WorkingDirectory>.\Applications\7-Zip 24.09</WorkingDirectory><Source>.\Applications\7-Zip 24.09</Source>
                <UninstallKey>{ProductCode}</UninstallKey><Reboot>False</Reboot><SupportedPlatform>All x64 Windows 10 and Newer</SupportedPlatform>
              </application>
              <application guid="{Runtime.Trim('{', '}')}" enable="True" hide="True">
                <Name>VC++ Runtime</Name><CommandLine>\\files\apps\vc_redist.x64.exe /install /quiet</CommandLine>
                <WorkingDirectory /><Source /><UninstallKey>VCRuntime</UninstallKey><Reboot>TRUE</Reboot>
              </application>
              <application guid="{Notepad}" enable="False">
                <Name>Notepad++; portable</Name><CommandLine>npp.exe /S</CommandLine>
                <WorkingDirectory>.\Applications\Other</WorkingDirectory><Source>.\Applications\Notepad</Source>
                <Dependency>{Runtime.ToUpperInvariant()}</Dependency><Dependency>{BaseBundle}</Dependency><Dependency>{Missing}</Dependency>
              </application>
              <application guid="{BaseBundle}" enable="True">
                <Name>Base Bundle</Name><Comments>What every PC gets.</Comments><CommandLine />
                <Dependency>{SevenZip}</Dependency><Dependency>{InnerBundle}</Dependency>
              </application>
              <application guid="{InnerBundle}" enable="True">
                <Name>Inner Bundle</Name><Dependency>{Runtime}</Dependency><Dependency>{BaseBundle}</Dependency>
              </application>
              <application guid="{CycleOne}"><Name>Cycle One</Name><CommandLine>one.cmd</CommandLine><Dependency>{CycleTwo}</Dependency></application>
              <application guid="{CycleTwo}"><Name>Cycle Two</Name><CommandLine>two.cmd</CommandLine><Dependency>{CycleOne}</Dependency></application>
              <application guid="{EmptyBundle}"><Name>Empty Bundle</Name><Dependency>{Missing}</Dependency></application>
              <application guid="{NoCommand}"><Name>No Command</Name></application>
              <application guid="{Viewer}">
                <DisplayName>Viewer</DisplayName><CommandLine>viewer.exe /S</CommandLine><Source>.\Applications\Viewer</Source>
              </application>
              <application guid="{UsesViewer}"><Name>Uses Viewer</Name><CommandLine>plugin.cmd</CommandLine><Dependency>{Viewer}</Dependency></application>
              <application><Name>No guid</Name><CommandLine>x.cmd</CommandLine></application>
              <application guid="{SevenZip}"><Name>7-Zip listed twice</Name><CommandLine>x.cmd</CommandLine></application>
            </applications>
            """);

        return share;
    }

    private static void Files(string share, string folder, params (string Name, string Content)[] files)
    {
        string path = Directory.CreateDirectory(Path.Combine(share, "Applications", folder)).FullName;

        foreach ((string name, string content) in files)
        {
            string file = Path.Combine(path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(file) ?? path);
            File.WriteAllText(file, content);
        }
    }
}
