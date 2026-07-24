namespace CvAut.Services.Emulators
{
    /// <summary>
    /// Common local emulator ADB ports shared by vendor install-path scanners and the
    /// generic <see cref="Scanners.CommonPortScanner"/>. Kept in one place so adding a
    /// new emulator does not require touching scanner internals.
    /// </summary>
    public static class CommonPorts
    {
        // BlueStacks / Android emulator / LDPlayer / MEmu common ranges.
        public static readonly int[] All =
        {
            5556, 5554, 5555, 5557, 5558, 5559, 5560,
            21503, 21513, 21523, 21533, 21543,
        };

        public static readonly int[] Memu =
        {
            21503, 21513, 21523, 21533, 21543, 5555,
        };

        public static readonly int[] BlueStacks =
        {
            5555, 5556, 5557, 5558, 5559, 5560,
        };
    }
}
