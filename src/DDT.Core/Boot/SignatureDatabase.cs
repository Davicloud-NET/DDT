// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Core.Boot;

// Reads the UEFI signature databases db and dbx (UEFI 2.10 section 32.4.1). A database is EFI_SIGNATURE_LISTs back to
// back. Each list has a signature type, a header and signatures of one size. Each signature is an owner GUID and data.
public static class SignatureDatabase
{
    // EFI_CERT_X509_GUID. In lists of this type, each signature's data is a DER certificate.
    public static readonly Guid X509Type = Guid.Parse("a5c059a1-94e4-4aa7-87b5-ab155c2bf072");

    // The type, then SignatureListSize, SignatureHeaderSize and SignatureSize.
    private const int ListHeaderLength = 16 + (3 * sizeof(uint));
    private const int OwnerLength = 16;

    // The certificates in the database's X509 lists, in order. Lists of other types, such as hashes, are skipped.
    // Throws InvalidDataException when the sizes don't add up.
    public static IReadOnlyList<byte[]> Certificates(ReadOnlySpan<byte> database)
    {
        List<byte[]> certificates = [];

        while (!database.IsEmpty)
        {
            if (database.Length < ListHeaderLength)
            {
                throw new InvalidDataException("The signature database ends inside a list header.");
            }

            Guid type = new(database[..16]);
            uint listSize = BinaryPrimitives.ReadUInt32LittleEndian(database[16..]);
            uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(database[20..]);
            uint signatureSize = BinaryPrimitives.ReadUInt32LittleEndian(database[24..]);

            if (listSize < ListHeaderLength
                || listSize > (uint)database.Length
                || headerSize > listSize - ListHeaderLength
                || signatureSize < OwnerLength
                || (listSize - ListHeaderLength - headerSize) % signatureSize != 0)
            {
                throw new InvalidDataException("The sizes in a list of the signature database do not add up.");
            }

            if (type == X509Type)
            {
                for (int offset = ListHeaderLength + (int)headerSize; offset < listSize; offset += (int)signatureSize)
                {
                    certificates.Add(database.Slice(offset + OwnerLength, (int)signatureSize - OwnerLength).ToArray());
                }
            }

            database = database[(int)listSize..];
        }

        return certificates;
    }
}
