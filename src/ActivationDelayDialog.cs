using System;
using System.Drawing;
using System.Windows.Forms;

namespace FpsSocdTool
{
    /// <summary>Modal dialog for the delay between release and the reverse tap.</summary>
    internal sealed class ActivationDelayDialog : Form
    {
        private readonly NumericUpDown _value;

        internal int SelectedValue
        {
            get { return (int)_value.Value; }
        }

        internal ActivationDelayDialog(int current)
        {
            Text = "反向点按生效延迟";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(300, 128);
            Font = SystemFonts.MessageBoxFont;

            Label label = new Label();
            label.Text = "松开方向键后等待多久发送反向补键（0-500 毫秒）：";
            label.AutoSize = true;
            label.Location = new Point(12, 14);
            Controls.Add(label);

            _value = new NumericUpDown();
            _value.Minimum = 0;
            _value.Maximum = 500;
            _value.Increment = 5;
            _value.Value = Math.Max(0, Math.Min(500, current));
            _value.Location = new Point(12, 42);
            _value.Width = 90;
            Controls.Add(_value);

            Label unit = new Label();
            unit.Text = "ms";
            unit.AutoSize = true;
            unit.Location = new Point(108, 45);
            Controls.Add(unit);

            Label hint = new Label();
            hint.Text = "0 = 松开后立即补键；越大越晚触发。";
            hint.AutoSize = true;
            hint.ForeColor = SystemColors.GrayText;
            hint.Location = new Point(12, 72);
            Controls.Add(hint);

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(146, 96);
            ok.Width = 65;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(218, 96);
            cancel.Width = 65;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
