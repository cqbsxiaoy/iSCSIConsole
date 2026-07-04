using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;

namespace ISCSIConsole
{
    public class ServiceConfiguration
    {
        public const string DefaultFileName = "iSCSIConsole.service.xml";

        public ServiceConfiguration()
        {
            ListenAddress = "0.0.0.0";
            Port = 3260;
            Targets = new List<TargetConfiguration>();
        }

        public string ListenAddress { get; set; }

        public int Port { get; set; }

        [XmlArrayItem("Target")]
        public List<TargetConfiguration> Targets { get; set; }

        public static string GetDefaultPath()
        {
            Assembly entryAssembly = Assembly.GetEntryAssembly();
            string assemblyLocation = entryAssembly != null ? entryAssembly.Location : typeof(ServiceConfiguration).Assembly.Location;
            string directory = Path.GetDirectoryName(assemblyLocation);
            if (String.IsNullOrEmpty(directory))
            {
                directory = AppDomain.CurrentDomain.BaseDirectory;
            }
            return Path.Combine(directory, DefaultFileName);
        }

        public static ServiceConfiguration Load(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ServiceConfiguration));
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ServiceConfiguration configuration = (ServiceConfiguration)serializer.Deserialize(stream);
                configuration.Normalize();
                return configuration;
            }
        }

        public void Save(string path)
        {
            Normalize();
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            XmlSerializer serializer = new XmlSerializer(typeof(ServiceConfiguration));
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.Serialize(stream, this);
            }
        }

        public void Normalize()
        {
            if (String.IsNullOrEmpty(ListenAddress))
            {
                ListenAddress = "0.0.0.0";
            }
            if (Port == 0)
            {
                Port = 3260;
            }
            if (Targets == null)
            {
                Targets = new List<TargetConfiguration>();
            }
            foreach (TargetConfiguration target in Targets)
            {
                if (target != null)
                {
                    target.Normalize();
                }
            }
        }
    }

    public class TargetConfiguration
    {
        public TargetConfiguration()
        {
            Disks = new List<DiskConfiguration>();
        }

        [XmlAttribute]
        public string TargetName { get; set; }

        [XmlArrayItem("Disk")]
        public List<DiskConfiguration> Disks { get; set; }

        public void Normalize()
        {
            if (Disks == null)
            {
                Disks = new List<DiskConfiguration>();
            }
            foreach (DiskConfiguration disk in Disks)
            {
                if (disk != null)
                {
                    disk.Normalize();
                }
            }
        }
    }

    public class DiskConfiguration
    {
        public const string TypeDiskImage = "DiskImage";
        public const string TypePhysicalDisk = "PhysicalDisk";
        public const string TypeVolume = "Volume";

        public DiskConfiguration()
        {
            CacheSizeMB = CachedDisk.DefaultCacheSizeMB;
        }

        [XmlAttribute]
        public string Type { get; set; }

        [XmlAttribute]
        public bool ReadOnly { get; set; }

        [XmlAttribute]
        public int CacheSizeMB { get; set; }

        public string Path { get; set; }

        public int PhysicalDiskIndex { get; set; }

        public string VolumeGuid { get; set; }

        public void Normalize()
        {
            if (String.IsNullOrEmpty(Type))
            {
                return;
            }

            if (!Type.Equals(TypeDiskImage, StringComparison.InvariantCultureIgnoreCase))
            {
                CacheSizeMB = 0;
                return;
            }

            if (CacheSizeMB < 0)
            {
                CacheSizeMB = 0;
            }
        }

        public static DiskConfiguration CreateDiskImage(string path, bool readOnly)
        {
            return CreateDiskImage(path, readOnly, CachedDisk.DefaultCacheSizeMB);
        }

        public static DiskConfiguration CreateDiskImage(string path, bool readOnly, int cacheSizeMB)
        {
            return new DiskConfiguration()
            {
                Type = TypeDiskImage,
                Path = path,
                ReadOnly = readOnly,
                CacheSizeMB = cacheSizeMB
            };
        }

        public static DiskConfiguration CreatePhysicalDisk(int physicalDiskIndex, bool readOnly)
        {
            return new DiskConfiguration()
            {
                Type = TypePhysicalDisk,
                PhysicalDiskIndex = physicalDiskIndex,
                ReadOnly = readOnly,
                CacheSizeMB = 0
            };
        }

        public static DiskConfiguration CreateVolume(Guid volumeGuid, bool readOnly)
        {
            return new DiskConfiguration()
            {
                Type = TypeVolume,
                VolumeGuid = volumeGuid.ToString("D"),
                ReadOnly = readOnly,
                CacheSizeMB = 0
            };
        }
    }
}
