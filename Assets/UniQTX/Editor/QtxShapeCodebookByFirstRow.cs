
using System.Collections.Generic;


internal static class ShapeCodebookByFirstRow
{
    public const int Lanes = 8;

    public static readonly int[] GroupStarts = new int[257];
    public static readonly int[] Entries = BuildEntries();
    public static readonly uint[] Patterns = BuildPatterns();

    private static int[] BuildEntries()
    {
        var groups = new List<int>[256];

        for (var value = 0; value < groups.Length; value++)
        {
            groups[value] = new List<int>();
        }

        for (var entry = 0; entry < ShapeCodebook.Size; entry++)
        {
            groups[(int)(ShapeCodebook.Patterns[entry] & 255)].Add(entry);
        }

        var entries = new List<int>();

        for (var value = 0; value < groups.Length; value++)
        {
            GroupStarts[value] = entries.Count;
            entries.AddRange(groups[value]);

            while (groups[value].Count > 0 && entries.Count % Lanes != 0)
            {
                entries.Add(groups[value][0]);
            }
        }

        GroupStarts[groups.Length] = entries.Count;

        return entries.ToArray();
    }

    private static uint[] BuildPatterns()
    {
        var patterns = new uint[Entries.Length];

        for (var index = 0; index < Entries.Length; index++)
        {
            patterns[index] = ShapeCodebook.Patterns[Entries[index]];
        }

        return patterns;
    }
}
