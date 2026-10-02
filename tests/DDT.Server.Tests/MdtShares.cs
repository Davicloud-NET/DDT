// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tests;

// A small MDT deployment share, with the lists in Control as the Deployment Workbench writes them.
internal static class MdtShares
{
    private const string E1d = "{11111111-1111-1111-1111-111111111111}";
    private const string Audio = "{22222222-2222-2222-2222-222222222222}";
    private const string Storage = "{33333333-3333-3333-3333-333333333333}";

    // One install.wim with two editions and an operating system whose file is gone, three drivers in two groups, an
    // application, a task sequence and CustomSettings.ini. Returns the share's path.
    public static string Write(string share)
    {
        string control = Directory.CreateDirectory(Path.Combine(share, "Control")).FullName;
        string sources = Directory.CreateDirectory(Path.Combine(share, "Operating Systems", "Windows 11 x64", "sources")).FullName;
        File.WriteAllBytes(Path.Combine(sources, "install.wim"), TestWim.Create(TestWim.X64));

        Driver(share, @"Net\e1d68x64_12.19", "e1d68x64.inf");
        Driver(share, @"Media\audio_6.0", "audio.inf");
        Driver(share, @"Storage\stor_1.0", "stor.inf");

        File.WriteAllText(Path.Combine(control, "OperatingSystems.xml"), """
            <oss>
              <os guid="{aaaaaaaa-0000-0000-0000-000000000001}" enable="True">
                <Name>Windows 11 Pro in Windows 11 x64 install.wim</Name>
                <Description>Windows 11 Pro</Description>
                <ImageFile>.\Operating Systems\Windows 11 x64\sources\install.wim</ImageFile>
                <ImageIndex>1</ImageIndex>
              </os>
              <os guid="{aaaaaaaa-0000-0000-0000-000000000002}" enable="True">
                <Name>Windows 11 Enterprise in Windows 11 x64 install.wim</Name>
                <Description>Windows 11 Enterprise</Description>
                <ImageFile>.\Operating Systems\Windows 11 x64\sources\install.wim</ImageFile>
                <ImageIndex>2</ImageIndex>
              </os>
              <os guid="{aaaaaaaa-0000-0000-0000-000000000003}" enable="True">
                <Name>Windows 10 that someone deleted</Name>
                <ImageFile>.\Operating Systems\Windows 10 x64\sources\install.wim</ImageFile>
                <ImageIndex>1</ImageIndex>
              </os>
            </oss>
            """);
        File.WriteAllText(Path.Combine(control, "Drivers.xml"), $"""
            <drivers>
              <driver guid="{E1d}" enable="True"><Name>Intel Net e1d68x64.inf</Name><Source>.\Out-of-Box Drivers\Net\e1d68x64_12.19\e1d68x64.inf</Source></driver>
              <driver guid="{Audio}" enable="True"><Name>Realtek Media audio.inf</Name><Source>.\Out-of-Box Drivers\Media\audio_6.0\audio.inf</Source></driver>
              <driver guid="{Storage}" enable="True"><Name>Intel Storage stor.inf</Name><Source>.\Out-of-Box Drivers\Storage\stor_1.0\stor.inf</Source></driver>
            </drivers>
            """);
        File.WriteAllText(Path.Combine(control, "DriverGroups.xml"), $"""
            <groups>
              <group guid="{"{bbbbbbbb-0000-0000-0000-000000000001}"}" enable="True"><Name>Out-of-Box Drivers</Name></group>
              <group guid="{"{bbbbbbbb-0000-0000-0000-000000000002}"}" enable="True">
                <Name>Out-of-Box Drivers\Dell Inc.\Latitude 7440</Name><Member>{E1d}</Member><Member>{Audio}</Member>
              </group>
              <group guid="{"{bbbbbbbb-0000-0000-0000-000000000003}"}" enable="True">
                <Name>Out-of-Box Drivers\WinPE x64\Net</Name><Member>{Storage}</Member><Member>{"{no-such-driver}"}</Member>
              </group>
            </groups>
            """);
        File.WriteAllText(Path.Combine(control, "Applications.xml"), "<applications><application guid=\"{c}\"><Name>7-Zip</Name></application></applications>");
        File.WriteAllText(Path.Combine(control, "TaskSequences.xml"), "<tss><ts guid=\"{d}\"><Name>Deploy Windows 11</Name><ID>W11</ID></ts></tss>");
        File.WriteAllText(Path.Combine(control, "CustomSettings.ini"), "[Settings]\nPriority=Default\n\n[Default]\nOSInstall=Y\n");

        return share;
    }

    private static void Driver(string share, string folder, string inf)
    {
        string path = Directory.CreateDirectory(Path.Combine(share, "Out-of-Box Drivers", folder)).FullName;
        File.WriteAllText(Path.Combine(path, inf), "[Version]\nSignature=\"$Windows NT$\"\n");
        File.WriteAllText(Path.Combine(path, Path.ChangeExtension(inf, ".sys")), "driver");
    }
}
