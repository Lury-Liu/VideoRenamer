namespace VideoMaterialRenamer
{
    // 一次命名计算所需的全部设置快照。原先构建器调用方在 5 个分部文件里
    // 分散地直接读控件（numEpisode.Value、chkKeepExtension.Checked……）；
    // 快照化后：读取集中在窗体 ReadNamingSettings() 一处，纯逻辑层
    // 不再认识任何控件。
    public struct NamingSettings
    {
        public int Episode;
        public int DefaultScene;
        public bool KeepExtensionCase;
        public bool Export1080p;
        public bool ExportWatermark;
        public bool UseRowScene;
    }
}
