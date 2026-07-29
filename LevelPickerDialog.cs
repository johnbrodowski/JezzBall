using System.Drawing;
using System.Windows.Forms;

namespace JezzBall
{
    // Small Win95-styled prompt for jumping straight to a level;
    // that level's ball count and lives both equal the level number.
    public class LevelPickerDialog : Form
    {
        public int ChosenLevel => (int)levelInput.Value;

        readonly NumericUpDown levelInput;

        public LevelPickerDialog(int startLevel)
        {
            Text = "Choose Level";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(260, 110);
            BackColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
            Font = new Font("Tahoma", 9f);

            var label = new Label
            {
                Text = "Start at level:",
                AutoSize = true,
                Left = 16,
                Top = 16,
            };
            Controls.Add(label);

            levelInput = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 99,
                Value = System.Math.Min(99, System.Math.Max(1, startLevel)),
                Left = 16,
                Top = 40,
                Width = 80,
            };
            Controls.Add(levelInput);

            var hint = new Label
            {
                Text = "Balls and lives = level number + 1",
                AutoSize = true,
                Left = 16,
                Top = 68,
                ForeColor = Color.FromArgb(0x40, 0x40, 0x40),
            };
            Controls.Add(hint);

            var ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Left = ClientSize.Width - 164,
                Top = ClientSize.Height - 32,
                Width = 75,
            };
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Left = ClientSize.Width - 83,
                Top = ClientSize.Height - 32,
                Width = 75,
            };
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
