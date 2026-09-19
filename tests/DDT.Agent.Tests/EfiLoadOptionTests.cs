using System.Buffers.Binary;
using System.Text;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class EfiLoadOptionTests
{
    private const string Description = WindowsBootEntry.Description;
    private const string Loader = WindowsBootEntry.LoaderPath;

    // The description's 42 bytes follow the 6-byte header.
    private const int ListStart = 48;

    private static readonly EspPartition s_esp = new(1, 2048, 614400, Guid.Parse("5e2b1a3c-4d6f-4a8b-9c0d-1e2f3a4b5c6d"));
    private static readonly EspPartition s_otherEsp = s_esp with { PartitionId = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0") };

    [Fact]
    public void BuildsTheBytesTheFirmwareReads()
    {
        byte[] expected =
        [
            // LOAD_OPTION_ACTIVE, then the length of the device paths: 42 + 70 + 4 bytes.
            .. Convert.FromHexString("01000000" + "7400"),
            .. Encoding.Unicode.GetBytes("Windows Boot Manager\0"),

            // Hard drive: partition 1, from block 2048, 614400 blocks, the unique partition GUID in EFI_GUID order,
            // then GPT and a GUID signature.
            .. Convert.FromHexString("04012A00" + "01000000" + "0008000000000000" + "0060090000000000"),
            .. Convert.FromHexString("3C1A2B5E" + "6F4D" + "8B4A" + "9C0D1E2F3A4B5C6D" + "0202"),

            // The file, null-terminated, then the end of the device path.
            .. Convert.FromHexString("04044600"),
            .. Encoding.Unicode.GetBytes(@"\EFI\Microsoft\Boot\bootmgfw.efi" + "\0"),
            .. Convert.FromHexString("7FFF0400"),
        ];

        Assert.Equal(expected, EfiLoadOption.Build(Description, s_esp, Loader));
    }

    [Fact]
    public void RecognisesTheOptionItBuiltWhateverTheCaseOfThePath()
    {
        byte[] option = EfiLoadOption.Build(Description, s_esp, Loader);

        Assert.True(EfiLoadOption.PointsAt(option, s_esp.PartitionId, Loader));
        Assert.True(EfiLoadOption.PointsAt(option, s_esp.PartitionId, Loader.ToUpperInvariant()));
    }

    [Fact]
    public void RecognisesAnOptionWindowsWroteWithItsOptionalData()
    {
        // bcdboot's entries carry "WINDOWS" and a reference to a BCD object after the device paths.
        byte[] option = [.. EfiLoadOption.Build(Description, s_esp, Loader), .. "WINDOWS\0"u8, 0x01, 0x00, 0x00, 0x00, 0x88, 0x00, 0x00, 0x00];

        Assert.True(EfiLoadOption.PointsAt(option, s_esp.PartitionId, Loader));
    }

    [Fact]
    public void DoesNotTakeAnotherPartitionOrAnotherFileForIt()
    {
        Assert.False(EfiLoadOption.PointsAt(EfiLoadOption.Build(Description, s_otherEsp, Loader), s_esp.PartitionId, Loader));
        Assert.False(EfiLoadOption.PointsAt(EfiLoadOption.Build(Description, s_esp, @"\EFI\Boot\bootx64.efi"), s_esp.PartitionId, Loader));
    }

    public static TheoryData<string> Damages =>
    [
        "empty",
        "cut in the header",
        "no end to the description",
        "device paths past the end",
        "a node of no length",
        "a node shorter than its header",
        "a node past the device paths",
        "a hard drive node cut short",
    ];

    [Theory]
    [MemberData(nameof(Damages))]
    public void AMalformedOptionPointsNowhere(string damage)
    {
        byte[] option = EfiLoadOption.Build(Description, s_esp, Loader);

        byte[] damaged = damage switch
        {
            "empty" => [],
            "cut in the header" => option[..5],
            "no end to the description" => option[..40],
            "device paths past the end" => option[..^1],
            "a node of no length" => WithNodeLength(option, ListStart, 0),
            "a node shorter than its header" => WithNodeLength(option, ListStart, 2),
            "a node past the device paths" => WithNodeLength(option, ListStart, 200),
            _ => WithNodeLength(option, ListStart, 20),
        };

        Assert.False(EfiLoadOption.PointsAt(damaged, s_esp.PartitionId, Loader));
    }

    private static byte[] WithNodeLength(byte[] option, int node, ushort length)
    {
        byte[] damaged = [.. option];
        BinaryPrimitives.WriteUInt16LittleEndian(damaged.AsSpan(node + 2), length);

        return damaged;
    }
}
