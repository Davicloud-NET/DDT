// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using DDT.Agent.WindowsUpdates;
using Xunit;

namespace DDT.Agent.Tests;

// The VARIANTs Windows Update answers with, as the spike saw them.
public sealed class AutomationValuesTests
{
    [Fact]
    public void AVariantIsTwentyFourBytesOnX64()
    {
        Assert.Equal(24, Unsafe.SizeOf<ComVariant>());
    }

    // VT_DECIMAL: the DECIMAL fills the first 16 bytes, and its reserved word is vt.
    [Fact]
    public void ADecimalSizeReadsFromTheNativeLayout()
    {
        byte[] raw = new byte[24];
        BinaryPrimitives.WriteUInt16LittleEndian(raw, (ushort)VarEnum.VT_DECIMAL);
        BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(8), 97_216_300_205);

        ComVariant value = MemoryMarshal.Read<ComVariant>(raw);

        Assert.Equal(97_216_300_205m, AutomationValues.ToDecimal(value, "MaxDownloadSize"));
    }

    [Fact]
    public void ADecimalWithScaleSignAndHighWordReadsWhole()
    {
        byte[] raw = new byte[24];
        BinaryPrimitives.WriteUInt16LittleEndian(raw, (ushort)VarEnum.VT_DECIMAL);
        raw[2] = 2;
        raw[3] = 0x80;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(8), 5);

        ComVariant value = MemoryMarshal.Read<ComVariant>(raw);

        Assert.Equal(-new decimal(5, 0, 1, false, 2), AutomationValues.ToDecimal(value, "TotalBytes"));
    }

    [Fact]
    public void ADecimalMadeHereReadsBack()
    {
        Assert.Equal(2_085_000_000.5m, AutomationValues.ToDecimal(ComVariant.Create(2_085_000_000.5m), "TotalBytes"));
    }

    [Fact]
    public void AVariantBoolIsTrueForMinusOneAndFalseForZero()
    {
        Assert.True(AutomationValues.ToBool(ComVariant.CreateRaw(VarEnum.VT_BOOL, (short)-1), "IsBusy"));
        Assert.False(AutomationValues.ToBool(ComVariant.CreateRaw(VarEnum.VT_BOOL, (short)0), "IsBusy"));
    }

    [Fact]
    public void AnEnumOrLongReadsAsAnInt()
    {
        Assert.Equal(2, AutomationValues.ToInt(AutomationValues.Int(2), "ResultCode"));
        Assert.Equal(unchecked((int)0x80240032), AutomationValues.ToInt(ComVariant.CreateRaw(VarEnum.VT_I4, unchecked((int)0x80240032)), "HResult"));
    }

    [Fact]
    public void AStringReadsBackAndANullBStrReadsAsNull()
    {
        ComVariant text = AutomationValues.Text("2026-11 Cumulative Update");

        try
        {
            Assert.Equal("2026-11 Cumulative Update", AutomationValues.ToText(text, "Title"));
        }
        finally
        {
            text.Dispose();
        }

        Assert.Null(AutomationValues.ToText(ComVariant.CreateRaw(VarEnum.VT_BSTR, (nint)0), "MsrcSeverity"));
        Assert.Null(AutomationValues.ToText(default, "MsrcSeverity"));
    }

    [Fact]
    public void AnObjectArgumentCarriesItsPointerAsItIs()
    {
        Assert.Equal(VarEnum.VT_DISPATCH, AutomationValues.Dispatch(0x1234).VarType);
        Assert.Equal(0x1234, AutomationValues.ToPointer(AutomationValues.Dispatch(0x1234), "Updates"));
        Assert.Equal(VarEnum.VT_UNKNOWN, AutomationValues.Unknown(0x5678).VarType);
        Assert.Equal(0, AutomationValues.ToPointer(default, "Updates"));
    }

    [Fact]
    public void AnUnexpectedTypeFailsAsATypeMismatchThatNamesTheMember()
    {
        ComCallException failure = Assert.Throws<ComCallException>(() => AutomationValues.ToBool(AutomationValues.Int(1), "IsCompleted"));

        Assert.Equal(AutomationValues.TypeMismatch, failure.Code);
        Assert.Equal("IsCompleted (0x80020005): answered with VARIANT type 3", failure.Message);
    }
}
