using System.Buffers.Binary;
using System.Text;

namespace BootScanner.Tests;

// Generates a small CFB container in memory to exercise the OS reader. No project file is included.
internal static class CompoundTestData
{
    public static byte[] Create()
    {
        var data = new byte[19 * 512];
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(data, 0);
        U16(24, 0x3E); U16(26, 3); U16(28, 0xFFFE); U16(30, 9); U16(32, 6);
        U32(44, 1); U32(48, 0); U32(56, 4096); U32(60, 0xFFFFFFFE); U32(68, 0xFFFFFFFE);
        for (var i = 0; i < 109; i++) U32(76 + i * 4, uint.MaxValue);
        U32(76, 1);
        for (var i = 0; i < 128; i++) U32(1024 + i * 4, uint.MaxValue);
        U32(1024, 0xFFFFFFFE); U32(1028, 0xFFFFFFFD);
        for (uint i = 2; i < 18; i++) U32(1024 + (int)i * 4, i == 9 || i == 17 ? 0xFFFFFFFE : i + 1);
        Entry(0, "Root Entry", 5, uint.MaxValue, uint.MaxValue, 1, 0xFFFFFFFE, 0);
        Entry(1, "TcSysManager", 1, uint.MaxValue, uint.MaxValue, 3, 0, 0);
        Entry(2, "SlaveDeviceInfo", 2, uint.MaxValue, uint.MaxValue, uint.MaxValue, 2, 4096);
        Entry(3, "WSM", 2, uint.MaxValue, 2, uint.MaxValue, 10, 4096);
        data[512 + 2 * 128 + 67] = 0; // Red right child; root WSM remains black.
        var xml = Encoding.Unicode.GetBytes("<SlaveDeviceInfo><TargetDesc><Name>unit-target</Name></TargetDesc></SlaveDeviceInfo>");
        U32(1536, 4092);
        xml.CopyTo(data, 1540);
        for (var i = 1540 + xml.Length; i < 1536 + 4096; i += 2) { data[i] = 32; data[i + 1] = 0; }
        var path = Encoding.ASCII.GetBytes(@"X:\not-accessed\unverified.tpy");
        data[11 * 512] = (byte)path.Length;
        path.CopyTo(data, 11 * 512 + 1);
        return data;

        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
        void Entry(int id, string name, byte type, uint left, uint right, uint child, uint sector, ulong length)
        {
            var offset = 512 + id * 128;
            var text = Encoding.Unicode.GetBytes(name + '\0'); text.CopyTo(data, offset);
            U16(offset + 64, (ushort)text.Length); data[offset + 66] = type; data[offset + 67] = 1;
            U32(offset + 68, left); U32(offset + 72, right); U32(offset + 76, child); U32(offset + 116, sector);
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset + 120), length);
        }
    }
}
