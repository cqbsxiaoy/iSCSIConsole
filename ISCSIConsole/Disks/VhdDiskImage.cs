#if !NET20
using System;
using System.IO;
using System.Reflection;
using DiskAccessLibrary;
using DiscUtils.Streams;

namespace ISCSIConsole
{
    public class VhdDiskImage : DiskImage, SCSI.IFlushableDisk
    {
        private const int VhdBytesPerSector = 512;

        private readonly object m_syncRoot = new object();
        private DiscUtils.Vhd.Disk m_disk;
        private SparseStream m_content;
        private bool m_isReadOnly;
        private FlushTrackingFileLocator m_fileLocator;

        public VhdDiskImage(string diskImagePath)
            : this(diskImagePath, false)
        {
        }

        public VhdDiskImage(string diskImagePath, bool isReadOnly)
            : base(diskImagePath, isReadOnly)
        {
            m_disk = OpenDisk(diskImagePath, isReadOnly, out m_isReadOnly, out m_fileLocator);
            m_content = m_disk.Content;
        }

        public override byte[] ReadSectors(long sectorIndex, int sectorCount)
        {
            CheckBoundaries(sectorIndex, sectorCount);

            byte[] result = new byte[sectorCount * BytesPerSector];
            lock (m_syncRoot)
            {
                m_content.Position = sectorIndex * BytesPerSector;
                ReadExactly(m_content, result, 0, result.Length);
            }
            return result;
        }

        public override void WriteSectors(long sectorIndex, byte[] data)
        {
            if (IsReadOnly)
            {
                throw new UnauthorizedAccessException("The VHD disk image is read-only");
            }
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }
            if (data.Length % BytesPerSector != 0)
            {
                throw new ArgumentException("The data length must be a multiple of the sector size", "data");
            }

            CheckBoundaries(sectorIndex, data.Length / BytesPerSector);

            lock (m_syncRoot)
            {
                m_content.Position = sectorIndex * BytesPerSector;
                m_content.Write(data, 0, data.Length);
            }
        }

        public override void Extend(long numberOfAdditionalBytes)
        {
            throw new NotImplementedException("VHD extension is not supported");
        }

        public void Flush()
        {
            lock (m_syncRoot)
            {
                if (m_content == null || m_isReadOnly)
                {
                    return;
                }

                m_content.Flush();
                m_fileLocator.FlushWritableStreams();
            }
        }

        public override bool ExclusiveLock()
        {
            return true;
        }

        public override bool ExclusiveLock(bool useOverlappedIO)
        {
            return true;
        }

        public override bool ReleaseLock()
        {
            lock (m_syncRoot)
            {
                if (m_content != null)
                {
                    try
                    {
                        m_content.Dispose();
                    }
                    catch (NotImplementedException)
                    {
                    }
                    finally
                    {
                        m_content = null;
                    }
                }
                if (m_disk != null)
                {
                    try
                    {
                        m_disk.Dispose();
                    }
                    catch (NotImplementedException)
                    {
                    }
                    finally
                    {
                        m_disk = null;
                    }
                }
                if (m_fileLocator != null)
                {
                    m_fileLocator.DisposeStreams();
                }
            }
            return true;
        }

        public override int BytesPerSector
        {
            get
            {
                return VhdBytesPerSector;
            }
        }

        public override long Size
        {
            get
            {
                return m_disk.Capacity;
            }
        }

        public override bool IsReadOnly
        {
            get
            {
                return m_isReadOnly;
            }
        }

        private static DiscUtils.Vhd.Disk OpenDisk(string diskImagePath, bool isReadOnly, out bool actualReadOnly, out FlushTrackingFileLocator fileLocator)
        {
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(diskImagePath));
            string fileName = System.IO.Path.GetFileName(diskImagePath);
            if (isReadOnly)
            {
                actualReadOnly = true;
                fileLocator = new FlushTrackingFileLocator(directory);
                return OpenDiskWithLocator(fileLocator, fileName, FileAccess.Read);
            }

            try
            {
                actualReadOnly = false;
                fileLocator = new FlushTrackingFileLocator(directory);
                return OpenDiskWithLocator(fileLocator, fileName, FileAccess.ReadWrite);
            }
            catch (UnauthorizedAccessException)
            {
                actualReadOnly = true;
                fileLocator = new FlushTrackingFileLocator(directory);
                return OpenDiskWithLocator(fileLocator, fileName, FileAccess.Read);
            }
            catch (IOException)
            {
                actualReadOnly = true;
                fileLocator = new FlushTrackingFileLocator(directory);
                return OpenDiskWithLocator(fileLocator, fileName, FileAccess.Read);
            }
        }

        private static DiscUtils.Vhd.Disk OpenDiskWithLocator(DiscUtils.FileLocator fileLocator, string fileName, FileAccess access)
        {
            ConstructorInfo constructor = typeof(DiscUtils.Vhd.Disk).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(DiscUtils.FileLocator), typeof(string), typeof(FileAccess) },
                null);
            if (constructor == null)
            {
                throw new MissingMethodException("DiscUtils VHD FileLocator constructor was not found.");
            }

            try
            {
                return (DiscUtils.Vhd.Disk)constructor.Invoke(new object[] { fileLocator, fileName, access });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int bytesRead = stream.Read(buffer, offset, count);
                if (bytesRead == 0)
                {
                    Array.Clear(buffer, offset, count);
                    return;
                }

                offset += bytesRead;
                count -= bytesRead;
            }
        }
    }
}
#endif
