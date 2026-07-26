

namespace VideoRenamer
{
    public class RenamePlan
    {
        public ShotRow Row;
        public int RowIndex;
        public string ColumnName;
        public bool IsMain;
        public int FileIndex;
        public int Scene;
        public int Shot;
        public string ShotLabel;
        public int Take;
        public string TailSegment;
        public string CustomTailText;
        public bool HasCustomTail;
        public string OldPath;
        public string TargetPath;
        public string OldName;
        public string NewName;
        public PlanStatus Status;

        // 浅克隆：Row 引用必须共享（撤销/导出进度按引用身份映射，测试锁定）。
        public RenamePlan Clone()
        {
            return new RenamePlan
            {
                Row = Row,
                RowIndex = RowIndex,
                ColumnName = ColumnName,
                IsMain = IsMain,
                FileIndex = FileIndex,
                Scene = Scene,
                Shot = Shot,
                ShotLabel = ShotLabel,
                Take = Take,
                TailSegment = TailSegment,
                CustomTailText = CustomTailText,
                HasCustomTail = HasCustomTail,
                OldPath = OldPath,
                TargetPath = TargetPath,
                OldName = OldName,
                NewName = NewName,
                Status = Status
            };
        }
    }
}
