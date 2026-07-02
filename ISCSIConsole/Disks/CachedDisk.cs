using System;
using System.Collections.Generic;
using System.IO;
using DiskAccessLibrary;

namespace ISCSIConsole
{
    public class CachedDisk : Disk
    {
        public const int DefaultCacheSizeMB = 32;
        public const int DefaultBlockSizeKB = 64;

        private class CacheEntry
        {
            public long BlockIndex;
            public byte[] Data;
            public LinkedListNode<long> Node;
        }

        private readonly Disk m_innerDisk;
        private readonly int m_blockSectorCount;
        private readonly long m_maxBytes;
        private readonly object m_lock = new object();
        private readonly Dictionary<long, CacheEntry> m_entries = new Dictionary<long, CacheEntry>();
        private readonly LinkedList<long> m_lru = new LinkedList<long>();
        private long m_cachedBytes;

        public CachedDisk(Disk innerDisk, int cacheSizeMB)
        {
            if (innerDisk == null)
            {
                throw new ArgumentNullException("innerDisk");
            }
            if (cacheSizeMB <= 0)
            {
                cacheSizeMB = DefaultCacheSizeMB;
            }

            m_innerDisk = innerDisk;
            m_maxBytes = (long)cacheSizeMB * 1024 * 1024;
            m_blockSectorCount = Math.Max(1, (DefaultBlockSizeKB * 1024) / innerDisk.BytesPerSector);
        }

        public override byte[] ReadSectors(long sectorIndex, int sectorCount)
        {
            CheckBoundaries(sectorIndex, sectorCount);

            byte[] result = new byte[sectorCount * BytesPerSector];
            int resultOffset = 0;
            long currentSector = sectorIndex;
            int remainingSectors = sectorCount;

            lock (m_lock)
            {
                while (remainingSectors > 0)
                {
                    long blockIndex = currentSector / m_blockSectorCount;
                    int offsetSectors = (int)(currentSector - (blockIndex * m_blockSectorCount));
                    int sectorsToCopy = Math.Min(remainingSectors, m_blockSectorCount - offsetSectors);
                    CacheEntry entry = GetOrReadBlock(blockIndex);
                    sectorsToCopy = Math.Min(sectorsToCopy, (entry.Data.Length / BytesPerSector) - offsetSectors);
                    if (sectorsToCopy <= 0)
                    {
                        throw new EndOfStreamException("Cached disk read reached the end of the disk image.");
                    }

                    Buffer.BlockCopy(
                        entry.Data,
                        offsetSectors * BytesPerSector,
                        result,
                        resultOffset,
                        sectorsToCopy * BytesPerSector);

                    currentSector += sectorsToCopy;
                    remainingSectors -= sectorsToCopy;
                    resultOffset += sectorsToCopy * BytesPerSector;
                }
            }

            return result;
        }

        public override void WriteSectors(long sectorIndex, byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }
            if (data.Length % BytesPerSector != 0)
            {
                throw new ArgumentException("The data length must be a multiple of the sector size", "data");
            }

            int sectorCount = data.Length / BytesPerSector;
            CheckBoundaries(sectorIndex, sectorCount);

            lock (m_lock)
            {
                m_innerDisk.WriteSectors(sectorIndex, data);
                InvalidateRange(sectorIndex, sectorCount);
            }
        }

        public override int BytesPerSector
        {
            get
            {
                return m_innerDisk.BytesPerSector;
            }
        }

        public override long Size
        {
            get
            {
                return m_innerDisk.Size;
            }
        }

        public override bool IsReadOnly
        {
            get
            {
                return m_innerDisk.IsReadOnly;
            }
        }

        public Disk InnerDisk
        {
            get
            {
                return m_innerDisk;
            }
        }

        private CacheEntry GetOrReadBlock(long blockIndex)
        {
            CacheEntry entry;
            if (m_entries.TryGetValue(blockIndex, out entry))
            {
                Touch(entry);
                return entry;
            }

            long blockStartSector = blockIndex * m_blockSectorCount;
            long sectorsInDisk = Size / BytesPerSector;
            int sectorsToRead = (int)Math.Min(m_blockSectorCount, sectorsInDisk - blockStartSector);
            if (sectorsToRead <= 0)
            {
                throw new EndOfStreamException("Cached disk block is outside the disk image.");
            }
            byte[] data = m_innerDisk.ReadSectors(blockStartSector, sectorsToRead);

            entry = new CacheEntry();
            entry.BlockIndex = blockIndex;
            entry.Data = data;
            entry.Node = m_lru.AddFirst(blockIndex);
            m_entries.Add(blockIndex, entry);
            m_cachedBytes += data.Length;
            TrimCache();
            return entry;
        }

        private void Touch(CacheEntry entry)
        {
            m_lru.Remove(entry.Node);
            entry.Node = m_lru.AddFirst(entry.BlockIndex);
        }

        private void InvalidateRange(long sectorIndex, int sectorCount)
        {
            long firstBlock = sectorIndex / m_blockSectorCount;
            long lastBlock = (sectorIndex + sectorCount - 1) / m_blockSectorCount;
            for (long blockIndex = firstBlock; blockIndex <= lastBlock; blockIndex++)
            {
                RemoveBlock(blockIndex);
            }
        }

        private void TrimCache()
        {
            while (m_cachedBytes > m_maxBytes && m_lru.Count > 0)
            {
                RemoveBlock(m_lru.Last.Value);
            }
        }

        private void RemoveBlock(long blockIndex)
        {
            CacheEntry entry;
            if (m_entries.TryGetValue(blockIndex, out entry))
            {
                m_lru.Remove(entry.Node);
                m_entries.Remove(blockIndex);
                m_cachedBytes -= entry.Data.Length;
            }
        }
    }
}
