// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // What a seed's user-data does to a run that goes on in the installed Linux. DDT's agent gets there as vendor data,
    // which user-data can switch off. A sentence says "may" where DDT could not read the key it names.

    public static readonly MessageTemplate SequenceUserDataNotCheckedWarning = Define(
        "sequence.userDataNotCheckedWarning",
        "DDT could not check this user-data: it reads only a #cloud-config document whose top level has one key per line. The run " +
        "goes on in the installed Linux only if cloud-init runs vendor scripts there, so vendor_data has to stay enabled, and " +
        "cloud_final_modules, if set, has to name scripts_vendor.");

    public static readonly MessageTemplate SequenceUserDataVendorDataWarning = Define(
        "sequence.userDataVendorDataWarning",
        "{how, select, notMapping {The user-data sets vendor_data to something that is not a mapping, so cloud-init reads no vendor " +
        "data.} notEnabled {The user-data sets enabled under vendor_data to something other than true, so cloud-init reads no vendor " +
        "data.} other {DDT could not read what the user-data sets vendor_data to. It may switch vendor data off.}} DDT's agent gets " +
        "into the installed Linux as vendor data, and without it the steps there never run. Leave vendor_data out, or set its enabled " +
        "to true.");

    public static readonly MessageTemplate SequenceUserDataVendorPrefixWarning = Define(
        "sequence.userDataVendorPrefixWarning",
        "The user-data sets prefix under vendor_data, so cloud-init starts vendor scripts through that command. One of them is DDT's, " +
        "which installs the agent in the installed Linux. If the command does not run it to its end, the steps there never run.");

    public static readonly MessageTemplate SequenceUserDataFinalModulesWarning = Define(
        "sequence.userDataFinalModulesWarning",
        "{how, select, missing {The user-data sets cloud_final_modules, and the list names neither scripts_vendor nor scripts-vendor, " +
        "so cloud-init runs no vendor scripts.} other {DDT could not read what the user-data sets cloud_final_modules to. If the list " +
        "does not name scripts_vendor, cloud-init runs no vendor scripts.}} DDT's script, which installs the agent in the installed " +
        "Linux, is a vendor script, and without it the steps there never run. Add scripts_vendor to the list.");

    public static readonly MessageTemplate SequenceUserDataPowerStateNote = Define(
        "sequence.userDataPowerStateNote",
        "{mode, select, reboot {The user-data restarts the machine with power_state. The run waits for that restart and then goes on " +
        "with its steps in the installed Linux.} off {The user-data shuts the machine down with power_state. The run waits until " +
        "someone switches the machine on again, and then goes on with its steps in the installed Linux.} other {The user-data sets " +
        "power_state, so cloud-init may restart the machine or shut it down. The run waits for that before its steps in the installed " +
        "Linux, and after a shutdown until someone switches the machine on again.}}");
}
