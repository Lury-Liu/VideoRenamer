using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VideoRenamer
{
    // rename_history.tsv 的编解码与读写（撤销功能的持久化层）。
    // 冻结契约：首行标记 "VideoRenamerHistoryV1"，每行 5 个
    // 制表符分隔字段（RowIndex、IsMain、FileIndex、Base64 原路径、
    // Base64 新路径），UTF-8。已部署用户的历史文件必须继续可读。
    public static class RenameHistoryStore
    {
        public const string HeaderLine = "VideoRenamerHistoryV1";

        internal static string EncodeValue(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        internal static string DecodeValue(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
        }

        public static void Save(string path, List<RenameOperation> operations)
        {
            if (operations == null || operations.Count == 0)
            {
                return;
            }

            List<string> lines = new List<string>();
            lines.Add(HeaderLine);
            foreach (RenameOperation op in operations)
            {
                lines.Add(string.Join("\t", new string[]
                {
                    op.RowIndex.ToString(),
                    op.IsMain ? "1" : "0",
                    op.FileIndex.ToString(),
                    EncodeValue(op.OriginalPath),
                    EncodeValue(op.RenamedPath)
                }));
            }

            File.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
        }

        // 返回的记录 Row 为 null——把 RowIndex 绑定回内存中的行列表是调用方
        // （窗体）的职责，存储层不认识 UI 的行模型状态。
        public static List<RenameOperation> Load(string path)
        {
            List<RenameOperation> operations = new List<RenameOperation>();
            if (!File.Exists(path))
            {
                return operations;
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0 || lines[0] != HeaderLine)
            {
                return operations;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');
                if (parts.Length != 5)
                {
                    continue;
                }

                int rowIndex;
                int fileIndex;
                if (!int.TryParse(parts[0], out rowIndex) || !int.TryParse(parts[2], out fileIndex))
                {
                    continue;
                }

                operations.Add(new RenameOperation
                {
                    Row = null,
                    RowIndex = rowIndex,
                    IsMain = parts[1] == "1",
                    FileIndex = fileIndex,
                    OriginalPath = DecodeValue(parts[3]),
                    RenamedPath = DecodeValue(parts[4])
                });
            }

            return operations;
        }
    }
}
