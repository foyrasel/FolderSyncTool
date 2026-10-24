using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FolderSyncTool
{
    public partial class MainForm : Form
    {
        // toolbar
        private Button _btnAddPair, _btnEditPair, _btnRemovePair, _btnBackupNow, _btnCancelBackup, _btnSettings;
        private Label _nextRunLabel;

        // pairs list
        private ListView _pairsList;

        // settings group
        private ComboBox _cboSchedule;
        private TextBox _txtDailyTime;
        private NumericUpDown _numInterval;
        private CheckBox _chkCatchUp;
        private TextBox _txtGlobalTypes;
        private CheckBox _chkMirror;
        private CheckBox _chkLogOnlyCopied;
        private CheckBox _chkStartWithWindows;

        // backup run state
        private CancellationTokenSource _backupCts;
        private Thread _backupThread;
        private ProgressBar _syncProgress;
        private Label _syncStatusLabel;

        private void InitSyncTab()
        {
            _tabSync = new TabPage("Sync");

            _pairsList = new ListView();
            _pairsList.View = View.Details;
            _pairsList.FullRowSelect = true;
            _pairsList.GridLines = true;
            _pairsList.Dock = DockStyle.Fill;
            _pairsList.Columns.Add("Source", 300);
            _pairsList.Columns.Add("Destination", 300);
            _pairsList.Columns.Add("Types", 150);
            _pairsList.Columns.Add("Versions", 70);
            _pairsList.Columns.Add("VSS", 50);
            _pairsList.Columns.Add("On", 40);
            _pairsList.ItemActivate += delegate { EditPair(); };

            _log = new TextBox();
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.Dock = DockStyle.Fill;
            _log.Font = new Font("Consolas", 9f);

            _syncProgress = new ProgressBar();
            _syncProgress.Dock = DockStyle.Bottom;
            _syncProgress.Height = 18;

            _syncStatusLabel = new Label();
            _syncStatusLabel.Dock = DockStyle.Bottom;
            _syncStatusLabel.Height = 22;
            _syncStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _syncStatusLabel.Text = "Ready.";

            // log panel: list on top, log below (50/50)
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.Panel1.Controls.Add(_pairsList);
            split.Panel2.Controls.Add(_log);
            split.SplitterDistance = 220;

            // settings group
            GroupBox grp = new GroupBox();
            grp.Text = "Schedule and filters";
            grp.Dock = DockStyle.Top;
            grp.Height = 108;
            BuildScheduleControls(grp);

            // toolbar
            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = 40;
            toolbar.FlowDirection = FlowDirection.LeftToRight;
            toolbar.Padding = new Padding(4);

            _btnAddPair = MkButton("Add pair…", 90, delegate { AddPair(); });
            _btnEditPair = MkButton("Edit", 60, delegate { EditPair(); });
            _btnRemovePair = MkButton("Remove", 70, delegate { RemovePair(); });
            _btnBackupNow = MkButton("Backup now", 100, delegate { RunBackupNowSafe(); });
            _btnCancelBackup = MkButton("Cancel backup", 110, delegate { CancelBackupSafe(); });
            _btnCancelBackup.Enabled = false;
            _btnSettings = MkButton("Settings…", 90, delegate { ShowSettings(); });

            toolbar.Controls.Add(_btnAddPair);
            toolbar.Controls.Add(_btnEditPair);
            toolbar.Controls.Add(_btnRemovePair);
            toolbar.Controls.Add(_btnBackupNow);
            toolbar.Controls.Add(_btnCancelBackup);
            toolbar.Controls.Add(_btnSettings);

            _nextRunLabel = new Label();
            _nextRunLabel.Dock = DockStyle.Bottom;
            _nextRunLabel.Height = 22;
            _nextRunLabel.TextAlign = ContentAlignment.MiddleLeft;
            _nextRunLabel.Text = "Next run: —";

            // Docking is applied last-added-first: fill first, then edges inner→outer.
            _tabSync.Controls.Add(split);            // fill (processed last)
            _tabSync.Controls.Add(_syncProgress);    // bottom, inner
            _tabSync.Controls.Add(_syncStatusLabel); // bottom
            _tabSync.Controls.Add(_nextRunLabel);    // bottom, outer (very bottom)
            _tabSync.Controls.Add(grp);              // top, inner (below toolbar)
            _tabSync.Controls.Add(toolbar);          // top, outer
        }

        private static Button MkButton(string text, int width, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 28;
            b.Click += onClick;
            return b;
        }

        private void BuildScheduleControls(GroupBox grp)
        {
            var lblSchedule = new Label { Text = "Run:", AutoSize = true, Location = new Point(10, 26) };
            _cboSchedule = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(45, 22),
                Width = 100
            };
            _cboSchedule.Items.AddRange(new object[] { "Daily", "Every N min", "On logoff" });
            _cboSchedule.SelectedIndexChanged += delegate { UpdateScheduleEditorState(); };

            var lblDaily = new Label { Text = "at", AutoSize = true, Location = new Point(155, 26) };
            _txtDailyTime = new TextBox { Location = new Point(175, 22), Width = 55, Text = "14:00" };

            var lblEvery = new Label { Text = "every", AutoSize = true, Location = new Point(155, 26) };
            _numInterval = new NumericUpDown { Location = new Point(195, 22), Width = 60, Minimum = 1, Maximum = 1440, Value = 60 };
            var lblMinutes = new Label { Text = "min", AutoSize = true, Location = new Point(260, 26) };

            _chkCatchUp = new CheckBox { Text = "Run missed daily slot on startup", AutoSize = true, Location = new Point(310, 24) };
            _chkStartWithWindows = new CheckBox { Text = "Start with Windows", AutoSize = true, Location = new Point(10, 76) };
            _chkStartWithWindows.CheckedChanged += delegate
            {
                SetStartWithWindows(_chkStartWithWindows.Checked);
            };

            var lblTypes = new Label { Text = "File types:", AutoSize = true, Location = new Point(10, 52) };
            _txtGlobalTypes = new TextBox { Location = new Point(80, 48), Width = 300 };
            var lblMirror = new Label { Text = "(empty = all files)", AutoSize = true, Location = new Point(385, 52), ForeColor = SystemColors.GrayText };
            _chkMirror = new CheckBox { Text = "Mirror deletes", AutoSize = true, Location = new Point(310, 48) };
            _chkLogOnlyCopied = new CheckBox { Text = "Quiet log", AutoSize = true, Location = new Point(430, 48) };

            grp.Controls.Add(lblSchedule);
            grp.Controls.Add(_cboSchedule);
            grp.Controls.Add(lblDaily);
            grp.Controls.Add(_txtDailyTime);
            grp.Controls.Add(lblEvery);
            grp.Controls.Add(_numInterval);
            grp.Controls.Add(lblMinutes);
            grp.Controls.Add(_chkCatchUp);
            grp.Controls.Add(lblTypes);
            grp.Controls.Add(_txtGlobalTypes);
            grp.Controls.Add(lblMirror);
            grp.Controls.Add(_chkMirror);
            grp.Controls.Add(_chkLogOnlyCopied);
            grp.Controls.Add(_chkStartWithWindows);

            // keep both daily-time and interval labels; visibility toggled below
            lblDaily.Tag = "daily";
            _txtDailyTime.Tag = "daily";
            lblEvery.Tag = "interval";
            _numInterval.Tag = "interval";
            lblMinutes.Tag = "interval";
            _scheduleDailyControls = new Control[] { lblDaily, _txtDailyTime };
            _scheduleIntervalControls = new Control[] { lblEvery, _numInterval, lblMinutes };
        }

        private Control[] _scheduleDailyControls;
        private Control[] _scheduleIntervalControls;

        private void UpdateScheduleEditorState()
        {
            bool daily = _cboSchedule.SelectedIndex == 0;
            bool interval = _cboSchedule.SelectedIndex == 1;
            foreach (Control c in _scheduleDailyControls) c.Visible = daily;
            foreach (Control c in _scheduleIntervalControls) c.Visible = interval;
        }

        // ---- settings <-> UI ---------------------------------------------

        private void ApplySettingsToSyncTab()
        {
            AppSettings s = Program.Settings;
            _cboSchedule.SelectedIndex = (int)s.Schedule;
            _txtDailyTime.Text = s.DailyTime;
            _numInterval.Value = Math.Max(1, Math.Min(1440, s.IntervalMinutes));
            _chkCatchUp.Checked = s.RunMissedOnStartup;
            _txtGlobalTypes.Text = s.GlobalTypes;
            _chkMirror.Checked = s.MirrorDeletes;
            _chkLogOnlyCopied.Checked = s.LogOnlyCopied;
            _chkStartWithWindows.Checked = s.StartWithWindows;
            UpdateScheduleEditorState();
        }

        private void SaveSyncTabSettings()
        {
            AppSettings s = Program.Settings;
            if (_cboSchedule.SelectedIndex >= 0) s.Schedule = (ScheduleType)_cboSchedule.SelectedIndex;
            s.DailyTime = _txtDailyTime.Text.Trim();
            s.IntervalMinutes = (int)_numInterval.Value;
            s.RunMissedOnStartup = _chkCatchUp.Checked;
            s.GlobalTypes = _txtGlobalTypes.Text.Trim();
            s.MirrorDeletes = _chkMirror.Checked;
            s.LogOnlyCopied = _chkLogOnlyCopied.Checked;
            s.StartWithWindows = _chkStartWithWindows.Checked;
        }

        private void SaveAllSettings()
        {
            SaveSyncTabSettings();
            Program.Settings.Save();
        }

        private void UpdateNextRunLabel()
        {
            if (_nextRunLabel == null || _nextRunLabel.IsDisposed) return;
            if (IsHandleCreated)
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    DateTime? next = ScheduleLogic.NextRunLocal(Program.Settings, _lastRunUtc, DateTime.Now);
                    _nextRunLabel.Text = "Next run: " +
                        (next != null ? next.Value.ToString("ddd HH:mm") : "—");
                });
            }
        }

        // ---- pairs ---------------------------------------------------------

        private void RefreshPairsList()
        {
            _pairsList.Items.Clear();
            foreach (PairInfo p in Program.Settings.Pairs)
            {
                var item = new ListViewItem(p.Source);
                item.SubItems.Add(p.Dest);
                item.SubItems.Add(string.IsNullOrEmpty(p.Types) ? "(global)" : p.Types);
                item.SubItems.Add(p.KeepVersions ? "on" : "");
                item.SubItems.Add(p.UseShadowCopy ? "yes" : "");
                item.SubItems.Add(p.Enabled ? "1" : "0");
                item.Checked = p.Enabled;
                item.Tag = p;
                _pairsList.Items.Add(item);
            }
        }

        private void AddPair()
        {
            using (PairEditorForm f = new PairEditorForm(null))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.Pair != null)
                {
                    Program.Settings.Pairs.Add(f.Pair);
                    RefreshPairsList();
                }
            }
        }

        private void EditPair()
        {
            if (_pairsList.SelectedItems.Count == 0) return;
            PairInfo p = (PairInfo)_pairsList.SelectedItems[0].Tag;
            using (PairEditorForm f = new PairEditorForm(p))
            {
                if (f.ShowDialog(this) == DialogResult.OK) RefreshPairsList();
            }
        }

        private void RemovePair()
        {
            if (_pairsList.SelectedItems.Count == 0) return;
            PairInfo p = (PairInfo)_pairsList.SelectedItems[0].Tag;
            if (MessageBox.Show("Remove this pair?\n" + p.Source, "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Program.Settings.Pairs.Remove(p);
                RefreshPairsList();
            }
        }

        // ---- backup run ------------------------------------------------------

        private void RunBackupNowSafe()
        {
            if (_backupThread != null && _backupThread.IsAlive) return;
            SaveSyncTabSettings();

            List<PairInfo> pairs = Program.Settings.Pairs.FindAll(p => p.Enabled);
            if (pairs.Count == 0)
            {
                SetSyncStatus("No enabled pairs to back up.");
                return;
            }

            _backupCts = new CancellationTokenSource();
            CancellationToken ct = _backupCts.Token;
            AppSettings snapshot = Program.Settings; // live object; fields read directly

            SetSyncStatus("Backing up…");
            _syncProgress.Style = ProgressBarStyle.Marquee;
            _btnBackupNow.Enabled = false;
            _btnCancelBackup.Enabled = true;

            _backupThread = new Thread(delegate ()
            {
                int totalCopied = 0, totalFailed = 0;
                foreach (PairInfo p in pairs)
                {
                    if (ct.IsCancellationRequested) break;
                    Log("— Pair: " + p.Source + " → " + p.Dest);
                    Func<string, string> shadowMap = null;
                    if (p.UseShadowCopy)
                        shadowMap = delegate (string src)
                        {
                            string tmp = VssHelper.CopyViaShadow(src);
                            if (tmp == null && !string.IsNullOrEmpty(VssHelper.LastError))
                                Log("VSS: " + VssHelper.LastError);
                            return tmp;
                        };

                    PairRunResult r = BackupEngine.RunPair(snapshot, p,
                        m => Log(m), ct, shadowMap);
                    totalCopied += r.Copied;
                    totalFailed += r.Failed;
                    if (r.Versioned > 0) Log("Versions saved: " + r.Versioned);
                    if (r.Deleted > 0) Log("Mirror-deleted: " + r.Deleted);
                }

                bool cancelled = ct.IsCancellationRequested;
                SafeEndBackup(cancelled, totalCopied, totalFailed);
            });
            _backupThread.IsBackground = true;
            _backupThread.Start();
        }

        private void SafeEndBackup(bool cancelled, int copied, int failed)
        {
            if (IsHandleCreated)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _syncProgress.Style = ProgressBarStyle.Blocks;
                        _syncProgress.Value = 0;
                        _btnBackupNow.Enabled = true;
                        _btnCancelBackup.Enabled = false;
                        _syncStatusLabel.Text = cancelled
                            ? "Backup cancelled."
                            : "Backup finished — copied " + copied + ", failed " + failed + ".";
                        Log(_syncStatusLabel.Text);
                        if (!cancelled && failed == 0 && copied > 0)
                            ShowBalloon("FolderSync Tool", "Backup done: " + copied + " file(s) copied.");
                    });
                }
                catch { }
            }
        }

        private void CancelBackupSafe()
        {
            if (_backupCts != null)
            {
                try { _backupCts.Cancel(); } catch { }
                SetSyncStatus("Cancelling backup…");
            }
        }

        private void SetSyncStatus(string text)
        {
            if (_syncStatusLabel == null || _syncStatusLabel.IsDisposed) return;
            if (IsHandleCreated)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate { _syncStatusLabel.Text = text; });
                }
                catch { }
            }
        }

        private void SetStartWithWindows(bool enable)
        {
            try
            {
                const string keyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
                using (Microsoft.Win32.RegistryKey k =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyName, true))
                {
                    if (k == null) return;
                    if (enable) k.SetValue("FolderSyncTool", "\"" + Application.ExecutablePath + "\" /minimized");
                    else if (k.GetValueNames() != null && Array.IndexOf(k.GetValueNames(), "FolderSyncTool") >= 0)
                        k.DeleteValue("FolderSyncTool", false);
                }
            }
            catch { }
        }

        private void ShowSettings()
        {
            using (SettingsForm f = new SettingsForm())
            {
                f.ShowDialog(this);
                RefreshPairsList();
            }
        }
    }
}
