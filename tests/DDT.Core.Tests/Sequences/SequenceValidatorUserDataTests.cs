// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// A seed's user-data can keep cloud-init from running the vendor-data script that takes the agent into the installed
// Linux. The files under UserData are those the lab wrote when it tried that on Debian 13 and Ubuntu 24.04 images.
public sealed class SequenceValidatorUserDataTests
{
    private const string NotChecked = "sequence.userDataNotCheckedWarning";
    private const string VendorData = "sequence.userDataVendorDataWarning";
    private const string VendorPrefix = "sequence.userDataVendorPrefixWarning";
    private const string FinalModules = "sequence.userDataFinalModulesWarning";
    private const string PowerState = "sequence.userDataPowerStateNote";

    private static string Lab(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sequences", "UserData", $"{name}.txt"));

    // A sequence that goes on in the installed Linux, or one that ends in the Linux boot environment.
    private static SequenceAnalysis Analyse(string userData, bool goesOn = true)
    {
        WriteCloudInitSeedStep seed = WriteSeed() with { UserData = userData };
        SequenceAnalysis analysis = SequenceValidator.Analyse(Definition(WriteRawImage(), seed, goesOn ? InLinux() : Shell()));

        Assert.Empty(analysis.Problems);
        Assert.All(analysis.Warnings.Concat(analysis.Notes), finding => Assert.Equal((seed.Id, "userData"), (finding.StepId, finding.Field)));

        return analysis;
    }

    // Each finding as its code and the form of its sentence, such as "notEnabled".
    private static string[] Said(IEnumerable<SequenceProblem> findings) =>
        [.. findings.Select(finding => finding.Args is { Count: > 0 } args ? $"{finding.Code} {args.Values.Single()}" : finding.Code ?? "")];

    private static string Document(string keys) => $"#cloud-config\n{keys}\n";

    // What real cloud-init did with each: the script ran, or it did not and the run's token stayed on the seed.
    [Theory]
    [InlineData("admin-a")]
    [InlineData("admin-b")]
    [InlineData("fail-runcmd")]
    [InlineData("runcmd-reboot")]
    [InlineData("vendor-prefix")]
    [InlineData("vendor-off", $"{VendorData} notEnabled")]
    [InlineData("vendor-false", $"{VendorData} notMapping")]
    [InlineData("final-modules", $"{FinalModules} missing")]
    [InlineData("shell-script", NotChecked)]
    [InlineData("invalid-yaml", NotChecked)]
    public void WarnsOfTheLabsUserDataThatKeptTheAgentOutOfLinux(string file, params string[] warnings)
    {
        SequenceAnalysis analysis = Analyse(Lab(file));

        Assert.Equal(warnings, Said(analysis.Warnings));
        Assert.Empty(analysis.Notes);
    }

    [Theory]
    [InlineData("power-state")]
    [InlineData("power-delay")]
    public void NotesTheRestartOfTheLabsUserDataWithoutWarning(string file)
    {
        SequenceAnalysis analysis = Analyse(Lab(file));

        Assert.Empty(analysis.Warnings);
        Assert.Equal([$"{PowerState} reboot"], Said(analysis.Notes));
        Assert.Equal(
            "The user-data restarts the machine with power_state. The run waits for that restart and then goes on with its steps in the " +
            "installed Linux.",
            analysis.Notes[0].Message);
    }

    [Theory]
    [InlineData("vendor_data: {enabled: false}", "notEnabled")]
    [InlineData("vendor_data: { enabled : no , prefix: [] }", "notEnabled")]
    [InlineData("vendor_data: {\"enabled\":false}", "notEnabled")]
    [InlineData("vendor_data:\n  enabled: false", "notEnabled")]
    [InlineData("vendor_data:\n    # switched off for now\n    enabled: Off   # really", "notEnabled")]
    [InlineData("\"vendor_data\":\n  'enabled': \"0\"", "notEnabled")]
    [InlineData("vendor_data:\n  enabled:", "notEnabled")]
    [InlineData("vendor_data: {enabled:\n  false}", "notEnabled")]
    [InlineData("vendor_data: {enabled: true}\nruncmd: [ls]\nvendor_data: {enabled: false}", "notEnabled")]
    [InlineData("vendor_data: false", "notMapping")]
    [InlineData("vendor_data: off", "notMapping")]
    [InlineData("vendor_data:", "notMapping")]
    [InlineData("vendor_data: ~", "notMapping")]
    [InlineData("vendor_data: [enabled]", "notMapping")]
    [InlineData("vendor_data:\n  - enabled: false", "notMapping")]
    [InlineData("vendor_data: *defaults", "unread")]
    [InlineData("vendor_data: &vendor {enabled: true}", "unread")]
    [InlineData("vendor_data: {enabled: \"{{VendorData}}\"}", "unread")]
    [InlineData("vendor_data:\n  enabled: !!bool yes", "unread")]
    public void WarnsOfVendorDataThatIsOffInBlockAndInFlowStyle(string keys, string how)
    {
        SequenceAnalysis analysis = Analyse(Document(keys));

        Assert.Equal([$"{VendorData} {how}"], Said(analysis.Warnings));
    }

    [Theory]
    [InlineData("vendor_data: {enabled: true}")]
    [InlineData("vendor_data: {enabled: True, disabled_handlers: []}")]
    [InlineData("vendor_data:\n  enabled: yes")]
    [InlineData("vendor_data:\n  enabled: \"ON\"")]
    [InlineData("vendor_data:\n  enabled: 1")]
    [InlineData("vendor_data: {}")]
    [InlineData("vendor_data: {prefix: []}")]
    [InlineData("vendor_data:\n  prefix:")]
    [InlineData("vendor_data: {enabled: false}\nvendor_data: {enabled: true}")]
    [InlineData("# vendor_data: {enabled: false}")]
    [InlineData("  # vendor_data: false")]
    [InlineData("write_files:\n  - path: /etc/cloud/cloud.cfg.d/99.cfg\n    content: |\n      vendor_data: {enabled: false}\n      cloud_final_modules: []")]
    [InlineData("users:\n  - name: admin\n    vendor_data: false")]
    [InlineData("final_message: \"vendor_data: {enabled: false}\"")]
    [InlineData("bootcmd:\n- [sh, -c, 'echo vendor_data: false']")]
    [InlineData("---\nhostname: \"{{ComputerName}}\"\nmessage: it's { fine\nusers: [default]")]
    public void SaysNothingWhileVendorDataStaysOn(string keys)
    {
        SequenceAnalysis analysis = Analyse(Document(keys));

        Assert.Empty(analysis.Warnings);
        Assert.Empty(analysis.Notes);
    }

    [Theory]
    [InlineData("vendor_data: {prefix: [timeout, 30]}")]
    [InlineData("vendor_data:\n  enabled: true\n  prefix:\n    - /usr/bin/ltrace")]
    [InlineData("vendor_data:\n  prefix: /usr/bin/time")]
    public void WarnsOfAPrefixThatVendorScriptsAreStartedThrough(string keys)
    {
        SequenceAnalysis analysis = Analyse(Document(keys));

        Assert.Equal([VendorPrefix], Said(analysis.Warnings));
        Assert.Equal(
            "The user-data sets prefix under vendor_data, so cloud-init starts vendor scripts through that command. One of them is DDT's, " +
            "which installs the agent in the installed Linux. If the command does not run it to its end, the steps there never run.",
            analysis.Warnings[0].Message);
    }

    [Theory]
    [InlineData("cloud_final_modules:\n  - scripts_user\n  - final_message", "missing")]
    [InlineData("cloud_final_modules:\n- scripts-user\n- [final-message, always]", "missing")]
    [InlineData("cloud_final_modules: [scripts_user, final_message]", "missing")]
    [InlineData("cloud_final_modules: []", "missing")]
    [InlineData("cloud_final_modules:\n  # - scripts_vendor\n  - scripts_user   # not scripts_vendor", "missing")]
    [InlineData("cloud_final_modules:\n  - scripts_vendors\n  - my-scripts_vendor", "missing")]
    [InlineData("cloud_final_modules:", "unread")]
    [InlineData("cloud_final_modules: *modules", "unread")]
    [InlineData("cloud_final_modules: [\"{{Modules}}\"]", "unread")]
    public void WarnsOfFinalModulesWithoutTheOneThatRunsVendorScripts(string keys, string how)
    {
        SequenceAnalysis analysis = Analyse(Document(keys));

        Assert.Equal([$"{FinalModules} {how}"], Said(analysis.Warnings));
    }

    // Debian's cloud.cfg writes the module with a dash, Ubuntu's with an underscore, and cloud-init reads both.
    [Theory]
    [InlineData("cloud_final_modules:\n  - scripts_vendor\n  - scripts_user")]
    [InlineData("cloud_final_modules:\n- scripts-vendor\n- scripts-user")]
    [InlineData("cloud_final_modules: [package_update_upgrade_install, [scripts_vendor, always], scripts_user]")]
    [InlineData("cloud_final_modules: [\n  'scripts-vendor',\n  \"final_message\"\n]\nruncmd: [ls]")]
    [InlineData("cloud_init_modules:\n  - seed_random\ncloud_config_modules:\n  - runcmd")]
    public void SaysNothingOfFinalModulesThatNameIt(string keys) => Assert.Empty(Analyse(Document(keys)).Warnings);

    [Theory]
    [InlineData("power_state:\n  mode: reboot\n  delay: now", "reboot")]
    [InlineData("power_state: {mode: reboot, condition: true}", "reboot")]
    [InlineData("power_state: {delay: 5, mode: \"poweroff\"}", "off")]
    [InlineData("power_state:\n  mode: halt", "off")]
    [InlineData("power_state:\n  mode: reboot\n  condition: test -e /run/restart-wanted", "unread")]
    [InlineData("power_state:\n  mode: \"{{PowerMode}}\"", "unread")]
    [InlineData("power_state:\n  delay: now", "unread")]
    [InlineData("power_state: *power", "unread")]
    public void NotesWhatPowerStateDoesToTheRun(string keys, string mode)
    {
        SequenceAnalysis analysis = Analyse(Document(keys));

        Assert.Empty(analysis.Warnings);
        Assert.Equal([$"{PowerState} {mode}"], Said(analysis.Notes));
    }

    // cloud-init then leaves the machine as it is, as the agent saw on real images.
    [Theory]
    [InlineData("power_state:\n  mode: reboot\n  condition: false")]
    [InlineData("power_state: {mode: poweroff, condition: False}")]
    [InlineData("runcmd:\n  - [reboot]")]
    public void NotesNoPowerStateWhoseConditionIsFalse(string keys) => Assert.Empty(Analyse(Document(keys)).Notes);

    [Theory]
    [InlineData("Content-Type: multipart/mixed; boundary=\"==BOUNDARY==\"\nMIME-Version: 1.0\n\n--==BOUNDARY==\n")]
    [InlineData("#include\nhttps://example.org/user-data\n")]
    [InlineData("#include-once\nhttps://example.org/user-data\n")]
    [InlineData("## template: jinja\n#cloud-config\nvendor_data: {enabled: false}\n")]
    [InlineData("#cloud-config-archive\n- type: text/cloud-config\n  content: \"vendor_data: {enabled: false}\"\n")]
    [InlineData("#cloud-config-jsonp\n[{ \"op\": \"add\", \"path\": \"/vendor_data\", \"value\": false }]\n")]
    [InlineData("#!/bin/sh\necho hello\n")]
    [InlineData("#cloud-boothook\n#!/bin/sh\necho early\n")]
    [InlineData("\u001f\u008b\u0008binary")]
    [InlineData("{{UserData}}")]
    [InlineData("vendor_data: {enabled: false}\n")]
    [InlineData("#cloud-config\n{vendor_data: {enabled: false}}\n")]
    [InlineData("#cloud-config\nbase: &base\n  enabled: false\n<<: *base\n")]
    [InlineData("#cloud-config\n? vendor_data\n: false\n")]
    [InlineData("#cloud-config\nruncmd:\n\t- ls\n")]
    [InlineData("#cloud-config\nruncmd: [ 'echo never',\n")]
    [InlineData("#cloud-config\nhostname: a\n---\nvendor_data: false\n")]
    [InlineData("#cloud-config\n  vendor_data: false\n")]
    public void SaysWhenItCouldNotCheckTheUserData(string userData)
    {
        SequenceAnalysis analysis = Analyse(userData);

        Assert.Equal([NotChecked], Said(analysis.Warnings));
        Assert.Empty(analysis.Notes);
        Assert.Equal(
            "DDT could not check this user-data: it reads only a #cloud-config document whose top level has one key per line. The run " +
            "goes on in the installed Linux only if cloud-init runs vendor scripts there, so vendor_data has to stay enabled, and " +
            "cloud_final_modules, if set, has to name scripts_vendor.",
            analysis.Warnings[0].Message);
    }

    // cloud-init skips white space before the header, and a byte order mark is none of the text.
    [Theory]
    [InlineData("")]
    [InlineData(" \n\t\n")]
    [InlineData("#cloud-config")]
    [InlineData("#cloud-config\n")]
    [InlineData("\uFEFF\n\n#cloud-config\r\nusers:\r\n  - default\r\n")]
    [InlineData("#cloud-config\n# Only comments.\n\n")]
    public void SaysNothingOfUserDataThatSetsNothing(string userData)
    {
        SequenceAnalysis analysis = Analyse(userData);

        Assert.Empty(analysis.Warnings);
        Assert.Empty(analysis.Notes);
    }

    [Fact]
    public void SaysEveryKeyItFoundAndReadsCarriageReturns()
    {
        SequenceAnalysis analysis = Analyse(
            "#cloud-config\r\nvendor_data:\r\n  enabled: false\r\ncloud_final_modules:\r\n  - scripts_user\r\npower_state:\r\n  mode: poweroff\r\n");

        Assert.Equal([$"{VendorData} notEnabled", $"{FinalModules} missing"], Said(analysis.Warnings));
        Assert.Equal([$"{PowerState} off"], Said(analysis.Notes));
    }

    // Without steps in the installed Linux the run ends before cloud-init starts, and the user-data is the image's own business.
    [Theory]
    [InlineData("#cloud-config\nvendor_data: {enabled: false}\ncloud_final_modules: []\npower_state: {mode: reboot}\n")]
    [InlineData("#!/bin/sh\nreboot\n")]
    public void SaysNothingOfASequenceThatEndsInTheLinuxBootEnvironment(string userData)
    {
        SequenceAnalysis analysis = Analyse(userData, goesOn: false);

        Assert.Empty(analysis.Warnings);
        Assert.Empty(analysis.Notes);
    }

    [Fact]
    public void SaysWhatSwitchedVendorDataOffAndWhatToDo()
    {
        const string End =
            " DDT's agent gets into the installed Linux as vendor data, and without it the steps there never run. Leave vendor_data out, " +
            "or set its enabled to true.";

        Assert.Equal(
            "The user-data sets enabled under vendor_data to something other than true, so cloud-init reads no vendor data." + End,
            Analyse(Document("vendor_data: {enabled: false}")).Warnings[0].Message);
        Assert.Equal(
            "The user-data sets vendor_data to something that is not a mapping, so cloud-init reads no vendor data." + End,
            Analyse(Document("vendor_data: false")).Warnings[0].Message);
        Assert.Equal(
            "DDT could not read what the user-data sets vendor_data to. It may switch vendor data off." + End,
            Analyse(Document("vendor_data: *defaults")).Warnings[0].Message);
    }

    [Fact]
    public void SaysWhichModuleTheFinalModulesLack()
    {
        const string End =
            " DDT's script, which installs the agent in the installed Linux, is a vendor script, and without it the steps there never " +
            "run. Add scripts_vendor to the list.";

        Assert.Equal(
            "The user-data sets cloud_final_modules, and the list names neither scripts_vendor nor scripts-vendor, so cloud-init runs no " +
            "vendor scripts." + End,
            Analyse(Document("cloud_final_modules: [scripts_user]")).Warnings[0].Message);
        Assert.Equal(
            "DDT could not read what the user-data sets cloud_final_modules to. If the list does not name scripts_vendor, cloud-init runs " +
            "no vendor scripts." + End,
            Analyse(Document("cloud_final_modules: *modules")).Warnings[0].Message);
    }

    [Fact]
    public void SaysWhatTheRunWaitsForAfterAShutdownAndWhenItCannotTell()
    {
        Assert.Equal(
            "The user-data shuts the machine down with power_state. The run waits until someone switches the machine on again, and then " +
            "goes on with its steps in the installed Linux.",
            Analyse(Document("power_state: {mode: poweroff}")).Notes[0].Message);
        Assert.Equal(
            "The user-data sets power_state, so cloud-init may restart the machine or shut it down. The run waits for that before its " +
            "steps in the installed Linux, and after a shutdown until someone switches the machine on again.",
            Analyse(Document("power_state:\n  condition: test -e /run/restart-wanted")).Notes[0].Message);
    }
}
