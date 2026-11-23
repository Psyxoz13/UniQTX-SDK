
using System;
using System.Collections.Generic;
using System.Threading.Tasks;


internal class QtxEncoderSettings
{
    public int[] CenterSteps = { 2, 3, 5, 8 };
    public int[] UpCenterSteps = { 4, 5, 8, 12 };
    public int[] ExtentSteps = { 2, 3, 5, 8 };
    public int AlphaStep = 4;
    public double Lambda = 60;
    public double PerceptualStrength = 0.25;
    public int ExactAlphaWeight = 128;
    public int AlphaWeight = 16;
    public int RedWeight = 5;
    public int GreenWeight = 10;
    public int BlueWeight = 2;
    public double StructureWeight = 3;
    public int EdgeShift = 1;
    public double StepLambda = 0;
    public double InheritWeight = 4;
    public int SolidStep = 1;
    public int InheritReach = 8;
    public bool ScaleStepsWithLambda = true;
    public int StripeBlocks = 16;
    public int TableCandidates = 2;
    public int OutlierThreshold = 40;
    public int OutlierWeight = 3;
    public double SmoothStructureScale = 2;

    public static QtxEncoderSettings ForQuality(int quality) => new QtxEncoderSettings { Lambda = 60 * Math.Pow(10, (50 - quality) / 30.0) };
}

internal static class QtxMath
{
    private const double InverseLn2 = 1.4426950408889634;

    public static double Log2(double value) => Math.Log(value) * InverseLn2;

    public static int PopCount(uint value)
    {
        value -= (value >> 1) & 0x55555555u;
        value = (value & 0x33333333u) + ((value >> 2) & 0x33333333u);
        return (int)((((value + (value >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24);
    }

    public static int LeadingZeroCount(uint value)
    {
        if (value == 0) return 32;
        var count = 0;
        while ((value & 0x80000000u) == 0) { value <<= 1; count++; }
        return count;
    }
}

internal static class QtxBits
{
    public static uint CompactEven(uint value)
    {
        value &= 0x55555555u;
        value = (value | (value >> 1)) & 0x33333333u;
        value = (value | (value >> 2)) & 0x0F0F0F0Fu;
        value = (value | (value >> 4)) & 0x00FF00FFu;
        return (value | (value >> 8)) & 0x0000FFFFu;
    }

    public static uint SubtractTones(uint a, uint b)
    {
        const uint High = 0xAAAAAAAAu;
        return ((a | High) - (b & ~High)) ^ ((a ^ ~b) & High);
    }
}

internal static class QtxField
{
    private static readonly int[,] Pairs = { { 0, 1 }, { 0, 2 }, { 0, 3 }, { 1, 2 }, { 1, 3 }, { 2, 3 } };

    public static readonly int[,] Bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };

    public static void SmoothPoints(ReadOnlySpan<int> neighborhood, Span<int> points)
    {
        for (var m = 0; m < 4; m++)
        {
            var horizontal = (m & 1) != 0 ? 5 : 3;
            var vertical = (m & 2) != 0 ? 7 : 1;
            var diagonal = vertical + horizontal - 4;
            for (var c = 0; c < 4; c++)
                points[m * 4 + c] = (9 * neighborhood[16 + c] + 3 * neighborhood[horizontal * 4 + c] + 3 * neighborhood[vertical * 4 + c] + neighborhood[diagonal * 4 + c] + 8) >> 4;
        }
    }

    public static void Derive(ReadOnlySpan<int> points, int channels, int[] low, int[] high, out uint tones, out uint ditheredTones)
    {
        ditheredTones = 0;
        int bestPair = 0, bestDistance = -1;
        for (var pair = 0; pair < 6; pair++)
        {
            var distance = 0;
            for (var c = 0; c < channels; c++)
                distance += Math.Abs(points[Pairs[pair, 0] * 4 + c] - points[Pairs[pair, 1] * 4 + c]);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                bestPair = pair;
            }
        }
        int first = Pairs[bestPair, 0], second = Pairs[bestPair, 1];
        for (var c = 0; c < 4; c++)
        {
            low[c] = points[first * 4 + c];
            high[c] = points[second * 4 + c];
        }
        tones = 0;
        var flat = true;
        for (var c = 0; c < channels; c++)
            if (high[c] != low[c]) flat = false;
        if (flat)
        {
            for (var c = 0; c < 4; c++)
            {
                var mean = (points[c] + points[4 + c] + points[8 + c] + points[12 + c] + 2) >> 2;
                low[c] = mean;
                high[c] = mean;
            }
            return;
        }
        long norm = 0;
        for (var c = 0; c < channels; c++)
            norm += (long)(high[c] - low[c]) * (high[c] - low[c]);
        Span<long> projections = stackalloc long[4];
        for (var m = 0; m < 4; m++)
        {
            long projection = 0;
            for (var c = 0; c < channels; c++)
                projection += (long)(points[m * 4 + c] - low[c]) * (high[c] - low[c]);
            projections[m] = projection;
        }
        for (var y = 0; y < 4; y++)
        {
            var left = (3 - y) * projections[0] + y * projections[2];
            var right = (3 - y) * projections[1] + y * projections[3];
            for (var x = 0; x < 4; x++)
            {
                var doubled = 2 * ((3 - x) * left + x * right);
                var tone = (doubled >= 3 * norm ? 1u : 0u) + (doubled >= 9 * norm ? 1u : 0u) + (doubled >= 15 * norm ? 1u : 0u);
                tones |= tone << ((y * 4 + x) * 2);
                var dithered = doubled - (2 * Bayer[y, x] - 15) * (3 * norm) / 16;
                var ditheredTone = (dithered >= 3 * norm ? 1u : 0u) + (dithered >= 9 * norm ? 1u : 0u) + (dithered >= 15 * norm ? 1u : 0u);
                ditheredTones |= ditheredTone << ((y * 4 + x) * 2);
            }
        }
    }
}

internal class QtxImageEncoder
{
    public const int FormatVersion = 1;
    public const int HeaderBytes = 24;
    public const int Lanes = 8;
    public const int TypeTransparent = 0;
    public const int TypeSmooth = 1;
    public const int TypeSolid = 2;
    public const int TypeDual = 6;
    public const int TypeCodebook = 7;
    public const int HeadCode = 3;
    public const int CodeBuckets = 120;
    public const int HeadTable = HeadCode + CodeBuckets;
    public const int HeadLiteral = HeadTable + CodeBuckets;
    public const int HeadDual = HeadLiteral + 2;
    public const int HeadPair = HeadDual + 1;
    public const int HeadBinary = HeadPair + 1;
    public const int OpcodeCopyLeft = HeadBinary + 2;
    public const int PairSlots = 9;
    public const int PairSymbols = 1 + PairSlots * (PairSlots - 1);
    public const int OpcodeCopyUp = OpcodeCopyLeft + 1;
    public const int OpcodeRun = OpcodeCopyUp + 1;
    public const int OpcodeSymbols = OpcodeRun + 1;
    public static readonly int[] CodeStarts = BuildCodeStarts();
    public static readonly int[] CodeBits = BuildCodeBits();
    private static readonly int[] bucketOfCode = BuildBucketOfCode();
    private static readonly int[] _groupedStraightBuckets = GetBucketsOfGroupedEntries(0);
    private static readonly int[] _groupedInvertedBuckets = GetBucketsOfGroupedEntries(1);

    private static int[] GetBucketsOfGroupedEntries(int flip)
    {
        var entries = ShapeCodebookByFirstRow.Entries;
        var buckets = new int[entries.Length];

        for (var index = 0; index < entries.Length; index++)
        {
            buckets[index] = bucketOfCode[entries[index] * 2 + flip];
        }

        return buckets;
    }

    private static int[] BuildCodeBits()
    {
        var bits = new int[CodeBuckets];
        var layout = new List<int>();
        foreach (var (count, size) in new[] { (8, 1), (8, 2), (16, 3), (16, 4), (16, 5), (16, 6), (16, 7), (8, 8), (8, 9), (6, 10), (1, 6), (1, 4) })
            for (var k = 0; k < count; k++)
                layout.Add(size);
        for (var bucket = 0; bucket < CodeBuckets; bucket++)
            bits[bucket] = bucket < layout.Count ? layout[bucket] : 0;
        return bits;
    }

    private static int[] BuildCodeStarts()
    {
        var bits = BuildCodeBits();
        var starts = new int[CodeBuckets + 1];
        for (var bucket = 0; bucket < CodeBuckets; bucket++)
            starts[bucket + 1] = starts[bucket] + (1 << bits[bucket]);
        return starts;
    }

    private static int[] BuildBucketOfCode()
    {
        var starts = BuildCodeStarts();
        var bucketOf = new int[starts[CodeBuckets]];
        for (var bucket = 0; bucket < CodeBuckets; bucket++)
            for (var code = starts[bucket]; code < starts[bucket + 1]; code++)
                bucketOf[code] = bucket;
        return bucketOf;
    }
    public const int MinRun = 4;
    public const int RunLengthBuckets = 13;
    public const int OpcodeContexts = 9;
    public const int FirstRowActivity = 1;
    public const int BucketSymbols = 21;
    public const int JointSymbols = 126;
    public const int JointEscape = 125;
    public const int TableSymbols = 8;
    public static readonly int[] TableExtents = { 8, 17, 29, 42, 60, 80, 106, 183 };
    public const int CenterSmooth = 0;
    public const int CenterSolid = 1;
    public const int CenterLine = 2;
    public const int CenterKinds = 3;

    public const int AlphabetOpcode = 0;
    public const int AlphabetRunLength = AlphabetOpcode + OpcodeContexts;
    public const int AlphabetCenter = AlphabetRunLength + 1;
    public const int AlphabetTableCenter = AlphabetCenter + CenterKinds * 20;
    public const int AlphabetExtent = AlphabetTableCenter + 5;
    public const int AlphabetTable = AlphabetExtent + 20;
    public const int AlphabetTableAlpha = AlphabetTable + 4;
    public const int AlphabetRow = AlphabetTableAlpha + 1;
    public const int AlphabetMask = AlphabetRow + 8;
    public const int AlphabetAlphaRow = AlphabetMask + 2;
    public const int AlphabetAlphaFine = AlphabetAlphaRow + 1;
    public const int AlphabetPairPattern = AlphabetAlphaFine + 1;
    public const int AlphabetPair = AlphabetPairPattern + 1;
    public const int AlphabetCount = AlphabetPair + 1;
    public static readonly int[] PlaneStarts = { AlphabetOpcode, AlphabetCenter, AlphabetExtent, AlphabetRow, AlphabetCount };
    public const int PlaneOpcode = 0;

    private static readonly int[] DecorrelatedOrder = { 1, 0, 2 };

    private readonly QtxEncoderSettings settings;
    private readonly int width, height, blocksWide, blocksHigh, channels;
    private readonly bool hasAlpha;
    private readonly int[] colorWeights;
    private readonly int colorScale;
    private readonly double baseLambda;
    private readonly bool premultiplied;
    private readonly byte[] blockPixels;
    private readonly bool[] transparentBlocks;
    private readonly float[] fittedCenters, fittedExtents, blockVariances, smoothTargets;
    private readonly bool[] opaqueHalo;
    private readonly double meanVariance;
    private readonly float[] inheritFactors;
    private readonly int[] centers;
    private readonly int[] extents;
    private readonly byte[] types;
    private readonly uint[] patterns;
    private readonly uint[] alphaPatterns;
    private readonly uint[] alphaFines;
    private readonly double[] rowLumaSums = new double[64];
    private readonly long[] rowErrors = new long[1024];
    private readonly double[] codebookCosts = new double[CodeBuckets];
    private readonly uint[] pairColors = new uint[PairSlots];
    private readonly bool[] pairSlotUsable = new bool[PairSlots];
    private int pairCount;
    private bool pairCoded;
    private readonly int[] codebookSeen = new int[ShapeCodebook.Size];
    private int codebookStamp;
    private readonly bool leftMode;

    private const int WarmChunks = 4;
    private readonly CostModel costs = new CostModel();
    private readonly QtxRansEncoder rans;
    private readonly int[] block = new int[64];
    private readonly int[] channelWeights = new int[64];
    private readonly long[] toneErrors = new long[64];
    private readonly int[] channelPlanes = new int[64];
    private readonly int[] weightPlanes = new int[64];
    private readonly int[] palette = new int[16];
    private readonly int[] edgeTargets = new int[128];
    private readonly int[] edgeCounts = new int[16];
    private readonly double[] sourceLuma = new double[16];
    private readonly double[] paletteLuma = new double[4];
    private double sourceLumaMean, sourceLumaVariance;
    private double blockLambda;
    private bool opaqueBlock;
    private bool transparentHole;
    private int centerStep, extentStep;
    private readonly int[] centerSteps = new int[4], extentSteps = new int[4];
    private readonly int alphaStep;
    private readonly int solidStep;
    private readonly bool copyEnabled;
    private double totalScore;
    private uint abovePattern;
    public readonly byte[] Reconstruction;

    public static void DetectAlpha(byte[] rgba, out bool hasAlpha, out bool premultiplied)
    {
        hasAlpha = false;
        premultiplied = true;
        for (var i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] != 255) hasAlpha = true;
            if (rgba[i - 1] > rgba[i] || rgba[i - 2] > rgba[i] || rgba[i - 3] > rgba[i]) premultiplied = false;
        }
    }

    private QtxImageEncoder(byte[] rgba, int width, int height, QtxEncoderSettings settings, bool hasAlpha, bool premultiplied, bool copyEnabled, float[] smoothTargets, bool[] opaqueHalo)
    {
        this.smoothTargets = smoothTargets;
        this.opaqueHalo = opaqueHalo;
        this.copyEnabled = copyEnabled;
        leftMode = hasAlpha || copyEnabled;
        this.settings = settings;
        this.width = width;
        this.height = height;
        this.hasAlpha = hasAlpha;
        this.premultiplied = premultiplied;
        colorWeights = hasAlpha ? new[] { 1, 1, 1 } : new[] { settings.RedWeight, settings.GreenWeight, settings.BlueWeight };
        colorScale = Math.Max(1, (colorWeights[0] + colorWeights[1] + colorWeights[2]) / 3);
        baseLambda = settings.Lambda * colorScale;
        blocksWide = (width + 3) / 4;
        blocksHigh = (height + 3) / 4;
        channels = hasAlpha ? 4 : 3;
        var blockCount = blocksWide * blocksHigh;
        blockPixels = new byte[blockCount * 64];
        transparentBlocks = new bool[blockCount];
        fittedCenters = new float[blockCount * 4];
        fittedExtents = new float[blockCount * 4];
        blockVariances = new float[blockCount];
        centers = new int[blockCount * 4];
        extents = new int[blockCount * 4];
        types = new byte[blockCount];
        patterns = new uint[blockCount];
        alphaPatterns = new uint[blockCount];
        alphaFines = new uint[blockCount];
        lineLows = new uint[blockCount];
        lineHighs = new uint[blockCount];
        Reconstruction = new byte[blocksWide * 4 * blocksHigh * 4 * 4];
        rans = new QtxRansEncoder(AlphabetSizes());
        var scale = settings.ScaleStepsWithLambda ? Math.Clamp(Math.Pow((settings.StepLambda > 0 ? settings.StepLambda : baseLambda) / 60, 0.3), 0.5, 2.5) : 1.0;
        for (var k = 0; k < 4; k++)
        {
            centerSteps[k] = Math.Max(1, (int)Math.Round((leftMode ? settings.CenterSteps : settings.UpCenterSteps)[k] * scale));
            extentSteps[k] = Math.Max(1, (int)Math.Round(settings.ExtentSteps[k] * scale));
        }
        alphaStep = Math.Max(1, (int)Math.Round(settings.AlphaStep * scale));
        solidStep = copyEnabled ? Math.Clamp(settings.SolidStep, 1, 255) : 0;
        Parallel.For(0, blocksHigh, by => AnalyzeRow(rgba, by));
        double varianceSum = 0;
        var visibleBlocks = 0;
        for (var i = 0; i < blockCount; i++)
        {
            if (transparentBlocks[i]) continue;
            varianceSum += blockVariances[i];
            visibleBlocks++;
        }
        meanVariance = visibleBlocks > 0 ? varianceSum / visibleBlocks : 0;
        inheritFactors = InheritFactors();
    }

    private bool Flat(int index)
    {
        if (transparentBlocks[index]) return false;
        for (var c = 0; c < 4; c++)
            if (fittedExtents[index * 4 + c] > 1) return false;
        return true;
    }

    private bool SameFlat(int a, int b)
    {
        if (!Flat(b)) return false;
        for (var c = 0; c < 4; c++)
            if (Math.Abs(fittedCenters[a * 4 + c] - fittedCenters[b * 4 + c]) > 1) return false;
        return true;
    }

    private float[] InheritFactors()
    {
        var factors = new float[blocksWide * blocksHigh];
        for (var by = 0; by < blocksHigh; by++)
        for (var bx = 0; bx < blocksWide; bx++)
        {
            var index = by * blocksWide + bx;
            factors[index] = 1;
            if (!copyEnabled || settings.InheritWeight <= 0 || !Flat(index)) continue;
            var heirs = 0;
            for (var k = 1; k <= settings.InheritReach && bx + k < blocksWide && SameFlat(index, index + k); k++) heirs++;
            for (var k = 1; k <= settings.InheritReach && by + k < blocksHigh && SameFlat(index, index + k * blocksWide); k++) heirs++;
            factors[index] = (float)(1 + settings.InheritWeight * heirs);
        }
        return factors;
    }

    public static int[] AlphabetSizes()
    {
        var sizes = new int[AlphabetCount];
        for (var i = 0; i < OpcodeContexts; i++) sizes[AlphabetOpcode + i] = OpcodeSymbols;
        sizes[AlphabetRunLength] = RunLengthBuckets;
        var starts = new List<int>();
        for (var k = 0; k < 4; k++)
        {
            for (var kind = 0; kind < CenterKinds; kind++) starts.Add(AlphabetCenter + kind * 20 + k * 5);
            starts.Add(AlphabetExtent + k * 5);
        }
        starts.Add(AlphabetTableCenter);
        foreach (var start in starts)
        {
            sizes[start] = JointSymbols;
            for (var i = 1; i < 5; i++) sizes[start + i] = BucketSymbols;
        }
        for (var i = 0; i < 4; i++) sizes[AlphabetTable + i] = TableSymbols;
        sizes[AlphabetTableAlpha] = BucketSymbols;
        for (var i = 0; i < 8; i++) sizes[AlphabetRow + i] = 256;
        sizes[AlphabetMask] = 256;
        sizes[AlphabetMask + 1] = 256;
        sizes[AlphabetAlphaRow] = 256;
        sizes[AlphabetAlphaFine] = 16;
        sizes[AlphabetPairPattern] = CodeBuckets + 1;
        sizes[AlphabetPair] = PairSymbols;
        return sizes;
    }
    public const int MipHeaderBytes = 8;
    public const int MipLevelsMax = 16;

    public static byte[] MipContainer(byte[][] levels)
    {
        if (levels.Length < 1 || levels.Length > MipLevelsMax)
            throw new ArgumentException($"QTX stores 1 to {MipLevelsMax} mip levels, got {levels.Length}");
        var total = MipHeaderBytes + levels.Length * 4;
        foreach (var level in levels)
            total += level.Length;
        var container = new byte[total];
        container[0] = (byte)'Q'; container[1] = (byte)'T'; container[2] = (byte)'X'; container[3] = (byte)'M';
        container[4] = (byte)levels.Length;
        var offset = MipHeaderBytes + levels.Length * 4;
        for (var i = 0; i < levels.Length; i++)
        {
            var length = levels[i].Length;
            for (var b = 0; b < 4; b++)
                container[MipHeaderBytes + i * 4 + b] = (byte)(length >> (b * 8));
            Array.Copy(levels[i], 0, container, offset, length);
            offset += length;
        }
        return container;
    }

    public static byte[] HalfSize(byte[] rgba, int width, int height, out int halfWidth, out int halfHeight)
    {
        halfWidth = Math.Max(1, width >> 1);
        halfHeight = Math.Max(1, height >> 1);
        var half = new byte[halfWidth * halfHeight * 4];
        for (var y = 0; y < halfHeight; y++)
        for (var x = 0; x < halfWidth; x++)
        for (var c = 0; c < 4; c++)
        {
            int x0 = Math.Min(x * 2, width - 1), x1 = Math.Min(x * 2 + 1, width - 1);
            int y0 = Math.Min(y * 2, height - 1), y1 = Math.Min(y * 2 + 1, height - 1);
            var sum = rgba[(y0 * width + x0) * 4 + c] + rgba[(y0 * width + x1) * 4 + c] + rgba[(y1 * width + x0) * 4 + c] + rgba[(y1 * width + x1) * 4 + c];
            half[(y * halfWidth + x) * 4 + c] = (byte)((sum + 2) >> 2);
        }
        return half;
    }

    public const int SharedTablesFlag = 2;

    public static byte[] EncodeWithMips(byte[] rgba, int width, int height, QtxEncoderSettings settings)
    {
        var levels = new List<byte[]> { rgba };
        var sizes = new List<(int Width, int Height)> { (width, height) };
        while (width > 1 || height > 1)
        {
            rgba = HalfSize(rgba, width, height, out width, out height);
            levels.Add(rgba);
            sizes.Add((width, height));
        }
        return EncodeLevels(levels.ToArray(), sizes.ToArray(), settings);
    }

    public static byte[] EncodeLevels(byte[][] levels, (int Width, int Height)[] sizes, QtxEncoderSettings settings)
    {
        var headers = new byte[levels.Length][];
        var logs = new QtxRansEncoder[levels.Length][];
        for (var level = 0; level < levels.Length; level++)
        {
            (headers[level], logs[level]) = EncodeLevel(levels[level], sizes[level].Width, sizes[level].Height, settings, out _);
            if (level > 0)
                headers[level][23] |= SharedTablesFlag;
        }
        return MipContainer(QtxRansEncoder.CombineLevels(headers, logs));
    }

    public static byte[] Encode(byte[] rgba, int width, int height, QtxEncoderSettings settings, out byte[] reconstruction)
    {
        var (header, logs) = EncodeLevel(rgba, width, height, settings, out reconstruction);
        return QtxRansEncoder.Combine(header, logs);
    }

    private static (byte[] Header, QtxRansEncoder[] Logs) EncodeLevel(byte[] rgba, int width, int height, QtxEncoderSettings settings, out byte[] reconstruction)
    {
        DetectAlpha(rgba, out var hasAlpha, out var premultiplied);
        var copyEnabled = hasAlpha;
        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        var stripeBlocks = Math.Clamp(settings.StripeBlocks, 1, 255);
        var stripeCount = (blocksHigh + stripeBlocks - 1) / stripeBlocks;
        var targets = SmoothTargets(rgba, width, height, hasAlpha);
        var encoders = new QtxImageEncoder[stripeCount];
        var logs = new QtxRansEncoder[stripeCount];
        var opaque = OpaqueBlocks(rgba, width, height);
        bool[] OpaqueHalo(int firstRow, int rows, int firstColumn, int columns)
        {
            var halo = new bool[(rows + 2) * columns];
            for (var r = -1; r <= rows; r++)
            for (var x = 0; x < columns; x++)
            {
                var y = firstRow + r;
                halo[(r + 1) * columns + x] = y < 0 || y >= blocksHigh || opaque[y * blocksWide + firstColumn + x];
            }
            return halo;
        }
        float[] Halo(int firstRow, int rows, int firstColumn, int columns)
        {
            var halo = new float[(rows + 2) * columns * 4];
            for (var r = -1; r <= rows; r++)
            for (var x = 0; x < columns; x++)
            for (var c = 0; c < 4; c++)
            {
                var y = firstRow + r;
                halo[((r + 1) * columns + x) * 4 + c] = y < 0 || y >= blocksHigh ? float.NaN : targets[(y * blocksWide + firstColumn + x) * 4 + c];
            }
            return halo;
        }
        QtxImageEncoder StripeEncoder(int stripe)
        {
            var firstRow = stripe * stripeBlocks * 4;
            var rows = Math.Min(stripeBlocks * 4, height - firstRow);
            var part = new byte[width * rows * 4];
            Buffer.BlockCopy(rgba, firstRow * width * 4, part, 0, part.Length);
            return new QtxImageEncoder(part, width, rows, settings, hasAlpha, premultiplied, copyEnabled, Halo(stripe * stripeBlocks, (rows + 3) / 4, 0, blocksWide), OpaqueHalo(stripe * stripeBlocks, (rows + 3) / 4, 0, blocksWide));
        }
        QtxImageEncoder WarmEncoder(int stripe, int chunk, int chunks, bool warmCopyEnabled)
        {
            var firstRow = stripe * stripeBlocks * 4;
            var rows = Math.Min(stripeBlocks * 4, height - firstRow);
            var firstColumn = width * chunk / chunks / 4 * 4;
            var warmWidth = Math.Min(width - firstColumn, Math.Max(4, width * (chunk + 1) / chunks / 4 * 4 - firstColumn));
            var part = new byte[warmWidth * rows * 4];
            for (var y = 0; y < rows; y++)
                Buffer.BlockCopy(rgba, ((firstRow + y) * width + firstColumn) * 4, part, y * warmWidth * 4, warmWidth * 4);
            return new QtxImageEncoder(part, warmWidth, rows, settings, hasAlpha, premultiplied, warmCopyEnabled, Halo(stripe * stripeBlocks, (rows + 3) / 4, firstColumn / 4, (warmWidth + 3) / 4), OpaqueHalo(stripe * stripeBlocks, (rows + 3) / 4, firstColumn / 4, (warmWidth + 3) / 4));
        }
        CostModel warm = null;
        if (stripeCount > 1 || hasAlpha == false)
        {
            var modes = hasAlpha ? new[] { true } : new[] { false, true };
            var chunks = Math.Max(1, Math.Min(WarmChunks, blocksWide / 16));
            var warmers = new QtxImageEncoder[modes.Length * chunks];
            Parallel.For(0, warmers.Length, task =>
            {
                warmers[task] = WarmEncoder(stripeCount / 2, task % chunks, chunks, modes[task / chunks]);
                warmers[task].EncodeStripe();
            });
            var bestMode = 0;
            var bestScore = double.MaxValue;
            for (var mode = 0; mode < modes.Length; mode++)
            {
                double score = 0;
                for (var chunk = 0; chunk < chunks; chunk++)
                {
                    score += warmers[mode * chunks + chunk].totalScore;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    bestMode = mode;
                }
            }
            copyEnabled = modes[bestMode];
            var bestCosts = new CostModel[chunks];
            for (var chunk = 0; chunk < chunks; chunk++)
            {
                bestCosts[chunk] = warmers[bestMode * chunks + chunk].costs;
            }
            warm = new CostModel();
            warm.MergeFrom(bestCosts);
        }
        Parallel.For(0, stripeCount, stripe =>
        {
            encoders[stripe] = StripeEncoder(stripe);
            if (warm != null)
                encoders[stripe].costs.CopyFrom(warm);
            logs[stripe] = encoders[stripe].EncodeStripe();
        });
        reconstruction = new byte[blocksWide * 16 * blocksHigh * 4];
        for (var stripe = 0; stripe < stripeCount; stripe++)
        {
            var encoder = encoders[stripe];
            FinalizeSmooth(encoder.Reconstruction, blocksWide, encoder.blocksHigh, encoder.types, encoder.centers, hasAlpha);
            Buffer.BlockCopy(encoder.Reconstruction, 0, reconstruction, stripe * stripeBlocks * 4 * blocksWide * 16, encoder.Reconstruction.Length);
        }
        var first = encoders[0];
        var header = new byte[HeaderBytes];
        header[0] = (byte)'Q'; header[1] = (byte)'T'; header[2] = (byte)'X'; header[3] = (byte)' ';
        header[4] = (byte)width; header[5] = (byte)(width >> 8);
        header[6] = (byte)height; header[7] = (byte)(height >> 8);
        header[8] = FormatVersion;
        header[9] = (byte)(hasAlpha ? 1 : 0);
        for (var i = 0; i < 4; i++)
        {
            header[10 + i] = (byte)first.centerSteps[i];
            header[14 + i] = (byte)first.extentSteps[i];
        }
        header[18] = (byte)first.alphaStep;
        header[22] = (byte)first.solidStep;
        header[23] = 0;
        header[19] = (byte)stripeBlocks;
        return (header, logs);
    }

    private static bool[] OpaqueBlocks(byte[] rgba, int width, int height)
    {
        int blocksWide = (width + 3) / 4, blocksHigh = (height + 3) / 4;
        var opaque = new bool[blocksWide * blocksHigh];
        for (var by = 0; by < blocksHigh; by++)
        for (var bx = 0; bx < blocksWide; bx++)
        {
            bool solid = true, clear = true;
            for (var p = 0; p < 16; p++)
            {
                var alpha = rgba[(Math.Min(by * 4 + (p >> 2), height - 1) * width + Math.Min(bx * 4 + (p & 3), width - 1)) * 4 + 3];
                solid &= alpha == 255;
                clear &= alpha == 0;
            }
            opaque[by * blocksWide + bx] = solid || clear;
        }
        return opaque;
    }

    private static float[] SmoothTargets(byte[] rgba, int width, int height, bool hasAlpha)
    {
        int blocksWide = (width + 3) / 4, blocksHigh = (height + 3) / 4;
        var count = blocksWide * blocksHigh;
        var source = new float[count * 64];
        var transparent = new bool[count];
        var targets = new double[count * 4];
        Parallel.For(0, blocksHigh, by =>
        {
            for (var bx = 0; bx < blocksWide; bx++)
            {
                var index = by * blocksWide + bx;
                var visible = false;
                for (var p = 0; p < 16; p++)
                {
                    var at = (Math.Min(by * 4 + (p >> 2), height - 1) * width + Math.Min(bx * 4 + (p & 3), width - 1)) * 4;
                    var alpha = rgba[at + 3];
                    visible |= alpha != 0;
                    for (var c = 0; c < 4; c++)
                    {
                        var value = c == 3 ? (hasAlpha ? alpha : 255) : alpha == 0 ? 0 : rgba[at + c];
                        source[index * 64 + p * 4 + c] = value;
                        targets[index * 4 + c] += value / 16.0;
                    }
                }
                transparent[index] = !visible;
            }
        });
        var corrections = new double[count * 4];
        for (var iteration = 0; iteration < 8; iteration++)
        {
            Parallel.For(0, blocksHigh, by =>
            {
                Span<double> neighborhood = stackalloc double[36];
                for (var bx = 0; bx < blocksWide; bx++)
                {
                    var index = by * blocksWide + bx;
                    for (var k = 0; k < 9; k++)
                    {
                        int nx = Math.Clamp(bx + k % 3 - 1, 0, blocksWide - 1), ny = Math.Clamp(by + k / 3 - 1, 0, blocksHigh - 1);
                        var own = transparent[ny * blocksWide + nx];
                        var neighbor = own ? index : ny * blocksWide + nx;
                        for (var c = 0; c < 4; c++)
                            neighborhood[k * 4 + c] = targets[neighbor * 4 + c];
                    }
                    for (var c = 0; c < 4; c++)
                        corrections[index * 4 + c] = 0;
                    for (var p = 0; p < 16; p++)
                    {
                        int x = p & 3, y = p >> 2;
                        var dx = x < 2 ? 3 : 5;
                        var dy = y < 2 ? 1 : 7;
                        var wx = Math.Abs((x + 0.5) / 4 - 0.5);
                        var wy = Math.Abs((y + 0.5) / 4 - 0.5);
                        var diagonal = dx + dy - 4;
                        for (var c = 0; c < 4; c++)
                        {
                            var predicted = (1 - wx) * (1 - wy) * neighborhood[16 + c] + wx * (1 - wy) * neighborhood[dx * 4 + c] + (1 - wx) * wy * neighborhood[dy * 4 + c] + wx * wy * neighborhood[diagonal * 4 + c];
                            corrections[index * 4 + c] += (source[index * 64 + p * 4 + c] - predicted) / 16.0;
                        }
                    }
                }
            });
            for (var i = 0; i < targets.Length; i++)
                targets[i] = Math.Clamp(targets[i] + corrections[i] * 1.6, 0, 255);
        }
        var result = new float[count * 4];
        for (var i = 0; i < count; i++)
        for (var c = 0; c < 4; c++)
            result[i * 4 + c] = transparent[i] ? float.NaN : (float)targets[i * 4 + c];
        return result;
    }

    private static void GatherNeighborhood(byte[] types, int[] centers, int blocksWide, int blocksHigh, int bx, int by, bool hasAlpha, Span<int> neighborhood)
    {
        var index = by * blocksWide + bx;
        for (var k = 0; k < 9; k++)
        {
            int nx = Math.Clamp(bx + k % 3 - 1, 0, blocksWide - 1), ny = Math.Clamp(by + k / 3 - 1, 0, blocksHigh - 1);
            var neighbor = types[ny * blocksWide + nx] == TypeTransparent ? index : ny * blocksWide + nx;
            for (var c = 0; c < 4; c++)
                neighborhood[k * 4 + c] = c == 3 && !hasAlpha ? 255 : Clamp255(centers[neighbor * 4 + c]);
        }
    }

    private static void FinalizeSmooth(byte[] reconstruction, int blocksWide, int blocksHigh, byte[] types, int[] centers, bool hasAlpha)
    {
        var channels = hasAlpha ? 4 : 3;
        Parallel.For(0, blocksHigh, by =>
        {
            Span<int> neighborhood = stackalloc int[36];
            Span<int> points = stackalloc int[16];
            int[] low = new int[4], high = new int[4], tonePalette = new int[16];
            var rowBytes = blocksWide * 16;
            for (var bx = 0; bx < blocksWide; bx++)
            {
                if (types[by * blocksWide + bx] != TypeSmooth)
                    continue;
                GatherNeighborhood(types, centers, blocksWide, blocksHigh, bx, by, hasAlpha, neighborhood);
                QtxField.SmoothPoints(neighborhood, points);
                QtxField.Derive(points, channels, low, high, out _, out var dithered);
                EndpointPalette(low, high, tonePalette);
                for (var p = 0; p < 16; p++)
                {
                    var at = (by * 4 + (p >> 2)) * rowBytes + (bx * 4 + (p & 3)) * 4;
                    var tone = (int)((dithered >> (p * 2)) & 3);
                    for (var c = 0; c < 4; c++)
                        reconstruction[at + c] = (byte)tonePalette[tone * 4 + c];
                }
            }
        });
    }
    private void AnalyzeRow(byte[] rgba, int by)
    {
        var pixels = new int[64];
        var center = new double[4];
        var extent = new double[4];
        for (var bx = 0; bx < blocksWide; bx++)
        {
            var index = by * blocksWide + bx;
            var transparent = true;
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
            {
                var px = Math.Min(bx * 4 + x, width - 1);
                var py = Math.Min(by * 4 + y, height - 1);
                var at = (py * width + px) * 4;
                var p = y * 4 + x;
                var alpha = rgba[at + 3];
                for (var c = 0; c < 3; c++)
                    pixels[p * 4 + c] = alpha == 0 ? 0 : rgba[at + c];
                pixels[p * 4 + 3] = alpha;
                if (alpha != 0) transparent = false;
            }
            for (var i = 0; i < 64; i++)
                blockPixels[index * 64 + i] = (byte)pixels[i];
            transparentBlocks[index] = transparent;
            if (transparent)
                continue;
            blockVariances[index] = (float)LumaVariance(pixels);
            FitLine(pixels, center, extent);
            if (!hasAlpha) { center[3] = 255; extent[3] = 0; }
            for (var c = 0; c < 4; c++)
            {
                fittedCenters[index * 4 + c] = (float)center[c];
                fittedExtents[index * 4 + c] = (float)extent[c];
            }
        }
    }

    private QtxRansEncoder EncodeStripe()
    {
        for (var by = 0; by < blocksHigh; by++)
        {
            for (var bx = 0; bx < blocksWide; bx++)
                EncodeBlock(bx, by);
            rans.EndRow();
        }
        return rans;
    }
    private static int Med(int a, int b, int c)
    {
        var max = Math.Max(a, b);
        var min = Math.Min(a, b);
        return c >= max ? min : c <= min ? max : a + b - c;
    }


    public static int ActivityClass(int activity) => activity < 3 ? 0 : activity < 10 ? 1 : activity < 28 ? 2 : 3;


    private static int Clamp255(int value) => value < 0 ? 0 : value > 255 ? 255 : value;

    private static double ResidualBits(int residual) => residual == 0 ? 0 : 2 + 2 * QtxMath.Log2(Math.Abs(residual) + 1);

    private struct Line
    {
        public int C0, C1, C2, C3;
        public int E0, E1, E2, E3;
        public int R0, R1, R2, R3;
        public int X0, X1, X2, X3;

        public int Center(int c) => c == 0 ? C0 : c == 1 ? C1 : c == 2 ? C2 : C3;
        public int Extent(int c) => c == 0 ? E0 : c == 1 ? E1 : c == 2 ? E2 : E3;
        public bool Tabled;
        public int TableIndex;
        public int Pair;
        public int[] CenterResiduals => new[] { R0, R1, R2, R3 };
        public int[] ExtentResiduals => new[] { X0, X1, X2, X3 };
    }

    private void LowHigh(in Line line, int c, out int low, out int high)
    {
        var center = line.Center(c);
        low = Clamp255(center - line.Extent(c));
        high = Clamp255(center + line.Extent(c));
    }
    private static double EndpointError(int center, int extent, double targetLow, double targetHigh)
    {
        double low = Clamp255(center - extent) - Math.Clamp(targetLow, 0, 255);
        double high = Clamp255(center + extent) - Math.Clamp(targetHigh, 0, 255);
        return low * low + high * high;
    }

    private int EndValue(int centerBase, int centerResidual, int centerScale, int extentBase, int extentResidual, int end) => centerBase + centerResidual * centerScale + end * (extentBase + extentResidual * extentStep);

    private Line QuantizeLine(double[] targetLow, double[] targetHigh, int[] centerPrediction, int[] extentPrediction, bool flat, bool solid)
    {
        var line = new Line();
        var lambda = blockLambda;
        var greenResidual = 0;
        var extentGreenResidual = 0;
        for (var i = 0; i < 4; i++)
        {
            var c = i < 3 ? DecorrelatedOrder[i] : 3;
            if (c == 3 && !hasAlpha)
            {
                line.C3 = 255;
                break;
            }
            var low = Math.Clamp(targetLow[c], -128.0, 383.0);
            var high = Math.Clamp(targetHigh[c], -128.0, 383.0);
            var mid = (low + high) / 2;
            var half = flat ? 0 : (high - low) / 2;
            var centerScale = solid && solidStep > 0 ? solidStep : c < 3 ? centerStep : alphaStep;
            var centerBase = c == 0 || c == 2 ? centerPrediction[c] + greenResidual * centerScale : centerPrediction[c];
            var extentBase = flat ? 0 : c == 0 || c == 2 ? extentPrediction[c] + extentGreenResidual * extentStep : extentPrediction[c];
            var centerGuess = (int)Math.Round((mid - centerBase) / centerScale);
            var extentGuess = (int)Math.Round((half - extentBase) / extentStep);
            var best = double.MaxValue;
            int bestCenterResidual = 0, bestExtentResidual = 0;
            for (var dc = -2; dc <= 1; dc++)
            {
                var centerResidual = dc == -2 ? 0 : centerGuess + dc;
                if (dc > -2 && centerResidual == 0)
                    continue;
                var centerValue = centerBase + centerResidual * centerScale;
                for (var de = flat ? 1 : -2; de <= 1; de++)
                {
                    var extentResidual = flat ? 0 : de == -2 ? 0 : extentGuess + de;
                    if (!flat && de > -2 && extentResidual == 0)
                        continue;
                    var extentValue = extentBase + extentResidual * extentStep;
                    var exactness = c == 3 && (targetLow[c] <= 0 || targetHigh[c] >= 255) ? settings.ExactAlphaWeight * colorScale : 1;
                    var score = EndpointError(centerValue, extentValue, targetLow[c], targetHigh[c]) * 8 * exactness + lambda * (ResidualBits(centerResidual) + ResidualBits(extentResidual));
                    if (score < best)
                    {
                        best = score;
                        bestCenterResidual = centerResidual;
                        bestExtentResidual = extentResidual;
                    }
                }
            }
            if (c == 3 && opaqueBlock)
            {
                var extentMagnitude = Math.Abs(extentBase + bestExtentResidual * extentStep);
                while (centerBase + bestCenterResidual * centerScale - extentMagnitude < 255)
                    bestCenterResidual++;
            }
            if (c == 3 && hasAlpha && !opaqueBlock && !flat)
            {
                var lowEnd = targetLow[c] <= targetHigh[c] ? -1 : 1;
                for (var end = -1; end <= 1; end += 2)
                {
                    var bound = end == lowEnd ? (blockHasClearAlpha ? -1 : 0) : (blockHasFullAlpha ? 1 : 0);
                    if (bound == 0)
                        continue;
                    var otherBound = end == lowEnd ? (blockHasFullAlpha ? 1 : 0) : (blockHasClearAlpha ? -1 : 0);
                    while (bound > 0 ? EndValue(centerBase, bestCenterResidual, centerScale, extentBase, bestExtentResidual, end) < 255 : EndValue(centerBase, bestCenterResidual, centerScale, extentBase, bestExtentResidual, end) > 0)
                    {
                        if (otherBound == -bound)
                            bestExtentResidual += end * bound;
                        else
                            bestCenterResidual += bound;
                    }
                }
            }
            if (c == 3 && transparentHole)
            {
                var extentMagnitude = Math.Abs(extentBase + bestExtentResidual * extentStep);
                while (centerBase + bestCenterResidual * centerScale - extentMagnitude > 0 && centerBase + bestCenterResidual * centerScale + extentMagnitude > 255)
                    bestCenterResidual--;
            }
            var center = centerBase + bestCenterResidual * centerScale;
            var extent = extentBase + bestExtentResidual * extentStep;
            if (c == 1) { greenResidual = bestCenterResidual; extentGreenResidual = bestExtentResidual; }
            switch (c)
            {
                case 0: line.C0 = center; line.E0 = extent; line.R0 = bestCenterResidual; line.X0 = bestExtentResidual; break;
                case 1: line.C1 = center; line.E1 = extent; line.R1 = bestCenterResidual; line.X1 = bestExtentResidual; break;
                case 2: line.C2 = center; line.E2 = extent; line.R2 = bestCenterResidual; line.X2 = bestExtentResidual; break;
                default: line.C3 = center; line.E3 = extent; line.R3 = bestCenterResidual; line.X3 = bestExtentResidual; break;
            }
        }
        return line;
    }

    private static uint PackColor(int r, int g, int b, int a) => (uint)(r | (g << 8) | (b << 16) | (a << 24));

    private void StoreLowHigh(int index, in Line line)
    {
        Span<int> low = stackalloc int[4], high = stackalloc int[4];
        for (var c = 0; c < 4; c++)
            LowHigh(line, c, out low[c], out high[c]);
        lineLows[index] = PackColor(low[0], low[1], low[2], low[3]);
        lineHighs[index] = PackColor(high[0], high[1], high[2], high[3]);
    }

    private void LinePalette(in Line line, int[] target)
    {
        for (var c = 0; c < 4; c++)
        {
            LowHigh(line, c, out var low, out var high);
            for (var t = 0; t < 4; t++)
                target[t * 4 + c] = (low * (3 - t) + high * t + 1) / 3;
        }
    }

    private static void EndpointPalette(int[] low, int[] high, int[] target)
    {
        for (var c = 0; c < 4; c++)
            for (var t = 0; t < 4; t++)
                target[t * 4 + c] = (Clamp255(low[c]) * (3 - t) + Clamp255(high[c]) * t + 1) / 3;
    }

    private double Luma(int r, int g, int b, int a) => (r * 0.2126 + g * 0.7152 + b * 0.0722) * (hasAlpha && !premultiplied ? a / 255.0 : 1.0);

    private void PrepareSourceLuma()
    {
        double sum = 0, squares = 0;
        for (var p = 0; p < 16; p++)
        {
            var y = Luma(block[p * 4], block[p * 4 + 1], block[p * 4 + 2], block[p * 4 + 3]);
            sourceLuma[p] = y;
            sum += y;
            squares += y * y;
        }
        sourceLumaMean = sum / 16;
        sourceLumaVariance = Math.Max(0, squares / 16 - sourceLumaMean * sourceLumaMean);
        for (var row = 0; row < 4; row++)
        for (var nibble = 0; nibble < 16; nibble++)
        {
            double rowSum = 0;
            for (var x = 0; x < 4; x++)
                if ((nibble & (1 << x)) != 0) rowSum += sourceLuma[row * 4 + x];
            rowLumaSums[row * 16 + nibble] = rowSum;
        }
    }

    private double StructurePenalty(uint pattern)
    {
        if (settings.StructureWeight <= 0)
            return 0;
        var low = CompactEvenBits(pattern);
        var high = CompactEvenBits(pattern >> 1);
        double sumY = 0, sumYY = 0, sumXY = 0;
        for (var t = 0; t < 4; t++)
        {
            var mask = ((t & 1) != 0 ? low : ~low) & ((t & 2) != 0 ? high : ~high) & 0xFFFFu;
            if (mask == 0) continue;
            var count = QtxMath.PopCount(mask);
            var y = paletteLuma[t];
            var sourceSum = rowLumaSums[mask & 15] + rowLumaSums[16 + ((mask >> 4) & 15)] + rowLumaSums[32 + ((mask >> 8) & 15)] + rowLumaSums[48 + (mask >> 12)];
            sumY += count * y;
            sumYY += count * y * y;
            sumXY += y * sourceSum;
        }
        var meanY = sumY / 16;
        var varianceY = Math.Max(0, sumYY / 16 - meanY * meanY);
        var covariance = sumXY / 16 - sourceLumaMean * meanY;
        const double C2 = 58.5;
        var contrastStructure = (2 * covariance + C2) / (sourceLumaVariance + varianceY + C2);
        return settings.StructureWeight * 10000 * (1 - contrastStructure);
    }

    private static uint CompactEvenBits(uint value)
    {
        value &= 0x55555555u;
        value = (value | (value >> 1)) & 0x33333333u;
        value = (value | (value >> 2)) & 0x0F0F0F0Fu;
        value = (value | (value >> 4)) & 0x00FF00FFu;
        return (value | (value >> 8)) & 0x0000FFFFu;
    }

    private void PrepareEdgeTargets(int bx, int by)
    {
        var rowBytes = blocksWide * 16;
        for (var p = 0; p < 16; p++) edgeCounts[p] = 0;
        var index = by * blocksWide + bx;
        for (var y = 0; bx > 0 && y < 4; y++)
        {
            var p = y * 4;
            var neighbor = (index - 1) * 64 + (y * 4 + 3) * 4;
            var reconstructed = (by * 4 + y) * rowBytes + (bx * 4 - 1) * 4;
            for (var c = 0; c < 4; c++)
                edgeTargets[(p * 2 + edgeCounts[p]) * 4 + c] = block[p * 4 + c] + Reconstruction[reconstructed + c] - blockPixels[neighbor + c];
            edgeCounts[p]++;
        }
        for (var x = 0; by > 0 && x < 4; x++)
        {
            var p = x;
            var neighbor = (index - blocksWide) * 64 + (12 + x) * 4;
            var reconstructed = (by * 4 - 1) * rowBytes + (bx * 4 + x) * 4;
            for (var c = 0; c < 4; c++)
                edgeTargets[(p * 2 + edgeCounts[p]) * 4 + c] = block[p * 4 + c] + Reconstruction[reconstructed + c] - blockPixels[neighbor + c];
            edgeCounts[p]++;
        }
    }

    private void ComputeToneErrors(int[] tonePalette)
    {
        for (var t = 0; t < 4; t++)
            paletteLuma[t] = Luma(tonePalette[t * 4], tonePalette[t * 4 + 1], tonePalette[t * 4 + 2], tonePalette[t * 4 + 3]);
        var outlierFloor = settings.OutlierThreshold * settings.OutlierThreshold;
        for (var p = 0; p < 16; p++)
        for (var t = 0; t < 4; t++)
        {
            var error = 0;
            for (var c = 0; c < 4; c++)
            {
                var d = tonePalette[t * 4 + c] - block[p * 4 + c];
                var square = d * d;
                error += square * channelWeights[p * 4 + c] + Math.Max(square - outlierFloor, 0) * settings.OutlierWeight;
            }
            for (var k = 0; k < edgeCounts[p]; k++)
            {
                var edge = 0;
                for (var c = 0; c < 4; c++)
                {
                    var d = tonePalette[t * 4 + c] - edgeTargets[(p * 2 + k) * 4 + c];
                    edge += d * d;
                }
                error += edge >> settings.EdgeShift;
            }
            toneErrors[p * 4 + t] = error;
        }
    }

    private long PatternError(uint pattern)
    {
        long error = 0;
        for (var p = 0; p < 16; p++)
            error += toneErrors[p * 4 + (int)((pattern >> (p * 2)) & 3)];
        return error;
    }

    private uint IdealPattern(out long error)
    {
        uint ideal = 0;
        error = 0;
        for (var p = 0; p < 16; p++)
        {
            var best = 0;
            for (var t = 1; t < 4; t++)
                if (toneErrors[p * 4 + t] < toneErrors[p * 4 + best]) best = t;
            ideal |= (uint)best << (p * 2);
            error += toneErrors[p * 4 + best];
        }
        return ideal;
    }

    private enum Kind { Smooth, Solid, Line }

    private struct Choice
    {
        public Kind Kind;
        public Line Line;
        public uint Pattern;
        public uint AlphaPattern;
        public uint AlphaFine;
        public bool Dual;
        public bool Binary;
        public int Entry;
        public bool Coded;
        public long Error;
        public double Penalty;
        public double Cost;
        public double Score;
    }

    private int OpcodeContext(int bx, int by)
    {
        int Class(int type) => type == TypeTransparent ? 0 : type <= TypeSolid ? 1 : 2;
        var up = by > 0 ? Class(types[(by - 1) * blocksWide + bx]) : 0;
        var upRight = by > 0 && bx + 1 < blocksWide ? Class(types[(by - 1) * blocksWide + bx + 1]) : 0;
        var left = bx > 0 ? Class(types[by * blocksWide + bx - 1]) : 0;
        return leftMode ? left * 3 + up : upRight * 3 + up;
    }

    private void PredictCenter(int bx, int by, int[] prediction)
    {
        var index = by * blocksWide + bx;
        var left = leftMode && bx > 0;
        for (var c = 0; c < 4; c++)
        {
            if (left && by > 0)
                prediction[c] = Med(centers[(index - 1) * 4 + c], centers[(index - blocksWide) * 4 + c], centers[(index - blocksWide - 1) * 4 + c]);
            else if (left)
                prediction[c] = centers[(index - 1) * 4 + c];
            else if (by > 0)
                prediction[c] = (centers[(index - blocksWide - (bx > 0 ? 1 : 0)) * 4 + c] + 2 * centers[(index - blocksWide) * 4 + c] + centers[(index - blocksWide + (bx + 1 < blocksWide ? 1 : 0)) * 4 + c] + 2) >> 2;
            else
                prediction[c] = c == 3 && !hasAlpha ? 255 : 128;
        }
        if (!hasAlpha) prediction[3] = 255;
    }

    private int CenterAlphabet(int kind) => AlphabetCenter + kind * 20 + costs.ActivityClass * 5;

    private void SetFlatCenter(int index, in Line line, int[] extentPrediction)
    {
        for (var c = 0; c < 4; c++)
        {
            centers[index * 4 + c] = Clamp255(line.Center(c));
            extents[index * 4 + c] = extentPrediction[c];
        }
    }

    private void EstimateNeighborhood(int bx, int by, in Line line, Span<int> neighborhood)
    {
        for (var k = 0; k < 9; k++)
        {
            int nx = Math.Clamp(bx + k % 3 - 1, 0, blocksWide - 1), ny = Math.Clamp(by + k / 3 - 1, 0, blocksHigh - 1);
            for (var c = 0; c < 4; c++)
            {
                var own = c == 3 && !hasAlpha ? 255 : Clamp255(line.Center(c));
                int value;
                if (nx == bx && ny == by)
                    value = own;
                else if (ny < by || (ny == by && nx < bx))
                    value = types[ny * blocksWide + nx] == TypeTransparent ? own : Clamp255(centers[(ny * blocksWide + nx) * 4 + c]);
                else
                {
                    var target = smoothTargets[((ny + 1) * blocksWide + nx) * 4 + c];
                    value = float.IsNaN(target) ? own : Clamp255((int)Math.Round(target));
                }
                neighborhood[k * 4 + c] = c == 3 && !hasAlpha ? 255 : value;
            }
        }
    }

    private bool NeighborsOpaque(int bx, int by)
    {
        for (var k = 0; k < 9; k++)
        {
            int nx = Math.Clamp(bx + k % 3 - 1, 0, blocksWide - 1), ny = Math.Clamp(by + k / 3 - 1, 0, blocksHigh - 1);
            if (nx == bx && ny == by)
                continue;
            var neighbor = ny * blocksWide + nx;
            if (ny < by || (ny == by && nx < bx))
            {
                if (types[neighbor] != TypeTransparent && Clamp255(centers[neighbor * 4 + 3]) != 255)
                    return false;
            }
            else if (!opaqueHalo[(ny + 1) * blocksWide + nx])
                return false;
        }
        return true;
    }

    private int TopActivity(int bx, int by)
    {
        if (by == 0)
            return FirstRowActivity;
        var up = (by - 1) * blocksWide + bx;
        var spread = 0;
        for (var c = 0; c < 3; c++)
        {
            var value = Clamp255(centers[up * 4 + c]);
            if (bx > 0) spread = Math.Max(spread, Math.Abs(value - Clamp255(centers[(up - 1) * 4 + c])));
            if (bx + 1 < blocksWide) spread = Math.Max(spread, Math.Abs(value - Clamp255(centers[(up + 1) * 4 + c])));
        }
        return ActivityClass(spread);
    }


    private double OpcodeCost(int context, int opcode) => costs.Opcode[context].Cost(opcode);

    private int CopySource(int bx, int by, int opcode)
    {
        if (opcode == OpcodeCopyLeft ? bx == 0 : by == 0)
            return -1;
        var source = by * blocksWide + bx - (opcode == OpcodeCopyLeft ? 1 : blocksWide);
        return types[source] == TypeSmooth ? -1 : source;
    }
    private void SourcePalette(int source, int[] target)
    {
        for (var c = 0; c < 4; c++)
        {
            var low = (int)((lineLows[source] >> (c * 8)) & 0xFF);
            var high = (int)((lineHighs[source] >> (c * 8)) & 0xFF);
            for (var t = 0; t < 4; t++)
                target[t * 4 + c] = (low * (3 - t) + high * t + 1) / 3;
        }
    }

    private double CopyScore(int source, int context, int opcode)
    {
        SourcePalette(source, palette);
        ComputeToneErrors(palette);
        long error;
        if (types[source] == TypeDual)
        {
            ComputeSplitToneErrors(palette);
            ComputeFineAlphaErrors(palette[3], palette[15]);
            error = 0;
            for (var p = 0; p < 16; p++)
                error += colorToneErrors[p * 4 + (int)((patterns[source] >> (p * 2)) & 3)] + fineAlphaErrors[p * 8 + FineLevel(alphaPatterns[source], alphaFines[source], p)];
        }
        else
            error = PatternError(patterns[source]);
        return error + StructurePenalty(patterns[source]) + blockLambda * OpcodeCost(context, opcode);
    }

    private void WriteCopy(int bx, int by, int source, int context, int opcode)
    {
        var index = by * blocksWide + bx;
        WriteOpcode(context, opcode);
        types[index] = types[source];
        patterns[index] = patterns[source];
        alphaPatterns[index] = alphaPatterns[source];
        alphaFines[index] = alphaFines[source];
        lineLows[index] = lineLows[source];
        lineHighs[index] = lineHighs[source];
        for (var c = 0; c < 4; c++)
        {
            extents[index * 4 + c] = extents[source * 4 + c];
            centers[index * 4 + c] = centers[source * 4 + c];
        }
        var rowBytes = blocksWide * 16;
        int sourceX = source % blocksWide, sourceY = source / blocksWide;
        for (var y = 0; y < 4; y++)
            Buffer.BlockCopy(Reconstruction, (sourceY * 4 + y) * rowBytes + sourceX * 16, Reconstruction, (by * 4 + y) * rowBytes + bx * 16, 16);
    }

    private void EncodeBlock(int bx, int by)
    {
        var index = by * blocksWide + bx;
        var lane = bx % Lanes;
        rans.Lane = lane;
        var context = OpcodeContext(bx, by);
        var centerPrediction = new int[4];
        PredictCenter(bx, by, centerPrediction);
        var activityClass = TopActivity(bx, by);
        costs.ActivityClass = activityClass;
        centerStep = centerSteps[activityClass];
        extentStep = extentSteps[activityClass];
        var extentPrediction = PredictExtent(bx, by);
        alphaPatterns[index] = 0;
        alphaFines[index] = 0;

        if (transparentBlocks[index])
        {
            var opcode = TypeTransparent;
            foreach (var copy in new[] { OpcodeCopyLeft, OpcodeCopyUp })
            {
                var source = CopySource(bx, by, copy);
                if (source >= 0 && types[source] == TypeTransparent && OpcodeCost(context, copy) < OpcodeCost(context, opcode))
                    opcode = copy;
            }
            if (opcode != TypeTransparent)
            {
                WriteCopy(bx, by, CopySource(bx, by, opcode), context, opcode);
                return;
            }
            WriteOpcode(context, TypeTransparent);
            types[index] = TypeTransparent;
            patterns[index] = 0;
            lineLows[index] = lineHighs[index] = 0;
            var carrySource = by > 0 ? index - blocksWide : bx > 0 ? index - 1 : -1;
            for (var c = 0; c < 4; c++)
            {
                centers[index * 4 + c] = c == 3 ? 0 : carrySource >= 0 ? centers[carrySource * 4 + c] : 128;
                extents[index * 4 + c] = carrySource >= 0 ? extents[carrySource * 4 + c] : 0;
            }
            StoreReconstruction(bx, by, null, 0);
            return;
        }
        for (var i = 0; i < 64; i++)
            block[i] = blockPixels[index * 64 + i];
        opaqueBlock = hasAlpha;
        transparentHole = false;
        blockHasClearAlpha = false;
        blockHasFullAlpha = false;
        for (var p = 0; p < 16; p++)
        {
            if (block[p * 4 + 3] != 255) opaqueBlock = false;
            if (block[p * 4 + 3] == 0) blockHasClearAlpha = true;
            if (block[p * 4 + 3] == 255) blockHasFullAlpha = true;
        }
        abovePattern = by > 0 ? patterns[index - blocksWide] : 0;
        for (var p = 0; p < 16; p++)
        {
            var alpha = block[p * 4 + 3];
            var transparentPixel = hasAlpha && alpha == 0;
            for (var c = 0; c < 3; c++)
                channelWeights[p * 4 + c] = (transparentPixel && premultiplied ? settings.ExactAlphaWeight : 1) * colorWeights[c];
            channelWeights[p * 4 + 3] = (hasAlpha && (alpha == 0 || alpha == 255) ? settings.ExactAlphaWeight : settings.AlphaWeight) * colorScale;
        }
        PrepareSourceLuma();
        PrepareEdgeTargets(bx, by);
        const double ContrastConstant = 58.5;
        blockLambda = baseLambda * Math.Pow((blockVariances[index] + ContrastConstant) / (meanVariance + ContrastConstant), settings.PerceptualStrength) / inheritFactors[index];

        var smoothPalette = new int[16];
        var best = SmoothChoice(bx, by, centerPrediction, extentPrediction, context, smoothPalette, out var smoothDithered);
        var mean = new double[4];
        for (var p = 0; p < 16; p++)
        for (var c = 0; c < 4; c++)
            mean[c] += block[p * 4 + c] / 16.0;
        var solid = SolidChoice(mean, centerPrediction, extentPrediction, context);
        if (solid.Score < best.Score)
            best = solid;

        if (best.Error > 0)
        {
            var low = new double[4];
            var high = new double[4];
            for (var c = 0; c < 4; c++)
            {
                low[c] = fittedCenters[index * 4 + c] - fittedExtents[index * 4 + c];
                high[c] = fittedCenters[index * 4 + c] + fittedExtents[index * 4 + c];
            }
            var line = QuantizeLine(low, high, centerPrediction, extentPrediction, false, false);
            var candidate = ChoosePattern(line, context);
            if (candidate.Score < best.Score)
                best = candidate;
            var refit = Refit(candidate, centerPrediction, extentPrediction, context, best.Score);
            if (refit.Score < best.Score)
                best = refit;
            if (settings.TableCandidates > 0)
                ConsiderTables(ref best, low, high, centerPrediction, extentPrediction, context);
            BuildPairColors(bx, by);
            if (pairCoded && best.Kind == Kind.Line)
                ConsiderPairs(ref best, context);
            if (best.Kind == Kind.Line)
                SearchWholeCodebook(ref best, context);
        }

        if (hasAlpha && best.Error > 0 && AlphaRange() >= DualAlphaRange)
        {
            var dual = DualCandidate(centerPrediction, extentPrediction, context);
            if (dual.Score < best.Score)
                best = dual;
        }

        var copyOpcode = -1;
        var copyScore = best.Score;
        foreach (var copy in new[] { OpcodeCopyLeft, OpcodeCopyUp })
        {
            var source = CopySource(bx, by, copy);
            if (source < 0 || types[source] == TypeTransparent || (types[source] == TypeSmooth && opaqueBlock && !NeighborsOpaque(bx, by)))
                continue;
            var score = CopyScore(source, context, copy);
            if (score < copyScore)
            {
                copyScore = score;
                copyOpcode = copy;
            }
        }
        if (copyOpcode >= 0)
        {
            totalScore += copyScore;
            WriteCopy(bx, by, CopySource(bx, by, copyOpcode), context, copyOpcode);
            return;
        }

        totalScore += best.Score;
        switch (best.Kind)
        {
            case Kind.Smooth:
                WriteOpcode(context, TypeSmooth);
                costs.Center(CenterSmooth).Write(rans, CenterAlphabet(CenterSmooth), best.Line.CenterResiduals, hasAlpha);
                types[index] = TypeSmooth;
                SetFlatCenter(index, best.Line, extentPrediction);
                patterns[index] = 0;
                lineLows[index] = lineHighs[index] = 0;
                StoreReconstruction(bx, by, smoothPalette, smoothDithered);
                return;
            case Kind.Solid:
                WriteOpcode(context, TypeSolid);
                costs.Center(CenterSolid).Write(rans, CenterAlphabet(CenterSolid), best.Line.CenterResiduals, hasAlpha);
                types[index] = TypeSolid;
                SetFlatCenter(index, best.Line, extentPrediction);
                patterns[index] = 0;
                LinePalette(best.Line, palette);
                StoreLowHigh(index, best.Line);
                StoreReconstruction(bx, by, palette, 0);
                return;
        }

        if (best.Dual)
        {
            WriteDual(best, context);
            types[index] = TypeDual;
            for (var c = 0; c < 4; c++)
            {
                extents[index * 4 + c] = best.Line.Extent(c);
                centers[index * 4 + c] = best.Line.Center(c);
            }
            patterns[index] = best.Pattern;
            alphaPatterns[index] = best.AlphaPattern;
            alphaFines[index] = best.AlphaFine;
            LinePalette(best.Line, palette);
            StoreLowHigh(index, best.Line);
            StoreDualReconstruction(bx, by, palette, best.Pattern, best.AlphaPattern, best.AlphaFine);
            return;
        }
        var type = TypeCodebook;
        if (best.Line.Pair > 0)
            WritePairLine(best, context);
        else if (best.Coded)
            WriteCodebook(context, best.Entry, best.Line.Tabled);
        else if (best.Binary)
        {
            WriteOpcode(context, HeadBinary + (best.Line.Tabled ? 1 : 0));
            WriteBinary(best.Pattern);
        }
        else
        {
            WriteOpcode(context, HeadLiteral + (best.Line.Tabled ? 1 : 0));
            WritePattern(best.Pattern);
        }
        if (best.Line.Pair > 0)
        {
        }
        else if (best.Line.Tabled)
        {
            costs.TableCenter.Write(rans, AlphabetTableCenter, best.Line.CenterResiduals, hasAlpha);
            costs.Table[costs.ActivityClass].Update(best.Line.TableIndex);
            rans.Write(AlphabetTable + costs.ActivityClass, best.Line.TableIndex);
            if (hasAlpha)
            {
                var bucket = SignedBucket(best.Line.X3, out var extraBits, out var extra);
                costs.TableAlpha.Update(bucket);
                rans.Write(AlphabetTableAlpha, bucket);
                rans.WriteBits(extra, extraBits);
            }
        }
        else
        {
            costs.Center(CenterLine).Write(rans, CenterAlphabet(CenterLine), best.Line.CenterResiduals, hasAlpha);
            costs.Extent.Write(rans, AlphabetExtent + costs.ActivityClass * 5, best.Line.ExtentResiduals, hasAlpha);
        }
        types[index] = (byte)type;
        for (var c = 0; c < 4; c++)
        {
            extents[index * 4 + c] = best.Line.Extent(c);
            centers[index * 4 + c] = best.Line.Center(c);
        }
        patterns[index] = best.Pattern;
        LinePalette(best.Line, palette);
        StoreLowHigh(index, best.Line);
        StoreReconstruction(bx, by, palette, best.Pattern);
    }

    private Choice SmoothChoice(int bx, int by, int[] centerPrediction, int[] extentPrediction, int context, int[] smoothPalette, out uint ditheredTones)
    {
        var target = new double[4];
        var at = ((by + 1) * blocksWide + bx) * 4;
        for (var c = 0; c < 4; c++)
            target[c] = smoothTargets[at + c];
        if (!hasAlpha) target[3] = 255;
        var line = QuantizeLine(target, target, centerPrediction, extentPrediction, true, false);
        Span<int> neighborhood = stackalloc int[36];
        Span<int> points = stackalloc int[16];
        EstimateNeighborhood(bx, by, line, neighborhood);
        QtxField.SmoothPoints(neighborhood, points);
        var low = new int[4];
        var high = new int[4];
        QtxField.Derive(points, channels, low, high, out var tones, out ditheredTones);
        EndpointPalette(low, high, smoothPalette);
        ComputeToneErrors(smoothPalette);
        var choice = new Choice { Kind = Kind.Smooth, Line = line, Pattern = tones, Error = PatternError(tones), Penalty = StructurePenalty(tones) * settings.SmoothStructureScale };
        choice.Cost = OpcodeCost(context, TypeSmooth) + costs.Center(CenterSmooth).Cost(line.CenterResiduals, hasAlpha);
        choice.Score = choice.Error + choice.Penalty + blockLambda * choice.Cost;
        if (opaqueBlock && !NeighborsOpaque(bx, by))
            choice.Score = double.MaxValue;
        return choice;
    }
    private Choice SolidChoice(double[] mean, int[] prediction, int[] extentPrediction, int context)
    {
        var line = QuantizeLine(mean, mean, prediction, extentPrediction, true, true);
        LinePalette(line, palette);
        ComputeToneErrors(palette);
        var choice = new Choice { Kind = Kind.Solid, Line = line, Error = PatternError(0), Penalty = StructurePenalty(0) };
        choice.Cost = OpcodeCost(context, TypeSolid) + costs.Center(CenterSolid).Cost(line.CenterResiduals, hasAlpha);
        choice.Score = choice.Error + choice.Penalty + blockLambda * choice.Cost;
        return choice;
    }
    private readonly uint[] lineLows, lineHighs;
    private bool blockHasClearAlpha, blockHasFullAlpha;

    private int[] PredictExtent(int bx, int by)
    {
        var prediction = new int[4];
        var index = by * blocksWide + bx;
        var left = leftMode && bx > 0;
        for (var c = 0; c < 4; c++)
        {
            if (left && by > 0)
                prediction[c] = Med(extents[(index - 1) * 4 + c], extents[(index - blocksWide) * 4 + c], extents[(index - blocksWide - 1) * 4 + c]);
            else if (left)
                prediction[c] = extents[(index - 1) * 4 + c];
            else if (by > 0)
                prediction[c] = (extents[(index - blocksWide - (bx > 0 ? 1 : 0)) * 4 + c] + 2 * extents[(index - blocksWide) * 4 + c] + extents[(index - blocksWide + (bx + 1 < blocksWide ? 1 : 0)) * 4 + c] + 2) >> 2;
        }
        if (!hasAlpha) prediction[3] = 0;
        return prediction;
    }
    private Choice Refit(in Choice from, int[] centerPrediction, int[] extentPrediction, int context, double ceiling)
    {
        double a = 0, b = 0, cc = 0;
        var pattern = from.Pattern;
        for (var p = 0; p < 16; p++)
        {
            var w = ((pattern >> (p * 2)) & 3) / 3.0;
            a += (1 - w) * (1 - w);
            b += w * (1 - w);
            cc += w * w;
        }
        var determinant = a * cc - b * b;
        if (determinant < 1e-6)
            return new Choice { Score = double.MaxValue };
        var low = new double[4];
        var high = new double[4];
        for (var c = 0; c < 4; c++)
        {
            double d1 = 0, d2 = 0;
            for (var p = 0; p < 16; p++)
            {
                var w = ((pattern >> (p * 2)) & 3) / 3.0;
                d1 += (1 - w) * block[p * 4 + c];
                d2 += w * block[p * 4 + c];
            }
            low[c] = (cc * d1 - b * d2) / determinant;
            high[c] = (a * d2 - b * d1) / determinant;
        }
        var line = QuantizeLine(low, high, centerPrediction, extentPrediction, false, false);
        if (from.Binary || from.Coded)
        {
            LinePalette(line, palette);
            ComputeToneErrors(palette);
            var refit = from;
            refit.Line = line;
            refit.Error = PatternError(pattern);
            refit.Penalty = StructurePenalty(pattern);
            refit.Cost = from.Cost - LineCost(from.Line) + LineCost(line);
            refit.Score = refit.Error + refit.Penalty + blockLambda * refit.Cost;
            return refit;
        }
        return ChoosePattern(line, context, ceiling);
    }

    private double LineCost(in Line line) => line.Pair > 0 ? costs.Pair.Cost(line.Pair) : line.Tabled
        ? costs.TableCenter.Cost(line.CenterResiduals, hasAlpha) + costs.Table[costs.ActivityClass].Cost(line.TableIndex) + (hasAlpha ? SignedModelCost(costs.TableAlpha, line.X3) : 0)
        : costs.Center(CenterLine).Cost(line.CenterResiduals, hasAlpha) + costs.Extent.Cost(line.ExtentResiduals, hasAlpha);

    private static double SignedModelCost(AdaptiveModel model, int value)
    {
        var bucket = SignedBucket(value, out var extraBits, out _);
        return model.Cost(bucket) + extraBits;
    }

    private static int NearestFive(double value)
    {
        var guess = Math.Clamp((int)Math.Round(value * 31 / 255.0), 0, 31);
        var best = guess;
        for (var q = Math.Max(0, guess - 1); q <= Math.Min(31, guess + 1); q++)
            if (Math.Abs(((q << 3) | (q >> 2)) - value) < Math.Abs(((best << 3) | (best >> 2)) - value))
                best = q;
        return best;
    }

    public static int FiveOf(int value) => (value * 31 + 127) / 255;

    private Line TableLine(double[] targetLow, double[] targetHigh, int table, int[] centerPrediction, int[] extentPrediction)
    {
        var line = QuantizeLine(targetLow, targetHigh, centerPrediction, extentPrediction, false, false);
        line.Tabled = true;
        line.TableIndex = table;
        var green = NearestFive((targetLow[1] + targetHigh[1]) / 2);
        line.R1 = green - FiveOf(centerPrediction[1]);
        line.C1 = (green << 3) | (green >> 2);
        foreach (var c in new[] { 0, 2 })
        {
            var basis = Math.Clamp(FiveOf(centerPrediction[c]) + line.R1, 0, 31);
            var q = NearestFive((targetLow[c] + targetHigh[c]) / 2);
            var center = (q << 3) | (q >> 2);
            if (c == 0) { line.R0 = q - basis; line.C0 = center; } else { line.R2 = q - basis; line.C2 = center; }
        }
        line.E0 = line.E1 = line.E2 = TableExtents[table];
        line.X0 = line.X1 = line.X2 = 0;
        return line;
    }

    private void ConsiderTables(ref Choice best, double[] low, double[] high, int[] centerPrediction, int[] extentPrediction, int context)
    {
        var luma = 0.0;
        for (var c = 0; c < 3; c++) luma += (high[c] - low[c]) / 6.0;
        var chroma = 0.0;
        for (var c = 0; c < 3; c++) chroma = Math.Max(chroma, Math.Abs((high[c] - low[c]) / 2.0 - luma));
        if (luma < 0) { (low, high) = (high, low); luma = -luma; }
        if (chroma > Math.Max(6, luma * 0.5))
            return;
        var order = new int[TableSymbols];
        for (var t = 0; t < TableSymbols; t++) order[t] = t;
        Array.Sort(order, (x, y) => Math.Abs(TableExtents[x] - luma).CompareTo(Math.Abs(TableExtents[y] - luma)));
        for (var k = 0; k < settings.TableCandidates; k++)
        {
            var candidate = ChoosePattern(TableLine(low, high, order[k], centerPrediction, extentPrediction), context, best.Score);
            if (candidate.Score < best.Score)
                best = candidate;
        }
    }




    private Choice ChoosePattern(in Line line, int context, double ceiling = double.MaxValue)
    {
        LinePalette(line, palette);
        ComputeToneErrors(palette);
        var ideal = IdealPattern(out var idealError);
        var lambda = blockLambda;
        var lineCost = LineCost(line);
        if (idealError + lambda * lineCost >= ceiling)
            return new Choice { Score = double.MaxValue };
        var literal = RateDistortionLiteral(lambda);
        var literalError = PatternError(literal);
        var tableOffset = line.Tabled ? 1 : 0;
        var literalHead = line.Pair > 0 ? OpcodeCost(context, HeadPair) + costs.PairPattern.Cost(CodeBuckets) : OpcodeCost(context, HeadLiteral + tableOffset);
        var choice = new Choice { Kind = Kind.Line, Line = line, Pattern = literal, Error = literalError, Penalty = StructurePenalty(literal), Cost = lineCost + literalHead + LiteralCost(literal) };
        choice.Score = choice.Error + choice.Penalty + lambda * choice.Cost;

        uint binary = 0;
        long binaryError = 0;
        for (var p = 0; p < 16; p++)
        {
            var high = toneErrors[p * 4 + 3] < toneErrors[p * 4];
            binary |= high ? 3u << (p * 2) : 0;
            binaryError += toneErrors[p * 4 + (high ? 3 : 0)];
        }
        var binaryPenalty = StructurePenalty(binary);
        var binaryCost = lineCost + OpcodeCost(context, HeadBinary + tableOffset) + MaskCost(binary);
        if (leftMode && line.Pair == 0 && binaryError + binaryPenalty + lambda * binaryCost < choice.Score)
            choice = new Choice { Kind = Kind.Line, Line = line, Pattern = binary, Error = binaryError, Penalty = binaryPenalty, Cost = binaryCost, Score = binaryError + binaryPenalty + lambda * binaryCost, Binary = true };


        ConsiderCodebook(ref choice, idealError, lineCost, context, line.Tabled ? HeadTable : HeadCode, line.Pair > 0, false, lambda);
        return choice;
    }

    private void SearchWholeCodebook(ref Choice best, int context)
    {
        LinePalette(best.Line, palette);
        ComputeToneErrors(palette);
        IdealPattern(out var idealError);
        ConsiderCodebook(ref best, idealError, LineCost(best.Line), context, best.Line.Tabled ? HeadTable : HeadCode, best.Line.Pair > 0, true, blockLambda);
    }

    private const int RowEntryLimit = 16;

    private void ConsiderCodebook(ref Choice choice, long idealError, double codebookBase, int context, int head, bool paired, bool wholeCodebook, double lambda)
    {
        var bucketFloor = double.MaxValue;
        var pairHead = paired ? OpcodeCost(context, HeadPair) : 0;
        for (var bucket = 0; bucket < CodeBuckets; bucket++)
        {
            codebookCosts[bucket] = (paired ? pairHead + costs.PairPattern.Cost(bucket) : OpcodeCost(context, head + bucket)) + CodeBits[bucket];
            bucketFloor = Math.Min(bucketFloor, codebookCosts[bucket]);
        }
        if (idealError + lambda * (codebookBase + bucketFloor) >= choice.Score)
            return;
        BuildRowErrors();
        var ideal = IdealPattern(out _);
        var stamp = ++codebookStamp;
        for (var row = 0; row < 4; row++)
        {
            var value = (int)((ideal >> (row * 8)) & 0xFF);
            for (var flip = 0; flip < 2; flip++)
            {
                var entries = ShapeCodebook.RowEntries(row, flip == 0 ? value : value ^ 0xFF);
                var count = Math.Min(RowEntryLimit, entries.Length);
                for (var i = 0; i < count; i++)
                    ConsiderEntry(ref choice, entries[i], stamp, codebookBase, lambda);
            }
        }
        if (!wholeCodebook)
        {
            for (var entry = 0; entry < ShapeCodebook.CommonEntries; entry++)
                ConsiderEntry(ref choice, entry, stamp, codebookBase, lambda);
            return;
        }
        ScanCodebook(ref choice, stamp, codebookBase, bucketFloor, lambda);
    }

    private void ScanCodebook(ref Choice choice, int stamp, double codebookBase, double bucketFloor, double lambda)
    {
        long smallestSecond = SmallestRowError(1), smallestThird = SmallestRowError(2), smallestFourth = SmallestRowError(3);
        var floor = lambda * (codebookBase + bucketFloor);
        var patterns = ShapeCodebook.Patterns;
        var errors = rowErrors;
        var bound = choice.Score - floor - smallestFourth;
        var entries = ShapeCodebookByFirstRow.Entries;
        var groupStarts = ShapeCodebookByFirstRow.GroupStarts;

        for (var value = 0; value < 256; value++)
        {
            if (errors[value] + smallestSecond + smallestThird >= bound && errors[value ^ 255] + smallestSecond + smallestThird >= bound)
            {
                continue;
            }

            for (var index = groupStarts[value]; index < groupStarts[value + 1]; index++)
            {
                var entry = entries[index];
                var pattern = patterns[entry];
                int first = (int)(pattern & 255), second = 256 + (int)((pattern >> 8) & 255), third = 512 + (int)((pattern >> 16) & 255);
                long straight = errors[first] + errors[second], inverted = errors[first ^ 255] + errors[second ^ 255];

                if (straight + smallestThird >= bound && inverted + smallestThird >= bound)
                {
                    continue;
                }

                int fourth = 768 + (int)(pattern >> 24);

                if (straight + errors[third] + errors[fourth] >= bound + smallestFourth && inverted + errors[third ^ 255] + errors[fourth ^ 255] >= bound + smallestFourth)
                {
                    continue;
                }

                ConsiderEntry(ref choice, entry, stamp, codebookBase, lambda);
                bound = choice.Score - floor - smallestFourth;
            }
        }
    }

    private long SmallestRowError(int row)
    {
        var smallest = long.MaxValue;
        for (var value = row * 256; value < row * 256 + 256; value++)
            smallest = Math.Min(smallest, rowErrors[value]);
        return smallest;
    }

    private void ConsiderEntry(ref Choice choice, int entry, int stamp, double codebookBase, double lambda)
    {
        if (codebookSeen[entry] == stamp)
            return;
        codebookSeen[entry] = stamp;
        var pattern = ShapeCodebook.Patterns[entry];
        for (var flip = 0; flip < 2; flip++)
        {
            var oriented = flip == 0 ? pattern : ~pattern;
            var code = entry * 2 + flip;
            var cost = codebookBase + codebookCosts[bucketOfCode[code]];
            var error = rowErrors[oriented & 255] + rowErrors[256 + ((oriented >> 8) & 255)] + rowErrors[512 + ((oriented >> 16) & 255)] + rowErrors[768 + (oriented >> 24)];
            if (error + lambda * cost >= choice.Score)
                continue;
            var penalty = StructurePenalty(oriented);
            if (error + penalty + lambda * cost >= choice.Score)
                continue;
            choice.Pattern = oriented;
            choice.Binary = false;
            choice.Coded = true;
            choice.Entry = code;
            choice.Error = error;
            choice.Penalty = penalty;
            choice.Cost = cost;
            choice.Score = error + penalty + lambda * cost;
        }
    }

    private void WritePairLine(in Choice choice, int context)
    {
        WriteOpcode(context, HeadPair);
        if (choice.Coded)
        {
            var bucket = bucketOfCode[choice.Entry];
            costs.PairPattern.Update(bucket);
            rans.Write(AlphabetPairPattern, bucket);
            rans.WriteBits((uint)(choice.Entry - CodeStarts[bucket]), CodeBits[bucket]);
        }
        else
        {
            costs.PairPattern.Update(CodeBuckets);
            rans.Write(AlphabetPairPattern, CodeBuckets);
            WritePattern(choice.Pattern);
        }
        costs.Pair.Update(choice.Line.Pair);
        rans.Write(AlphabetPair, choice.Line.Pair);
    }

    private void AddPairNeighbour(bool present, int neighbour)
    {
        if (!present || types[neighbour] < TypeSolid)
            return;
        pairColors[pairCount++] = lineLows[neighbour];
        if (types[neighbour] != TypeSolid)
            pairColors[pairCount++] = lineHighs[neighbour];
    }

    private void BuildPairColors(int bx, int by)
    {
        pairCount = 0;
        pairCoded = false;
        if (!copyEnabled)
            return;
        var index = by * blocksWide + bx;
        AddPairNeighbour(bx > 0, index - 1);
        AddPairNeighbour(by > 0, index - blocksWide);
        AddPairNeighbour(by > 0 && bx > 0, index - blocksWide - 1);
        AddPairNeighbour(by > 0 && bx + 1 < blocksWide, index - blocksWide + 1);
        pairCoded = pairCount > 0;
        if (hasAlpha)
            pairColors[pairCount++] = 0;
        for (var i = 0; i < pairCount; i++)
        {
            pairSlotUsable[i] = true;
            for (var k = 0; k < i && pairSlotUsable[i]; k++)
                if (pairColors[k] == pairColors[i])
                    pairSlotUsable[i] = false;
        }
    }

    private Line PairLine(int i, int j)
    {
        var line = new Line { Pair = 1 + i * (PairSlots - 1) + (j > i ? j - 1 : j) };
        for (var c = 0; c < 4; c++)
        {
            var low = (int)((pairColors[i] >> (c * 8)) & 0xFF);
            var high = (int)((pairColors[j] >> (c * 8)) & 0xFF);
            var center = (low + high) >> 1;
            switch (c)
            {
                case 0: line.C0 = center; line.E0 = high - center; break;
                case 1: line.C1 = center; line.E1 = high - center; break;
                case 2: line.C2 = center; line.E2 = high - center; break;
                default: line.C3 = center; line.E3 = high - center; break;
            }
        }
        if (!hasAlpha) { line.C3 = 255; line.E3 = 0; }
        return line;
    }

    private long PairError(uint first, uint second)
    {
        Span<int> tones = stackalloc int[16];
        for (var c = 0; c < 4; c++)
        {
            var low = (int)((first >> (c * 8)) & 0xFF);
            var high = (int)((second >> (c * 8)) & 0xFF);
            for (var t = 0; t < 4; t++)
                tones[t * 4 + c] = (low * (3 - t) + high * t + 1) / 3;
        }
        var channels = hasAlpha ? 4 : 3;
        long total = 0;
        for (var p = 0; p < 16; p++)
        {
            var best = long.MaxValue;
            for (var t = 0; t < 4; t++)
            {
                long error = 0;
                for (var c = 0; c < channels; c++)
                {
                    var d = block[p * 4 + c] - tones[t * 4 + c];
                    error += channelWeights[p * 4 + c] * d * d;
                }
                if (error < best)
                    best = error;
            }
            total += best;
        }
        return total;
    }

    private void ConsiderPairs(ref Choice best, int context)
    {
        for (var p = 0; p < 16; p++)
        {
            for (var c = 0; c < 4; c++)
            {
                channelPlanes[c * 16 + p] = block[p * 4 + c];
                weightPlanes[c * 16 + p] = channelWeights[p * 4 + c];
            }
        }
        var closest = -1;
        var closestError = long.MaxValue;
        for (var i = 0; i < pairCount; i++)
        {
            for (var j = i + 1; j < pairCount; j++)
            {
                if (pairSlotUsable[i] == false || pairSlotUsable[j] == false)
                {
                    continue;
                }
                var error = PairError(pairColors[i], pairColors[j]);
                if (error < closestError)
                {
                    closest = (i << 4) | j;
                    closestError = error;
                }
            }
        }
        if (closest < 0)
        {
            return;
        }
        var forward = PairLine(closest >> 4, closest & 15);
        var backward = PairLine(closest & 15, closest >> 4);
        var candidate = ChoosePattern(costs.Pair.Cost(forward.Pair) <= costs.Pair.Cost(backward.Pair) ? forward : backward, context, best.Score);
        if (candidate.Score < best.Score)
        {
            best = candidate;
        }
    }

    private void WriteCodebook(int context, int code, bool tabled)
    {
        var bucket = bucketOfCode[code];
        WriteOpcode(context, (tabled ? HeadTable : HeadCode) + bucket);
        rans.WriteBits((uint)(code - CodeStarts[bucket]), CodeBits[bucket]);
    }

    private void BuildRowErrors()
    {
        Span<long> pairs = stackalloc long[128];
        for (var pair = 0; pair < 8; pair++)
        for (var nibble = 0; nibble < 16; nibble++)
            pairs[pair * 16 + nibble] = toneErrors[pair * 8 + (nibble & 3)] + toneErrors[pair * 8 + 4 + (nibble >> 2)];
        for (var row = 0; row < 4; row++)
        for (var value = 0; value < 256; value++)
            rowErrors[row * 256 + value] = pairs[row * 32 + (value & 15)] + pairs[row * 32 + 16 + (value >> 4)];
    }

    private uint RateDistortionLiteral(double lambda)
    {
        uint pattern = 0;
        Span<int> best = stackalloc int[4];
        Span<int> second = stackalloc int[4];
        for (var row = 0; row < 4; row++)
        {
            for (var x = 0; x < 4; x++)
            {
                var p = row * 4 + x;
                int first = 0, runner = 1;
                for (var t = 1; t < 4; t++)
                {
                    if (toneErrors[p * 4 + t] < toneErrors[p * 4 + first]) { runner = first; first = t; }
                    else if (t != first && (runner == first || toneErrors[p * 4 + t] < toneErrors[p * 4 + runner])) runner = t;
                }
                best[x] = first;
                second[x] = runner;
            }
            var predicted = row == 0 ? abovePattern >> 24 : (pattern >> ((row - 1) * 8)) & 0xFF;
            var bestScore = double.MaxValue;
            uint bestRow = 0;
            for (var mask = 0; mask < 16; mask++)
            {
                uint value = 0;
                long error = 0;
                for (var x = 0; x < 4; x++)
                {
                    var tone = (mask >> x & 1) != 0 ? second[x] : best[x];
                    value |= (uint)tone << (x * 2);
                    error += toneErrors[(row * 4 + x) * 4 + tone];
                }
                var residual = (int)(QtxBits.SubtractTones(value, predicted) & 0xFF);
                var score = error + lambda * costs.Row[RowContext(row, pattern) + row * 2].Cost(residual);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestRow = value;
                }
            }
            pattern |= bestRow << (row * 8);
        }
        return pattern;
    }

    private uint RowResiduals(uint pattern)
    {
        var predicted = (pattern << 8) | (abovePattern >> 24);
        return QtxBits.SubtractTones(pattern, predicted);
    }

    private int RowContext(int row, uint pattern)
    {
        if (row == 0)
            return abovePattern == 0 ? 1 : 0;
        var residuals = RowResiduals(pattern);
        return ((residuals >> ((row - 1) * 8)) & 0xFF) == 0 ? 1 : 0;
    }

    private double LiteralCost(uint pattern)
    {
        var residuals = RowResiduals(pattern);
        var cost = 0.0;
        for (var row = 0; row < 4; row++)
            cost += costs.Row[RowContext(row, pattern) + row * 2].Cost((int)((residuals >> (row * 8)) & 0xFF));
        return cost;
    }

    private double MaskCost(uint pattern)
    {
        var mask = QtxBits.CompactEven(pattern);
        return costs.Mask[0].Cost((int)(mask & 0xFF)) + costs.Mask[1].Cost((int)(mask >> 8));
    }

    private void WriteOpcode(int context, int opcode)
    {
        costs.Opcode[context].Update(opcode);
        rans.Write(AlphabetOpcode + context, opcode);
    }

    private void WriteBinary(uint pattern)
    {
        var mask = QtxBits.CompactEven(pattern);
        for (var i = 0; i < 2; i++)
        {
            var symbol = (int)((mask >> (i * 8)) & 0xFF);
            costs.Mask[i].Update(symbol);
            rans.Write(AlphabetMask + i, symbol);
        }
    }

    private void WritePattern(uint pattern)
    {
        var residuals = RowResiduals(pattern);
        for (var row = 0; row < 4; row++)
        {
            var symbol = (int)((residuals >> (row * 8)) & 0xFF);
            var model = RowContext(row, pattern) + row * 2;
            costs.Row[model].Update(symbol);
            rans.Write(AlphabetRow + model, symbol);
        }
    }

    public static int SignedBucket(int value, out int extraBits, out uint extra)
    {
        if (value == 0) { extraBits = 0; extra = 0; return 0; }
        var magnitude = (uint)Math.Abs(value);
        var log = Log2(magnitude);
        extraBits = log;
        extra = magnitude - (1u << log);
        return 1 + log * 2 + (value < 0 ? 1 : 0);
    }

    public static int UnsignedBucket(int value, out int extraBits, out uint extra)
    {
        if (value == 0) { extraBits = 0; extra = 0; return 0; }
        var log = Log2((uint)value);
        extraBits = log;
        extra = (uint)value - (1u << log);
        return log + 1;
    }

    private static int Log2(uint value)
    {
        var log = 0;
        while ((value >>= 1) != 0) log++;
        return log;
    }

    private const int DualAlphaRange = 24;
    private readonly long[] colorToneErrors = new long[64];
    private readonly long[] alphaToneErrors = new long[64];
    private readonly long[] fineAlphaErrors = new long[128];

    private int AlphaRange()
    {
        int low = 255, high = 0;
        for (var p = 0; p < 16; p++)
        {
            low = Math.Min(low, block[p * 4 + 3]);
            high = Math.Max(high, block[p * 4 + 3]);
        }
        return high - low;
    }

    private void ComputeSplitToneErrors(int[] tonePalette)
    {
        for (var p = 0; p < 16; p++)
        for (var t = 0; t < 4; t++)
        {
            long color = 0, alpha = 0;
            for (var c = 0; c < 4; c++)
            {
                long d = tonePalette[t * 4 + c] - block[p * 4 + c];
                var error = d * d * channelWeights[p * 4 + c];
                if (d * d > settings.OutlierThreshold * settings.OutlierThreshold)
                    error += (d * d - settings.OutlierThreshold * settings.OutlierThreshold) * settings.OutlierWeight;
                for (var k = 0; k < edgeCounts[p]; k++)
                {
                    long e = tonePalette[t * 4 + c] - edgeTargets[(p * 2 + k) * 4 + c];
                    error += (e * e) >> settings.EdgeShift;
                }
                if (c < 3) color += error; else alpha += error;
            }
            colorToneErrors[p * 4 + t] = color;
            alphaToneErrors[p * 4 + t] = alpha;
        }
    }

    private Choice DualCandidate(int[] linePrediction, int[] extentPrediction, int context)
    {
        var pixels = new int[64];
        for (var p = 0; p < 16; p++)
            for (var c = 0; c < 3; c++)
                pixels[p * 4 + c] = block[p * 4 + c];
        var center = new double[4];
        var extent = new double[4];
        FitLine(pixels, center, extent);
        var low = new double[4];
        var high = new double[4];
        for (var c = 0; c < 3; c++)
        {
            low[c] = center[c] - extent[c];
            high[c] = center[c] + extent[c];
        }
        low[3] = 255;
        high[3] = 0;
        for (var p = 0; p < 16; p++)
        {
            low[3] = Math.Min(low[3], block[p * 4 + 3]);
            high[3] = Math.Max(high[3], block[p * 4 + 3]);
        }
        var line = QuantizeLine(low, high, linePrediction, extentPrediction, false, false);
        LinePalette(line, palette);
        ComputeToneErrors(palette);
        ComputeSplitToneErrors(palette);
        Array.Copy(colorToneErrors, toneErrors, 64);
        var lambda = blockLambda;
        var colorPattern = RateDistortionLiteral(lambda);
        long error = 0;
        for (var p = 0; p < 16; p++)
            error += colorToneErrors[p * 4 + (int)((colorPattern >> (p * 2)) & 3)];
        ComputeFineAlphaErrors(palette[3], palette[15]);
        uint alphaPattern = 0, alphaFine = 0;
        var alphaCost = 0.0;
        for (var row = 0; row < 4; row++)
        {
            var colorRow = (colorPattern >> (row * 8)) & 0xFFu;
            uint idealCoarse = 0, idealFine = 0, followFine = 0;
            long idealError = 0, followError = 0;
            for (var x = 0; x < 4; x++)
            {
                var p = row * 4 + x;
                var bestLevel = 0;
                for (var level = 1; level < 8; level++)
                    if (fineAlphaErrors[p * 8 + level] < fineAlphaErrors[p * 8 + bestLevel]) bestLevel = level;
                idealCoarse |= (uint)(bestLevel >> 1) << (x * 2);
                idealFine |= (uint)(bestLevel & 1) << x;
                idealError += fineAlphaErrors[p * 8 + bestLevel];
                var coarse = (int)((colorRow >> (x * 2)) & 3);
                var upper = fineAlphaErrors[p * 8 + coarse * 2 + 1] < fineAlphaErrors[p * 8 + coarse * 2] ? 1 : 0;
                followFine |= (uint)upper << x;
                followError += fineAlphaErrors[p * 8 + coarse * 2 + upper];
            }
            var bestScore = double.MaxValue;
            uint bestCoarse = 0, bestFine = 0;
            long bestRowError = 0;
            var bestRowCost = 0.0;
            foreach (var (coarseRow, fineRow, rowError) in new[] { (idealCoarse, idealFine, idealError), (colorRow, followFine, followError) })
            {
                var rowCost = costs.AlphaRow.Cost((int)(QtxBits.SubtractTones(coarseRow, colorRow) & 0xFF)) + costs.AlphaFine.Cost((int)fineRow);
                var score = rowError + lambda * rowCost;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestCoarse = coarseRow;
                    bestFine = fineRow;
                    bestRowError = rowError;
                    bestRowCost = rowCost;
                }
            }
            alphaPattern |= bestCoarse << (row * 8);
            alphaFine |= bestFine << (row * 4);
            error += bestRowError;
            alphaCost += bestRowCost;
        }
        var choice = new Choice { Kind = Kind.Line, Line = line, Pattern = colorPattern, AlphaPattern = alphaPattern, AlphaFine = alphaFine, Dual = true, Error = error, Penalty = StructurePenalty(colorPattern) };
        choice.Cost = OpcodeCost(context, HeadDual) + costs.Center(CenterLine).Cost(line.CenterResiduals, hasAlpha) + costs.Extent.Cost(line.ExtentResiduals, hasAlpha) + LiteralCost(colorPattern) + alphaCost;
        choice.Score = choice.Error + choice.Penalty + lambda * choice.Cost;
        return choice;
    }

    private void WriteDual(in Choice choice, int context)
    {
        WriteOpcode(context, HeadDual);
        costs.Center(CenterLine).Write(rans, CenterAlphabet(CenterLine), choice.Line.CenterResiduals, hasAlpha);
        costs.Extent.Write(rans, AlphabetExtent + costs.ActivityClass * 5, choice.Line.ExtentResiduals, hasAlpha);
        WritePattern(choice.Pattern);
        for (var row = 0; row < 4; row++)
        {
            var residual = (int)(QtxBits.SubtractTones((choice.AlphaPattern >> (row * 8)) & 0xFFu, (choice.Pattern >> (row * 8)) & 0xFFu) & 0xFF);
            costs.AlphaRow.Update(residual);
            rans.Write(AlphabetAlphaRow, residual);
        }
        for (var row = 0; row < 4; row++)
        {
            var fine = (int)((choice.AlphaFine >> (row * 4)) & 15);
            costs.AlphaFine.Update(fine);
            rans.Write(AlphabetAlphaFine, fine);
        }
    }

    public static int FineAlphaLevel(int low, int high, int level) => ((low * (7 - level) + high * level + 3) * 9363) >> 16;

    private static int FineLevel(uint alphaPattern, uint alphaFine, int pixel) => (int)((((alphaPattern >> (pixel * 2)) & 3) << 1) | ((alphaFine >> pixel) & 1));

    private void ComputeFineAlphaErrors(int low, int high)
    {
        var threshold = settings.OutlierThreshold * settings.OutlierThreshold;
        for (var level = 0; level < 8; level++)
        {
            var value = FineAlphaLevel(low, high, level);
            for (var p = 0; p < 16; p++)
            {
                long d = value - block[p * 4 + 3];
                var error = d * d * channelWeights[p * 4 + 3];
                if (d * d > threshold)
                    error += (d * d - threshold) * settings.OutlierWeight;
                for (var k = 0; k < edgeCounts[p]; k++)
                {
                    long e = value - edgeTargets[(p * 2 + k) * 4 + 3];
                    error += (e * e) >> settings.EdgeShift;
                }
                fineAlphaErrors[p * 8 + level] = (error + alphaToneErrors[p * 4 + (level >> 1)] * 2) / 3;
            }
        }
    }

    private void StoreDualReconstruction(int bx, int by, int[] tonePalette, uint colorPattern, uint alphaPattern, uint alphaFine)
    {
        var rowBytes = blocksWide * 16;
        for (var p = 0; p < 16; p++)
        {
            var at = (by * 4 + (p >> 2)) * rowBytes + (bx * 4 + (p & 3)) * 4;
            var tone = (int)((colorPattern >> (p * 2)) & 3);
            for (var c = 0; c < 3; c++)
                Reconstruction[at + c] = (byte)tonePalette[tone * 4 + c];
            Reconstruction[at + 3] = (byte)FineAlphaLevel(tonePalette[3], tonePalette[15], FineLevel(alphaPattern, alphaFine, p));
        }
    }

    private void StoreReconstruction(int bx, int by, int[] tonePalette, uint pattern)
    {
        var rowBytes = blocksWide * 16;
        for (var p = 0; p < 16; p++)
        {
            var at = (by * 4 + (p >> 2)) * rowBytes + (bx * 4 + (p & 3)) * 4;
            var tone = (int)((pattern >> (p * 2)) & 3);
            for (var c = 0; c < 4; c++)
                Reconstruction[at + c] = tonePalette == null ? (byte)0 : (byte)tonePalette[tone * 4 + c];
        }
    }
    private double LumaVariance(int[] pixels)
    {
        double sum = 0, squares = 0;
        for (var p = 0; p < 16; p++)
        {
            var alpha = hasAlpha ? pixels[p * 4 + 3] / 255.0 : 1.0;
            var luma = (pixels[p * 4] * 0.2126 + pixels[p * 4 + 1] * 0.7152 + pixels[p * 4 + 2] * 0.0722) * (premultiplied ? 1.0 : alpha);
            sum += luma;
            squares += luma * luma;
        }
        var mean = sum / 16;
        return Math.Max(0, squares / 16 - mean * mean);
    }

    private static void FitLine(int[] pixels, double[] center, double[] extent)
    {
        var mean = new double[4];
        for (var p = 0; p < 16; p++)
        for (var c = 0; c < 4; c++)
            mean[c] += pixels[p * 4 + c] / 16.0;
        var covariance = new double[4, 4];
        for (var p = 0; p < 16; p++)
        for (var i = 0; i < 4; i++)
        for (var j = i; j < 4; j++)
            covariance[i, j] += (pixels[p * 4 + i] - mean[i]) * (pixels[p * 4 + j] - mean[j]);
        for (var i = 0; i < 4; i++)
        for (var j = 0; j < i; j++)
            covariance[i, j] = covariance[j, i];
        var axis = new double[] { 1, 1, 1, 1 };
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var next = new double[4];
            for (var i = 0; i < 4; i++)
            for (var j = 0; j < 4; j++)
                next[i] += covariance[i, j] * axis[j];
            var length = Math.Sqrt(next[0] * next[0] + next[1] * next[1] + next[2] * next[2] + next[3] * next[3]);
            if (length < 1e-9) break;
            for (var i = 0; i < 4; i++) axis[i] = next[i] / length;
        }
        if (axis[0] + 2 * axis[1] + axis[2] + axis[3] < 0)
            for (var i = 0; i < 4; i++) axis[i] = -axis[i];
        double low = double.MaxValue, high = double.MinValue;
        for (var p = 0; p < 16; p++)
        {
            double t = 0;
            for (var c = 0; c < 4; c++) t += (pixels[p * 4 + c] - mean[c]) * axis[c];
            low = Math.Min(low, t);
            high = Math.Max(high, t);
        }
        var mid = (low + high) / 2;
        var half = (high - low) / 2;
        for (var c = 0; c < 4; c++)
        {
            center[c] = mean[c] + axis[c] * mid;
            extent[c] = axis[c] * half;
        }
    }

    private class JointCost
    {
        public void MergeFrom(JointCost[] others)
        {
            joint.MergeFrom(Array.ConvertAll(others, o => o.joint));
            for (var i = 0; i < 3; i++) buckets[i].MergeFrom(Array.ConvertAll(others, o => o.buckets[i]));
            alpha.MergeFrom(Array.ConvertAll(others, o => o.alpha));
        }

        public JointCost(int symbols = JointSymbols) => joint = new AdaptiveModel(symbols);

        private readonly AdaptiveModel joint;
        private readonly AdaptiveModel[] buckets = { new AdaptiveModel(BucketSymbols), new AdaptiveModel(BucketSymbols), new AdaptiveModel(BucketSymbols) };
        private readonly AdaptiveModel alpha = new AdaptiveModel(BucketSymbols);

        private static int JointOf(int[] r)
        {
            if (r[0] < -2 || r[0] > 2 || r[1] < -2 || r[1] > 2 || r[2] < -2 || r[2] > 2) return JointEscape;
            return (r[1] + 2) * 25 + (r[0] + 2) * 5 + r[2] + 2;
        }

        private static double SignedCost(AdaptiveModel model, int value)
        {
            var bucket = SignedBucket(value, out var extraBits, out _);
            return model.Cost(bucket) + extraBits;
        }

        public double Cost(int[] r, bool hasAlpha)
        {
            var symbol = JointOf(r);
            var cost = joint.Cost(symbol);
            if (symbol == JointEscape)
                cost += SignedCost(buckets[0], r[1]) + SignedCost(buckets[1], r[0]) + SignedCost(buckets[2], r[2]);
            if (hasAlpha)
                cost += SignedCost(alpha, r[3]);
            return cost;
        }

        public void Write(QtxRansEncoder rans, int firstAlphabet, int[] r, bool hasAlpha)
        {
            var symbol = JointOf(r);
            joint.Update(symbol);
            rans.Write(firstAlphabet, symbol);
            if (hasAlpha)
                WriteSigned(rans, firstAlphabet + 4, alpha, r[3]);
            if (symbol != JointEscape)
                return;
            rans.Deferred = true;
            WriteSigned(rans, firstAlphabet + 1, buckets[0], r[1]);
            WriteSigned(rans, firstAlphabet + 2, buckets[1], r[0]);
            WriteSigned(rans, firstAlphabet + 3, buckets[2], r[2]);
            rans.Deferred = false;
        }

        private static void WriteSigned(QtxRansEncoder rans, int alphabet, AdaptiveModel model, int value)
        {
            var bucket = SignedBucket(value, out var extraBits, out var extra);
            model.Update(bucket);
            rans.Write(alphabet, bucket);
            rans.WriteBits(extra, extraBits);
        }
    }

    private class CostModel
    {
        public void CopyFrom(CostModel other) => MergeFrom(new[] { other });

        public void MergeFrom(CostModel[] others)
        {
            for (var i = 0; i < Opcode.Length; i++) Opcode[i].MergeFrom(Array.ConvertAll(others, o => o.Opcode[i]));
            for (var i = 0; i < Centers.Length; i++) Centers[i].MergeFrom(Array.ConvertAll(others, o => o.Centers[i]));
            for (var i = 0; i < Extents.Length; i++) Extents[i].MergeFrom(Array.ConvertAll(others, o => o.Extents[i]));
            TableCenter.MergeFrom(Array.ConvertAll(others, o => o.TableCenter));
            for (var i = 0; i < Table.Length; i++) Table[i].MergeFrom(Array.ConvertAll(others, o => o.Table[i]));
            TableAlpha.MergeFrom(Array.ConvertAll(others, o => o.TableAlpha));
            Pair.MergeFrom(Array.ConvertAll(others, o => o.Pair));
            PairPattern.MergeFrom(Array.ConvertAll(others, o => o.PairPattern));
            for (var i = 0; i < Row.Length; i++) Row[i].MergeFrom(Array.ConvertAll(others, o => o.Row[i]));
            for (var i = 0; i < Mask.Length; i++) Mask[i].MergeFrom(Array.ConvertAll(others, o => o.Mask[i]));
            AlphaRow.MergeFrom(Array.ConvertAll(others, o => o.AlphaRow));
            AlphaFine.MergeFrom(Array.ConvertAll(others, o => o.AlphaFine));
        }

        public readonly AdaptiveModel[] Opcode = new AdaptiveModel[OpcodeContexts];
        public readonly JointCost[] Centers = new JointCost[CenterKinds * 4];
        public readonly JointCost[] Extents = { new JointCost(), new JointCost(), new JointCost(), new JointCost() };
        public readonly JointCost TableCenter = new JointCost();
        public readonly AdaptiveModel[] Table = { new AdaptiveModel(TableSymbols), new AdaptiveModel(TableSymbols), new AdaptiveModel(TableSymbols), new AdaptiveModel(TableSymbols) };
        public readonly AdaptiveModel TableAlpha = new AdaptiveModel(BucketSymbols);
        public readonly AdaptiveModel Pair = new AdaptiveModel(PairSymbols);
        public readonly AdaptiveModel PairPattern = new AdaptiveModel(CodeBuckets + 1);
        public int ActivityClass;
        public JointCost Center(int kind) => Centers[kind * 4 + ActivityClass];
        public JointCost Extent => Extents[ActivityClass];
        public readonly AdaptiveModel AlphaRow = new AdaptiveModel(256);
        public readonly AdaptiveModel AlphaFine = new AdaptiveModel(16);
        public readonly AdaptiveModel[] Row = new AdaptiveModel[8];
        public readonly AdaptiveModel[] Mask = { new AdaptiveModel(256), new AdaptiveModel(256) };

        public CostModel()
        {
            for (var i = 0; i < OpcodeContexts; i++) Opcode[i] = new AdaptiveModel(OpcodeSymbols);
            for (var i = 0; i < 8; i++) Row[i] = new AdaptiveModel(256);
            for (var i = 0; i < Centers.Length; i++) Centers[i] = new JointCost();
        }
    }
}

