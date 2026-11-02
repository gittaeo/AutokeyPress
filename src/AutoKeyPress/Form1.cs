using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoKeyPress;

public partial class Form1 : Form
{
	private class KeyTask
	{
		public Panel RowPanel { get; set; }

		public Label NoLabel { get; set; }

		public TextBox KeyBox { get; set; }

		public TextBox IntervalMinBox { get; set; }

		public TextBox IntervalMaxBox { get; set; }

		public TextBox DelayMinBox { get; set; }

		public TextBox DelayMaxBox { get; set; }

		public TextBox HoldMinBox { get; set; }

		public TextBox HoldMaxBox { get; set; }

		public Button DeleteButton { get; set; }

		public string DisplayText { get; set; } = "";

		public Keys TargetKey { get; set; }

		public double IntervalMinSeconds { get; set; }

		public double IntervalMaxSeconds { get; set; }

		public int DelayMinMs { get; set; }

		public int DelayMaxMs { get; set; }

		public int HoldMinMs { get; set; } = 25;

		public int HoldMaxMs { get; set; } = 25;

		public DateTime NextRunTime { get; set; }

		public bool IsRunning { get; set; }
	}

	private class MacroAction
	{
		public string Type { get; set; } = "";

		public char TextChar { get; set; }

		public Keys SpecialKey { get; set; }

		public string Display { get; set; } = "";
	}

	private class PressedKeyInfo
	{
		public Guid PressId { get; set; }

		public Keys Key { get; set; }

		public ushort ScanCode { get; set; }

		public bool IsExtended { get; set; }
	}

	private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

	private readonly List<KeyTask> keyTasks = new List<KeyTask>();

	private readonly List<MacroAction> macroActions = new List<MacroAction>();

	private readonly List<PressedKeyInfo> pressedKeys = new List<PressedKeyInfo>();

	private readonly object pressedKeyLock = new object();

	private readonly Random random = new Random();

	private CancellationTokenSource keyCancelSource = new CancellationTokenSource();

	private CancellationTokenSource textCancelSource = new CancellationTokenSource();

	private bool isRecordingText;

	private bool isReplayingText;

	private bool stopRequested;

	private Keys globalStartKey;

	private bool keyRepeatPausedByText;

	private bool keyRepeatWasRunningBeforeText;

	private int textKeyDelayMinMs = 120;

	private int textKeyDelayMaxMs = 120;

	private int textHoldMinMs = 80;

	private int textHoldMaxMs = 80;

	private double textRepeatMinSeconds = 10.0;

	private double textRepeatMaxSeconds = 10.0;

	private string recordedText = "";

	private const int WH_KEYBOARD_LL = 13;

	private const int WM_KEYDOWN = 256;

	private const int WM_SYSKEYDOWN = 260;

	private LowLevelKeyboardProc keyboardProc;

	private nint hookId = IntPtr.Zero;

	private IContainer components;

	private Label labelTitle;

	private Panel panelGuide;

	private Label labelGuide1;

	private Label labelGuide2;

	private Label labelStartHotkey;

	private TextBox txtStartHotkey;

	private Label labelStartHotkeyDesc;

	private GroupBox groupKeyRepeat;

	private Panel headerPanel;

	private FlowLayoutPanel rowsPanel;

	private Label labelHeaderNo;

	private Label labelHeaderKey;

	private Label labelHeaderIntervalMin;

	private Label labelHeaderIntervalMax;

	private Label labelHeaderDelayMin;

	private Label labelHeaderDelayMax;

	private Label labelHeaderHoldMin;

	private Label labelHeaderHoldMax;

	private Label labelHeaderDelete;

	private Button btnAddRow;

	private Button btnStart;

	private Button btnStop;

	private Label lblStatus;

	private System.Windows.Forms.Timer timer1;

	private GroupBox groupTextRepeat;

	private Label labelTextTitle;

	private TextBox txtRecordedText;

	private Button btnRecordStart;

	private Button btnRecordStop;

	private Label labelTextInterval;

	private TextBox txtTextInterval;

	private Label labelTextIntervalWave;

	private TextBox txtTextIntervalMax;

	private Label labelTextIntervalSec;

	private Label labelTextKeyDelay;

	private TextBox txtTextKeyDelayMin;

	private Label labelTextKeyDelayWave;

	private TextBox txtTextKeyDelayMax;

	private Label labelTextKeyDelayMs;

	private Label labelTextHold;

	private TextBox txtTextHoldMin;

	private Label labelTextHoldWave;

	private TextBox txtTextHoldMax;

	private Label labelTextHoldMs;

	private Button btnTextStart;

	private Button btnTextStop;

	private Label lblTextStatus;

	private System.Windows.Forms.Timer timerText;

	public Form1() : this(true) { }

	public Form1(bool enableGlobalHook)
	{
		InitializeComponent();
		lblStatus.Text = "상태: 대기 중";
		lblTextStatus.Text = "문장 상태: 대기 중";
		btnStop.Enabled = false;
		btnRecordStop.Enabled = false;
		btnTextStop.Enabled = false;
		txtRecordedText.ReadOnly = true;
		txtRecordedText.KeyDown += txtRecordedText_KeyDown;
		txtRecordedText.KeyPress += txtRecordedText_KeyPress;
		txtStartHotkey.ReadOnly = true;
		txtStartHotkey.KeyDown += txtStartHotkey_KeyDown;
		txtStartHotkey.KeyUp += txtStartHotkey_KeyUp;
		txtStartHotkey.PreviewKeyDown += txtStartHotkey_PreviewKeyDown;
		txtStartHotkey.MouseDoubleClick += delegate
		{
			ClearGlobalStartHotkey();
		};
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip
		{
			Items = { 
			{
				"시작 단축키 초기화",
				(Image?)null,
				(EventHandler?)delegate
				{
					ClearGlobalStartHotkey();
				}
			} }
		};
		txtStartHotkey.ContextMenuStrip = contextMenuStrip;
		AddKeyTaskRow("F5", "60", "70", "1000", "1100", "1300", "1400");
		keyboardProc = HookCallback;
		if (enableGlobalHook) hookId = SetHook(keyboardProc);
		InitializeWorkspace();
	}

	private void ResetKeyCancelSource()
	{
		try
		{
			keyCancelSource.Cancel();
			keyCancelSource.Dispose();
		}
		catch
		{
		}
		keyCancelSource = new CancellationTokenSource();
	}

	private void ResetTextCancelSource()
	{
		try
		{
			textCancelSource.Cancel();
			textCancelSource.Dispose();
		}
		catch
		{
		}
		textCancelSource = new CancellationTokenSource();
	}

	private void AddKeyTaskRow(string keyText = "", string intervalMin = "", string intervalMax = "", string delayMin = "", string delayMax = "", string holdMin = "", string holdMax = "")
	{
		int num = keyTasks.Count + 1;
		Panel panel = new Panel();
		panel.Width = 1100;
		panel.Height = 42;
		panel.Margin = new Padding(0, 0, 0, 6);
		panel.BackColor = Color.White;
		Label label = new Label();
		label.Text = num.ToString();
		label.TextAlign = ContentAlignment.MiddleCenter;
		label.Location = new Point(5, 6);
		label.Size = new Size(40, 28);
		label.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
		label.ForeColor = Color.FromArgb(73, 80, 87);
		TextBox textBox = CreateRowTextBox(55, keyText, 95);
		textBox.ReadOnly = true;
		textBox.PlaceholderText = "키 입력";
		TextBox textBox2 = CreateRowTextBox(170, intervalMin, 85);
		textBox2.PlaceholderText = "최소 초";
		TextBox textBox3 = CreateRowTextBox(280, intervalMax, 85);
		textBox3.PlaceholderText = "최대 초";
		TextBox textBox4 = CreateRowTextBox(395, delayMin, 85);
		textBox4.PlaceholderText = "최소";
		TextBox textBox5 = CreateRowTextBox(505, delayMax, 85);
		textBox5.PlaceholderText = "최대";
		TextBox textBox6 = CreateRowTextBox(620, holdMin, 85);
		textBox6.PlaceholderText = "최소";
		TextBox textBox7 = CreateRowTextBox(730, holdMax, 85);
		textBox7.PlaceholderText = "최대";
		Button button = new Button();
		button.Text = "삭제";
		button.Location = new Point(850, 5);
		button.Size = new Size(80, 30);
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = Color.FromArgb(220, 53, 69);
		button.ForeColor = Color.White;
		button.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
		button.UseVisualStyleBackColor = false;
		panel.Controls.Add(label);
		panel.Controls.Add(textBox);
		panel.Controls.Add(textBox2);
		panel.Controls.Add(textBox3);
		panel.Controls.Add(textBox4);
		panel.Controls.Add(textBox5);
		panel.Controls.Add(textBox6);
		panel.Controls.Add(textBox7);
		panel.Controls.Add(button);
		KeyTask task = new KeyTask
		{
			RowPanel = panel,
			NoLabel = label,
			KeyBox = textBox,
			IntervalMinBox = textBox2,
			IntervalMaxBox = textBox3,
			DelayMinBox = textBox4,
			DelayMaxBox = textBox5,
			HoldMinBox = textBox6,
			HoldMaxBox = textBox7,
			DeleteButton = button
		};
		if (!string.IsNullOrWhiteSpace(keyText) && Enum.TryParse<Keys>(keyText, out var result))
		{
			task.TargetKey = result;
			task.DisplayText = result.ToString();
			ushort item = (ushort)MapVirtualKey((uint)result, 0u);
			bool item2 = IsExtendedKey(result);
			textBox.Tag = new Tuple<ushort, bool>(item, item2);
		}
		textBox.KeyDown += txtKey_KeyDown;
		textBox.KeyUp += txtKey_KeyUp;
		textBox.PreviewKeyDown += txtKey_PreviewKeyDown;
		textBox.MouseDoubleClick += delegate
		{
			ClearKeyTask(task);
		};
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		contextMenuStrip.Items.Add("이 칸 초기화하기", null, delegate
		{
			ClearKeyTask(task);
		});
		textBox.ContextMenuStrip = contextMenuStrip;
		button.Click += delegate
		{
			RemoveKeyTaskRow(task);
		};
		keyTasks.Add(task);
		rowsPanel.Controls.Add(panel);
		RefreshRowNumbers();
		StyleControl(panel);
		if (dashboardReady) LayoutKeyRow(task, Math.Max(600, keyCard.Width - 48));
	}

	private TextBox CreateRowTextBox(int x, string text, int width)
	{
		return new TextBox
		{
			Location = new Point(x, 5),
			Size = new Size(width, 27),
			Text = text,
			Font = new Font("맑은 고딕", 9f)
		};
	}

	private void RemoveKeyTaskRow(KeyTask task)
	{
		if (!btnStart.Enabled)
		{
			MessageBox.Show("실행 중에는 항목을 삭제할 수 없습니다. 먼저 중지하세요.");
			return;
		}
		if (keyTasks.Count <= 1)
		{
			MessageBox.Show("최소 1개 항목은 남겨두어야 합니다.");
			return;
		}
		keyTasks.Remove(task);
		rowsPanel.Controls.Remove(task.RowPanel);
		task.RowPanel.Dispose();
		RefreshRowNumbers();
		lblStatus.Text = "상태: 항목이 삭제되었습니다.";
	}

	private void RefreshRowNumbers()
	{
		for (int i = 0; i < keyTasks.Count; i++)
		{
			keyTasks[i].NoLabel.Text = (i + 1).ToString();
		}
	}

	private void ClearKeyTask(KeyTask task)
	{
		task.TargetKey = Keys.None;
		task.DisplayText = "";
		task.IntervalMinSeconds = 0.0;
		task.IntervalMaxSeconds = 0.0;
		task.DelayMinMs = 0;
		task.DelayMaxMs = 0;
		task.HoldMinMs = 25;
		task.HoldMaxMs = 25;
		task.IsRunning = false;
		task.KeyBox.Text = "";
		task.KeyBox.Tag = null;
		task.IntervalMinBox.Text = "";
		task.IntervalMaxBox.Text = "";
		task.DelayMinBox.Text = "";
		task.DelayMaxBox.Text = "";
		task.HoldMinBox.Text = "";
		task.HoldMaxBox.Text = "";
		lblStatus.Text = "상태: 선택한 항목이 초기화되었습니다.";
	}

	private void txtStartHotkey_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
	{
		if (e.KeyCode == Keys.Prior || e.KeyCode == Keys.Next || e.KeyCode == Keys.Home || e.KeyCode == Keys.End || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Delete || e.KeyCode == Keys.Insert || e.KeyCode == Keys.Tab)
		{
			e.IsInputKey = true;
		}
	}

	private void txtStartHotkey_KeyDown(object sender, KeyEventArgs e)
	{
		e.SuppressKeyPress = true;
		e.Handled = true;
		if (e.KeyCode == Keys.Escape)
		{
			MessageBox.Show("ESC는 전체 중단 전용 키라서 시작 단축키로 사용할 수 없습니다.");
			return;
		}
		globalStartKey = e.KeyCode;
		txtStartHotkey.Text = globalStartKey.ToString();
		lblStatus.Text = $"상태: 전체 시작 단축키 설정됨 → {globalStartKey}";
	}

	private void txtStartHotkey_KeyUp(object sender, KeyEventArgs e)
	{
		lblStatus.Focus();
	}

	private void ClearGlobalStartHotkey()
	{
		globalStartKey = Keys.None;
		txtStartHotkey.Text = "";
		lblStatus.Text = "상태: 전체 시작 단축키가 초기화되었습니다.";
	}

	private void StartAllByHotkey()
	{
		bool flag = false;
		if (btnStart.Enabled)
		{
			btnStart.PerformClick();
			if (!btnStart.Enabled)
			{
				flag = true;
			}
		}
		if (btnTextStart.Enabled && macroActions.Count > 0)
		{
			btnTextStart.PerformClick();
			if (!btnTextStart.Enabled)
			{
				flag = true;
			}
		}
		if (!flag)
		{
			lblStatus.Text = "상태: 이미 실행 중이거나 시작할 항목이 없습니다.";
		}
	}

	private void btnAddRow_Click(object sender, EventArgs e)
	{
		if (!btnStart.Enabled)
		{
			MessageBox.Show("실행 중에는 항목을 추가할 수 없습니다. 먼저 중지하세요.");
			return;
		}
		AddKeyTaskRow();
		lblStatus.Text = "상태: 새 항목이 추가되었습니다.";
	}

	private void txtKey_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
	{
		if (e.KeyCode == Keys.Prior || e.KeyCode == Keys.Next || e.KeyCode == Keys.Home || e.KeyCode == Keys.End || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Delete || e.KeyCode == Keys.Insert || e.KeyCode == Keys.Tab)
		{
			e.IsInputKey = true;
		}
	}

	private void txtKey_KeyDown(object sender, KeyEventArgs e)
	{
		TextBox currentBox = sender as TextBox;
		if (currentBox != null)
		{
			KeyTask keyTask = keyTasks.Find((KeyTask task) => task.KeyBox == currentBox);
			if (keyTask != null)
			{
				e.SuppressKeyPress = true;
				e.Handled = true;
				keyTask.TargetKey = e.KeyCode;
				keyTask.DisplayText = e.KeyCode.ToString();
				ushort item = (ushort)MapVirtualKey((uint)e.KeyCode, 0u);
				bool item2 = IsExtendedKey(e.KeyCode);
				currentBox.Tag = new Tuple<ushort, bool>(item, item2);
				currentBox.Text = keyTask.DisplayText;
			}
		}
	}

	private void txtKey_KeyUp(object sender, KeyEventArgs e)
	{
		lblStatus.Focus();
	}

	private bool IsExtendedKey(Keys key)
	{
		if (key != Keys.Up && key != Keys.Down && key != Keys.Left && key != Keys.Right && key != Keys.Home && key != Keys.End && key != Keys.Prior && key != Keys.Next && key != Keys.Delete)
		{
			return key == Keys.Insert;
		}
		return true;
	}

	private bool TryParseSeconds(string text, out double seconds)
	{
		text = text.Trim().Replace(",", ".");
		return double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out seconds);
	}

	private bool ValidateRangeSeconds(string title, TextBox minBox, TextBox maxBox, out double min, out double max)
	{
		min = 0.0;
		max = 0.0;
		if (string.IsNullOrWhiteSpace(minBox.Text) || string.IsNullOrWhiteSpace(maxBox.Text))
		{
			MessageBox.Show(title + " 최소/최대 초를 모두 입력하세요.");
			return false;
		}
		if (!TryParseSeconds(minBox.Text, out min))
		{
			MessageBox.Show(title + " 최소 초는 숫자로 입력하세요.");
			return false;
		}
		if (!TryParseSeconds(maxBox.Text, out max))
		{
			MessageBox.Show(title + " 최대 초는 숫자로 입력하세요.");
			return false;
		}
		if (min <= 0.0 || max <= 0.0)
		{
			MessageBox.Show(title + " 값은 0보다 크게 입력하세요.");
			return false;
		}
		if (min > max)
		{
			MessageBox.Show(title + " 최소값은 최대값보다 작거나 같아야 합니다.");
			return false;
		}
		return true;
	}

	private bool ValidateRangeMs(string title, TextBox minBox, TextBox maxBox, out int min, out int max)
	{
		min = 0;
		max = 0;
		if (string.IsNullOrWhiteSpace(minBox.Text) || string.IsNullOrWhiteSpace(maxBox.Text))
		{
			MessageBox.Show(title + " 최소/최대 ms를 모두 입력하세요.");
			return false;
		}
		if (!int.TryParse(minBox.Text.Trim(), out min))
		{
			MessageBox.Show(title + " 최소 ms는 숫자로 입력하세요.");
			return false;
		}
		if (!int.TryParse(maxBox.Text.Trim(), out max))
		{
			MessageBox.Show(title + " 최대 ms는 숫자로 입력하세요.");
			return false;
		}
		if (min < 0 || max < 0)
		{
			MessageBox.Show(title + " 값은 0 이상으로 입력하세요.");
			return false;
		}
		if (min > max)
		{
			MessageBox.Show(title + " 최소값은 최대값보다 작거나 같아야 합니다.");
			return false;
		}
		return true;
	}

	private int GetRandomMs(int min, int max)
	{
		if (min == max)
		{
			return min;
		}
		return random.Next(min, max + 1);
	}

	private double GetRandomSeconds(double min, double max)
	{
		if (Math.Abs(min - max) < 0.0001)
		{
			return min;
		}
		return min + random.NextDouble() * (max - min);
	}

	private double GetNextTextRepeatSeconds()
	{
		return GetRandomSeconds(textRepeatMinSeconds, textRepeatMaxSeconds);
	}

	private void ScheduleNextTextReplay()
	{
		double nextTextRepeatSeconds = GetNextTextRepeatSeconds();
		timerText.Interval = Math.Max(1, (int)(nextTextRepeatSeconds * 1000.0));
		timerText.Start();
		lblTextStatus.Text = $"문장 상태: 다음 입력까지 {nextTextRepeatSeconds:0.00}초 / 다음 키 딜레이 {textKeyDelayMinMs}~{textKeyDelayMaxMs}ms / 누름 {textHoldMinMs}~{textHoldMaxMs}ms";
	}

	private bool IsSpecialRecordKey(Keys key)
	{
		if (key != Keys.Up && key != Keys.Down && key != Keys.Left && key != Keys.Right && key != Keys.Return && key != Keys.Tab && key != Keys.Back && key != Keys.Delete && key != Keys.Home && key != Keys.End && key != Keys.Prior)
		{
			return key == Keys.Next;
		}
		return true;
	}

	private void btnStart_Click(object sender, EventArgs e)
	{
		if (!CheckHidReady(false)) return;
		stopRequested = false;
		ResetKeyCancelSource();
		int num = 0;
		foreach (KeyTask keyTask in keyTasks)
		{
			bool flag = keyTask.TargetKey != Keys.None;
			bool flag2 = !string.IsNullOrWhiteSpace(keyTask.IntervalMinBox.Text);
			bool flag3 = !string.IsNullOrWhiteSpace(keyTask.IntervalMaxBox.Text);
			bool flag4 = !string.IsNullOrWhiteSpace(keyTask.DelayMinBox.Text);
			bool flag5 = !string.IsNullOrWhiteSpace(keyTask.DelayMaxBox.Text);
			bool flag6 = !string.IsNullOrWhiteSpace(keyTask.HoldMinBox.Text);
			bool flag7 = !string.IsNullOrWhiteSpace(keyTask.HoldMaxBox.Text);
			if (flag || flag2 || flag3 || flag4 || flag5 || flag6 || flag7)
			{
				if (!flag)
				{
					MessageBox.Show("키 설정 없이 값만 입력된 항목이 있습니다. 키를 먼저 선택하세요.");
					return;
				}
				if (!ValidateRangeSeconds(keyTask.DisplayText + " 반복 주기", keyTask.IntervalMinBox, keyTask.IntervalMaxBox, out var min, out var max) || !ValidateRangeMs(keyTask.DisplayText + " 딜레이", keyTask.DelayMinBox, keyTask.DelayMaxBox, out var min2, out var max2) || !ValidateRangeMs(keyTask.DisplayText + " 누름 시간", keyTask.HoldMinBox, keyTask.HoldMaxBox, out var min3, out var max3))
				{
					return;
				}
				if (min3 <= 0 || max3 <= 0)
				{
					MessageBox.Show("누름 시간은 1ms 이상 입력하세요.");
					return;
				}
				keyTask.IntervalMinSeconds = min;
				keyTask.IntervalMaxSeconds = max;
				keyTask.DelayMinMs = min2;
				keyTask.DelayMaxMs = max2;
				keyTask.HoldMinMs = min3;
				keyTask.HoldMaxMs = max3;
				keyTask.NextRunTime = DateTime.Now;
				keyTask.IsRunning = false;
				num++;
			}
		}
		if (num == 0)
		{
			MessageBox.Show("최소 1개 이상의 키와 값을 입력하세요.");
			return;
		}
		timer1.Interval = 50;
		timer1.Start();
		btnStart.Enabled = false;
		btnStop.Enabled = true;
		btnAddRow.Enabled = false;
		SetKeyInputsEnabled(enabled: false);
		lblStatus.Text = $"상태: 실행 중 / {num}개 키 랜덤 동작";
	}

	private void btnStop_Click(object sender, EventArgs e)
	{
		StopKeyRepeatOnly();
	}

	private void timer1_Tick(object sender, EventArgs e)
	{
		if (stopRequested)
		{
			return;
		}
		DateTime now = DateTime.Now;
		foreach (KeyTask keyTask in keyTasks)
		{
			if (stopRequested)
			{
				break;
			}
			if (keyTask.TargetKey != Keys.None && !keyTask.IsRunning && now >= keyTask.NextRunTime)
			{
				int randomMs = GetRandomMs(keyTask.DelayMinMs, keyTask.DelayMaxMs);
				int randomMs2 = GetRandomMs(keyTask.HoldMinMs, keyTask.HoldMaxMs);
				double randomSeconds = GetRandomSeconds(keyTask.IntervalMinSeconds, keyTask.IntervalMaxSeconds);
				_ = RunKeyTaskAsync(keyTask, randomMs, randomMs2, randomSeconds, keyCancelSource.Token);
			}
		}
	}

	private async Task RunKeyTaskAsync(KeyTask task, int delayMs, int holdMs, double nextIntervalSeconds, CancellationToken token)
	{
		task.IsRunning = true;
		try
		{
			lblStatus.Text = $"상태: {task.DisplayText} 준비 / 딜레이 {delayMs}ms";
			if (delayMs > 0)
			{
				await Task.Delay(delayMs, token);
			}
			token.ThrowIfCancellationRequested();
			await SendScanCodeKeyAsync(task.TargetKey, holdMs, task, token);
			if (!stopRequested && !token.IsCancellationRequested)
			{
				lblStatus.Text = $"상태: {DateTime.Now:HH:mm:ss} {task.DisplayText} 입력 / 딜레이 {delayMs}ms / 누름 {holdMs}ms / 다음 {nextIntervalSeconds:0.00}초 후";
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			if (!stopRequested)
			{
				StopAllByEsc(); lblStatus.Text = "상태: " + ex.Message;
			}
		}
		finally
		{
			task.IsRunning = false;
			if (!stopRequested && !token.IsCancellationRequested)
			{
				task.NextRunTime = DateTime.Now.AddMilliseconds(nextIntervalSeconds * 1000.0);
			}
		}
	}

	private void SetKeyInputsEnabled(bool enabled)
	{
		foreach (KeyTask keyTask in keyTasks)
		{
			keyTask.KeyBox.Enabled = enabled;
			keyTask.IntervalMinBox.Enabled = enabled;
			keyTask.IntervalMaxBox.Enabled = enabled;
			keyTask.DelayMinBox.Enabled = enabled;
			keyTask.DelayMaxBox.Enabled = enabled;
			keyTask.HoldMinBox.Enabled = enabled;
			keyTask.HoldMaxBox.Enabled = enabled;
			keyTask.DeleteButton.Enabled = enabled;
		}
	}

	private void PauseKeyRepeatOnlyDuringTextInput()
	{
		keyRepeatWasRunningBeforeText = timer1.Enabled;
		if (!keyRepeatWasRunningBeforeText)
		{
			keyRepeatPausedByText = false;
			return;
		}
		keyRepeatPausedByText = true;
		timer1.Stop();
		keyCancelSource.Cancel();
		ForceReleaseAllPressedKeys();
		ResetKeyCancelSource();
		foreach (KeyTask keyTask in keyTasks)
		{
			keyTask.IsRunning = false;
		}
		lblStatus.Text = "상태: 문장/여러 키 입력 중 → 개별 키 반복 OFF";
	}

	private void ResumeKeyRepeatAfterTextInput()
	{
		if (!keyRepeatPausedByText || !keyRepeatWasRunningBeforeText)
		{
			keyRepeatPausedByText = false;
			keyRepeatWasRunningBeforeText = false;
			return;
		}
		ResetKeyCancelSource();
		foreach (KeyTask keyTask in keyTasks)
		{
			if (keyTask.TargetKey != Keys.None)
			{
				keyTask.IsRunning = false;
				keyTask.NextRunTime = DateTime.Now;
			}
		}
		timer1.Interval = 50;
		timer1.Start();
		btnStart.Enabled = false;
		btnStop.Enabled = true;
		btnAddRow.Enabled = false;
		SetKeyInputsEnabled(enabled: false);
		keyRepeatPausedByText = false;
		keyRepeatWasRunningBeforeText = false;
		lblStatus.Text = "상태: 문장/여러 키 입력 완료 → 개별 키 반복 재시작";
	}

	private void btnRecordStart_Click(object sender, EventArgs e)
	{
		stopRequested = false;
		ResetTextCancelSource();
		isRecordingText = true;
		macroActions.Clear();
		recordedText = "";
		txtRecordedText.ReadOnly = false;
		txtRecordedText.Clear();
		txtRecordedText.Focus();
		btnRecordStart.Enabled = false;
		btnRecordStop.Enabled = true;
		btnTextStart.Enabled = false;
		lblTextStatus.Text = "문장 상태: 입력 기록 중 / ESC 누르면 전체 중단";
	}

	private void btnRecordStop_Click(object sender, EventArgs e)
	{
		StopRecordingOnly();
	}

	private void txtRecordedText_KeyDown(object sender, KeyEventArgs e)
	{
		if (isRecordingText && IsSpecialRecordKey(e.KeyCode))
		{
			macroActions.Add(new MacroAction
			{
				Type = "KEY",
				SpecialKey = e.KeyCode,
				Display = e.KeyCode.ToString()
			});
		}
	}

	private void txtRecordedText_KeyPress(object sender, KeyPressEventArgs e)
	{
		if (isRecordingText && !char.IsControl(e.KeyChar))
		{
			macroActions.Add(new MacroAction
			{
				Type = "TEXT",
				TextChar = e.KeyChar,
				Display = e.KeyChar.ToString()
			});
		}
	}

	private void btnTextStart_Click(object sender, EventArgs e)
	{
		if (!CheckHidReady(true)) return;
		double min;
		double max;
		int min2;
		int max2;
		int min3;
		int max3;
		if (macroActions.Count == 0)
		{
			MessageBox.Show("반복할 입력 기록을 먼저 만들어주세요.");
		}
		else if (ValidateRangeSeconds("문장/여러 키 반복 주기", txtTextInterval, txtTextIntervalMax, out min, out max) && ValidateRangeMs("문장/여러 키 다음 키 딜레이", txtTextKeyDelayMin, txtTextKeyDelayMax, out min2, out max2) && ValidateRangeMs("문장/여러 키 누름 시간", txtTextHoldMin, txtTextHoldMax, out min3, out max3))
		{
			if (min3 <= 0 || max3 <= 0)
			{
				MessageBox.Show("문장/여러 키 누름 시간은 1ms 이상 입력하세요.");
				return;
			}
			textRepeatMinSeconds = min;
			textRepeatMaxSeconds = max;
			textKeyDelayMinMs = min2;
			textKeyDelayMaxMs = max2;
			textHoldMinMs = min3;
			textHoldMaxMs = max3;
			stopRequested = false;
			ResetTextCancelSource();
			txtRecordedText.Enabled = false;
			txtTextInterval.Enabled = false;
			txtTextIntervalMax.Enabled = false;
			txtTextKeyDelayMin.Enabled = false;
			txtTextKeyDelayMax.Enabled = false;
			txtTextHoldMin.Enabled = false;
			txtTextHoldMax.Enabled = false;
			btnTextStart.Enabled = false;
			btnTextStop.Enabled = true;
			btnRecordStart.Enabled = false;
			btnRecordStop.Enabled = false;
			ScheduleNextTextReplay();
		}
	}

	private void btnTextStop_Click(object sender, EventArgs e)
	{
		StopTextRepeatOnly();
	}

	private async void timerText_Tick(object sender, EventArgs e)
	{
		if (isReplayingText || stopRequested)
		{
			return;
		}
		timerText.Stop();
		isReplayingText = true;
		try
		{
			PauseKeyRepeatOnlyDuringTextInput();
			await ReplayMacroActions(textCancelSource.Token);
			if (!stopRequested && !textCancelSource.Token.IsCancellationRequested)
			{
				lblTextStatus.Text = $"문장 상태: {DateTime.Now:HH:mm:ss} 입력 완료";
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			if (!stopRequested)
			{
				StopAllByEsc(); lblTextStatus.Text = "문장 상태: " + ex.Message;
			}
		}
		finally
		{
			isReplayingText = false;
			if (!stopRequested && !textCancelSource.Token.IsCancellationRequested && btnTextStop.Enabled)
			{
				ResumeKeyRepeatAfterTextInput();
				ScheduleNextTextReplay();
			}
		}
	}

	private async Task ReplayMacroActions(CancellationToken token)
	{
		for (int i = 0; i < macroActions.Count; i++)
		{
			token.ThrowIfCancellationRequested();
			MacroAction macroAction = macroActions[i];
			int randomMs = GetRandomMs(textHoldMinMs, textHoldMaxMs);
			if (macroAction.Type == "TEXT")
			{
				await SendHidCharacterAsync(macroAction.TextChar, randomMs, token);
			}
			else if (macroAction.Type == "KEY")
			{
				await SendScanCodeKeyAsync(macroAction.SpecialKey, randomMs, null, token);
			}
			if (i < macroActions.Count - 1)
			{
				await Task.Delay(GetRandomMs(textKeyDelayMinMs, textKeyDelayMaxMs), token);
			}
		}
	}

	private void StopKeyRepeatOnly()
	{
		keyCancelSource.Cancel();
		timer1.Stop();
		ForceReleaseAllPressedKeys();
		btnStart.Enabled = true;
		btnStop.Enabled = false;
		btnAddRow.Enabled = true;
		SetKeyInputsEnabled(enabled: true);
		foreach (KeyTask keyTask in keyTasks)
		{
			keyTask.IsRunning = false;
		}
		keyRepeatPausedByText = false;
		keyRepeatWasRunningBeforeText = false;
		lblStatus.Text = "상태: 개별 키 반복 중지됨";
	}

	private void StopRecordingOnly()
	{
		isRecordingText = false;
		recordedText = txtRecordedText.Text;
		txtRecordedText.ReadOnly = true;
		bool flag = false;
		foreach (MacroAction macroAction in macroActions)
		{
			if (macroAction.Type == "TEXT")
			{
				flag = true;
				break;
			}
		}
		if (!flag && !string.IsNullOrEmpty(recordedText))
		{
			string text = recordedText;
			for (int i = 0; i < text.Length; i++)
			{
				char textChar = text[i];
				macroActions.Add(new MacroAction
				{
					Type = "TEXT",
					TextChar = textChar,
					Display = textChar.ToString()
				});
			}
		}
		btnRecordStart.Enabled = true;
		btnRecordStop.Enabled = false;
		btnTextStart.Enabled = true;
		lblTextStatus.Text = $"문장 상태: 입력 완료 / 글자 {recordedText.Length}개 / 동작 {macroActions.Count}개";
	}

	private void StopTextRepeatOnly()
	{
		textCancelSource.Cancel();
		timerText.Stop();
		if (isReplayingText || keyRepeatPausedByText)
		{
			ForceReleaseAllPressedKeys();
		}
		txtRecordedText.Enabled = true;
		txtTextInterval.Enabled = true;
		txtTextIntervalMax.Enabled = true;
		txtTextKeyDelayMin.Enabled = true;
		txtTextKeyDelayMax.Enabled = true;
		txtTextHoldMin.Enabled = true;
		txtTextHoldMax.Enabled = true;
		btnTextStart.Enabled = true;
		btnTextStop.Enabled = false;
		btnRecordStart.Enabled = true;
		lblTextStatus.Text = "문장 상태: 중지됨";
		ResumeKeyRepeatAfterTextInput();
	}

	private void StopAllByEsc()
	{
		stopRequested = true;
		keyRepeatPausedByText = false;
		keyRepeatWasRunningBeforeText = false;
		keyCancelSource.Cancel();
		textCancelSource.Cancel();
		timer1.Stop();
		timerText.Stop();
		ForceReleaseAllPressedKeys();
		liveInput.Clear();
		isRecordingText = false;
		isReplayingText = false;
		btnStart.Enabled = true;
		btnStop.Enabled = false;
		btnAddRow.Enabled = true;
		SetKeyInputsEnabled(enabled: true);
		foreach (KeyTask keyTask in keyTasks)
		{
			keyTask.IsRunning = false;
		}
		txtRecordedText.ReadOnly = true;
		txtRecordedText.Enabled = true;
		txtTextInterval.Enabled = true;
		txtTextIntervalMax.Enabled = true;
		txtTextKeyDelayMin.Enabled = true;
		txtTextKeyDelayMax.Enabled = true;
		txtTextHoldMin.Enabled = true;
		txtTextHoldMax.Enabled = true;
		btnRecordStart.Enabled = true;
		btnRecordStop.Enabled = false;
		btnTextStart.Enabled = macroActions.Count > 0;
		btnTextStop.Enabled = false;
		lblStatus.Text = "상태: ESC로 전체 즉시 중단됨 / 눌린 키 해제 완료";
		lblTextStatus.Text = "문장 상태: ESC로 전체 즉시 중단됨";
	}

	private PressedKeyInfo RemovePressedKeyIfExists(Keys key, ushort scanCode, bool isExtended)
	{
		lock (pressedKeyLock)
		{
			for (int num = pressedKeys.Count - 1; num >= 0; num--)
			{
				if (pressedKeys[num].Key == key && pressedKeys[num].ScanCode == scanCode && pressedKeys[num].IsExtended == isExtended)
				{
					PressedKeyInfo result = pressedKeys[num];
					pressedKeys.RemoveAt(num);
					return result;
				}
			}
		}
		return null;
	}

	private Guid RegisterPressedKey(Keys key, ushort scanCode, bool isExtended)
	{
		Guid guid = Guid.NewGuid();
		lock (pressedKeyLock)
		{
			pressedKeys.Add(new PressedKeyInfo
			{
				PressId = guid,
				Key = key,
				ScanCode = scanCode,
				IsExtended = isExtended
			});
			liveInput.Begin(VisualKey(key, isExtended), key == Keys.Enter && isExtended ? "Num Enter" : key.ToString(), guid);
			return guid;
		}
	}

	private bool UnregisterPressedKey(Guid pressId)
	{
		lock (pressedKeyLock)
		{
			for (int num = pressedKeys.Count - 1; num >= 0; num--)
			{
				if (pressedKeys[num].PressId == pressId)
				{
					pressedKeys.RemoveAt(num);
					return true;
				}
			}
		}
		return false;
	}

	private void ForceReleaseAllPressedKeys()
	{
		try { hidKeyboard.ReleaseAll(); } catch { hidKeyboard.Dispose(); }
		
		List<PressedKeyInfo> list;
		lock (pressedKeyLock)
		{
			list = new List<PressedKeyInfo>(pressedKeys);
			pressedKeys.Clear();
		}
		foreach (PressedKeyInfo item in list)
		{
			SendKeyUp(item.Key, item.ScanCode, item.IsExtended);
		}
	}

	private void SendKeyUp(Keys key, ushort scanCode, bool isExtended)
	{
		try { hidKeyboard.Up(HidKeys.Usage(key,isExtended)); }
		catch(Exception ex) { hidKeyboard.Dispose();lblStatus.Text="상태: 키 해제 실패 · "+ex.Message; }
		liveInput.EndKey(VisualKey(key,isExtended));
	}
	private nint SetHook(LowLevelKeyboardProc proc)
	{
		using Process process = Process.GetCurrentProcess();
		using ProcessModule processModule = process.MainModule;
		return SetWindowsHookEx(13, proc, GetModuleHandle(processModule.ModuleName), 0u);
	}

	private nint HookCallback(int nCode, nint wParam, nint lParam)
	{
		// Do not let generated input trigger our global start shortcut again.
		if (nCode >= 0 && (Marshal.ReadInt32(lParam, 8) & 0x10) != 0)
			return CallNextHookEx(hookId, nCode, wParam, lParam);
		if (nCode >= 0 && (wParam == 256 || wParam == 260))
		{
			Keys keys = (Keys)Marshal.ReadInt32(lParam);
			if (keys == Keys.Escape)
			{
				if (!base.IsDisposed && base.IsHandleCreated)
				{
					BeginInvoke(StopAllByEsc);
				}
				return 1;
			}
			if (globalStartKey != Keys.None && keys == globalStartKey)
			{
				if (txtStartHotkey != null && txtStartHotkey.Focused)
				{
					return CallNextHookEx(hookId, nCode, wParam, lParam);
				}
				if (!base.IsDisposed && base.IsHandleCreated)
				{
					BeginInvoke(StartAllByHotkey);
				}
				return 1;
			}
		}
		return CallNextHookEx(hookId, nCode, wParam, lParam);
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		SaveLastSettings();
		try
		{
			stopRequested = true;
			keyCancelSource.Cancel();
			textCancelSource.Cancel();
			ForceReleaseAllPressedKeys();
			liveInput.Clear();
			keyCancelSource.Dispose();
			textCancelSource.Dispose();
		}
		catch
		{
		}
		if (hookId != IntPtr.Zero)
		{
			UnhookWindowsHookEx(hookId);
			hookId = IntPtr.Zero;
		}
		
		hidKeyboard.Disconnected -= HidDisconnected;
		hidKeyboard.Dispose();
		base.OnFormClosing(e);
	}

	private async Task SendScanCodeKeyAsync(Keys key, int holdMs, KeyTask sourceTask, CancellationToken token)
	{
		ushort scanCode = (ushort)MapVirtualKey((uint)key, 0u);
		bool extended = IsExtendedKey(key);
		if (sourceTask?.KeyBox.Tag is Tuple<ushort, bool> captured) { scanCode=captured.Item1; extended=captured.Item2; }
		byte usage=HidKeys.Usage(key,extended);
		Guid pressId=Guid.Empty;
		try
		{
			token.ThrowIfCancellationRequested();
			var previous=RemovePressedKeyIfExists(key,scanCode,extended);
			if(previous!=null) { SendKeyUp(previous.Key,previous.ScanCode,previous.IsExtended); await Task.Delay(GetRandomMs(10,100),token); }
			token.ThrowIfCancellationRequested();
			hidKeyboard.Down(usage);
			pressId=RegisterPressedKey(key,scanCode,extended);
			await Task.Delay(holdMs,token);
		}
		finally { if(pressId!=Guid.Empty && UnregisterPressedKey(pressId)) SendKeyUp(key,scanCode,extended); }
	}

	private async Task SendHidCharacterAsync(char ch, int holdMs, CancellationToken token)
	{
		var mapped=HidKeys.Character(ch,hidKeyboard.CapsLock);
		bool keyDown=false,shiftDown=false;
		Guid visual=Guid.Empty,shiftVisual=Guid.Empty;
		try
		{
			token.ThrowIfCancellationRequested();
			if(mapped.Shift) { hidKeyboard.Down(225);shiftDown=true;shiftVisual=liveInput.Begin(Keys.LShiftKey,"Shift"); }
			hidKeyboard.Down(mapped.Usage);keyDown=true;
			visual=liveInput.Begin(HidKeys.VisualKey(mapped.Usage),"문자  "+ch+" · HID");
			await Task.Delay(holdMs,token);
		}
		finally
		{
			try { if(keyDown)hidKeyboard.Up(mapped.Usage); }
			finally { try { if(shiftDown)hidKeyboard.Up(225); }finally{liveInput.End(visual);liveInput.End(shiftVisual);} }
		}
	}
	

	[DllImport("user32.dll")]
	private static extern uint MapVirtualKey(uint uCode, uint uMapType);

	[DllImport("user32.dll")]
	private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

	[DllImport("user32.dll")]
	private static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll")]
	private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("kernel32.dll")]
	private static extern nint GetModuleHandle(string lpModuleName);

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		this.components = new System.ComponentModel.Container();
		this.labelTitle = new System.Windows.Forms.Label();
		this.panelGuide = new System.Windows.Forms.Panel();
		this.labelGuide1 = new System.Windows.Forms.Label();
		this.labelGuide2 = new System.Windows.Forms.Label();
		this.labelStartHotkey = new System.Windows.Forms.Label();
		this.txtStartHotkey = new System.Windows.Forms.TextBox();
		this.labelStartHotkeyDesc = new System.Windows.Forms.Label();
		this.groupKeyRepeat = new System.Windows.Forms.GroupBox();
		this.headerPanel = new System.Windows.Forms.Panel();
		this.rowsPanel = new System.Windows.Forms.FlowLayoutPanel();
		this.labelHeaderNo = new System.Windows.Forms.Label();
		this.labelHeaderKey = new System.Windows.Forms.Label();
		this.labelHeaderIntervalMin = new System.Windows.Forms.Label();
		this.labelHeaderIntervalMax = new System.Windows.Forms.Label();
		this.labelHeaderDelayMin = new System.Windows.Forms.Label();
		this.labelHeaderDelayMax = new System.Windows.Forms.Label();
		this.labelHeaderHoldMin = new System.Windows.Forms.Label();
		this.labelHeaderHoldMax = new System.Windows.Forms.Label();
		this.labelHeaderDelete = new System.Windows.Forms.Label();
		this.btnAddRow = new System.Windows.Forms.Button();
		this.btnStart = new System.Windows.Forms.Button();
		this.btnStop = new System.Windows.Forms.Button();
		this.lblStatus = new System.Windows.Forms.Label();
		this.timer1 = new System.Windows.Forms.Timer(this.components);
		this.groupTextRepeat = new System.Windows.Forms.GroupBox();
		this.labelTextTitle = new System.Windows.Forms.Label();
		this.txtRecordedText = new System.Windows.Forms.TextBox();
		this.btnRecordStart = new System.Windows.Forms.Button();
		this.btnRecordStop = new System.Windows.Forms.Button();
		this.labelTextInterval = new System.Windows.Forms.Label();
		this.txtTextInterval = new System.Windows.Forms.TextBox();
		this.labelTextIntervalWave = new System.Windows.Forms.Label();
		this.txtTextIntervalMax = new System.Windows.Forms.TextBox();
		this.labelTextIntervalSec = new System.Windows.Forms.Label();
		this.labelTextKeyDelay = new System.Windows.Forms.Label();
		this.txtTextKeyDelayMin = new System.Windows.Forms.TextBox();
		this.labelTextKeyDelayWave = new System.Windows.Forms.Label();
		this.txtTextKeyDelayMax = new System.Windows.Forms.TextBox();
		this.labelTextKeyDelayMs = new System.Windows.Forms.Label();
		this.labelTextHold = new System.Windows.Forms.Label();
		this.txtTextHoldMin = new System.Windows.Forms.TextBox();
		this.labelTextHoldWave = new System.Windows.Forms.Label();
		this.txtTextHoldMax = new System.Windows.Forms.TextBox();
		this.labelTextHoldMs = new System.Windows.Forms.Label();
		this.btnTextStart = new System.Windows.Forms.Button();
		this.btnTextStop = new System.Windows.Forms.Button();
		this.lblTextStatus = new System.Windows.Forms.Label();
		this.timerText = new System.Windows.Forms.Timer(this.components);
		base.SuspendLayout();
		base.AutoScaleDimensions = new System.Drawing.SizeF(9f, 20f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		this.BackColor = System.Drawing.Color.FromArgb(245, 247, 250);
		base.ClientSize = new System.Drawing.Size(1440, 850);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
		base.MaximizeBox = false;
		base.Name = "Form1";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "AutoKeyPress - Dynamic Random Version";
		this.labelTitle.AutoSize = true;
		this.labelTitle.Font = new System.Drawing.Font("맑은 고딕", 17f, System.Drawing.FontStyle.Bold);
		this.labelTitle.ForeColor = System.Drawing.Color.FromArgb(33, 37, 41);
		this.labelTitle.Location = new System.Drawing.Point(30, 20);
		this.labelTitle.Name = "labelTitle";
		this.labelTitle.Size = new System.Drawing.Size(426, 40);
		this.labelTitle.TabIndex = 0;
		this.labelTitle.Text = "AutoKeyPress 랜덤 주기 설정";
		this.panelGuide.BackColor = System.Drawing.Color.FromArgb(232, 244, 255);
		this.panelGuide.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
		this.panelGuide.Location = new System.Drawing.Point(30, 75);
		this.panelGuide.Name = "panelGuide";
		this.panelGuide.Size = new System.Drawing.Size(1080, 95);
		this.panelGuide.TabIndex = 1;
		this.labelGuide1.AutoSize = true;
		this.labelGuide1.Font = new System.Drawing.Font("맑은 고딕", 9.5f, System.Drawing.FontStyle.Bold);
		this.labelGuide1.ForeColor = System.Drawing.Color.FromArgb(0, 95, 160);
		this.labelGuide1.Location = new System.Drawing.Point(18, 10);
		this.labelGuide1.Name = "labelGuide1";
		this.labelGuide1.Size = new System.Drawing.Size(820, 21);
		this.labelGuide1.TabIndex = 2;
		this.labelGuide1.Text = "개별 키: 반복 주기/딜레이/누름 시간 랜덤 / 문장 입력 시 개별 키는 잠깐 OFF 후 처음부터 다시 시작";
		this.labelGuide2.AutoSize = true;
		this.labelGuide2.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.labelGuide2.ForeColor = System.Drawing.Color.FromArgb(70, 70, 70);
		this.labelGuide2.Location = new System.Drawing.Point(18, 35);
		this.labelGuide2.Name = "labelGuide2";
		this.labelGuide2.Size = new System.Drawing.Size(579, 20);
		this.labelGuide2.TabIndex = 3;
		this.labelGuide2.Text = "ESC: 전체 즉시 중단 + 눌린 키 해제 / + 항목 추가 / 삭제 / 스크롤 지원";
		this.labelStartHotkey.AutoSize = true;
		this.labelStartHotkey.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelStartHotkey.ForeColor = System.Drawing.Color.FromArgb(13, 110, 253);
		this.labelStartHotkey.Location = new System.Drawing.Point(18, 62);
		this.labelStartHotkey.Name = "labelStartHotkey";
		this.labelStartHotkey.Size = new System.Drawing.Size(89, 20);
		this.labelStartHotkey.TabIndex = 4;
		this.labelStartHotkey.Text = "전체 시작키";
		this.txtStartHotkey.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.txtStartHotkey.Location = new System.Drawing.Point(112, 58);
		this.txtStartHotkey.Name = "txtStartHotkey";
		this.txtStartHotkey.PlaceholderText = "키 입력";
		this.txtStartHotkey.ReadOnly = true;
		this.txtStartHotkey.Size = new System.Drawing.Size(90, 27);
		this.txtStartHotkey.TabIndex = 5;
		this.labelStartHotkeyDesc.AutoSize = true;
		this.labelStartHotkeyDesc.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.labelStartHotkeyDesc.ForeColor = System.Drawing.Color.FromArgb(70, 70, 70);
		this.labelStartHotkeyDesc.Location = new System.Drawing.Point(215, 62);
		this.labelStartHotkeyDesc.Name = "labelStartHotkeyDesc";
		this.labelStartHotkeyDesc.Size = new System.Drawing.Size(440, 20);
		this.labelStartHotkeyDesc.TabIndex = 6;
		this.labelStartHotkeyDesc.Text = "여기에 키를 지정하면 해당 키로 전체 시작 / ESC는 전체 중단";
		this.panelGuide.Controls.Add(this.labelGuide1);
		this.panelGuide.Controls.Add(this.labelGuide2);
		this.panelGuide.Controls.Add(this.labelStartHotkey);
		this.panelGuide.Controls.Add(this.txtStartHotkey);
		this.panelGuide.Controls.Add(this.labelStartHotkeyDesc);
		this.groupKeyRepeat.BackColor = System.Drawing.Color.White;
		this.groupKeyRepeat.Font = new System.Drawing.Font("맑은 고딕", 10f, System.Drawing.FontStyle.Bold);
		this.groupKeyRepeat.ForeColor = System.Drawing.Color.FromArgb(52, 58, 64);
		this.groupKeyRepeat.Location = new System.Drawing.Point(30, 185);
		this.groupKeyRepeat.Name = "groupKeyRepeat";
		this.groupKeyRepeat.Size = new System.Drawing.Size(1110, 455);
		this.groupKeyRepeat.TabIndex = 4;
		this.groupKeyRepeat.TabStop = false;
		this.groupKeyRepeat.Text = "개별 키 반복 설정";
		this.headerPanel.BackColor = System.Drawing.Color.FromArgb(248, 249, 250);
		this.headerPanel.Location = new System.Drawing.Point(18, 32);
		this.headerPanel.Name = "headerPanel";
		this.headerPanel.Size = new System.Drawing.Size(1070, 38);
		this.headerPanel.TabIndex = 5;
		this.labelHeaderNo.AutoSize = true;
		this.labelHeaderNo.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelHeaderNo.ForeColor = System.Drawing.Color.FromArgb(108, 117, 125);
		this.labelHeaderNo.Location = new System.Drawing.Point(12, 9);
		this.labelHeaderNo.Name = "labelHeaderNo";
		this.labelHeaderNo.Size = new System.Drawing.Size(37, 20);
		this.labelHeaderNo.Text = "번호";
		this.labelHeaderKey.AutoSize = true;
		this.labelHeaderKey.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelHeaderKey.ForeColor = System.Drawing.Color.FromArgb(13, 110, 253);
		this.labelHeaderKey.Location = new System.Drawing.Point(70, 9);
		this.labelHeaderKey.Name = "labelHeaderKey";
		this.labelHeaderKey.Size = new System.Drawing.Size(69, 20);
		this.labelHeaderKey.Text = "누를 키";
		this.labelHeaderIntervalMin.AutoSize = true;
		this.labelHeaderIntervalMin.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelHeaderIntervalMin.ForeColor = System.Drawing.Color.FromArgb(25, 135, 84);
		this.labelHeaderIntervalMin.Location = new System.Drawing.Point(160, 9);
		this.labelHeaderIntervalMin.Name = "labelHeaderIntervalMin";
		this.labelHeaderIntervalMin.Size = new System.Drawing.Size(116, 20);
		this.labelHeaderIntervalMin.Text = "반복 최소(초)";
		this.labelHeaderIntervalMax.AutoSize = true;
		this.labelHeaderIntervalMax.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelHeaderIntervalMax.ForeColor = System.Drawing.Color.FromArgb(25, 135, 84);
		this.labelHeaderIntervalMax.Location = new System.Drawing.Point(270, 9);
		this.labelHeaderIntervalMax.Name = "labelHeaderIntervalMax";
		this.labelHeaderIntervalMax.Size = new System.Drawing.Size(116, 20);
		this.labelHeaderIntervalMax.Text = "반복 최대(초)";
		this.labelHeaderDelayMin.AutoSize = true;
		this.labelHeaderDelayMin.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelHeaderDelayMin.ForeColor = System.Drawing.Color.FromArgb(255, 132, 0);
		this.labelHeaderDelayMin.Location = new System.Drawing.Point(390, 9);
		this.labelHeaderDelayMin.Name = "labelHeaderDelayMin";
		this.labelHeaderDelayMin.Size = new System.Drawing.Size(116, 20);
		this.labelHeaderDelayMin.Text = "딜레이 최소(ms)";
		this.labelHeaderDelayMax.AutoSize = true;
		this.labelHeaderDelayMax.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelHeaderDelayMax.ForeColor = System.Drawing.Color.FromArgb(255, 132, 0);
		this.labelHeaderDelayMax.Location = new System.Drawing.Point(500, 9);
		this.labelHeaderDelayMax.Name = "labelHeaderDelayMax";
		this.labelHeaderDelayMax.Size = new System.Drawing.Size(116, 20);
		this.labelHeaderDelayMax.Text = "딜레이 최대(ms)";
		this.labelHeaderHoldMin.AutoSize = true;
		this.labelHeaderHoldMin.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelHeaderHoldMin.ForeColor = System.Drawing.Color.FromArgb(220, 53, 69);
		this.labelHeaderHoldMin.Location = new System.Drawing.Point(615, 9);
		this.labelHeaderHoldMin.Name = "labelHeaderHoldMin";
		this.labelHeaderHoldMin.Size = new System.Drawing.Size(116, 20);
		this.labelHeaderHoldMin.Text = "누름 최소(ms)";
		this.labelHeaderHoldMax.AutoSize = true;
		this.labelHeaderHoldMax.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelHeaderHoldMax.ForeColor = System.Drawing.Color.FromArgb(220, 53, 69);
		this.labelHeaderHoldMax.Location = new System.Drawing.Point(725, 9);
		this.labelHeaderHoldMax.Name = "labelHeaderHoldMax";
		this.labelHeaderHoldMax.Size = new System.Drawing.Size(116, 20);
		this.labelHeaderHoldMax.Text = "누름 최대(ms)";
		this.labelHeaderDelete.AutoSize = true;
		this.labelHeaderDelete.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelHeaderDelete.ForeColor = System.Drawing.Color.FromArgb(108, 117, 125);
		this.labelHeaderDelete.Location = new System.Drawing.Point(865, 9);
		this.labelHeaderDelete.Name = "labelHeaderDelete";
		this.labelHeaderDelete.Size = new System.Drawing.Size(39, 20);
		this.labelHeaderDelete.Text = "삭제";
		this.headerPanel.Controls.Add(this.labelHeaderNo);
		this.headerPanel.Controls.Add(this.labelHeaderKey);
		this.headerPanel.Controls.Add(this.labelHeaderIntervalMin);
		this.headerPanel.Controls.Add(this.labelHeaderIntervalMax);
		this.headerPanel.Controls.Add(this.labelHeaderDelayMin);
		this.headerPanel.Controls.Add(this.labelHeaderDelayMax);
		this.headerPanel.Controls.Add(this.labelHeaderHoldMin);
		this.headerPanel.Controls.Add(this.labelHeaderHoldMax);
		this.headerPanel.Controls.Add(this.labelHeaderDelete);
		this.rowsPanel.AutoScroll = true;
		this.rowsPanel.BackColor = System.Drawing.Color.FromArgb(250, 250, 250);
		this.rowsPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
		this.rowsPanel.Location = new System.Drawing.Point(18, 76);
		this.rowsPanel.Name = "rowsPanel";
		this.rowsPanel.Size = new System.Drawing.Size(1070, 285);
		this.rowsPanel.TabIndex = 6;
		this.rowsPanel.WrapContents = false;
		this.btnAddRow.BackColor = System.Drawing.Color.FromArgb(25, 135, 84);
		this.btnAddRow.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnAddRow.Font = new System.Drawing.Font("맑은 고딕", 10f, System.Drawing.FontStyle.Bold);
		this.btnAddRow.ForeColor = System.Drawing.Color.White;
		this.btnAddRow.Location = new System.Drawing.Point(20, 382);
		this.btnAddRow.Name = "btnAddRow";
		this.btnAddRow.Size = new System.Drawing.Size(150, 45);
		this.btnAddRow.TabIndex = 7;
		this.btnAddRow.Text = "+ 항목 추가";
		this.btnAddRow.UseVisualStyleBackColor = false;
		this.btnAddRow.Click += new System.EventHandler(btnAddRow_Click);
		this.btnStart.BackColor = System.Drawing.Color.FromArgb(13, 110, 253);
		this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnStart.Font = new System.Drawing.Font("맑은 고딕", 10f, System.Drawing.FontStyle.Bold);
		this.btnStart.ForeColor = System.Drawing.Color.White;
		this.btnStart.Location = new System.Drawing.Point(740, 382);
		this.btnStart.Name = "btnStart";
		this.btnStart.Size = new System.Drawing.Size(150, 45);
		this.btnStart.TabIndex = 8;
		this.btnStart.Text = "시작";
		this.btnStart.UseVisualStyleBackColor = false;
		this.btnStart.Click += new System.EventHandler(btnStart_Click);
		this.btnStop.BackColor = System.Drawing.Color.FromArgb(220, 53, 69);
		this.btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnStop.Font = new System.Drawing.Font("맑은 고딕", 10f, System.Drawing.FontStyle.Bold);
		this.btnStop.ForeColor = System.Drawing.Color.White;
		this.btnStop.Location = new System.Drawing.Point(920, 382);
		this.btnStop.Name = "btnStop";
		this.btnStop.Size = new System.Drawing.Size(150, 45);
		this.btnStop.TabIndex = 9;
		this.btnStop.Text = "중지";
		this.btnStop.UseVisualStyleBackColor = false;
		this.btnStop.Click += new System.EventHandler(btnStop_Click);
		this.groupKeyRepeat.Controls.Add(this.headerPanel);
		this.groupKeyRepeat.Controls.Add(this.rowsPanel);
		this.groupKeyRepeat.Controls.Add(this.btnAddRow);
		this.groupKeyRepeat.Controls.Add(this.btnStart);
		this.groupKeyRepeat.Controls.Add(this.btnStop);
		this.groupTextRepeat.BackColor = System.Drawing.Color.White;
		this.groupTextRepeat.Font = new System.Drawing.Font("맑은 고딕", 10f, System.Drawing.FontStyle.Bold);
		this.groupTextRepeat.ForeColor = System.Drawing.Color.FromArgb(52, 58, 64);
		this.groupTextRepeat.Location = new System.Drawing.Point(1165, 185);
		this.groupTextRepeat.Name = "groupTextRepeat";
		this.groupTextRepeat.Size = new System.Drawing.Size(245, 510);
		this.groupTextRepeat.TabIndex = 10;
		this.groupTextRepeat.TabStop = false;
		this.groupTextRepeat.Text = "문장 / 여러 키 반복";
		this.labelTextTitle.AutoSize = true;
		this.labelTextTitle.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.labelTextTitle.ForeColor = System.Drawing.Color.FromArgb(73, 80, 87);
		this.labelTextTitle.Location = new System.Drawing.Point(18, 38);
		this.labelTextTitle.Name = "labelTextTitle";
		this.labelTextTitle.Size = new System.Drawing.Size(199, 20);
		this.labelTextTitle.TabIndex = 11;
		this.labelTextTitle.Text = "입력시작 후 아래에 입력";
		this.txtRecordedText.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.txtRecordedText.Location = new System.Drawing.Point(18, 70);
		this.txtRecordedText.Multiline = true;
		this.txtRecordedText.Name = "txtRecordedText";
		this.txtRecordedText.PlaceholderText = "반복할 문장 또는 키";
		this.txtRecordedText.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
		this.txtRecordedText.Size = new System.Drawing.Size(210, 120);
		this.txtRecordedText.TabIndex = 12;
		this.btnRecordStart.BackColor = System.Drawing.Color.FromArgb(25, 135, 84);
		this.btnRecordStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnRecordStart.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnRecordStart.ForeColor = System.Drawing.Color.White;
		this.btnRecordStart.Location = new System.Drawing.Point(18, 210);
		this.btnRecordStart.Name = "btnRecordStart";
		this.btnRecordStart.Size = new System.Drawing.Size(95, 38);
		this.btnRecordStart.TabIndex = 13;
		this.btnRecordStart.Text = "입력시작";
		this.btnRecordStart.UseVisualStyleBackColor = false;
		this.btnRecordStart.Click += new System.EventHandler(btnRecordStart_Click);
		this.btnRecordStop.BackColor = System.Drawing.Color.FromArgb(108, 117, 125);
		this.btnRecordStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnRecordStop.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnRecordStop.ForeColor = System.Drawing.Color.White;
		this.btnRecordStop.Location = new System.Drawing.Point(132, 210);
		this.btnRecordStop.Name = "btnRecordStop";
		this.btnRecordStop.Size = new System.Drawing.Size(95, 38);
		this.btnRecordStop.TabIndex = 14;
		this.btnRecordStop.Text = "입력중지";
		this.btnRecordStop.UseVisualStyleBackColor = false;
		this.btnRecordStop.Click += new System.EventHandler(btnRecordStop_Click);
		this.labelTextInterval.AutoSize = true;
		this.labelTextInterval.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelTextInterval.ForeColor = System.Drawing.Color.FromArgb(13, 110, 253);
		this.labelTextInterval.Location = new System.Drawing.Point(18, 270);
		this.labelTextInterval.Name = "labelTextInterval";
		this.labelTextInterval.Size = new System.Drawing.Size(151, 20);
		this.labelTextInterval.TabIndex = 15;
		this.labelTextInterval.Text = "반복 주기 랜덤(초)";
		this.txtTextInterval.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.txtTextInterval.Location = new System.Drawing.Point(18, 295);
		this.txtTextInterval.Name = "txtTextInterval";
		this.txtTextInterval.PlaceholderText = "최소";
		this.txtTextInterval.Size = new System.Drawing.Size(70, 27);
		this.txtTextInterval.TabIndex = 16;
		this.txtTextInterval.Text = "10";
		this.labelTextIntervalWave.AutoSize = true;
		this.labelTextIntervalWave.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelTextIntervalWave.ForeColor = System.Drawing.Color.FromArgb(73, 80, 87);
		this.labelTextIntervalWave.Location = new System.Drawing.Point(95, 299);
		this.labelTextIntervalWave.Name = "labelTextIntervalWave";
		this.labelTextIntervalWave.Size = new System.Drawing.Size(19, 20);
		this.labelTextIntervalWave.TabIndex = 17;
		this.labelTextIntervalWave.Text = "~";
		this.txtTextIntervalMax.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.txtTextIntervalMax.Location = new System.Drawing.Point(118, 295);
		this.txtTextIntervalMax.Name = "txtTextIntervalMax";
		this.txtTextIntervalMax.PlaceholderText = "최대";
		this.txtTextIntervalMax.Size = new System.Drawing.Size(70, 27);
		this.txtTextIntervalMax.TabIndex = 18;
		this.txtTextIntervalMax.Text = "15";
		this.labelTextIntervalSec.AutoSize = true;
		this.labelTextIntervalSec.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.labelTextIntervalSec.ForeColor = System.Drawing.Color.FromArgb(73, 80, 87);
		this.labelTextIntervalSec.Location = new System.Drawing.Point(195, 299);
		this.labelTextIntervalSec.Name = "labelTextIntervalSec";
		this.labelTextIntervalSec.Size = new System.Drawing.Size(19, 20);
		this.labelTextIntervalSec.TabIndex = 19;
		this.labelTextIntervalSec.Text = "초";
		this.labelTextKeyDelay.AutoSize = true;
		this.labelTextKeyDelay.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelTextKeyDelay.ForeColor = System.Drawing.Color.FromArgb(255, 132, 0);
		this.labelTextKeyDelay.Location = new System.Drawing.Point(18, 330);
		this.labelTextKeyDelay.Name = "labelTextKeyDelay";
		this.labelTextKeyDelay.Size = new System.Drawing.Size(120, 20);
		this.labelTextKeyDelay.TabIndex = 20;
		this.labelTextKeyDelay.Text = "다음 키 딜레이";
		this.txtTextKeyDelayMin.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.txtTextKeyDelayMin.Location = new System.Drawing.Point(18, 355);
		this.txtTextKeyDelayMin.Name = "txtTextKeyDelayMin";
		this.txtTextKeyDelayMin.PlaceholderText = "최소";
		this.txtTextKeyDelayMin.Size = new System.Drawing.Size(70, 27);
		this.txtTextKeyDelayMin.TabIndex = 21;
		this.txtTextKeyDelayMin.Text = "100";
		this.labelTextKeyDelayWave.AutoSize = true;
		this.labelTextKeyDelayWave.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelTextKeyDelayWave.ForeColor = System.Drawing.Color.FromArgb(73, 80, 87);
		this.labelTextKeyDelayWave.Location = new System.Drawing.Point(95, 359);
		this.labelTextKeyDelayWave.Name = "labelTextKeyDelayWave";
		this.labelTextKeyDelayWave.Size = new System.Drawing.Size(19, 20);
		this.labelTextKeyDelayWave.TabIndex = 22;
		this.labelTextKeyDelayWave.Text = "~";
		this.txtTextKeyDelayMax.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.txtTextKeyDelayMax.Location = new System.Drawing.Point(118, 355);
		this.txtTextKeyDelayMax.Name = "txtTextKeyDelayMax";
		this.txtTextKeyDelayMax.PlaceholderText = "최대";
		this.txtTextKeyDelayMax.Size = new System.Drawing.Size(70, 27);
		this.txtTextKeyDelayMax.TabIndex = 23;
		this.txtTextKeyDelayMax.Text = "200";
		this.labelTextKeyDelayMs.AutoSize = true;
		this.labelTextKeyDelayMs.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.labelTextKeyDelayMs.ForeColor = System.Drawing.Color.FromArgb(73, 80, 87);
		this.labelTextKeyDelayMs.Location = new System.Drawing.Point(195, 359);
		this.labelTextKeyDelayMs.Name = "labelTextKeyDelayMs";
		this.labelTextKeyDelayMs.Size = new System.Drawing.Size(27, 20);
		this.labelTextKeyDelayMs.TabIndex = 24;
		this.labelTextKeyDelayMs.Text = "ms";
		this.labelTextHold.AutoSize = true;
		this.labelTextHold.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelTextHold.ForeColor = System.Drawing.Color.FromArgb(220, 53, 69);
		this.labelTextHold.Location = new System.Drawing.Point(18, 390);
		this.labelTextHold.Name = "labelTextHold";
		this.labelTextHold.Size = new System.Drawing.Size(120, 20);
		this.labelTextHold.TabIndex = 25;
		this.labelTextHold.Text = "누름 시간";
		this.txtTextHoldMin.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.txtTextHoldMin.Location = new System.Drawing.Point(18, 415);
		this.txtTextHoldMin.Name = "txtTextHoldMin";
		this.txtTextHoldMin.PlaceholderText = "최소";
		this.txtTextHoldMin.Size = new System.Drawing.Size(70, 27);
		this.txtTextHoldMin.TabIndex = 26;
		this.txtTextHoldMin.Text = "80";
		this.labelTextHoldWave.AutoSize = true;
		this.labelTextHoldWave.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.labelTextHoldWave.ForeColor = System.Drawing.Color.FromArgb(73, 80, 87);
		this.labelTextHoldWave.Location = new System.Drawing.Point(95, 419);
		this.labelTextHoldWave.Name = "labelTextHoldWave";
		this.labelTextHoldWave.Size = new System.Drawing.Size(19, 20);
		this.labelTextHoldWave.TabIndex = 27;
		this.labelTextHoldWave.Text = "~";
		this.txtTextHoldMax.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.txtTextHoldMax.Location = new System.Drawing.Point(118, 415);
		this.txtTextHoldMax.Name = "txtTextHoldMax";
		this.txtTextHoldMax.PlaceholderText = "최대";
		this.txtTextHoldMax.Size = new System.Drawing.Size(70, 27);
		this.txtTextHoldMax.TabIndex = 28;
		this.txtTextHoldMax.Text = "120";
		this.labelTextHoldMs.AutoSize = true;
		this.labelTextHoldMs.Font = new System.Drawing.Font("맑은 고딕", 9f);
		this.labelTextHoldMs.ForeColor = System.Drawing.Color.FromArgb(73, 80, 87);
		this.labelTextHoldMs.Location = new System.Drawing.Point(195, 419);
		this.labelTextHoldMs.Name = "labelTextHoldMs";
		this.labelTextHoldMs.Size = new System.Drawing.Size(27, 20);
		this.labelTextHoldMs.TabIndex = 29;
		this.labelTextHoldMs.Text = "ms";
		this.btnTextStart.BackColor = System.Drawing.Color.FromArgb(13, 110, 253);
		this.btnTextStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnTextStart.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnTextStart.ForeColor = System.Drawing.Color.White;
		this.btnTextStart.Location = new System.Drawing.Point(18, 455);
		this.btnTextStart.Name = "btnTextStart";
		this.btnTextStart.Size = new System.Drawing.Size(95, 38);
		this.btnTextStart.TabIndex = 30;
		this.btnTextStart.Text = "반복 시작";
		this.btnTextStart.UseVisualStyleBackColor = false;
		this.btnTextStart.Click += new System.EventHandler(btnTextStart_Click);
		this.btnTextStop.BackColor = System.Drawing.Color.FromArgb(220, 53, 69);
		this.btnTextStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.btnTextStop.Font = new System.Drawing.Font("맑은 고딕", 9f, System.Drawing.FontStyle.Bold);
		this.btnTextStop.ForeColor = System.Drawing.Color.White;
		this.btnTextStop.Location = new System.Drawing.Point(132, 455);
		this.btnTextStop.Name = "btnTextStop";
		this.btnTextStop.Size = new System.Drawing.Size(95, 38);
		this.btnTextStop.TabIndex = 31;
		this.btnTextStop.Text = "반복 중지";
		this.btnTextStop.UseVisualStyleBackColor = false;
		this.btnTextStop.Click += new System.EventHandler(btnTextStop_Click);
		this.groupTextRepeat.Controls.Add(this.labelTextTitle);
		this.groupTextRepeat.Controls.Add(this.txtRecordedText);
		this.groupTextRepeat.Controls.Add(this.btnRecordStart);
		this.groupTextRepeat.Controls.Add(this.btnRecordStop);
		this.groupTextRepeat.Controls.Add(this.labelTextInterval);
		this.groupTextRepeat.Controls.Add(this.txtTextInterval);
		this.groupTextRepeat.Controls.Add(this.labelTextIntervalWave);
		this.groupTextRepeat.Controls.Add(this.txtTextIntervalMax);
		this.groupTextRepeat.Controls.Add(this.labelTextIntervalSec);
		this.groupTextRepeat.Controls.Add(this.labelTextKeyDelay);
		this.groupTextRepeat.Controls.Add(this.txtTextKeyDelayMin);
		this.groupTextRepeat.Controls.Add(this.labelTextKeyDelayWave);
		this.groupTextRepeat.Controls.Add(this.txtTextKeyDelayMax);
		this.groupTextRepeat.Controls.Add(this.labelTextKeyDelayMs);
		this.groupTextRepeat.Controls.Add(this.labelTextHold);
		this.groupTextRepeat.Controls.Add(this.txtTextHoldMin);
		this.groupTextRepeat.Controls.Add(this.labelTextHoldWave);
		this.groupTextRepeat.Controls.Add(this.txtTextHoldMax);
		this.groupTextRepeat.Controls.Add(this.labelTextHoldMs);
		this.groupTextRepeat.Controls.Add(this.btnTextStart);
		this.groupTextRepeat.Controls.Add(this.btnTextStop);
		this.lblStatus.AutoSize = true;
		this.lblStatus.Font = new System.Drawing.Font("맑은 고딕", 10f, System.Drawing.FontStyle.Bold);
		this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(25, 135, 84);
		this.lblStatus.Location = new System.Drawing.Point(45, 720);
		this.lblStatus.Name = "lblStatus";
		this.lblStatus.Size = new System.Drawing.Size(104, 23);
		this.lblStatus.TabIndex = 27;
		this.lblStatus.Text = "상태: 대기 중";
		this.lblTextStatus.AutoSize = true;
		this.lblTextStatus.Font = new System.Drawing.Font("맑은 고딕", 10f, System.Drawing.FontStyle.Bold);
		this.lblTextStatus.ForeColor = System.Drawing.Color.FromArgb(13, 110, 253);
		this.lblTextStatus.Location = new System.Drawing.Point(45, 755);
		this.lblTextStatus.Name = "lblTextStatus";
		this.lblTextStatus.Size = new System.Drawing.Size(139, 23);
		this.lblTextStatus.TabIndex = 28;
		this.lblTextStatus.Text = "문장 상태: 대기 중";
		this.timer1.Tick += new System.EventHandler(timer1_Tick);
		this.timerText.Tick += new System.EventHandler(timerText_Tick);
		base.Controls.Add(this.labelTitle);
		base.Controls.Add(this.panelGuide);
		base.Controls.Add(this.groupKeyRepeat);
		base.Controls.Add(this.groupTextRepeat);
		base.Controls.Add(this.lblStatus);
		base.Controls.Add(this.lblTextStatus);
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
