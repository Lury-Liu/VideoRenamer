using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VideoMaterialRenamer.Tests
{
    // Characterization tests for the naming engine, plan statuses, export plan
    // preparation, and history encoding. These pin CURRENT behavior - including
    // known bugs, which are marked and only flipped in their own fix commits.
    public static class CoreTests
    {
        public static List<TestCase> Cases()
        {
            List<TestCase> cases = new List<TestCase>();
            cases.Add(new TestCase("naming_take_numbering_across_main_backup", NamingTakeNumberingAcrossMainBackup));
            cases.Add(new TestCase("naming_custom_shot_number", NamingCustomShotNumber));
            cases.Add(new TestCase("naming_row_scene_enabled", NamingRowSceneEnabled));
            cases.Add(new TestCase("naming_row_scene_disabled", NamingRowSceneDisabled));
            cases.Add(new TestCase("naming_custom_tail_text", NamingCustomTailText));
            cases.Add(new TestCase("naming_unique_custom_tail_auto_number", NamingUniqueCustomTailAutoNumber));
            cases.Add(new TestCase("naming_append_tail_counter_underscore", NamingAppendTailCounterUnderscore));
            cases.Add(new TestCase("naming_shot_suffix_28A", NamingShotSuffix28A));
            cases.Add(new TestCase("normalize_shot_suffix_rules", NormalizeShotSuffixRules));
            cases.Add(new TestCase("batch_tails_plain_base", BatchTailsPlainBase));
            cases.Add(new TestCase("batch_tails_numbered_base", BatchTailsNumberedBase));
            cases.Add(new TestCase("batch_tails_empty_base", BatchTailsEmptyBase));
            cases.Add(new TestCase("get_effective_scene_rules", GetEffectiveSceneRules));
            cases.Add(new TestCase("material_file_name_extension_lowercase", MaterialFileNameExtensionLowercase));
            cases.Add(new TestCase("material_file_name_empty_tail_fallback", MaterialFileNameEmptyTailFallback));
            cases.Add(new TestCase("material_file_name_clamps_episode_scene", MaterialFileNameClampsEpisodeScene));
            cases.Add(new TestCase("normalize_custom_tail_invalid_chars", NormalizeCustomTailInvalidChars));
            cases.Add(new TestCase("normalize_custom_tail_80_char_truncation", NormalizeCustomTail80CharTruncation));
            cases.Add(new TestCase("status_ready", StatusReady));
            cases.Add(new TestCase("status_unchanged", StatusUnchanged));
            cases.Add(new TestCase("status_target_exists", StatusTargetExists));
            cases.Add(new TestCase("status_duplicate_new_name", StatusDuplicateNewName));
            cases.Add(new TestCase("status_source_missing", StatusSourceMissing));
            cases.Add(new TestCase("status_export_overwrite_pending", StatusExportOverwritePending));
            cases.Add(new TestCase("is_blocking_issue_truth_table", IsBlockingIssueTruthTable));
            cases.Add(new TestCase("build_plan_resizes_tail_overrides", BuildPlanResizesTailOverrides));
            cases.Add(new TestCase("shot_label_pattern_table", ShotLabelPatternTable));
            cases.Add(new TestCase("clone_rename_plan_copies_fields_drops_shot_label", CloneRenamePlanCopiesFieldsDropsShotLabel));
            cases.Add(new TestCase("clone_rename_plan_shares_row_reference", CloneRenamePlanSharesRowReference));
            cases.Add(new TestCase("prepare_export_plan_save_as_renames_unchanged", PrepareExportPlanSaveAsRenamesUnchanged));
            cases.Add(new TestCase("prepare_export_plan_duplicate_target_throws", PrepareExportPlanDuplicateTargetThrows));
            cases.Add(new TestCase("prepare_export_plan_existing_target_throws", PrepareExportPlanExistingTargetThrows));
            cases.Add(new TestCase("history_value_roundtrip", HistoryValueRoundtrip));
            cases.Add(new TestCase("history_encode_golden", HistoryEncodeGolden));
            cases.Add(new TestCase("unique_path_with_suffix_first_candidate", UniquePathWithSuffixFirstCandidate));
            cases.Add(new TestCase("unique_path_with_suffix_counter_and_default", UniquePathWithSuffixCounterAndDefault));
            return cases;
        }

        internal static void WithTempDir(Action<string> body)
        {
            string dir = Path.Combine(Path.GetTempPath(), "VmrTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                body(dir);
            }
            finally
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                }
            }
        }

        private static List<RenamePlan> BuildSingleRowPlan(ShotRow row, int episode, int scene, bool keepExtensionCase, bool export1080p)
        {
            return RenamePlanBuilder.BuildPlan(new List<ShotRow> { row }, episode, scene, keepExtensionCase, export1080p);
        }

        private static void NamingTakeNumberingAcrossMainBackup()
        {
            ShotRow row = new ShotRow { Sequence = 5 };
            row.MainFiles.Add(@"C:\Temp\main1.mp4");
            row.MainFiles.Add(@"C:\Temp\main2.mp4");
            row.MainFiles.Add(@"C:\Temp\main3.mp4");
            row.BackupFiles.Add(@"C:\Temp\backup1.mp4");
            row.BackupFiles.Add(@"C:\Temp\backup2.mp4");
            row.BackupFiles.Add(@"C:\Temp\backup3.mp4");

            List<RenamePlan> plan = BuildSingleRowPlan(row, 5, 1, true, false);
            string actual = string.Join("|", plan.Select(p => p.NewName).ToArray());
            TestAssert.AreEqual(
                "E5-S1-5-T1.mp4|E5-S1-5-T2.mp4|E5-S1-5-T3.mp4|E5-S1-5-T4.mp4|E5-S1-5-T5.mp4|E5-S1-5-T6.mp4",
                actual, "take numbering continues from main into backup");
        }

        private static void NamingCustomShotNumber()
        {
            ShotRow row = new ShotRow { Sequence = 17 };
            row.MainFiles.Add(@"C:\Temp\custom.mp4");
            List<RenamePlan> plan = BuildSingleRowPlan(row, 5, 1, true, false);
            TestAssert.AreEqual(1, plan.Count, "custom shot count");
            TestAssert.AreEqual("E5-S1-17-T1.mp4", plan[0].NewName, "custom shot name");
        }

        private static void NamingRowSceneEnabled()
        {
            ShotRow row = new ShotRow { Scene = 3, Sequence = 7 };
            row.MainFiles.Add(@"C:\Temp\custom_scene.mp4");
            List<RenamePlan> plan = RenamePlanBuilder.BuildPlan(new List<ShotRow> { row }, 5, 1, true, false, true);
            TestAssert.AreEqual("E5-S3-7-T1.mp4", plan[0].NewName, "row scene name");
            TestAssert.AreEqual(3, plan[0].Scene, "row scene value");
        }

        private static void NamingRowSceneDisabled()
        {
            ShotRow row = new ShotRow { Scene = 3, Sequence = 7 };
            row.MainFiles.Add(@"C:\Temp\custom_scene.mp4");
            List<RenamePlan> plan = RenamePlanBuilder.BuildPlan(new List<ShotRow> { row }, 5, 1, true, false, false);
            TestAssert.AreEqual("E5-S1-7-T1.mp4", plan[0].NewName, "default scene name");
        }

        private static void NamingCustomTailText()
        {
            ShotRow row = new ShotRow { Sequence = 1 };
            row.MainFiles.Add(@"C:\Temp\custom_tail.mp4");
            row.MainTailOverrides.Add("补+文字");
            List<RenamePlan> plan = BuildSingleRowPlan(row, 5, 1, true, false);
            TestAssert.AreEqual("E5-S1-1-补+文字.mp4", plan[0].NewName, "custom tail name");
            TestAssert.AreEqual("补+文字", plan[0].TailSegment, "custom tail segment");
        }

        private static void NamingUniqueCustomTailAutoNumber()
        {
            ShotRow row = new ShotRow { Sequence = 5 };
            row.MainFiles.Add(@"C:\Temp\dup1.mp4");
            row.MainFiles.Add(@"C:\Temp\dup2.mp4");
            row.MainTailOverrides.Add("补手机");
            row.MainTailOverrides.Add("");
            List<RenamePlan> plan = RenamePlanBuilder.BuildPlan(new List<ShotRow> { row }, 5, 6, true, false);
            string uniqueTail = RenamePlanBuilder.GetUniqueCustomTail(plan[1], "补手机", plan, 5, 6, true);
            TestAssert.AreEqual("补手机_2", uniqueTail, "duplicate custom tail auto-numbers");
        }

        private static void NamingAppendTailCounterUnderscore()
        {
            TestAssert.AreEqual("TT1_11", RenamePlanBuilder.AppendCustomTailCounter("TT1", 11), "underscore separator");
            TestAssert.AreEqual("补_2", RenamePlanBuilder.AppendCustomTailCounter("补", 1), "counter floor is 2");
        }

        private static void NamingShotSuffix28A()
        {
            ShotRow row = new ShotRow { Sequence = 28, ShotSuffix = "a" };
            row.MainFiles.Add(@"C:\Temp\bridge.mp4");
            List<RenamePlan> plan = BuildSingleRowPlan(row, 1, 2, true, false);
            TestAssert.AreEqual("E1-S2-28A-T1.mp4", plan[0].NewName, "28A name");
            TestAssert.AreEqual("28A", plan[0].ShotLabel, "28A label");
        }

        private static void NormalizeShotSuffixRules()
        {
            TestAssert.AreEqual("B", RenamePlanBuilder.NormalizeShotSuffix(" b# "), "letters only, uppercased");
            TestAssert.AreEqual("AB", RenamePlanBuilder.NormalizeShotSuffix("abc"), "max two letters");
            TestAssert.AreEqual("", RenamePlanBuilder.NormalizeShotSuffix("  "), "whitespace becomes empty");
            TestAssert.AreEqual("", RenamePlanBuilder.NormalizeShotSuffix(null), "null becomes empty");
        }

        private static void BatchTailsPlainBase()
        {
            List<string> tails = RenamePlanBuilder.BuildBatchTails("补", 3);
            TestAssert.AreEqual("补_1|补_2|补_3", string.Join("|", tails.ToArray()), "plain base numbering");
        }

        private static void BatchTailsNumberedBase()
        {
            List<string> tails = RenamePlanBuilder.BuildBatchTails("替换1", 3);
            TestAssert.AreEqual("替换_1|替换_2|替换_3", string.Join("|", tails.ToArray()), "numbered base continues");
        }

        private static void BatchTailsEmptyBase()
        {
            List<string> tails = RenamePlanBuilder.BuildBatchTails("", 2);
            TestAssert.AreEqual("|", string.Join("|", tails.ToArray()), "empty base falls back to default takes");
        }

        private static void GetEffectiveSceneRules()
        {
            ShotRow row = new ShotRow { Scene = 4 };
            TestAssert.AreEqual(4, RenamePlanBuilder.GetEffectiveScene(row, 2, true), "row scene wins when enabled");
            TestAssert.AreEqual(2, RenamePlanBuilder.GetEffectiveScene(row, 2, false), "default scene when disabled");
            TestAssert.AreEqual(2, RenamePlanBuilder.GetEffectiveScene(new ShotRow { Scene = 0 }, 2, true), "zero row scene falls back");
            TestAssert.AreEqual(1, RenamePlanBuilder.GetEffectiveScene(null, 0, false), "scene clamps to 1");
        }

        private static void MaterialFileNameExtensionLowercase()
        {
            TestAssert.AreEqual("E1-S1-1-T1.mp4",
                RenamePlanBuilder.GetMaterialFileName(1, 1, 1, "", "T1", @"C:\Temp\a.MP4", false),
                "extension lowercased when keepExtensionCase=false");
            TestAssert.AreEqual("E1-S1-1-T1.MP4",
                RenamePlanBuilder.GetMaterialFileName(1, 1, 1, "", "T1", @"C:\Temp\a.MP4", true),
                "extension preserved when keepExtensionCase=true");
        }

        private static void MaterialFileNameEmptyTailFallback()
        {
            TestAssert.AreEqual("E1-S1-1-T1.mp4",
                RenamePlanBuilder.GetMaterialFileName(1, 1, 1, "", "", @"C:\Temp\a.mp4", true),
                "empty tail falls back to T1");
        }

        private static void MaterialFileNameClampsEpisodeScene()
        {
            TestAssert.AreEqual("E1-S1-1-T1.mp4",
                RenamePlanBuilder.GetMaterialFileName(0, 0, 0, "", "T1", @"C:\Temp\a.mp4", true),
                "episode/scene/shot clamp to 1");
        }

        private static void NormalizeCustomTailInvalidChars()
        {
            TestAssert.AreEqual("a_b_c", RenamePlanBuilder.NormalizeCustomTailText("a<b>c"), "invalid filename chars replaced");
            TestAssert.AreEqual("x_y", RenamePlanBuilder.NormalizeCustomTailText("x\ty"), "control chars replaced");
            TestAssert.AreEqual("tail", RenamePlanBuilder.NormalizeCustomTailText(" tail. "), "trims spaces and dots");
            TestAssert.AreEqual("", RenamePlanBuilder.NormalizeCustomTailText(null), "null becomes empty");
        }

        private static void NormalizeCustomTail80CharTruncation()
        {
            string longText = new string('x', 100);
            string normalized = RenamePlanBuilder.NormalizeCustomTailText(longText);
            TestAssert.AreEqual(80, normalized.Length, "truncated to 80 chars");
        }

        private static void StatusReady()
        {
            WithTempDir(delegate(string dir)
            {
                string source = Path.Combine(dir, "a.mp4");
                File.WriteAllText(source, "x");
                ShotRow row = new ShotRow { Sequence = 1 };
                row.MainFiles.Add(source);
                List<RenamePlan> plan = BuildSingleRowPlan(row, 1, 1, true, false);
                TestAssert.AreEqual("就绪", plan[0].Status, "ready status");
            });
        }

        private static void StatusUnchanged()
        {
            WithTempDir(delegate(string dir)
            {
                string source = Path.Combine(dir, "E1-S1-1-T1.mp4");
                File.WriteAllText(source, "x");
                ShotRow row = new ShotRow { Sequence = 1 };
                row.MainFiles.Add(source);
                List<RenamePlan> plan = BuildSingleRowPlan(row, 1, 1, true, false);
                TestAssert.AreEqual("未变化", plan[0].Status, "unchanged status");
            });
        }

        private static void StatusTargetExists()
        {
            WithTempDir(delegate(string dir)
            {
                string source = Path.Combine(dir, "a.mp4");
                string target = Path.Combine(dir, "E1-S1-1-T1.mp4");
                File.WriteAllText(source, "x");
                File.WriteAllText(target, "y");
                ShotRow row = new ShotRow { Sequence = 1 };
                row.MainFiles.Add(source);
                List<RenamePlan> plan = BuildSingleRowPlan(row, 1, 1, true, false);
                TestAssert.AreEqual("目标已存在", plan[0].Status, "target exists status");
            });
        }

        private static void StatusDuplicateNewName()
        {
            WithTempDir(delegate(string dir)
            {
                string first = Path.Combine(dir, "a.mp4");
                string second = Path.Combine(dir, "b.mp4");
                File.WriteAllText(first, "x");
                File.WriteAllText(second, "y");
                ShotRow rowA = new ShotRow { Sequence = 1 };
                rowA.MainFiles.Add(first);
                ShotRow rowB = new ShotRow { Sequence = 1 };
                rowB.MainFiles.Add(second);
                List<RenamePlan> plan = RenamePlanBuilder.BuildPlan(new List<ShotRow> { rowA, rowB }, 1, 1, true, false);
                TestAssert.AreEqual("就绪", plan[0].Status, "first target keeps ready");
                TestAssert.AreEqual("新文件名重复", plan[1].Status, "second same target flagged duplicate");
            });
        }

        private static void StatusSourceMissing()
        {
            ShotRow row = new ShotRow { Sequence = 1 };
            row.MainFiles.Add(@"C:\VmrNoSuchDir_ff8a2\missing.mp4");
            List<RenamePlan> plan = BuildSingleRowPlan(row, 1, 1, true, false);
            TestAssert.AreEqual("源文件丢失", plan[0].Status, "source missing status");
        }

        private static void StatusExportOverwritePending()
        {
            WithTempDir(delegate(string dir)
            {
                string alreadyNamed = Path.Combine(dir, "E5-S1-17-T1.mp4");
                File.WriteAllText(alreadyNamed, "test");
                ShotRow row = new ShotRow { Sequence = 17 };
                row.MainFiles.Add(alreadyNamed);
                List<RenamePlan> plan = BuildSingleRowPlan(row, 5, 1, true, true);
                TestAssert.AreEqual("待覆盖导出1080p", plan[0].Status, "export overwrite pending status");

                string freshSource = Path.Combine(dir, "fresh.mp4");
                File.WriteAllText(freshSource, "test");
                ShotRow freshRow = new ShotRow { Sequence = 18 };
                freshRow.MainFiles.Add(freshSource);
                List<RenamePlan> freshPlan = BuildSingleRowPlan(freshRow, 5, 1, true, true);
                TestAssert.AreEqual("待覆盖导出1080p", freshPlan[0].Status, "ready promotes to export pending");
            });
        }

        private static void IsBlockingIssueTruthTable()
        {
            string[] blocking = { "目标已存在", "目标文件被占用", "新文件名重复", "源文件丢失" };
            foreach (string status in blocking)
            {
                TestAssert.IsTrue(RenamePlanBuilder.IsBlockingIssue(new RenamePlan { Status = status }),
                    "blocking: " + status);
            }

            string[] nonBlocking = { "就绪", "未变化", "待覆盖导出1080p", "另存为新文件", "" };
            foreach (string status in nonBlocking)
            {
                TestAssert.IsFalse(RenamePlanBuilder.IsBlockingIssue(new RenamePlan { Status = status }),
                    "non-blocking: " + status);
            }

            TestAssert.IsFalse(RenamePlanBuilder.IsBlockingIssue(null), "null entry is not blocking");
        }

        private static void BuildPlanResizesTailOverrides()
        {
            // Pinned side effect: BuildPlan resizes the caller's tail-override lists
            // in place to match the file lists (a de facto contract for the UI).
            ShotRow row = new ShotRow { Sequence = 1 };
            row.MainFiles.Add(@"C:\Temp\a.mp4");
            row.MainFiles.Add(@"C:\Temp\b.mp4");
            TestAssert.AreEqual(0, row.MainTailOverrides.Count, "no overrides before build");
            BuildSingleRowPlan(row, 1, 1, true, false);
            TestAssert.AreEqual(2, row.MainTailOverrides.Count, "overrides resized to file count");
        }

        private static void ShotLabelPatternTable()
        {
            string pattern = MaterialRenamerForm.ShotLabelPattern;

            Match m = Regex.Match("28A", pattern);
            TestAssert.IsTrue(m.Success, "28A matches");
            TestAssert.AreEqual("28", m.Groups["num"].Value, "28A number group");
            TestAssert.AreEqual("A", m.Groups["suf"].Value, "28A suffix group");

            m = Regex.Match("28 a", pattern);
            TestAssert.IsTrue(m.Success, "28 a matches with inner whitespace");
            TestAssert.AreEqual("a", m.Groups["suf"].Value, "lowercase suffix captured raw");

            m = Regex.Match("28ab", pattern);
            TestAssert.IsTrue(m.Success, "two-letter suffix matches");
            TestAssert.AreEqual("ab", m.Groups["suf"].Value, "two-letter suffix group");

            m = Regex.Match("7", pattern);
            TestAssert.IsTrue(m.Success, "digits only matches");
            TestAssert.AreEqual("", m.Groups["suf"].Value, "empty suffix group");

            TestAssert.IsFalse(Regex.Match("abc", pattern).Success, "letters only rejected");
            TestAssert.IsFalse(Regex.Match("28abc", pattern).Success, "three-letter suffix rejected");
            TestAssert.IsFalse(Regex.Match("", pattern).Success, "empty rejected");
            TestAssert.IsFalse(Regex.Match("1.5", pattern).Success, "decimal rejected");
        }

        private static RenamePlan BuildFullyPopulatedPlanEntry(ShotRow row)
        {
            return new RenamePlan
            {
                Row = row,
                RowIndex = 3,
                ColumnName = "主要素材",
                IsMain = true,
                FileIndex = 2,
                Scene = 4,
                Shot = 28,
                ShotLabel = "28A",
                Take = 5,
                TailSegment = "T5",
                CustomTailText = "自定义",
                HasCustomTail = true,
                OldPath = @"C:\Temp\a.mp4",
                TargetPath = @"C:\Temp\b.mp4",
                OldName = "a.mp4",
                NewName = "b.mp4",
                Status = "就绪"
            };
        }

        private static void CloneRenamePlanCopiesFieldsDropsShotLabel()
        {
            RenamePlan source = BuildFullyPopulatedPlanEntry(new ShotRow());
            RenamePlan clone = MaterialRenamerForm.CloneRenamePlan(source);

            TestAssert.AreEqual(source.RowIndex, clone.RowIndex, "clone RowIndex");
            TestAssert.AreEqual(source.ColumnName, clone.ColumnName, "clone ColumnName");
            TestAssert.AreEqual(source.IsMain, clone.IsMain, "clone IsMain");
            TestAssert.AreEqual(source.FileIndex, clone.FileIndex, "clone FileIndex");
            TestAssert.AreEqual(source.Scene, clone.Scene, "clone Scene");
            TestAssert.AreEqual(source.Shot, clone.Shot, "clone Shot");
            TestAssert.AreEqual(source.Take, clone.Take, "clone Take");
            TestAssert.AreEqual(source.TailSegment, clone.TailSegment, "clone TailSegment");
            TestAssert.AreEqual(source.CustomTailText, clone.CustomTailText, "clone CustomTailText");
            TestAssert.AreEqual(source.HasCustomTail, clone.HasCustomTail, "clone HasCustomTail");
            TestAssert.AreEqual(source.OldPath, clone.OldPath, "clone OldPath");
            TestAssert.AreEqual(source.TargetPath, clone.TargetPath, "clone TargetPath");
            TestAssert.AreEqual(source.OldName, clone.OldName, "clone OldName");
            TestAssert.AreEqual(source.NewName, clone.NewName, "clone NewName");
            TestAssert.AreEqual(source.Status, clone.Status, "clone Status");

            // KNOWN BUG (pinned as current behavior): CloneRenamePlan omits the
            // ShotLabel field, so cloned export plans lose the "28A" suffix label.
            // Phase 2 replaces this with RenamePlan.Clone() and flips this
            // assertion in its own dedicated bug-fix commit.
            TestAssert.IsNull(clone.ShotLabel, "clone drops ShotLabel (current bug, fix scheduled)");
        }

        private static void CloneRenamePlanSharesRowReference()
        {
            ShotRow row = new ShotRow();
            RenamePlan source = BuildFullyPopulatedPlanEntry(row);
            RenamePlan clone = MaterialRenamerForm.CloneRenamePlan(source);
            TestAssert.IsTrue(object.ReferenceEquals(row, clone.Row),
                "clone must SHARE the live ShotRow reference (undo/export progress depend on identity)");
            TestAssert.IsNull(MaterialRenamerForm.CloneRenamePlan(null), "null clones to null");
        }

        private static void PrepareExportPlanSaveAsRenamesUnchanged()
        {
            string samePath = @"C:\VmrNoSuchDir_ff8a2\E1-S1-1-T1.mp4";
            RenamePlan entry = new RenamePlan
            {
                Row = new ShotRow(),
                OldPath = samePath,
                TargetPath = samePath,
                OldName = "E1-S1-1-T1.mp4",
                NewName = "E1-S1-1-T1.mp4",
                Status = "待覆盖导出1080p"
            };
            List<RenamePlan> prepared = MaterialRenamerForm.PrepareExportPlan(
                new List<RenamePlan> { entry }, ExportOutputMode.SaveAsNewFile);
            TestAssert.AreEqual(1, prepared.Count, "prepared count");
            TestAssert.AreEqual("E1-S1-1-T1_1080p.mp4", prepared[0].NewName, "save-as derives _1080p name");
            TestAssert.AreEqual("另存为新文件", prepared[0].Status, "save-as status");
            TestAssert.AreEqual(samePath, entry.TargetPath, "source entry untouched");
        }

        private static void PrepareExportPlanDuplicateTargetThrows()
        {
            string target = @"C:\VmrNoSuchDir_ff8a2\E1-S1-1-T1.mp4";
            RenamePlan first = new RenamePlan { OldPath = @"C:\VmrNoSuchDir_ff8a2\a.mp4", TargetPath = target, NewName = "E1-S1-1-T1.mp4" };
            RenamePlan second = new RenamePlan { OldPath = @"C:\VmrNoSuchDir_ff8a2\b.mp4", TargetPath = target, NewName = "E1-S1-1-T1.mp4" };
            IOException ex = TestAssert.Throws<IOException>(delegate
            {
                MaterialRenamerForm.PrepareExportPlan(new List<RenamePlan> { first, second }, ExportOutputMode.OverwriteOriginal);
            }, "duplicate export target");
            TestAssert.IsTrue(ex.Message.StartsWith("新文件名重复"), "duplicate message prefix: " + ex.Message);
        }

        private static void PrepareExportPlanExistingTargetThrows()
        {
            WithTempDir(delegate(string dir)
            {
                string source = Path.Combine(dir, "a.mp4");
                string target = Path.Combine(dir, "E1-S1-1-T1.mp4");
                File.WriteAllText(source, "x");
                File.WriteAllText(target, "y");
                RenamePlan entry = new RenamePlan { OldPath = source, TargetPath = target, NewName = "E1-S1-1-T1.mp4" };
                IOException ex = TestAssert.Throws<IOException>(delegate
                {
                    MaterialRenamerForm.PrepareExportPlan(new List<RenamePlan> { entry }, ExportOutputMode.SaveAsNewFile);
                }, "existing export target");
                TestAssert.IsTrue(ex.Message.StartsWith("目标文件已存在"), "existing message prefix: " + ex.Message);
            });
        }

        private static void HistoryValueRoundtrip()
        {
            string[] samples =
            {
                @"C:\素材\第1集\a b.mp4",
                "tab\there",
                "newline\r\nhere",
                ""
            };
            foreach (string sample in samples)
            {
                TestAssert.AreEqual(sample,
                    MaterialRenamerForm.DecodeHistoryValue(MaterialRenamerForm.EncodeHistoryValue(sample)),
                    "history roundtrip");
            }
            TestAssert.AreEqual("", MaterialRenamerForm.DecodeHistoryValue(MaterialRenamerForm.EncodeHistoryValue(null)), "null encodes as empty");
        }

        private static void HistoryEncodeGolden()
        {
            TestAssert.AreEqual("Qzpc57Sg5p2QXOesrDHpm4ZcYSBiLm1wNA==",
                MaterialRenamerForm.EncodeHistoryValue(@"C:\素材\第1集\a b.mp4"),
                "history encode golden (UTF-8 base64)");
        }

        private static void UniquePathWithSuffixFirstCandidate()
        {
            WithTempDir(delegate(string dir)
            {
                string basePath = Path.Combine(dir, "v.mp4");
                TestAssert.AreEqual(Path.Combine(dir, "v_1080p.mp4"),
                    RenamePlanBuilder.GetUniquePathWithSuffix(basePath, "_1080p"),
                    "first free candidate");
            });
        }

        private static void UniquePathWithSuffixCounterAndDefault()
        {
            WithTempDir(delegate(string dir)
            {
                string basePath = Path.Combine(dir, "v.mp4");
                File.WriteAllText(Path.Combine(dir, "v_1080p.mp4"), "x");
                TestAssert.AreEqual(Path.Combine(dir, "v_1080p2.mp4"),
                    RenamePlanBuilder.GetUniquePathWithSuffix(basePath, "_1080p"),
                    "counter appended when taken");
                TestAssert.AreEqual(Path.Combine(dir, "v_副本.mp4"),
                    RenamePlanBuilder.GetUniquePathWithSuffix(basePath, ""),
                    "empty suffix falls back to _副本");
            });
        }
    }
}
