using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal struct QtxEncodeParams
{
    public uint StructSize;
    public int Quality;
    public uint Flags;
}
