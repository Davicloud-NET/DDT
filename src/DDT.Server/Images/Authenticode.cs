// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Images;

// Checks an EFI program's Authenticode signatures the way Secure Boot firmware does. Each signature is checked on its
// own against the file's hash and must chain to a certificate in trusted. A trusted certificate is an anchor even if it
// isn't self-signed. Like firmware, it ignores validity periods and key usages. Unlike firmware, it doesn't read dbx or
// SBAT, so a revoked file still counts as trusted.
public static class Authenticode
{
    private const string IndirectDataContentType = "1.3.6.1.4.1.311.2.1.4";
    private const int MaxChainLength = 8;

    public static AuthenticodeResult Check(byte[] file, IReadOnlyCollection<X509Certificate2> trusted)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(trusted);

        PeImage? image = PeImage.Read(file);

        if (image is null)
        {
            return new AuthenticodeResult(AuthenticodeStatus.Unreadable, null, null, "is not an EFI program");
        }

        if (image.CertificateTableLength == 0)
        {
            return new AuthenticodeResult(AuthenticodeStatus.NotSigned, image.Machine, null, "carries no signature");
        }

        if (image.Signatures() is not { } signatures)
        {
            return new AuthenticodeResult(AuthenticodeStatus.Unreadable, image.Machine, null, "has a damaged signature table");
        }

        Tally tally = new();

        foreach (byte[] signature in signatures)
        {
            Count(signature, image, trusted, tally);
        }

        if (tally.TrustedSigner is not null)
        {
            return new AuthenticodeResult(AuthenticodeStatus.Trusted, image.Machine, tally.TrustedSigner, null, tally.Anchors);
        }

        if (tally.OtherSigner is not null)
        {
            return new AuthenticodeResult(AuthenticodeStatus.SignedByOthers, image.Machine, tally.OtherSigner, null);
        }

        return tally.Mismatch
            ? new AuthenticodeResult(AuthenticodeStatus.NotSigned, image.Machine, null, "carries a signature that does not match its content")
            : new AuthenticodeResult(
                tally.Unreadable ? AuthenticodeStatus.Unreadable : AuthenticodeStatus.NotSigned,
                image.Machine,
                null,
                tally.Unreadable ? "carries a signature DDT cannot read" : "carries no signature");
    }

    // Every signature counts. A shim signed under both of Microsoft's CAs starts on a PC that trusts either one.
    private static void Count(byte[] signature, PeImage image, IReadOnlyCollection<X509Certificate2> trusted, Tally tally)
    {
        SignedCms cms = new();
        (HashAlgorithmName Algorithm, byte[] Digest)? indirect;

        try
        {
            cms.Decode(signature);
            indirect = cms.ContentInfo.ContentType.Value == IndirectDataContentType && cms.SignerInfos.Count == 1
                ? ReadIndirectData(cms.ContentInfo.Content)
                : null;
        }
        catch (Exception exception) when (exception is CryptographicException or AsnContentException)
        {
            indirect = null;
        }

        if (indirect is not { } content)
        {
            tally.Unreadable = true;

            return;
        }

        if (!tally.Hashes.TryGetValue(content.Algorithm, out byte[]? hash))
        {
            hash = image.Hash(content.Algorithm);
            tally.Hashes[content.Algorithm] = hash;
        }

        SignerInfo signer = cms.SignerInfos[0];

        if (!CryptographicOperations.FixedTimeEquals(hash, content.Digest) || signer.Certificate is not { } certificate || !Verifies(signer))
        {
            tally.Mismatch = true;

            return;
        }

        string name = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);

        if (ChainsTo(certificate, cms.Certificates, trusted) is { } anchor)
        {
            tally.TrustedSigner ??= name;

            if (!tally.Anchors.Contains(anchor))
            {
                tally.Anchors.Add(anchor);
            }

            return;
        }

        tally.OtherSigner ??= name;
    }

    private static bool Verifies(SignerInfo signer)
    {
        try
        {
            signer.CheckSignature(verifySignatureOnly: true);

            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    // SpcIndirectDataContent holds the data it describes, then a DigestInfo with the file's hash.
    private static (HashAlgorithmName, byte[])? ReadIndirectData(byte[] content)
    {
        AsnReader indirect = new AsnReader(content, AsnEncodingRules.BER).ReadSequence();
        indirect.ReadSequence();
        AsnReader digestInfo = indirect.ReadSequence();
        string algorithm = digestInfo.ReadSequence().ReadObjectIdentifier();
        byte[] digest = digestInfo.ReadOctetString();

        return algorithm switch
        {
            "1.3.14.3.2.26" => (HashAlgorithmName.SHA1, digest),
            "2.16.840.1.101.3.4.2.1" => (HashAlgorithmName.SHA256, digest),
            "2.16.840.1.101.3.4.2.2" => (HashAlgorithmName.SHA384, digest),
            "2.16.840.1.101.3.4.2.3" => (HashAlgorithmName.SHA512, digest),
            _ => null,
        };
    }

    // Returns the trusted certificate the signer's chain leads to, or null.
    private static X509Certificate2? ChainsTo(X509Certificate2 signer, X509Certificate2Collection carried, IReadOnlyCollection<X509Certificate2> trusted)
    {
        X509Certificate2 current = signer;

        for (int depth = 0; depth < MaxChainLength; depth++)
        {
            if (trusted.FirstOrDefault(anchor => anchor.RawData.AsSpan().SequenceEqual(current.RawData)) is { } reached)
            {
                return reached;
            }

            X509Certificate2? issuer = trusted.Concat(carried).FirstOrDefault(candidate =>
                candidate.SubjectName.RawData.AsSpan().SequenceEqual(current.IssuerName.RawData)
                && !candidate.RawData.AsSpan().SequenceEqual(current.RawData)
                && IsSignedBy(current, candidate));

            if (issuer is null)
            {
                return null;
            }

            current = issuer;
        }

        return null;
    }

    private static bool IsSignedBy(X509Certificate2 certificate, X509Certificate2 issuer)
    {
        try
        {
            AsnReader parts = new AsnReader(certificate.RawData, AsnEncodingRules.DER).ReadSequence();
            ReadOnlyMemory<byte> signed = parts.ReadEncodedValue();
            string algorithm = parts.ReadSequence().ReadObjectIdentifier();
            byte[] signature = parts.ReadBitString(out _);

            (HashAlgorithmName hash, bool rsa) = algorithm switch
            {
                "1.2.840.113549.1.1.5" => (HashAlgorithmName.SHA1, true),
                "1.2.840.113549.1.1.11" => (HashAlgorithmName.SHA256, true),
                "1.2.840.113549.1.1.12" => (HashAlgorithmName.SHA384, true),
                "1.2.840.113549.1.1.13" => (HashAlgorithmName.SHA512, true),
                "1.2.840.10045.4.3.2" => (HashAlgorithmName.SHA256, false),
                "1.2.840.10045.4.3.3" => (HashAlgorithmName.SHA384, false),
                "1.2.840.10045.4.3.4" => (HashAlgorithmName.SHA512, false),
                _ => (default, false),
            };

            if (hash == default)
            {
                return false;
            }

            if (rsa)
            {
                using RSA? key = issuer.GetRSAPublicKey();

                return key?.VerifyData(signed.Span, signature, hash, RSASignaturePadding.Pkcs1) ?? false;
            }

            using ECDsa? curve = issuer.GetECDsaPublicKey();

            return curve?.VerifyData(signed.Span, signature, hash, DSASignatureFormat.Rfc3279DerSequence) ?? false;
        }
        catch (Exception exception) when (exception is CryptographicException or AsnContentException)
        {
            return false;
        }
    }

    // Collects the results for all signatures of one file. Each hash is computed once and shared by the signatures that
    // use it.
    private sealed class Tally
    {
        public Dictionary<HashAlgorithmName, byte[]> Hashes { get; } = [];

        public List<X509Certificate2> Anchors { get; } = [];

        public string? TrustedSigner { get; set; }

        public string? OtherSigner { get; set; }

        public bool Mismatch { get; set; }

        public bool Unreadable { get; set; }
    }
}
