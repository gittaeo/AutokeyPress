using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoKeyPress;

public static class HidKeys
{
    public static byte Usage(Keys key, bool extended = false)
    {
        key &= Keys.KeyCode;
        if (key >= Keys.A && key <= Keys.Z) return (byte)(4 + key - Keys.A);
        if (key >= Keys.D1 && key <= Keys.D9) return (byte)(30 + key - Keys.D1);
        if (key >= Keys.F1 && key <= Keys.F12) return (byte)(58 + key - Keys.F1);
        if (key >= Keys.F13 && key <= Keys.F24) return (byte)(104 + key - Keys.F13);
        if (key >= Keys.NumPad1 && key <= Keys.NumPad9) return (byte)(89 + key - Keys.NumPad1);
        return key switch
        {
            Keys.D0 => 39, Keys.Enter => extended ? (byte)88 : (byte)40, Keys.Escape => 41, Keys.Back => 42, Keys.Tab => 43, Keys.Space => 44,
            Keys.OemMinus => 45, Keys.Oemplus => 46, Keys.OemOpenBrackets => 47, Keys.OemCloseBrackets => 48, Keys.OemPipe => 49,
            Keys.OemSemicolon => 51, Keys.OemQuotes => 52, Keys.Oemtilde => 53, Keys.Oemcomma => 54, Keys.OemPeriod => 55, Keys.OemQuestion => 56,
            Keys.CapsLock => 57, Keys.PrintScreen => 70, Keys.Scroll => 71, Keys.Pause => 72, Keys.Insert => 73, Keys.Home => 74, Keys.PageUp => 75,
            Keys.Delete => 76, Keys.End => 77, Keys.PageDown => 78, Keys.Right => 79, Keys.Left => 80, Keys.Down => 81, Keys.Up => 82,
            Keys.NumLock => 83, Keys.Divide => 84, Keys.Multiply => 85, Keys.Subtract => 86, Keys.Add => 87, Keys.NumPad0 => 98, Keys.Decimal => 99,
            Keys.OemBackslash => 100, Keys.Apps => 101, Keys.VolumeMute => 127, Keys.VolumeUp => 128, Keys.VolumeDown => 129,
            Keys.HangulMode => 144, Keys.HanjaMode => 145,
            Keys.ControlKey or Keys.LControlKey => 224, Keys.ShiftKey or Keys.LShiftKey => 225, Keys.Menu or Keys.LMenu => 226, Keys.LWin => 227,
            Keys.RControlKey => 228, Keys.RShiftKey => 229, Keys.RMenu => 230, Keys.RWin => 231,
            _ => throw new NotSupportedException($"가상 HID에서 지원하지 않는 키: {key}")
        };
    }
    // HID sends physical keys, not Unicode. This map assumes the foreground IME is in English mode.
    public static (byte Usage, bool Shift) Character(char c, bool capsLock = false)
    {
        if (c >= 'a' && c <= 'z') return ((byte)(4+c-'a'), capsLock);
        if (c >= 'A' && c <= 'Z') return ((byte)(4+c-'A'), !capsLock);
        if (c >= '1' && c <= '9') return ((byte)(30+c-'1'), false);
        const string plain="0\r\u001b\b\t -=[]\\;'`,./";
        byte[] usages={39,40,41,42,43,44,45,46,47,48,49,51,52,53,54,55,56};
        int at=plain.IndexOf(c); if(at>=0)return(usages[at],false);
        const string shifted="!@#$%^&*()_+{}|:\"~<>?";
        byte[] shiftedUsages={30,31,32,33,34,35,36,37,38,39,45,46,47,48,49,51,52,53,54,55,56};
        at=shifted.IndexOf(c); if(at>=0)return(shiftedUsages[at],true);
        throw new NotSupportedException("가상 HID 문장은 영문·숫자·기호만 지원합니다. 한글 등 Unicode 문자는 직접 전송할 수 없습니다.");
    }
    public static Keys? VisualKey(byte usage)
    {
        if(usage>=4 && usage<=29)return Keys.A+usage-4;
        if(usage>=30 && usage<=38)return Keys.D1+usage-30;
        return usage switch {39=>Keys.D0,40=>Keys.Enter,43=>Keys.Tab,44=>Keys.Space,45=>Keys.OemMinus,46=>Keys.Oemplus,47=>Keys.OemOpenBrackets,48=>Keys.OemCloseBrackets,49=>Keys.OemPipe,51=>Keys.OemSemicolon,52=>Keys.OemQuotes,53=>Keys.Oemtilde,54=>Keys.Oemcomma,55=>Keys.OemPeriod,56=>Keys.OemQuestion,_=>null};
    }
}

// A complete key state is sent for every transition, retaining overlapping holds.
public sealed class HidReportState
{
    private readonly Dictionary<byte,int> held=new();
    private readonly Action<byte[]> send;
    public HidReportState(Action<byte[]> send) { this.send=send; }
    public byte[] Packet()
    {
        byte mods=0;
        foreach(var key in held.Keys.Where(k=>k>=224 && k<=231)) mods|=(byte)(1 << (key-224));
        var keys=held.Keys.Where(k=>k<224).OrderBy(k=>k).ToArray();
        return new[]{mods,(byte)keys.Length}.Concat(keys).ToArray();
    }
    public void Down(byte usage)
    {
        int old=held.GetValueOrDefault(usage); held[usage]=old+1;
        try { send(Packet()); } catch { if(old==0)held.Remove(usage);else held[usage]=old; throw; }
    }
    public void Up(byte usage)
    {
        if(!held.TryGetValue(usage,out int count))return;
        if(count==1)held.Remove(usage);else held[usage]=count-1;
        try { send(Packet()); } catch { held[usage]=count; throw; }
    }
    public void ReleaseAll() { send(new byte[]{0,0}); held.Clear(); }
}

public sealed class VirtualHidKeyboard : IDisposable
{
    private readonly string runtimePath;
    public VirtualHidKeyboard(string runtimePath=null) { this.runtimePath=runtimePath ?? Path.Combine(AppContext.BaseDirectory,"HidRuntime"); }
    private const int ApiPort=33242, UsbPort=33241;
    private readonly object gate=new();
    private Process server;
    private OwnedProcessJob serverJob;
    private TcpClient device;
    private CancellationTokenSource feedback;
    private HidReportState state;
    private uint bus;
    private volatile byte leds;
    public bool IsConnected { get; private set; }
    public bool CapsLock => (leds&2)!=0;
    public event Action<string> Disconnected;

    public async Task ConnectAsync()
    {
        if(IsConnected)return;
        if(server!=null)Dispose();
        string runtime=runtimePath;
        string exe=Path.Combine(runtime,"viiper.exe");
        if(!File.Exists(exe))throw new FileNotFoundException("HidRuntime의 viiper.exe가 없습니다.");
        if(!DriverInstalled())throw new InvalidOperationException("USB/IP 가상 장치 드라이버가 설치되지 않았습니다. HidRuntime의 설치 안내를 먼저 확인하세요.");
        var start=new ProcessStartInfo(exe) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=runtime,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var arg in new[]{"server","--no-tray","--update-notify=none","--usb.addr=127.0.0.1:"+UsbPort,"--api.addr=127.0.0.1:"+ApiPort,"--api.auto-attach-local-client=true","--api.auto-attach-windows-native=true"})start.ArgumentList.Add(arg);
        server=Process.Start(start) ?? throw new IOException("가상 HID 서버를 시작하지 못했습니다.");
        // Drain diagnostics without recording user input or leaving redirected pipes full.
        server.OutputDataReceived+=(_,_)=>{};server.ErrorDataReceived+=(_,_)=>{};
        server.BeginOutputReadLine();server.BeginErrorReadLine();
        try
        {
            serverJob=new OwnedProcessJob(server);
            bool ready=false;
            for(int i=0;i<35;i++)
            {
                if(server.HasExited)throw new IOException("가상 HID 서버가 종료되었습니다. 로컬 포트 33241/33242 충돌 여부를 확인하세요.");
                try { using var ping=await Request("ping"); ready=ping.RootElement.GetProperty("server").GetString()=="VIIPER"; if(ready)break; }catch(SocketException){}catch(IOException){}
                await Task.Delay(100);
            }
            if(!ready)throw new IOException("가상 HID 서버의 응답이 없습니다.");
            using(var response=await Request("bus/create")) bus=response.RootElement.GetProperty("busId").GetUInt32();
            var previousKeyboards=KeyboardIds();
            string id,vid,pid;
            using(var response=await Request($"bus/{bus}/add {{\"type\":\"keyboard\"}}"))
            {
                var info=response.RootElement; id=info.GetProperty("devId").GetString();
                vid=info.GetProperty("vid").GetString().Replace("0x","");pid=info.GetProperty("pid").GetString().Replace("0x","");
            }
            device=new TcpClient { NoDelay=true, SendTimeout=1500 };
            await device.ConnectAsync(IPAddress.Loopback,ApiPort).WaitAsync(TimeSpan.FromSeconds(2));
            await device.GetStream().WriteAsync(Encoding.UTF8.GetBytes($"bus/{bus}/{id}\0"));
            feedback=new CancellationTokenSource();
            state=new HidReportState(Send);
            _=ReadFeedback(feedback.Token);
            // Device creation alone is insufficient: auto-attach failures do not fail VIIPER's API.
            bool attached=false;
            for(int i=0;i<60;i++) { if(KeyboardIds(vid,pid).Except(previousKeyboards).Any()){attached=true;break;} await Task.Delay(100); }
            if(!attached)throw new IOException("가상 키보드를 Windows에 연결하지 못했습니다. 드라이버 상태와 관리자 실행 여부를 확인하세요.");
            if(server.HasExited || device.Client.Poll(0,SelectMode.SelectRead)&&device.Available==0)throw new IOException("가상 키보드 연결이 끊겼습니다.");
            state.ReleaseAll(); IsConnected=true;
        }
        catch { Dispose(); throw; }
    }
    private static bool DriverInstalled()
    {
        using var service=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\usbip2_ude");
        return service!=null;
    }
    private async Task<JsonDocument> Request(string command)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var tcp=new TcpClient(); await tcp.ConnectAsync(IPAddress.Loopback,ApiPort,timeout.Token);
        await tcp.GetStream().WriteAsync(Encoding.UTF8.GetBytes(command+"\0"),timeout.Token);
        using var data=new MemoryStream(); byte[] buffer=new byte[1024]; int count;
        while((count=await tcp.GetStream().ReadAsync(buffer,timeout.Token))>0) { data.Write(buffer,0,count);if(data.Length>65536)throw new IOException("가상 HID 응답이 너무 큽니다."); }
        var json=JsonDocument.Parse(data.ToArray());
        if(json.RootElement.TryGetProperty("status",out var status)&&status.GetInt32()>=400) { string detail=json.RootElement.GetProperty("detail").GetString();json.Dispose();throw new IOException(detail); }
        return json;
    }
    private async Task ReadFeedback(CancellationToken token)
    {
        try { var stream=device.GetStream();byte[] b=new byte[1];while(await stream.ReadAsync(b,token)>0)leds=b[0]; }
        catch(OperationCanceledException){return;}catch(IOException){}catch(ObjectDisposedException){}
        if(!token.IsCancellationRequested) { IsConnected=false;Disconnected?.Invoke("가상 HID 연결이 끊겼습니다. 입력을 중지했습니다."); }
    }
    private void Send(byte[] packet)
    {
        if(device==null || server==null || server.HasExited)throw new IOException("가상 HID가 연결되지 않았습니다.");
        device.GetStream().Write(packet);
    }
    public void Down(byte usage) { lock(gate){if(!IsConnected)throw new IOException("가상 HID 연결이 필요합니다.");state.Down(usage);} }
    public void Up(byte usage) { lock(gate){if(IsConnected)state.Up(usage);} }
    public void ReleaseAll() { lock(gate){if(IsConnected)state.ReleaseAll();} }
    public void Dispose()
    {
        lock(gate)
        {
            try { state?.ReleaseAll(); }catch{}
            IsConnected=false; feedback?.Cancel();device?.Dispose();device=null;
            if(server!=null) { try { if(!server.HasExited)server.Kill(); }catch(InvalidOperationException){} server.Dispose();server=null; }
            serverJob?.Dispose();serverJob=null;
            feedback?.Dispose();feedback=null;state=null;
        }
    }
    // Enumerate keyboard hardware IDs to verify the device actually reached Windows.
    private static HashSet<uint> KeyboardIds(string vid=null,string pid=null)
    {
        var found=new HashSet<uint>();
        var guid=new Guid("4d36e96b-e325-11ce-bfc1-08002be10318");
        IntPtr set=SetupDiGetClassDevs(ref guid,null,IntPtr.Zero,2);
        if(set==new IntPtr(-1))return found;
        try { for(uint i=0;;i++) { var info=new DeviceInfo { Size=Marshal.SizeOf<DeviceInfo>() };if(!SetupDiEnumDeviceInfo(set,i,ref info))break;
            if(vid==null){found.Add(info.DevInst);continue;}
            byte[] bytes=new byte[4096];if(SetupDiGetDeviceRegistryProperty(set,ref info,1,out _,bytes,(uint)bytes.Length,out _))
            { string ids=Encoding.Unicode.GetString(bytes);if(ids.Contains("VID_"+vid,StringComparison.OrdinalIgnoreCase)&&ids.Contains("PID_"+pid,StringComparison.OrdinalIgnoreCase))found.Add(info.DevInst); }
        } return found; }finally{SetupDiDestroyDeviceInfoList(set);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public int Size;public Guid ClassGuid;public uint DevInst;public UIntPtr Reserved; }
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid,string enumerator,IntPtr window,uint flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInfo(IntPtr set,uint index,ref DeviceInfo info);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode)] private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set,ref DeviceInfo info,uint property,out uint type,byte[] buffer,uint size,out uint required);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
