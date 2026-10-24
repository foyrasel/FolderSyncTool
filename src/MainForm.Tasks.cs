using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FolderSyncTool
{
    public partial class MainForm : Form
    {
        private ListView _tasksList;
        private Button _btnNewTask, _btnEditTask, _btnDeleteTask, _btnDoneTask, _btnSendTask;

        private void InitTasksTab()
        {
            _tabTasks = new TabPage("Work");

            _tasksList = new ListView();
            _tasksList.View = View.Details;
            _tasksList.FullRowSelect = true;
            _tasksList.GridLines = true;
            _tasksList.Dock = DockStyle.Fill;
            _tasksList.Columns.Add("Task", 330);
            _tasksList.Columns.Add("Deadline", 130);
            _tasksList.Columns.Add("Priority", 80);
            _tasksList.Columns.Add("Status", 90);
            _tasksList.Columns.Add("From", 110);
            _tasksList.ItemActivate += delegate { EditTask(); };

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = 40;
            toolbar.Padding = new Padding(4);

            _btnNewTask = MkButton("New task…", 90, delegate { NewTask(); });
            _btnEditTask = MkButton("Edit", 60, delegate { EditTask(); });
            _btnDeleteTask = MkButton("Delete", 70, delegate { DeleteTask(); });
            _btnDoneTask = MkButton("Mark done", 90, delegate { MarkTaskDone(); });
            _btnSendTask = MkButton("Send to PC(s)…", 120, delegate { SendSelectedTasks(); });

            toolbar.Controls.Add(_btnNewTask);
            toolbar.Controls.Add(_btnEditTask);
            toolbar.Controls.Add(_btnDeleteTask);
            toolbar.Controls.Add(_btnDoneTask);
            toolbar.Controls.Add(_btnSendTask);

            _tabTasks.Controls.Add(_tasksList);  // fill (processed last)
            _tabTasks.Controls.Add(toolbar);     // top
        }

        // ---- persistence ---------------------------------------------------

        private List<WorkItem> LoadTasks()
        {
            string path = Program.TasksPathOverride ?? AppSettings.TasksPath;
            return TaskStore.LoadAll(path);
        }

        private void SaveTasks(List<WorkItem> items)
        {
            string path = Program.TasksPathOverride ?? AppSettings.TasksPath;
            TaskStore.SaveAll(path, items);
        }

        public void RefreshTasksList()
        {
            if (_tasksList == null || _tasksList.IsDisposed) return;
            string selectedId = _tasksList.SelectedItems.Count > 0
                ? ((WorkItem)_tasksList.SelectedItems[0].Tag).Id : null;

            _tasksList.Items.Clear();
            List<WorkItem> items = LoadTasks();
            items.Sort(CompareTasksByDeadline);

            foreach (WorkItem w in items)
            {
                var item = new ListViewItem(w.Title);
                item.SubItems.Add(w.DeadlineUtc != null
                    ? w.DeadlineUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—");
                item.SubItems.Add(w.Priority.ToString());
                item.SubItems.Add(w.Status == "done" ? "done" : (w.Confirmed ? "confirmed" : "open"));
                item.SubItems.Add(w.AssignedBy);
                item.Tag = w;
                item.ForeColor = TaskRowColor(w);
                if (w.Status == "done")
                    item.Font = new Font(_tasksList.Font, FontStyle.Strikeout);
                _tasksList.Items.Add(item);
                if (selectedId != null && w.Id == selectedId) item.Selected = true;
            }
        }

        private static int CompareTasksByDeadline(WorkItem a, WorkItem b)
        {
            if (a.DeadlineUtc == null && b.DeadlineUtc == null)
                return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            if (a.DeadlineUtc == null) return 1;
            if (b.DeadlineUtc == null) return -1;
            return DateTime.Compare(a.DeadlineUtc.Value, b.DeadlineUtc.Value);
        }

        private Color TaskRowColor(WorkItem w)
        {
            if (w.Status == "done") return Theme.RowText(TaskPriority.Low, Program.Settings.DarkMode);
            if (w.DeadlineUtc != null && w.DeadlineUtc.Value < DateTime.UtcNow)
                return Theme.OverdueText(Program.Settings.DarkMode);
            return Theme.RowText(w.Priority, Program.Settings.DarkMode);
        }

        // ---- actions ---------------------------------------------------------

        private void NewTask()
        {
            using (TaskEditorForm f = new TaskEditorForm(null))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.Item != null)
                {
                    TaskStore.AddOrUpdate(Program.TasksPathOverride ?? AppSettings.TasksPath, f.Item);
                    RefreshTasksList();
                }
            }
        }

        private void EditTask()
        {
            if (_tasksList.SelectedItems.Count == 0) return;
            WorkItem w = (WorkItem)_tasksList.SelectedItems[0].Tag;
            using (TaskEditorForm f = new TaskEditorForm(w))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    TaskStore.AddOrUpdate(Program.TasksPathOverride ?? AppSettings.TasksPath, w);
                    RefreshTasksList();
                }
            }
        }

        private void DeleteTask()
        {
            if (_tasksList.SelectedItems.Count == 0) return;
            WorkItem w = (WorkItem)_tasksList.SelectedItems[0].Tag;
            if (MessageBox.Show("Delete task \"" + w.Title + "\"?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                TaskStore.Remove(Program.TasksPathOverride ?? AppSettings.TasksPath, w.Id);
                RefreshTasksList();
            }
        }

        private void MarkTaskDone()
        {
            if (_tasksList.SelectedItems.Count == 0) return;
            WorkItem w = (WorkItem)_tasksList.SelectedItems[0].Tag;
            w.Status = "done";
            TaskStore.AddOrUpdate(Program.TasksPathOverride ?? AppSettings.TasksPath, w);
            RefreshTasksList();
        }

        private void SendSelectedTasks()
        {
            if (_tasksList.SelectedItems.Count == 0)
            {
                MessageBox.Show("Select task(s) first.", "Send task",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_discovery == null || _discovery.Snapshot().Length == 0)
            {
                MessageBox.Show("No PCs discovered.", "Send task",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            byte[] key = (Program.Settings.EncryptTransfers) ? AesKey() : null;
            List<WorkItem> selected = new List<WorkItem>();
            foreach (ListViewItem it in _tasksList.SelectedItems)
                selected.Add((WorkItem)it.Tag);

            foreach (WorkItem w in selected)
            {
                w.AssignedBy = _discovery.MachineName;
                w.Confirmed = false;
                TaskStore.AddOrUpdate(Program.TasksPathOverride ?? AppSettings.TasksPath, w);

                foreach (PeerInfo p in _discovery.Snapshot())
                    LanTransfer.SendSmall(p.Ip, "TASK", TaskStore.ToWire(w), key, _discovery.MachineName);
            }
            RefreshTasksList();
            ShowBalloon("Task sent", selected.Count + " task(s) sent to all PCs.");
        }

        // ---- incoming ---------------------------------------------------------

        private void OnTaskReceived(string from, WorkItem w)
        {
            if (IsHandleCreated)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        RefreshTasksList();
                        ShowBalloon("New task from " + from, w.Title);
                        Log("Task received from " + from + ": " + w.Title);
                    });
                }
                catch { }
            }
        }

        private void OnTaskDoneReceived(string from, string taskId)
        {
            if (IsHandleCreated)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        List<WorkItem> items = LoadTasks();
                        WorkItem w = items.Find(x => x.Id == taskId);
                        if (w != null)
                        {
                            w.Status = "done";
                            w.Confirmed = true;
                            SaveTasks(items);
                            RefreshTasksList();
                            Log(from + " completed task: " + w.Title);
                        }
                    });
                }
                catch { }
            }
        }

        // ---- deadline reminders (v5 #1) -----------------------------------------

        private void CheckDeadlines()
        {
            List<WorkItem> items = LoadTasks();
            List<WorkItem> due = TaskStore.DueForReminder(items, DateTime.UtcNow, TimeSpan.FromMinutes(60));
            if (due.Count == 0) return;

            SaveTasks(items); // persist Reminded/RemindAgain flags set above
            RefreshTasksList();

            foreach (WorkItem w in due)
            {
                string when = w.DeadlineUtc != null
                    ? w.DeadlineUtc.Value.ToLocalTime().ToString("HH:mm")
                    : "";
                bool overdue = w.DeadlineUtc != null && w.DeadlineUtc.Value < DateTime.UtcNow;
                ShowBalloon(overdue ? "Task OVERDUE" : "Task due soon",
                    w.Title + (overdue ? " (was due " + when + ")" : " — due " + when));
                Log(overdue ? "Reminder: OVERDUE — " + w.Title : "Reminder: " + w.Title + " due at " + when);
            }
        }
    }
}
