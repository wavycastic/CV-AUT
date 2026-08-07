using System;
using System.IO;
using CvAut;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class WallLoggerTests
    {
        [Fact]
        public void GenerateRunId_ReturnsNonEmptySixCharHex()
        {
            string runId = WallLogger.GenerateRunId();
            Assert.NotNull(runId);
            Assert.Equal(6, runId.Length);
        }

        [Fact]
        public void LogInfo_OutputsFormattedString()
        {
            var sw = new StringWriter();
            Console.SetOut(sw);

            WallLogger.LogInfo(
                phase: "test_phase",
                status: "ok",
                reason: "test_reason",
                village: 1,
                cycle: 5,
                trigger: "post_battle",
                batchBudget: 3,
                batchLimit: 5,
                runId: "a1b2c3",
                elapsedMs: 120,
                extra: "key=val");

            string output = sw.ToString().Trim();
            Assert.Contains("[WALL-MV]", output);
            Assert.Contains("phase=test_phase", output);
            Assert.Contains("status=ok", output);
            Assert.Contains("reason=test_reason", output);
            Assert.Contains("village=1", output);
            Assert.Contains("cycle=5", output);
            Assert.Contains("trigger=post_battle", output);
            Assert.Contains("batch_budget=3", output);
            Assert.Contains("batch_limit=5", output);
            Assert.Contains("run_id=a1b2c3", output);
            Assert.Contains("elapsed_ms=120", output);
            Assert.Contains("key=val", output);
        }
    }
}
