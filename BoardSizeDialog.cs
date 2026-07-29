using System.Drawing;
using System.Windows.Forms;

namespace JezzBall
{
    // Win95-styled prompt for choosing the board's columns and rows.
    public class BoardSizeDialog : Form
    {
        public int ChosenCols => (int)colsInput.Value;
        public int ChosenRows => (int)rowsInput.Value;

        readonly NumericUpDown colsInput;
        readonly NumericUpDown rowsInput;

        public BoardSizeDialog(int cols, int rows, int min, int maxCols, int maxRows)
        {
            Text = "Board Size";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(240, 140);
            BackColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
            Font = new Font("Tahoma", 9f);

            Controls.Add(new Label { Text = "Columns:", AutoSize = true, Left = 16, Top = 18 });
            colsInput = new NumericUpDown
            {
                Minimum = min, Maximum = maxCols,
                Value = System.Math.Min(maxCols, System.Math.Max(min, cols)),
                Left = 100, Top = 15, Width = 70,
            };
            Controls.Add(colsInput);

            Controls.Add(new Label { Text = "Rows:", AutoSize = true, Left = 16, Top = 50 });
            rowsInput = new NumericUpDown
            {
                Minimum = min, Maximum = maxRows,
                Value = System.Math.Min(maxRows, System.Math.Max(min, rows)),
                Left = 100, Top = 47, Width = 70,
            };
            Controls.Add(rowsInput);

            Controls.Add(new Label
            {
                Text = "Original is 20 x 28. Changing size restarts the level.",
                AutoSize = false, Left = 16, Top = 78, Width = 210, Height = 30,
                ForeColor = Color.FromArgb(0x40, 0x40, 0x40),
            });

            var ok = new Button
            {
                Text = "OK", DialogResult = DialogResult.OK,
                Left = ClientSize.Width - 164, Top = ClientSize.Height - 32, Width = 75,
            };
            var cancel = new Button
            {
                Text = "Cancel", DialogResult = DialogResult.Cancel,
                Left = ClientSize.Width - 83, Top = ClientSize.Height - 32, Width = 75,
            };
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
