using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DiskAccessLibrary;

namespace ISCSIConsole
{
    public class CachedDisk : Disk, SCSI.IFlushableDisk
    {
        public const int DefaultCacheSizeMB = 256;
        public const int DefaultBlockSizeKB = 64;
        public const int DefaultReadAheadBlocks = 8;

        private class CacheEntry
        {
            public long BlockIndex;
            public CacheGroup Group;
            public int Offset;
            public int Length;
            public LinkedListNode<long> Node;
        }

        private class CacheGroup
        {
            public byte[] Data;
            public int ReferenceCount;
        }

        private readonly Disk m_innerDisk;
        private readonly int m_blockSectorCount;
        private readonly long m_maxBytes;
        private readonly int m_cacheSizeMB;
        private readonly object m_lock = new object();
        private readonly Dictionary<long, CacheEntry> m_entries = new Dictionary<long, CacheEntry>();
        private readonly LinkedList<long> m_lru = new LinkedList<long>();
        private long m_cachedBytes;
        private long m_readCommands;
        private long m_blocksRequested;
        private long m_cacheHits;
        private long m_cacheMisses;
        private long m_evictions;
        private long m_writeInvalidations;
        private long m_readAheadBlocks;

        public CachedDisk(Disk innerDisk, int cacheSizeMB)
        {
            if (innerDisk == null)
            {
                throw new ArgumentNullException("innerDisk");
            }
            if (cacheSizeMB < 0)
            {
                throw new ArgumentOutOfRangeException("cacheSizeMB");
            }

            m_innerDisk = innerDisk;
            m_cacheSizeMB = cacheSizeMB;
            m_maxBytes = (long)cacheSizeMB * 1024 * 1024;
            m_blockSectorCount = Math.Max(1, (DefaultBlockSizeKB * 1024) / innerDisk.BytesPerSector);
        }

        public override byte[] ReadSectors(long sectorIndex, int sectorCount)
        {
            int byteCount = CheckDiskBoundaries(sectorIndex, sectorCount);
            if (m_maxBytes == 0)
            {
                return m_innerDisk.ReadSectors(sectorIndex, sectorCount);
            }

            byte[] result = new byte[byteCount];
            int resultOffset = 0;
            long currentSector = sectorIndex;
            int remainingSectors = sectorCount;

            lock (m_lock)
            {
                m_readCommands++;
                while (remainingSectors > 0)
                {
                    long blockIndex = currentSector / m_blockSectorCount;
                    int offsetSectors = (int)(currentSector - (blockIndex * m_blockSectorCount));
                    int sectorsToCopy = Math.Min(remainingSectors, m_blockSectorCount - offsetSectors);
                    int readAheadBlocks = 1;
                    if (remainingSectors > sectorsToCopy)
                    {
                        int blocksRemaining = (remainingSectors - sectorsToCopy + m_blockSectorCount - 1) / m_blockSectorCount;
                        readAheadBlocks = Math.Min(DefaultReadAheadBlocks, blocksRemaining + 1);
                    }
                    CacheEntry entry = GetOrReadBlock(blockIndex, readAheadBlocks);
                    sectorsToCopy = Math.Min(sectorsToCopy, (entry.Length / BytesPerSector) - offsetSectors);
                    if (sectorsToCopy <= 0)
                    {
                        throw new EndOfStreamException("Cached disk read reached the end of the disk image.");
                    }

                    Buffer.BlockCopy(
                        entry.Group.Data,
                        entry.Offset + (offsetSectors * BytesPerSector),
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
            CheckDiskBoundaries(sectorIndex, sectorCount);

            lock (m_lock)
            {
                InvalidateRange(sectorIndex, sectorCount);
                try
                {
                    m_innerDisk.WriteSectors(sectorIndex, data);
                }
                finally
                {
                    InvalidateRange(sectorIndex, sectorCount);
                }
            }
        }

        public void Flush()
        {
            lock (m_lock)
            {
                SCSI.IFlushableDisk flushableDisk = m_innerDisk as SCSI.IFlushableDisk;
                if (flushableDisk != null)
                {
                    flushableDisk.Flush();
                }
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

        public int CacheSizeMB
        {
            get
            {
                return m_cacheSizeMB;
            }
        }

        public string GetStatistics()
        {
            lock (m_lock)
            {
                double hitRate = m_blocksRequested > 0 ? (m_cacheHits * 100.0) / m_blocksRequested : 0;
                double cachedMB = m_cachedBytes / 1024.0 / 1024.0;
                return String.Format(
                    CultureInfo.InvariantCulture,
                    "cacheMB={0} blockKB={1} readAheadBlocks={2} prefetchedBlocks={3} readCommands={4} blocks={5} hits={6} misses={7} hitRate={8:0.0}% cachedMB={9:0.0} evictions={10} writeInvalidations={11}",
                    m_cacheSizeMB,
                    DefaultBlockSizeKB,
                    DefaultReadAheadBlocks,
                    m_readAheadBlocks,
                    m_readCommands,
                    m_blocksRequested,
                    m_cacheHits,
                    m_cacheMisses,
                    hitRate,
                    cachedMB,
                    m_evictions,
                    m_writeInvalidations);
            }
        }

        private CacheEntry GetOrReadBlock(long blockIndex, int requestedReadAheadBlocks)
        {
            m_blocksRequested++;
            CacheEntry entry;
            if (m_entries.TryGetValue(blockIndex, out entry))
            {
                m_cacheHits++;
                Touch(entry);
                return entry;
            }

            m_cacheMisses++;
            long blockStartSector = blockIndex * m_blockSectorCount;
            long sectorsInDisk = Size / BytesPerSector;
            if (blockStartSector < 0 || blockStartSector >= sectorsInDisk)
            {
                throw new EndOfStreamException("Cached disk block is outside the disk image.");
            }

            // A cache block remains 64KB so small random reads do not become
            // expensive.  For a larger sequential SCSI read, combine adjacent
            // misses into one inner-disk read.  This is especially useful for
            // differencing VHDX images, where each inner read repeats the
            // parent BAT/metadata lookup.
            int maxCacheBlocks = (int)Math.Max(1, Math.Min(
                (long)Int32.MaxValue,
                m_maxBytes / ((long)m_blockSectorCount * BytesPerSector)));
            int blocksToRead = Math.Max(1, Math.Min(DefaultReadAheadBlocks, requestedReadAheadBlocks));
            blocksToRead = Math.Min(blocksToRead, maxCacheBlocks);
            for (int candidate = 1; candidate < blocksToRead; candidate++)
            {
                if (m_entries.ContainsKey(blockIndex + candidate))
                {
                    // Stop before the first already cached block.  The range
                    // must never include an existing entry because it is
                    // inserted as a group below.
                    blocksToRead = candidate;
                    break;
                }
            }

            long availableSectors = sectorsInDisk - blockStartSector;
            int sectorsToRead = (int)Math.Min(
                availableSectors,
                (long)m_blockSectorCount * blocksToRead);
            if (sectorsToRead <= 0)
            {
                throw new EndOfStreamException("Cached disk block is outside the disk image.");
            }

            byte[] data = m_innerDisk.ReadSectors(blockStartSector, sectorsToRead);
            if (data == null || data.Length != sectorsToRead * BytesPerSector)
            {
                throw new EndOfStreamException("The disk image returned an incomplete cache block.");
            }
            CacheGroup group = new CacheGroup();
            group.Data = data;
            group.ReferenceCount = 0;
            m_cachedBytes += data.Length;
            int dataOffset = 0;
            CacheEntry firstEntry = null;
            long currentBlockIndex = blockIndex;
            int remainingBlockSectors = sectorsToRead;
            while (remainingBlockSectors > 0)
            {
                int blockSectors = Math.Min(m_blockSectorCount, remainingBlockSectors);
                int blockBytes = blockSectors * BytesPerSector;

                CacheEntry newEntry = new CacheEntry();
                newEntry.BlockIndex = currentBlockIndex;
                newEntry.Group = group;
                newEntry.Offset = dataOffset;
                newEntry.Length = blockBytes;
                newEntry.Node = m_lru.AddFirst(currentBlockIndex);
                m_entries.Add(currentBlockIndex, newEntry);
                group.ReferenceCount++;
                if (firstEntry == null)
                {
                    firstEntry = newEntry;
                }
                else
                {
                    m_readAheadBlocks++;
                }

                dataOffset += blockBytes;
                remainingBlockSectors -= blockSectors;
                currentBlockIndex++;
            }

            // Publish and account the whole buffer before eviction, then keep
            // the block requested by the caller as the most recently used one.
            Touch(firstEntry);
            TrimCache();
            return firstEntry;
        }

        private int CheckDiskBoundaries(long sectorIndex, int sectorCount)
        {
            if (sectorIndex < 0)
            {
                throw new ArgumentOutOfRangeException("sectorIndex");
            }
            if (sectorCount < 0)
            {
                throw new ArgumentOutOfRangeException("sectorCount");
            }

            long byteOffset = checked(sectorIndex * (long)BytesPerSector);
            long byteCount = checked(sectorCount * (long)BytesPerSector);
            if (byteOffset + byteCount < byteOffset || byteOffset + byteCount > Size)
            {
                throw new ArgumentOutOfRangeException("sectorCount", "The requested sectors are outside the disk.");
            }
            if (byteCount > Int32.MaxValue)
            {
                throw new ArgumentOutOfRangeException("sectorCount", "The requested sector count is too large.");
            }

            return (int)byteCount;
        }

        private void Touch(CacheEntry entry)
        {
            m_lru.Remove(entry.Node);
            m_lru.AddFirst(entry.Node);
        }

        private void InvalidateRange(long sectorIndex, int sectorCount)
        {
            if (sectorCount <= 0)
            {
                return;
            }

            long firstBlock = sectorIndex / m_blockSectorCount;
            long lastBlock = (sectorIndex + sectorCount - 1) / m_blockSectorCount;
            for (long blockIndex = firstBlock; blockIndex <= lastBlock; blockIndex++)
            {
                if (RemoveBlock(blockIndex, false))
                {
                    m_writeInvalidations++;
                }
            }
        }

        private void TrimCache()
        {
            while (m_cachedBytes > m_maxBytes && m_lru.Count > 0)
            {
                RemoveBlock(m_lru.Last.Value, true);
            }
        }

        private bool RemoveBlock(long blockIndex, bool evicted)
        {
            CacheEntry entry;
            if (m_entries.TryGetValue(blockIndex, out entry))
            {
                m_lru.Remove(entry.Node);
                m_entries.Remove(blockIndex);
                entry.Group.ReferenceCount--;
                if (entry.Group.ReferenceCount == 0)
                {
                    m_cachedBytes -= entry.Group.Data.Length;
                }
                if (evicted)
                {
                    m_evictions++;
                }
                return true;
            }

            return false;
        }
    }
}
