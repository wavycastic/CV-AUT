using CvAut.Models;
using Xunit;

namespace CvAut.Tests
{
    /// <summary>
    /// Locks the behaviour that makes a run observable rather than merely readable: the
    /// measurement fields must reach the screen, amounts must be grouped, the subsystem
    /// must be named, and a field nobody has translated yet must still show up.
    /// </summary>
    public class LogObservabilityTests
    {
        [Fact]
        public void Summary_ReadsBattleResultAsASentenceWithGroupedLoot()
        {
            var entry = new LogEntry(
                "[ATTACK-CS] phase=battle_stats status=success stars=0 gold=368207 elixir=671064 dark_elixir=15707",
                LogLevel.Info);

            Assert.Contains("Kết thúc trận: 0 sao", entry.Summary);
            Assert.Contains("368.207 vàng", entry.Summary);
            Assert.Contains("671.064 dầu", entry.Summary);
            Assert.Contains("15.707 dầu đen", entry.Summary);
        }

        [Fact]
        public void Summary_ShowsTheMatchNumbersThatExplainAFailure()
        {
            var entry = new LogEntry(
                "[TRAIN] phase=validate_troops status=fail reason=troop_missing_dragon score=0.09 threshold=0.80",
                LogLevel.Info);

            // The score and the threshold are the whole point of the line; before this they
            // were parsed and then thrown away.
            Assert.Contains("điểm khớp: 0.09", entry.Summary);
            Assert.Contains("ngưỡng: 0.80", entry.Summary);
        }

        [Fact]
        public void Summary_ShowsAnUntranslatedFieldUnderItsRawName()
        {
            var entry = new LogEntry("[TRAIN] phase=cycle status=success brand_new_metric=42", LogLevel.Info);

            // A value that disappears is worse than a value with an English label.
            Assert.Contains("brand_new_metric: 42", entry.Summary);
        }

        [Fact]
        public void Summary_RendersBooleanFieldsInVietnamese()
        {
            var entry = new LogEntry("[SCOUT-CS] phase=extract status=success total=2606341 total_ok=True", LogLevel.Info);

            Assert.Contains("tổng tài nguyên: 2.606.341", entry.Summary);
            Assert.Contains("đủ dữ liệu: có", entry.Summary);
        }

        [Fact]
        public void Summary_KeepsSmallNumbersUngrouped()
        {
            var entry = new LogEntry("[TRAIN] phase=validate_remaining status=fallback remaining=-1 tap_count=4", LogLevel.Info);

            Assert.Contains("còn lại: -1", entry.Summary);
            Assert.Contains("số lần nhấn: 4", entry.Summary);
        }

        [Fact]
        public void ModuleLabel_NamesKnownSubsystemsAndLeavesUnknownOnesAlone()
        {
            Assert.Equal("Bot", new LogEntry("[FSM-CS] phase=cycle status=start").ModuleLabel);
            Assert.Equal("Giả lập", new LogEntry("[ADB] phase=connect status=success").ModuleLabel);
            Assert.Equal("Huấn luyện", new LogEntry("[TRAIN] phase=train status=start").ModuleLabel);
            Assert.Equal("Làng đêm", new LogEntry("[BB-CS] phase=cycle status=start").ModuleLabel);

            // A severity suffix must not lose the subsystem name.
            Assert.Equal("Bot (cảnh báo)", new LogEntry("[FSM-CS WARNING] phase=cycle status=fail").ModuleLabel);

            // An unknown tag stays verbatim instead of being swallowed.
            Assert.Equal("BRAND-NEW", new LogEntry("[BRAND-NEW] phase=cycle status=start").ModuleLabel);
        }

        [Fact]
        public void Icon_DistinguishesStatusesThatUsedToLookIdentical()
        {
            Assert.Equal("➤", new LogEntry("[ADB] phase=input status=send action=bot_tap x=62 y=658").Icon);
            Assert.Equal("↩", new LogEntry("[TRAIN] phase=validate_remaining status=fallback item=dragon").Icon);
            Assert.Equal("⏳", new LogEntry("[FSM-CS] phase=battle_wait status=pending").Icon);
            Assert.Equal("🔎", new LogEntry("[FSM-CS] phase=home_check status=check").Icon);
            Assert.Equal("∅", new LogEntry("[VISION] phase=find_template status=not_found").Icon);
        }

        [Fact]
        public void Summary_StillKeepsRawInputActionsForDebugging()
        {
            // Guards the field rendering against regressing the deliberate exception:
            // bot_tap and bot_swipe name the exact input sent to the emulator.
            var entry = new LogEntry("[ADB] phase=input status=send action=bot_swipe x1=1 y1=2 x2=3 y2=4 duration_ms=300", LogLevel.Debug);

            Assert.Contains("Bot_swipe", entry.Summary);
            Assert.Contains("nhập liệu", entry.Summary);
            Assert.Contains("từ x: 1", entry.Summary);
            Assert.Contains("đến y: 4", entry.Summary);
        }
    }
}
