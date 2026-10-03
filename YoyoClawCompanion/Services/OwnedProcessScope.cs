using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace YoyoClawCompanion.Services;

// Only attach a subprocess created by this application, before sending it work.
// Closing this private job reaps its children without enumerating unrelated processes.
internal sealed class OwnedProcessScope : IDisposable
{
    private readonly SafeFileHandle _job;
    private OwnedProcessScope(SafeFileHandle job) => _job = job;

    internal static OwnedProcessScope Attach(Process process)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (job.IsInvalid || !SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<ExtendedLimits>())
            || !AssignProcessToJobObject(job, process.Handle))
        {
            var error = Marshal.GetLastWin32Error();
            job.Dispose();
            // Fail closed: the caller cleans up its own root and sends no RPC work.
            throw new System.ComponentModel.Win32Exception(error, "Cannot own quota reader process");
        }
        return new(job);
    }

    internal static async Task StopAsync(Process process, OwnedProcessScope? scope, TimeSpan grace)
    {
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(grace);
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or System.IO.IOException) { }
        finally
        {
            scope?.Dispose();
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(); // Own root only; never walk the system process tree.
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await process.WaitForExitAsync(timeout.Token);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception) { }
        }
    }

    public void Dispose() => _job.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcesses;
        public UIntPtr Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits information, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
