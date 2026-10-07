// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class StorageDescriptorReaderTests
{
    [Fact]
    public void ReadsTheBusTypeRemovableFlagAndModel()
    {
        byte[] descriptor = Descriptor(removable: false, StorageBusType.Scsi, "Msft    ", "Virtual Disk    ");

        StorageDeviceInfo device = StorageDescriptorReader.Read(descriptor);

        Assert.Equal(new StorageDeviceInfo(false, StorageBusType.Scsi, "Msft Virtual Disk"), device);
        Assert.Equal(descriptor.Length, StorageDescriptorReader.ReadSize(descriptor));
    }

    [Fact]
    public void AModelWithoutVendorIsTheProductAlone()
    {
        StorageDeviceInfo device = StorageDescriptorReader.Read(Descriptor(removable: true, StorageBusType.Nvme, null, "Samsung SSD 980 PRO 1TB"));

        Assert.Equal(new StorageDeviceInfo(true, StorageBusType.Nvme, "Samsung SSD 980 PRO 1TB"), device);
    }

    [Fact]
    public void ADiskWithoutNamesHasNoModel() =>
        Assert.Null(StorageDescriptorReader.Read(Descriptor(removable: false, StorageBusType.Sata, null, null)).Model);

    // STORAGE_DEVICE_DESCRIPTOR with the vendor and product strings after the fixed part, as Windows returns it.
    private static byte[] Descriptor(bool removable, StorageBusType busType, string? vendor, string? product)
    {
        List<byte> strings = [];
        uint vendorOffset = Append(vendor);
        uint productOffset = Append(product);

        byte[] descriptor = [.. new byte[40], .. strings];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(4), (uint)descriptor.Length);
        descriptor[10] = removable ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(12), vendorOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(16), productOffset);
        BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(28), (int)busType);

        return descriptor;

        uint Append(string? text)
        {
            if (text is null)
            {
                return 0;
            }

            uint offset = (uint)(40 + strings.Count);
            strings.AddRange(Encoding.ASCII.GetBytes(text));
            strings.Add(0);

            return offset;
        }
    }
}
