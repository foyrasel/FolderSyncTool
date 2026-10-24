using System;
using System.Drawing;
using System.Windows.Forms;

namespace FolderSyncTool
{
    /// <summary>
    /// v5: application-wide dark mode. ApplyTheme walks every open form and
    /// recolors controls recursively; the toggle applies instantly without
    /// restarting (a few owner-drawn details may need a reopen to refresh).
    /// </summary>
    public static class Theme
    {
        public static readonly Color LightBack = SystemColors.Control;
        public static readonly Color LightText = SystemColors.ControlText;

        public static readonly Color DarkBack = Color.FromArgb(30, 30, 30);        // #1E1E1E
        public static readonly Color DarkPanel = Color.FromArgb(37, 37, 38);       // #252526
        public static readonly Color DarkText = Color.FromArgb(241, 241, 241);     // #F1F1F1
        public static readonly Color DarkAccent = Color.FromArgb(0, 122, 204);     // #007ACC
        public static readonly Color DarkField = Color.FromArgb(51, 51, 55);       // #333337
        public static readonly Color DarkBorder = Color.FromArgb(67, 67, 70);

        public static bool IsDark { get; private set; }

        public static void Apply(Form form, bool dark)
        {
            if (form == null) return;
            IsDark = dark;
            Walk(form, dark);
        }

        public static void ApplyAllOpenForms(bool dark)
        {
            foreach (Form f in Application.OpenForms)
                Apply(f, dark);
        }

        private static void Walk(Control c, bool dark)
        {
            if (dark) StyleDark(c); else StyleLight(c);
            foreach (Control child in c.Controls) Walk(child, dark);
        }

        private static void StyleDark(Control c)
        {
            if (c is Form || c is Panel || c is GroupBox || c is TabControl || c is TabPage
                || c is SplitContainer || c is FlowLayoutPanel || c is TableLayoutPanel)
            {
                c.BackColor = DarkBack;
                c.ForeColor = DarkText;
            }
            else if (c is Label || c is LinkLabel || c is CheckBox || c is RadioButton)
            {
                c.BackColor = c.Parent is TabPage || c.Parent is Form || c.Parent is Panel
                    || c.Parent is GroupBox ? c.Parent.BackColor : DarkBack;
                c.ForeColor = DarkText;
            }
            else if (c is Button)
            {
                Button b = (Button)c;
                b.BackColor = DarkField;
                b.ForeColor = DarkText;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = DarkBorder;
            }
            else if (c is TextBox)
            {
                TextBox tb = (TextBox)c;
                if (tb.ReadOnly)
                {
                    tb.BackColor = DarkPanel;
                    tb.ForeColor = DarkText;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                }
                else
                {
                    tb.BackColor = DarkField;
                    tb.ForeColor = DarkText;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                }
            }
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c;
                cb.BackColor = DarkField;
                cb.ForeColor = DarkText;
                cb.FlatStyle = FlatStyle.Flat;
            }
            else if (c is ListView)
            {
                ListView lv = (ListView)c;
                lv.BackColor = DarkPanel;
                lv.ForeColor = DarkText;
                lv.BorderStyle = BorderStyle.FixedSingle;
                foreach (ColumnHeader h in lv.Columns) { /* header uses system theme */ }
            }
            else if (c is ListBox)
            {
                ListBox lb = (ListBox)c;
                lb.BackColor = DarkPanel;
                lb.ForeColor = DarkText;
                lb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is ProgressBar)
            {
                ProgressBar pb = (ProgressBar)c;
                pb.BackColor = DarkPanel;
                pb.ForeColor = DarkAccent;
            }
            else if (c is DateTimePicker)
            {
                DateTimePicker dtp = (DateTimePicker)c;
                dtp.CalendarMonthBackground = DarkField;
                dtp.CalendarForeColor = DarkText;
                dtp.ForeColor = DarkText;
            }
            else if (c is NumericUpDown)
            {
                NumericUpDown nud = (NumericUpDown)c;
                nud.BackColor = DarkField;
                nud.ForeColor = DarkText;
                nud.BorderStyle = BorderStyle.FixedSingle;
            }
        }

        private static void StyleLight(Control c)
        {
            if (c is Button)
            {
                Button b = (Button)c;
                b.UseVisualStyleBackColor = true;
                b.ForeColor = LightText;
                b.FlatStyle = FlatStyle.Standard;
            }
            else if (c is ListView || c is ListBox)
            {
                c.BackColor = SystemColors.Window;
                c.ForeColor = LightText;
            }
            else if (c is TextBox)
            {
                TextBox tb = (TextBox)c;
                tb.BackColor = SystemColors.Window;
                tb.ForeColor = LightText;
                tb.BorderStyle = BorderStyle.Fixed3D;
            }
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c;
                cb.BackColor = SystemColors.Window;
                cb.ForeColor = LightText;
                cb.FlatStyle = FlatStyle.Standard;
            }
            else if (c is Form || c is Panel || c is GroupBox || c is TabPage
                || c is Label || c is CheckBox || c is RadioButton || c is ProgressBar)
            {
                c.BackColor = LightBack;
                c.ForeColor = LightText;
            }
        }

        // ---- row colors for lists ---------------------------------------

        public static Color RowText(TaskPriority p, bool dark)
        {
            if (!dark)
            {
                switch (p)
                {
                    case TaskPriority.High: return Color.FromArgb(156, 24, 24);
                    case TaskPriority.Low: return Color.FromArgb(90, 90, 90);
                    default: return LightText;
                }
            }
            switch (p)
            {
                case TaskPriority.High: return Color.FromArgb(255, 120, 120);
                case TaskPriority.Low: return Color.FromArgb(150, 150, 150);
                default: return DarkText;
            }
        }

        public static Color OverdueText(bool dark)
        {
            return dark ? Color.FromArgb(255, 80, 80) : Color.FromArgb(180, 0, 0);
        }
    }
}
