#if !NET20
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using DiskAccessLibrary;
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
                   (IsOption(args[0], "server") || IsOption(args[0], "serve") || IsDiskImagePath(args[0]));
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

            options.Port = DefaultPort;
            options.ListenAddress = IPAddress.Any;

            if (args == null || args.Length == 0)
            {
                error = "Missing disk image path.";
                return false;
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

                    if (value == "*" || value == "0.0.0.0")
                    {
                        options.ListenAddress = IPAddress.Any;
                    }
                    else if (!IPAddress.TryParse(value, out options.ListenAddress))
                    {
                        error = "Invalid listen address: " + value;
                        return false;
                    }
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
            return "Usage: ISCSIConsole.exe <path.vhdx> [target-name] [/listen 0.0.0.0] [/port 3260] [/readonly] [/status <path>] [/stopfile <path>]";
        }

        private class ServeOptions
        {
            public string DiskPath;
            public string TargetName;
            public IPAddress ListenAddress;
            public int Port;
            public bool ReadOnly;
            public string StatusPath;
            public string StopFilePath;
        }
    }
}
#endif
