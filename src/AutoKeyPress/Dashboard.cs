using System;
using System.Drawing;
using System.Linq;
using System.Security.Principal;
using System.Windows.Forms;

namespace AutoKeyPress;

public partial class Form1
{
    private readonly LiveInputState liveInput = new();
    private SurfaceCard keyCard, textCard;
    private Label currentInputLabel, lastInputLabel;
    private bool dashboardReady;

    private static Label Caption(string text, float size = 9, bool bold = false) => new()
    {
        Text=text, AutoSize=false, ForeColor=Ink, BackColor=Color.Transparent,
        Font=new Font("맑은 고딕",size,bold ? FontStyle.Bold : FontStyle.Regular),
        TextAlign=ContentAlignment.MiddleLeft, AutoEllipsis=true
    };

    private Button DashboardButton(string text, Action action, bool secondary=false)
    {
        var b=new Button { Text=text, Height=34, Width=text.Length>7?142:112, Margin=new Padding(8,0,0,0), Tag=secondary?"secondary":null };
        b.Click+=(_,_)=>action(); return b;
    }

    private void BuildDashboard()
    {
        SuspendLayout();
        // Replace the old fixed-position containers, while retaining their input controls/events.
        Controls.Clear();
        Text="AutoKeyPress · 가상 HID 반복 입력";
        BackColor=Color.FromArgb(244,246,251);
        AutoScaleMode=AutoScaleMode.Dpi;
        AutoScaleDimensions=new SizeF(96,96);
        Font=new Font("맑은 고딕",9);
        FormBorderStyle=FormBorderStyle.Sizable;
        MaximizeBox=true;
        MinimumSize=new Size(1060,710);
        var area=Screen.FromControl(this).WorkingArea;
        ClientSize=new Size(Math.Min(1320,area.Width-56),Math.Min(840,area.Height-72));
        AutoScroll=true;
        var root=new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(20,14,20,12), ColumnCount=1, RowCount=6, BackColor=BackColor, MinimumSize=new Size(1020,650) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach (float height in new[]{65f,42f,0f,42f,188f,25f}) root.RowStyles.Add(new RowStyle(height==0?SizeType.Percent:SizeType.Absolute,height==0?100:height));
        Controls.Add(root);

        var top=new Panel { Dock=DockStyle.Fill, Margin=Padding.Empty };
        labelTitle=Caption("AutoKeyPress",23,true); labelTitle.SetBounds(0,0,280,40);
        var subtitle=Caption("반복 설정과 입력 상태를 한 화면에서",9); subtitle.SetBounds(2,40,350,22); subtitle.ForeColor=Color.FromArgb(112,123,141);
        var actions=new FlowLayoutPanel { Dock=DockStyle.Right, AutoSize=true, WrapContents=false, Padding=new Padding(0,5,0,0), FlowDirection=FlowDirection.LeftToRight };
        actions.Controls.Add(DashboardButton("설정 저장",()=> {if(WriteSettings(settingsPath)) settingsStatus.Text="현재 설정 저장 완료";},true));
        actions.Controls.Add(DashboardButton("파일 내보내기",ExportSettings,true));
        importButton=DashboardButton("불러오기",ImportSettings,true); actions.Controls.Add(importButton);
        hidConnectButton=DashboardButton("HID 연결",ConnectHid,true);actions.Controls.Add(hidConnectButton);
        hidKeyboard.Disconnected+=HidDisconnected;
        actions.Controls.Add(DashboardButton("전체 중지 · ESC",StopAllByEsc));
        top.Controls.Add(labelTitle); top.Controls.Add(subtitle); top.Controls.Add(actions); root.Controls.Add(top,0,0);

        var guide=new Panel { Dock=DockStyle.Fill, Margin=new Padding(0,0,0,8), BackColor=Color.FromArgb(231,238,243) };
        var hotkeyTitle=Caption("전체 시작키",9,true); hotkeyTitle.SetBounds(12,3,92,27);
        txtStartHotkey.SetBounds(108,4,82,27);
        var hint=Caption("가상 HID 연결 후 시작 · 문자는 영문 입력 모드에서 사용 · ESC 전체 중지",8.5f); hint.SetBounds(204,3,650,27); hint.ForeColor=Color.FromArgb(90,105,125);
        var admin=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        var privilege=Caption(admin?"관리자 실행 중":"일반 권한",8.5f,true); privilege.Dock=DockStyle.Right; privilege.Width=116; privilege.ForeColor=admin?Accent:Color.FromArgb(112,123,141);
        guide.Controls.Add(hotkeyTitle); guide.Controls.Add(txtStartHotkey); guide.Controls.Add(hint); guide.Controls.Add(privilege); root.Controls.Add(guide,0,1);

        var body=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1, Margin=Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,68)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,32));
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        keyCard=new SurfaceCard { Dock=DockStyle.Fill, Margin=new Padding(0,0,12,0) };
        textCard=new SurfaceCard { Dock=DockStyle.Fill, Margin=Padding.Empty };
        foreach(var c in groupKeyRepeat.Controls.Cast<Control>().ToArray()) keyCard.Controls.Add(c);
        foreach(var c in groupTextRepeat.Controls.Cast<Control>().ToArray()) textCard.Controls.Add(c);
        var keyTitle=Caption("개별 키 반복",11,true); keyTitle.SetBounds(14,9,190,26); keyCard.Controls.Add(keyTitle);
        var textTitle=Caption("문장 / 여러 키 반복",11,true); textTitle.SetBounds(14,9,250,26); textCard.Controls.Add(textTitle);
        groupKeyRepeat.Dispose(); groupTextRepeat.Dispose();
        rowsPanel.BackColor=Color.White; rowsPanel.Margin=Padding.Empty;
        headerPanel.BackColor=Color.FromArgb(244,247,249);
        string[] headers={"#","키","주기 최소\n초","주기 최대\n초","딜레이 최소\nms","딜레이 최대\nms","누름 최소\nms","누름 최대\nms",""};
        var labels=RowHeaders;
        for(int i=0;i<labels.Length;i++) { labels[i].Text=headers[i]; labels[i].AutoSize=false; labels[i].TextAlign=ContentAlignment.MiddleCenter; labels[i].ForeColor=Color.FromArgb(101,116,135); labels[i].Font=new Font("맑은 고딕",8); }
        btnAddRow.Text="+ 키 추가"; btnStart.Text="반복 시작"; btnStop.Text="중지";
        labelTextTitle.Text="입력시작을 눌러 문장 또는 키를 기록하세요"; labelTextTitle.AutoSize=false; labelTextTitle.ForeColor=Color.FromArgb(112,123,141); labelTextTitle.Font=new Font("맑은 고딕",8);
        btnRecordStart.Text="입력시작"; btnRecordStop.Text="입력완료"; btnRecordStart.Tag="secondary"; btnRecordStop.Tag="secondary";
        labelTextInterval.Text="반복 주기 · 초"; labelTextKeyDelay.Text="키 사이 간격 · ms"; labelTextHold.Text="누름 시간 · ms";
        foreach(var l in new[]{labelTextInterval,labelTextKeyDelay,labelTextHold}) { l.AutoSize=false; l.Font=new Font("맑은 고딕",8.5f); }
        foreach(var l in new[]{labelTextIntervalSec,labelTextKeyDelayMs,labelTextHoldMs}) l.Visible=false;
        foreach(var t in TimingFields) t.TextAlign=HorizontalAlignment.Center;
        body.Controls.Add(keyCard,0,0); body.Controls.Add(textCard,1,0); root.Controls.Add(body,0,2);
        keyCard.Resize+=(_,_)=>LayoutKeyCard(); textCard.Resize+=(_,_)=>LayoutTextCard();

        var statusBar=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1, Margin=new Padding(2,4,0,0) };
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,68)); statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,32));
        foreach(var l in new[]{lblStatus,lblTextStatus}) { l.AutoSize=false; l.Dock=DockStyle.Fill; l.AutoEllipsis=true; l.Margin=Padding.Empty; l.Font=new Font("맑은 고딕",8.5f); l.ForeColor=Color.FromArgb(86,104,124); l.TextAlign=ContentAlignment.MiddleLeft; }
        statusBar.Controls.Add(lblStatus,0,0); statusBar.Controls.Add(lblTextStatus,1,0); root.Controls.Add(statusBar,0,3);

        var keyboardCard=new SurfaceCard { Dock=DockStyle.Fill, Margin=Padding.Empty, Padding=new Padding(14,0,14,10) };
        var keyboardHeader=new Panel { Dock=DockStyle.Top, Height=41, BackColor=Color.White };
        var keyboardTitle=Caption("LIVE KEYBOARD",9,true); keyboardTitle.SetBounds(0,5,160,31); keyboardTitle.ForeColor=Color.FromArgb(109,122,142);
        currentInputLabel=Caption("입력 대기",12,true); currentInputLabel.SetBounds(164,5,410,31); currentInputLabel.ForeColor=Accent;
        lastInputLabel=Caption("아직 입력 없음",8.5f); lastInputLabel.Dock=DockStyle.Right; lastInputLabel.Width=250; lastInputLabel.TextAlign=ContentAlignment.MiddleRight; lastInputLabel.ForeColor=Color.FromArgb(123,135,151);
        keyboardHeader.Controls.Add(keyboardTitle); keyboardHeader.Controls.Add(currentInputLabel); keyboardHeader.Controls.Add(lastInputLabel);
        var keyboard=new LiveKeyboard(liveInput) { Dock=DockStyle.Fill, Margin=Padding.Empty };
        keyboardCard.Controls.Add(keyboard); keyboardCard.Controls.Add(keyboardHeader); root.Controls.Add(keyboardCard,0,4);
        liveInput.Changed+=UpdateLiveInputLabels;
        var footer=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1, Margin=Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,70)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,30));
        var note=Caption("가상 USB HID 전송 · 진한 초록: 누르는 중 / 연한 초록: 최근 입력 · HID 연결 후 사용",8); note.Dock=DockStyle.Fill; note.ForeColor=Color.FromArgb(123,135,151);
        settingsStatus.Dock=DockStyle.Fill; settingsStatus.AutoEllipsis=true; settingsStatus.Font=new Font("맑은 고딕",8); settingsStatus.Text="종료 시 자동 저장 · 다음 실행 시 복원"; settingsStatus.TextAlign=ContentAlignment.MiddleRight; settingsStatus.ForeColor=Color.FromArgb(123,135,151);
        footer.Controls.Add(note,0,0); footer.Controls.Add(settingsStatus,1,0); root.Controls.Add(footer,0,5);
        StyleControl(this);
        dashboardReady=true;
        ResumeLayout(true);
        LayoutKeyCard(); LayoutTextCard();
    }

    private Label[] RowHeaders => new[]{labelHeaderNo,labelHeaderKey,labelHeaderIntervalMin,labelHeaderIntervalMax,labelHeaderDelayMin,labelHeaderDelayMax,labelHeaderHoldMin,labelHeaderHoldMax,labelHeaderDelete};
    private void LayoutKeyCard()
    {
        if(!dashboardReady || keyCard==null) return;
        int width=Math.Max(620,keyCard.Width-28);
        headerPanel.SetBounds(14,40,width,39);
        rowsPanel.SetBounds(14,84,width,Math.Max(58,keyCard.Height-140));
        var cols=ColumnBounds(width-20);
        var headers=RowHeaders;
        for(int i=0;i<headers.Length;i++) headers[i].SetBounds(cols[i].X,0,cols[i].Width,39);
        foreach(var task in keyTasks) LayoutKeyRow(task,width-20);
        btnAddRow.SetBounds(14,keyCard.Height-45,100,32);
        btnStart.SetBounds(keyCard.Width-215,keyCard.Height-45,118,32);
        btnStop.SetBounds(keyCard.Width-87,keyCard.Height-45,73,32);
    }
    private static Rectangle[] ColumnBounds(int width)
    {
        // No/key, six timing fields, delete. The header and inputs share the same grid.
        int[] weights={4,12,12,12,12,12,12,12,12};
        var result=new Rectangle[9]; int x=0;
        for(int i=0;i<weights.Length;i++) { int next=i==8?width:x+width*weights[i]/100; result[i]=new Rectangle(x,0,Math.Max(12,next-x-6),30); x=next; }
        return result;
    }
    private void LayoutKeyRow(KeyTask task,int width)
    {
        var cols=ColumnBounds(width);
        task.RowPanel.Size=new Size(width,40); task.RowPanel.Margin=new Padding(0,0,0,6); task.RowPanel.BackColor=Color.White;
        Control[] fields={task.NoLabel,task.KeyBox,task.IntervalMinBox,task.IntervalMaxBox,task.DelayMinBox,task.DelayMaxBox,task.HoldMinBox,task.HoldMaxBox,task.DeleteButton};
        for(int i=0;i<fields.Length;i++) { fields[i].SetBounds(cols[i].X,5,cols[i].Width,28); if(fields[i] is TextBox t)t.TextAlign=HorizontalAlignment.Center; }
        task.NoLabel.ForeColor=Color.FromArgb(123,135,151);
    }
    private void LayoutTextCard()
    {
        if(!dashboardReady || textCard==null)return;
        int w=textCard.Width, h=textCard.Height;
        labelTextTitle.SetBounds(14,35,w-28,23);
        int editorHeight=Math.Max(45,h-258);
        txtRecordedText.SetBounds(14,60,w-28,editorHeight);
        int y=66+editorHeight;
        btnRecordStart.SetBounds(14,y,(w-36)/2,28); btnRecordStop.SetBounds(22+(w-36)/2,y,(w-36)/2,28);
        y+=35;
        Label[] labels={labelTextInterval,labelTextKeyDelay,labelTextHold};
        TextBox[] mins={txtTextInterval,txtTextKeyDelayMin,txtTextHoldMin}, maxs={txtTextIntervalMax,txtTextKeyDelayMax,txtTextHoldMax};
        Label[] waves={labelTextIntervalWave,labelTextKeyDelayWave,labelTextHoldWave};
        int field=Math.Clamp((w-160)/2,45,84);
        for(int i=0;i<3;i++) { labels[i].SetBounds(14,y+i*30,w-2*field-58,27); mins[i].SetBounds(w-2*field-38,y+i*30,field,27); waves[i].SetBounds(w-field-34,y+i*30,18,27); maxs[i].SetBounds(w-field-14,y+i*30,field,27); }
        btnTextStart.SetBounds(14,h-45,(w-36)/2,32); btnTextStop.SetBounds(22+(w-36)/2,h-45,(w-36)/2,32);
    }
    private void UpdateLiveInputLabels()
    {
        if(currentInputLabel==null || IsDisposed)return;
        currentInputLabel.Text=liveInput.CurrentInput;
        lastInputLabel.Text="최근 입력  " + liveInput.LastInput;
    }
    private static Keys VisualKey(Keys key,bool extended) => key==Keys.Enter && extended ? Keys.Separator : key;
}
