#if !NET20
using System;
using System.IO;
using DiskAccessLibrary;
using DiscUtils.Streams;

namespace ISCSIConsole
{
    public class VhdDiskImage : DiskImage
    {
        private const int VhdBytesPerSector = 512;

        private readonly object m_syncRoot = new object();
        private DiscUtils.Vhd.Disk m_disk;
        private SparseStream m_content;
        private bool m_isReadOnly;

        public VhdDiskImage(string diskImagePath)
            : this(diskImagePath, false)
        {
        }

        public VhdDiskImage(string diskImagePath, bool isReadOnly)
            : base(diskImagePath, isReadOnly)
        {
            m_disk = OpenDisk(diskImagePath, isReadOnly, out m_isReadOnly);
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

        private static DiscUtils.Vhd.Disk OpenDisk(string diskImagePath, bool isReadOnly, out bool actualReadOnly)
        {
            if (isReadOnly)
            {
                actualReadOnly = true;
                return new DiscUtils.Vhd.Disk(diskImagePath, FileAccess.Read);
            }

            try
            {
                actualReadOnly = false;
                return new DiscUtils.Vhd.Disk(diskImagePath, FileAccess.ReadWrite);
            }
            catch (UnauthorizedAccessException)
            {
                actualReadOnly = true;
                return new DiscUtils.Vhd.Disk(diskImagePath, FileAccess.Read);
            }
            catch (IOException)
            {
                actualReadOnly = true;
                return new DiscUtils.Vhd.Disk(diskImagePath, FileAccess.Read);
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
