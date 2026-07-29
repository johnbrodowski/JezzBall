using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace JezzBall
{
    // Persistent high-score table stored under %AppData%\JezzBall\highscores.txt.
    static class HighScores
    {
        public struct Entry { public string Name; public int Score; public DateTime When; }

        const int Keep = 10;
        static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JezzBall");
        static readonly string File = Path.Combine(Dir, "highscores.txt");

        static List<Entry> _all;
        static List<Entry> All { get { if (_all == null) Load(); return _all; } }

        static void Load()
        {
            _all = new List<Entry>();
            try
            {
                if (!System.IO.File.Exists(File)) return;
                foreach (var line in System.IO.File.ReadAllLines(File))
                {
                    var p = line.Split('\t');
                    if (p.Length == 3 && int.TryParse(p[0], out int sc) && DateTime.TryParse(p[1], out var dt))
                        _all.Add(new Entry { Score = sc, When = dt, Name = p[2] });
                }
            }
            catch { /* corrupt or unreadable: start fresh */ }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                System.IO.File.WriteAllLines(File,
                    All.Select(e => e.Score + "\t" + e.When.ToString("o") + "\t" + e.Name));
            }
            catch { /* best-effort */ }
        }

        public static bool Qualifies(int score)
        {
            if (score <= 0) return false;
            var top = HallOfFame();
            return top.Count < Keep || score > top.Last().Score;
        }

        public static void Add(string name, int score)
        {
            All.Add(new Entry { Name = name, Score = score, When = DateTime.Now });
            Save();
        }

        public static void Clear() { All.Clear(); Save(); }

        public static List<Entry> HallOfFame() =>
            All.OrderByDescending(e => e.Score).Take(Keep).ToList();

        public static List<Entry> Today() =>
            All.Where(e => e.When.Date == DateTime.Today)
               .OrderByDescending(e => e.Score).Take(Keep).ToList();
    }

    // Scoreboard window: Hall of Fame + Today's High Scores, matching the original's layout
    // (using the app's current gray dialog style rather than the Windows 3.1 palette).
    public class HighScoresDialog : Form
    {
        public HighScoresDialog()
        {
            Text = "JezzBall High Scores";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            BackColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
            Font = new Font("Tahoma", 9f);
            ClientSize = new Size(300, 420);

            Controls.Add(MakeGroup("Hall of Fame", HighScores.HallOfFame(), 12, 176));
            Controls.Add(MakeGroup("Today's High Scores", HighScores.Today(), 200, 176));

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 150, Top = 386, Width = 66 };
            var clear = new Button { Text = "Clear Scores", Left = 222, Top = 386, Width = 66 };
            clear.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Clear all high scores?", "JezzBall",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    HighScores.Clear();
                    Controls.Clear();
                    // rebuild after clearing
                    Controls.Add(MakeGroup("Hall of Fame", HighScores.HallOfFame(), 12, 176));
                    Controls.Add(MakeGroup("Today's High Scores", HighScores.Today(), 200, 176));
                    var ok2 = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 150, Top = 386, Width = 66 };
                    var clr2 = new Button { Text = "Clear Scores", Left = 222, Top = 386, Width = 66 };
                    clr2.Click += (s2, e2) => Close();
                    Controls.Add(ok2); Controls.Add(clr2);
                    AcceptButton = ok2;
                }
            };
            Controls.Add(ok); Controls.Add(clear);
            AcceptButton = ok;
        }

        static GroupBox MakeGroup(string title, List<HighScores.Entry> rows, int top, int height)
        {
            var gb = new GroupBox { Text = title, Left = 12, Top = top, Width = 276, Height = height };
            var list = new Label
            {
                Left = 12, Top = 22, Width = 252, Height = height - 34,
                TextAlign = ContentAlignment.TopLeft,
                Font = new Font("Consolas", 10f),
                Text = rows.Count == 0 ? "  (none yet)"
                     : string.Join("\n", rows.Select((e, i) => $"{i + 1,2}. {e.Score,6}  {e.Name}")),
            };
            gb.Controls.Add(list);
            return gb;
        }
    }

    // Prompt shown when the player earns a high score.
    public class NameEntryDialog : Form
    {
        readonly TextBox box;
        public string EnteredName => box.Text.Trim();

        public NameEntryDialog()
        {
            Text = "JezzBall High Score";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            BackColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
            Font = new Font("Tahoma", 9f);
            ClientSize = new Size(300, 120);

            Controls.Add(new Label
            {
                Text = "You have achieved a high score!\nPlease enter your name:",
                AutoSize = false, Left = 16, Top = 14, Width = 268, Height = 36,
                TextAlign = ContentAlignment.MiddleCenter,
            });
            box = new TextBox { Left = 20, Top = 56, Width = 260 };
            Controls.Add(box);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 114, Top = 86, Width = 72 };
            Controls.Add(ok);
            AcceptButton = ok;
        }
    }
}
