using CvAut.Models;
using Xunit;

namespace CvAut.Tests
{
    /// <summary>
    /// Locks the Vietnamese wording for the log lines that are actually used while
    /// debugging a run: the training validation failures, the attack pipeline stages and
    /// the OCR diagnostics. These are the shapes a flat phrase book cannot cover, so they
    /// are the ones worth pinning.
    /// </summary>
    public class LogVocabularyTests
    {
        [Fact]
        public void Summary_ExpandsMissingTroopReason_AndKeepsDiagnosticNumbers()
        {
            var entry = new LogEntry(
                "[TRAIN] phase=validate_troops status=fail reason=troop_missing_dragon score=0.09 threshold=0.80 detail=\"score_below_threshold roi=891x155 template=64x64\"",
                LogLevel.Info);

            Assert.Contains("xác thực lính", entry.Summary.ToLowerInvariant());
            Assert.Contains("thiếu lính rồng", entry.Summary);
            Assert.Contains("điểm khớp dưới ngưỡng", entry.Summary);

            // The measurements are the evidence: they must survive translation verbatim.
            Assert.Contains("roi=891x155", entry.Summary);
            Assert.Contains("template=64x64", entry.Summary);
        }

        [Fact]
        public void Summary_TranslatesOcrDetail_AndKeepsConfidence()
        {
            var entry = new LogEntry(
                "[TRAIN] phase=validate status=fail reason=army_space_unreadable detail=\"ocr_low_confidence confidence=0.00 digits=5\"",
                LogLevel.Info);

            Assert.Contains("không đọc được ô sức chứa quân", entry.Summary);
            Assert.Contains("OCR đọc được nhưng không đủ tin cậy", entry.Summary);
            Assert.Contains("confidence=0.00", entry.Summary);
        }

        [Fact]
        public void Summary_NamesThePipelineStage()
        {
            var entry = new LogEntry(
                "[ATTACK-CS] phase=pipeline stage=spell_deployment status=succeeded strategy=Dragon_Attack",
                LogLevel.Info);

            Assert.Contains("chuỗi tấn công", entry.Summary.ToLowerInvariant());
            Assert.Contains("thả phép", entry.Summary);
        }

        [Fact]
        public void Summary_TranslatesUnknownDetailTokenVerbatim()
        {
            var entry = new LogEntry("[TRAIN] phase=validate status=fail detail=\"brand_new_token x=1\"", LogLevel.Info);

            // An unmapped token must show up as-is rather than disappearing.
            Assert.Contains("brand_new_token x=1", entry.Summary);
        }

        [Fact]
        public void Summary_KeepsRawInputActionForDebugging()
        {
            // bot_tap and bot_swipe are intentionally not translated: these rows exist to
            // show which input was sent to the emulator.
            var entry = new LogEntry("[ADB] phase=input status=send action=bot_tap x=62 y=658", LogLevel.Debug);

            Assert.Contains("Bot_tap", entry.Summary);
            Assert.Contains("nhập liệu", entry.Summary);
        }
    }
}
