using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FolderSyncTool
{
    public partial class MainForm : Form
    {
        private ComboBox _cboChatPeer;
        private ListView _chatLog;
        private TextBox _txtChatInput;
        private Button _btnChatSend;

        private void InitChatTab()
        {
            _tabChat = new TabPage("Chat");

            _chatLog = new ListView();
            _chatLog.View = View.Details;
            _chatLog.FullRowSelect = true;
            _chatLog.Dock = DockStyle.Fill;
            _chatLog.MultiSelect = false;
            _chatLog.Columns.Add("Time", 70);
            _chatLog.Columns.Add("From", 110);
            _chatLog.Columns.Add("Message", 560);

            FlowLayoutPanel inputRow = new FlowLayoutPanel();
            inputRow.Dock = DockStyle.Bottom;
            inputRow.Height = 40;
            inputRow.Padding = new Padding(4);

            _cboChatPeer = new ComboBox();
            _cboChatPeer.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboChatPeer.Width = 170;

            _txtChatInput = new TextBox();
            _txtChatInput.Width = 460;
            _txtChatInput.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendChatMessage(); }
            };

            _btnChatSend = MkButton("Send", 70, delegate { SendChatMessage(); });

            inputRow.Controls.Add(_cboChatPeer);
            inputRow.Controls.Add(_txtChatInput);
            inputRow.Controls.Add(_btnChatSend);

            _tabChat.Controls.Add(_chatLog);      // fill (processed last)
            _tabChat.Controls.Add(inputRow);      // bottom
        }

        public void RefreshChatPeers()
        {
            if (_cboChatPeer == null || _cboChatPeer.IsDisposed || _discovery == null) return;
            string prev = _cboChatPeer.SelectedIndex >= 0
                ? (string)_cboChatPeer.SelectedItem : null;

            _cboChatPeer.Items.Clear();
            _cboChatPeer.Items.Add("All PCs (broadcast)");
            foreach (PeerInfo p in _discovery.Snapshot())
                _cboChatPeer.Items.Add(p.Name + " (" + p.Ip + ")");

            int idx = prev != null ? _cboChatPeer.Items.IndexOf(prev) : -1;
            _cboChatPeer.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void ApplySettingsToChatTab()
        {
            // nothing persistent yet — placeholder for parity
        }

        private void SendChatMessage()
        {
            string text = _txtChatInput.Text.Trim();
            if (text.Length == 0) return;
            if (_discovery == null) return;

            byte[] key = (Program.Settings.EncryptTransfers) ? AesKey() : null;
            if (Program.Settings.EncryptTransfers && key == null)
            {
                MessageBox.Show("Encryption is on but no valid AES key is set. Open Settings… to generate one.",
                    "Chat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PeerInfo[] targets;
            bool broadcast = _cboChatPeer.SelectedIndex <= 0;
            if (broadcast)
                targets = _discovery.Snapshot();
            else
            {
                PeerInfo sel = SelectedChatPeer();
                targets = sel != null ? new[] { sel } : new PeerInfo[0];
            }

            if (targets.Length == 0)
            {
                MessageBox.Show("No PCs discovered. Make sure FolderSyncTool runs on the other machine.",
                    "Chat", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int sent = 0;
            foreach (PeerInfo p in targets)
            {
                if (LanTransfer.SendSmall(p.Ip, "CHAT", text, key, _discovery.MachineName))
                    sent++;
            }

            AppendChat(_discovery.MachineName + (broadcast ? " → all" : " → " + targets[0].Name), text);
            _txtChatInput.Clear();
            if (sent == 0)
                MessageBox.Show("Message could not be delivered.", "Chat",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private PeerInfo SelectedChatPeer()
        {
            if (_cboChatPeer.SelectedIndex <= 0) return null;
            string entry = (string)_cboChatPeer.SelectedItem;
            int l = entry.LastIndexOf('(');
            int r = entry.LastIndexOf(')');
            if (l < 0 || r <= l) return null;
            string ip = entry.Substring(l + 1, r - l - 1);
            foreach (PeerInfo p in _discovery.Snapshot())
                if (p.Ip == ip) return p;
            return null;
        }

        private void OnChatReceived(string from, string text)
        {
            if (IsHandleCreated)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        AppendChat(from, text);
                        if (_tabs.SelectedTab != _tabChat || WindowState == FormWindowState.Minimized)
                            ShowBalloon("Chat from " + from, text);
                    });
                }
                catch { }
            }
        }

        private void AppendChat(string from, string text)
        {
            if (_chatLog == null || _chatLog.IsDisposed) return;
            var item = new ListViewItem(DateTime.Now.ToString("HH:mm:ss"));
            item.SubItems.Add(from);
            item.SubItems.Add(text);
            _chatLog.Items.Add(item);
            _chatLog.EnsureVisible(_chatLog.Items.Count - 1);
        }
    }
}
