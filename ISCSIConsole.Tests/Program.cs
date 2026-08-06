using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using DiskAccessLibrary;
using ISCSI.Server;
using SCSI;

namespace ISCSIConsole.Tests
{
    internal static class Program
    {
        private sealed class MemoryDisk : Disk, IFlushableDisk
        {
            private readonly byte[] m_data = new byte[2 * 1024 * 1024];

            public int FlushCount { get; private set; }

            public override byte[] ReadSectors(long sectorIndex, int sectorCount)
            {
                byte[] result = new byte[sectorCount * BytesPerSector];
                Buffer.BlockCopy(m_data, checked((int)(sectorIndex * BytesPerSector)), result, 0, result.Length);
                return result;
            }

            public override void WriteSectors(long sectorIndex, byte[] data)
            {
                Buffer.BlockCopy(data, 0, m_data, checked((int)(sectorIndex * BytesPerSector)), data.Length);
            }

            public void Flush()
            {
                FlushCount++;
            }

            public override int BytesPerSector { get { return 512; } }
            public override long Size { get { return m_data.Length; } }
            public override bool IsReadOnly { get { return false; } }
        }

        private static int Main()
        {
            try
            {
                TestCmdSNOrdering();
                TestCdb12Serialization();
                TestScsiFlushAndFua();
                TestReadWrite12AndCacheInvalidation();
                TestSingleClientGate();
                TestVhdxDurableFlush();
                Console.WriteLine("ALL_TESTS_PASSED");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("TEST_FAILED: " + ex);
                return 1;
            }
        }

        private static void TestCmdSNOrdering()
        {
            Assert(ISCSISession.IsFirstCmdSNPreceding(10, 11), "normal CmdSN order");
            Assert(!ISCSISession.IsFirstCmdSNPreceding(11, 10), "reverse CmdSN order");
            Assert(!ISCSISession.IsFirstCmdSNPreceding(10, 10), "equal CmdSN order");
            Assert(ISCSISession.IsFirstCmdSNPreceding(UInt32.MaxValue, 0), "CmdSN wrap order");
            Assert(!ISCSISession.IsFirstCmdSNPreceding(0, UInt32.MaxValue), "CmdSN reverse wrap order");
        }

        private static void TestCdb12Serialization()
        {
            SCSICommandDescriptorBlock12 command = new SCSICommandDescriptorBlock12(SCSIOpCodeName.Write12);
            command.LogicalBlockAddress = 123;
            command.TransferLength = 7;
            command.ForceUnitAccess = true;
            byte[] bytes = command.GetBytes();
            Assert(bytes.Length == 12, "12-byte CDB length");

            SCSICommandDescriptorBlock parsed = SCSICommandDescriptorBlock.FromBytes(bytes, 0);
            Assert(parsed.OpCode == SCSIOpCodeName.Write12, "WRITE(12) parser");
            Assert(parsed.LogicalBlockAddress == 123 && parsed.TransferLength == 7, "WRITE(12) fields");
            Assert(parsed.ForceUnitAccess, "WRITE(12) FUA parser");
        }

        private static void TestScsiFlushAndFua()
        {
            MemoryDisk disk = new MemoryDisk();
            VirtualSCSITarget target = CreateTarget(disk);
            byte[] response;

            SCSICommandDescriptorBlock10 sync10 = new SCSICommandDescriptorBlock10(SCSIOpCodeName.SynchronizeCache10);
            Assert(target.ExecuteCommand(sync10.GetBytes(), (ushort)0, null, out response) == SCSIStatusCodeName.Good, "SYNCHRONIZE CACHE(10)");
            Assert(disk.FlushCount == 1, "SYNCHRONIZE CACHE(10) invokes flush");

            SCSICommandDescriptorBlock16 sync16 = new SCSICommandDescriptorBlock16(SCSIOpCodeName.SynchronizeCache16);
            Assert(target.ExecuteCommand(sync16.GetBytes(), (ushort)0, null, out response) == SCSIStatusCodeName.Good, "SYNCHRONIZE CACHE(16)");
            Assert(disk.FlushCount == 2, "SYNCHRONIZE CACHE(16) invokes flush");

            SCSICommandDescriptorBlock10 write = new SCSICommandDescriptorBlock10(SCSIOpCodeName.Write10);
            write.TransferLength = 1;
            write.ForceUnitAccess = true;
            Assert(target.ExecuteCommand(write.GetBytes(), (ushort)0, FilledSector(0x5A), out response) == SCSIStatusCodeName.Good, "FUA write");
            Assert(disk.FlushCount == 3, "FUA write invokes flush");

            ModeSense6CommandDescriptorBlock modeSense = new ModeSense6CommandDescriptorBlock();
            modeSense.DBD = true;
            modeSense.PageCode = ModePageCodeName.CachingParametersPage;
            modeSense.AllocationLength = Byte.MaxValue;
            Assert(target.ExecuteCommand(modeSense.GetBytes(), (ushort)0, null, out response) == SCSIStatusCodeName.Good, "MODE SENSE(6)");
            Assert((response[2] & 0x10) == 0, "DPOFUA is not over-advertised");
            Assert((response[6] & 0x04) != 0, "write cache is advertised");
        }

        private static void TestReadWrite12AndCacheInvalidation()
        {
            MemoryDisk inner = new MemoryDisk();
            ISCSIConsole.CachedDisk cached = new ISCSIConsole.CachedDisk(inner, 1);
            VirtualSCSITarget target = CreateTarget(cached);
            byte[] response;

            SCSICommandDescriptorBlock12 read = new SCSICommandDescriptorBlock12(SCSIOpCodeName.Read12);
            read.LogicalBlockAddress = 8;
            read.TransferLength = 1;
            Assert(target.ExecuteCommand(read.GetBytes(), (ushort)0, null, out response) == SCSIStatusCodeName.Good, "READ(12)");
            Assert(response[0] == 0, "initial cached read");

            SCSICommandDescriptorBlock12 write = new SCSICommandDescriptorBlock12(SCSIOpCodeName.Write12);
            write.LogicalBlockAddress = 8;
            write.TransferLength = 1;
            Assert(target.ExecuteCommand(write.GetBytes(), (ushort)0, FilledSector(0xA5), out response) == SCSIStatusCodeName.Good, "WRITE(12)");
            Assert(target.ExecuteCommand(read.GetBytes(), (ushort)0, null, out response) == SCSIStatusCodeName.Good, "READ(12) after write");
            Assert(response[0] == 0xA5, "write invalidates read cache");

            SCSICommandDescriptorBlock12 verify = new SCSICommandDescriptorBlock12(SCSIOpCodeName.Verify12);
            Assert(target.ExecuteCommand(verify.GetBytes(), (ushort)0, null, out response) == SCSIStatusCodeName.Good, "VERIFY(12)");
        }

        private static void TestSingleClientGate()
        {
            ISCSIConsole.SingleClientGate gate = new ISCSIConsole.SingleClientGate();
            bool assigned;
            Assert(gate.Authorize(IPAddress.Parse("192.0.2.10"), out assigned) && assigned, "first client owns target");
            Assert(gate.Authorize(IPAddress.Parse("192.0.2.10"), out assigned) && !assigned, "same source IP reconnects");
            Assert(!gate.Authorize(IPAddress.Parse("192.0.2.11"), out assigned), "second source IP rejected");
        }

        private static void TestVhdxDurableFlush()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ISCSIConsole.Tests." + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "flush.vhdx");
            Directory.CreateDirectory(directory);
            try
            {
                ISCSIConsole.VhdxDiskImage disk = ISCSIConsole.VhdxDiskImage.CreateDynamicDisk(path, 64L * 1024 * 1024);
                disk.WriteSectors(2048, FilledSector(0x3C));
                disk.Flush();
                disk.ReleaseLock();

                ISCSIConsole.VhdxDiskImage reopened = new ISCSIConsole.VhdxDiskImage(path, true);
                byte[] data = reopened.ReadSectors(2048, 1);
                reopened.ReleaseLock();
                Assert(data[0] == 0x3C && data[data.Length - 1] == 0x3C, "VHDX data survives flush and reopen");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static VirtualSCSITarget CreateTarget(Disk disk)
        {
            return new VirtualSCSITarget(new List<Disk> { disk });
        }

        private static byte[] FilledSector(byte value)
        {
            byte[] data = new byte[512];
            for (int index = 0; index < data.Length; index++)
            {
                data[index] = value;
            }
            return data;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
