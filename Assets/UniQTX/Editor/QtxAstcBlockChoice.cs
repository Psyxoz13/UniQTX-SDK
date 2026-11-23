using System.IO;
using UnityEngine;

public class QtxAstcBlockChoice : ScriptableObject
{
    public static readonly int[] BlockSizes = { 4, 6, 8 };

    [SerializeField] private bool _isMeasured;
    [SerializeField] private int blockSize = 4;
    [SerializeField] private bool hasAlpha;
    [SerializeField] private double[] colorPsnr = new double[3];
    [SerializeField] private double[] alphaPsnr = new double[3];
    [SerializeField] private double[] ssim = new double[3];

    public bool IsMeasured => _isMeasured;
    public int BlockSize => blockSize;
    public bool HasAlpha => hasAlpha;
    public double ColorPsnr(int candidate) => colorPsnr[candidate];
    public double AlphaPsnr(int candidate) => alphaPsnr[candidate];
    public double Ssim(int candidate) => ssim[candidate];

    public static QtxAstcBlockChoice Fixed(int blockSize)
    {
        var choice = CreateInstance<QtxAstcBlockChoice>();
        choice.blockSize = blockSize;
        return choice;
    }

    public static QtxAstcBlockChoice Measured(QtxMeasuredQuality[] candidates)
    {
        var choice = CreateInstance<QtxAstcBlockChoice>();
        choice._isMeasured = true;
        choice.hasAlpha = candidates[0].HasAlpha;
        for (var i = 0; i < candidates.Length; i++)
        {
            choice.colorPsnr[i] = candidates[i].ColorPsnr;
            choice.alphaPsnr[i] = candidates[i].AlphaPsnr;
            choice.ssim[i] = candidates[i].Ssim;
        }
        return choice;
    }

    public int GetAutomaticSize(float maxPsnrLoss, float maxSsimLoss)
    {
        for (var candidate = BlockSizes.Length - 1; candidate > 0; candidate--)
        {
            if (colorPsnr[0] - colorPsnr[candidate] <= maxPsnrLoss && alphaPsnr[0] - alphaPsnr[candidate] <= maxPsnrLoss && ssim[0] - ssim[candidate] <= maxSsimLoss)
            {
                return BlockSizes[candidate];
            }
        }

        return BlockSizes[0];
    }

    public QtxAstcBlockChoice ChooseBlockSize(float maxPsnrLoss, float maxSsimLoss)
    {
        blockSize = GetAutomaticSize(maxPsnrLoss, maxSsimLoss);

        return this;
    }

    public QtxAstcBlockChoice ForceBlockSize(int size)
    {
        blockSize = size;

        return this;
    }

    public static QtxAstcBlockChoice ReadFrom(BinaryReader reader)
    {
        var choice = CreateInstance<QtxAstcBlockChoice>();
        choice._isMeasured = true;
        choice.hasAlpha = reader.ReadBoolean();
        for (var i = 0; i < BlockSizes.Length; i++)
        {
            choice.colorPsnr[i] = reader.ReadDouble();
            choice.alphaPsnr[i] = reader.ReadDouble();
            choice.ssim[i] = reader.ReadDouble();
        }
        return choice;
    }

    public void WriteTo(BinaryWriter writer)
    {
        writer.Write(hasAlpha);
        for (var i = 0; i < BlockSizes.Length; i++)
        {
            writer.Write(colorPsnr[i]);
            writer.Write(alphaPsnr[i]);
            writer.Write(ssim[i]);
        }
    }
}

public static class QtxAstcBlockChoiceCache
{
    private const int CacheFormatVersion = 2;
    private const string Folder = "Library/QtxAstcBlockChoiceCache";

    public static string PathOf(string encodeCachePath)
    {
        var key = new Hash128();
        key.Append(encodeCachePath);
        key.Append(QtxLibraryVersion.Decoder);
        key.Append(Application.unityVersion);
        key.Append(CacheFormatVersion);
        return Path.Combine(Folder, key + ".bin");
    }

    public static QtxAstcBlockChoice TryRead(string path)
    {
        if (!File.Exists(path))
            return null;

        using var reader = new BinaryReader(File.OpenRead(path));
        try
        {
            return QtxAstcBlockChoice.ReadFrom(reader);
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    public static QtxAstcBlockChoice Write(string path, QtxAstcBlockChoice choice)
    {
        Directory.CreateDirectory(Folder);
        var temporaryPath = path + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temporaryPath)))
            choice.WriteTo(writer);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
        return choice;
    }
}
