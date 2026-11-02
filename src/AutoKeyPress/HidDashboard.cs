using System;
using System.Linq;
using System.Windows.Forms;

namespace AutoKeyPress;

public partial class Form1
{
    private readonly VirtualHidKeyboard hidKeyboard=new();
    private Button hidConnectButton;
    private bool hidConnecting;
    private async void ConnectHid()
    {
        if(hidConnecting || IsDisposed)return;
        if(hidKeyboard.IsConnected) { settingsStatus.Text="가상 HID 키보드 연결됨";return; }
        hidConnecting=true;hidConnectButton.Enabled=false;hidConnectButton.Text="HID 연결 중";
        try { await hidKeyboard.ConnectAsync();if(!IsDisposed){hidConnectButton.Text="HID 연결됨";settingsStatus.Text="가상 HID 키보드 준비 완료";} }
        catch(Exception ex) { if(!IsDisposed){hidConnectButton.Text="HID 연결";settingsStatus.Text="가상 HID 미연결";MessageBox.Show(this,ex.Message,"가상 HID 연결",MessageBoxButtons.OK,MessageBoxIcon.Warning);} }
        finally { hidConnecting=false;if(!IsDisposed)hidConnectButton.Enabled=true; }
    }
    private bool CheckHidReady(bool text)
    {
        if(!hidKeyboard.IsConnected) { MessageBox.Show(this,"먼저 상단의 HID 연결을 눌러 가상 키보드를 연결하세요. 드라이버가 없으면 HidRuntime의 설치 안내를 확인하세요.","가상 HID 연결 필요");return false; }
        try
        {
            byte? shortcut=globalStartKey==Keys.None ? null : HidKeys.Usage(globalStartKey);
            if(text)
            {
                if(shortcut>=224)throw new InvalidOperationException("문장 반복의 전체 시작키는 Shift/Ctrl/Alt 대신 F8 같은 기능키로 지정하세요.");
                foreach(var action in macroActions)
                {
                    byte usage=action.Type=="TEXT" ? HidKeys.Character(action.TextChar).Usage : HidKeys.Usage(action.SpecialKey);
                    if(shortcut==usage || action.Type=="TEXT" && HidKeys.Character(action.TextChar).Shift && shortcut==225)
                        throw new InvalidOperationException("전체 시작키가 문장의 입력 키와 겹칩니다. 시작키를 다른 키로 지정하세요.");
                }
            }
            else foreach(var task in keyTasks.Where(t=>t.TargetKey!=Keys.None))
            {
                bool extended=IsExtendedKey(task.TargetKey);
                if(task.KeyBox.Tag is Tuple<ushort,bool> captured)extended=captured.Item2;
                byte usage=HidKeys.Usage(task.TargetKey,extended);
                if(shortcut==usage)throw new InvalidOperationException("전체 시작키와 반복 키가 같습니다. 가상 HID의 재시작 반복을 막으려면 서로 다른 키를 지정하세요.");
            }
            return true;
        }
        catch(Exception ex) when(ex is NotSupportedException || ex is InvalidOperationException) { MessageBox.Show(this,ex.Message,"HID 입력 확인");return false; }
    }
    private void HidDisconnected(string reason)
    {
        if(IsDisposed || !IsHandleCreated)return;
        try { BeginInvoke((Action)(()=> { if(IsDisposed)return;StopAllByEsc();hidConnectButton.Text="HID 연결";settingsStatus.Text=reason; })); }
        catch(InvalidOperationException) { }
    }
}
