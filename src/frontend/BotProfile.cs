using System;
using System.IO;

namespace CvAut
{
    public sealed class BotProfile
    {
        public string Name { get; init; } = "Default";
        public string ConfigPath { get; init; } = Path.Combine("Config", "test_config.json");
        public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

        public override string ToString() => Name;
    }
}
