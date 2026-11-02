using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace AutoKeyPress;

public partial class Form1
{
    private static readonly Color Ink = Color.FromArgb(32, 42, 65);
    private static readonly Color Accent = Color.FromArgb(18, 122, 109);
    private readonly Label settingsStatus = new Label();
    private readonly string settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    private bool settingsLoaded;
    private Button importButton;

    public sealed class SavedSettings
    {
        public int Version { get; set; } = 1;
        public List<string[]> Rows { get; set; } = new();
        public Dictionary<string, string> TextFields { get; set; } = new();
        public int StartKey { get; set; }
        public string RecordedText { get; set; } = "";
        public List<SavedAction> Actions { get; set; } = new();
    }
    public sealed class SavedAction
    {
        public string Type { get; set; } = "";
        public char TextChar { get; set; }
        public int SpecialKey { get; set; }
        public string Display { get; set; } = "";
    }

    private TextBox[] TimingFields => new[] { txtTextInterval, txtTextIntervalMax, txtTextKeyDelayMin, txtTextKeyDelayMax, txtTextHoldMin, txtTextHoldMax };

    private void InitializeWorkspace()
    {
        BuildDashboard();
        if (File.Exists(settingsPath))
        {
            try { ApplySettings(ReadSettings(settingsPath)); settingsStatus.Text = "마지막 설정을 복원했습니다"; }
            catch (Exception ex) { settingsStatus.Text = "설정 복원 실패 · 원본을 유지합니다"; MessageBox.Show("설정 파일을 읽지 못했습니다. 원본 파일은 유지됩니다.\n" + ex.Message, "설정 복원", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        }
        settingsLoaded = true;
    }

    private void StyleControl(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is Button b)
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                bool secondary = Equals(b.Tag, "secondary") || b == btnAddRow;
                b.BackColor = secondary ? Color.FromArgb(232, 238, 242) : b.Text.Contains("중지") || b.Text == "삭제" ? Color.FromArgb(174, 64, 82) : Accent;
                b.ForeColor = secondary ? Ink : Color.White;
                b.Cursor = Cursors.Hand;
                b.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 145, 128);
            }
            if (c is TextBox t) { t.BorderStyle = BorderStyle.FixedSingle; t.BackColor = Color.FromArgb(248, 249, 253); t.ForeColor = Ink; }
            if (c is GroupBox) c.ForeColor = Ink;
            StyleControl(c);
        }
    }

    private SavedSettings CaptureSettings() => new SavedSettings
    {
        Rows = keyTasks.Select(t => new[] { t.KeyBox.Text, t.IntervalMinBox.Text, t.IntervalMaxBox.Text, t.DelayMinBox.Text, t.DelayMaxBox.Text, t.HoldMinBox.Text, t.HoldMaxBox.Text }).ToList(),
        TextFields = TimingFields.ToDictionary(t => t.Name, t => t.Text),
        StartKey = (int)globalStartKey,
        RecordedText = txtRecordedText.Text,
        Actions = macroActions.Select(a => new SavedAction { Type = a.Type, TextChar = a.TextChar, SpecialKey = (int)a.SpecialKey, Display = a.Display }).ToList()
    };

    private static SavedSettings ReadSettings(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("설정 파일이 너무 큽니다.");
        var data = JsonSerializer.Deserialize<SavedSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("빈 설정 파일입니다.");
        if (data.Version != 1 || data.Rows == null || data.Rows.Count < 1 || data.Rows.Count > 500 || data.Rows.Any(r => r == null || r.Length != 7 || r.Any(s => s == null)) || data.Actions == null || data.TextFields == null || data.RecordedText == null)
            throw new InvalidDataException("지원하지 않는 설정 형식입니다.");
        foreach (var row in data.Rows)
            if (row[0] != "" && (!Enum.TryParse<Keys>(row[0], out var key) || !Enum.IsDefined(typeof(Keys), key))) throw new InvalidDataException("알 수 없는 키입니다.");
        if (!Enum.IsDefined(typeof(Keys), (Keys)data.StartKey) || data.StartKey == (int)Keys.Escape || data.Actions.Any(a => a == null || (a.Type != "TEXT" && a.Type != "KEY") || (a.Type == "KEY" && !Enum.IsDefined(typeof(Keys), (Keys)a.SpecialKey))))
            throw new InvalidDataException("단축키 또는 녹화 동작이 올바르지 않습니다.");
        return data;
    }

    private void ApplySettings(SavedSettings data)
    {
        foreach (var task in keyTasks) task.RowPanel.Dispose();
        keyTasks.Clear();
        rowsPanel.Controls.Clear();
        foreach (var r in data.Rows) AddKeyTaskRow(r[0], r[1], r[2], r[3], r[4], r[5], r[6]);
        foreach (var t in TimingFields) if (data.TextFields.TryGetValue(t.Name, out var value)) t.Text = value;
        globalStartKey = (Keys)data.StartKey;
        txtStartHotkey.Text = globalStartKey == Keys.None ? "" : globalStartKey.ToString();
        recordedText = data.RecordedText;
        txtRecordedText.Text = recordedText;
        macroActions.Clear();
        macroActions.AddRange(data.Actions.Select(a => new MacroAction { Type = a.Type, TextChar = a.TextChar, SpecialKey = (Keys)a.SpecialKey, Display = a.Display }));
    }

    private bool WriteSettings(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(CaptureSettings(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
            settingsLoaded = true;
            return true;
        }
        catch (Exception ex) { MessageBox.Show("설정을 저장하지 못했습니다.\n" + ex.Message, "설정 저장", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
    }

    private void SaveLastSettings() { if (settingsLoaded) WriteSettings(settingsPath); }
    private void ExportSettings()
    {
        using var dialog = new SaveFileDialog { Filter = "AutoKeyPress 설정 (*.json)|*.json", FileName = "AutoKeyPress-settings.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK && WriteSettings(dialog.FileName)) settingsStatus.Text = "설정 파일을 내보냈습니다";
    }
    private void ImportSettings()
    {
        if (!btnStart.Enabled || !btnTextStart.Enabled || isRecordingText || isReplayingText) { MessageBox.Show("실행 및 녹화를 중지한 뒤 불러오세요."); return; }
        using var dialog = new OpenFileDialog { Filter = "AutoKeyPress 설정 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { var data = ReadSettings(dialog.FileName); ApplySettings(data); settingsLoaded = true; settingsStatus.Text = "설정을 불러왔습니다"; }
        catch (Exception ex) { MessageBox.Show("설정을 불러오지 못했습니다.\n" + ex.Message, "설정 불러오기", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
