using System;
using System.Drawing;
using System.Windows.Forms;

namespace FolderSyncTool
{
    public class TaskEditorForm : Form
    {
        public WorkItem Item;

        private TextBox _txtTitle;
        private TextBox _txtNotes;
        private CheckBox _chkHasDeadline;
        private DateTimePicker _dtpDeadline;
        private ComboBox _cboPriority;

        public TaskEditorForm(WorkItem existing)
        {
            Item = existing != null ? existing : new WorkItem();

            Text = existing != null ? "Edit task" : "New task";
            Width = 480;
            Height = 330;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            int y = 15;
            var lblTitle = new Label { Text = "Title:", AutoSize = true, Location = new Point(12, y + 3) };
            _txtTitle = new TextBox { Location = new Point(90, y), Width = 360 };
            y += 32;

            var lblNotes = new Label { Text = "Notes:", AutoSize = true, Location = new Point(12, y + 3) };
            _txtNotes = new TextBox
            {
                Location = new Point(90, y),
                Width = 360,
                Height = 80,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };
            y += 92;

            _chkHasDeadline = new CheckBox
            {
                Text = "Deadline",
                AutoSize = true,
                Location = new Point(90, y)
            };
            _dtpDeadline = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd HH:mm",
                Location = new Point(180, y - 2),
                Width = 170,
                ShowCheckBox = false
            };
            y += 30;

            var lblPri = new Label { Text = "Priority:", AutoSize = true, Location = new Point(12, y + 3) };
            _cboPriority = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(90, y),
                Width = 120
            };
            _cboPriority.Items.AddRange(new object[] { "Low", "Normal", "High" });
            y += 40;

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(284, y), Width = 80 };
            btnOk.Click += OnOk;
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(370, y), Width = 80 };
            CancelButton = btnCancel;

            Controls.Add(lblTitle); Controls.Add(_txtTitle);
            Controls.Add(lblNotes); Controls.Add(_txtNotes);
            Controls.Add(_chkHasDeadline); Controls.Add(_dtpDeadline);
            Controls.Add(lblPri); Controls.Add(_cboPriority);
            Controls.Add(btnOk); Controls.Add(btnCancel);
            AcceptButton = btnOk;

            _txtTitle.Text = Item.Title;
            _txtNotes.Text = Item.Notes;
            _chkHasDeadline.Checked = Item.DeadlineUtc != null;
            _dtpDeadline.Value = Item.DeadlineUtc != null
                ? Item.DeadlineUtc.Value.ToLocalTime()
                : DateTime.Now.AddHours(2);
            _cboPriority.SelectedIndex = (int)Item.Priority;

            Theme.Apply(this, Program.Settings.DarkMode);
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (_txtTitle.Text.Trim().Length == 0)
            {
                MessageBox.Show("Title is required.", "Task",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Item.Title = _txtTitle.Text.Trim();
            Item.Notes = _txtNotes.Text.Trim();
            Item.DeadlineUtc = _chkHasDeadline.Checked
                ? _dtpDeadline.Value.ToUniversalTime()
                : (DateTime?)null;
            Item.Priority = _cboPriority.SelectedIndex >= 0
                ? (TaskPriority)_cboPriority.SelectedIndex : TaskPriority.Normal;
            DialogResult = DialogResult.OK;
        }
    }
}
