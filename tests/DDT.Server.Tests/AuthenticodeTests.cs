// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Security.Cryptography.X509Certificates;
using DDT.Server.Images;
using Xunit;

namespace DDT.Server.Tests;

public sealed class AuthenticodeTests
{
    [Fact]
    public void TrustsAShimSignedUnderMicrosoftsUefiCa()
    {
        AuthenticodeResult result = Authenticode.Check(TestPe.Fixture("shimx64.efi.dualsigned"), UefiCertificateAuthorities.Microsoft);

        Assert.Equal(
            (AuthenticodeStatus.Trusted, PeImage.MachineAmd64, "Microsoft Windows UEFI Driver Publisher", (string?)null),
            (result.Status, result.Machine, result.Signer, result.Reason));

        // The anchor its signatures lead to, which tells which of Microsoft's two CAs a firmware needs to start it.
        Assert.Equal(["CN=Microsoft Corporation UEFI CA 2011"], result.Anchors!.Select(anchor => anchor.Subject.Split(", ")[0]));
    }

    [Fact]
    public void NamesTheSignerOfAProgramNoCaInTheDbSigned()
    {
        AuthenticodeResult result = Authenticode.Check(TestPe.Fixture("fbx64.efi"), UefiCertificateAuthorities.Microsoft);

        Assert.Equal(
            new AuthenticodeResult(AuthenticodeStatus.SignedByOthers, PeImage.MachineAmd64, "Canonical Ltd. Secure Boot Signing (2022 v1)", null),
            result);
    }

    [Fact]
    public void RefusesAShimChangedAfterSigning()
    {
        byte[] shim = TestPe.Fixture("shimx64.efi.dualsigned");
        shim[0x1000] ^= 0x01;

        AuthenticodeResult result = Authenticode.Check(shim, UefiCertificateAuthorities.Microsoft);

        Assert.Equal(AuthenticodeStatus.NotSigned, result.Status);
        Assert.Equal("carries a signature that does not match its content", result.Reason);
    }

    [Fact]
    public void SaysWhenAProgramCarriesNoSignature()
    {
        AuthenticodeResult result = Authenticode.Check(TestPe.Create(), UefiCertificateAuthorities.Microsoft);

        Assert.Equal(new AuthenticodeResult(AuthenticodeStatus.NotSigned, PeImage.MachineAmd64, null, "carries no signature"), result);
    }

    [Fact]
    public void SaysWhenAFileIsNoProgram()
    {
        byte[] noise = new byte[4096];
        new Random(3).NextBytes(noise);

        Assert.Equal(AuthenticodeStatus.Unreadable, Authenticode.Check(noise, UefiCertificateAuthorities.Microsoft).Status);
        Assert.Equal(AuthenticodeStatus.Unreadable, Authenticode.Check(TestPe.Create()[..300], UefiCertificateAuthorities.Microsoft).Status);
    }

    // Sections that overlap would make the hash read the file many times over.
    [Fact]
    public void RefusesSectionsLongerTogetherThanTheFile()
    {
        byte[] file = TestPe.Create();
        int secondSection = 0x40 + 24 + 240 + 40;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x40 + 6), 2);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(secondSection + 16), 0x400);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(secondSection + 20), 0);

        Assert.Null(PeImage.Read(file));
        Assert.NotNull(PeImage.Read(TestPe.Create()));
    }

    [Fact]
    public void SaysWhenASignatureCannotBeRead()
    {
        byte[] signed = TestPe.Sign(TestPe.Create(), TestPe.Certificate("Signer"));
        signed[1024 + 8 + 2] ^= 0xFF;

        Assert.Equal(AuthenticodeStatus.Unreadable, Authenticode.Check(signed, UefiCertificateAuthorities.Microsoft).Status);
    }

    [Fact]
    public void FollowsTheChainToATrustedCertificateWhateverItsDates()
    {
        using X509Certificate2 root = TestPe.Certificate("Test Root", notAfter: DateTimeOffset.UtcNow.AddDays(-1));
        using X509Certificate2 authority = TestPe.Certificate("Test UEFI CA", root);
        using X509Certificate2 signer = TestPe.Certificate("Test Signer", authority, authority: false);
        byte[] signed = TestPe.Sign(TestPe.Create(), signer, authority);

        Assert.Equal(AuthenticodeStatus.Trusted, Authenticode.Check(signed, [root]).Status);
        Assert.Equal(AuthenticodeStatus.Trusted, Authenticode.Check(signed, [authority]).Status);
        Assert.Equal("Test Signer", Authenticode.Check(signed, [root]).Signer);
        Assert.Equal(AuthenticodeStatus.SignedByOthers, Authenticode.Check(signed, UefiCertificateAuthorities.Microsoft).Status);
    }

    [Fact]
    public void FindsTheTrustedSignatureAmongOthers()
    {
        using X509Certificate2 trusted = TestPe.Certificate("Trusted CA");
        using X509Certificate2 other = TestPe.Certificate("Other CA");
        byte[] twice = TestPe.Sign(
            TestPe.Sign(TestPe.Create(), TestPe.Certificate("Other Signer", other, authority: false)),
            TestPe.Certificate("Trusted Signer", trusted, authority: false));

        AuthenticodeResult result = Authenticode.Check(twice, [trusted]);

        Assert.Equal(AuthenticodeStatus.Trusted, result.Status);
        Assert.Equal("Trusted Signer", result.Signer);
    }

    // A certificate that takes the trusted one's name, but not its key, proves nothing.
    [Fact]
    public void RefusesAnIssuerThatOnlyCarriesTheTrustedName()
    {
        using X509Certificate2 trusted = TestPe.Certificate("Microsoft Corporation UEFI CA 2011");
        using X509Certificate2 impostor = TestPe.Certificate("Microsoft Corporation UEFI CA 2011");
        byte[] signed = TestPe.Sign(TestPe.Create(), TestPe.Certificate("Signer", impostor, authority: false), impostor);

        Assert.Equal(AuthenticodeStatus.SignedByOthers, Authenticode.Check(signed, [trusted]).Status);
    }

    [Fact]
    public void ReadsTheMachineTheProgramIsFor()
    {
        byte[] arm = TestPe.Create(PeImage.MachineArm64);

        Assert.Equal(PeImage.MachineArm64, Authenticode.Check(arm, UefiCertificateAuthorities.Microsoft).Machine);
        Assert.Equal(PeImage.MachineArm64, BinaryPrimitives.ReadUInt16LittleEndian(arm.AsSpan(0x44)));
    }

    [Fact]
    public void CarriesMicrosoftsTwoUefiCertificateAuthorities()
    {
        Assert.Equal(
            ["48E99B991F57FC52F76149599BFF0A58C47154229B9F8D603AC40D3500248507", "F6124E34125BEE3FE6D79A574EAA7B91C0E7BD9D929C1A321178EFD611DAD901"],
            UefiCertificateAuthorities.Microsoft.Select(certificate => certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)));
    }
}
