
using System;
using System.Collections.Generic;
using System.Linq;


internal class QtxRansEncoder
{
    public const int ProbabilityBits = 11;
    public const int ProbabilityTotal = 1 << ProbabilityBits;
    public const int Lanes = QtxImageEncoder.Lanes;

    private static readonly int[] ClassWeights = { 0, 8, 14, 24, 42, 73, 128, 222, 388, 676, 1176, 2048, 3566, 6208, 10809, 18820 };

    private readonly int planes;
    private readonly List<(int Alphabet, int Symbol)>[,] streams;
    private readonly List<(uint Value, int Count)>[,] rawBits;
    private readonly List<(int Alphabet, int Symbol)>[,] rowSymbols;
    private readonly List<(int Alphabet, int Symbol)>[,] rowDeferred;
    private readonly List<(uint Value, int Count)>[,] rowBits;
    private readonly List<(uint Value, int Count)>[,] rowDeferredBits;
    private readonly int[][] counts;
    private readonly int[] planeOf;
    private int lastPlane;

    public bool Deferred;

    public int Lane;

    private readonly List<List<(uint Value, int Count)>>[] rowOpcodeBits;

    public QtxRansEncoder(int[] alphabetSizes)
    {
        counts = new int[alphabetSizes.Length][];
        for (var i = 0; i < alphabetSizes.Length; i++)
            counts[i] = new int[alphabetSizes[i]];
        var planeStarts = QtxImageEncoder.PlaneStarts;
        planes = planeStarts.Length - 1;
        streams = new List<(int, int)>[planes, Lanes];
        rawBits = new List<(uint, int)>[planes, Lanes];
        rowSymbols = new List<(int, int)>[planes, Lanes];
        rowDeferred = new List<(int, int)>[planes, Lanes];
        rowBits = new List<(uint, int)>[planes, Lanes];
        rowDeferredBits = new List<(uint, int)>[planes, Lanes];
        planeOf = new int[alphabetSizes.Length];
        rowOpcodeBits = new List<List<(uint, int)>>[Lanes];
        for (var lane = 0; lane < Lanes; lane++)
            rowOpcodeBits[lane] = new List<List<(uint, int)>>();
        for (var plane = 0; plane < planes; plane++)
        {
            for (var lane = 0; lane < Lanes; lane++)
            {
                streams[plane, lane] = new List<(int, int)>();
                rawBits[plane, lane] = new List<(uint, int)>();
                rowSymbols[plane, lane] = new List<(int, int)>();
                rowDeferred[plane, lane] = new List<(int, int)>();
                rowBits[plane, lane] = new List<(uint, int)>();
                rowDeferredBits[plane, lane] = new List<(uint, int)>();
            }
            for (var alphabet = planeStarts[plane]; alphabet < planeStarts[plane + 1]; alphabet++)
                planeOf[alphabet] = plane;
        }
    }

    public void Write(int alphabet, int symbol)
    {
        lastPlane = planeOf[alphabet];
        if (lastPlane == QtxImageEncoder.PlaneOpcode)
            rowOpcodeBits[Lane].Add(new List<(uint, int)>());
        (Deferred ? rowDeferred : rowSymbols)[lastPlane, Lane].Add((alphabet, symbol));
        counts[alphabet][symbol]++;
    }

    public void WriteBits(uint value, int count)
    {
        if (count <= 0)
            return;
        if (lastPlane == QtxImageEncoder.PlaneOpcode)
            rowOpcodeBits[Lane][^1].Add((value, count));
        else
            (Deferred ? rowDeferredBits : rowBits)[lastPlane, Lane].Add((value, count));
    }

    public void EndRow()
    {
        var opcodes = new List<(int Alphabet, int Symbol)>();
        var opcodeBits = new List<List<(uint, int)>>();
        var blocks = 0;
        for (var lane = 0; lane < Lanes; lane++)
            blocks += rowSymbols[QtxImageEncoder.PlaneOpcode, lane].Count;
        for (var bx = 0; bx < blocks; bx++)
        {
            opcodes.Add(rowSymbols[QtxImageEncoder.PlaneOpcode, bx % Lanes][bx / Lanes]);
            opcodeBits.Add(rowOpcodeBits[bx % Lanes][bx / Lanes]);
        }
        foreach (var turn in Turns(opcodes))
        {
            WriteOpcodeTurn(turn, turn.Index % Lanes);
            if (turn.RunLength == 0)
                rawBits[QtxImageEncoder.PlaneOpcode, turn.Index % Lanes].AddRange(opcodeBits[turn.Index]);
        }
        for (var lane = 0; lane < Lanes; lane++)
        {
            rowSymbols[QtxImageEncoder.PlaneOpcode, lane].Clear();
            rowOpcodeBits[lane].Clear();
            for (var plane = 0; plane < planes; plane++)
            {
                if (plane != QtxImageEncoder.PlaneOpcode)
                {
                    streams[plane, lane].AddRange(rowSymbols[plane, lane]);
                    streams[plane, lane].AddRange(rowDeferred[plane, lane]);
                }
                rawBits[plane, lane].AddRange(rowBits[plane, lane]);
                rawBits[plane, lane].AddRange(rowDeferredBits[plane, lane]);
                rowSymbols[plane, lane].Clear();
                rowDeferred[plane, lane].Clear();
                rowBits[plane, lane].Clear();
                rowDeferredBits[plane, lane].Clear();
            }
        }
    }

    private static bool Repeatable(int opcode) =>
        opcode == QtxImageEncoder.TypeTransparent || opcode == QtxImageEncoder.OpcodeCopyLeft || opcode == QtxImageEncoder.OpcodeCopyUp;

    private List<(int Alphabet, int Symbol, int RunLength, int Index)> Turns(List<(int Alphabet, int Symbol)> opcodes)
    {
        var turns = new List<(int Alphabet, int Symbol, int RunLength, int Index)>();
        for (var bx = 0; bx < opcodes.Count; bx++)
        {
            turns.Add((opcodes[bx].Alphabet, opcodes[bx].Symbol, 0, bx));
            var repeated = opcodes[bx].Symbol;
            if (!Repeatable(repeated))
                continue;
            var runEnd = bx + 1;
            while (runEnd < opcodes.Count && opcodes[runEnd].Symbol == repeated)
                runEnd++;
            var length = runEnd - bx - 1;
            if (length < QtxImageEncoder.MinRun)
                continue;
            for (var k = bx + 1; k < runEnd; k++)
                counts[opcodes[k].Alphabet][opcodes[k].Symbol]--;
            turns.Add((opcodes[bx + 1].Alphabet, QtxImageEncoder.OpcodeRun, length, bx + 1));
            bx = runEnd - 1;
        }
        return turns;
    }

    private void WriteOpcodeTurn((int Alphabet, int Symbol, int RunLength, int Index) turn, int lane)
    {
        var plane = QtxImageEncoder.PlaneOpcode;
        streams[plane, lane].Add((turn.Alphabet, turn.Symbol));
        if (turn.RunLength == 0)
            return;
        counts[turn.Alphabet][turn.Symbol]++;
        var bucket = QtxImageEncoder.UnsignedBucket(turn.RunLength - QtxImageEncoder.MinRun, out var extraBits, out var extra);
        streams[plane, lane].Add((QtxImageEncoder.AlphabetRunLength, bucket));
        counts[QtxImageEncoder.AlphabetRunLength][bucket]++;
        if (extraBits > 0)
            rawBits[plane, lane].Add((extra, extraBits));
    }

    private static int[] ClassesOf(int[] frequencies)
    {
        var classes = new int[frequencies.Length];
        for (var s = 0; s < frequencies.Length; s++)
            classes[s] = frequencies[s] == 0 ? 0 : Math.Clamp((int)Math.Round(QtxMath.Log2(frequencies[s]) / 0.8) + 1, 1, 15);
        return classes;
    }

    private static int[] FromClasses(int[] classes)
    {
        var frequencies = new int[classes.Length];
        long total = 0;
        foreach (var k in classes) total += ClassWeights[k];
        if (total == 0)
            return frequencies;
        var assigned = 0;
        var order = new List<int>();
        for (var s = 0; s < classes.Length; s++)
        {
            if (classes[s] == 0) continue;
            frequencies[s] = (int)Math.Max(1, ClassWeights[classes[s]] * (long)ProbabilityTotal / total);
            assigned += frequencies[s];
            order.Add(s);
        }
        order.Sort((x, y) => classes[x] != classes[y] ? classes[y].CompareTo(classes[x]) : x.CompareTo(y));
        for (var i = 0; assigned != ProbabilityTotal; i = (i + 1) % order.Count)
        {
            var s = order[i];
            if (assigned < ProbabilityTotal) { frequencies[s]++; assigned++; }
            else if (frequencies[s] > 1) { frequencies[s]--; assigned--; }
        }
        return frequencies;
    }

    private static void WriteTables(List<byte> output, int[][] classes)
    {
        var used = new int[classes.Length];
        for (var a = 0; a < classes.Length; a++)
        {
            for (var s = 0; s < classes[a].Length; s++)
                if (classes[a][s] > 0) used[a] = s + 1;
            WriteVarint(output, used[a]);
        }
        var nibbles = new List<int>();
        for (var a = 0; a < classes.Length; a++)
        {
            for (var s = 0; s < used[a];)
            {
                if (classes[a][s] != 0)
                {
                    nibbles.Add(classes[a][s]);
                    s++;
                    continue;
                }
                var run = 0;
                while (s + run < used[a] && classes[a][s + run] == 0 && run < 16) run++;
                nibbles.Add(0);
                nibbles.Add(run - 1);
                s += run;
            }
        }
        for (var i = 0; i < nibbles.Count; i += 2)
            output.Add((byte)(nibbles[i] | ((i + 1 < nibbles.Count ? nibbles[i + 1] : 0) << 4)));
    }

    private static int[][] Summed(List<QtxRansEncoder> logs)
    {
        var combined = new int[logs[0].counts.Length][];
        for (var a = 0; a < combined.Length; a++)
        {
            combined[a] = new int[logs[0].counts[a].Length];
            foreach (var log in logs)
                for (var s = 0; s < combined[a].Length; s++)
                    combined[a][s] += log.counts[a][s];
        }
        return combined;
    }

    public static byte[] Combine(byte[] header, QtxRansEncoder[] logs) => CombineLevels(new[] { header }, new[] { logs })[0];

    public static byte[][] CombineLevels(byte[][] headers, QtxRansEncoder[][] levels)
    {
        var all = new List<QtxRansEncoder>();
        foreach (var level in levels)
            all.AddRange(level);
        var combined = Summed(all);
        var frequencies = new int[combined.Length][];
        var classes = new int[combined.Length][];
        for (var a = 0; a < combined.Length; a++)
        {
            classes[a] = ClassesOf(Normalized(combined[a]));
            frequencies[a] = FromClasses(classes[a]);
        }
        var starts = new int[combined.Length][];
        for (var a = 0; a < combined.Length; a++)
        {
            starts[a] = new int[frequencies[a].Length];
            for (var s = 1; s < starts[a].Length; s++)
                starts[a][s] = starts[a][s - 1] + frequencies[a][s - 1];
        }
        var results = new byte[levels.Length][];
        for (var level = 0; level < levels.Length; level++)
        {
            var output = new List<byte>(headers[level]);
            if (level == 0)
                WriteTables(output, classes);
            var stripes = new List<byte[]>();
            foreach (var log in levels[level])
                stripes.Add(log.StripeBytes(frequencies, starts));
            foreach (var stripe in stripes)
                AppendUInt32(output, (uint)stripe.Length);
            foreach (var stripe in stripes)
                output.AddRange(stripe);
            results[level] = output.ToArray();
        }
        return results;
    }

    private byte[] StripeBytes(int[][] frequencies, int[][] starts)
    {
        var directory = new List<byte>();
        var data = new List<byte>();
        for (var plane = 0; plane < planes; plane++)
        for (var lane = 0; lane < Lanes; lane++)
        {
            var words = EncodeLane(streams[plane, lane], frequencies, starts);
            WriteVarint(directory, words.Count * 2);
            foreach (var word in words)
            {
                data.Add((byte)word);
                data.Add((byte)(word >> 8));
            }
            var raw = PackRawBits(rawBits[plane, lane]);
            WriteVarint(directory, raw.Length);
            data.AddRange(raw);
        }
        directory.AddRange(data);
        return directory.ToArray();
    }

    private static List<ushort> EncodeLane(List<(int Alphabet, int Symbol)> lane, int[][] frequencies, int[][] starts)
    {
        var words = new List<ushort>();
        if (lane.Count == 0)
            return words;
        var state = 1u << 16;
        for (var i = lane.Count - 1; i >= 0; i--)
        {
            var (alphabet, symbol) = lane[i];
            var frequency = (uint)frequencies[alphabet][symbol];
            if (frequency == ProbabilityTotal)
                continue;
            var start = (uint)starts[alphabet][symbol];
            if (state >= frequency << (32 - ProbabilityBits))
            {
                words.Add((ushort)state);
                state >>= 16;
            }
            state = ((state / frequency) << ProbabilityBits) + state % frequency + start;
        }
        words.Add((ushort)state);
        words.Add((ushort)(state >> 16));
        words.Reverse();
        return words;
    }

    private static void AppendUInt32(List<byte> output, uint value)
    {
        for (var i = 0; i < 4; i++)
            output.Add((byte)(value >> (i * 8)));
    }

    private static void WriteVarint(List<byte> output, int value)
    {
        while (value >= 0x80)
        {
            output.Add((byte)(0x80 | (value & 0x7F)));
            value >>= 7;
        }
        output.Add((byte)value);
    }

    private static int[] Normalized(int[] counts)
    {
        var total = 0L;
        foreach (var count in counts) total += count;
        var frequencies = new int[counts.Length];
        if (total == 0)
            return frequencies;

        var assigned = 0;
        for (var s = 0; s < counts.Length; s++)
        {
            if (counts[s] == 0) continue;
            frequencies[s] = Math.Max(1, (int)(counts[s] * (long)ProbabilityTotal / total));
            assigned += frequencies[s];
        }

        while (assigned != ProbabilityTotal)
        {
            var best = -1;
            var bestLoss = double.MaxValue;
            var step = assigned < ProbabilityTotal ? 1 : -1;
            for (var s = 0; s < counts.Length; s++)
            {
                if (counts[s] == 0 || frequencies[s] + step < 1) continue;
                var loss = counts[s] * (Math.Log((double)frequencies[s] / (frequencies[s] + step)));
                if (loss < bestLoss)
                {
                    bestLoss = loss;
                    best = s;
                }
            }
            frequencies[best] += step;
            assigned += step;
        }
        return frequencies;
    }

    private static byte[] PackRawBits(List<(uint Value, int Count)> rawBits)
    {
        var bytes = new List<byte>();
        ulong buffer = 0;
        var filled = 0;
        foreach (var (value, count) in rawBits)
        {
            buffer |= (ulong)(value & ((1u << count) - 1)) << filled;
            filled += count;
            while (filled >= 8)
            {
                bytes.Add((byte)buffer);
                buffer >>= 8;
                filled -= 8;
            }
        }
        if (filled > 0)
            bytes.Add((byte)buffer);
        return bytes.ToArray();
    }
}
