using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using DiskAccessLibrary;
using ISCSI.Server;
using Microsoft.Win32.SafeHandles;
using SCSI;

namespace ISCSIConsole.Tests
{
    internal static class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct VirtualStorageType
        {
            public uint DeviceId;
            public Guid VendorId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CreateVirtualDiskParametersVersion2
        {
            public Guid UniqueId;
            public ulong MaximumSize;
            public uint BlockSizeInBytes;
            public uint SectorSizeInBytes;
            public uint PhysicalSectorSizeInBytes;
            public IntPtr ParentPath;
            public IntPtr SourcePath;
            public uint OpenFlags;
            public VirtualStorageType ParentVirtualStorageType;
            public VirtualStorageType SourceVirtualStorageType;
            public Guid ResiliencyGuid;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct CreateVirtualDiskParameters
        {
            [FieldOffset(0)]
            public uint Version;

            [FieldOffset(8)]
            public CreateVirtualDiskParametersVersion2 Version2;
        }

        [DllImport("virtdisk.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint CreateVirtualDisk(
            ref VirtualStorageType virtualStorageType,
            string path,
            uint virtualDiskAccessMask,
            IntPtr securityDescriptor,
            uint flags,
            uint providerSpecificFlags,
            ref CreateVirtualDiskParameters parameters,
            IntPtr overlapped,
            out SafeFileHandle handle);

        private sealed class MemoryDisk : Disk, IFlushableDisk
        {
            private readonly byte[] m_data = new byte[2 * 1024 * 1024];

            public int FlushCount { get; private set; }
            public int ReadCount { get; private set; }
            public int LastReadSectorCount { get; private set; }
            public bool ReturnShortRead { get; set; }

            public override byte[] ReadSectors(long sectorIndex, int sectorCount)
            {
                ReadCount++;
                LastReadSectorCount = sectorCount;
                byte[] result = new byte[sectorCount * BytesPerSector - (ReturnShortRead ? BytesPerSector : 0)];
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
                RunTest("CmdSN ordering", TestCmdSNOrdering);
                RunTest("CDB12 serialization", TestCdb12Serialization);
                RunTest("SCSI flush and FUA", TestScsiFlushAndFua);
                RunTest("READ/WRITE12 and cache", TestReadWrite12AndCacheInvalidation);
                RunTest("sequential cache read coalescing", TestSequentialCacheReadCoalescing);
                RunTest("cache eviction and short read rejection", TestCacheEvictionAndShortRead);
                RunTest("VHD logical short read rejection", TestLogicalShortRead);
                RunTest("failed image open releases handles", TestFailedOpenReleasesHandles);
                RunTest("disabled packet logging avoids formatting", TestQuietPacketLogging);
                RunTest("single-client gate", TestSingleClientGate);
                RunTest("initiator-name gate", TestInitiatorNameGate);
                RunTest("80-target worker lifecycle", TestTargetWorkerLifecycle);
                RunTest("classroom cache budget", TestClassroomCacheBudget);
                RunTest("concurrent management pipe", TestConcurrentManagementPipe);
                RunTest("VHDX durable flush", TestVhdxDurableFlush);
                RunTest("VHDX child-only writes", TestVhdxDifferencingWritesOnlyChild);
                if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("ISCSI_CONSOLE_EXE")))
                {
                    RunTest("published headless command and TCP read/write", TestPublishedHeadless);
                }
                Console.WriteLine("ALL_TESTS_PASSED");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("TEST_FAILED: " + ex);
                return 1;
            }
        }

        private static void RunTest(string name, Action test)
        {
            Console.WriteLine("TEST_START " + name);
            Exception failure = null;
            Thread testThread = new Thread(delegate()
            {
                try
                {
                    test();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            testThread.IsBackground = true;
            testThread.Start();
            if (!testThread.Join(30000))
            {
                throw new TimeoutException("Test exceeded 30 seconds: " + name);
            }
            if (failure != null)
            {
                throw new InvalidOperationException("Test failed: " + name, failure);
            }
            Console.WriteLine("TEST_PASSED " + name);
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

        private static void TestSequentialCacheReadCoalescing()
        {
            MemoryDisk inner = new MemoryDisk();
            ISCSIConsole.CachedDisk cached = new ISCSIConsole.CachedDisk(inner, 1);

            int sectorsPerCacheBlock = (ISCSIConsole.CachedDisk.DefaultBlockSizeKB * 1024) / inner.BytesPerSector;
            int requestedSectors = sectorsPerCacheBlock * 8;
            byte[] firstRead = cached.ReadSectors(0, requestedSectors);

            Assert(firstRead.Length == requestedSectors * inner.BytesPerSector, "coalesced read length");
            Assert(inner.ReadCount == 1, "sequential cache misses are coalesced into one inner read");
            Assert(inner.LastReadSectorCount == requestedSectors, "coalesced read covers the requested range");

            cached.ReadSectors(sectorsPerCacheBlock * 7, 1);
            Assert(inner.ReadCount == 1, "coalesced blocks remain cached");
        }

        private static void TestQuietPacketLogging()
        {
            ISCSIServer server = new ISCSIServer();
            server.LogLevel = ISCSI.Logging.Severity.Information;
            int count = 0;
            server.OnLogEntry += delegate(object sender, ISCSI.Logging.LogEntry entry) { count++; };
            // A filtered message must not even reach String.Format.
            server.Log(ISCSI.Logging.Severity.Debug, "{", 1);
            server.Log(ISCSI.Logging.Severity.Verbose, "normal packet");
            Assert(count == 0, "per-packet logs filtered before formatting and output");
            server.Log(ISCSI.Logging.Severity.Warning, "Warning {0}", 1);
            Assert(count == 1, "operational warnings remain visible");

            VirtualSCSITarget target = CreateTarget(new MemoryDisk());
            target.LogLevel = ISCSI.Logging.Severity.Information;
            target.OnLogEntry += delegate(object sender, ISCSI.Logging.LogEntry entry) { count++; };
            target.Log(ISCSI.Logging.Severity.Verbose, "{", 1);
            Assert(count == 1, "SCSI command logs filtered before formatting");
            target.Log(ISCSI.Logging.Severity.Error, "Read error");
            Assert(count == 2, "SCSI errors remain visible");
        }

        private static void TestCacheEvictionAndShortRead()
        {
            MemoryDisk inner = new MemoryDisk();
            ISCSIConsole.CachedDisk cached = new ISCSIConsole.CachedDisk(inner, 1);
            int blockSectors = ISCSIConsole.CachedDisk.DefaultBlockSizeKB * 1024 / inner.BytesPerSector;
            for (int round = 0; round < 8; round++)
            {
                for (int block = 0; block < 32; block += 8)
                {
                    cached.WriteSectors(block * blockSectors, FilledSector((byte)(round + block)));
                    byte[] read = cached.ReadSectors(block * blockSectors, 8 * blockSectors);
                    Assert(read[0] == (byte)(round + block), "eviction does not return stale data");
                }
            }
            Assert(cached.GetStatistics().Contains("cachedMB=1.0"), "full cache remains within its budget");
            MemoryDisk shortDisk = new MemoryDisk();
            shortDisk.ReturnShortRead = true;
            try
            {
                new ISCSIConsole.CachedDisk(shortDisk, 1).ReadSectors(0, 1);
                throw new InvalidOperationException("Incomplete cache read was accepted");
            }
            catch (EndOfStreamException) { }
        }

        private static void TestLogicalShortRead()
        {
            foreach (Type type in new Type[] { typeof(VhdDiskImage), typeof(VhdxDiskImage) })
            {
                System.Reflection.MethodInfo read = type.GetMethod("ReadExactly", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                using (MemoryStream stream = new MemoryStream(new byte[512]))
                {
                    try
                    {
                        read.Invoke(null, new object[] { stream, new byte[1024], 0, 1024 });
                        throw new InvalidOperationException("Unexpected EOF returned as successful zero data");
                    }
                    catch (System.Reflection.TargetInvocationException ex)
                    {
                        Assert(ex.InnerException is EndOfStreamException, "incomplete logical read raises an I/O error");
                    }
                }
            }
        }

        private static void TestFailedOpenReleasesHandles()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ISCSIConsole.Tests.Open." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                foreach (string extension in new string[] { ".vhd", ".vhdx" })
                {
                    string path = Path.Combine(directory, "invalid" + extension);
                    File.WriteAllBytes(path, new byte[1024 * 1024]);
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            if (extension == ".vhd") { new VhdDiskImage(path, false).ReleaseLock(); }
                            else { new VhdxDiskImage(path, false).ReleaseLock(); }
                            throw new InvalidOperationException("Invalid image unexpectedly opened");
                        }
                        catch (IOException) { }
                        // No GC is needed to recover file handles from failed opens.
                        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    }
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        private static System.Diagnostics.Process StartPublished(string exe, string arguments)
        {
            System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo(exe, arguments);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.EnvironmentVariables["DOTNET_ROOT"] = Path.Combine(Path.GetTempPath(), "no-installed-dotnet");
            start.EnvironmentVariables["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            start.EnvironmentVariables["ISCSICONSOLE_DEBUG"] = "0";
            System.Diagnostics.Process process = System.Diagnostics.Process.Start(start);
            process.OutputDataReceived += delegate(object sender, System.Diagnostics.DataReceivedEventArgs e) { if (e.Data != null) { Console.WriteLine("PUBLISHED " + e.Data); } };
            process.ErrorDataReceived += delegate(object sender, System.Diagnostics.DataReceivedEventArgs e) { if (e.Data != null) { Console.WriteLine("PUBLISHED_ERROR " + e.Data); } };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        private static void PublishedCommand(string exe, string arguments)
        {
            using (System.Diagnostics.Process process = StartPublished(exe, arguments))
            {
                if (!process.WaitForExit(10000)) { process.Kill(); throw new TimeoutException("Published command timed out: " + arguments); }
                Assert(process.ExitCode == 0, "published command: " + arguments);
            }
        }

        private static void TestPublishedHeadless()
        {
            string exe = Environment.GetEnvironmentVariable("ISCSI_CONSOLE_EXE");
            string directory = Path.Combine(Path.GetTempPath(), "ISCSIConsole.Tests.Exe." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string configPath = Path.Combine(directory, "targets.xml");
            string statusPath = Path.Combine(directory, "ready.txt");
            string logPath = Path.Combine(directory, "service.log");
            string diskPath = Path.Combine(directory, "disk.vhdx");
            string configArgument = " /config \"" + configPath + "\"";
            System.Diagnostics.Process service = null;
            try
            {
                System.Net.Sockets.TcpListener portProbe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                portProbe.Start();
                int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
                portProbe.Stop();
                ServiceConfiguration configuration = new ServiceConfiguration();
                configuration.ListenAddress = "127.0.0.1";
                configuration.Port = port;
                configuration.Save(configPath);
                VhdxDiskImage image = VhdxDiskImage.CreateDynamicDisk(diskPath, 64L * 1024 * 1024);
                image.WriteSectors(2048, FilledSector(0x37));
                image.Flush();
                image.ReleaseLock();
                service = StartPublished(exe, "/start" + configArgument + " /status \"" + statusPath + "\" /log \"" + logPath + "\"");
                DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                while (DateTime.UtcNow < deadline && !service.HasExited && !File.Exists(statusPath)) { Thread.Sleep(100); }
                Assert(File.Exists(statusPath) && File.ReadAllText(statusPath).StartsWith("READY"), "published service ready without installed runtime");
                PublishedCommand(exe, "/addtarget \"" + diskPath + "\" test /cachemb 1 /nosave" + configArgument);
                PublishedCommand(exe, "/list" + configArgument);
                ISCSI.Client.ISCSIDisk remote = new ISCSI.Client.ISCSIDisk();
                try
                {
                    Assert(remote.Connect(IPAddress.Loopback, port, "iqn.1991-05.com.microsoft:test", 0), "published iSCSI login");
                    Assert(remote.ReadSectors(2048, 1)[0] == 0x37, "published VHDX sector read");
                    remote.WriteSectors(2048, FilledSector(0x72));
                    Assert(remote.ReadSectors(2048, 1)[0] == 0x72, "published VHDX write invalidates cache");
                }
                finally { remote.Disconnect(); }
                PublishedCommand(exe, "/save" + configArgument);
                PublishedCommand(exe, "/removetarget test /force /nosave" + configArgument);
                VhdxDiskImage reopened = new VhdxDiskImage(diskPath, true);
                try { Assert(reopened.ReadSectors(2048, 1)[0] == 0x72, "published target removal flushes data"); }
                finally { reopened.ReleaseLock(); }
                PublishedCommand(exe, "/stop" + configArgument);
                Assert(service.WaitForExit(5000) && service.ExitCode == 0, "published service stops cleanly");
                string serviceLog = File.ReadAllText(logPath);
                Assert(serviceLog.Contains("Starting Server"), "normal /log retains operational information");
                Assert(!serviceLog.Contains("Verbose") && !serviceLog.Contains("Debug"), "normal /log skips per-packet and SCSI command logging");
            }
            finally
            {
                if (service != null) { if (!service.HasExited) { service.Kill(); service.WaitForExit(5000); } service.Dispose(); }
                Directory.Delete(directory, true);
            }
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
            Assert(DiskConfiguration.DefaultServiceCacheSizeMB == 32, "classroom target default cache");
            long maximumCacheMB = 80L * DiskConfiguration.DefaultServiceCacheSizeMB;
            Assert(maximumCacheMB <= 2560, "80-target cache budget stays at or below 2560 MB");

            TargetConfiguration configuration = new TargetConfiguration();
            configuration.TargetName = "iqn.2026-08.cn.bscx:test-client";
            configuration.AllowedInitiatorName = " iqn.2026-08.cn.bscx:mac-aabbccddeeff ";
            configuration.Disks.Add(DiskConfiguration.CreateDiskImage("client.vhdx", false));
            configuration.Normalize();
            Assert(configuration.AllowedInitiatorName == "iqn.2026-08.cn.bscx:mac-aabbccddeeff", "initiator binding is normalized");
            Assert(configuration.Disks[0].CacheSizeMB == 32, "new service disk uses classroom cache default");
            Assert(ISCSIServer.DeclaredParameters.MaxRecvDataSegmentLength == 1024 * 1024, "server advertises a 1MB data segment");
            Assert(ISCSIServer.DesiredParameters.MaxBurstLength == 1024 * 1024, "server allows 1MB SCSI bursts");
            Assert(ISCSIServer.SocketBufferSize >= 2 * 1024 * 1024, "server socket buffers are sized for burst traffic");
        }

        private static void TestConcurrentManagementPipe()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ISCSIConsole.Tests.Pipe." + Guid.NewGuid().ToString("N"));
            string configPath = Path.Combine(directory, "service.xml");
            Directory.CreateDirectory(directory);
            ISCSIServer server = new ISCSIServer();
            HeadlessServiceRuntime runtime = new HeadlessServiceRuntime(server, new ServiceConfiguration(), configPath);
            try
            {
                server.Start(new IPEndPoint(IPAddress.Loopback, 0), null);
                runtime.StartManagementPipe();
                Exception[] errors = new Exception[32];
                Thread[] clients = new Thread[32];
                CountdownEvent completed = new CountdownEvent(clients.Length);
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
                        finally
                        {
                            completed.Signal();
                        }
                    });
                    clients[index].IsBackground = true;
                    clients[index].Start();
                }

                Assert(completed.Wait(15000), "all concurrent management clients complete within 15 seconds");
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
                server.Stop();
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
            Exception testFailure = null;
            try
            {
                ISCSIConsole.VhdxDiskImage parent = ISCSIConsole.VhdxDiskImage.CreateDynamicDisk(parentPath, 64L * 1024 * 1024);
                parent.WriteSectors(2048, FilledSector(0x31));
                parent.Flush();
                parent.ReleaseLock();
                byte[] parentHashBefore = ComputeHash(parentPath);

                CreateDifferencingVhdx(childPath, parentPath);

                Console.WriteLine("VHDX_DIFF_STAGE open-writable-child");
                ISCSIConsole.VhdxDiskImage writableChild = new ISCSIConsole.VhdxDiskImage(childPath, false);
                Assert(writableChild.ReadSectors(2048, 1)[0] == 0x31, "child reads unchanged sectors from parent");
                writableChild.WriteSectors(2048, FilledSector(0x72));
                writableChild.Flush();
                writableChild.ReleaseLock();

                Console.WriteLine("VHDX_DIFF_STAGE hash-parent");
                byte[] parentHashAfter = ComputeHash(parentPath);
                Assert(ByteArraysEqual(parentHashBefore, parentHashAfter), "child write does not modify parent VHDX bytes");

                ISCSIConsole.VhdxDiskImage reopenedChild = new ISCSIConsole.VhdxDiskImage(childPath, true);
                Assert(reopenedChild.ReadSectors(2048, 1)[0] == 0x72, "child write survives flush and reopen");
                reopenedChild.ReleaseLock();

                ISCSIConsole.VhdxDiskImage reopenedParent = new ISCSIConsole.VhdxDiskImage(parentPath, true);
                Assert(reopenedParent.ReadSectors(2048, 1)[0] == 0x31, "parent logical content remains unchanged");
                reopenedParent.ReleaseLock();
            }
            catch (Exception ex)
            {
                testFailure = ex;
                throw;
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    try
                    {
                        Directory.Delete(directory, true);
                    }
                    catch (Exception cleanupFailure)
                    {
                        if (testFailure == null)
                        {
                            throw;
                        }
                        Console.Error.WriteLine("VHDX_DIFF_CLEANUP_FAILED: " + cleanupFailure);
                    }
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

        private static void CreateDifferencingVhdx(string childPath, string parentPath)
        {
            VirtualStorageType storageType = new VirtualStorageType();

            IntPtr parentPathPointer = Marshal.StringToHGlobalUni(Path.GetFullPath(parentPath));
            try
            {
                CreateVirtualDiskParameters parameters = new CreateVirtualDiskParameters();
                parameters.Version = 2;
                parameters.Version2.UniqueId = Guid.NewGuid();
                parameters.Version2.ParentPath = parentPathPointer;

                SafeFileHandle handle;
                uint result = CreateVirtualDisk(
                    ref storageType,
                    Path.GetFullPath(childPath),
                    0,
                    IntPtr.Zero,
                    0,
                    0,
                    ref parameters,
                    IntPtr.Zero,
                    out handle);
                if (handle != null)
                {
                    handle.Dispose();
                }
                if (result != 0)
                {
                    throw new Win32Exception(
                        (int)result,
                        "CreateVirtualDisk failed for the differencing VHDX (" + result + ", 0x" + result.ToString("X8") + ").");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(parentPathPointer);
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
