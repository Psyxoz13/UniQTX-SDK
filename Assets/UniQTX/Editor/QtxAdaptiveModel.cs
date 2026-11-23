
using System;


internal class AdaptiveModel
{
    private readonly int[] counts;
    private readonly double[] cachedCost;
    private readonly int[] cachedVersion;
    private int version = 1;
    private int total;

    public AdaptiveModel(int symbols)
    {
        counts = new int[symbols];
        cachedCost = new double[symbols];
        cachedVersion = new int[symbols];
        for (var i = 0; i < symbols; i++) counts[i] = 1;
        total = symbols;
    }

    public virtual double Cost(int symbol)
    {
        if (cachedVersion[symbol] == version)
            return cachedCost[symbol];
        var cost = QtxMath.Log2((double)total / Math.Max(counts[symbol], total / (counts.Length * RareFloor)));
        cachedCost[symbol] = cost;
        cachedVersion[symbol] = version;
        return cost;
    }

    private const int RareFloor = 8;

    public void Update(int symbol)
    {
        version++;
        counts[symbol] += 16;
        total += 16;
        if (total < 1 << 16) return;
        total = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            counts[i] = (counts[i] + 1) >> 1;
            total += counts[i];
        }
    }

    public void CopyFrom(AdaptiveModel other) => MergeFrom(new[] { other });

    public void MergeFrom(AdaptiveModel[] sources)
    {
        total = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            var sum = 0;
            foreach (var source in sources) sum += source.counts[i];
            counts[i] = sum;
            total += sum;
        }
        while (total >= 1 << 16)
        {
            total = 0;
            for (var i = 0; i < counts.Length; i++)
            {
                counts[i] = (counts[i] + 1) >> 1;
                total += counts[i];
            }
        }
        version++;
    }
}
