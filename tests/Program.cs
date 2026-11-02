using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using AutoKeyPress;

internal static class Program
{
    static int passed;
    static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "roundtrip.json");
    static Form1.SavedSettings Read() => (Form1.SavedSettings)typeof(Form1).GetMethod("ReadSettings", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { PathName });
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
    static void Reject(string json, string name)
    {
        File.WriteAllText(PathName, json);
        try { Read(); throw new Exception("Invalid settings accepted: " + name); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException || ex.InnerException is JsonException) { Check(true, name); }
    }
    [STAThread]
    static void Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--hid-probe")
        {
            var runtime=System.IO.Path.GetFullPath(args[1]);
            var reportPath=System.IO.Path.Combine(runtime,"connection-check.json");
            using var keyboard=new VirtualHidKeyboard(runtime);
            try
            {
                keyboard.ConnectAsync().GetAwaiter().GetResult();
                File.WriteAllText(reportPath,JsonSerializer.Serialize(new {phase="connected",connected=keyboard.IsConnected,utc=DateTimeOffset.UtcNow,keysSent=false},new JsonSerializerOptions{WriteIndented=true}));
                Console.WriteLine("PASS Windows virtual HID keyboard attached through the production backend.");
                System.Threading.Thread.Sleep(12000);
                keyboard.ReleaseAll();keyboard.Dispose();
                File.WriteAllText(reportPath,JsonSerializer.Serialize(new {phase="complete",connected=true,cleanup=true,utc=DateTimeOffset.UtcNow,keysSent=false},new JsonSerializerOptions{WriteIndented=true}));
                Console.WriteLine("PASS neutral report and test device cleanup. No keys were typed.");
            }
            catch(Exception ex)
            {
                File.WriteAllText(reportPath,JsonSerializer.Serialize(new {phase="failed",error=ex.ToString(),utc=DateTimeOffset.UtcNow},new JsonSerializerOptions{WriteIndented=true}));
                Console.Error.WriteLine(ex.ToString());Environment.ExitCode=1;
            }
            return;
        }
        var data = new Form1.SavedSettings {
            Rows = { new[] { "F5", "60", "70", "1000", "1100", "1300", "1400" }, new[] { "", "", "", "", "", "", "" } },
            StartKey = (int)Keys.F9,
            RecordedText = "한글 test\n",
            Actions = { new Form1.SavedAction { Type = "TEXT", TextChar = '한', Display = "한" }, new Form1.SavedAction { Type = "KEY", SpecialKey = (int)Keys.Enter, Display = "Enter" } },
            TextFields = { ["txtTextInterval"] = "12.5", ["txtTextHoldMin"] = "80" }
        };
        File.WriteAllText(PathName, JsonSerializer.Serialize(data));
        var result = Read();
        Check(result.Rows.Count == 2 && result.Rows[0][6] == "1400" && result.Rows[1][0] == "", "rows and blank fields round-trip");
        Check(result.RecordedText == data.RecordedText && result.Actions[1].SpecialKey == (int)Keys.Enter && result.Actions[0].TextChar == '한', "Unicode and special-key macro round-trip");
        Check(result.StartKey == (int)Keys.F9 && result.TextFields["txtTextInterval"] == "12.5", "hotkey and timing round-trip");
        Reject("{", "malformed JSON rejected");
        Reject("{\"Version\":2,\"Rows\":[[\"F5\",\"1\",\"1\",\"0\",\"0\",\"1\",\"1\"]]}", "future schema rejected");
        data.Rows[0][0] = "UnknownKey";
        Reject(JsonSerializer.Serialize(data), "unknown key rejected");
        data.Rows[0][0] = "F5";
        data.Actions[0].Type = "UNKNOWN";
        Reject(JsonSerializer.Serialize(data), "unknown macro rejected");
        data.Actions[0].Type = "TEXT";
        data.StartKey = (int)Keys.Escape;
        Reject(JsonSerializer.Serialize(data), "reserved stop key rejected");
        Reject("{\"Rows\":null}", "null rows rejected");
        long now = 100;
        var live = new LiveInputState(() => now);
        var f5 = live.Begin(Keys.F5, "F5");
        var f6 = live.Begin(Keys.F6, "F6");
        live.End(f5);
        Check(!live.IsDown(Keys.F5) && live.IsDown(Keys.F6) && live.ActiveCount == 1, "overlapping holds release independently");
        Check(live.IsRecent(Keys.F5), "short taps remain briefly visible");
        now += 161;
        Check(!live.IsRecent(Keys.F5), "recent indicator expires");
        var shift = live.Begin(Keys.ShiftKey, "Shift");
        Check(live.IsDown(Keys.LShiftKey) && !live.IsDown(Keys.RShiftKey), "generic modifier maps to one key");
        var unicode = live.Begin(null, "문자 한");
        Check(live.CurrentInput.Contains("문자 한") && !live.IsDown(Keys.H), "Unicode shown without inventing physical key presses");
        live.Clear(); live.End(f6); live.End(shift); live.End(unicode);
        Check(live.ActiveCount == 0 && !live.IsRecent(Keys.F5) && live.CurrentInput == "입력 대기", "stop clears active and recent keys; late releases harmless");
        var enter = live.Begin(Keys.Enter, "Enter");
        Check(live.IsDown(Keys.Enter) && !live.IsDown(Keys.Separator), "main and numpad Enter distinct");
        live.End(enter);
        Check(HidKeys.Usage(Keys.A)==4 && HidKeys.Usage(Keys.F5)==62, "letter and function keys map to HID usages");
        Check(HidKeys.Usage(Keys.Enter)==40 && HidKeys.Usage(Keys.Enter,true)==88, "HID main and numpad Enter mapping");
        Check(HidKeys.Usage(Keys.RControlKey)==228 && HidKeys.Usage(Keys.Up)==82, "HID extended and modifier mapping");
        Check(HidKeys.Character('A')==(4,true) && HidKeys.Character('a',true)==(4,true) && HidKeys.Character('A',true)==(4,false), "HID ASCII honors Caps Lock and Shift");
        Check(HidKeys.Character('!')==(30,true) && HidKeys.Character(' ')==(44,false), "HID symbols and whitespace mapping");
        try { HidKeys.Character('한');throw new Exception("Unicode silently accepted"); }catch(NotSupportedException){Check(true,"unsupported Unicode rejected without software fallback");}
        var packets=new System.Collections.Generic.List<byte[]>();
        var reports=new HidReportState(p=>packets.Add(p));
        reports.Down(224);reports.Down(4);reports.Down(5);reports.Up(4);
        Check(System.Linq.Enumerable.SequenceEqual(packets[^1],new byte[]{1,1,5}),"HID releases preserve other held keys and modifiers");
        reports.Down(5);reports.Up(5);
        Check(System.Linq.Enumerable.SequenceEqual(packets[^1],new byte[]{1,1,5}),"HID overlapping key ownership is reference counted");
        reports.ReleaseAll();
        Check(System.Linq.Enumerable.SequenceEqual(packets[^1],new byte[]{0,0}),"HID stop sends neutral keyboard report");
        bool fail=true;
        var transactional=new HidReportState(p=> {if(fail)throw new IOException("disconnected");});
        try { transactional.Down(4); }catch(IOException){}
        Check(System.Linq.Enumerable.SequenceEqual(transactional.Packet(),new byte[]{0,0}),"failed HID press rolls back local state");
        fail=false;transactional.Down(4);fail=true;
        try { transactional.Up(4); }catch(IOException){}
        Check(System.Linq.Enumerable.SequenceEqual(transactional.Packet(),new byte[]{0,1,4}),"failed HID release retains key for cleanup");
        using (var form = new Form1(false))
        {
            foreach (var size in new[] { new System.Drawing.Size(1320, 840), new System.Drawing.Size(1044, 671) })
            {
                form.ClientSize = size;
                form.PerformLayout();
                var keyCard = (Control)typeof(Form1).GetField("keyCard", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                var textCard = (Control)typeof(Form1).GetField("textCard", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                Check(keyCard.Parent == textCard.Parent && keyCard.Right <= textCard.Left && keyCard.Height >= 270, "both editors share a non-overlapping row at " + size.Width);
                foreach (var field in new[] { "btnTextStart", "btnTextStop", "txtTextHoldMin", "txtTextHoldMax", "btnStart", "btnStop" })
                {
                    var control = (Control)typeof(Form1).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                    Check(control.Parent.ClientRectangle.Contains(control.Bounds), field + " stays inside card at " + size.Width);
                }
                var hold = (Control)typeof(Form1).GetField("txtTextHoldMin", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                var start = (Control)typeof(Form1).GetField("btnTextStart", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                Check(hold.Bottom <= start.Top, "timing fields do not overlap text actions at " + size.Width);
            }
        }
        Console.WriteLine(passed + " checks passed. No key input was sent; no global hook was installed.");
    }
}
