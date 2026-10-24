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
        private ListView _peersList;
        private Button _btnRescan, _btnAddIp, _btnRemovePeer, _btnSendFiles, _btnCancelSend;
        private ProgressBar _sendProgress;
        private Label _sendStatusLabel;
        private TextBox _receiveHint;

        private LanDiscovery _discovery;
        private LanTransfer _transfer;
        private System.Windows.Forms.Timer _peerTimer;
        private CancellationTokenSource _sendCts;
        private Thread _sendThread;

        private void InitTransferTab()
        {
            _tabTransfer = new TabPage("LAN Transfer");

            _peersList = new ListView();
            _peersList.View = View.Details;
            _peersList.FullRowSelect = true;
            _peersList.GridLines = true;
            _peersList.Dock = DockStyle.Fill;
            _peersList.Columns.Add("PC name", 220);
            _peersList.Columns.Add("IP", 140);
            _peersList.Columns.Add("Source", 90);
            _peersList.AllowDrop = true;
            _peersList.DragEnter += PeersDragEnter;
            _peersList.DragDrop += PeersDragDrop;

            _sendProgress = new ProgressBar();
            _sendProgress.Dock = DockStyle.Bottom;
            _sendProgress.Height = 18;

            _sendStatusLabel = new Label();
            _sendStatusLabel.Dock = DockStyle.Bottom;
            _sendStatusLabel.Height = 40;
            _sendStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _sendStatusLabel.Text = "Files you drop here are sent to the selected PC.";
            _sendStatusLabel.ForeColor = SystemColors.GrayText;

            _receiveHint = new TextBox();
            _receiveHint.ReadOnly = true;
            _receiveHint.Dock = DockStyle.Bottom;
            _receiveHint.Height = 26;
            _receiveHint.TextAlign = HorizontalAlignment.Center;

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = 40;
            toolbar.Padding = new Padding(4);

            _btnRescan = MkButton("Rescan", 80, delegate { RescanPeers(); });
            _btnAddIp = MkButton("Add PC by IP…", 120, delegate { AddPeerByIp(); });
            _btnRemovePeer = MkButton("Remove", 70, delegate { RemoveSelectedPeer(); });
            _btnSendFiles = MkButton("Send files…", 100, delegate { SendFilesDialog(); });
            _btnCancelSend = MkButton("Cancel send", 100, delegate { CancelSend(); });
            _btnCancelSend.Enabled = false;

            toolbar.Controls.Add(_btnRescan);
            toolbar.Controls.Add(_btnAddIp);
            toolbar.Controls.Add(_btnRemovePeer);
            toolbar.Controls.Add(_btnSendFiles);
            toolbar.Controls.Add(_btnCancelSend);

            _tabTransfer.Controls.Add(_peersList);   // fill (processed last)
            _tabTransfer.Controls.Add(_receiveHint); // bottom inner
            _tabTransfer.Controls.Add(_sendStatusLabel); // bottom
            _tabTransfer.Controls.Add(_sendProgress);    // bottom
            _tabTransfer.Controls.Add(toolbar);          // top outer
        }

        // ---- LAN lifecycle -------------------------------------------------

        private void InitLan()
        {
            _discovery = new LanDiscovery();
            _discovery.PeerAdded += delegate { SafeRefreshPeers(); };
            _discovery.PeerRemoved += delegate { SafeRefreshPeers(); };
            _discovery.Start();
            _discovery.Announce();

            _transfer = new LanTransfer();
            _transfer.KeyProvider = AesKey;
            _transfer.EncryptEnabled = delegate { return Program.Settings.EncryptTransfers; };
            _transfer.KeepReceivedVersions = delegate { return Program.Settings.KeepReceivedVersions; };
            _transfer.VersionCount = delegate { return Program.Settings.VersionCount; };
            _transfer.ChatReceived += OnChatReceived;
            _transfer.TaskReceived += OnTaskReceived;
            _transfer.TaskDoneReceived += OnTaskDoneReceived;
            _transfer.FileReceived += delegate (string from, string path)
            {
                Log("Received from " + from + ": " + path);
                ShowBalloon("File received", from + " sent " + Path.GetFileName(path));
                SetSendStatus("Received: " + path, Color.Empty);
            };
            _transfer.Start();

            _peerTimer = new System.Windows.Forms.Timer();
            _peerTimer.Interval = 20000;
            _peerTimer.Tick += delegate
            {
                _discovery.Announce();
                _discovery.ExpireStale();
            };
            _peerTimer.Start();
        }

        private void ShutdownLan()
        {
            try { if (_peerTimer != null) _peerTimer.Stop(); } catch { }
            try { if (_discovery != null) _discovery.Dispose(); } catch { }
            try { if (_transfer != null) _transfer.Dispose(); } catch { }
        }

        private byte[] AesKey()
        {
            try
            {
                if (string.IsNullOrEmpty(Program.Settings.AesKeyB64)) return null;
                byte[] k = Convert.FromBase64String(Program.Settings.AesKeyB64);
                return k.Length == 32 ? k : null;
            }
            catch { return null; }
        }

        private void SafeRefreshPeers()
        {
            if (IsHandleCreated)
            {
                try { BeginInvoke((MethodInvoker)delegate { RefreshPeersList(); }); }
                catch { }
            }
        }

        public void RefreshPeersList()
        {
            if (_peersList == null || _peersList.IsDisposed || _discovery == null) return;
            string selectedIp = _peersList.SelectedItems.Count > 0
                ? _peersList.SelectedItems[0].SubItems[1].Text : null;

            _peersList.Items.Clear();
            foreach (PeerInfo p in _discovery.Snapshot())
            {
                var item = new ListViewItem(p.Name);
                item.SubItems.Add(p.Ip);
                item.SubItems.Add(p.Manual ? "manual" : "discovered");
                item.Tag = p;
                _peersList.Items.Add(item);
                if (selectedIp != null && p.Ip == selectedIp) item.Selected = true;
            }
            RefreshChatPeers();
        }

        private void ApplySettingsToTransferTab()
        {
            if (_receiveHint != null)
                _receiveHint.Text = "Received files go to: " +
                    (LanTransfer.ReceiveRootOverride ??
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                  "FolderSyncTool", "Received"));
        }

        // ---- peers ---------------------------------------------------------

        private void RescanPeers()
        {
            _discovery.Announce();
            SetSendStatus("Broadcasting discovery…", Color.Empty);
        }

        private void AddPeerByIp()
        {
            string ip = Prompt.Show(this, "Add PC by IP address", "IP address (e.g. 192.168.1.57):");
            if (string.IsNullOrWhiteSpace(ip)) return;
            ip = ip.Trim();
            if (!LanDiscovery.IsPlausibleIp(ip))
            {
                MessageBox.Show("That does not look like an IPv4 address.", "Add PC by IP",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetSendStatus("Probing " + ip + "…", Color.Empty);
            ThreadPool.QueueUserWorkItem(delegate
            {
                PeerInfo peer = LanDiscovery.ProbeManualPeer(ip, 4000);
                if (peer == null)
                {
                    SafeStatus("No FolderSyncTool answering at " + ip + ".", Color.Firebrick);
                    return;
                }
                _discovery.AddManualPeer(peer);
                SafeStatus("Added " + peer.Name + " (" + ip + ").", Color.Empty);
                SafeRefreshPeers();
            });
        }

        private void RemoveSelectedPeer()
        {
            if (_peersList.SelectedItems.Count == 0) return;
            PeerInfo p = (PeerInfo)_peersList.SelectedItems[0].Tag;
            _discovery.RemovePeer(p.Ip);
            RefreshPeersList();
        }

        private void RestoreManualPeers()
        {
            foreach (string ip in LanDiscovery.ParseManualPeers(Program.Settings.ManualPeers))
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    PeerInfo peer = LanDiscovery.ProbeManualPeer(ip, 2500);
                    if (peer != null)
                    {
                        _discovery.AddManualPeer(peer);
                        SafeRefreshPeers();
                    }
                });
            }
        }

        private void SafeStatus(string text, Color color)
        {
            if (IsHandleCreated)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate { SetSendStatus(text, color); });
                }
                catch { }
            }
        }

        private void SetSendStatus(string text, Color color)
        {
            if (_sendStatusLabel == null || _sendStatusLabel.IsDisposed) return;
            _sendStatusLabel.ForeColor = color.IsEmpty ? SystemColors.GrayText : color;
            _sendStatusLabel.Text = text;
        }

        // ---- sending ---------------------------------------------------------

        private PeerInfo SelectedPeer()
        {
            if (_peersList.SelectedItems.Count == 0) return null;
            return (PeerInfo)_peersList.SelectedItems[0].Tag;
        }

        private void PeersDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void PeersDragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            PeerInfo peer = SelectedPeer();
            if (peer == null)
            {
                MessageBox.Show("Select a PC first, then drop files.", "Send",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StartSend(files, new PeerInfo[] { peer });
        }

        private void SendFilesDialog()
        {
            PeerInfo peer = SelectedPeer();
            if (peer == null)
            {
                MessageBox.Show("Select a PC first.", "Send",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Send to " + peer.Name;
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    StartSend(dlg.FileNames, new PeerInfo[] { peer });
            }
        }

        private void StartSend(string[] files, PeerInfo[] peers)
        {
            if (_sendThread != null && _sendThread.IsAlive)
            {
                MessageBox.Show("A send is already in progress.", "Send",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _sendCts = new CancellationTokenSource();
            CancellationToken ct = _sendCts.Token;
            _btnSendFiles.Enabled = false;
            _btnCancelSend.Enabled = true;
            SetSendStatus("Sending " + files.Length + " file(s)…", Color.Empty);

            _sendThread = new Thread(delegate ()
            {
                int ok = 0;
                foreach (PeerInfo peer in peers)
                {
                    if (ct.IsCancellationRequested) break;
                    bool r = _transfer.SendFiles(files, peer, delegate (TransferProgress pr)
                    {
                        SafeSendProgress(pr);
                    }, ct);
                    if (r) ok++;
                }
                bool cancelled = ct.IsCancellationRequested;
                SafeSendDone(ok, peers.Length, cancelled);
            });
            _sendThread.IsBackground = true;
            _sendThread.Start();
        }

        private void SafeSendProgress(TransferProgress pr)
        {
            if (!IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (pr.Phase == "hashing")
                    {
                        _sendProgress.Value = 0;
                        SetSendStatus("Hashing " + pr.FileName + " (integrity check)…", Color.Empty);
                        return;
                    }
                    _sendProgress.Value = Math.Max(0, Math.Min(100, pr.Percent));
                    string speed = LanTransfer.FormatSpeed(pr.BytesPerSec);
                    string eta = pr.Eta > TimeSpan.Zero && pr.Eta < TimeSpan.FromHours(10)
                        ? " — ETA " + pr.Eta.ToString(@"mm\:ss")
                        : "";
                    SetSendStatus("[" + (pr.FileIndex + 1) + "/" + pr.FileCount + "] " +
                        pr.FileName + " — " + pr.Percent + "% — " + speed + eta, Color.Empty);
                });
            }
            catch { }
        }

        private void SafeSendDone(int ok, int total, bool cancelled)
        {
            if (!IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    _btnSendFiles.Enabled = true;
                    _btnCancelSend.Enabled = false;
                    _sendProgress.Value = 0;
                    if (cancelled)
                        SetSendStatus("Send cancelled.", Color.Firebrick);
                    else
                    {
                        SetSendStatus("Sent to " + ok + "/" + total + " PC(s).", Color.Empty);
                        if (ok > 0) ShowBalloon("Send complete", "Files delivered to " + ok + " PC(s).");
                    }
                });
            }
            catch { }
        }

        private void CancelSend()
        {
            if (_sendCts != null)
            {
                try { _sendCts.Cancel(); } catch { }
                SetSendStatus("Cancelling send…", Color.Firebrick);
            }
        }

    }

    /// <summary>Tiny one-line input dialog used for "Add PC by IP".</summary>
    public static class Prompt
    {
        public static string Show(IWin32Window owner, string title, string label)
        {
            Form f = new Form();
            f.Text = title;
            f.Width = 380;
            f.Height = 140;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.MaximizeBox = false;
            f.MinimizeBox = false;

            var lbl = new Label { Text = label, AutoSize = true, Location = new Point(10, 12) };
            var txt = new TextBox { Location = new Point(12, 34), Width = 340 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(190, 66), Width = 75 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(275, 66), Width = 75 };
            f.Controls.Add(lbl);
            f.Controls.Add(txt);
            f.Controls.Add(ok);
            f.Controls.Add(cancel);
            f.AcceptButton = ok;
            f.CancelButton = cancel;

            string result = null;
            if (f.ShowDialog(owner) == DialogResult.OK) result = txt.Text;
            f.Dispose();
            return result;
        }
    }
}
