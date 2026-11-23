using System;
using System.Runtime.InteropServices;
using UnityEngine;

public static class QtxQualityPrediction
{
    private const int LowestQuality = 20;
    private const int ProbeQuality = 60;
    private const int CeilingQuality = 95;
    private const double QualityPerLambdaDecade = 30;

    public static int PredictQuality(Color32[] sample, int width, int height, float maxPsnrLoss, float maxSsimLoss)
    {
        var rgba = MemoryMarshal.AsBytes(sample.AsSpan()).ToArray();
        var ceiling = Measure(rgba, sample, width, height, CeilingQuality);
        var probe = Measure(rgba, sample, width, height, ProbeQuality);
        var allowedExcess = Math.Pow(10, maxPsnrLoss / 10) - 1;

        var quality = Math.Max(GetQualityFor(GetExcess(ceiling.colorPsnr, probe.colorPsnr), allowedExcess), GetQualityFor(GetExcess(ceiling.alphaPsnr, probe.alphaPsnr), allowedExcess));
        quality = Math.Max(quality, GetQualityFor(ceiling.ssim - probe.ssim, maxSsimLoss));

        return (int)Math.Ceiling(Math.Clamp(quality, LowestQuality, CeilingQuality));
    }

    private static double GetExcess(double ceilingPsnr, double probePsnr)
    {
        return Math.Pow(10, (ceilingPsnr - probePsnr) / 10) - 1;
    }

    private static double GetQualityFor(double excess, double allowedExcess)
    {
        if (excess <= 0)
        {
            return LowestQuality;
        }

        return ProbeQuality - QualityPerLambdaDecade * Math.Log10(allowedExcess / excess);
    }

    private static (double colorPsnr, double alphaPsnr, double ssim) Measure(byte[] rgba, Color32[] sample, int width, int height, int quality)
    {
        var qtx = QtxNativeEncoder.Encode(rgba, width, height, quality);
        var pixels = QtxDecoder.Decode(qtx, QtxTarget.Rgba32);
        var decoded = MemoryMarshal.Cast<byte, Color32>(pixels.AsSpan(0, width * height * 4)).ToArray();

        return QtxMeasuredQuality.Compare(sample, decoded, width, height, true);
    }
}
