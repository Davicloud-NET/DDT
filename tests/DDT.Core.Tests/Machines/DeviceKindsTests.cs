// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Core.Machines;
using Xunit;

namespace DDT.Core.Tests.Machines;

public sealed class DeviceKindsTests
{
    [Theory]
    [InlineData(8, DeviceKind.Laptop)]
    [InlineData(9, DeviceKind.Laptop)]
    [InlineData(10, DeviceKind.Laptop)]
    [InlineData(14, DeviceKind.Laptop)]
    [InlineData(31, DeviceKind.Laptop)]
    [InlineData(32, DeviceKind.Laptop)]
    [InlineData(30, DeviceKind.Tablet)]
    [InlineData(3, DeviceKind.Desktop)]
    [InlineData(4, DeviceKind.Desktop)]
    [InlineData(5, DeviceKind.Desktop)]
    [InlineData(6, DeviceKind.Desktop)]
    [InlineData(7, DeviceKind.Desktop)]
    [InlineData(13, DeviceKind.Desktop)]
    [InlineData(15, DeviceKind.Desktop)]
    [InlineData(16, DeviceKind.Desktop)]
    [InlineData(35, DeviceKind.Desktop)]
    [InlineData(36, DeviceKind.Desktop)]
    [InlineData(17, DeviceKind.Server)]
    [InlineData(23, DeviceKind.Server)]
    [InlineData(25, DeviceKind.Server)]
    [InlineData(28, DeviceKind.Server)]
    [InlineData(29, DeviceKind.Server)]
    public void TellsTheKindFromTheChassisType(int chassisType, DeviceKind expected)
    {
        Assert.Equal(expected, DeviceKinds.Classify("Dell Inc.", "OptiPlex 7010", chassisType));
    }

    // Other, Unknown, a docking station, an expansion chassis, and values no version of SMBIOS defines.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(12)]
    [InlineData(19)]
    [InlineData(127)]
    public void KnowsNothingFromAChassisTypeThatSaysNothingAboutTheComputer(int chassisType)
    {
        Assert.Equal(DeviceKind.Unknown, DeviceKinds.Classify("Dell Inc.", "OptiPlex 7010", chassisType));
    }

    [Fact]
    public void KnowsNothingWithoutAChassisType()
    {
        Assert.Equal(DeviceKind.Unknown, DeviceKinds.Classify("Dell Inc.", "OptiPlex 7010", null));
        Assert.Equal(DeviceKind.Unknown, DeviceKinds.Classify(null, null, null));
    }

    // As the hypervisors report themselves. Most of them report a desktop chassis or Other, which the names override.
    [Theory]
    [InlineData("Microsoft Corporation", "Virtual Machine", 3)]
    [InlineData("VMware, Inc.", "VMware7,1", 1)]
    [InlineData("innotek GmbH", "VirtualBox", 1)]
    [InlineData("QEMU", "Standard PC (Q35 + ICH9, 2009)", 1)]
    [InlineData("Red Hat", "KVM", 1)]
    [InlineData("Xen", "HVM domU", 1)]
    [InlineData("Parallels Software International Inc.", "Parallels Virtual Platform", 1)]
    [InlineData("Bochs", "Bochs", 1)]
    [InlineData("vmware, inc.", "vmware20,1", null)]
    public void TellsAVirtualMachineByItsManufacturerOrModel(string manufacturer, string model, int? chassisType)
    {
        Assert.Equal(DeviceKind.Virtual, DeviceKinds.Classify(manufacturer, model, chassisType));
    }

    [Fact]
    public void TellsAVirtualMachineByEitherName()
    {
        Assert.Equal(DeviceKind.Virtual, DeviceKinds.Classify(null, "Virtual Machine", 9));
        Assert.Equal(DeviceKind.Virtual, DeviceKinds.Classify("QEMU", null, 9));
    }
}
