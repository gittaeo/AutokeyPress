using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AutoKeyPress;

// Tracks successful sends from this application only; never observes global typing.
public sealed class LiveInputState
{
    private readonly Func<long> clock;
    private readonly Dictionary<Guid, (Keys? Key, string Label)> active = new();
    private readonly Dictionary<Keys, long> recent = new();
    public event Action Changed;
    public string LastInput { get; private set; } = "아직 입력 없음";
    public LiveInputState(Func<long> clock = null) { this.clock = clock ?? (() => Environment.TickCount64); }
    public int ActiveCount => active.Count;
    public string CurrentInput => active.Count == 0 ? "입력 대기" : string.Join(" + ", active.Values.Select(v => v.Label).Distinct());
    public static Keys Normalize(Keys key) => key switch
    {
        Keys.ControlKey => Keys.LControlKey,
        Keys.ShiftKey => Keys.LShiftKey,
        Keys.Menu => Keys.LMenu,
        _ => key & Keys.KeyCode
    };
    public Guid Begin(Keys? key, string label, Guid? token = null)
    {
        var id = token ?? Guid.NewGuid();
        active[id] = (key.HasValue ? Normalize(key.Value) : null, label);
        LastInput = label;
        Changed?.Invoke();
        return id;
    }
    public void End(Guid id)
    {
        if (!active.Remove(id, out var entry)) return;
        if (entry.Key.HasValue) recent[entry.Key.Value] = clock() + 160;
        Changed?.Invoke();
    }
    public void EndKey(Keys key)
    {
        foreach (var id in active.Where(pair => pair.Value.Key == Normalize(key)).Select(pair => pair.Key).ToArray()) End(id);
    }
    public bool IsDown(Keys key) => active.Values.Any(e => e.Key == Normalize(key));
    public bool IsRecent(Keys key) => recent.TryGetValue(Normalize(key), out var until) && until > clock();
    public bool HasRecent => recent.Values.Any(until => until > clock());
    public void Clear()
    {
        active.Clear();
        recent.Clear();
        Changed?.Invoke();
    }
}

public sealed class LiveKeyboard : Control
{
    private record Keycap(Keys Key, string Label, float X, float Y, float W = 1, float H = 1);
    private readonly List<Keycap> caps = new();
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 40 };
    public LiveInputState State { get; }
    public LiveKeyboard(LiveInputState state)
    {
        State = state;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        AccessibleName = "라이브 키보드: 프로그램에서 전송한 키";
        TabStop = false;
        BuildKeys();
        State.Changed += OnInputChanged;
        refresh.Tick += (_, _) => { Invalidate(); if (!State.HasRecent) refresh.Stop(); };
    }
    private void OnInputChanged() { if (!IsDisposed) { Invalidate(); if (State.HasRecent) refresh.Start(); } }
    private void Add(Keys key, string label, float x, float y, float w = 1, float h = 1) => caps.Add(new(key, label, x, y, w, h));
    private void Row(float y, params (Keys Key, string Label, float W)[] keys)
    {
        float x = 0;
        foreach (var key in keys) { Add(key.Key, key.Label, x, y, key.W); x += key.W; }
    }
    private void BuildKeys()
    {
        Add(Keys.Escape, "Esc", 0, 0);
        for (int i = 0; i < 12; i++) Add(Keys.F1 + i, "F" + (i + 1), 2 + i + (i / 4) * .4f, 0);
        Add(Keys.PrintScreen, "PrtSc", 15.5f, 0); Add(Keys.Scroll, "ScrLk", 16.5f, 0); Add(Keys.Pause, "Pause", 17.5f, 0);
        var digits = new List<(Keys, string, float)> { (Keys.Oemtilde, "` ~", 1) };
        for (int i = 1; i <= 9; i++) digits.Add((Keys.D0 + i, i.ToString(), 1));
        digits.AddRange(new[] { (Keys.D0, "0", 1f), (Keys.OemMinus, "− _", 1f), (Keys.Oemplus, "= +", 1f), (Keys.Back, "Backspace", 2f) });
        Row(1.15f, digits.ToArray());
        var top = new List<(Keys, string, float)> { (Keys.Tab, "Tab", 1.5f) };
        foreach (char c in "QWERTYUIOP") top.Add(((Keys)c, c.ToString(), 1));
        top.AddRange(new[] { (Keys.OemOpenBrackets, "[ {", 1f), (Keys.OemCloseBrackets, "] }", 1f), (Keys.OemPipe, "\\ |", 1.5f) });
        Row(2.15f, top.ToArray());
        var home = new List<(Keys, string, float)> { (Keys.CapsLock, "Caps Lock", 1.75f) };
        foreach (char c in "ASDFGHJKL") home.Add(((Keys)c, c.ToString(), 1));
        home.AddRange(new[] { (Keys.OemSemicolon, "; :", 1f), (Keys.OemQuotes, "' \"", 1f), (Keys.Enter, "Enter", 2.25f) });
        Row(3.15f, home.ToArray());
        var bottom = new List<(Keys, string, float)> { (Keys.LShiftKey, "Shift", 2.25f) };
        foreach (char c in "ZXCVBNM") bottom.Add(((Keys)c, c.ToString(), 1));
        bottom.AddRange(new[] { (Keys.Oemcomma, ", <", 1f), (Keys.OemPeriod, ". >", 1f), (Keys.OemQuestion, "/ ?", 1f), (Keys.RShiftKey, "Shift", 2.75f) });
        Row(4.15f, bottom.ToArray());
        Row(5.15f, (Keys.LControlKey,"Ctrl",1.25f),(Keys.LWin,"Win",1.25f),(Keys.LMenu,"Alt",1.25f),(Keys.Space,"Space",6.25f),(Keys.RMenu,"Alt",1.25f),(Keys.RWin,"Win",1.25f),(Keys.Apps,"Menu",1.25f),(Keys.RControlKey,"Ctrl",1.25f));
        var nav = new[] { (Keys.Insert,"Ins"),(Keys.Home,"Home"),(Keys.PageUp,"PgUp"),(Keys.Delete,"Del"),(Keys.End,"End"),(Keys.PageDown,"PgDn") };
        for (int i=0; i<6; i++) Add(nav[i].Item1,nav[i].Item2,15.5f+i%3,1.15f+i/3);
        Add(Keys.Up,"↑",16.5f,4.15f); Add(Keys.Left,"←",15.5f,5.15f); Add(Keys.Down,"↓",16.5f,5.15f); Add(Keys.Right,"→",17.5f,5.15f);
        Add(Keys.NumLock,"Num",19,1.15f); Add(Keys.Divide,"/",20,1.15f); Add(Keys.Multiply,"×",21,1.15f); Add(Keys.Subtract,"−",22,1.15f);
        for (int row=0;row<3;row++) for (int col=0;col<3;col++) { int n=7-row*3+col; Add(Keys.NumPad0+n,n.ToString(),19+col,2.15f+row); }
        Add(Keys.Add,"+",22,2.15f,1,2); Add(Keys.Separator,"Enter",22,4.15f,1,2);
        Add(Keys.NumPad0,"0",19,5.15f,2); Add(Keys.Decimal,".",21,5.15f);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float unit = Math.Max(1, ClientSize.Width / 23f);
        float row = Math.Max(1, ClientSize.Height / 6.15f);
        using var font = new Font("Segoe UI", Math.Clamp(unit * .18f, 6.5f, 9.5f), FontStyle.Regular);
        foreach (var cap in caps)
        {
            var rect = new RectangleF(cap.X*unit+2,cap.Y*row+2,cap.W*unit-5,cap.H*row-5);
            bool down=State.IsDown(cap.Key), recent=State.IsRecent(cap.Key);
            using var fill = new SolidBrush(down ? Color.FromArgb(18,122,109) : recent ? Color.FromArgb(203,234,224) : Color.FromArgb(244,246,249));
            using var border = new Pen(down ? Color.FromArgb(18,122,109) : Color.FromArgb(220,225,232));
            using var shape = RoundRect(rect,4);
            e.Graphics.FillPath(fill,shape); e.Graphics.DrawPath(border,shape);
            TextRenderer.DrawText(e.Graphics,cap.Label,font,Rectangle.Round(rect),down ? Color.White : Color.FromArgb(61,73,93),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        }
    }
    internal static GraphicsPath RoundRect(RectangleF r,float radius)
    {
        var path=new GraphicsPath(); float d=radius*2;
        path.AddArc(r.Left,r.Top,d,d,180,90); path.AddArc(r.Right-d,r.Top,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.Left,r.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { State.Changed-=OnInputChanged; refresh.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class SurfaceCard : Panel
{
    public SurfaceCard() { DoubleBuffered=true; BackColor=Color.FromArgb(244,246,251); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        using var path=LiveKeyboard.RoundRect(new RectangleF(0,0,Width-1,Height-1),10);
        using var fill=new SolidBrush(Color.White); using var border=new Pen(Color.FromArgb(224,229,236));
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias; e.Graphics.FillPath(fill,path); e.Graphics.DrawPath(border,path);
    }
}
