using System.Text;

/// <summary>
/// Patches a .NET DLL's PE export directory to add native exports
/// pointing to existing VTableFixup slot entries.
/// </summary>
static class PeExportPatcher
{
    public static void Patch(string dllPath, List<string> exportNames)
    {
        var data = File.ReadAllBytes(dllPath);
        var ctx = new PeContext(data);

        if (ctx.ExportDirRva != 0)
            Console.Write("(overwriting existing exports) ");

        // Read VTableFixup entries from CLR header
        var vtFixups = ReadVTableFixups(data, ctx);
        if (vtFixups.Count == 0)
            throw new InvalidDataException("No VTableFixup entries found. Did .vtfixup directives compile?");

        Console.Write($"({vtFixups.Count} VTable slots) ");

        if (vtFixups.Count != exportNames.Count)
            Console.Error.WriteLine($"WARNING: {vtFixups.Count} slots vs {exportNames.Count} exports");

        // Calculate sizes
        int n = exportNames.Count;
        int namesTotalLen = exportNames.Sum(s => s.Length + 1);
        int dirSize = 40;                 // IMAGE_EXPORT_DIRECTORY
        int addrSize = n * 4;             // AddressOfFunctions
        int namePtrSize = n * 4;          // AddressOfNames
        int ordSize = n * 2;              // AddressOfNameOrdinals
        int totalSize = dirSize + addrSize + namePtrSize + ordSize + namesTotalLen;
        totalSize = (totalSize + 15) & ~15; // 16-byte align

        // We'll add a new section for the export data
        // First find a suitable RVA and file offset
        AddExportSection(data, ctx, exportNames, vtFixups, totalSize);

        File.WriteAllBytes(dllPath, data);
    }

    static List<uint> ReadVTableFixups(byte[] data, PeContext ctx)
    {
        var result = new List<uint>();

        if (ctx.ClrHeaderRva == 0) return result;

        int clrOffset = RvaToOffset(data, ctx, ctx.ClrHeaderRva);
        if (clrOffset < 0) return result;

        // CLR header VTable fixups: at offset 0x70 for PE32+, 0x6C for PE32
        int vtRvaOff = ctx.IsPE32Plus ? 0x70 : 0x6C;
        uint vtRva = ReadU32(data, clrOffset + vtRvaOff);
        uint vtSize = ReadU32(data, clrOffset + vtRvaOff + 4);

        if (vtRva == 0 || vtSize == 0) return result;

        Console.Write($"VTable@0x{vtRva:X8}+0x{vtSize:X} ");

        int vtOffset = RvaToOffset(data, ctx, vtRva);
        if (vtOffset < 0) return result;

        int pos = vtOffset;
        while (pos < vtOffset + vtSize)
        {
            uint slotTableRva = ReadU32(data, pos);
            ushort slotCount = ReadU16(data, pos + 4);
            // ushort slotType = ReadU16(data, pos + 6); // not needed
            pos += 8;

            // Each slot is at slotTableRva + (i * pointerSize)
            for (int i = 0; i < slotCount; i++)
            {
                uint slotRva = slotTableRva + (uint)(i * (ctx.IsPE32Plus ? 8u : 4u));
                result.Add(slotRva);
            }
        }

        return result;
    }

    static void AddExportSection(byte[] data, PeContext ctx, List<string> names,
        List<uint> slotRvas, int totalSize)
    {
        int n = names.Count;

        // We'll extend the last section since adding a new section header
        // would shift everything. Find a gap at end of last section.
        var lastSec = ctx.Sections[^1];

        uint newSectionRva = lastSec.Rva + Math.Max(lastSec.VirtualSize, lastSec.Size);
        // Align to section alignment
        newSectionRva = ((newSectionRva + ctx.SectionAlignment - 1) / ctx.SectionAlignment) * ctx.SectionAlignment;

        uint newSectionOffset = lastSec.Offset + lastSec.Size;
        // Align to file alignment
        newSectionOffset = ((newSectionOffset + ctx.FileAlignment - 1) / ctx.FileAlignment) * ctx.FileAlignment;

        // Expand file
        int newFileSize = (int)(newSectionOffset + totalSize);
        if (newFileSize > data.Length)
            Array.Resize(ref data, newFileSize);

        // Write export data at newSectionOffset
        uint exportRva = newSectionRva;
        int fileOff = (int)newSectionOffset;

        // Layout: dir | funcAddrs | namePtrs | ordinals | name strings
        uint funcAddrRva = exportRva + 40;
        uint namePtrRva = funcAddrRva + (uint)(n * 4);
        uint ordRva = namePtrRva + (uint)(n * 4);
        uint nameStrRva = ordRva + (uint)(n * 2);

        // IMAGE_EXPORT_DIRECTORY
        WriteU32(data, fileOff + 0, 0);        // Characteristics
        WriteU32(data, fileOff + 4, 0);        // TimeDateStamp
        WriteU16(data, fileOff + 8, 0);        // MajorVersion
        WriteU16(data, fileOff + 10, 0);       // MinorVersion
        WriteU32(data, fileOff + 12, nameStrRva); // Name RVA (points to "MdViewerWlx.dll")
        WriteU32(data, fileOff + 16, 1);        // Base ordinal
        WriteU32(data, fileOff + 20, (uint)n);  // NumberOfFunctions
        WriteU32(data, fileOff + 24, (uint)n);  // NumberOfNames
        WriteU32(data, fileOff + 28, funcAddrRva);
        WriteU32(data, fileOff + 32, namePtrRva);
        WriteU32(data, fileOff + 36, ordRva);

        // Function address array (each entry = RVA of VTable slot)
        for (int i = 0; i < n; i++)
            WriteU32(data, fileOff + 40 + i * 4, slotRvas[i]);

        // Name pointer array (RVA of each name string)
        uint strOff = nameStrRva;
        for (int i = 0; i < n; i++)
        {
            WriteU32(data, fileOff + 40 + n * 4 + i * 4, strOff);
            strOff += (uint)(names[i].Length + 1);
        }

        // Ordinal array (0, 1, 2, ...)
        for (int i = 0; i < n; i++)
            WriteU16(data, fileOff + 40 + n * 8 + i * 2, (ushort)i);

        // Name strings (null-terminated) + DLL name
        int nameStrOff = fileOff + 40 + n * 8 + n * 2;
        // First: DLL name
        string dllName = Path.GetFileName(names[0] ?? "MdViewerWlx.dll");
        // Actually let's just use a generic name
        dllName = "MdViewerWlx.dll";
        for (int i = 0; i < dllName.Length; i++) data[nameStrOff + i] = (byte)dllName[i];
        data[nameStrOff + dllName.Length] = 0;
        nameStrOff += dllName.Length + 1;

        // Then export names
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < names[i].Length; j++)
                data[nameStrOff + j] = (byte)names[i][j];
            data[nameStrOff + names[i].Length] = 0;
            nameStrOff += names[i].Length + 1;
        }

        // Update data directory
        WriteU32(data, ctx.DataDirOffset + 0, exportRva);
        WriteU32(data, ctx.DataDirOffset + 4, (uint)totalSize);

        // Update last section's virtual size
        uint newVirtualSize = (uint)(totalSize + (newSectionRva - lastSec.Rva));
        uint alignedVS = ((newVirtualSize + ctx.SectionAlignment - 1) / ctx.SectionAlignment) * ctx.SectionAlignment;
        WriteU32(data, ctx.LastSectionHeaderOffset + 8, Math.Max(alignedVS, ReadU32(data, ctx.LastSectionHeaderOffset + 8)));

        // Update raw size
        uint newRawSize = (uint)(totalSize + (newSectionOffset - lastSec.Offset));
        uint alignedRaw = ((newRawSize + ctx.FileAlignment - 1) / ctx.FileAlignment) * ctx.FileAlignment;
        WriteU32(data, ctx.LastSectionHeaderOffset + 16, Math.Max(alignedRaw, ReadU32(data, ctx.LastSectionHeaderOffset + 16)));

        // Update SizeOfImage
        uint imageEnd = ((newSectionRva + alignedVS + ctx.SectionAlignment - 1) / ctx.SectionAlignment) * ctx.SectionAlignment;
        WriteU32(data, ctx.SizeOfImageOffset, imageEnd);
    }

    // ========== PE helpers ==========

    static int RvaToOffset(byte[] data, PeContext ctx, uint rva)
    {
        foreach (var s in ctx.Sections)
        {
            uint secEnd = s.Rva + Math.Max(s.VirtualSize, s.Size);
            if (rva >= s.Rva && rva < secEnd)
                return (int)(s.Offset + (rva - s.Rva));
        }
        return -1;
    }

    static ushort ReadU16(byte[] d, int o) => BitConverter.ToUInt16(d, o);
    static uint ReadU32(byte[] d, int o) => BitConverter.ToUInt32(d, o);
    static void WriteU16(byte[] d, int o, ushort v) { d[o] = (byte)(v & 0xFF); d[o + 1] = (byte)((v >> 8) & 0xFF); }
    static void WriteU32(byte[] d, int o, uint v) { d[o] = (byte)(v & 0xFF); d[o + 1] = (byte)((v >> 8) & 0xFF); d[o + 2] = (byte)((v >> 16) & 0xFF); d[o + 3] = (byte)((v >> 24) & 0xFF); }

    struct SectionInfo
    {
        public uint Rva, VirtualSize, Offset, Size;
    }

    class PeContext
    {
        public int DataDirOffset;
        public int SizeOfImageOffset;
        public int LastSectionHeaderOffset;
        public uint SectionAlignment, FileAlignment;
        public bool IsPE32Plus;
        public uint ExportDirRva, ClrHeaderRva;
        public SectionInfo[] Sections;

        public PeContext(byte[] d)
        {
            int peOff = BitConverter.ToInt32(d, 0x3C);
            int ohOff = peOff + 24; // PE signature (4) + COFF header (20)
            ushort magic = ReadU16(d, ohOff);
            IsPE32Plus = magic == 0x20b;

            SectionAlignment = ReadU32(d, ohOff + (IsPE32Plus ? 40 : 36));
            FileAlignment = ReadU32(d, ohOff + (IsPE32Plus ? 44 : 40));
            uint sizeOfHeaders = ReadU32(d, ohOff + (IsPE32Plus ? 60 : 56));

            int ohSize = IsPE32Plus ? 112 : 96;
            uint numDataDirs = ReadU32(d, ohOff + (IsPE32Plus ? 108 : 92));
            DataDirOffset = ohOff + ohSize;
            SizeOfImageOffset = ohOff + (IsPE32Plus ? 56 : 52);

            // Export directory (index 0)
            ExportDirRva = ReadU32(d, DataDirOffset + 0);

            // CLR header (index 14)
            ClrHeaderRva = ReadU32(d, DataDirOffset + 14 * 8);

            // Section headers
            int sectionHeaders = DataDirOffset + (int)(numDataDirs * 8);
            ushort numSections = ReadU16(d, peOff + 4 + 2);
            Sections = new SectionInfo[numSections];
            LastSectionHeaderOffset = -1;

            for (int i = 0; i < numSections; i++)
            {
                int sh = sectionHeaders + i * 40;
                Sections[i] = new SectionInfo
                {
                    Rva = ReadU32(d, sh + 12),
                    VirtualSize = ReadU32(d, sh + 8),
                    Offset = ReadU32(d, sh + 20),
                    Size = ReadU32(d, sh + 16),
                };
                LastSectionHeaderOffset = sh;
            }
        }
    }
}
