using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AutoKeyPress;

// Windows closes the helper as well if the app crashes or is terminated.
internal sealed class OwnedProcessJob : IDisposable
{
    private readonly SafeFileHandle handle;
    public OwnedProcessJob(Process process)
    {
        handle=CreateJobObject(IntPtr.Zero,null);
        if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits=new ExtendedLimits { Basic=new BasicLimits { Flags=0x2000 } };
        if(!SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimits>()) || !AssignProcessToJobObject(handle,process.Handle))
        { int error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error,"가상 HID 보조 프로세스의 종료 관리를 설정하지 못했습니다."); }
    }
    public void Dispose()=>handle.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    { public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinWorkingSet,MaxWorkingSet;public uint ActiveProcesses;public UIntPtr Affinity;public uint Priority,Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    { public BasicLimits Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes,string name);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetInformationJobObject(SafeFileHandle job,int type,ref ExtendedLimits limits,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
}
