using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace JezzBall
{
    public class GameForm : Form
    {
        // --- gameplay constants (mirrors game.js) ---
        const int Cell = 14;              // ~20% larger than original 12; ball diameter matches so walls stay ball-width
        int Cols = 28;                     // default matches the original; adjustable via Game > Board Size...
        int Rows = 20;
        int BoardW => Cols * Cell;
        int BoardH => Rows * Cell;
        int TotalCells => Cols * Rows;
        const int MinDim = 10, MaxCols = 64, MaxRows = 50;
        const double WinPercent = 75;
        const int WallGrowInterval = 7;   // frames to grow one full cell
        const double BallBaseSpeed = 1.9; // scaled with Cell so motion stays in proportion
        const int StartTime = 1440;
        const double StepMs = 1000.0 / 60.0; // fixed 60Hz timestep

        static readonly Color BgBase  = Color.FromArgb(0xC0, 0xC0, 0xC0); // live area: gray grid
        static readonly Color BgLight = Color.FromArgb(0xDC, 0xDC, 0xDC); // top/left highlight for the tile bevel
        static readonly Color BgDark  = Color.FromArgb(0x9C, 0x9C, 0x9C); // bottom/right shadow for the tile bevel
        static readonly Color SolidColor = Color.Black;                  // walls & captured space go black
        static readonly Color NegColor = Color.FromArgb(0xC0, 0x00, 0x00); // up/left arm
        static readonly Color PosColor = Color.FromArgb(0x00, 0x00, 0xC0); // down/right arm

        enum GameState { Ready, Playing, Paused, LevelComplete, GameOver }

        class Ball
        {
            public double X, Y, Vx, Vy, Angle;
            public int R; // set at spawn from Cell/2
        }

        class Arm
        {
            public int Tip;
            public bool Blocked, Dead, Skipped;
            public double VisLen = Cell;
            public List<Point> Cells = new List<Point>();
            public bool Terminal => Dead || Blocked || Skipped;
        }

        class Wall
        {
            public bool Vertical;
            public int FixedCoord, Origin, RootCX, RootCY;
            public Arm Neg = new Arm(); // up/left
            public Arm Pos = new Arm(); // down/right
        }

        // --- state ---
        byte[] grid; // 0 empty, 1 solid, 2 growing; sized in ApplyBoardSize
        int filledCount;
        readonly List<Ball> balls = new List<Ball>();
        readonly List<Wall> wallQueue = new List<Wall>();
        Wall slotA, slotB; // walls currently allowed to grow their neg / pos arm
        int level = 1, score, livesRemaining = 2;
        int frameCount, timeLeft = StartTime;
        bool buildVertical = true;
        Point? hoverPx;
        GameState state = GameState.Ready;
        string message;
        int messageLife;
        readonly Random rng = new Random();

        // --- rendering resources ---
        Bitmap bgBitmap, fillBitmap;
        Graphics fillG;
        readonly Canvas board = new Canvas();
        bool cursorHidden;

        // --- HUD controls ---
        Label hudScore, hudTime, hudLives, hudPercent;
        Panel hudPanel, host;
        ToolStripMenuItem menuPause, slowItem, fastItem, soundItem;

        // --- sound & speed ---
        SoundFx sndBounce, sndDead;
        bool soundOn = true;
        double speedScale = 1.0;   // Fast = 1.0, Slow = 0.6
        bool scoreCounted;         // guards the game-over high-score prompt from firing twice

        // --- loop ---
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastMs;
        double acc;

        class Canvas : Panel
        {
            public Canvas()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer, true);
            }
        }

        public GameForm()
        {
            Text = "JezzBall";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
            Font = new Font("Tahoma", 9f);
            KeyPreview = true;

            LoadSounds();

            var menu = new MenuStrip();

            var game = new ToolStripMenuItem("Game");
            var newGame = new ToolStripMenuItem("New Game", null, (s, e) => NewGame()) { ShortcutKeys = Keys.F2 };
            menuPause = new ToolStripMenuItem("Pause", null, (s, e) => TogglePause()) { ShortcutKeys = Keys.F3 };
            var highScores = new ToolStripMenuItem("High Scores...", null, (s, e) => ShowHighScores());
            var chooseLevel = new ToolStripMenuItem("Choose Level...", null, (s, e) => ChooseLevel());
            var boardSize = new ToolStripMenuItem("Board Size...", null, (s, e) => ChooseBoardSize());
            var exit = new ToolStripMenuItem("Exit", null, (s, e) => Close());
            game.DropDownItems.AddRange(new ToolStripItem[] {
                newGame, menuPause, highScores, new ToolStripSeparator(),
                chooseLevel, boardSize, new ToolStripSeparator(), exit });

            var options = new ToolStripMenuItem("Options");
            slowItem = new ToolStripMenuItem("Slow", null, (s, e) => SetSpeed(false));
            fastItem = new ToolStripMenuItem("Fast", null, (s, e) => SetSpeed(true)) { Checked = true };
            soundItem = new ToolStripMenuItem("Sound", null, (s, e) => ToggleSound()) { Checked = true };
            options.DropDownItems.AddRange(new ToolStripItem[] {
                slowItem, fastItem, new ToolStripSeparator(), soundItem });

            var help = new ToolStripMenuItem("Help");
            help.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("How To Play", null, (s, e) => ShowHelp()),
                new ToolStripMenuItem("Commands", null, (s, e) => ShowCommands()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("About JezzBall...", null, (s, e) => ShowAbout()) });

            menu.Items.AddRange(new ToolStripItem[] { game, options, help });
            MainMenuStrip = menu;
            Controls.Add(menu);

            BackColor = Color.Black; // black surround like the original

            hudPanel = new Panel { BackColor = Color.Black, Top = menu.Height };
            hudScore = MakeHudLabel(hudPanel, "Score: 0", ContentAlignment.MiddleCenter, 0, 0, 240);
            hudLives = MakeHudLabel(hudPanel, "Lives: 2", ContentAlignment.MiddleCenter, 0, 0, 200);
            hudTime = MakeHudLabel(hudPanel, "Time Left: " + StartTime, ContentAlignment.MiddleCenter, 0, 0, 200);
            Controls.Add(hudPanel);

            host = new Panel
            {
                BackColor = Color.Black,
                BorderStyle = BorderStyle.None,
            };
            host.Controls.Add(board);
            Controls.Add(host);

            hudPercent = new Label
            {
                Text = "Area Cleared: 0.0%",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Color.Black,
                Height = 24,
                Font = new Font("Tahoma", 11f, FontStyle.Bold),
            };
            Controls.Add(hudPercent);

            ApplyBoardSize();

            board.Paint += Board_Paint;
            board.MouseDown += Board_MouseDown;
            board.MouseMove += (s, e) => hoverPx = e.Location;
            board.MouseLeave += (s, e) => { hoverPx = null; SetCursorHidden(false); };
            board.MouseEnter += (s, e) => SetCursorHidden(state == GameState.Playing);
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space && (state == GameState.Playing || state == GameState.Paused))
                {
                    TogglePause();
                    e.SuppressKeyPress = true;
                }
            };
            FormClosed += (s, e) => SetCursorHidden(false);

            timer.Interval = 15;
            timer.Tick += Loop;
            lastMs = clock.Elapsed.TotalMilliseconds;
            timer.Start();
            UpdateHud();
        }

        Label MakeHudLabel(Panel parent, string text, ContentAlignment align, int x, int y, int w)
        {
            var l = new Label
            {
                Text = text, TextAlign = align, Left = x, Top = y, Width = w, Height = 24,
                ForeColor = Color.White, BackColor = Color.Black,
                Font = new Font("Tahoma", 11f, FontStyle.Bold),
            };
            parent.Controls.Add(l);
            return l;
        }

        void BuildBackground()
        {
            bgBitmap?.Dispose();
            bgBitmap = new Bitmap(BoardW, BoardH);
            using (var g = Graphics.FromImage(bgBitmap))
            using (var light = new Pen(BgLight))
            using (var dark = new Pen(BgDark))
            {
                g.Clear(BgBase);
                // give each cell a slight raised-tile bevel: light on top/left, shadow on bottom/right
                for (int cy = 0; cy < Rows; cy++)
                    for (int cx = 0; cx < Cols; cx++)
                    {
                        int x = cx * Cell, y = cy * Cell, e = Cell - 1;
                        g.DrawLine(light, x, y, x + e, y);         // top
                        g.DrawLine(light, x, y, x, y + e);         // left
                        g.DrawLine(dark, x, y + e, x + e, y + e);  // bottom
                        g.DrawLine(dark, x + e, y, x + e, y + e);  // right
                    }
            }
        }

        // Re-lays out every size-dependent piece for the current Cols/Rows and
        // rebuilds the grid + off-screen bitmaps. Safe to call at runtime.
        void ApplyBoardSize()
        {
            grid = new byte[TotalCells];

            const int Margin = 28;    // black gutter left/right of the play area
            const int Pad = 3;        // thin black frame between HUD text and the grid
            const int HudZone = 84;   // roomy gap between the menu bar and the play area
            int width = BoardW + 2 * Margin;

            // HUD zone: Score centered on top, Lives + Time centered on a lower row
            int hudTop = MainMenuStrip.Height;
            hudPanel.Left = 0;
            hudPanel.Top = hudTop;
            hudPanel.Width = width;
            hudPanel.Height = HudZone;

            hudScore.Top = 10;
            hudScore.Left = (width - hudScore.Width) / 2;

            // second row vertically centered between the score line and the board
            int rowTop = (hudScore.Top + hudScore.Height + HudZone - hudLives.Height) / 2;
            hudLives.Top = rowTop;
            hudLives.Left = width / 2 - hudLives.Width;      // centered in the left half
            hudTime.Top = rowTop;
            hudTime.Left = width / 2;                          // centered in the right half

            // centered play area with a black surround
            host.Left = Margin - Pad;
            host.Top = hudTop + hudPanel.Height + Pad;
            host.Width = BoardW + 2 * Pad;
            host.Height = BoardH + 2 * Pad;
            board.Bounds = new Rectangle(Pad, Pad, BoardW, BoardH);

            // bottom HUD row: Area Cleared, centered, full width
            hudPercent.Left = 0;
            hudPercent.Top = host.Top + host.Height + Pad;
            hudPercent.Width = width;

            ClientSize = new Size(width, hudPercent.Top + hudPercent.Height + Margin / 2);

            BuildBackground();
            fillG?.Dispose();
            fillBitmap?.Dispose();
            fillBitmap = new Bitmap(BoardW, BoardH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            fillG = Graphics.FromImage(fillBitmap);
        }

        void ChooseBoardSize()
        {
            bool wasPlaying = state == GameState.Playing;
            if (wasPlaying) state = GameState.Paused;
            using (var dlg = new BoardSizeDialog(Cols, Rows, MinDim, MaxCols, MaxRows))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    Cols = dlg.ChosenCols;
                    Rows = dlg.ChosenRows;
                    ApplyBoardSize();
                    score = 0;
                    StartLevel(level);   // restart the current level on the new board
                    state = GameState.Playing;
                    menuPause.Text = "Pause";
                    return;
                }
            }
            if (wasPlaying) state = GameState.Playing;
        }

        void SetCursorHidden(bool hide)
        {
            if (hide == cursorHidden) return;
            cursorHidden = hide;
            if (hide) Cursor.Hide(); else Cursor.Show();
        }

        // --- helpers ---
        int Idx(int cx, int cy) => cy * Cols + cx;
        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        void SetSolid(int cx, int cy)
        {
            int i = Idx(cx, cy);
            if (grid[i] != 1)
            {
                grid[i] = 1;
                filledCount++;
                using (var b = new SolidBrush(SolidColor))
                    fillG.FillRectangle(b, cx * Cell, cy * Cell, Cell, Cell);
            }
        }

        void ClearBoard()
        {
            Array.Clear(grid, 0, grid.Length);
            filledCount = 0;
            fillG.Clear(Color.Transparent);
        }

        bool CircleHitsSolid(double x, double y, double r)
        {
            int minCx = (int)Math.Floor((x - r) / Cell);
            int maxCx = (int)Math.Floor((x + r) / Cell);
            int minCy = (int)Math.Floor((y - r) / Cell);
            int maxCy = (int)Math.Floor((y + r) / Cell);
            for (int cy = minCy; cy <= maxCy; cy++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    bool solid = (cx < 0 || cx >= Cols || cy < 0 || cy >= Rows) || grid[Idx(cx, cy)] == 1;
                    if (!solid) continue;
                    double nx = Clamp(x, cx * Cell, cx * Cell + Cell);
                    double ny = Clamp(y, cy * Cell, cy * Cell + Cell);
                    double dx = x - nx, dy = y - ny;
                    if (dx * dx + dy * dy < r * r) return true;
                }
            }
            return false;
        }

        static bool CircleRectHit(double cx, double cy, double r, Rectangle rect)
        {
            double nx = Clamp(cx, rect.X, rect.X + rect.Width);
            double ny = Clamp(cy, rect.Y, rect.Y + rect.Height);
            double dx = cx - nx, dy = cy - ny;
            return dx * dx + dy * dy < r * r;
        }

        // --- game flow ---
        void SpawnBalls(int n)
        {
            balls.Clear();
            int attempts = 0;
            while (balls.Count < n && attempts < 2000)
            {
                attempts++;
                int r = Cell / 2; // ball diameter matches a cell so it fits wall-width gaps
                double x = r + 20 + rng.NextDouble() * (BoardW - 2 * r - 40);
                double y = r + 20 + rng.NextDouble() * (BoardH - 2 * r - 40);
                bool ok = true;
                foreach (var b in balls)
                {
                    double dx = b.X - x, dy = b.Y - y;
                    if (Math.Sqrt(dx * dx + dy * dy) < r * 4) { ok = false; break; }
                }
                if (!ok) continue;
                // original balls travel on 45-degree diagonals only
                double speed = (BallBaseSpeed + Math.Min(level - 1, 12) * 0.045) * speedScale;
                balls.Add(new Ball
                {
                    X = x,
                    Y = y,
                    R = r,
                    Vx = (rng.Next(2) == 0 ? -1 : 1) * speed,
                    Vy = (rng.Next(2) == 0 ? -1 : 1) * speed,
                    Angle = rng.NextDouble() * Math.PI * 2,
                });
            }
        }

        void StartLevel(int n)
        {
            level = n;
            ClearBoard();
            int numBalls = level + 1; // level N starts with N+1 balls (and matching lives), as in the original
            SpawnBalls(numBalls);
            livesRemaining = numBalls;
            wallQueue.Clear();
            slotA = null;
            slotB = null;
            hoverPx = null;
            message = null;
            frameCount = 0;
            timeLeft = StartTime;
            scoreCounted = false;
            UpdateHud();
        }

        void NewGame()
        {
            score = 0;
            StartLevel(1);
            state = GameState.Playing;
            menuPause.Text = "Pause";
        }

        void ChooseLevel()
        {
            bool wasPlaying = state == GameState.Playing;
            if (wasPlaying) state = GameState.Paused;
            using (var dlg = new LevelPickerDialog(Math.Max(1, level)))
            {
                var result = dlg.ShowDialog(this);
                if (result == DialogResult.OK)
                {
                    score = 0;
                    StartLevel(dlg.ChosenLevel);
                    state = GameState.Playing;
                    menuPause.Text = "Pause";
                    return;
                }
            }
            if (wasPlaying) state = GameState.Playing;
        }

        void LevelComplete()
        {
            state = GameState.LevelComplete;
            score += 500 * level;
            UpdateHud();
        }

        void GameOver()
        {
            state = GameState.GameOver;
            wallQueue.Clear();
            slotA = null;
            slotB = null;
            if (scoreCounted) return;   // only run the end-of-game flow once
            scoreCounted = true;

            if (HighScores.Qualifies(score))
            {
                using (var dlg = new NameEntryDialog())
                    if (dlg.ShowDialog(this) == DialogResult.OK && dlg.EnteredName.Length > 0)
                    {
                        HighScores.Add(dlg.EnteredName, score);
                        using (var hs = new HighScoresDialog()) hs.ShowDialog(this);
                    }
            }

            if (MessageBox.Show(this, "Do you want to start a new game?", "Game Over",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                NewGame();
        }

        void TogglePause()
        {
            if (state == GameState.Playing) { state = GameState.Paused; menuPause.Text = "Resume"; }
            else if (state == GameState.Paused) { state = GameState.Playing; menuPause.Text = "Pause"; }
        }

        void ShowHelp()
        {
            bool wasPlaying = state == GameState.Playing;
            if (wasPlaying) state = GameState.Paused;
            MessageBox.Show(this,
                "Wall off 75% of the room without letting a ball touch an unfinished wall.\n\n" +
                "Clear enough of each room to advance. Every wall you start grows in\n" +
                "both directions; if a ball hits it before both ends anchor, you lose a life.",
                "How To Play");
            if (wasPlaying) state = GameState.Playing;
        }

        void ShowCommands()
        {
            bool wasPlaying = state == GameState.Playing;
            if (wasPlaying) state = GameState.Paused;
            MessageBox.Show(this,
                "Left-click:  build a wall in the current direction\n" +
                "Right-click: rotate the direction arrow (vertical / horizontal)\n" +
                "Space / F3:  pause or resume\n" +
                "F2:          new game",
                "Commands");
            if (wasPlaying) state = GameState.Playing;
        }

        void ShowAbout()
        {
            bool wasPlaying = state == GameState.Playing;
            if (wasPlaying) state = GameState.Paused;
            MessageBox.Show(this,
                "JezzBall\n\nA faithful remake of the Windows classic.\n" +
                "Trap the bouncing balls by walling off 75% of each room.",
                "About JezzBall");
            if (wasPlaying) state = GameState.Playing;
        }

        void ShowHighScores()
        {
            bool wasPlaying = state == GameState.Playing;
            if (wasPlaying) state = GameState.Paused;
            using (var dlg = new HighScoresDialog()) dlg.ShowDialog(this);
            if (wasPlaying) state = GameState.Playing;
        }

        // --- sound & speed ---
        void LoadSounds()
        {
            try
            {
                string dir = Path.Combine(AppContext.BaseDirectory, "sounds");
                sndBounce = SoundFx.Load(Path.Combine(dir, "BOUNCE.WAV"));
                sndDead = SoundFx.Load(Path.Combine(dir, "JEZZDEAD.WAV"), 3); // looped x3 to lengthen it
            }
            catch { sndBounce = null; sndDead = null; } // play silently if the WAVs are missing
        }

        void PlayBounce() { if (soundOn) sndBounce?.Play(); }
        void PlayDead() { if (soundOn) sndDead?.Play(); }

        void ToggleSound() { soundOn = !soundOn; soundItem.Checked = soundOn; }

        void SetSpeed(bool fast)
        {
            double newScale = fast ? 1.0 : 0.6;
            if (newScale != speedScale)
            {
                double ratio = newScale / speedScale;
                foreach (var b in balls) { b.Vx *= ratio; b.Vy *= ratio; } // rescale balls already in play
                speedScale = newScale;
            }
            fastItem.Checked = fast;
            slowItem.Checked = !fast;
        }

        void Flash(string text) { message = text; messageLife = 70; }

        void UpdateHud()
        {
            hudScore.Text = "Score: " + score;
            Text = "JezzBall — Level " + level; // original shows no Level readout; keep it in the title bar
            hudTime.Text = "Time Left: " + Math.Max(0, timeLeft);
            hudLives.Text = "Lives: " + livesRemaining;
            double pct = filledCount * 100.0 / TotalCells;
            hudPercent.Text = "Area Cleared: " + pct.ToString("0.0") + "%";
        }

        // --- walls ---
        void StartWall(bool vertical, int cx, int cy)
        {
            bool negFree = slotA == null;
            bool posFree = slotB == null;
            if (!negFree && !posFree) return; // both directions busy elsewhere; click fizzles
            var w = new Wall
            {
                Vertical = vertical,
                FixedCoord = vertical ? cx : cy,
                Origin = vertical ? cy : cx,
                RootCX = cx,
                RootCY = cy,
            };
            w.Neg.Tip = w.Origin; w.Neg.Skipped = !negFree;
            w.Pos.Tip = w.Origin; w.Pos.Skipped = !posFree;
            grid[Idx(cx, cy)] = 2;
            if (negFree) slotA = w;
            if (posFree) slotB = w;
            wallQueue.Add(w);
        }

        Rectangle ArmRect(Wall w, Arm arm, int sign)
        {
            int len = (int)Math.Round(arm.VisLen);
            if (w.Vertical)
            {
                if (sign < 0) return new Rectangle(w.FixedCoord * Cell, w.Origin * Cell + Cell - len, Cell, len);
                return new Rectangle(w.FixedCoord * Cell, w.Origin * Cell, Cell, len);
            }
            if (sign < 0) return new Rectangle(w.Origin * Cell + Cell - len, w.FixedCoord * Cell, len, Cell);
            return new Rectangle(w.Origin * Cell, w.FixedCoord * Cell, len, Cell);
        }

        void RevertArm(Arm arm)
        {
            foreach (var c in arm.Cells) grid[Idx(c.X, c.Y)] = 0;
            arm.Cells.Clear();
        }

        void SettleArm(Arm arm)
        {
            foreach (var c in arm.Cells) SetSolid(c.X, c.Y);
        }

        void GrowArm(Wall w, Arm arm, int sign)
        {
            if (arm.Terminal) return;
            arm.VisLen += (double)Cell / WallGrowInterval;
            while (!arm.Terminal && (int)Math.Floor(arm.VisLen / Cell) > arm.Cells.Count)
            {
                int nx, ny;
                if (w.Vertical) { nx = w.FixedCoord; ny = arm.Tip + sign; }
                else { ny = w.FixedCoord; nx = arm.Tip + sign; }
                bool outOfBounds = w.Vertical ? (ny < 0 || ny >= Rows) : (nx < 0 || nx >= Cols);
                // stop on any occupied cell: settled walls (1) AND other still-growing arms (2)
                if (outOfBounds || grid[Idx(nx, ny)] != 0)
                {
                    arm.Blocked = true;
                    arm.VisLen = (arm.Cells.Count + 1) * Cell;
                    SettleArm(arm);
                    break;
                }
                grid[Idx(nx, ny)] = 2;
                arm.Cells.Add(new Point(nx, ny));
                arm.Tip += sign;
            }
        }

        bool CheckArmHit(Wall w, Arm arm, int sign)
        {
            if (arm.Terminal) return false;
            var rect = ArmRect(w, arm, sign);
            foreach (var b in balls)
            {
                if (CircleRectHit(b.X, b.Y, b.R, rect))
                {
                    arm.Dead = true;
                    RevertArm(arm);
                    return true;
                }
            }
            return false;
        }

        void ResolveWallAttempt(Wall w)
        {
            bool anySettled = w.Neg.Blocked || w.Pos.Blocked;
            if (anySettled) SetSolid(w.RootCX, w.RootCY); // arms already went solid when they settled
            else grid[Idx(w.RootCX, w.RootCY)] = 0;
            int before = filledCount;
            RecalcCapture();
            int delta = filledCount - before;
            if (delta > 0) score += (int)Math.Round(delta * (3 + level * 1.2));
            UpdateHud();
            double pct = filledCount * 100.0 / TotalCells;
            if (pct >= WinPercent) LevelComplete();
        }

        void RecalcCapture()
        {
            var visited = new bool[TotalCells];
            var queue = new int[TotalCells];
            int qLen = 0;
            foreach (var b in balls)
            {
                int cx = (int)Clamp(Math.Floor(b.X / Cell), 0, Cols - 1);
                int cy = (int)Clamp(Math.Floor(b.Y / Cell), 0, Rows - 1);
                int i = Idx(cx, cy);
                if (grid[i] == 0 && !visited[i]) { visited[i] = true; queue[qLen++] = i; }
            }
            int qi = 0;
            while (qi < qLen)
            {
                int i = queue[qi++];
                int cx = i % Cols, cy = i / Cols;
                if (cx > 0 && !visited[i - 1] && grid[i - 1] == 0) { visited[i - 1] = true; queue[qLen++] = i - 1; }
                if (cx < Cols - 1 && !visited[i + 1] && grid[i + 1] == 0) { visited[i + 1] = true; queue[qLen++] = i + 1; }
                if (cy > 0 && !visited[i - Cols] && grid[i - Cols] == 0) { visited[i - Cols] = true; queue[qLen++] = i - Cols; }
                if (cy < Rows - 1 && !visited[i + Cols] && grid[i + Cols] == 0) { visited[i + Cols] = true; queue[qLen++] = i + Cols; }
            }
            for (int cy = 0; cy < Rows; cy++)
                for (int cx = 0; cx < Cols; cx++)
                {
                    int i = Idx(cx, cy);
                    if (grid[i] == 0 && !visited[i]) SetSolid(cx, cy);
                }
        }

        void UpdateWalls()
        {
            int hits = 0;
            if (slotA != null)
            {
                GrowArm(slotA, slotA.Neg, -1);
                if (CheckArmHit(slotA, slotA.Neg, -1)) hits++;
                if (slotA.Neg.Terminal) slotA = null;
            }
            if (slotB != null)
            {
                GrowArm(slotB, slotB.Pos, 1);
                if (CheckArmHit(slotB, slotB.Pos, 1)) hits++;
                if (slotB.Pos.Terminal) slotB = null;
            }
            if (hits > 0)
            {
                livesRemaining -= hits;
                PlayDead();
                Flash(hits > 1 ? "Walls Destroyed!" : "Wall Destroyed!");
                UpdateHud();
                if (livesRemaining < 0) { GameOver(); return; }
            }
            for (int i = wallQueue.Count - 1; i >= 0; i--)
            {
                var w = wallQueue[i];
                if (w.Neg.Terminal && w.Pos.Terminal)
                {
                    ResolveWallAttempt(w);
                    wallQueue.RemoveAt(i);
                }
            }
        }

        // --- balls ---
        void MoveBalls()
        {
            // axis-separated movement gives the original's clean 45-degree bounces
            foreach (var b in balls)
            {
                double nx = b.X + b.Vx;
                if (CircleHitsSolid(nx, b.Y, b.R))
                {
                    b.Vx = -b.Vx;
                    PlayBounce();
                    nx = b.X + b.Vx;
                    if (CircleHitsSolid(nx, b.Y, b.R)) nx = b.X; // pinched in a 1-cell gap
                }
                b.X = nx;
                double ny = b.Y + b.Vy;
                if (CircleHitsSolid(b.X, ny, b.R))
                {
                    b.Vy = -b.Vy;
                    PlayBounce();
                    ny = b.Y + b.Vy;
                    if (CircleHitsSolid(b.X, ny, b.R)) ny = b.Y;
                }
                b.Y = ny;
                b.Angle += Math.Abs(b.Vx) * 0.3;
            }
            ResolveBallCollisions();
        }

        void ResolveBallCollisions()
        {
            for (int i = 0; i < balls.Count; i++)
            {
                for (int j = i + 1; j < balls.Count; j++)
                {
                    var a = balls[i];
                    var b = balls[j];
                    double dx = b.X - a.X, dy = b.Y - a.Y;
                    double minDist = a.R + b.R;
                    double distSq = dx * dx + dy * dy;
                    if (distSq >= minDist * minDist) continue;
                    double dist = Math.Sqrt(distSq);
                    double nx = 1, ny = 0;
                    if (dist > 0.0001) { nx = dx / dist; ny = dy / dist; } else dist = 0.0001;
                    double overlap = minDist - dist;
                    // push apart, but never into a wall
                    double ax = a.X - nx * overlap / 2, ay = a.Y - ny * overlap / 2;
                    double bx = b.X + nx * overlap / 2, by = b.Y + ny * overlap / 2;
                    if (!CircleHitsSolid(ax, ay, a.R)) { a.X = ax; a.Y = ay; }
                    if (!CircleHitsSolid(bx, by, b.R)) { b.X = bx; b.Y = by; }
                    // swap a component only along axes where the balls approach each other;
                    // both keep moving on 45-degree diagonals afterwards
                    if ((b.Vx - a.Vx) * dx < 0) { double t = a.Vx; a.Vx = b.Vx; b.Vx = t; }
                    if ((b.Vy - a.Vy) * dy < 0) { double t = a.Vy; a.Vy = b.Vy; b.Vy = t; }
                }
            }
        }

        // --- loop ---
        void Loop(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            acc += Math.Min(now - lastMs, 100); // cap to avoid a spiral after a stall
            lastMs = now;
            while (acc >= StepMs)
            {
                acc -= StepMs;
                if (state == GameState.Playing)
                {
                    frameCount++;
                    if (frameCount % 60 == 0) { timeLeft = Math.Max(0, timeLeft - 1); UpdateHud(); }
                    MoveBalls();
                    if (wallQueue.Count > 0) UpdateWalls();
                }
                if (messageLife > 0 && --messageLife == 0) message = null;
            }
            SetCursorHidden(state == GameState.Playing && hoverPx.HasValue);
            board.Invalidate();
        }

        // --- input ---
        void Board_MouseDown(object sender, MouseEventArgs e)
        {
            if (state == GameState.Ready || state == GameState.GameOver)
            {
                if (e.Button == MouseButtons.Left) NewGame();
                return;
            }
            if (state == GameState.LevelComplete)
            {
                if (e.Button == MouseButtons.Left) { StartLevel(level + 1); state = GameState.Playing; }
                return;
            }
            if (state != GameState.Playing) return;
            if (e.Button == MouseButtons.Right)
            {
                buildVertical = !buildVertical;
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            int cx = e.X / Cell, cy = e.Y / Cell;
            if (cx < 0 || cx >= Cols || cy < 0 || cy >= Rows) return;
            if (grid[Idx(cx, cy)] != 0) return;
            StartWall(buildVertical, cx, cy);
        }

        // --- rendering ---
        void Board_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(bgBitmap, 0, 0);
            g.DrawImageUnscaled(fillBitmap, 0, 0);

            using (var negBrush = new SolidBrush(NegColor))
            using (var posBrush = new SolidBrush(PosColor))
            {
                foreach (var w in wallQueue)
                {
                    if (!w.Neg.Terminal)
                    {
                        var r = ArmRect(w, w.Neg, -1);
                        g.FillRectangle(negBrush, r);
                        DrawCap(g, r.X, r.Y, negBrush);
                    }
                    if (!w.Pos.Terminal)
                    {
                        var r = ArmRect(w, w.Pos, 1);
                        g.FillRectangle(posBrush, r);
                        if (w.Vertical) DrawCap(g, r.X, r.Y + r.Height - Cell, posBrush);
                        else DrawCap(g, r.X + r.Width - Cell, r.Y, posBrush);
                    }
                }
            }

            foreach (var b in balls) DrawBall(g, b);

            if (hoverPx.HasValue && state == GameState.Playing)
                DrawCursorArrow(g, hoverPx.Value.X, hoverPx.Value.Y, buildVertical);

            if (message != null && messageLife > 0)
                DrawToast(g, message, Math.Min(1.0, messageLife / 30.0));

            switch (state)
            {
                case GameState.Ready:
                    DrawOverlay(g, "JezzBall",
                        "Wall off 75% of the room without letting a ball\ntouch an unfinished wall.\n\n" +
                        "Left-click: build a wall    Right-click: rotate arrow\nSpace: pause\n\nClick to start");
                    break;
                case GameState.Paused:
                    DrawDim(g);
                    DrawCenteredText(g, "PAUSED", Color.White, 20);
                    break;
                case GameState.LevelComplete:
                    DrawOverlay(g, "Level " + level + " Complete!",
                        "Score: " + score + "\nNext up: Level " + (level + 1) + " with " + (level + 2) + " balls.\n\nClick for next level");
                    break;
                case GameState.GameOver:
                    DrawOverlay(g, "Game Over",
                        "You ran out of lives on Level " + level + ".\nFinal Score: " + score + "\n\nClick to play again");
                    break;
            }
        }

        void DrawBall(Graphics g, Ball b)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = new RectangleF((float)(b.X - b.R), (float)(b.Y - b.R), b.R * 2, b.R * 2);
            g.FillEllipse(Brushes.WhiteSmoke, rect);
            float deg = (float)(b.Angle * 180.0 / Math.PI);
            using (var red = new SolidBrush(Color.FromArgb(0xD8, 0x1E, 0x1E)))
                g.FillPie(red, rect.X, rect.Y, rect.Width, rect.Height, deg + 90f, 180f);
            using (var pen = new Pen(Color.FromArgb(0x40, 0x40, 0x40)))
                g.DrawEllipse(pen, rect);
            g.SmoothingMode = old;
        }

        // wall end cap: white-bordered colored block with four dots, like the original
        void DrawCap(Graphics g, int x, int y, Brush color)
        {
            g.FillRectangle(Brushes.White, x, y, Cell, Cell);
            g.FillRectangle(color, x + 1, y + 1, Cell - 2, Cell - 2);
            g.FillRectangle(Brushes.Black, x + 3, y + 3, 2, 2);
            g.FillRectangle(Brushes.Black, x + 7, y + 3, 2, 2);
            g.FillRectangle(Brushes.Black, x + 3, y + 7, 2, 2);
            g.FillRectangle(Brushes.Black, x + 7, y + 7, 2, 2);
        }

        void DrawCursorArrow(Graphics g, int px, int py, bool vertical)
        {
            const int len = 9, head = 4;
            var pts = new List<Point[]>();
            if (vertical)
            {
                pts.Add(new[] { new Point(px, py - len), new Point(px, py + len) });
                pts.Add(new[] { new Point(px - head, py - len + head), new Point(px, py - len), new Point(px + head, py - len + head) });
                pts.Add(new[] { new Point(px - head, py + len - head), new Point(px, py + len), new Point(px + head, py + len - head) });
            }
            else
            {
                pts.Add(new[] { new Point(px - len, py), new Point(px + len, py) });
                pts.Add(new[] { new Point(px - len + head, py - head), new Point(px - len, py), new Point(px - len + head, py + head) });
                pts.Add(new[] { new Point(px + len - head, py - head), new Point(px + len, py), new Point(px + len - head, py + head) });
            }
            // black halo under white stroke so it reads on both gray and black
            using (var halo = new Pen(Color.Black, 4f))
            using (var core = new Pen(Color.White, 2f))
            {
                foreach (var line in pts) g.DrawLines(halo, line);
                foreach (var line in pts) g.DrawLines(core, line);
            }
        }

        void DrawToast(Graphics g, string text, double alpha)
        {
            using (var font = new Font("Tahoma", 14f, FontStyle.Bold))
            {
                var size = g.MeasureString(text, font);
                float x = (BoardW - size.Width) / 2f, y = (BoardH - size.Height) / 2f;
                using (var outline = new SolidBrush(Color.FromArgb((int)(alpha * 255), Color.White)))
                {
                    g.DrawString(text, font, outline, x - 1, y);
                    g.DrawString(text, font, outline, x + 1, y);
                    g.DrawString(text, font, outline, x, y - 1);
                    g.DrawString(text, font, outline, x, y + 1);
                }
                using (var fill = new SolidBrush(Color.FromArgb((int)(alpha * 255), 0xB0, 0x00, 0x00)))
                    g.DrawString(text, font, fill, x, y);
            }
        }

        void DrawDim(Graphics g)
        {
            using (var dim = new SolidBrush(Color.FromArgb(90, Color.Black)))
                g.FillRectangle(dim, 0, 0, BoardW, BoardH);
        }

        void DrawCenteredText(Graphics g, string text, Color color, float fontSize, float yOffset = 0)
        {
            using (var font = new Font("Tahoma", fontSize, FontStyle.Bold))
            using (var brush = new SolidBrush(color))
            {
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, brush, (BoardW - size.Width) / 2f, (BoardH - size.Height) / 2f + yOffset);
            }
        }

        void DrawOverlay(Graphics g, string title, string body)
        {
            DrawDim(g);
            using (var titleFont = new Font("Tahoma", 15f, FontStyle.Bold))
            using (var bodyFont = new Font("Tahoma", 9.5f))
            {
                var titleSize = g.MeasureString(title, titleFont);
                var bodySize = g.MeasureString(body, bodyFont);
                float boxW = Math.Max(titleSize.Width, bodySize.Width) + 48;
                float boxH = titleSize.Height + bodySize.Height + 44;
                float bx = (BoardW - boxW) / 2f, by = (BoardH - boxH) / 2f;
                using (var face = new SolidBrush(Color.FromArgb(0xC0, 0xC0, 0xC0)))
                    g.FillRectangle(face, bx, by, boxW, boxH);
                using (var light = new Pen(Color.White, 2f))
                using (var dark = new Pen(Color.FromArgb(0x40, 0x40, 0x40), 2f))
                {
                    g.DrawLine(light, bx, by, bx + boxW, by);
                    g.DrawLine(light, bx, by, bx, by + boxH);
                    g.DrawLine(dark, bx, by + boxH, bx + boxW, by + boxH);
                    g.DrawLine(dark, bx + boxW, by, bx + boxW, by + boxH);
                }
                using (var navy = new SolidBrush(Color.FromArgb(0x00, 0x00, 0x80)))
                    g.DrawString(title, titleFont, navy, bx + (boxW - titleSize.Width) / 2f, by + 14);
                var fmt = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(body, bodyFont, Brushes.Black,
                    new RectangleF(bx, by + titleSize.Height + 24, boxW, bodySize.Height + 6), fmt);
            }
        }
    }
}
