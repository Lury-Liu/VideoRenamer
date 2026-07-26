using System;
using System.Collections.Generic;
using System.IO;

namespace VideoRenamer.Tests
{
    // Golden-master corpus: a fixed fixture shot table whose complete plan output
    // (column, indices, scene/shot labels, take, tail, new name, status) was
    // captured from the shipped V1.0.6.0 behavior. Every later phase - refactor
    // or performance work alike - must keep this output byte-identical. Only a
    // commit that deliberately changes naming behavior may update these goldens.
    public static class GoldenMasterTests
    {
        private static readonly string[] CorpusAGolden =
        {
            "主要素材|True|0|1|1|1|1|T1|E2-S1-1-T1.mp4|就绪",
            "主要素材|True|1|1|1|1|2|T2|E2-S1-1-T2.mp4|就绪",
            "备用素材|False|0|1|1|1|3|T3|E2-S1-1-T3.mov|就绪",
            "主要素材|True|0|1|28|28A|1|补|E2-S1-28A-补.mp4|就绪",
            "主要素材|True|0|1|1|1|1|T1|E2-S1-1-T1.mp4|新文件名重复",
            "主要素材|True|0|1|99|99|1|T1|E2-S1-99-T1.mp4|源文件丢失",
            "主要素材|True|0|1|50|50|1|T1|E2-S1-50-T1.mp4|未变化",
            "主要素材|True|0|1|60|60|1|T1|E2-S1-60-T1.mp4|目标已存在"
        };

        private static readonly string[] CorpusBGolden =
        {
            "主要素材|True|0|5|1|1|1|T1|E2-S5-1-T1.MP4|待覆盖导出1080p",
            "主要素材|True|1|5|1|1|2|T2|E2-S5-1-T2.mp4|待覆盖导出1080p",
            "备用素材|False|0|5|1|1|3|T3|E2-S5-1-T3.mov|待覆盖导出1080p",
            "主要素材|True|0|1|28|28A|1|补|E2-S1-28A-补.mp4|待覆盖导出1080p",
            "主要素材|True|0|2|1|1|1|T1|E2-S2-1-T1.mp4|待覆盖导出1080p",
            "主要素材|True|0|1|99|99|1|T1|E2-S1-99-T1.mp4|源文件丢失",
            "主要素材|True|0|1|50|50|1|T1|E2-S1-50-T1.mp4|待覆盖导出1080p",
            "主要素材|True|0|1|60|60|1|T1|E2-S1-60-T1.mp4|目标已存在"
        };

        public static List<TestCase> Cases()
        {
            List<TestCase> cases = new List<TestCase>();
            cases.Add(new TestCase("golden_master_corpus_a_plain", GoldenMasterCorpusAPlain));
            cases.Add(new TestCase("golden_master_corpus_b_export_row_scene", GoldenMasterCorpusBExportRowScene));
            return cases;
        }

        private static ShotRow BuildRow(string tempDir, int scene, int sequence, string suffix, string[] mainFiles, string[] backupFiles, string[] mainTails)
        {
            ShotRow row = new ShotRow { Scene = scene, Sequence = sequence, ShotSuffix = suffix };
            foreach (string name in mainFiles)
            {
                row.MainFiles.Add(Path.Combine(tempDir, name));
            }
            foreach (string name in backupFiles)
            {
                row.BackupFiles.Add(Path.Combine(tempDir, name));
            }
            foreach (string tail in mainTails)
            {
                row.MainTailOverrides.Add(tail);
            }
            return row;
        }

        private static List<ShotRow> BuildFixtureRows(string tempDir)
        {
            // Fixture files: everything except missing.mp4 exists on disk.
            string[] existing =
            {
                "clipA.MP4", "clipB.mp4", "clipC.mov", "bridge.mp4",
                "dup.mp4", "E2-S1-50-T1.mp4", "src60.mp4", "E2-S1-60-T1.mp4"
            };
            foreach (string name in existing)
            {
                File.WriteAllText(Path.Combine(tempDir, name), "x");
            }

            List<ShotRow> rows = new List<ShotRow>();
            rows.Add(BuildRow(tempDir, 5, 1, "", new[] { "clipA.MP4", "clipB.mp4" }, new[] { "clipC.mov" }, new string[0]));
            rows.Add(BuildRow(tempDir, 0, 28, "a", new[] { "bridge.mp4" }, new string[0], new[] { "补" }));
            rows.Add(BuildRow(tempDir, 2, 1, "", new[] { "dup.mp4" }, new string[0], new string[0]));
            rows.Add(BuildRow(tempDir, 0, 99, "", new[] { "missing.mp4" }, new string[0], new string[0]));
            rows.Add(BuildRow(tempDir, 0, 50, "", new[] { "E2-S1-50-T1.mp4" }, new string[0], new string[0]));
            rows.Add(BuildRow(tempDir, 0, 60, "", new[] { "src60.mp4" }, new string[0], new string[0]));
            return rows;
        }

        private static string SerializePlan(List<RenamePlan> plan)
        {
            List<string> lines = new List<string>();
            foreach (RenamePlan p in plan)
            {
                lines.Add(string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}",
                    p.ColumnName, p.IsMain, p.FileIndex, p.Scene, p.Shot,
                    p.ShotLabel, p.Take, p.TailSegment, p.NewName, PlanStatusText.For(p.Status)));
            }
            return string.Join("\n", lines.ToArray());
        }

        private static void GoldenMasterCorpusAPlain()
        {
            CoreTests.WithTempDir(delegate(string dir)
            {
                List<ShotRow> rows = BuildFixtureRows(dir);
                List<RenamePlan> plan = RenamePlanBuilder.BuildPlan(rows, 2, 1, false, false, false);
                TestAssert.AreEqual(string.Join("\n", CorpusAGolden), SerializePlan(plan),
                    "golden master corpus A (episode=2 scene=1 keepExt=false export=false rowScene=false)");
            });
        }

        private static void GoldenMasterCorpusBExportRowScene()
        {
            CoreTests.WithTempDir(delegate(string dir)
            {
                List<ShotRow> rows = BuildFixtureRows(dir);
                List<RenamePlan> plan = RenamePlanBuilder.BuildPlan(rows, 2, 1, true, true, true);
                TestAssert.AreEqual(string.Join("\n", CorpusBGolden), SerializePlan(plan),
                    "golden master corpus B (episode=2 scene=1 keepExt=true export=true rowScene=true)");
            });
        }
    }
}
