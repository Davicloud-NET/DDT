// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Server.Applications.Installers;

// The sectors of a compound file and the FAT that chains them. FAT sectors are read when a chain reaches them, so a
// large MSI with embedded cabinets costs only the sectors of the streams that are read.
internal sealed class CompoundFileSectors
{
    public const uint EndOfChain = 0xFFFFFFFE;
    public const uint NoEntry = 0xFFFFFFFF;

    private const uint MaxRegularSector = 0xFFFFFFFA;

    private readonly Stream _file;
    private readonly uint[] _fatSectors;
    private readonly Dictionary<uint, byte[]> _fatCache = [];

    private CompoundFileSectors(Stream file, CompoundFileHeader header, long sectorCount, uint[] fatSectors)
    {
        _file = file;
        Header = header;
        SectorCount = sectorCount;
        _fatSectors = fatSectors;
    }

    public CompoundFileHeader Header { get; }

    public long SectorCount { get; }

    public int SectorSize => Header.SectorSize;

    public static CompoundFileSectors Open(Stream file, CompoundFileHeader header)
    {
        int sectorSize = header.SectorSize;
        long sectorCount = (file.Length - 1) / sectorSize;
        int entriesPerSector = sectorSize / 4;

        // Every FAT sector is a sector of the file, and no more are needed than cover the file.
        if (sectorCount <= 0 || header.FatSectorCount > (sectorCount / entriesPerSector) + 2)
        {
            throw new InvalidDataException("The FAT is larger than the file.");
        }

        CompoundFileSectors sectors = new(file, header, sectorCount, new uint[header.FatSectorCount]);
        sectors.ReadDifat();

        return sectors;
    }

    public byte[] Read(uint sector) => Read(sector, SectorSize);

    // The first length bytes of a sector. Writers may cut the file's last sector short after the data it holds.
    public byte[] Read(uint sector, int length)
    {
        if (!IsInFile(sector) || length > SectorSize)
        {
            throw new InvalidDataException("A sector lies outside the file.");
        }

        byte[] buffer = new byte[length];
        _file.ReadExactlyAt((sector + 1L) * SectorSize, buffer);

        return buffer;
    }

    // Exactly count sectors of the chain from start. A loop or a chain that ends early makes the file unreadable.
    public List<uint> Chain(uint start, long count, Func<uint, uint> next)
    {
        List<uint> chain = [];
        HashSet<uint> seen = [];
        uint sector = start;

        while (chain.Count < count)
        {
            if (sector > MaxRegularSector || !seen.Add(sector))
            {
                throw new InvalidDataException("A sector chain ends early or loops.");
            }

            chain.Add(sector);
            sector = next(sector);
        }

        return chain;
    }

    // The sectors of a chain whose length isn't stored, such as the directory's in a version 3 file.
    public List<uint> OpenChain(uint start, long maxCount)
    {
        List<uint> chain = [];
        HashSet<uint> seen = [];

        for (uint sector = start; sector != EndOfChain; sector = Next(sector))
        {
            if (sector > MaxRegularSector || !seen.Add(sector) || chain.Count >= maxCount)
            {
                throw new InvalidDataException("A sector chain is broken, loops or is too long.");
            }

            chain.Add(sector);
        }

        return chain;
    }

    public uint Next(uint sector)
    {
        int entriesPerSector = SectorSize / 4;
        long fatIndex = sector / entriesPerSector;

        if (!IsInFile(sector) || fatIndex >= _fatSectors.Length)
        {
            throw new InvalidDataException("A sector has no FAT entry.");
        }

        uint fatSector = _fatSectors[fatIndex];

        if (!_fatCache.TryGetValue(fatSector, out byte[]? fat))
        {
            fat = Read(fatSector);
            _fatCache[fatSector] = fat;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(fat.AsSpan((int)(sector % entriesPerSector) * 4));
    }

    private bool IsInFile(uint sector) => sector <= MaxRegularSector && sector < SectorCount;

    // The first 109 FAT sectors are listed in the header, the rest in a chain of DIFAT sectors.
    private void ReadDifat()
    {
        int filled = 0;

        for (; filled < _fatSectors.Length && filled < Header.HeaderDifat.Count; filled++)
        {
            _fatSectors[filled] = Header.HeaderDifat[filled];
        }

        uint difatSector = Header.FirstDifatSector;
        int perSector = (SectorSize / 4) - 1;
        HashSet<uint> seen = [];

        while (filled < _fatSectors.Length)
        {
            if (seen.Count >= Header.DifatSectorCount || !seen.Add(difatSector))
            {
                throw new InvalidDataException("The DIFAT is shorter than the FAT or loops.");
            }

            byte[] difat = Read(difatSector);

            for (int index = 0; index < perSector && filled < _fatSectors.Length; index++, filled++)
            {
                _fatSectors[filled] = BinaryPrimitives.ReadUInt32LittleEndian(difat.AsSpan(index * 4));
            }

            difatSector = BinaryPrimitives.ReadUInt32LittleEndian(difat.AsSpan(perSector * 4));
        }
    }
}
