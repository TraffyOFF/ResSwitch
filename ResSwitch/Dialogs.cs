using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using L = ResSwitch.Localization;

namespace ResSwitch
{
    // "Keep this resolution?" with an automatic revert, like the Windows dialog.
    internal class ConfirmRevertForm : Form
    {
        private int left;
        private readonly Label lbl = new Label();
        private readonly Timer timer = new Timer();

        public ConfirmRevertForm(int seconds)
        {
            left = seconds;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = "ResSwitch";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; TopMost = true;
            ClientSize = new Size(380, 120);

            lbl.Dock = DockStyle.Top;
            lbl.Height = 70;
            lbl.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(lbl);

            Button keep = new Button();
            keep.Text = L.T("test.keep");
            keep.DialogResult = DialogResult.Yes;
            keep.SetBounds(60, 78, 120, 30);
            Button revert = new Button();
            revert.Text = L.T("test.revert");
            revert.DialogResult = DialogResult.No;
            revert.SetBounds(200, 78, 120, 30);
            Controls.Add(keep);
            Controls.Add(revert);
            AcceptButton = keep;
            CancelButton = revert;

            UpdateText();
            timer.Interval = 1000;
            timer.Tick += delegate
            {
                left--;
                if (left <= 0) { timer.Stop(); DialogResult = DialogResult.No; Close(); }
                else UpdateText();
            };
            timer.Start();
            Theme.Apply(this);
        }

        private void UpdateText() { lbl.Text = L.T("test.confirm", left); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // Simple "choose one in a list" dialog.
    internal class PickListForm : Form
    {
        private readonly ListBox list = new ListBox();

        public int SelectedIndex { get { return list.SelectedIndex; } }

        public PickListForm(string title, string prompt, IList<string> items, int preselect)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = title;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(520, 360);
            MinimumSize = new Size(360, 260);

            Label l = new Label();
            l.Text = prompt;
            l.Dock = DockStyle.Top;
            l.Height = 28;
            l.Padding = new Padding(8, 8, 8, 0);

            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 44;
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.Padding = new Padding(8, 6, 8, 6);
            Button cancel = new Button();
            cancel.Text = L.T("common.cancel");
            cancel.DialogResult = DialogResult.Cancel;
            Button ok = new Button();
            ok.Text = L.T("common.ok");
            ok.DialogResult = DialogResult.OK;
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;

            list.Dock = DockStyle.Fill;
            foreach (string s in items) list.Items.Add(s);
            if (list.Items.Count > 0) list.SelectedIndex = (preselect >= 0 && preselect < list.Items.Count) ? preselect : 0;
            list.DoubleClick += delegate { if (list.SelectedIndex >= 0) DialogResult = DialogResult.OK; };

            Controls.Add(list);
            Controls.Add(bottom);
            Controls.Add(l);
            Theme.Apply(this);
        }
    }
}
