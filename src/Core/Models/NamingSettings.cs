using System;

namespace VideoRenamer
{
    // Snapshot of all naming options needed by the pure plan builder.
    public struct NamingSettings
    {
        public int Episode;
        public int DefaultScene;
        public bool KeepExtensionCase;
        public bool Export1080p;
        public bool ExportWatermark;
        public bool UseRowScene;
        public string OutputDirectory;
        public System.Collections.Generic.HashSet<string> ComparisonFileNames;
        public bool AutoResolveConflicts;
    }
}
