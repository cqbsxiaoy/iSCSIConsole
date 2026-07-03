#if !NET20
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using DiskAccessLibrary;
using ISCSI.Server;

namespace ISCSIConsole
{
    internal class HeadlessServiceRuntime
    {
        private const int PipeConnectTimeoutMilliseconds = 5000;

        private class RuntimeTarget
        {
            public ISCSITarget Target;
            public List<Disk> Disks;
            public TargetConfiguration Configuration;
        }

        private readonly object m_lock = new object();
        private readonly ISCSIServer m_server;
        private readonly ServiceConfiguration m_configuration;
        private readonly Dictionary<string, RuntimeTarget> m_targets = new Dictionary<string, RuntimeTarget>(StringComparer.InvariantCultureIgnoreCase);
        private readonly string m_configPath;
        private readonly string m_pipeName;
        private readonly ManualResetEvent m_stopRequested = new ManualResetEvent(false);
        private bool m_stopping;
        private bool m_serverStopQueued;
        private Thread m_pipeThread;

        public HeadlessServiceRuntime(ISCSIServer server, ServiceConfiguration configuration, string configPath)
        {
            m_server = server;
            m_configuration = configuration;
            m_configPath = Path.GetFullPath(configPath);
            m_pipeName = "iSCSIConsole-" + GetStableHash(m_configPath);
        }

        public string PipeName
        {
            get
            {
                return m_pipeName;
            }
        }

        public int TargetCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_targets.Count;
                }
            }
        }

        public bool StopRequested
        {
            get
            {
                return m_stopRequested.WaitOne(0);
            }
        }

        public void AddInitialTarget(TargetConfiguration targetConfiguration)
        {
            AddTarget(targetConfiguration, false);
        }

        public void StartManagementPipe()
        {
            m_pipeThread = new Thread(PipeServerLoop);
            m_pipeThread.IsBackground = true;
            m_pipeThread.Start();
        }

        public void Stop()
        {
            m_stopping = true;
            WakePipeServer();

            lock (m_lock)
            {
                foreach (RuntimeTarget runtimeTarget in m_targets.Values)
                {
                    LockUtils.ReleaseDisks(runtimeTarget.Disks);
                }
                m_targets.Clear();
            }
        }

        private string AddTarget(TargetConfiguration targetConfiguration, bool save)
        {
            if (targetConfiguration == null)
            {
                throw new InvalidDataException("Target configuration is empty.");
            }
            targetConfiguration.Normalize();

            List<Disk> disks = new List<Disk>();
            bool addedToConfiguration = false;
            bool addedToServer = false;
            try
            {
                ISCSITarget target = HeadlessServer.CreateTarget(targetConfiguration, disks);
                lock (m_lock)
                {
                    if (m_targets.ContainsKey(targetConfiguration.TargetName))
                    {
                        throw new InvalidOperationException("Target already exists: " + targetConfiguration.TargetName);
                    }

                    if (!ContainsTargetConfiguration(targetConfiguration.TargetName))
                    {
                        m_configuration.Targets.Add(targetConfiguration);
                        addedToConfiguration = true;
                    }
                    if (save)
                    {
                        m_configuration.Save(m_configPath);
                    }

                    m_server.AddTarget(target);
                    addedToServer = true;
                    RuntimeTarget runtimeTarget = new RuntimeTarget();
                    runtimeTarget.Target = target;
                    runtimeTarget.Disks = disks;
                    runtimeTarget.Configuration = targetConfiguration;
                    m_targets.Add(targetConfiguration.TargetName, runtimeTarget);
                }
            }
            catch
            {
                if (addedToServer)
                {
                    try
                    {
                        m_server.RemoveTarget(targetConfiguration.TargetName);
                    }
                    catch
                    {
                    }
                }
                if (addedToConfiguration)
                {
                    RemoveTargetConfiguration(targetConfiguration.TargetName);
                    try
                    {
                        if (save)
                        {
                            m_configuration.Save(m_configPath);
                        }
                    }
                    catch
                    {
                    }
                }
                LockUtils.ReleaseDisks(disks);
                throw;
            }

            return "OK ADDED target=" + targetConfiguration.TargetName;
        }

        private string RemoveTarget(string targetName, bool save)
        {
            targetName = HeadlessServer.BuildTargetName(targetName);
            lock (m_lock)
            {
                RuntimeTarget runtimeTarget;
                if (!m_targets.TryGetValue(targetName, out runtimeTarget))
                {
                    return "ERROR Target was not found: " + targetName;
                }

                bool removed = m_server.RemoveTarget(targetName);
                if (!removed)
                {
                    return "ERROR Target is in use and cannot be removed: " + targetName;
                }

                LockUtils.ReleaseDisks(runtimeTarget.Disks);
                m_targets.Remove(targetName);
                RemoveTargetConfiguration(targetName);
                if (save)
                {
                    m_configuration.Save(m_configPath);
                }
                return "OK REMOVED target=" + targetName;
            }
        }

        private string ListTargets()
        {
            StringBuilder builder = new StringBuilder();
            lock (m_lock)
            {
                builder.Append("OK TARGETS " + m_targets.Count);
                foreach (RuntimeTarget runtimeTarget in m_targets.Values)
                {
                    builder.Append(" | ");
                    builder.Append(runtimeTarget.Configuration.TargetName);
                    foreach (DiskConfiguration disk in runtimeTarget.Configuration.Disks)
                    {
                        if (disk.Type == DiskConfiguration.TypeDiskImage)
                        {
                            builder.Append(" disk=\"");
                            builder.Append(disk.Path);
                            builder.Append("\"");
                        }
                        else if (disk.Type == DiskConfiguration.TypePhysicalDisk)
                        {
                            builder.Append(" physicalDisk=");
                            builder.Append(disk.PhysicalDiskIndex);
                        }
                        else if (disk.Type == DiskConfiguration.TypeVolume)
                        {
                            builder.Append(" volume=");
                            builder.Append(disk.VolumeGuid);
                        }
                    }
                }
            }
            return builder.ToString().Replace(Environment.NewLine, String.Empty).TrimEnd();
        }

        private string Save()
        {
            lock (m_lock)
            {
                m_configuration.Save(m_configPath);
            }
            return "OK SAVED config=\"" + m_configPath + "\"";
        }

        private bool ContainsTargetConfiguration(string targetName)
        {
            foreach (TargetConfiguration targetConfiguration in m_configuration.Targets)
            {
                if (String.Equals(targetConfiguration.TargetName, targetName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void RemoveTargetConfiguration(string targetName)
        {
            for (int index = 0; index < m_configuration.Targets.Count; index++)
            {
                if (String.Equals(m_configuration.Targets[index].TargetName, targetName, StringComparison.InvariantCultureIgnoreCase))
                {
                    m_configuration.Targets.RemoveAt(index);
                    index--;
                }
            }
        }

        private void PipeServerLoop()
        {
            while (!m_stopping)
            {
                try
                {
                    using (NamedPipeServerStream pipe = new NamedPipeServerStream(m_pipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte))
                    {
                        pipe.WaitForConnection();
                        using (StreamReader reader = new StreamReader(pipe, Encoding.UTF8))
                        using (StreamWriter writer = new StreamWriter(pipe, Encoding.UTF8))
                        {
                            writer.AutoFlush = true;
                            string command = reader.ReadLine();
                            string response = HandlePipeCommand(command);
                            writer.WriteLine(response);
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("WARNING: Management pipe error: " + ex.Message);
                }
            }
        }

        private string HandlePipeCommand(string command)
        {
            try
            {
                if (String.IsNullOrEmpty(command))
                {
                    return "ERROR Empty management command";
                }

                string[] parts = command.Split('|');
                string verb = parts[0].ToUpperInvariant();
                if (verb == "ADD")
                {
                    if (parts.Length < 6)
                    {
                        return "ERROR Invalid ADD command";
                    }

                    string targetName = Decode(parts[1]);
                    string diskPath = Decode(parts[2]);
                    bool readOnly = parts[3] == "1";
                    int cacheSizeMB = Convert.ToInt32(parts[4]);
                    bool save = parts[5] == "1";
                    TargetConfiguration targetConfiguration = new TargetConfiguration();
                    targetConfiguration.TargetName = HeadlessServer.BuildTargetName(targetName);
                    targetConfiguration.Disks.Add(DiskConfiguration.CreateDiskImage(diskPath, readOnly, cacheSizeMB));
                    return AddTarget(targetConfiguration, save);
                }
                if (verb == "REMOVE")
                {
                    if (parts.Length < 3)
                    {
                        return "ERROR Invalid REMOVE command";
                    }
                    return RemoveTarget(Decode(parts[1]), parts[2] == "1");
                }
                if (verb == "LIST")
                {
                    return ListTargets();
                }
                if (verb == "SAVE")
                {
                    return Save();
                }
                if (verb == "STOP")
                {
                    m_stopRequested.Set();
                    m_stopping = true;
                    QueueServerStop();
                    return "OK STOPPING";
                }

                return "ERROR Unknown management command: " + verb;
            }
            catch (Exception ex)
            {
                return "ERROR " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private void WakePipeServer()
        {
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", m_pipeName, PipeDirection.InOut))
                {
                    pipe.Connect(250);
                    using (StreamWriter writer = new StreamWriter(pipe, Encoding.UTF8))
                    {
                        writer.WriteLine("LIST");
                    }
                }
            }
            catch
            {
            }
        }

        private void QueueServerStop()
        {
            bool shouldQueue = false;
            lock (m_lock)
            {
                if (!m_serverStopQueued)
                {
                    m_serverStopQueued = true;
                    shouldQueue = true;
                }
            }

            if (!shouldQueue)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    m_server.Stop();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("WARNING: Failed to stop iSCSI server: " + ex.Message);
                }
            });
        }

        public static string SendManagementCommand(string pipeName, string command)
        {
            using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
            {
                pipe.Connect(PipeConnectTimeoutMilliseconds);
                using (StreamWriter writer = new StreamWriter(pipe, Encoding.UTF8))
                using (StreamReader reader = new StreamReader(pipe, Encoding.UTF8))
                {
                    writer.AutoFlush = true;
                    writer.WriteLine(command);
                    string response = reader.ReadLine();
                    if (response == null)
                    {
                        throw new IOException("No response was received from the running service.");
                    }
                    return response;
                }
            }
        }

        public static string BuildAddCommand(string targetName, string diskPath, bool readOnly, int cacheSizeMB, bool save)
        {
            return "ADD|" + Encode(targetName) + "|" + Encode(diskPath) + "|" + (readOnly ? "1" : "0") + "|" + cacheSizeMB + "|" + (save ? "1" : "0");
        }

        public static string BuildRemoveCommand(string targetName, bool save)
        {
            return "REMOVE|" + Encode(targetName) + "|" + (save ? "1" : "0");
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? String.Empty));
        }

        private static string Decode(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }

        private static string GetStableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                string normalized = (value ?? String.Empty).ToUpperInvariant();
                for (int index = 0; index < normalized.Length; index++)
                {
                    hash ^= normalized[index];
                    hash *= 16777619;
                }
                return hash.ToString("X8");
            }
        }
    }
}
#endif
