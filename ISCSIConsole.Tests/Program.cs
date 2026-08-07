using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
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
                TestInitiatorNameGate();
                TestTargetWorkerLifecycle();
                TestClassroomCacheBudget();
                TestConcurrentManagementPipe();
                TestVhdxDurableFlush();
                TestVhdxDifferencingWritesOnlyChild();
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

        private static void TestInitiatorNameGate()
        {
            const string allowed = "iqn.2026-08.cn.bscx:mac-aabbccddeeff";
            ISCSIConsole.InitiatorNameGate gate = new ISCSIConsole.InitiatorNameGate(allowed);
            Assert(gate.Authorize(allowed.ToUpperInvariant()), "allowed initiator is accepted case-insensitively");
            Assert(!gate.Authorize("iqn.2026-08.cn.bscx:mac-001122334455"), "wrong initiator is rejected");
        }

        private static void TestTargetWorkerLifecycle()
        {
            int baselineWorkerCount = SCSITarget.ActiveWorkerCount;
            ISCSIServer server = new ISCSIServer();
            MemoryDisk disk = new MemoryDisk();
            List<ISCSITarget> targets = new List<ISCSITarget>();

            for (int index = 0; index < 80; index++)
            {
                ISCSITarget target = new ISCSITarget(
                    "iqn.2026-08.cn.bscx:test-" + index.ToString("000"),
                    new List<Disk> { disk });
                server.AddTarget(target);
                targets.Add(target);
            }

            Assert(SCSITarget.ActiveWorkerCount == baselineWorkerCount + 80, "80 targets create 80 workers");
            foreach (ISCSITarget target in targets)
            {
                Assert(server.RemoveTarget(target.TargetName), "idle target can be removed");
            }
            Assert(SCSITarget.ActiveWorkerCount == baselineWorkerCount, "target removal stops every worker");
        }

        private static void TestClassroomCacheBudget()
        {
            Assert(DiskConfiguration.DefaultServiceCacheSizeMB == 16, "classroom target default cache");
            long maximumCacheMB = 80L * DiskConfiguration.DefaultServiceCacheSizeMB;
            Assert(maximumCacheMB <= 1280, "80-target cache budget stays at or below 1280 MB");

            TargetConfiguration configuration = new TargetConfiguration();
            configuration.TargetName = "iqn.2026-08.cn.bscx:test-client";
            configuration.AllowedInitiatorName = " iqn.2026-08.cn.bscx:mac-aabbccddeeff ";
            configuration.Disks.Add(DiskConfiguration.CreateDiskImage("client.vhdx", false));
            configuration.Normalize();
            Assert(configuration.AllowedInitiatorName == "iqn.2026-08.cn.bscx:mac-aabbccddeeff", "initiator binding is normalized");
            Assert(configuration.Disks[0].CacheSizeMB == 16, "new service disk uses classroom cache default");
        }

        private static void TestConcurrentManagementPipe()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ISCSIConsole.Tests.Pipe." + Guid.NewGuid().ToString("N"));
            string configPath = Path.Combine(directory, "service.xml");
            Directory.CreateDirectory(directory);
            HeadlessServiceRuntime runtime = new HeadlessServiceRuntime(new ISCSIServer(), new ServiceConfiguration(), configPath);
            try
            {
                runtime.StartManagementPipe();
                Exception[] errors = new Exception[32];
                Thread[] clients = new Thread[32];
                for (int index = 0; index < clients.Length; index++)
                {
                    int clientIndex = index;
                    clients[index] = new Thread(delegate()
                    {
                        try
                        {
                            string response = HeadlessServiceRuntime.SendManagementCommand(runtime.PipeName, "LIST");
                            if (!response.StartsWith("OK TARGETS 0", StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException("Unexpected management response: " + response);
                            }
                        }
                        catch (Exception ex)
                        {
                            errors[clientIndex] = ex;
                        }
                    });
                    clients[index].IsBackground = true;
                    clients[index].Start();
                }

                foreach (Thread client in clients)
                {
                    Assert(client.Join(15000), "concurrent management client completes");
                }
                foreach (Exception error in errors)
                {
                    if (error != null)
                    {
                        throw new InvalidOperationException("Concurrent management request failed.", error);
                    }
                }
            }
            finally
            {
                runtime.Stop();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
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

        private static void TestVhdxDifferencingWritesOnlyChild()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ISCSIConsole.Tests.Diff." + Guid.NewGuid().ToString("N"));
            string parentPath = Path.Combine(directory, "parent.vhdx");
            string childPath = Path.Combine(directory, "child.vhdx");
            Directory.CreateDirectory(directory);
            try
            {
                ISCSIConsole.VhdxDiskImage parent = ISCSIConsole.VhdxDiskImage.CreateDynamicDisk(parentPath, 64L * 1024 * 1024);
                parent.WriteSectors(2048, FilledSector(0x31));
                parent.Flush();
                parent.ReleaseLock();
                byte[] parentHashBefore = ComputeHash(parentPath);

                using (DiscUtils.Vhdx.Disk child = DiscUtils.Vhdx.Disk.InitializeDifferencing(childPath, parentPath))
                {
                }

                ISCSIConsole.VhdxDiskImage writableChild = new ISCSIConsole.VhdxDiskImage(childPath, false);
                Assert(writableChild.ReadSectors(2048, 1)[0] == 0x31, "child reads unchanged sectors from parent");
                writableChild.WriteSectors(2048, FilledSector(0x72));
                writableChild.Flush();
                writableChild.ReleaseLock();

                byte[] parentHashAfter = ComputeHash(parentPath);
                Assert(ByteArraysEqual(parentHashBefore, parentHashAfter), "child write does not modify parent VHDX bytes");

                ISCSIConsole.VhdxDiskImage reopenedChild = new ISCSIConsole.VhdxDiskImage(childPath, true);
                Assert(reopenedChild.ReadSectors(2048, 1)[0] == 0x72, "child write survives flush and reopen");
                reopenedChild.ReleaseLock();

                ISCSIConsole.VhdxDiskImage reopenedParent = new ISCSIConsole.VhdxDiskImage(parentPath, true);
                Assert(reopenedParent.ReadSectors(2048, 1)[0] == 0x31, "parent logical content remains unchanged");
                reopenedParent.ReleaseLock();
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static byte[] ComputeHash(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return sha256.ComputeHash(stream);
            }
        }

        private static bool ByteArraysEqual(byte[] first, byte[] second)
        {
            if (first == null || second == null || first.Length != second.Length)
            {
                return false;
            }
            for (int index = 0; index < first.Length; index++)
            {
                if (first[index] != second[index])
                {
                    return false;
                }
            }
            return true;
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
