using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FolderSyncTool
{
    /// <summary>
    /// Add/edit one pair. v5: per-pair file type filter, versioning toggle,
    /// VSS shadow-copy toggle (locked files) with elevation hint.
    /// </summary>
    public class PairEditorForm : Form
    {
        public PairInfo Pair;

        private TextBox _txtSource;
        private TextBox _txtDest;
        private ComboBox _cboCloud;
        private TextBox _txtTypes;
        private CheckBox _chkKeepVersions;
        private CheckBox _chkShadow;
        private CheckBox _chkEnabled;

        public PairEditorForm(PairInfo existing)
        {
            Pair = existing != null ? existing : new PairInfo();

            Text = existing != null ? "Edit pair" : "Add pair";
            Width = 560;
            Height = 360;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            int y = 15;

            var lblSrc = new Label { Text = "Source folder:", AutoSize = true, Location = new Point(12, y + 3) };
            _txtSource = new TextBox { Location = new Point(110, y), Width = 340 };
            var btnSrc = new Button { Text = "Browse…", Location = new Point(458, y - 1), Width = 80 };
            btnSrc.Click += delegate { Browse(_txtSource); };
            y += 34;

            var lblDst = new Label { Text = "Destination:", AutoSize = true, Location = new Point(12, y + 3) };
            _txtDest = new TextBox { Location = new Point(110, y), Width = 340 };
            var btnDst = new Button { Text = "Browse…", Location = new Point(458, y - 1), Width = 80 };
            btnDst.Click += delegate { Browse(_txtDest); };
            y += 30;

            var lblCloud = new Label { Text = "Quick pick:", AutoSize = true, Location = new Point(12, y + 3) };
            _cboCloud = new ComboBox { Location = new Point(110, y), Width = 340, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboCloud.Items.Add("(choose a cloud folder…)");
            int idx = 1;
            foreach (CloudFolder c in CloudFolders.GetKnownCloudFolders())
            {
                _cboCloud.Items.Add(c.Name + " — " + c.Path + (c.Exists ? "" : "  (not found)"));
                idx++;
            }
            _cboCloud.SelectedIndex = 0;
            _cboCloud.SelectedIndexChanged += delegate
            {
                if (_cboCloud.SelectedIndex > 0)
                {
                    string picked = _cboCloud.SelectedItem.ToString();
                    int dash = picked.IndexOf(" — ", StringComparison.Ordinal);
                    string path = dash > 0 ? picked.Substring(dash + 3) : picked;
                    int notFound = path.IndexOf("  (not found)", StringComparison.Ordinal);
                    if (notFound > 0) path = path.Substring(0, notFound);
                    _txtDest.Text = Path.Combine(path, "FolderSyncTool");
                }
            };
            y += 36;

            var lblTypes = new Label { Text = "File types:", AutoSize = true, Location = new Point(12, y + 3) };
            _txtTypes = new TextBox { Location = new Point(110, y), Width = 340 };
            var lblTypesHint = new Label
            {
                Text = "(empty = use global filter)",
                AutoSize = true,
                Location = new Point(110, y + 22),
                ForeColor = SystemColors.GrayText
            };
            y += 48;

            FlowLayoutPanel typeChips = new FlowLayoutPanel
            {
                Location = new Point(110, y),
                Width = 430,
                Height = 30,
                FlowDirection = FlowDirection.LeftToRight
            };
            foreach (string ext in new[] { ".docx", ".xlsx", ".pptx", ".pdf", ".jpg", ".png", ".zip" })
            {
                var chip = new Button
                {
                    Text = ext,
                    Width = 52,
                    Height = 24,
                    Tag = ext
                };
                chip.Click += delegate
                {
                    string cur = _txtTypes.Text.Trim();
                    if (cur.IndexOf(ext, StringComparison.OrdinalIgnoreCase) >= 0) return;
                    _txtTypes.Text = string.IsNullOrEmpty(cur) ? ext : cur + "," + ext;
                };
                typeChips.Controls.Add(chip);
            }
            y += 36;

            _chkKeepVersions = new CheckBox
            {
                Text = "Keep versions of overwritten files (in _versions, count from Settings)",
                AutoSize = true,
                Location = new Point(110, y)
            };
            y += 24;

            _chkShadow = new CheckBox
            {
                Text = "Use VSS shadow copy for locked files (needs admin)",
                AutoSize = true,
                Location = new Point(110, y)
            };
            y += 24;

            _chkEnabled = new CheckBox
            {
                Text = "Pair enabled",
                AutoSize = true,
                Location = new Point(110, y)
            };
            y += 40;

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(370, y), Width = 80 };
            btnOk.Click += OnOk;
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(458, y), Width = 80 };
            CancelButton = btnCancel;

            Controls.Add(lblSrc); Controls.Add(_txtSource); Controls.Add(btnSrc);
            Controls.Add(lblDst); Controls.Add(_txtDest); Controls.Add(btnDst);
            Controls.Add(lblCloud); Controls.Add(_cboCloud);
            Controls.Add(lblTypes); Controls.Add(_txtTypes); Controls.Add(lblTypesHint);
            Controls.Add(typeChips);
            Controls.Add(_chkKeepVersions);
            Controls.Add(_chkShadow);
            Controls.Add(_chkEnabled);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;

            // load existing
            _txtSource.Text = Pair.Source;
            _txtDest.Text = Pair.Dest;
            _txtTypes.Text = Pair.Types;
            _chkKeepVersions.Checked = Pair.KeepVersions;
            _chkShadow.Checked = Pair.UseShadowCopy;
            _chkEnabled.Checked = Pair.Enabled;

            Theme.Apply(this, Program.Settings.DarkMode);
        }

        private void Browse(TextBox target)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                if (Directory.Exists(target.Text)) dlg.SelectedPath = target.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            string src = _txtSource.Text.Trim();
            string dst = _txtDest.Text.Trim();
            if (src.Length == 0 || dst.Length == 0)
            {
                MessageBox.Show("Both source and destination are required.", "Pair",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)
                || IsSubPath(dst, src))
            {
                MessageBox.Show("Destination must not be inside the source (or equal to it).",
                    "Pair", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Pair.Source = src;
            Pair.Dest = dst;
            Pair.Types = _txtTypes.Text.Trim();
            Pair.KeepVersions = _chkKeepVersions.Checked;
            Pair.UseShadowCopy = _chkShadow.Checked;
            Pair.Enabled = _chkEnabled.Checked;
            DialogResult = DialogResult.OK;
        }

        private static bool IsSubPath(string candidateChild, string candidateParent)
        {
            try
            {
                string c = Path.GetFullPath(candidateChild).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                string p = Path.GetFullPath(candidateParent).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
