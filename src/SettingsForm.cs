using System;
using System.Drawing;
using System.Windows.Forms;

namespace FolderSyncTool
{
    /// <summary>
    /// App-level settings (v5): dark mode, LAN encryption + AES key,
    /// version count, versioning of received files.
    /// </summary>
    public class SettingsForm : Form
    {
        private CheckBox _chkDark;
        private CheckBox _chkEncrypt;
        private TextBox _txtKey;
        private NumericUpDown _numVersionCount;
        private CheckBox _chkRecvVersions;

        public SettingsForm()
        {
            Text = "Settings";
            Width = 500;
            Height = 300;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            int y = 15;
            _chkDark = new CheckBox
            {
                Text = "Dark mode (applies immediately)",
                AutoSize = true,
                Location = new Point(15, y)
            };
            y += 28;

            _chkEncrypt = new CheckBox
            {
                Text = "Encrypt LAN transfers (AES-256). Both PCs must share the key.",
                AutoSize = true,
                Location = new Point(15, y)
            };
            y += 28;

            var lblKey = new Label { Text = "AES key:", AutoSize = true, Location = new Point(15, y + 3) };
            _txtKey = new TextBox { Location = new Point(90, y), Width = 290, ReadOnly = true };
            var btnGen = new Button { Text = "Generate", Location = new Point(388, y - 1), Width = 80 };
            btnGen.Click += delegate
            {
                Program.Settings.AesKeyB64 = GenerateKey();
                _txtKey.Text = Program.Settings.AesKeyB64;
            };
            var btnCopy = new Button { Text = "Copy", Location = new Point(90, y + 28), Width = 80 };
            btnCopy.Click += delegate
            {
                if (_txtKey.Text.Length > 0)
                    Clipboard.SetText(_txtKey.Text);
            };
            var lblKeyHint = new Label
            {
                Text = "Paste the same key on the other PC (Settings → Generate/Copy).",
                AutoSize = true,
                Location = new Point(180, y + 31),
                ForeColor = SystemColors.GrayText
            };
            y += 62;

            var lblVer = new Label { Text = "Versions kept:", AutoSize = true, Location = new Point(15, y + 3) };
            _numVersionCount = new NumericUpDown
            {
                Location = new Point(110, y),
                Width = 60,
                Minimum = 1,
                Maximum = 50,
                Value = 5
            };
            var lblVerHint = new Label
            {
                Text = "copies per overwritten file",
                AutoSize = true,
                Location = new Point(180, y + 3),
                ForeColor = SystemColors.GrayText
            };
            y += 30;

            _chkRecvVersions = new CheckBox
            {
                Text = "Also keep versions of files received over the LAN",
                AutoSize = true,
                Location = new Point(15, y)
            };
            y += 40;

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(300, y), Width = 80 };
            btnOk.Click += OnOk;
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(388, y), Width = 80 };
            CancelButton = btnCancel;

            Controls.Add(_chkDark);
            Controls.Add(_chkEncrypt);
            Controls.Add(lblKey); Controls.Add(_txtKey); Controls.Add(btnGen); Controls.Add(btnCopy); Controls.Add(lblKeyHint);
            Controls.Add(lblVer); Controls.Add(_numVersionCount); Controls.Add(lblVerHint);
            Controls.Add(_chkRecvVersions);
            Controls.Add(btnOk); Controls.Add(btnCancel);
            AcceptButton = btnOk;

            AppSettings s = Program.Settings;
            _chkDark.Checked = s.DarkMode;
            _chkEncrypt.Checked = s.EncryptTransfers;
            _txtKey.Text = s.AesKeyB64;
            _numVersionCount.Value = Math.Max(1, Math.Min(50, s.VersionCount));
            _chkRecvVersions.Checked = s.KeepReceivedVersions;

            // theme the dialog with the SAVED look (not the tentative checkbox)
            Theme.Apply(this, s.DarkMode);
        }

        private void OnOk(object sender, EventArgs e)
        {
            AppSettings s = Program.Settings;
            bool themeChanged = s.DarkMode != _chkDark.Checked;

            s.DarkMode = _chkDark.Checked;
            s.EncryptTransfers = _chkEncrypt.Checked;
            s.VersionCount = (int)_numVersionCount.Value;
            s.KeepReceivedVersions = _chkRecvVersions.Checked;

            if (_chkEncrypt.Checked && string.IsNullOrEmpty(s.AesKeyB64))
            {
                s.AesKeyB64 = GenerateKey();
                MessageBox.Show("An AES key was generated. Copy it (Settings → Copy) and paste on the other PC.",
                    "Encryption", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            Program.Settings.Save();

            if (themeChanged)
                Theme.ApplyAllOpenForms(s.DarkMode);
        }

        private static string GenerateKey()
        {
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.GenerateKey();
                return Convert.ToBase64String(aes.Key);
            }
        }
    }
}
