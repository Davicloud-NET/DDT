// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Contracts.Images;
using DDT.Core.Boot;
using Xunit;

namespace DDT.Core.Tests.Boot;

public sealed class SignatureDatabaseTests
{
    // EFI_CERT_SHA256_GUID. Each signature's data is a hash, like in dbx.
    private static readonly Guid s_sha256Type = Guid.Parse("c1c41626-504c-4092-aca9-41f936934328");
    private static readonly Guid s_owner = Guid.Parse("77fa9abd-0359-4d32-bd60-28f4e78f784b");

    // One EFI_SIGNATURE_LIST whose signatures all have the length of the first.
    internal static byte[] List(Guid type, int headerLength, params byte[][] signatures)
    {
        int signatureSize = 16 + signatures[0].Length;
        byte[] list = new byte[28 + headerLength + (signatures.Length * signatureSize)];
        type.TryWriteBytes(list);
        BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(16), (uint)list.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(20), (uint)headerLength);
        BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(24), (uint)signatureSize);

        for (int index = 0; index < signatures.Length; index++)
        {
            int offset = 28 + headerLength + (index * signatureSize);
            s_owner.TryWriteBytes(list.AsSpan(offset));
            signatures[index].CopyTo(list, offset + 16);
        }

        return list;
    }

    private static byte[] Bytes(int length, byte value) => Enumerable.Repeat(value, length).ToArray();

    [Fact]
    public void ReadsTheCertificatesOfTheX509ListsAndSkipsTheOthers()
    {
        byte[] database =
        [
            .. List(s_sha256Type, 0, Bytes(32, 1), Bytes(32, 2)),
            .. List(SignatureDatabase.X509Type, 0, Bytes(900, 3)),
            .. List(SignatureDatabase.X509Type, 4, Bytes(700, 4), Bytes(700, 5)),
        ];

        Assert.Equal([Bytes(900, 3), Bytes(700, 4), Bytes(700, 5)], SignatureDatabase.Certificates(database));
        Assert.Empty(SignatureDatabase.Certificates([]));
    }

    [Fact]
    public void RefusesSizesThatDoNotAddUp()
    {
        byte[] list = List(SignatureDatabase.X509Type, 0, Bytes(100, 1));

        Assert.Throws<InvalidDataException>(() => SignatureDatabase.Certificates(list[..27]));
        Assert.Throws<InvalidDataException>(() => SignatureDatabase.Certificates(list[..^1]));

        byte[] uneven = (byte[])list.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(uneven.AsSpan(24), 30);
        Assert.Throws<InvalidDataException>(() => SignatureDatabase.Certificates(uneven));

        byte[] noSignatureSize = (byte[])list.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(noSignatureSize.AsSpan(24), 0);
        Assert.Throws<InvalidDataException>(() => SignatureDatabase.Certificates(noSignatureSize));

        byte[] largeHeader = (byte[])list.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(largeHeader.AsSpan(20), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => SignatureDatabase.Certificates(largeHeader));
    }

    [Fact]
    public void TrustsNoOtherCertificateAsMicrosoftsUefiCa()
    {
        Assert.Equal(UefiCa.None, MicrosoftUefiCa.TrustedBy(List(SignatureDatabase.X509Type, 0, Bytes(1000, 7))));
        Assert.Equal(UefiCa.None, MicrosoftUefiCa.TrustedBy([]));
    }

    [Theory]
    [InlineData(UefiCa.Microsoft2011, UefiCa.Microsoft2011, false)]
    [InlineData(UefiCa.Microsoft2011, UefiCa.Microsoft2023, true)]
    [InlineData(UefiCa.Microsoft2023, UefiCa.Microsoft2011, true)]
    [InlineData(UefiCa.Microsoft2011, UefiCa.Microsoft2011 | UefiCa.Microsoft2023, false)]
    [InlineData(UefiCa.None, UefiCa.Microsoft2011 | UefiCa.Microsoft2023, true)]
    public void TellsWhetherTheFirmwareTrustsNoneOfTheCasAFileIsSignedUnder(UefiCa trusted, UefiCa signedUnder, bool untrusted)
    {
        Assert.Equal(untrusted, MicrosoftUefiCa.Untrusted(trusted, signedUnder));
    }

    [Fact]
    public void KnowsNothingWhereEitherSideIsUnknown()
    {
        Assert.False(MicrosoftUefiCa.Untrusted(null, UefiCa.Microsoft2023));
        Assert.False(MicrosoftUefiCa.Untrusted(UefiCa.None, null));
        Assert.False(MicrosoftUefiCa.Untrusted(UefiCa.None, UefiCa.None));
    }
}
