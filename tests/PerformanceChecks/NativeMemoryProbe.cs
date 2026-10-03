using System.Diagnostics;
using System.Runtime.InteropServices;

// Region/residency classification only, not an allocation-owner attribution or a dump.
internal static class NativeMemoryProbe
{
    internal static object Capture(Process process)
    {
        var groups = new Dictionary<string, (long Commit, long Resident, int Regions)>();
        var pages = new List<PageInfo>();
        var kinds = new List<string>();
        ulong address = 0;
        while (VirtualQueryEx(process.Handle, (IntPtr)(long)address, out var region,
            (UIntPtr)Marshal.SizeOf<RegionInfo>()) != UIntPtr.Zero)
        {
            var size = region.Size.ToUInt64();
            if (size == 0 || address + size <= address) break;
            address = (ulong)region.Base.ToInt64() + size;
            if (region.State != 0x1000) continue; // MEM_COMMIT
            var kind = region.Type switch { 0x20000 => "private", 0x1000000 => "image", 0x40000 => "mapped", _ => "other" };
            var existing = groups.GetValueOrDefault(kind);
            groups[kind] = (existing.Commit + (long)size, existing.Resident, existing.Regions + 1);
            for (ulong offset = 0; offset < size; offset += (ulong)Environment.SystemPageSize)
            {
                pages.Add(new PageInfo { Address = new IntPtr(region.Base.ToInt64() + (long)offset) }); kinds.Add(kind);
            }
        }
        var entries = pages.ToArray();
        if (!QueryWorkingSetEx(process.Handle, entries, checked((uint)(entries.Length * Marshal.SizeOf<PageInfo>()))))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        for (var i = 0; i < entries.Length; i++)
        {
            if ((entries[i].Attributes.ToUInt64() & 1) == 0) continue; // Valid/resident bit
            var existing = groups[kinds[i]];
            groups[kinds[i]] = (existing.Commit, existing.Resident + Environment.SystemPageSize, existing.Regions);
        }
        return groups.ToDictionary(item => item.Key, item => new
        { virtualCommittedMiB = item.Value.Commit / 1048576d, residentMiB = item.Value.Resident / 1048576d, regions = item.Value.Regions });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RegionInfo
    {
        public IntPtr Base, AllocationBase;
        public uint AllocationProtection;
        public ushort Partition;
        public UIntPtr Size;
        public uint State, Protection, Type;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PageInfo { public IntPtr Address; public UIntPtr Attributes; }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out RegionInfo information, UIntPtr size);
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryWorkingSetEx(IntPtr process, [In, Out] PageInfo[] pages, uint length);
}
