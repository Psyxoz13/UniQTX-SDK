using System.Runtime.InteropServices;

internal static class QtxProcessorFeatures
{
    public static bool IsAvx2Available => IsProcessorFeaturePresent(Avx2InstructionsAvailable);

    private const uint Avx2InstructionsAvailable = 40;

    [DllImport("kernel32")]
    private static extern bool IsProcessorFeaturePresent(uint processorFeature);
}
