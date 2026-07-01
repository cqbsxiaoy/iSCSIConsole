#if !NET20
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using DiskAccessLibrary;
using DiskAccessLibrary.LogicalDiskManager;
using DiskAccessLibrary.Win32;
using ISCSI.Logging;
using ISCSI.Server;

namespace ISCSIConsole
{
    internal class HeadlessServer
    {
        private const string DefaultTargetPrefix = "iqn.1991-05.com.microsoft";
        private const int DefaultPort = 3260;

        public static bool IsServeCommand(string[] args)
        {
            return args != null &&
                   args.Length > 0 &&
                   (IsOption(args[0], "server") ||
                    IsOption(args[0], "serve") ||
                    IsOption(args[0], "start") ||
                    IsOption(args[0], "stop") ||
                    IsDiskImagePath(args[0]));
        }

        public static int Run(string[] args)
        {
            ServeOptions options;
            string error;
            if (!TryParseOptions(args, out options, out error))
            {
                WriteError(options, error);
                Console.Error.WriteLine(GetUsage());
                return 2;
            }

            if (options.Mode == ServeMode.StopConfig)
            {
                return StopConfiguredServer(options);
            }
            if (options.Mode == ServeMode.StartConfig)
            {
                return RunConfiguredServer(options);
            }

            return RunSingleDiskServer(options);
        }

        private static int RunSingleDiskServer(ServeOptions options)
        {
            ISCSIServer server = null;
            List<Disk> disks = null;
            try
            {
                DiskImage disk = OpenDiskImage(options.DiskPath, options.ReadOnly);
                disks = new List<Disk>();
                disks.Add(disk);

                ISCSITarget target = new ISCSITarget(options.TargetName, disks);
                target.OnAuthorizationRequest += delegate(object sender, AuthorizationRequestArgs request)
                {
                    request.Accept = true;
                };
                target.OnSessionTermination += delegate(object sender, SessionTerminationArgs request)
                {
                    Console.WriteLine("SESSION_TERMINATED target={0} initiator={1}", options.TargetName, request.InitiatorName);
                };

                server = new ISCSIServer();
                server.OnLogEntry += Server_OnLogEntry;
                server.AddTarget(target);
                EnsureFirewallRule(options.Port);
                server.Start(new IPEndPoint(options.ListenAddress, options.Port));

                string ready = String.Format(
                    "READY iqn={0} address={1} port={2} disk=\"{3}\" readonly={4}",
                    options.TargetName,
                    options.ListenAddress,
                    options.Port,
                    options.DiskPath,
                    disk.IsReadOnly);
                Console.WriteLine(ready);
                WriteStatus(options.StatusPath, ready);

                using (ManualResetEvent stopEvent = new ManualResetEvent(false))
                {
                    Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs eventArgs)
                    {
                        eventArgs.Cancel = true;
                        stopEvent.Set();
                    };

                    while (true)
                    {
                        if (stopEvent.WaitOne(1000))
                        {
                            break;
                        }

                        if (!String.IsNullOrEmpty(options.StopFilePath) && File.Exists(options.StopFilePath))
                        {
                            break;
                        }
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                WriteError(options, ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
            finally
            {
                StopAndRelease(server, disks);
            }
        }

        private static int RunConfiguredServer(ServeOptions options)
        {
            ISCSIServer server = null;
            List<Disk> allDisks = new List<Disk>();
            string statePath = GetStatePath(options.ConfigPath);
            string stopFilePath = String.IsNullOrEmpty(options.StopFilePath) ? GetDefaultStopFilePath(options.ConfigPath) : options.StopFilePath;

            try
            {
                ServiceConfiguration configuration = ServiceConfiguration.Load(options.ConfigPath);
                if (options.HasListenAddressOverride)
                {
                    configuration.ListenAddress = options.ListenAddress.ToString();
                }
                if (options.HasPortOverride)
                {
                    configuration.Port = options.Port;
                }

                IPAddress listenAddress;
                if (!TryParseListenAddress(configuration.ListenAddress, out listenAddress))
                {
                    throw new InvalidDataException("Invalid listen address in configuration: " + configuration.ListenAddress);
                }
                if (configuration.Port <= 0 || configuration.Port > UInt16.MaxValue)
                {
                    throw new InvalidDataException("Invalid TCP port in configuration: " + configuration.Port);
                }
                if (configuration.Targets.Count == 0)
                {
                    throw new InvalidDataException("The service configuration does not contain any target.");
                }

                server = new ISCSIServer();
                server.OnLogEntry += Server_OnLogEntry;

                foreach (TargetConfiguration targetConfiguration in configuration.Targets)
                {
                    ISCSITarget target = CreateTarget(targetConfiguration, allDisks);
                    server.AddTarget(target);
                }

                DeleteFileIfExists(stopFilePath);
                EnsureFirewallRule(configuration.Port);
                server.Start(new IPEndPoint(listenAddress, configuration.Port));
                WriteState(statePath, stopFilePath);

                string ready = String.Format(
                    "READY config=\"{0}\" targets={1} address={2} port={3}",
                    options.ConfigPath,
                    configuration.Targets.Count,
                    listenAddress,
                    configuration.Port);
                Console.WriteLine(ready);
                WriteStatus(options.StatusPath, ready);

                WaitForStopFile(stopFilePath);
                return 0;
            }
            catch (Exception ex)
            {
                WriteError(options, ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
            finally
            {
                DeleteFileIfExists(statePath);
                DeleteFileIfExists(stopFilePath);
                StopAndRelease(server, allDisks);
            }
        }

        private static int StopConfiguredServer(ServeOptions options)
        {
            try
            {
                string statePath = GetStatePath(options.ConfigPath);
                if (!File.Exists(statePath))
                {
                    Console.Error.WriteLine("ERROR: No running service state was found: " + statePath);
                    return 1;
                }

                string stopFilePath = ReadStateStopFilePath(statePath);
                if (String.IsNullOrEmpty(stopFilePath))
                {
                    stopFilePath = GetDefaultStopFilePath(options.ConfigPath);
                }

                WriteStatus(stopFilePath, "stop");
                Console.WriteLine("STOP_REQUESTED stopfile=\"{0}\"", stopFilePath);
                return 0;
            }
            catch (Exception ex)
            {
                WriteError(options, ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        private static ISCSITarget CreateTarget(TargetConfiguration targetConfiguration, List<Disk> allDisks)
        {
            if (targetConfiguration == null || String.IsNullOrEmpty(targetConfiguration.TargetName))
            {
                throw new InvalidDataException("A target in the service configuration has no IQN.");
            }
            if (!ISCSINameHelper.IsValidIQN(targetConfiguration.TargetName))
            {
                throw new InvalidDataException("Invalid target IQN in configuration: " + targetConfiguration.TargetName);
            }
            if (targetConfiguration.Disks == null || targetConfiguration.Disks.Count == 0)
            {
                throw new InvalidDataException("Target has no disk: " + targetConfiguration.TargetName);
            }

            List<Disk> disks = new List<Disk>();
            foreach (DiskConfiguration diskConfiguration in targetConfiguration.Disks)
            {
                Disk disk = OpenConfiguredDisk(diskConfiguration);
                disks.Add(disk);
                allDisks.Add(disk);
            }

            ISCSITarget target = new ISCSITarget(targetConfiguration.TargetName, disks);
            target.OnAuthorizationRequest += delegate(object sender, AuthorizationRequestArgs request)
            {
                request.Accept = true;
            };
            target.OnSessionTermination += delegate(object sender, SessionTerminationArgs request)
            {
                Console.WriteLine("SESSION_TERMINATED target={0} initiator={1}", targetConfiguration.TargetName, request.InitiatorName);
            };
            return target;
        }

        private static Disk OpenConfiguredDisk(DiskConfiguration diskConfiguration)
        {
            if (diskConfiguration == null || String.IsNullOrEmpty(diskConfiguration.Type))
            {
                throw new InvalidDataException("A disk in the service configuration has no type.");
            }

            if (diskConfiguration.Type.Equals(DiskConfiguration.TypeDiskImage, StringComparison.InvariantCultureIgnoreCase))
            {
                if (String.IsNullOrEmpty(diskConfiguration.Path))
                {
                    throw new InvalidDataException("Disk image path is empty.");
                }
                string path = Path.GetFullPath(diskConfiguration.Path);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Disk image was not found.", path);
                }
                return OpenDiskImage(path, diskConfiguration.ReadOnly);
            }

            if (diskConfiguration.Type.Equals(DiskConfiguration.TypePhysicalDisk, StringComparison.InvariantCultureIgnoreCase))
            {
                return OpenPhysicalDisk(diskConfiguration.PhysicalDiskIndex, diskConfiguration.ReadOnly);
            }

            if (diskConfiguration.Type.Equals(DiskConfiguration.TypeVolume, StringComparison.InvariantCultureIgnoreCase))
            {
                return OpenVolumeDisk(diskConfiguration.VolumeGuid, diskConfiguration.ReadOnly);
            }

            throw new InvalidDataException("Unsupported disk type in service configuration: " + diskConfiguration.Type);
        }

        private static PhysicalDisk OpenPhysicalDisk(int physicalDiskIndex, bool readOnly)
        {
            PhysicalDisk disk = new PhysicalDisk(physicalDiskIndex, readOnly);
            if (readOnly)
            {
                return disk;
            }

            if (Environment.OSVersion.Version.Major >= 6)
            {
                bool isDiskReadOnly;
                bool isOnline = disk.GetOnlineStatus(out isDiskReadOnly);
                if (isDiskReadOnly)
                {
                    throw new UnauthorizedAccessException("Physical disk is marked read-only: " + physicalDiskIndex);
                }
                if (isOnline && !disk.SetOnlineStatus(false))
                {
                    throw new IOException("Failed to set physical disk offline: " + physicalDiskIndex);
                }
            }
            else if (!DynamicDisk.IsDynamicDisk(disk))
            {
                LockStatus status = LockHelper.LockBasicDiskAndVolumesOrNone(disk);
                if (status == LockStatus.CannotLockDisk)
                {
                    throw new IOException("Failed to lock physical disk: " + physicalDiskIndex);
                }
                if (status == LockStatus.CannotLockVolume)
                {
                    throw new IOException("Failed to lock a volume on physical disk: " + physicalDiskIndex);
                }
            }

            return disk;
        }

        private static VolumeDisk OpenVolumeDisk(string volumeGuidText, bool readOnly)
        {
            if (String.IsNullOrEmpty(volumeGuidText))
            {
                throw new InvalidDataException("Volume GUID is empty.");
            }

            Guid volumeGuid = new Guid(volumeGuidText);
            Volume volume = FindVolume(volumeGuid);
            if (volume == null)
            {
                throw new FileNotFoundException("Volume was not found: " + volumeGuidText);
            }

            if (!readOnly)
            {
                bool skipLock = Environment.OSVersion.Version.Major >= 6 && VolumeInfo.IsOffline(volume);
                if (!skipLock)
                {
                    if (Environment.OSVersion.Version.Major >= 6)
                    {
                        volume = new OperatingSystemVolume(volumeGuid, volume.BytesPerSector, volume.Size, readOnly);
                    }

                    bool isLocked = WindowsVolumeManager.ExclusiveLock(volumeGuid);
                    if (!isLocked)
                    {
                        throw new IOException("Failed to lock volume: " + volumeGuidText);
                    }
                }
            }

            return new VolumeDisk(volume, readOnly);
        }

        private static Volume FindVolume(Guid volumeGuid)
        {
            return WindowsVolumeHelper.GetVolumeByGuid(volumeGuid);
        }

        private static void StopAndRelease(ISCSIServer server, List<Disk> disks)
        {
            if (server != null)
            {
                try
                {
                    server.Stop();
                }
                catch
                {
                }
            }

            if (disks != null)
            {
                try
                {
                    LockUtils.ReleaseDisks(disks);
                }
                catch
                {
                }
            }
        }

        private static void WaitForStopFile(string stopFilePath)
        {
            using (ManualResetEvent stopEvent = new ManualResetEvent(false))
            {
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs eventArgs)
                {
                    eventArgs.Cancel = true;
                    stopEvent.Set();
                };

                while (true)
                {
                    if (stopEvent.WaitOne(1000))
                    {
                        break;
                    }

                    if (!String.IsNullOrEmpty(stopFilePath) && File.Exists(stopFilePath))
                    {
                        break;
                    }
                }
            }
        }

        private static DiskImage OpenDiskImage(string diskPath, bool readOnly)
        {
            if (diskPath.EndsWith(".vhdx", StringComparison.InvariantCultureIgnoreCase))
            {
                return new VhdxDiskImage(diskPath, readOnly);
            }

            return DiskImage.GetDiskImage(diskPath, readOnly);
        }

        private static bool TryParseOptions(string[] args, out ServeOptions options, out string error)
        {
            options = new ServeOptions();
            error = null;

            options.Mode = ServeMode.SingleDisk;
            options.Port = DefaultPort;
            options.ListenAddress = IPAddress.Any;
            options.ConfigPath = ServiceConfiguration.GetDefaultPath();

            if (args == null || args.Length == 0)
            {
                error = "Missing command or disk image path.";
                return false;
            }

            if (IsOption(args[0], "start"))
            {
                options.Mode = ServeMode.StartConfig;
                return TryParseConfigOptions(args, 1, options, out error);
            }
            if (IsOption(args[0], "stop"))
            {
                options.Mode = ServeMode.StopConfig;
                return TryParseConfigOptions(args, 1, options, out error);
            }

            int index = 0;
            if (IsOption(args[0], "server") || IsOption(args[0], "serve"))
            {
                index = 1;
                if (index < args.Length && !IsOption(args[index]))
                {
                    options.DiskPath = args[index];
                    index++;
                }
            }
            else if (IsDiskImagePath(args[0]))
            {
                options.DiskPath = args[0];
                index = 1;
            }
            else
            {
                error = "First argument must be a VHD/VHDX disk image path.";
                return false;
            }

            if (index < args.Length && !IsOption(args[index]))
            {
                options.TargetName = BuildTargetName(args[index]);
                index++;
            }

            for (; index < args.Length; index++)
            {
                string key = args[index];
                if (IsOption(key, "disk"))
                {
                    if (!TryReadValue(args, ref index, out options.DiskPath, out error))
                    {
                        return false;
                    }
                }
                else if (IsOption(key, "target") || IsOption(key, "iqn"))
                {
                    if (!TryReadValue(args, ref index, out options.TargetName, out error))
                    {
                        return false;
                    }
                    options.TargetName = BuildTargetName(options.TargetName);
                }
                else if (IsOption(key, "listen") || IsOption(key, "ip"))
                {
                    string value;
                    if (!TryReadValue(args, ref index, out value, out error))
                    {
                        return false;
                    }

                    if (!TryParseListenAddress(value, out options.ListenAddress))
                    {
                        error = "Invalid listen address: " + value;
                        return false;
                    }
                    options.HasListenAddressOverride = true;
                }
                else if (IsOption(key, "port"))
                {
                    string value;
                    if (!TryReadValue(args, ref index, out value, out error))
                    {
                        return false;
                    }

                    int port;
                    if (!Int32.TryParse(value, out port) || port <= 0 || port > UInt16.MaxValue)
                    {
                        error = "Invalid TCP port: " + value;
                        return false;
                    }
                    options.Port = port;
                    options.HasPortOverride = true;
                }
                else if (IsOption(key, "readonly"))
                {
                    options.ReadOnly = true;
                }
                else if (IsOption(key, "status"))
                {
                    if (!TryReadValue(args, ref index, out options.StatusPath, out error))
                    {
                        return false;
                    }
                }
                else if (IsOption(key, "stopfile"))
                {
                    if (!TryReadValue(args, ref index, out options.StopFilePath, out error))
                    {
                        return false;
                    }
                }
                else if (IsOption(key, "help") || IsOption(key, "?"))
                {
                    error = GetUsage();
                    return false;
                }
                else
                {
                    error = "Unknown argument: " + key;
                    return false;
                }
            }

            if (String.IsNullOrEmpty(options.DiskPath))
            {
                error = "Missing /disk path.";
                return false;
            }

            options.DiskPath = Path.GetFullPath(options.DiskPath);
            if (!File.Exists(options.DiskPath))
            {
                error = "Disk image was not found: " + options.DiskPath;
                return false;
            }

            if (!options.DiskPath.EndsWith(".vhd", StringComparison.InvariantCultureIgnoreCase) &&
                !options.DiskPath.EndsWith(".vhdx", StringComparison.InvariantCultureIgnoreCase))
            {
                error = "Only VHD and VHDX disk images are supported in command line mode.";
                return false;
            }

            if (String.IsNullOrEmpty(options.TargetName))
            {
                options.TargetName = BuildTargetName(Path.GetFileNameWithoutExtension(options.DiskPath));
            }

            if (!ISCSINameHelper.IsValidIQN(options.TargetName))
            {
                error = "Invalid target IQN: " + options.TargetName;
                return false;
            }

            return true;
        }

        private static bool TryParseConfigOptions(string[] args, int index, ServeOptions options, out string error)
        {
            error = null;

            for (; index < args.Length; index++)
            {
                string key = args[index];
                if (IsOption(key, "config"))
                {
                    if (!TryReadValue(args, ref index, out options.ConfigPath, out error))
                    {
                        return false;
                    }
                }
                else if (IsOption(key, "listen") || IsOption(key, "ip"))
                {
                    string value;
                    if (!TryReadValue(args, ref index, out value, out error))
                    {
                        return false;
                    }

                    if (!TryParseListenAddress(value, out options.ListenAddress))
                    {
                        error = "Invalid listen address: " + value;
                        return false;
                    }
                    options.HasListenAddressOverride = true;
                }
                else if (IsOption(key, "port"))
                {
                    string value;
                    if (!TryReadValue(args, ref index, out value, out error))
                    {
                        return false;
                    }

                    int port;
                    if (!Int32.TryParse(value, out port) || port <= 0 || port > UInt16.MaxValue)
                    {
                        error = "Invalid TCP port: " + value;
                        return false;
                    }
                    options.Port = port;
                    options.HasPortOverride = true;
                }
                else if (IsOption(key, "status"))
                {
                    if (!TryReadValue(args, ref index, out options.StatusPath, out error))
                    {
                        return false;
                    }
                }
                else if (IsOption(key, "stopfile"))
                {
                    if (!TryReadValue(args, ref index, out options.StopFilePath, out error))
                    {
                        return false;
                    }
                }
                else if (IsOption(key, "help") || IsOption(key, "?"))
                {
                    error = GetUsage();
                    return false;
                }
                else
                {
                    error = "Unknown argument: " + key;
                    return false;
                }
            }

            options.ConfigPath = Path.GetFullPath(options.ConfigPath);
            if (options.Mode == ServeMode.StartConfig && !File.Exists(options.ConfigPath))
            {
                error = "Service configuration was not found: " + options.ConfigPath;
                return false;
            }

            return true;
        }

        private static bool TryReadValue(string[] args, ref int index, out string value, out string error)
        {
            value = null;
            error = null;

            if (index + 1 >= args.Length)
            {
                error = "Missing value for " + args[index];
                return false;
            }

            index++;
            value = args[index];
            return true;
        }

        private static bool TryParseListenAddress(string value, out IPAddress listenAddress)
        {
            if (value == "*" || value == "0.0.0.0")
            {
                listenAddress = IPAddress.Any;
                return true;
            }

            return IPAddress.TryParse(value, out listenAddress);
        }

        private static string GetStatePath(string configPath)
        {
            return Path.GetFullPath(configPath) + ".state";
        }

        private static string GetDefaultStopFilePath(string configPath)
        {
            return Path.GetFullPath(configPath) + ".stop";
        }

        private static void WriteState(string statePath, string stopFilePath)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(statePath));
            if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string[] lines = new string[]
            {
                "pid=" + Process.GetCurrentProcess().Id,
                "stopfile=" + stopFilePath
            };
            File.WriteAllLines(statePath, lines);
        }

        private static string ReadStateStopFilePath(string statePath)
        {
            string[] lines = File.ReadAllLines(statePath);
            foreach (string line in lines)
            {
                if (line.StartsWith("stopfile=", StringComparison.InvariantCultureIgnoreCase))
                {
                    return line.Substring("stopfile=".Length).Trim();
                }
            }
            return null;
        }

        private static void DeleteFileIfExists(string path)
        {
            if (!String.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                }
            }
        }

        private static bool IsOption(string value)
        {
            return !String.IsNullOrEmpty(value) &&
                   (value.StartsWith("/") || value.StartsWith("-"));
        }

        private static bool IsOption(string value, string name)
        {
            if (String.IsNullOrEmpty(value))
            {
                return false;
            }

            return value.Equals("/" + name, StringComparison.InvariantCultureIgnoreCase) ||
                   value.Equals("-" + name, StringComparison.InvariantCultureIgnoreCase) ||
                   value.Equals("--" + name, StringComparison.InvariantCultureIgnoreCase);
        }

        private static bool IsDiskImagePath(string value)
        {
            return !String.IsNullOrEmpty(value) &&
                   (value.EndsWith(".vhd", StringComparison.InvariantCultureIgnoreCase) ||
                    value.EndsWith(".vhdx", StringComparison.InvariantCultureIgnoreCase));
        }

        private static string BuildTargetName(string value)
        {
            value = (value ?? String.Empty).Trim();
            if (value.StartsWith("iqn.", StringComparison.InvariantCultureIgnoreCase))
            {
                return value;
            }

            return DefaultTargetPrefix + ":" + NormalizeTargetSuffix(value);
        }

        private static string NormalizeTargetSuffix(string value)
        {
            value = (value ?? String.Empty).Trim().ToLowerInvariant();
            if (value.Length == 0)
            {
                return "target";
            }

            char[] result = new char[value.Length];
            for (int index = 0; index < value.Length; index++)
            {
                char c = value[index];
                bool valid = (c >= 'a' && c <= 'z') ||
                             (c >= '0' && c <= '9') ||
                             c == '-' ||
                             c == '_' ||
                             c == '.';
                result[index] = valid ? c : '-';
            }
            string suffix = new String(result).Trim('-');
            return suffix.Length > 0 ? suffix : "target";
        }

        private static void Server_OnLogEntry(object sender, LogEntry entry)
        {
            Console.WriteLine("{0:u} [{1}] {2}: {3}", entry.Time, entry.Severity, entry.Source, entry.Message);
        }

        private static void EnsureFirewallRule(int port)
        {
            try
            {
                RunNetsh("advfirewall firewall delete rule name=\"iSCSIConsole Target\"");
                RunNetsh(String.Format("advfirewall firewall add rule name=\"iSCSIConsole Target\" dir=in action=allow protocol=TCP localport={0}", port));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WARNING: Failed to update Windows Firewall rule: " + ex.Message);
            }
        }

        private static void RunNetsh(string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "netsh.exe";
            startInfo.Arguments = arguments;
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            using (Process process = Process.Start(startInfo))
            {
                if (process != null)
                {
                    process.WaitForExit(5000);
                }
            }
        }

        private static void WriteStatus(string path, string content)
        {
            if (String.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, content + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static void WriteError(ServeOptions options, string message)
        {
            Console.Error.WriteLine("ERROR: " + message);
            if (options != null)
            {
                WriteStatus(options.StatusPath, "ERROR " + message);
            }
        }

        private static string GetUsage()
        {
            return "Usage:\r\n" +
                   "  ISCSIConsole.exe <path.vhdx> [target-name] [/listen 0.0.0.0] [/port 3260] [/readonly] [/status <path>] [/stopfile <path>]\r\n" +
                   "  ISCSIConsole.exe /start [/config <path>] [/listen <ip>] [/port <port>] [/status <path>]\r\n" +
                   "  ISCSIConsole.exe /stop [/config <path>]";
        }

        private enum ServeMode
        {
            SingleDisk,
            StartConfig,
            StopConfig
        }

        private class ServeOptions
        {
            public ServeMode Mode;
            public string DiskPath;
            public string TargetName;
            public IPAddress ListenAddress;
            public int Port;
            public bool HasListenAddressOverride;
            public bool HasPortOverride;
            public bool ReadOnly;
            public string ConfigPath;
            public string StatusPath;
            public string StopFilePath;
        }
    }
}
#endif
