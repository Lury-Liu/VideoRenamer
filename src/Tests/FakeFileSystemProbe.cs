using System;
using System.Collections.Generic;

namespace VideoMaterialRenamer.Tests
{
    // Deterministic IFileSystemProbe for plan-status tests - no temp files needed.
    public sealed class FakeFileSystemProbe : IFileSystemProbe
    {
        private readonly HashSet<string> existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FakeFileSystemProbe AddExisting(string path)
        {
            existing.Add(path);
            return this;
        }

        public FakeFileSystemProbe AddLocked(string path)
        {
            existing.Add(path);
            locked.Add(path);
            return this;
        }

        public bool FileExists(string path)
        {
            return path != null && existing.Contains(path);
        }

        public bool IsFileLocked(string path)
        {
            return path != null && locked.Contains(path);
        }
    }
}
