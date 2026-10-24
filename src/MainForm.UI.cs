using System;
using System.Drawing;
using System.Windows.Forms;

namespace FolderSyncTool
{
    public partial class MainForm : Form
    {
        // tabs
        protected TabControl _tabs;
        protected TabPage _tabSync;
        protected TabPage _tabTransfer;
        protected TabPage _tabChat;
        protected TabPage _tabTasks;

        // tray
        protected NotifyIcon _tray;
        protected bool _forceClose;

        // shared log
        protected TextBox _log;

        // timers
        private System.Windows.Forms.Timer _scheduleTimer;
        private System.Windows.Forms.Timer _reminderTimer;
        private DateTime? _lastRunUtc;

        private readonly bool _startMinimized;

        public MainForm(bool startMinimized)
        {
            _startMinimized = startMinimized;
            Text = "FolderSync Tool v5.0";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(980, 660);
            MinimumSize = new Size(860, 560);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            InitSyncTab();
            InitTransferTab();
            InitChatTab();
            InitTasksTab();
            InitShell();
            InitTray();
            InitLan();          // after tray: LAN events may show balloons
            LoadAllSettings();
            InitTimers();

            Theme.Apply(this, Program.Settings.DarkMode);

            if (_startMinimized) WindowState = FormWindowState.Minimized;
        }

        private void InitShell()
        {
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;

            _tabs.TabPages.Add(_tabSync);
            _tabs.TabPages.Add(_tabTransfer);
            _tabs.TabPages.Add(_tabChat);
            _tabs.TabPages.Add(_tabTasks);

            Controls.Add(_tabs);
        }

        private void InitTimers()
        {
            _scheduleTimer = new System.Windows.Forms.Timer();
            _scheduleTimer.Interval = 30000;
            _scheduleTimer.Tick += ScheduleTick;
            _scheduleTimer.Start();

            _reminderTimer = new System.Windows.Forms.Timer();
            _reminderTimer.Interval = 30000;
            _reminderTimer.Tick += ReminderTick;
            _reminderTimer.Start();

            this.FormClosing += OnFormClosing;
            this.Resize += OnResize;
            Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
        }

        private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e)
        {
            // ScheduleType.OnLogoff trigger
            if (Program.Settings.Schedule == ScheduleType.OnLogoff)
                RunBackupNowSafe();
        }

        private void ScheduleTick(object sender, EventArgs e)
        {
            try
            {
                AppSettings s = Program.Settings;
                DateTime nowLocal = DateTime.Now;
                if (ScheduleLogic.ShouldRunNow(s, _lastRunUtc, nowLocal))
                {
                    _lastRunUtc = nowLocal.ToUniversalTime();
                    RunBackupNowSafe();
                }
                UpdateNextRunLabel();
            }
            catch { }
        }

        private void ReminderTick(object sender, EventArgs e)
        {
            try { CheckDeadlines(); }
            catch { }
        }

        public void Log(string msg)
        {
            if (_log == null || _log.IsDisposed) return;
            if (IsHandleCreated)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
                    });
                }
                catch { }
            }
        }

        public void ShowBalloon(string title, string text)
        {
            try
            {
                if (_tray != null && _tray.Visible)
                    _tray.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);
            }
            catch { }
        }

        private void LoadAllSettings()
        {
            ApplySettingsToSyncTab();
            ApplySettingsToTransferTab();
            ApplySettingsToChatTab();
            RefreshPairsList();
            RefreshPeersList();
            RefreshTasksList();
            RefreshChatPeers();
            RestoreManualPeers();
            UpdateNextRunLabel();
        }

        // ---- tray --------------------------------------------------------

        private void InitTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = Icon;
            _tray.Text = "FolderSync Tool";
            _tray.Visible = true;
            _tray.DoubleClick += delegate
            {
                Show();
                WindowState = FormWindowState.Normal;
            };

            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add("Open", delegate
            {
                Show();
                WindowState = FormWindowState.Normal;
            });
            menu.MenuItems.Add("Backup now", delegate { RunBackupNowSafe(); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Exit", delegate { _forceClose = true; Close(); });
            _tray.ContextMenu = menu;
        }

        private void OnResize(object sender, EventArgs e)
        {
            if (Program.Settings.MinimizeToTray && WindowState == FormWindowState.Minimized)
                Hide();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_forceClose && Program.Settings.MinimizeToTray &&
                e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            SaveAllSettings();
            _scheduleTimer.Stop();
            _reminderTimer.Stop();
            if (_tray != null) _tray.Visible = false;
            ShutdownLan();
            CancelBackupSafe();
        }
    }
}
