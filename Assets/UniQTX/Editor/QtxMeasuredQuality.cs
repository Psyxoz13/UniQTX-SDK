using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class QtxMeasuredQuality : ScriptableObject
{
    private const int Window = 8;
    private const int WindowPixels = Window * Window;
    private const double MaxPsnr = 99;
    private const double LumaUnit = 1000.0 * 255;
    private const double C1 = 0.01 * 255 * 0.01 * 255 * LumaUnit * LumaUnit;
    private const double C2 = 0.03 * 255 * 0.03 * 255 * LumaUnit * LumaUnit;

    [SerializeField] private TextureFormat format;
    [SerializeField] private bool hasAlpha;
    [SerializeField] private double colorPsnr;
    [SerializeField] private double alphaPsnr;
    [SerializeField] private double ssim;

    public TextureFormat Format => format;
    public bool HasAlpha => hasAlpha;
    public double ColorPsnr => colorPsnr;
    public double AlphaPsnr => alphaPsnr;
    public double Ssim => ssim;

    public static QtxMeasuredQuality Measure(Color32[] source, Color32[] transcoded, int width, int height, bool hasAlpha, TextureFormat format)
    {
        var (colorPsnr, alphaPsnr, ssim) = Compare(source, transcoded, width, height, false);

        return Create(format, hasAlpha, colorPsnr, alphaPsnr, ssim);
    }

    public static (double colorPsnr, double alphaPsnr, double ssim) Compare(Color32[] source, Color32[] transcoded, int width, int height, bool isVisibleOnly)
    {
        if (source.Length != width * height || transcoded.Length != width * height)
        {
            throw new ArgumentException($"compared images have {source.Length} and {transcoded.Length} pixels, expected {width * height}");
        }

        var bands = (height + Window - 1) / Window;
        var colorErrors = new long[bands];
        var alphaErrors = new long[bands];
        var visiblePixels = new long[bands];
        var ssimSums = new double[bands];
        var ssimWindows = new int[bands];

        Parallel.For(
            0,
            bands,
            band =>
            {
                var top = band * Window;
                var bottom = Math.Min(height, top + Window);
                (colorErrors[band], alphaErrors[band], visiblePixels[band]) = SquaredErrors(source, transcoded, top * width, bottom * width, isVisibleOnly);

                if (bottom - top == Window)
                {
                    (ssimSums[band], ssimWindows[band]) = SsimSum(source, transcoded, width, top, isVisibleOnly);
                }
            });

        long colorError = 0;
        long alphaError = 0;
        long visible = 0;
        double ssimSum = 0;
        var windows = 0;

        for (var band = 0; band < bands; band++)
        {
            colorError += colorErrors[band];
            alphaError += alphaErrors[band];
            visible += visiblePixels[band];
            ssimSum += ssimSums[band];
            windows += ssimWindows[band];
        }

        if (visible == 0)
        {
            return (MaxPsnr, MaxPsnr, 1);
        }

        return (Psnr(colorError / (255.0 * 255 * visible * 3)), Psnr(alphaError / (double)visible), windows == 0 ? 1 : ssimSum / windows);
    }

    public static QtxMeasuredQuality ReadFrom(BinaryReader reader)
    {
        return Create((TextureFormat)reader.ReadInt32(), reader.ReadBoolean(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
    }

    public void WriteTo(BinaryWriter writer)
    {
        writer.Write((int)format);
        writer.Write(hasAlpha);
        writer.Write(colorPsnr);
        writer.Write(alphaPsnr);
        writer.Write(ssim);
    }

    private static QtxMeasuredQuality Create(TextureFormat format, bool hasAlpha, double colorPsnr, double alphaPsnr, double ssim)
    {
        var quality = CreateInstance<QtxMeasuredQuality>();
        quality.format = format;
        quality.hasAlpha = hasAlpha;
        quality.colorPsnr = colorPsnr;
        quality.alphaPsnr = alphaPsnr;
        quality.ssim = ssim;
        return quality;
    }

    private static (long color, long alpha, long visible) SquaredErrors(Color32[] source, Color32[] transcoded, int start, int end, bool isVisibleOnly)
    {
        long color = 0;
        long alpha = 0;
        long visible = 0;

        for (var i = start; i < end; i++)
        {
            var a = source[i];
            var b = transcoded[i];
            long red = a.r * a.a - b.r * b.a, green = a.g * a.a - b.g * b.a, blue = a.b * a.a - b.b * b.a;
            var opacity = a.a - b.a;
            color += red * red + green * green + blue * blue;
            alpha += opacity * opacity;

            if (isVisibleOnly == false || a.a > 0)
            {
                visible++;
            }
        }

        return (color, alpha, visible);
    }

    private static (double sum, int windows) SsimSum(Color32[] source, Color32[] transcoded, int width, int top, bool isVisibleOnly)
    {
        double sum = 0;
        var windows = 0;

        for (var left = 0; left + Window <= width; left += Window)
        {
            long sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0;
            var isVisible = isVisibleOnly == false;

            for (var y = top; y < top + Window; y++)
            {
                for (var i = y * width + left; i < y * width + left + Window; i++)
                {
                    long a = Luma(source[i]), b = Luma(transcoded[i]);
                    sumA += a;
                    sumB += b;
                    sumAA += a * a;
                    sumBB += b * b;
                    sumAB += a * b;
                    isVisible |= source[i].a > 0;
                }
            }

            if (isVisible == false)
            {
                continue;
            }

            double meanA = sumA / (double)WindowPixels, meanB = sumB / (double)WindowPixels;
            var varianceA = sumAA / (double)WindowPixels - meanA * meanA;
            var varianceB = sumBB / (double)WindowPixels - meanB * meanB;
            var covariance = sumAB / (double)WindowPixels - meanA * meanB;
            sum += (2 * meanA * meanB + C1) * (2 * covariance + C2) / ((meanA * meanA + meanB * meanB + C1) * (varianceA + varianceB + C2));
            windows++;
        }

        return (sum, windows);
    }

    private static int Luma(Color32 color) => (299 * color.r + 587 * color.g + 114 * color.b) * color.a;

    private static double Psnr(double meanSquaredError) => meanSquaredError <= 0 ? MaxPsnr : Math.Min(MaxPsnr, 10 * Math.Log10(255.0 * 255.0 / meanSquaredError));
}

public static class QtxMeasuredQualityCache
{
    private const int CacheFormatVersion = 1;
    private const string Folder = "Library/QtxMeasuredQualityCache";

    public static string PathOf(string encodeCachePath, byte[] transcoded)
    {
        var key = new Hash128();
        key.Append(encodeCachePath);
        key.Append(transcoded);
        key.Append(Application.unityVersion);
        key.Append(CacheFormatVersion);
        return Path.Combine(Folder, key + ".bin");
    }

    public static QtxMeasuredQuality TryRead(string path)
    {
        if (!File.Exists(path))
            return null;

        using var reader = new BinaryReader(File.OpenRead(path));
        try
        {
            return QtxMeasuredQuality.ReadFrom(reader);
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    public static QtxMeasuredQuality Write(string path, QtxMeasuredQuality quality)
    {
        Directory.CreateDirectory(Folder);
        var temporaryPath = path + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temporaryPath)))
            quality.WriteTo(writer);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
        return quality;
    }
}
