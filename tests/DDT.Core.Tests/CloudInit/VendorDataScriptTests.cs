// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Core.Tests.CloudInit;

// The script cloud-init runs in the installed Linux. It was tried on Debian 13 and Ubuntu 24.04 as it is, so these
// tests hold its text to what was tried and to the names the agent uses.
public sealed class VendorDataScriptTests
{
    private const string Unit =
        """
        [Unit]
        Description=DDT agent, which goes on with this machine's task sequence and then removes itself
        Wants=network-online.target
        After=network-online.target
        ConditionPathExists=/usr/local/lib/ddt/ddt-agent

        [Service]
        Type=simple
        ExecStart=/usr/local/lib/ddt/ddt-agent --linux-phase
        Environment=HOME=/root PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
        UMask=0077
        Restart=on-failure
        RestartSec=15
        KillMode=mixed
        TimeoutStopSec=120

        [Install]
        WantedBy=multi-user.target
        """;

    private static string[] Lines => VendorDataScript.Text.Split('\n');

    // The kernel reads #! from the first two bytes, and a shell takes a carriage return as part of a word.
    [Fact]
    public void ReachesTheSeedAsAShellScriptWithLineFeedsAndNoByteOrderMark()
    {
        byte[] bytes = VendorDataScript.Bytes();

        Assert.Equal("#!/bin/sh\n"u8.ToArray(), bytes[..10]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.All(bytes, value => Assert.InRange(value, (byte)'\n', (byte)'~'));
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.Equal(VendorDataScript.Text, Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void TakesOutWhatACheckOutOnWindowsMayPutIn()
    {
        Assert.Equal("#!/bin/sh\nset -eu\n", VendorDataScript.Unix("\uFEFF#!/bin/sh\r\nset -eu\r\n"));
        Assert.Equal(VendorDataScript.Text, VendorDataScript.Unix(VendorDataScript.Text.Replace("\n", "\r\n", StringComparison.Ordinal)));
    }

    [Fact]
    public void InstallsWhatTheHandOverPutsOnTheSeedWhereTheAgentLooksForIt()
    {
        string[] lines = Lines;

        Assert.Contains($"seed={LinuxAgentFiles.SeedMount}", lines);
        Assert.Contains($"install -d -m 0755 {LinuxAgentFiles.ProgramFolder}", lines);
        Assert.Contains($"install -d -m 0700 {LinuxAgentFiles.StateFolder} {LinuxAgentFiles.StateFolder}/run", lines);
        Assert.Contains($"install -m 0755 \"$seed/{LinuxAgentFiles.SeedAgent}\" {LinuxAgentFiles.Agent}", lines);
        Assert.Contains($"install -m 0600 \"$seed/{LinuxAgentFiles.SeedConfiguration}\" {LinuxAgentFiles.Configuration}", lines);
        Assert.Contains($"install -m 0600 \"$seed/{LinuxAgentFiles.SeedState}\" {LinuxAgentFiles.State}", lines);
        Assert.Contains($"install -m 0600 \"$seed/{LinuxAgentFiles.SeedToken}\" {LinuxAgentFiles.Token}", lines);
        Assert.Contains($"cat > {LinuxAgentFiles.Unit} <<'UNIT'", lines);
        Assert.Contains($"    if [ -f \"$seed/{LinuxAgentFiles.SeedToken}\" ]; then", lines);
        Assert.Contains($"LABEL={CloudInitSeed.Label} ", VendorDataScript.Text, StringComparison.Ordinal);
        Assert.Contains($"/dev/disk/by-label/{CloudInitSeed.Label} ", VendorDataScript.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesTheUnitThatStartsTheAgentInTheInstalledLinux()
    {
        string text = VendorDataScript.Text;
        int start = text.IndexOf("<<'UNIT'\n", StringComparison.Ordinal) + "<<'UNIT'\n".Length;
        string unit = text[start..text.IndexOf("\nUNIT\n", StringComparison.Ordinal)];

        Assert.Equal(Unit.ReplaceLineEndings("\n"), unit);
        Assert.Contains($"ExecStart={LinuxAgentFiles.Agent} {LinuxAgentFiles.Argument}", unit.Split('\n'));
        Assert.Contains($"ConditionPathExists={LinuxAgentFiles.Agent}", unit.Split('\n'));
        Assert.Equal("--linux-phase", LinuxAgentFiles.Argument);

        // systemd signals the agent alone at a stop. The agent asks its script to end, once, and gives it its moment.
        Assert.Contains("KillMode=mixed", unit.Split('\n'));

        // Not ordered after cloud-init's last stage, so the agent can say that it waits for cloud-init.
        Assert.DoesNotContain("cloud", unit, StringComparison.Ordinal);
    }

    // The lines the tries on real images went through: the exit trap and the search for the seed that holds the run.
    [Fact]
    public void FindsTheSeedByItsTokenAndLetsGoOfItHoweverTheScriptEnds()
    {
        const string Tried =
            """
            seed=/run/ddt-seed
            mkdir -p "$seed"
            trap 'code=$?; umount "$seed" 2>/dev/null || true; rmdir "$seed" 2>/dev/null || true; [ "$code" -eq 0 ] || say "the agent could not be installed (exit code $code), so the run does not go on in this Linux." >&2' EXIT
            # The seed the run was handed over on is the CIDATA volume that holds the run's token.
            found=
            for device in /dev/disk/by-label/CIDATA $(blkid -t LABEL=CIDATA -o device 2>/dev/null || true); do
                [ -b "$device" ] || continue
                mount -t vfat -o rw,umask=077 "$device" "$seed" 2>/dev/null || continue
                if [ -f "$seed/ddt/token" ]; then
                    found=$device
                    break
                fi
                umount "$seed"
            done
            if [ -z "$found" ]; then
                say "no seed holds a run to go on with, so nothing was installed."
                exit 0
            fi
            """;
        string[] tried = Tried.ReplaceLineEndings("\n").Split('\n');
        List<string> lines = [.. Lines];
        int start = lines.IndexOf($"seed={LinuxAgentFiles.SeedMount}");

        Assert.Equal(tried, lines[start..(start + tried.Length)]);

        // Under set -e a command that fails inside the trap ends it, so neither may fail where nothing is mounted.
        Assert.Equal(2, tried[2].Split(" 2>/dev/null || true;").Length - 1);
    }

    // A machine that loses power in between must never be left with the token gone and no agent to start.
    [Fact]
    public void WipesTheTokenOnlyOnceTheAgentIsInstalledForGood()
    {
        List<string> lines = [.. Lines];
        int enabled = lines.IndexOf($"systemctl enable {LinuxAgentFiles.UnitName}");
        int synced = lines.IndexOf("sync");
        int wiped = lines.FindIndex(line => line.Contains($"dd if=/dev/zero of=\"$seed/{LinuxAgentFiles.SeedToken}\"", StringComparison.Ordinal));
        int started = lines.IndexOf($"systemctl start --no-block {LinuxAgentFiles.UnitName}");

        Assert.True(enabled > 0 && enabled < synced && synced < wiped && wiped < started, $"enable {enabled}, sync {synced}, wipe {wiped}, start {started}");
        Assert.Contains("conv=notrunc,fsync", lines[wiped], StringComparison.Ordinal);
        Assert.Contains($"rm -f \"$seed/{LinuxAgentFiles.SeedToken}\"", lines[wiped], StringComparison.Ordinal);
    }

    // What the spike's hardening added to the first draft: systemd has to run, and every outcome is said.
    [Fact]
    public void RefusesAnImageWithoutSystemdAndSaysHowItEnded()
    {
        string text = VendorDataScript.Text;

        Assert.Equal("set -eu", Lines[5]);
        Assert.Contains("if ! command -v systemctl >/dev/null || [ ! -d /run/systemd/system ]; then", Lines);
        Assert.Contains("say \"this image does not run systemd, so the run cannot go on in it.\" >&2", text, StringComparison.Ordinal);
        Assert.Contains("say \"no seed holds a run to go on with, so nothing was installed.\"", text, StringComparison.Ordinal);
        Assert.Contains("say \"the agent could not be installed (exit code $code), so the run does not go on in this Linux.\" >&2", text, StringComparison.Ordinal);
        Assert.Contains("say \"the agent is installed from $found and the run's token is off the seed.\"", text, StringComparison.Ordinal);
        Assert.Contains("the run's token could not be removed from the seed. The agent tries again.", text, StringComparison.Ordinal);
        Assert.EndsWith(
            "say \"ddt-agent.service is started. It waits for cloud-init, goes on with the task sequence and removes itself at the end.\"\n",
            text,
            StringComparison.Ordinal);
    }
}
