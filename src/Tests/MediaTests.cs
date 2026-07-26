using System;
using System.Collections.Generic;

namespace VideoRenamer.Tests
{
    // Characterization tests for ffmpeg argument construction. The exact argument
    // strings are golden values captured from the shipped V1.0.6.0 behavior -
    // any byte-level drift here would change encode quality or break exports.
    public static class MediaTests
    {
        // Golden strings captured from the unmodified code (2026-07-26).
        private const string ExportArgsAudioCopyGolden =
            "-hide_banner -nostdin -nostats -y -i \"C:\\Temp\\input.mp4\" -vf \"scale=1080:1920:flags=bicubic,setsar=1\" -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -threads 0 -progress pipe:1 -c:a copy \"C:\\Temp\\output.mp4\"";

        private const string ExportArgsAudioReencodeGolden =
            "-hide_banner -nostdin -nostats -y -i \"C:\\Temp\\input.mp4\" -vf \"scale=1080:1920:flags=bicubic,setsar=1\" -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -threads 0 -progress pipe:1 -c:a aac -b:a 160k \"C:\\Temp\\output.mp4\"";

        private const string StripArgsGolden =
            "-hide_banner -nostdin -nostats -y -i \"C:\\Temp\\in.mp4\" -vf \"thumbnail=n=12,scale=320:-1\" -frames:v 12 -vsync vfr \"C:\\Temp\\out\\f_%03d.png\"";

        public static List<TestCase> Cases()
        {
            List<TestCase> cases = new List<TestCase>();
            cases.Add(new TestCase("ffmpeg_export_args_audio_copy_golden", FfmpegExportArgsAudioCopyGolden));
            cases.Add(new TestCase("ffmpeg_export_args_audio_reencode_golden", FfmpegExportArgsAudioReencodeGolden));
            cases.Add(new TestCase("ffmpeg_watermark_args_structure", FfmpegWatermarkArgsStructure));
            cases.Add(new TestCase("ffmpeg_no_watermark_args_clean", FfmpegNoWatermarkArgsClean));
            cases.Add(new TestCase("frame_strip_args_golden", FrameStripArgsGolden));
            return cases;
        }

        private static void FfmpegExportArgsAudioCopyGolden()
        {
            TestAssert.AreEqual(ExportArgsAudioCopyGolden,
                FfmpegArguments.BuildExportArguments(@"C:\Temp\input.mp4", @"C:\Temp\output.mp4", true, ""),
                "export args (audio copy) golden");
        }

        private static void FfmpegExportArgsAudioReencodeGolden()
        {
            TestAssert.AreEqual(ExportArgsAudioReencodeGolden,
                FfmpegArguments.BuildExportArguments(@"C:\Temp\input.mp4", @"C:\Temp\output.mp4", false, ""),
                "export args (audio re-encode) golden");
        }

        private static void FfmpegWatermarkArgsStructure()
        {
            // Watermark args embed a machine-dependent font path, so this pins
            // structure rather than an exact golden string.
            string args = FfmpegArguments.BuildExportArguments(@"C:\Temp\input.mp4", @"C:\Temp\output.mp4", true, "E5-S1-1-T1.mp4");
            TestAssert.IsTrue(args.Contains("-vf"), "watermark args use -vf");
            TestAssert.IsTrue(args.Contains("drawtext="), "watermark args use drawtext");
            TestAssert.IsFalse(args.Contains("-filter_complex"), "watermark args avoid -filter_complex");
            TestAssert.IsFalse(args.Contains("-loop"), "watermark args avoid -loop");
        }

        private static void FfmpegNoWatermarkArgsClean()
        {
            string args = FfmpegArguments.BuildExportArguments(@"C:\Temp\input.mp4", @"C:\Temp\output.mp4", true, "");
            TestAssert.IsFalse(args.Contains("drawtext="), "no drawtext without watermark");
            TestAssert.IsFalse(args.Contains("未命名视频"), "no placeholder text without watermark");
        }

        private static void FrameStripArgsGolden()
        {
            TestAssert.AreEqual(StripArgsGolden,
                VideoFrameStripProvider.BuildStripArguments(@"C:\Temp\in.mp4", @"C:\Temp\out\f_%03d.png", 12),
                "frame strip args golden");
        }
    }
}
