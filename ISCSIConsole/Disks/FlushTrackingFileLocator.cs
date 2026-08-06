#if !NET20
using System;
using System.Collections.Generic;
using System.IO;
using DiscUtils;

namespace ISCSIConsole
{
    internal sealed class FlushTrackingFileLocator : FileLocator
    {
        private sealed class StreamTracker
        {
            private readonly object m_lock = new object();
            private readonly List<FileStream> m_streams = new List<FileStream>();

            public void Track(FileStream stream)
            {
                if (stream == null)
                {
                    return;
                }

                lock (m_lock)
                {
                    if (!m_streams.Contains(stream))
                    {
                        m_streams.Add(stream);
                    }
                }
            }

            public void Flush()
            {
                lock (m_lock)
                {
                    for (int index = m_streams.Count - 1; index >= 0; index--)
                    {
                        FileStream stream = m_streams[index];
                        try
                        {
                            if (stream.CanWrite)
                            {
                                stream.Flush(true);
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            m_streams.RemoveAt(index);
                        }
                    }
                }
            }

            public void DisposeStreams()
            {
                lock (m_lock)
                {
                    foreach (FileStream stream in m_streams)
                    {
                        try
                        {
                            stream.Dispose();
                        }
                        catch
                        {
                        }
                    }
                    m_streams.Clear();
                }
            }
        }

        private readonly string m_directory;
        private readonly StreamTracker m_tracker;

        public FlushTrackingFileLocator(string directory)
            : this(directory, new StreamTracker())
        {
        }

        private FlushTrackingFileLocator(string directory, StreamTracker tracker)
        {
            if (String.IsNullOrEmpty(directory))
            {
                directory = Directory.GetCurrentDirectory();
            }

            m_directory = Path.GetFullPath(directory);
            m_tracker = tracker;
        }

        public override bool Exists(string fileName)
        {
            return File.Exists(GetFullPath(fileName));
        }

        protected override Stream OpenFile(string fileName, FileMode mode, FileAccess access, FileShare share)
        {
            FileStream stream = new FileStream(GetFullPath(fileName), mode, access, share);
            m_tracker.Track(stream);
            return stream;
        }

        public override FileLocator GetRelativeLocator(string path)
        {
            return new FlushTrackingFileLocator(GetFullPath(path), m_tracker);
        }

        public override string GetFullPath(string path)
        {
            if (String.IsNullOrEmpty(path))
            {
                return m_directory;
            }
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }
            return Path.GetFullPath(Path.Combine(m_directory, path));
        }

        public override string GetDirectoryFromPath(string path)
        {
            return Path.GetDirectoryName(path);
        }

        public override string GetFileFromPath(string path)
        {
            return Path.GetFileName(path);
        }

        public override DateTime GetLastWriteTimeUtc(string path)
        {
            return File.GetLastWriteTimeUtc(GetFullPath(path));
        }

        public override bool HasCommonRoot(FileLocator other)
        {
            if (other == null)
            {
                return false;
            }

            string thisRoot = Path.GetPathRoot(m_directory);
            string otherRoot = Path.GetPathRoot(other.GetFullPath(String.Empty));
            return String.Equals(thisRoot, otherRoot, StringComparison.OrdinalIgnoreCase);
        }

        public override string ResolveRelativePath(string path)
        {
            return GetFullPath(path);
        }

        public void Track(FileStream stream)
        {
            m_tracker.Track(stream);
        }

        public void FlushWritableStreams()
        {
            m_tracker.Flush();
        }

        public void DisposeStreams()
        {
            m_tracker.DisposeStreams();
        }
    }
}
#endif
