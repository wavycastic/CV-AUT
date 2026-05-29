using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;

namespace CvAut
{
    public sealed class BotControlForm : Form
    {
        private const string ConfigPath = "CV-AUT-PY/test_config.json";
        private const int SidebarWidth = 160;
        private const int ContentPadding = 10;
        private const int SectionWidth = 330;

        private static readonly Color AppBack = Color.FromArgb(18, 18, 18);
        private static readonly Color SidebarBack = Color.FromArgb(18, 18, 18);
        private static readonly Color Surface = Color.FromArgb(28, 28, 28);
        private static readonly Color SurfaceSunken = Color.FromArgb(12, 12, 12);
        private static readonly Color DisabledSurface = Color.FromArgb(24, 24, 24);
        private static readonly Color DisabledBorder = Color.FromArgb(32, 32, 32);
        private static readonly Color Border = Color.FromArgb(38, 38, 38);
        private static readonly Color BorderStrong = Color.FromArgb(52, 52, 52);
        private static readonly Color TextMain = Color.FromArgb(240, 240, 240);
        private static readonly Color TextBody = Color.FromArgb(170, 170, 170);
        private static readonly Color TextMuted = Color.FromArgb(110, 110, 110);
        private static readonly Color PrimaryOrange = Color.FromArgb(245, 78, 0);
        private static readonly Color PrimaryOrangeActive = Color.FromArgb(208, 66, 0);
        private static readonly Color Accent = Color.FromArgb(245, 78, 0);
        private static readonly Color Danger = Color.FromArgb(223, 75, 106);
        private static readonly Color CocCream = Color.FromArgb(239, 226, 186);


        private string HeadlineFont = "Segoe UI";
        private string BodyFont = "Segoe UI";
        private const string MonoFont = "JetBrains Mono";

        private enum ButtonKind
        {
            Primary,
            Secondary,
            Destructive
        }

        private readonly SimplicityNavListBox _nav = new();
        private readonly Panel _stack = new();
        private readonly Dictionary<string, Panel> _pages = new();
        private readonly TextBox _logBox = new();
        private readonly Label _pageTitle = new();
        private readonly RawStatusChip _statusLabel = new();

        private readonly TextBox _goldThresholdInput = new();
        private readonly TextBox _elixirThresholdInput = new();
        private readonly TextBox _darkThresholdInput = new();
        private readonly RawCheckBox _upgradeWallCheck = new();
        private readonly TextBox _wallGoldInput = new();
        private readonly TextBox _wallElixirInput = new();
        private readonly NumericUpDown _wallLevelInput = new();
        private readonly RawCheckBox _requestTroopsCheck = new();

        private readonly ComboBox _attackCombo = new();
        private readonly Label _attackPreview = new();
        private readonly Label _attackDescription = new();
        private readonly RadioButton _smartTrainRadio = new();
        private readonly RadioButton _quickTrainRadio = new();
        private readonly NumericUpDown _quickSlotInput = new();

        private readonly RawCheckBox _multiAccountCheck = new();
        private readonly NumericUpDown _accountCountInput = new();
        private readonly ComboBox _intervalCombo = new();
        private readonly List<RawCheckBox> _villageChecks = new();
        private readonly List<Button> _loadVillageButtons = new();
        private readonly List<Button> _saveVillageButtons = new();

        private readonly RawCheckBox _clanGamesCheck = new();
        private readonly RawCheckBox _clanCapitalCheck = new();
        private readonly NumericUpDown _capitalHallInput = new();
        private readonly Label _capitalNoteLabel = new();
        private readonly Label _capitalLevelLabel = new();

        private readonly RawCheckBox _enableStatsCheck = new();
        private readonly Label _statsGoldLabel = new();
        private readonly Label _statsElixirLabel = new();
        private readonly Label _statsDarkLabel = new();
        private readonly Label _statsAttacksLabel = new();
        private readonly Label _statsAvgGoldLabel = new();
        private readonly Label _statsAvgElixirLabel = new();
        private readonly Label _statsAvgDarkLabel = new();
        private readonly Label[] _statsStarLabels = { new(), new(), new(), new() };

        private readonly TextBox _adbHostInput = new();
        private readonly NumericUpDown _adbPortInput = new();



        private readonly Button _startButton = new();
        private readonly Button _stopButton = new();
        private readonly Button _pauseToggleButton = new();
        private readonly System.Windows.Forms.Timer _statsTimer = new();

        private CVAutomationFramework? _framework;
        private TextWriter? _originalOut;
        private UiLogTextWriter? _uiWriter;
        private bool _loadingProfile;
        private bool _paused;
        private int _currentVillage = 1;
        private Image? _woodBg;
        private Bitmap? _cachedBg;
        private Image? _startIcon;
        private Image? _stopIcon;
        private Image? _pauseIcon;

        public BotControlForm()
        {
            Text = "CV-AUT Bot Control v1.0.0";
            ClientSize = new Size(530, 680);
            MinimumSize = new Size(530, 680);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font(BodyFont, 9F, FontStyle.Regular);
            DoubleBuffered = true;

            string bgPath = ResolveTemplatePath("BG.png");
            if (File.Exists(bgPath))
            {
                try { _woodBg = Image.FromFile(bgPath); }
                catch (Exception ex) { Console.WriteLine($"[UI] Không load được BG: {ex.Message}"); }
            }

            string iconPath = ResolveTemplatePath("app_icon.ico");
            if (File.Exists(iconPath))
            {
                Icon = new Icon(iconPath);
            }

            _startIcon = LoadImage("start.png", 14, 14);
            _stopIcon = LoadImage("end.png", 14, 14);
            _pauseIcon = LoadImage("stop_button.png", 14, 14);

            ApplyBackground();
            BuildLayout();
            LoadMainConfig();
            LoadSelectedProfile();
            SelectPage("General");
            SetRunningState(false);

            this.ClientSizeChanged += (s, e) => { UpdateCachedBackground(); Invalidate(true); };

            _statsTimer.Interval = 5000;
            _statsTimer.Tick += (_, _) => RefreshStatsFromJson();
            _statsTimer.Start();
            RefreshStatsFromJson();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _statsTimer.Stop();
            StopBot();
            _cachedBg?.Dispose();
            _woodBg?.Dispose();
            _startIcon?.Dispose();
            _stopIcon?.Dispose();
            _pauseIcon?.Dispose();
            base.OnFormClosing(e);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        private void ApplyBackground()
        {
            BackgroundImage = null;
            BackColor = AppBack;
            UpdateCachedBackground();
        }

        private void UpdateCachedBackground()
        {
            if (_woodBg == null) return;

            int width = ClientSize.Width;
            int height = ClientSize.Height;
            if (width <= 0 || height <= 0) return;

            _cachedBg?.Dispose();
            _cachedBg = new Bitmap(width, height);
            using (var g = Graphics.FromImage(_cachedBg))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(_woodBg, 0, 0, width, height);

                // Bake the 100 alpha dark overlay!
                using (var overlay = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                {
                    g.FillRectangle(overlay, 0, 0, width, height);
                }
            }
        }

        private void BuildLayout()
        {
            var root = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = Padding.Empty };
            Controls.Add(root);

            root.Paint += (s, e) =>
            {
                if (_cachedBg != null)
                {
                    e.Graphics.DrawImageUnscaled(_cachedBg, 0, 0);
                }
                else
                {
                    using (var brush = new SolidBrush(AppBack))
                    {
                        e.Graphics.FillRectangle(brush, root.ClientRectangle);
                    }
                }
            };

            var navPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(SidebarWidth, ClientSize.Height),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 8, 0, 8)
            };
            navPanel.Paint += (s, e) =>
            {
                // Overlay with semi-transparent black (180 alpha) - matches Simplicity's rgba(0,0,0,180)
                using (var overlay = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                {
                    e.Graphics.FillRectangle(overlay, navPanel.ClientRectangle);
                }
            };
            root.Controls.Add(navPanel);

            _nav.ForeColor = CocCream;
            _nav.Font = new Font("Segoe UI", 12.5F, FontStyle.Bold);
            _nav.ItemHeight = 44;
            _nav.Dock = DockStyle.Top;
            _nav.Height = 320;
            _nav.Items.AddRange(new object[] { "General", "Army", "Multi-Vill...", "Clan Games", "Clan Capital", "Statistics", "Logs" });
            _nav.SelectedIndexChanged += (_, _) =>
            {
                if (_nav.SelectedItem is string page)
                {
                    string realPage = page == "Multi-Vill..." ? "Multi-Village" : page;
                    SelectPage(realPage);
                }
            };
            navPanel.Controls.Add(_nav);

            _statusLabel.Location = new Point(25, 550);
            _statusLabel.Anchor = AnchorStyles.Bottom;
            navPanel.Controls.Add(_statusLabel);

            // Pause toggle button at bottom of nav sidebar (like Simplicity)
            StyleImageButton(_pauseToggleButton, "pauseBtn", "stop_button.png", 80, 80);
            _pauseToggleButton.Location = new Point(40, 594);
            _pauseToggleButton.Anchor = AnchorStyles.Bottom;
            navPanel.Controls.Add(_pauseToggleButton);
            _pauseToggleButton.Click += (_, _) => TogglePause();

            var right = new RawContentPanel
            {
                Location = new Point(SidebarWidth, 0),
                Size = new Size(ClientSize.Width - SidebarWidth, ClientSize.Height - 100),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent,
                Padding = new Padding(ContentPadding)
            };
            root.Controls.Add(right);

            _stack.Dock = DockStyle.Fill;
            _stack.BackColor = Color.Transparent;
            _stack.SizeChanged += (s, e) =>
            {
                foreach (var page in _pages.Values)
                {
                    page.Size = _stack.ClientSize;
                }
            };
            right.Controls.Add(_stack);
            _stack.BringToFront();

            // Bottom bar: Start + End (like Simplicity)
            var bottom = new Panel
            {
                Location = new Point(SidebarWidth, ClientSize.Height - 100),
                Size = new Size(ClientSize.Width - SidebarWidth, 100),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            root.Controls.Add(bottom);
            bottom.BringToFront();

            // Start button (image-based, left half) — Simplicity uses 180×90, resized to 170 to avoid overlap
            StyleImageButton(_startButton, "startBtn", "start.png", 170, 90);
            _startButton.Location = new Point(10, 5);
            bottom.Controls.Add(_startButton);
            _startButton.Click += (_, _) => StartBot();

            // End/Stop button (image-based, right half) — Simplicity uses 180×90, resized to 170 to avoid overlap
            StyleImageButton(_stopButton, "endBtn", "end.png", 170, 90);
            _stopButton.Location = new Point(190, 5);
            bottom.Controls.Add(_stopButton);
            _stopButton.Click += (_, _) => StopBot();

            BuildGeneralPage();
            BuildArmyPage();
            BuildMultiVillagePage();
            BuildClanGamesPage();
            BuildClanCapitalPage();
            BuildStatisticsPage();
            BuildLogsPage();
            _nav.SelectedIndex = 0;
        }

        private void BuildGeneralPage()
        {
            FlowLayoutPanel layout = CreatePageLayout("General");

            // Decorative Attack Criteria header (3 images like Simplicity)
            var headerRow = new FlowLayoutPanel
            {
                Width = SectionWidth,
                Height = 70,
                WrapContents = false,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 4, 0, 4),
                Padding = new Padding(12, 0, 0, 0)
            };
            headerRow.Controls.Add(CreateImageLabel("icon_siege.png", 50, new Padding(0, 0, 0, 0), 0.7f));
            var centerImg = CreateImageLabel("icon_attack_criteria.png", 180, new Padding(8, 0, 8, 0), 1f);
            headerRow.Controls.Add(centerImg);
            headerRow.Controls.Add(CreateImageLabel("icon_dragon.png", 50, new Padding(0, 0, 0, 0), 0.7f));
            layout.Controls.Add(headerRow);
            layout.Controls.Add(SeparatorControl());

            TableLayoutPanel farming = CreateSection("Farming thresholds", "");
            AddInputRow(farming, "Gold:", _goldThresholdInput, "icon_gold.png");
            AddInputRow(farming, "Elixir:", _elixirThresholdInput, "icon_elixir.png");
            AddInputRow(farming, "Dark Elixir:", _darkThresholdInput, "icon_de.png");
            layout.Controls.Add(farming.Parent!);
            layout.Controls.Add(SeparatorControl());

            TableLayoutPanel walls = CreateSection("Wall upgrades", "Optional spending rules after farming.");
            _upgradeWallCheck.Text = "Enable Upgrade Wall";
            StyleCheck(_upgradeWallCheck, 10.5f);
            AddFullWidth(walls, IconCheckRow("icon_up.png", _upgradeWallCheck, 20));
            _wallLevelInput.Minimum = 8;
            _wallLevelInput.Maximum = 18;
            AddInputRow(walls, "Gold Threshold:", _wallGoldInput, "icon_gold.png");
            AddInputRow(walls, "Elixir Threshold:", _wallElixirInput, "icon_elixir.png");
            AddInputRow(walls, "Target Wall Level:", _wallLevelInput, "icon_wall.png");
            layout.Controls.Add(walls.Parent!);
            layout.Controls.Add(SeparatorControl());

            TableLayoutPanel device = CreateSection("Device and clan", "Connection and request settings for the active village.");
            _requestTroopsCheck.Text = "Enable Request Troops";
            StyleCheck(_requestTroopsCheck, 10.5f);
            AddFullWidth(device, IconCheckRow("icon_clan_castle.png", _requestTroopsCheck, 24));
            _adbPortInput.Minimum = 1;
            _adbPortInput.Maximum = 65535;
            AddInputRow(device, "ADB Host:", _adbHostInput, "icon_siege.png");
            AddInputRow(device, "ADB Port:", _adbPortInput, "icon_siege.png");
            layout.Controls.Add(device.Parent!);

            _upgradeWallCheck.CheckedChanged += (_, _) => ToggleWallInputs();
        }

        private void BuildArmyPage()
        {
            FlowLayoutPanel layout = CreatePageLayout("Army");
            TableLayoutPanel attack = CreateSection("Attack setup", "Choose attack strategy and training mode.");

            _attackPreview.Height = 52;
            _attackPreview.Width = SectionWidth - 48;
            _attackPreview.TextAlign = ContentAlignment.MiddleLeft;
            _attackPreview.ForeColor = TextMain;
            _attackPreview.Font = new Font(HeadlineFont, 22F, FontStyle.Regular);
            AddFullWidth(attack, _attackPreview);
            _attackCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _attackCombo.Items.AddRange(new object[] { "Dragon", "Electro Dragon" });
            _attackCombo.SelectedIndexChanged += (_, _) => UpdateAttackPreview();
            AddInputRow(attack, "Attack:", _attackCombo, "icon_attack_criteria.png");

            _smartTrainRadio.Text = "Smart";
            _quickTrainRadio.Text = "Quick";
            StyleTrainRadio(_smartTrainRadio);
            StyleTrainRadio(_quickTrainRadio);
            _smartTrainRadio.CheckedChanged += (_, _) => UpdateQuickSlotState();
            _quickTrainRadio.CheckedChanged += (_, _) => UpdateQuickSlotState();

            var modes = new FlowLayoutPanel { Width = 136, Height = 40, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
            modes.Controls.Add(_smartTrainRadio);
            modes.Controls.Add(_quickTrainRadio);
            AddInputRow(attack, "Train Mode:", modes, "icon_dragon.png");

            _quickSlotInput.Minimum = 1;
            _quickSlotInput.Maximum = 2;
            AddInputRow(attack, "Slot:", _quickSlotInput, "icon_hourglass.png");

            _attackDescription.Width = SectionWidth - 48;
            _attackDescription.Height = 34;
            _attackDescription.ForeColor = TextMuted;
            _attackDescription.Font = new Font(MonoFont, 10F, FontStyle.Regular);
            AddFullWidth(attack, _attackDescription);

            // Troop decorators row (representing COC army styles in transparent overlay)
            var troopRow = new FlowLayoutPanel
            {
                Width = SectionWidth - 48,
                Height = 60,
                WrapContents = false,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 10, 0, 0),
                Padding = new Padding(12, 0, 0, 0)
            };
            troopRow.Controls.Add(CreateImageLabel("icon_pekka.png", 60, new Padding(0, 0, 24, 0), 0.8f));
            troopRow.Controls.Add(CreateImageLabel("icon_rr.png", 50, new Padding(0, 0, 24, 0), 0.8f));
            troopRow.Controls.Add(CreateImageLabel("icon_golem.png", 60, new Padding(0, 0, 0, 0), 0.8f));
            AddFullWidth(attack, troopRow);

            layout.Controls.Add(attack.Parent!);
        }

        private void BuildMultiVillagePage()
        {
            FlowLayoutPanel layout = CreatePageLayout("Multi-Village");
            TableLayoutPanel settings = CreateSection("Multi-village rotation", "Enable and choose which accounts are part of the farming loop.");

            _multiAccountCheck.Text = "Enable Multi-Village";
            StyleCheck(_multiAccountCheck, 10.5f);
            AddFullWidth(settings, IconCheckRow("icon_switch.png", _multiAccountCheck, 24));

            _accountCountInput.Minimum = 2;
            _accountCountInput.Maximum = 5;
            AddInputRow(settings, "How many accounts?", _accountCountInput, "icon_switch.png");

            _intervalCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _intervalCombo.Items.AddRange(new object[] { "30 minutes", "1 hour", "1.5 hours", "2 hours" });
            AddInputRow(settings, "Switch interval:", _intervalCombo, "icon_hourglass.png");

            layout.Controls.Add(settings.Parent!);

            TableLayoutPanel villages = CreateSection("Villages", "Load or save profile data for each account.");

            for (int i = 1; i <= 5; i++)
            {
                var row = new FlowLayoutPanel { Width = SectionWidth - 48, Height = 42, WrapContents = false, BackColor = Color.Transparent };
                var cb = new RawCheckBox { Text = $"Village {i}", Width = 104 };
                StyleCheck(cb, 9.5f);
                cb.Width = 118;
                var load = new Button { Text = "Load", Width = 70, Height = 30 };
                var save = new Button { Text = "Save", Width = 70, Height = 30 };
                int village = i;
                load.Click += (_, _) => ConfirmAndLoadVillage(village);
                save.Click += (_, _) => SaveVillage(village);
                StyleSolidButton(load, ButtonKind.Secondary);
                StyleSolidButton(save, ButtonKind.Primary);
                row.Controls.Add(cb);
                row.Controls.Add(load);
                row.Controls.Add(save);
                AddFullWidth(villages, row);
                _villageChecks.Add(cb);
                _loadVillageButtons.Add(load);
                _saveVillageButtons.Add(save);
            }
            layout.Controls.Add(villages.Parent!);

            _multiAccountCheck.CheckedChanged += (_, _) => ApplyMultiVillageState();
            _accountCountInput.ValueChanged += (_, _) => ApplyMultiVillageState();
        }

        private void BuildClanGamesPage()
        {
            FlowLayoutPanel layout = CreatePageLayout("Clan Games");
            TableLayoutPanel section = CreateSection("Clan Games", "Optional event automation for the active village.");

            _clanGamesCheck.Text = "Enable Clan Games";
            StyleCheck(_clanGamesCheck, 10.5f);
            AddFullWidth(section, IconCheckRow("icon_games.png", _clanGamesCheck, 32));

            // Render large Clan Games illustration
            var imgGames = CreateImageLabel("clan_games.png", SectionWidth - 48, new Padding(12, 12, 0, 0), 1f);
            AddFullWidth(section, imgGames);

            layout.Controls.Add(section.Parent!);
        }

        private void BuildClanCapitalPage()
        {
            FlowLayoutPanel layout = CreatePageLayout("Clan Capital");
            TableLayoutPanel section = CreateSection("Clan Capital", "Control weekly capital raid automation.");

            _clanCapitalCheck.Text = "Enable Clan Capital";
            StyleCheck(_clanCapitalCheck, 10.5f);
            AddFullWidth(section, IconCheckRow("clan_capital_logo.png", _clanCapitalCheck, 32));

            _capitalNoteLabel.Text = "Note: If you enable Clan Capital, please use the default scenery so Simplicity can detect the Clan Capital boat more reliably.";
            _capitalNoteLabel.Width = SectionWidth - 48;
            _capitalNoteLabel.Height = 64;
            _capitalNoteLabel.ForeColor = TextMuted;
            _capitalNoteLabel.Font = new Font(MonoFont, 9.5F, FontStyle.Regular);
            AddFullWidth(section, _capitalNoteLabel);

            _capitalHallInput.Minimum = 1;
            _capitalHallInput.Maximum = 10;
            AddInputRow(section, "Capital Hall level:", _capitalHallInput, "icon_up.png");
            layout.Controls.Add(section.Parent!);

            _clanCapitalCheck.CheckedChanged += (_, _) => ApplyClanCapitalState();
        }



        private void BuildStatisticsPage()
        {
            FlowLayoutPanel layout = CreatePageLayout("Statistics");

            _enableStatsCheck.Text = "Enable Statistics";
            StyleCheck(_enableStatsCheck, 10.5f);
            TableLayoutPanel settings = CreateSection("Statistics", "Read current totals from the active village profile.");
            AddFullWidth(settings, _enableStatsCheck);
            layout.Controls.Add(settings.Parent!);

            var total = new SimplicityGroupBox
            {
                Text = "Total Resources Gain",
                Width = SectionWidth,
                Height = 300,
                ForeColor = TextMain,
                BackColor = Color.Transparent,
                Font = new Font(HeadlineFont, 11F, FontStyle.Regular)
            };
            layout.Controls.Add(total);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 34, 18, 14), BackColor = Color.Transparent };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            total.Controls.Add(grid);
            AddStatsRow(grid, "Gold", _statsGoldLabel, "icon_gold.png");
            AddStatsRow(grid, "Elixir", _statsElixirLabel, "icon_elixir.png");
            AddStatsRow(grid, "Dark Elixir", _statsDarkLabel, "icon_de.png");
            AddStatsRow(grid, "Total Attacks:", _statsAttacksLabel, "icon_attack_criteria.png");
            AddStatsSeparator(grid);
            AddStatsRow(grid, "Avg Gold/hr:", _statsAvgGoldLabel, "icon_gold.png");
            AddStatsRow(grid, "Avg Elixir/hr:", _statsAvgElixirLabel, "icon_elixir.png");
            AddStatsRow(grid, "Avg DE/hr:", _statsAvgDarkLabel, "icon_de.png");

            var results = new SimplicityGroupBox
            {
                Text = "Attack Results",
                Width = SectionWidth,
                Height = 190,
                ForeColor = TextMain,
                BackColor = Color.Transparent,
                Font = new Font(HeadlineFont, 11F, FontStyle.Regular)
            };
            layout.Controls.Add(results);
            var stars = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 30, 18, 12), BackColor = Color.Transparent };
            stars.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            stars.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            results.Controls.Add(stars);
            for (int i = 0; i <= 3; i++)
            {
                AddStarStatsRow(stars, i, _statsStarLabels[i]);
            }
        }

        private void BuildLogsPage()
        {
            Panel page = CreatePage("Logs");
            page.Dock = DockStyle.Fill; // Force the logs page to fill the entire right container
            page.Padding = new Padding(12, 12, 16, 12);

            // Create a gorgeous rounded terminal container (Terminal Shell)
            var terminalShell = new CardPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(12, 12, 14), // Deep midnight black/charcoal
                Padding = new Padding(12)
            };
            page.Controls.Add(terminalShell);

            _logBox.Dock = DockStyle.Fill;
            _logBox.Multiline = true;
            _logBox.ScrollBars = ScrollBars.None; // Hide the ugly Windows standard scrollbar completely
            _logBox.ReadOnly = true;
            _logBox.BackColor = Color.FromArgb(12, 12, 14);
            _logBox.ForeColor = CocCream; // Matching premium Clash of Clans cream color
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.Font = new Font("Consolas", 10F, FontStyle.Regular);

            terminalShell.Controls.Add(_logBox);
        }

        private Panel CreatePage(string name)
        {
            var page = new Panel { Dock = DockStyle.Fill, Location = Point.Empty, Size = _stack.ClientSize, BackColor = Color.Transparent, Visible = false };
            _pages[name] = page;
            _stack.Controls.Add(page);
            return page;
        }

        private FlowLayoutPanel CreatePageLayout(string name)
        {
            Panel page = CreatePage(name);
            page.Padding = Padding.Empty;

            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                HorizontalScroll = { Enabled = false, Visible = false },
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 4, 0)
            };
            page.Controls.Add(layout);
            return layout;
        }

        private Panel CreateCardShell(string title, string subtitle)
        {
            var shell = new CardPanel
            {
                Width = SectionWidth,
                BackColor = Surface,
                Padding = new Padding(24),
                Margin = new Padding(0, 0, 0, 24)
            };

            var titleLabel = new Label
            {
                Dock = DockStyle.None,
                Location = new Point(24, 24),
                Width = SectionWidth - 48,
                Height = 28,
                Text = title.ToUpperInvariant(),
                ForeColor = TextMain,
                Font = new Font(HeadlineFont, 14F, FontStyle.Regular),
                BackColor = Color.Transparent
            };
            var subtitleLabel = new Label
            {
                Dock = DockStyle.None,
                Location = new Point(24, 56),
                Width = SectionWidth - 48,
                Height = 28,
                Text = subtitle,
                ForeColor = TextMuted,
                Font = new Font(MonoFont, 9F, FontStyle.Regular),
                BackColor = Color.Transparent
            };
            shell.Controls.Add(subtitleLabel);
            shell.Controls.Add(titleLabel);
            return shell;
        }

        private TableLayoutPanel CreateSection(string title, string subtitle)
        {
            var frame = new TableLayoutPanel
            {
                Width = SectionWidth,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = Color.Transparent,
                Padding = new Padding(12, 4, 12, 4),
                Margin = new Padding(0, 0, 0, 8)
            };

            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var grid = new TableLayoutPanel
            {
                Width = SectionWidth - 24,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            frame.Controls.Add(grid, 0, frame.RowCount++);
            return grid;
        }

        private void AddInputRow(TableLayoutPanel grid, string label, Control input, string? icon = null)
        {
            ConfigureInput(input);
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, input is FlowLayoutPanel ? 46 : 36));

            var lblRow = IconLabelRow(icon, label);
            lblRow.Dock = DockStyle.Fill;
            lblRow.Margin = new Padding(0, 2, 0, 2);
            grid.Controls.Add(lblRow, 0, row);

            if (input is TextBox || input is NumericUpDown || input is ComboBox)
            {
                if (input is TextBox tb) tb.BorderStyle = BorderStyle.None;
                else if (input is NumericUpDown nud) nud.BorderStyle = BorderStyle.None;

                var container = new RawInputContainer(input)
                {
                    Width = 126,
                    Height = 26,
                    Anchor = AnchorStyles.Right,
                    Margin = new Padding(0, 5, 0, 5)
                };
                grid.Controls.Add(container, 1, row);
            }
            else
            {
                bool flowInput = input is FlowLayoutPanel;
                input.Width = flowInput ? 136 : 126;
                input.Height = flowInput ? 40 : 24;
                input.Anchor = AnchorStyles.Right;
                input.Margin = new Padding(0, 4, 0, 4);
                grid.Controls.Add(input, 1, row);
            }
        }

        private void AddFullWidth(TableLayoutPanel grid, Control control)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(42, control.Height + 8)));
            control.Margin = new Padding(0, 4, 0, 4);
            grid.Controls.Add(control, 0, row);
            grid.SetColumnSpan(control, 2);
        }

        private void ConfigureInput(Control input)
        {
            if (input is FlowLayoutPanel)
            {
                input.Width = 126;
                input.Margin = new Padding(0, 2, 0, 2);
                return;
            }

            input.Width = 126;
            input.Height = 24;
            input.Margin = Padding.Empty;

            if (input is TextBox textBox)
            {
                StyleTextBox(textBox);
            }
            else if (input is NumericUpDown numeric)
            {
                StyleNumeric(numeric);
            }
            else if (input is ComboBox combo)
            {
                StyleCombo(combo);
            }
        }

        private TableLayoutPanel CreateGrid(Control parent, int columns)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = columns,
                BackColor = Color.Transparent
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            parent.Controls.Add(grid);
            return grid;
        }

        private void SelectPage(string pageName)
        {
            foreach (var pair in _pages)
            {
                if (pair.Key == pageName)
                {
                    var page = pair.Value;
                    page.Top = 0;
                    page.Visible = true;
                    page.BringToFront();
                }
                else
                {
                    pair.Value.Visible = false;
                }
            }
        }

        private void ReapplyAllFonts()
        {
            HeadlineFont = "Segoe UI";
            BodyFont = "Segoe UI";


            _startButton.Font = new Font(HeadlineFont, 9.5F, FontStyle.Regular);
            _stopButton.Font = new Font(HeadlineFont, 9.5F, FontStyle.Regular);
            _pauseToggleButton.Font = new Font(HeadlineFont, 9.5F, FontStyle.Regular);

            UpdateControlFonts(this);
            this.Invalidate(true);
        }

        private void UpdateControlFonts(Control parent)
        {
            foreach (Control ctrl in parent.Controls)
            {
                if (ctrl is Label lbl)
                {
                    if (lbl.Font.Name == "JetBrains Mono" || lbl.Font.Name == "Consolas")
                    {
                        // Keep mono
                    }
                    else
                    {
                        float currentSize = lbl.Font.Size;
                        FontStyle style = lbl.Font.Style;
                        lbl.Font = new Font(currentSize >= 13F || style == FontStyle.Bold ? HeadlineFont : BodyFont, currentSize, style);
                    }
                }
                else if (ctrl is CheckBox cb)
                {
                    float currentSize = cb.Font.Size;
                    cb.Font = new Font(BodyFont, currentSize, FontStyle.Bold);
                }
                else if (ctrl is RadioButton rb)
                {
                    float currentSize = rb.Font.Size;
                    rb.Font = new Font(HeadlineFont, currentSize, FontStyle.Bold);
                }
                else if (ctrl is Button btn)
                {
                    if (btn == _startButton || btn == _stopButton || btn == _pauseToggleButton)
                    {
                        btn.Font = new Font(HeadlineFont, 9.5F, FontStyle.Regular);
                    }
                    else
                    {
                        float currentSize = btn.Font.Size;
                        btn.Font = new Font(HeadlineFont, currentSize, FontStyle.Regular);
                    }
                }
                else if (ctrl is NumericUpDown nud)
                {
                    float currentSize = nud.Font.Size;
                    nud.Font = new Font(BodyFont, currentSize, FontStyle.Bold);
                }
                else if (ctrl is ComboBox cbx)
                {
                    float currentSize = cbx.Font.Size;
                    cbx.Font = new Font(BodyFont, currentSize, FontStyle.Bold);
                }

                if (ctrl.HasChildren)
                {
                    UpdateControlFonts(ctrl);
                }
            }
        }

        private void AddIconTextRow(TableLayoutPanel grid, ref int row, string icon, string label, Control input)
        {
            input.Width = 120;
            if (input is TextBox textBox)
            {
                StyleTextBox(textBox);
            }

            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            grid.Controls.Add(LabelRow(label), 0, row);
            grid.Controls.Add(input, 1, row);
            row++;
        }

        private void AddTextRow(TableLayoutPanel grid, ref int row, string label, Control input)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            grid.Controls.Add(WhiteLabel(label, 11, true), 0, row);
            grid.Controls.Add(input, 1, row);
            row++;
        }

        private void AddAbsoluteIconInputRow(Control parent, int x, int y, string icon, string label, Control input)
        {
            var text = WhiteLabel(label, 11);
            text.Location = new Point(x, y + 3);
            text.AutoSize = false;
            text.Size = new Size(175, 30);
            parent.Controls.Add(text);

            input.Location = new Point(215, y);
            input.Size = new Size(120, 28);
            parent.Controls.Add(input);
        }

        private Control LineAt(int x, int y, int width)
        {
            return new Label
            {
                Location = new Point(x, y),
                Size = new Size(width, 1),
                BackColor = Border
            };
        }

        private void AddSeparator(TableLayoutPanel grid, ref int row)
        {
            Control sep = SeparatorControl();
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            grid.Controls.Add(sep, 0, row);
            grid.SetColumnSpan(sep, 2);
            row++;
        }

        private Control IconLabelRow(string? icon, string label)
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = Padding.Empty
            };
            if (!string.IsNullOrEmpty(icon))
            {
                var imgLbl = CreateImageLabel(icon, 20, new Padding(0, 6, 6, 0), 1f);
                row.Controls.Add(imgLbl);
            }
            row.Controls.Add(WhiteLabel(label, 10f, false));
            return row;
        }

        private Control LabelRow(string label)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
            row.Controls.Add(WhiteLabel(label, 10f, false));
            return row;
        }

        private Control IconCheckRow(string icon, CheckBox check, int iconWidth)
        {
            var row = new FlowLayoutPanel { Width = 520, Height = Math.Max(42, iconWidth + 8), WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4) };
            row.Controls.Add(check);
            row.Controls.Add(CreateImageLabel(icon, iconWidth, new Padding(8, 6, 0, 0), 1f));
            return row;
        }

        private Label CreateImageLabel(string file, int width, Padding margin, float opacity)
        {
            var label = new Label { AutoSize = false, BackColor = Color.Transparent, Margin = margin };
            Image? image = LoadImage(file, width, 0);
            if (image != null && opacity < 1f)
            {
                image = ApplyOpacity(image, opacity);
            }

            label.Image = image;
            label.Size = image?.Size ?? new Size(width, width);
            return label;
        }

        private Label CreateVillageIcon(int village)
        {
            string path = Path.Combine("profiles", $"account_{village}.png");
            var label = new Label { Width = 95, Height = 40, BackColor = Color.Transparent };
            if (File.Exists(path))
            {
                using Image img = Image.FromFile(path);
                label.Image = ScaleImage(img, 90, 38);
            }

            return label;
        }

        private Label WhiteLabel(string text, float size, bool uppercase = false)
        {
            return new Label
            {
                Text = uppercase ? text.ToUpperInvariant() : text,
                AutoSize = true,
                ForeColor = TextMain,
                BackColor = Color.Transparent,
                Font = new Font(uppercase ? HeadlineFont : BodyFont, size, uppercase ? FontStyle.Regular : FontStyle.Bold),
                Margin = new Padding(0, 6, 6, 0)
            };
        }

        private Control SeparatorControl()
        {
            return new Label
            {
                Height = 1,
                Width = 330,
                BackColor = Border,
                Margin = new Padding(0, 7, 0, 7)
            };
        }

        private void StyleCheck(CheckBox check, float size)
        {
            check.AutoSize = false;
            check.Width = 280;
            check.Height = 32;
            check.ForeColor = TextMain;
            check.BackColor = Color.Transparent;
            check.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            check.Margin = new Padding(0, 6, 6, 0);
            check.FlatStyle = FlatStyle.Standard;
            check.FlatAppearance.BorderColor = Border;
            check.FlatAppearance.BorderSize = 3;
            check.FlatAppearance.CheckedBackColor = Border;
            check.FlatAppearance.MouseOverBackColor = SurfaceSunken;
        }

        private void StyleTextBox(TextBox input)
        {
            input.Font = new Font(BodyFont, 10F, FontStyle.Regular);
            input.BackColor = Color.FromArgb(200, 200, 200);
            input.ForeColor = Color.Black;
            input.BorderStyle = BorderStyle.None;
            input.Margin = Padding.Empty;
        }

        private void StyleNumeric(NumericUpDown input)
        {
            input.Font = new Font(BodyFont, 10F, FontStyle.Regular);
            input.BackColor = Color.FromArgb(200, 200, 200);
            input.ForeColor = Color.Black;
            input.BorderStyle = BorderStyle.None;
            input.Margin = Padding.Empty;
            input.ThousandsSeparator = false;
        }

        private void StyleCombo(ComboBox combo)
        {
            combo.Font = new Font(BodyFont, 10F, FontStyle.Regular);
            combo.BackColor = Color.FromArgb(200, 200, 200);
            combo.ForeColor = Color.Black;
            combo.FlatStyle = FlatStyle.Flat;
            combo.Margin = Padding.Empty;
        }

        private void StyleTrainRadio(RadioButton radio)
        {
            radio.Appearance = Appearance.Button;
            radio.AutoSize = false;
            radio.Width = 64;
            radio.Height = 34;
            radio.FlatStyle = FlatStyle.Flat;
            radio.FlatAppearance.BorderSize = 0;
            radio.Cursor = Cursors.Hand;
            radio.Font = new Font(HeadlineFont, 10F, FontStyle.Regular);
            radio.Margin = new Padding(0, 4, 0, 4);

            radio.MouseEnter += (s, e) => { if (radio.Enabled) radio.Invalidate(); };
            radio.MouseLeave += (s, e) => { if (radio.Enabled) radio.Invalidate(); };
            radio.MouseDown += (s, e) => { if (radio.Enabled) radio.Invalidate(); };
            radio.MouseUp += (s, e) => { if (radio.Enabled) radio.Invalidate(); };
            radio.EnabledChanged += (s, e) => radio.Invalidate();
            radio.CheckedChanged += (s, e) => radio.Invalidate();

            radio.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                var rect = new Rectangle(0, 0, radio.Width - 1, radio.Height - 1);
                using (var path = GetRoundedPath(rect, 4))
                {
                    Color backColor = radio.Checked ? TextMain : Surface;
                    Color foreColor = radio.Checked ? Color.FromArgb(15, 15, 20) : TextMain;
                    Color borderColor = radio.Checked ? Color.Transparent : BorderStrong;

                    bool isHovered = radio.ClientRectangle.Contains(radio.PointToClient(Cursor.Position));
                    bool isPressed = isHovered && (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left;

                    if (!radio.Enabled)
                    {
                        backColor = DisabledSurface;
                        foreColor = TextMuted;
                        borderColor = DisabledBorder;
                    }
                    else if (isPressed)
                    {
                        backColor = radio.Checked ? Color.FromArgb(230, 230, 230) : SurfaceSunken;
                    }
                    else if (isHovered)
                    {
                        backColor = radio.Checked ? Color.FromArgb(240, 240, 240) : Color.FromArgb(36, 36, 44);
                    }

                    using (var brush = new SolidBrush(backColor))
                    {
                        e.Graphics.FillPath(brush, path);
                    }

                    if (borderColor != Color.Transparent)
                    {
                        using (var pen = new Pen(borderColor, 1f))
                        {
                            e.Graphics.DrawPath(pen, path);
                        }
                    }

                    TextRenderer.DrawText(
                        e.Graphics,
                        radio.Text,
                        radio.Font,
                        rect,
                        foreColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            };
        }

        private void StyleSolidButton(Button button, ButtonKind kind)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 3;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = kind == ButtonKind.Destructive ? Border : Border;
            button.FlatAppearance.MouseDownBackColor = kind == ButtonKind.Destructive ? Danger : Border;
            button.BackColor = kind == ButtonKind.Primary ? Border : kind == ButtonKind.Destructive ? Danger : Surface;
            button.ForeColor = kind == ButtonKind.Primary || kind == ButtonKind.Destructive ? Color.White : TextMain;
            button.Font = new Font(HeadlineFont, 8.5F, FontStyle.Regular);
            button.Margin = new Padding(4, 4, 4, 4);
            button.Cursor = Cursors.Hand;
            AttachButtonInteraction(button, kind);
        }

        private void StyleCommandButton(Button button, string text, ButtonKind kind)
        {
            button.Text = text;
            button.Width = 100;
            button.Height = 36;
            button.Image = null;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 3;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = kind == ButtonKind.Destructive ? Border : Border;
            button.FlatAppearance.MouseDownBackColor = kind == ButtonKind.Destructive ? Danger : Border;
            button.BackColor = kind == ButtonKind.Primary ? Border : kind == ButtonKind.Destructive ? Danger : Surface;
            button.ForeColor = kind == ButtonKind.Primary || kind == ButtonKind.Destructive ? Color.White : TextMain;
            button.Font = new Font(HeadlineFont, 9.5F, FontStyle.Regular);
            button.Margin = new Padding(10, 0, 0, 0);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Cursor = Cursors.Hand;
            AttachButtonInteraction(button, kind);
        }

        private void AttachButtonInteraction(Button button, ButtonKind kind)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;

            button.MouseEnter += (s, e) => { if (button.Enabled) button.Invalidate(); };
            button.MouseLeave += (s, e) => { if (button.Enabled) button.Invalidate(); };
            button.MouseDown += (s, e) => { if (button.Enabled) button.Invalidate(); };
            button.MouseUp += (s, e) => { if (button.Enabled) button.Invalidate(); };
            button.EnabledChanged += (s, e) => button.Invalidate();

            button.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                var rect = new Rectangle(0, 0, button.Width - 1, button.Height - 1);
                using (var path = GetRoundedPath(rect, 4))
                {
                    Color backColor = kind == ButtonKind.Primary ? PrimaryOrange : (kind == ButtonKind.Destructive ? Danger : Surface);
                    Color foreColor = kind == ButtonKind.Primary || kind == ButtonKind.Destructive ? Color.White : TextMain;
                    Color borderColor = kind == ButtonKind.Secondary ? BorderStrong : Color.Transparent;

                    bool isHovered = button.ClientRectangle.Contains(button.PointToClient(Cursor.Position));
                    bool isPressed = isHovered && (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left;

                    if (!button.Enabled)
                    {
                        backColor = DisabledSurface;
                        foreColor = TextMuted;
                        borderColor = DisabledBorder;
                    }
                    else if (isPressed)
                    {
                        if (kind == ButtonKind.Primary)
                        {
                            backColor = PrimaryOrangeActive;
                        }
                        else if (kind == ButtonKind.Destructive)
                        {
                            backColor = Color.FromArgb(177, 35, 70);
                        }
                        else
                        {
                            backColor = SurfaceSunken;
                        }
                    }
                    else if (isHovered)
                    {
                        if (kind == ButtonKind.Primary)
                        {
                            backColor = Color.FromArgb(255, 95, 20);
                        }
                        else if (kind == ButtonKind.Destructive)
                        {
                            backColor = Color.FromArgb(227, 55, 96);
                        }
                        else
                        {
                            backColor = Color.FromArgb(36, 36, 44);
                        }
                    }

                    using (var brush = new SolidBrush(backColor))
                    {
                        e.Graphics.FillPath(brush, path);
                    }

                    if (borderColor != Color.Transparent)
                    {
                        using (var pen = new Pen(borderColor, 1f))
                        {
                            e.Graphics.DrawPath(pen, path);
                        }
                    }

                    // Draw optional icon next to the text
                    Image? iconImg = null;
                    if (button == _startButton)
                    {
                        iconImg = _startIcon;
                    }
                    else if (button == _stopButton)
                    {
                        iconImg = _stopIcon;
                    }
                    else if (button == _pauseToggleButton)
                    {
                        iconImg = _pauseIcon;
                    }

                    if (iconImg != null)
                    {
                        Image drawImg = iconImg;
                        bool isTempImage = false;
                        if (!button.Enabled)
                        {
                            Image clone = (Image)iconImg.Clone();
                            drawImg = ApplyOpacity(clone, 0.4f);
                            isTempImage = true;
                        }

                        // Measure text to center text and icon together
                        Size textSize = TextRenderer.MeasureText(button.Text, button.Font);
                        int totalWidth = drawImg.Width + 6 + textSize.Width;
                        int startX = (button.Width - totalWidth) / 2;
                        int iconY = (button.Height - drawImg.Height) / 2;

                        e.Graphics.DrawImage(drawImg, startX, iconY);

                        var textRect = new Rectangle(startX + drawImg.Width + 6, 0, textSize.Width + 4, button.Height);
                        TextRenderer.DrawText(
                            e.Graphics,
                            button.Text,
                            button.Font,
                            textRect,
                            foreColor,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

                        if (isTempImage)
                        {
                            drawImg.Dispose();
                        }
                    }
                    else
                    {
                        TextRenderer.DrawText(
                            e.Graphics,
                            button.Text,
                            button.Font,
                            rect,
                            foreColor,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                }
            };
        }

        private void StyleImageButton(Button button, string name, string imageFile, int width, int height)
        {
            button.Name = name;
            button.Width = width;
            button.Height = height;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseDownBackColor = Color.Transparent;
            button.FlatAppearance.MouseOverBackColor = Color.Transparent;
            button.FlatAppearance.CheckedBackColor = Color.Transparent;
            button.BackColor = Color.Transparent;
            button.Image = LoadImage(imageFile, width, height);
            button.Margin = new Padding(0, 0, 6, 0);
            button.Text = "";
        }

        private void AddStatsRow(TableLayoutPanel grid, string name, Label value, string? icon = null)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            grid.Controls.Add(IconLabelRow(icon, name + (name.EndsWith(":", StringComparison.Ordinal) ? "" : ":")), 0, row);
            value.Text = "0";
            value.ForeColor = TextMain;
            value.BackColor = Color.Transparent;
            value.Font = new Font(MonoFont, 9.5F, FontStyle.Regular);
            value.TextAlign = ContentAlignment.MiddleRight;
            value.Dock = DockStyle.Fill;
            grid.Controls.Add(value, 1, row);
        }

        private void AddStatsSeparator(TableLayoutPanel grid)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
            var sep = new Label { Height = 3, Dock = DockStyle.Fill, BackColor = Border, Margin = new Padding(0, 4, 0, 4) };
            grid.Controls.Add(sep, 0, row);
            grid.SetColumnSpan(sep, 2);
        }

        private void AddStarStatsRow(TableLayoutPanel grid, int stars, Label value)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
            left.Controls.Add(WhiteLabel(stars == 1 ? "1 Star:" : $"{stars} Stars:", 10, true));
            grid.Controls.Add(left, 0, row);

            value.Text = "0";
            value.ForeColor = stars switch
            {
                0 => Danger,
                1 => PrimaryOrange,
                2 => TextMain,
                _ => Accent
            };
            value.BackColor = Color.Transparent;
            value.Font = new Font(MonoFont, 10F, FontStyle.Regular);
            value.TextAlign = ContentAlignment.MiddleRight;
            value.Dock = DockStyle.Fill;
            grid.Controls.Add(value, 1, row);
        }

        private void ToggleWallInputs()
        {
            bool enabled = _upgradeWallCheck.Checked;
            foreach (Control control in new Control[] { _wallGoldInput, _wallElixirInput, _wallLevelInput })
            {
                control.Enabled = enabled;
            }
        }

        private void UpdateQuickSlotState()
        {
            _quickSlotInput.Enabled = _quickTrainRadio.Checked;
        }

        private void ApplyMultiVillageState()
        {
            bool enabled = _multiAccountCheck.Checked;
            int count = (int)_accountCountInput.Value;
            _accountCountInput.Enabled = enabled;
            _intervalCombo.Enabled = enabled;

            for (int i = 0; i < _villageChecks.Count; i++)
            {
                bool active = enabled && i < count;
                _villageChecks[i].Enabled = active;
                _loadVillageButtons[i].Enabled = active;
                _saveVillageButtons[i].Enabled = true;
            }
        }

        private void ApplyClanCapitalState()
        {
            bool enabled = _clanCapitalCheck.Checked;
            _capitalHallInput.Enabled = enabled;
            _capitalNoteLabel.Enabled = enabled;
            _capitalLevelLabel.Enabled = enabled;
        }

        private void UpdateAttackPreview()
        {
            string key = AttackKeyFromDisplay(_attackCombo.Text);
            _attackPreview.Image = null;
            _attackPreview.Text = key == "ElectroDragon_Attack" ? "ELECTRO DRAGON" : "DRAGON";
            _attackDescription.Text = key == "ElectroDragon_Attack" ? "ELECTRO DRAGON ATTACK" : "DRAGON ATTACK";
        }

        private void ConfirmAndLoadVillage(int village)
        {
            _currentVillage = village;
            LoadSelectedProfile();
            HighlightVillage(village);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Loaded Village_{village}");
        }

        private void SaveVillage(int village)
        {
            int previous = _currentVillage;
            _currentVillage = village;
            SaveCurrentProfile();
            _currentVillage = previous;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Saved Village_{village}");
        }

        private void HighlightVillage(int village)
        {
            for (int i = 0; i < _villageChecks.Count; i++)
            {
                _villageChecks[i].ForeColor = i + 1 == village ? Accent : TextMain;
            }
        }

        private void LoadMainConfig()
        {
            JsonObject root = ReadJsonObject(ConfigPath);

            JsonObject device = GetObject(root, "device_connection");
            _adbHostInput.Text = GetString(device, "host", "127.0.0.1");
            _adbPortInput.Value = Clamp(GetInt(device, "port", 5556), _adbPortInput);

            JsonObject multi = GetObject(root, "multi_account");
            _multiAccountCheck.Checked = GetBool(multi, "enable_multi_account", true);
            _accountCountInput.Value = Clamp(GetSelectedVillages(multi).Length, _accountCountInput);
            _intervalCombo.SelectedItem = MinutesToInterval(GetInt(multi, "multi_interval_mins", 60));
            if (_intervalCombo.SelectedIndex < 0) _intervalCombo.SelectedIndex = 1;
            int[] selected = GetSelectedVillages(multi);
            for (int i = 0; i < _villageChecks.Count; i++)
            {
                _villageChecks[i].Checked = selected.Contains(i + 1);
            }

            JsonObject farming = GetObject(root, "farming_thresholds");
            if (farming.Count == 0)
            {
                JsonObject target = GetObject(root, "target_data_threshold");
                _goldThresholdInput.Text = GetInt(target, "gold", 650_000).ToString();
                _elixirThresholdInput.Text = GetInt(target, "elixir", 650_000).ToString();
                _darkThresholdInput.Text = GetInt(target, "dark_elixir", 1_000).ToString();
            }
            else
            {
                _goldThresholdInput.Text = GetInt(farming, "gold_threshold", 650_000).ToString();
                _elixirThresholdInput.Text = GetInt(farming, "elixir_threshold", 650_000).ToString();
                _darkThresholdInput.Text = GetInt(farming, "dark_elixir_threshold", 1_000).ToString();
            }

            _enableStatsCheck.Checked = GetBool(root, "enable_stats", true);
            SelectAttack(GetString(root, "attack", "Dragon_Attack"));
            string trainMode = GetString(root, "train_mode", "smart");
            _smartTrainRadio.Checked = trainMode != "quick";
            _quickTrainRadio.Checked = trainMode == "quick";
            _quickSlotInput.Value = Clamp(GetInt(root, "quick_slot", 1), _quickSlotInput);


            UpdateQuickSlotState();
            ApplyMultiVillageState();
            UpdateAttackPreview();
        }

        private void LoadSelectedProfile()
        {
            if (_loadingProfile)
            {
                return;
            }

            _loadingProfile = true;
            try
            {
                JsonObject profile = ReadJsonObject(ProfilePath(_currentVillage));
                _upgradeWallCheck.Checked = GetBool(profile, "upgrade_wall", true);
                _wallLevelInput.Value = Clamp(GetInt(profile, "wall_level", 18), _wallLevelInput);
                _wallGoldInput.Text = GetInt(profile, "wall_gold_threshold", 5_000_000).ToString();
                _wallElixirInput.Text = GetInt(profile, "wall_elixir_threshold", 5_000_000).ToString();
                _requestTroopsCheck.Checked = GetBool(profile, "request_troops", false);

                _clanGamesCheck.Checked = GetBool(profile, "enable_clan_games", false);
                _clanCapitalCheck.Checked = GetBool(profile, "enable_clan_capital", false);
                _capitalHallInput.Value = Clamp(GetInt(profile, "capital_hall_level", 9), _capitalHallInput);



                SelectAttack(GetString(profile, "attack", AttackKeyFromDisplay(_attackCombo.Text)));
                string trainMode = GetString(profile, "train_mode", _quickTrainRadio.Checked ? "quick" : "smart");
                _smartTrainRadio.Checked = trainMode != "quick";
                _quickTrainRadio.Checked = trainMode == "quick";
                _quickSlotInput.Value = Clamp(GetInt(profile, "quick_slot", (int)_quickSlotInput.Value), _quickSlotInput);
                ToggleWallInputs();
                ApplyClanCapitalState();
                UpdateQuickSlotState();
                HighlightVillage(_currentVillage);
            }
            finally
            {
                _loadingProfile = false;
            }
        }

        private void SaveConfigFromForm()
        {
            Directory.CreateDirectory("profiles");
            SaveMainConfig();
            SaveCurrentProfile();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Saved config and Village_{_currentVillage}.json");
        }

        private void SaveMainConfig()
        {
            JsonObject root = ReadJsonObject(ConfigPath);
            int gold = ParseInt(_goldThresholdInput, 650_000);
            int elixir = ParseInt(_elixirThresholdInput, 650_000);
            int dark = ParseInt(_darkThresholdInput, 1_000);

            root["device_connection"] = new JsonObject
            {
                ["host"] = _adbHostInput.Text.Trim().Length == 0 ? "127.0.0.1" : _adbHostInput.Text.Trim(),
                ["port"] = (int)_adbPortInput.Value
            };

            root["farming_thresholds"] = new JsonObject
            {
                ["gold_threshold"] = gold,
                ["elixir_threshold"] = elixir,
                ["dark_elixir_threshold"] = dark
            };

            root["target_data_threshold"] = new JsonObject
            {
                ["gold"] = gold,
                ["elixir"] = elixir,
                ["dark_elixir"] = dark
            };

            int[] selected = SelectedVillages();
            root["multi_account"] = new JsonObject
            {
                ["enable_multi_account"] = _multiAccountCheck.Checked,
                ["multi_interval_mins"] = IntervalToMinutes(_intervalCombo.Text),
                ["selected_villages"] = new JsonArray(selected.Select(v => JsonValue.Create(v)).ToArray<JsonNode?>())
            };

            root["enable_stats"] = _enableStatsCheck.Checked;
            root["attack"] = AttackKeyFromDisplay(_attackCombo.Text);
            root["train_mode"] = _quickTrainRadio.Checked ? "quick" : "smart";
            root["quick_slot"] = (int)_quickSlotInput.Value;
            root["upgrade_wall"] = _upgradeWallCheck.Checked;
            root["wall_level"] = (int)_wallLevelInput.Value;
            root["wall_gold_threshold"] = ParseInt(_wallGoldInput, 5_000_000);
            root["wall_elixir_threshold"] = ParseInt(_wallElixirInput, 5_000_000);
            root["request_troops"] = _requestTroopsCheck.Checked;
            root["enable_clan_games"] = _clanGamesCheck.Checked;
            root["enable_clan_capital"] = _clanCapitalCheck.Checked;
            root["capital_hall_level"] = (int)_capitalHallInput.Value;



            WriteJson(ConfigPath, root);
        }

        private void SaveCurrentProfile()
        {
            JsonObject profile = ReadJsonObject(ProfilePath(_currentVillage));
            profile["gold_threshold"] = ParseInt(_goldThresholdInput, 650_000);
            profile["elixir_threshold"] = ParseInt(_elixirThresholdInput, 650_000);
            profile["dark_elixir_threshold"] = ParseInt(_darkThresholdInput, 1_000);
            profile["upgrade_wall"] = _upgradeWallCheck.Checked;
            profile["wall_level"] = (int)_wallLevelInput.Value;
            profile["wall_gold_threshold"] = ParseInt(_wallGoldInput, 5_000_000);
            profile["wall_elixir_threshold"] = ParseInt(_wallElixirInput, 5_000_000);
            profile["request_troops"] = _requestTroopsCheck.Checked;
            profile["enable_clan_games"] = _clanGamesCheck.Checked;
            profile["enable_clan_capital"] = _clanCapitalCheck.Checked;
            profile["capital_hall_level"] = (int)_capitalHallInput.Value;
            profile["enable_stats"] = _enableStatsCheck.Checked;
            profile["attack"] = AttackKeyFromDisplay(_attackCombo.Text);
            profile["train_mode"] = _quickTrainRadio.Checked ? "quick" : "smart";
            profile["quick_slot"] = (int)_quickSlotInput.Value;



            WriteJson(ProfilePath(_currentVillage), profile);
        }

        private void StartBot()
        {
            if (_framework != null)
            {
                return;
            }

            SaveConfigFromForm();
            _originalOut = Console.Out;
            _uiWriter = new UiLogTextWriter(_originalOut, AppendLog, this);
            Console.SetOut(_uiWriter);

            try
            {
                _framework = new CVAutomationFramework(ConfigPath);
                _framework.Start();
                SetRunningState(true);
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Bot started");
            }
            catch (Exception ex)
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI ERROR] {ex}");
                RestoreConsole();
                _framework = null;
                SetRunningState(false);
            }
        }

        private void StopBot()
        {
            if (_framework == null)
            {
                return;
            }

            try
            {
                _framework.Stop();
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Stop requested");
            }
            finally
            {
                _framework = null;
                RestoreConsole();
                SetRunningState(false);
            }
        }

        private void TogglePause()
        {
            if (_framework == null)
            {
                return;
            }

            if (_paused)
            {
                _framework.Resume();
                _paused = false;
                _pauseToggleButton.Text = "";
                _pauseToggleButton.BackColor = Color.Transparent;
                _startButton.Enabled = false;
                _stopButton.Enabled = true;
                _statusLabel.Text = "RUNNING";
                _statusLabel.ForeColor = Accent;
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Resume requested");
            }
            else
            {
                _framework.Pause();
                _paused = true;
                _pauseToggleButton.Text = "";
                _pauseToggleButton.BackColor = Color.Transparent;
                _startButton.Enabled = false;
                _stopButton.Enabled = true;
                _statusLabel.Text = "PAUSED";
                _statusLabel.ForeColor = TextMain;
                AppendLog($"[{DateTime.Now:HH:mm:ss}] [GUI] Pause requested");
            }
        }

        private void SetRunningState(bool running)
        {
            _startButton.Enabled = !running;
            _stopButton.Enabled = running;
            _pauseToggleButton.Enabled = running;
            _paused = false;
            _pauseToggleButton.Text = "";
            _pauseToggleButton.BackColor = Color.Transparent;
            _statusLabel.Text = running ? "RUNNING" : "IDLE";
            _statusLabel.ForeColor = running ? Accent : TextMuted;
        }

        private void RefreshStatsFromJson()
        {
            JsonObject stats = ReadJsonObject(Path.Combine("profiles", $"Stats_{_currentVillage}.json"));
            int gold = GetInt(stats, "gold", 0);
            int elixir = GetInt(stats, "elixir", 0);
            int dark = GetInt(stats, "de", 0);
            int attacks = GetInt(stats, "attacks", 0);

            _statsGoldLabel.Text = FormatShortNumber(gold);
            _statsElixirLabel.Text = FormatShortNumber(elixir);
            _statsDarkLabel.Text = FormatShortNumber(dark);
            _statsAttacksLabel.Text = attacks.ToString();

            double hours = Math.Max(1.0 / 60.0, (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - GetInt(stats, "last_update_ts", 0)) / 3600.0);
            if (GetInt(stats, "last_update_ts", 0) <= 0)
            {
                hours = 1;
            }

            _statsAvgGoldLabel.Text = FormatShortNumber((int)Math.Round(gold / hours));
            _statsAvgElixirLabel.Text = FormatShortNumber((int)Math.Round(elixir / hours));
            _statsAvgDarkLabel.Text = FormatShortNumber((int)Math.Round(dark / hours));

            JsonObject stars = GetObject(stats, "stars");
            for (int i = 0; i < _statsStarLabels.Length; i++)
            {
                _statsStarLabels[i].Text = GetInt(stars, i.ToString(), 0).ToString();
            }
        }

        private static string FormatShortNumber(int value)
        {
            return Math.Abs((long)value) switch
            {
                >= 1_000_000 => (value / 1_000_000D).ToString("0.##") + "M",
                >= 1_000 => (value / 1_000D).ToString("0.#") + "K",
                _ => value.ToString()
            };
        }

        private void RestoreConsole()
        {
            if (_originalOut != null)
            {
                Console.SetOut(_originalOut);
                _originalOut = null;
            }

            _uiWriter?.Dispose();
            _uiWriter = null;
        }

        private void AppendLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            string englishLog = line;
            bool alreadyProcessed = line.Length > 20 && line.StartsWith("[", StringComparison.Ordinal) && line.IndexOf("] [", StringComparison.Ordinal) > 0;

            if (!alreadyProcessed)
            {
                if (ShouldIgnoreLog(line)) return;

                englishLog = TranslateLogToEnglish(line);
                if (string.IsNullOrWhiteSpace(englishLog)) return;
            }

            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(AppendLog), englishLog);
                }
                catch
                {
                    // The form can close while a worker is writing its final line.
                }

                return;
            }

            _logBox.AppendText(englishLog);
            if (!englishLog.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                _logBox.AppendText(Environment.NewLine);
            }
        }

        private bool ShouldIgnoreLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return true;

            string[] noiseKeywords = new[]
            {
                "[WINDOW CHECK]",
                "[TEMPLATE]",
                "[SPACE secondary]",
                "[DIAG]",
                "[DIAG COUNT]",
                "[DIAG COUNT SCAN]",
                "[ATTACK-CS DEBUG]",
                "match=army_space",
                "best match =",
                "[DEBUG]",
                "match score =",
                "[COUNT OCR]",
                "[TPL]",
                "Checking if we're on the home base screen",
                "Checking image at path",
                "Confidence:",
                "match score = ",
                "max match =",
                "deploy elapsed=",
                "spell elapsed=",
                "Heroes deploy elapsed=",
                "not found (",
                "Bỏ qua: Thẻ '",
                "Bỏ qua: Không có tọa độ thả lính",
                "Không kiểm tra quân còn lại: Thẻ '",
                "Không có điểm rải bổ sung",
                "Không đọc được số quân còn lại",
                "đã rải hết.",
                "confidence 0.",
                "Bỏ qua "
            };

            foreach (var keyword in noiseKeywords)
            {
                if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string TranslateLogToEnglish(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return "";

            // 1. Check and extract pre-existing timestamp
            string timestamp = $"[{DateTime.Now:HH:mm:ss}]";
            var timeMatch = System.Text.RegularExpressions.Regex.Match(line, @"^\[\d{2}:\d{2}:\d{2}\]\s*");
            if (timeMatch.Success)
            {
                timestamp = timeMatch.Value.Trim();
                line = line.Substring(timeMatch.Length);
            }

            // 2. Normalize and strip tags
            string cleanLine = line.Trim();
            string tag = "[BOT]";

            if (cleanLine.StartsWith("[FSM-CS]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(8).Trim();
                tag = "[BOT]";
            }
            else if (cleanLine.StartsWith("[ADB]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(5).Trim();
                tag = "[ADB]";
            }
            else if (cleanLine.StartsWith("[ADB WARNING]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(13).Trim();
                tag = "[ADB WARNING]";
            }
            else if (cleanLine.StartsWith("[ADB ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(11).Trim();
                tag = "[ADB ERROR]";
            }
            else if (cleanLine.StartsWith("[ATTACK-CS]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(11).Trim();
                tag = "[ATTACK]";
            }
            else if (cleanLine.StartsWith("[ATTACK-CS WARNING]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(19).Trim();
                tag = "[ATTACK WARNING]";
            }
            else if (cleanLine.StartsWith("[ATTACK-CS ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(17).Trim();
                tag = "[ATTACK ERROR]";
            }
            else if (cleanLine.StartsWith("[VISION]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(8).Trim();
                tag = "[VISION]";
            }
            else if (cleanLine.StartsWith("[GUI]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(5).Trim();
                tag = "[GUI]";
            }
            else if (cleanLine.StartsWith("[GUI ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(11).Trim();
                tag = "[GUI ERROR]";
            }
            else if (cleanLine.StartsWith("[WALL]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(6).Trim();
                tag = "[WALL]";
            }
            else if (cleanLine.StartsWith("[TRAIN]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(7).Trim();
                tag = "[TRAIN]";
            }
            else if (cleanLine.StartsWith("[SCOUT-CS]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(10).Trim();
                tag = "[MATCH]";
            }
            else if (cleanLine.StartsWith("[SCOUT-CS ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                cleanLine = cleanLine.Substring(16).Trim();
                tag = "[MATCH ERROR]";
            }

            // 3. Translate using standard mapping dictionary & regexes
            string translated = TranslateText(cleanLine, ref tag);
            if (string.IsNullOrWhiteSpace(translated)) return "";

            // 4. Translate resource keywords in translated text
            translated = translated
                .Replace("Vàng:", "Gold:", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu hồng:", "Elixir:", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu đen:", "Dark Elixir:", StringComparison.OrdinalIgnoreCase)
                .Replace("Vàng", "Gold", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu hồng", "Elixir", StringComparison.OrdinalIgnoreCase)
                .Replace("Dầu đen", "Dark Elixir", StringComparison.OrdinalIgnoreCase);

            return $"{timestamp} {tag} {translated}";
        }

        private string TranslateText(string text, ref string tag)
        {
            text = text.Trim();

            // Check matching target patterns first
            if (text.Contains("Bắt đầu Chu kỳ đơn", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                var m = System.Text.RegularExpressions.Regex.Match(text, @"Làng_(\d+)");
                return m.Success ? $"--- Starting Cycle (Village {m.Groups[1].Value}) ---" : "--- Starting Cycle ---";
            }
            if (text.Contains("Kết thúc Chu kỳ đơn", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                var m = System.Text.RegularExpressions.Regex.Match(text, @"Làng_(\d+)");
                return m.Success ? $"--- Finished Cycle (Village {m.Groups[1].Value}) ---" : "--- Finished Cycle ---";
            }
            if (text.Contains("TÌM TRẬN ĐẤU", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[MATCH]";
                var goldMatch = System.Text.RegularExpressions.Regex.Match(text, @"Vàng\s*>=?\s*([\d,]+)");
                var elixirMatch = System.Text.RegularExpressions.Regex.Match(text, @"Dầu hồng\s*>=?\s*([\d,]+)");
                string gold = goldMatch.Success ? goldMatch.Groups[1].Value : "N/A";
                string elixir = elixirMatch.Success ? elixirMatch.Groups[1].Value : "N/A";
                return $"Scouting targets (Gold >= {gold} | Elixir >= {elixir})...";
            }
            if (text.Contains("Phân tích nhà đối thủ", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[MATCH]";
                var m = System.Text.RegularExpressions.Regex.Match(text, @"đối thủ\s*(\d+/\d+)");
                return m.Success ? $"Scouting opponent target {m.Groups[1].Value}..." : "Scouting opponent target...";
            }
            if (text.Contains("ĐÃ ĐẠT TIÊU CHÍ!", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[MATCH]";
                var goldMatch = System.Text.RegularExpressions.Regex.Match(text, @"Gold=([\d,]+)");
                var elixirMatch = System.Text.RegularExpressions.Regex.Match(text, @"Elixir=([\d,]+)");
                string g = goldMatch.Success ? goldMatch.Groups[1].Value : "Target";
                string e = elixirMatch.Success ? elixirMatch.Groups[1].Value : "Target";
                return $"TARGET FOUND! Loot: Gold={g}, Elixir={e}";
            }
            if (text.Contains("Thực thi:", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[ATTACK]";
                var stratMatch = System.Text.RegularExpressions.Regex.Match(text, @"Thực thi:\s*([a-zA-Z0-9_]+)");
                var sideMatch = System.Text.RegularExpressions.Regex.Match(text, @"Tấn công cánh:\s*([a-zA-Z0-9_]+)");
                string strat = stratMatch.Success ? stratMatch.Groups[1].Value : "Attack Strategy";
                string side = sideMatch.Success ? sideMatch.Groups[1].Value : "N/A";
                return $"Executing strategy: {strat} | Side: {side.ToUpper()}";
            }
            if (text.Contains("Đã phát hiện thẻ '", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[ATTACK]";
                var m = System.Text.RegularExpressions.Regex.Match(text, @"thẻ\s*'([^']+)'");
                return m.Success ? $"Card detected: '{m.Groups[1].Value}'" : "Card detected.";
            }
            if (text.Contains("Chọn thẻ '", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[ATTACK]";
                var cardMatch = System.Text.RegularExpressions.Regex.Match(text, @"thẻ\s*'([^']+)'");
                var tapsMatch = System.Text.RegularExpressions.Regex.Match(text, @"\((\d+)\s*taps?\)");
                string card = cardMatch.Success ? cardMatch.Groups[1].Value : "unknown";
                string taps = tapsMatch.Success ? tapsMatch.Groups[1].Value : "some";
                return $"Deploying '{card}' ({taps} taps)";
            }
            if (text.Contains("Thu hoạch", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                var m = System.Text.RegularExpressions.Regex.Match(text, @"Thu hoạch\s+([a-zA-Z0-9_]+)");
                if (m.Success)
                {
                    string name = m.Groups[1].Value.Replace("_", " ");
                    name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
                    return $"Harvested {name}.";
                }
                return "Harvesting resources...";
            }
            if (text.Contains("Luyện quân nhanh (Quick Train Slot", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[TRAIN]";
                var m = System.Text.RegularExpressions.Regex.Match(text, @"Slot\s*(\d+)");
                return $"Quick Train started (Slot {(m.Success ? m.Groups[1].Value : "1")})...";
            }
            if (text.Contains("Smart Train theo cấu hình", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[TRAIN]";
                var m = System.Text.RegularExpressions.Regex.Match(text, @"attack='([^']+)'");
                return $"Smart Train started (Strategy: {(m.Success ? m.Groups[1].Value : "Default")})...";
            }
            if (text.Contains("Wall Updater - nâng tường level", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[WALL]";
                var m = System.Text.RegularExpressions.Regex.Match(text, @"level\s*(\d+)");
                return $"Scanning wall upgrades (Target level: {(m.Success ? m.Groups[1].Value : "N/A")})...";
            }
            if (text.Contains("Đang thực hiện chuyển sang Làng_", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                var m = System.Text.RegularExpressions.Regex.Match(text, @"Làng_(\d+)");
                return m.Success ? $"Switching to Village {m.Groups[1].Value}..." : "Switching Village...";
            }
            if (text.Contains("Hoàn tất thời gian chơi của Làng_", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                var m = System.Text.RegularExpressions.Regex.Match(text, @"Làng_(\d+)");
                return m.Success ? $"Finished session for Village {m.Groups[1].Value}." : "Finished village session.";
            }
            if (text.Contains("Đang chụp màn hình giả lập để quét tài nguyên LÀNG CHÍNH...", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                return "Capturing screen for home base resources check...";
            }
            if (text.Contains("Đang chụp màn hình giả lập để quét tài nguyên...", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[MATCH]";
                return "Capturing screen for scouting loot...";
            }
            if (text.Contains("Kết quả quét Làng chính -> Vàng:", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[BOT]";
                return text.Substring(text.IndexOf("Vàng:", StringComparison.OrdinalIgnoreCase));
            }
            if (text.Contains("Kết quả quét -> Vàng:", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[MATCH]";
                return text.Substring(text.IndexOf("Vàng:", StringComparison.OrdinalIgnoreCase));
            }
            if (text.Contains("Không thể chụp ảnh màn hình hoặc ảnh trống.", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[ERROR]";
                return "Failed to capture screenshot or image is blank.";
            }

            var dict = new Dictionary<string, (string newText, string newTag)>(StringComparer.OrdinalIgnoreCase)
            {
                { "Phân hệ lõi Máy trạng thái đã khởi tạo thành công.", ("State machine initialized successfully.", "[BOT]") },
                { "Vòng lặp tự động hóa đã bắt đầu chạy ngầm...", ("Automation loop started in background.", "[BOT]") },
                { "Đã gửi lệnh dừng khẩn cấp Máy trạng thái.", ("Emergency stop command sent.", "[BOT]") },
                { "Đã tạm dừng luồng chạy bot.", ("Bot execution paused.", "[BOT]") },
                { "Tiếp tục chạy luồng bot.", ("Bot execution resumed.", "[BOT]") },
                { "Bước 1: Xác thực tiêu điểm Làng chính...", ("Verifying Home Base focus...", "[BOT]") },
                { "Bước 2: Kéo camera góc rộng (Zoom Out)...", ("Zooming out camera view...", "[BOT]") },
                { "Đang thực hiện thu nhỏ góc nhìn bản đồ (Zoom Out)...", ("Zooming out camera view...", "[BOT]") },
                { "Quay trở về làng chính...", ("Returning to Home Base...", "[BOT]") },
                { "Vòng lặp bất đồng bộ bắt đầu xử lý...", ("Asynchronous processing active.", "[BOT]") },
                { "Chế độ chạy đơn tài khoản (Single Account Mode).", ("Running in Single Account Mode.", "[BOT]") },
                { "Vòng lặp chạy ngầm đã dừng hoàn toàn.", ("Automation loop stopped completely.", "[BOT]") },
                { "Bước 5: Tự động thu hoạch tài nguyên tại các mỏ sản xuất...", ("Harvesting resources from collectors...", "[BOT]") },
                { "Still not on home base after Treasure handling. Sending one BACK.", ("Home base check failed. Sending BACK command.", "[BOT]") },
                { "Chờ 1.5s để trang trí/base overlay của đối thủ ẩn hết trước khi đánh...", ("Waiting for base overlay to settle...", "[MATCH]") },
                { "Đang triển khai kịch bản thả quân", ("Executing army deployment...", "[ATTACK]") },
                { "Đang triển khai quân tướng...", ("Deploying Heroes...", "[ATTACK]") },
                { "Đang kích hoạt kỹ năng đặc biệt của Tướng...", ("Activating Hero special abilities...", "[ATTACK]") },
                { "Chờ thả phép đóng băng (Freeze)...", ("Deploying Freeze spell...", "[ATTACK]") },
                { "Kịch bản cướp trận hoàn tất.", ("Attack sequence completed.", "[ATTACK]") },
                { "Gửi lệnh Zoom Out ngầm tới MEmu hoàn tất.", ("MEmu background zoom-out complete.", "[ADB]") },
                { "Gửi lệnh Zoom Out BlueStacks qua ADB hoàn tất.", ("BlueStacks ADB zoom-out complete.", "[ADB]") },
                { "Tự động dò tìm và kết nối thành công tới cổng dự phòng:", ("Auto-detected and connected to backup port:", "[ADB]") },
                { "Tự động phát hiện thiết bị đang hoạt động:", ("Auto-detected active device:", "[ADB]") },
                { "Không thể lấy danh sách thiết bị từ AdbClient.", ("Could not retrieve device list from AdbClient.", "[ADB WARNING]") },
                { "Không có thiết bị nào kết nối. Đã mặc định serial:", ("No connected devices found. Setting default serial:", "[ADB WARNING]") },
                { "UIAutomator2 pinch-in không chạy được. Thử fallback ADB swipe đồng thời...", ("UIAutomator2 pinch-in failed. Falling back to simultaneous ADB swipes...", "[ADB WARNING]") },
                { "Không tìm thấy u2.jar của UIAutomator2 trong repo hoặc thư mục Simplicity.", ("u2.jar not found in repository or Simplicity folder.", "[ADB WARNING]") },
                { "Đã kết nối đến thiết bị cấu hình:", ("Connected to configured device:", "[ADB]") },
                { "Đã cache u2.jar vào thư mục build:", ("Cached u2.jar to build directory:", "[ADB]") },
                { "Bot started", ("Bot started.", "[GUI]") },
                { "Stop requested", ("Stop requested.", "[GUI]") },
                { "Resume requested", ("Resume requested.", "[GUI]") },
                { "Pause requested", ("Pause requested.", "[GUI]") },
                { "Loaded Village_", ("Loaded profile for Village", "[GUI]") },
                { "Saved Village_", ("Saved profile for Village", "[GUI]") },
                { "Saved config and Village_", ("Saved configuration and profile for Village", "[GUI]") },
                { "Không thể khởi động server ADB:", ("Cannot start ADB server:", "[ADB ERROR]") }
            };

            foreach (var kvp in dict)
            {
                if (text.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    tag = kvp.Value.newTag;
                    return text.Replace(kvp.Key, kvp.Value.newText, StringComparison.OrdinalIgnoreCase);
                }
            }

            return text;
        }

        private static string ProfilePath(int village) => Path.Combine("profiles", $"Village_{village}.json");

        private int[] SelectedVillages()
        {
            var selected = new List<int>();
            for (int i = 0; i < _villageChecks.Count; i++)
            {
                if (_villageChecks[i].Checked)
                {
                    selected.Add(i + 1);
                }
            }

            if (selected.Count == 0)
            {
                selected.Add(_currentVillage);
            }

            return selected.ToArray();
        }

        private void SelectAttack(string key)
        {
            string display = key == "ElectroDragon_Attack" ? "Electro Dragon" : "Dragon";
            _attackCombo.SelectedItem = display;
            if (_attackCombo.SelectedIndex < 0)
            {
                _attackCombo.SelectedIndex = 0;
            }
        }

        private static string AttackKeyFromDisplay(string display)
        {
            return display == "Electro Dragon" || display == "Electro Dragon Attack" || display == "ElectroDragon_Attack" ? "ElectroDragon_Attack" : "Dragon_Attack";
        }

        private static string MinutesToInterval(int minutes)
        {
            return minutes switch
            {
                30 => "30 minutes",
                90 => "1.5 hours",
                120 => "2 hours",
                _ => "1 hour"
            };
        }

        private static int IntervalToMinutes(string value)
        {
            return value switch
            {
                "30 minutes" => 30,
                "1.5 hours" => 90,
                "2 hours" => 120,
                _ => 60
            };
        }

        private static int ParseInt(TextBox input, int fallback)
        {
            string cleaned = input.Text.Replace(",", "", StringComparison.Ordinal).Trim();
            if (int.TryParse(cleaned, out int value))
            {
                return value;
            }

            input.Text = fallback.ToString();
            return fallback;
        }

        private static JsonObject ReadJsonObject(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
                    if (node is JsonObject obj)
                    {
                        return obj;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONFIG] Không đọc được config {path}: {ex.Message}");
            }

            return new JsonObject();
        }

        private static void WriteJson(string path, JsonObject obj)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static JsonObject GetObject(JsonObject parent, string propertyName)
        {
            return parent[propertyName] as JsonObject ?? new JsonObject();
        }

        private static string GetString(JsonObject obj, string propertyName, string fallback)
        {
            return obj[propertyName]?.GetValue<string>() ?? fallback;
        }

        private static bool GetBool(JsonObject obj, string propertyName, bool fallback)
        {
            return obj[propertyName]?.GetValue<bool>() ?? fallback;
        }

        private static int GetInt(JsonObject obj, string propertyName, int fallback)
        {
            try
            {
                return obj[propertyName]?.GetValue<int>() ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static int[] GetSelectedVillages(JsonObject multi)
        {
            if (multi["selected_villages"] is JsonArray arr)
            {
                int[] selected = arr
                    .Select(node => node?.GetValue<int>() ?? 0)
                    .Where(village => village > 0)
                    .ToArray();
                if (selected.Length > 0)
                {
                    return selected;
                }
            }

            return new[] { 1, 2 };
        }

        private static decimal Clamp(int value, NumericUpDown input)
        {
            return Math.Min(input.Maximum, Math.Max(input.Minimum, value));
        }

        private static string ResolveTemplatePath(string file)
        {
            string basePath = Path.Combine(AppContext.BaseDirectory, "Templates", file);
            if (File.Exists(basePath))
            {
                return basePath;
            }

            return Path.Combine("Templates", file);
        }

        private static Image? LoadImage(string file, int width, int height)
        {
            string path = ResolveTemplatePath(file);
            if (!File.Exists(path))
            {
                return null;
            }

            using Image image = Image.FromFile(path);
            return ScaleImage(image, width, height);
        }

        private static Image ScaleImage(Image image, int width, int height)
        {
            if (height <= 0)
            {
                height = Math.Max(1, (int)Math.Round(image.Height * (width / (double)image.Width)));
            }

            var bitmap = new Bitmap(width, height);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawImage(image, 0, 0, width, height);
            return bitmap;
        }

        private static Image ApplyOpacity(Image image, float opacity)
        {
            var bitmap = new Bitmap(image.Width, image.Height);
            using Graphics graphics = Graphics.FromImage(bitmap);
            var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity };
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(matrix, System.Drawing.Imaging.ColorMatrixFlag.Default, System.Drawing.Imaging.ColorAdjustType.Bitmap);
            graphics.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            image.Dispose();
            return bitmap;
        }

        private sealed class UiLogTextWriter : TextWriter
        {
            private readonly TextWriter _inner;
            private readonly Action<string> _append;
            private readonly BotControlForm _form;
            private readonly StringBuilder _lineBuffer = new();

            public UiLogTextWriter(TextWriter inner, Action<string> append, BotControlForm form)
            {
                _inner = inner;
                _append = append;
                _form = form;
            }

            public override Encoding Encoding => _inner.Encoding;

            public override void Write(char value)
            {
                if (value == '\r')
                {
                    return;
                }

                if (value == '\n')
                {
                    FlushLine();
                    return;
                }

                _lineBuffer.Append(value);
            }

            public override void Write(string? value)
            {
                if (value == null)
                {
                    return;
                }

                foreach (char ch in value)
                {
                    if (ch == '\r')
                    {
                        continue;
                    }

                    if (ch == '\n')
                    {
                        FlushLine();
                    }
                    else
                    {
                        _lineBuffer.Append(ch);
                    }
                }
            }

            public override void WriteLine(string? value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    _lineBuffer.Append(value);
                }

                FlushLine();
            }

            protected override void Dispose(bool disposing)
            {
                if (_lineBuffer.Length > 0)
                {
                    FlushLine();
                }

                base.Dispose(disposing);
            }

            private void FlushLine()
            {
                string line = _lineBuffer.ToString();
                _lineBuffer.Clear();

                if (_form.ShouldIgnoreLog(line))
                {
                    return;
                }

                string englishLog = _form.TranslateLogToEnglish(line);
                if (string.IsNullOrWhiteSpace(englishLog))
                {
                    return;
                }

                _inner.WriteLine(englishLog);
                _append(englishLog);
            }
        }

        private sealed class SimplicityNavListBox : Control
        {
            private int _hoverIndex = -1;
            private int _selectedIndex = -1;

            public event EventHandler? SelectedIndexChanged;
            public List<object> Items { get; } = new();
            public int ItemHeight { get; set; } = 44;

            public object? SelectedItem => _selectedIndex >= 0 && _selectedIndex < Items.Count
                ? Items[_selectedIndex]
                : null;

            public int SelectedIndex
            {
                get => _selectedIndex;
                set
                {
                    int next = value >= 0 && value < Items.Count ? value : -1;
                    if (_selectedIndex == next)
                    {
                        return;
                    }

                    _selectedIndex = next;
                    Invalidate();
                    SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            public SimplicityNavListBox()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.SupportsTransparentBackColor |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.AllPaintingInWmPaint,
                    true);
                BackColor = Color.Transparent;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int index = e.Y / ItemHeight;
                if (index < 0 || index >= Items.Count)
                {
                    index = -1;
                }

                if (index != _hoverIndex)
                {
                    _hoverIndex = index;
                    Invalidate();
                }

                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hoverIndex = -1;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                int index = e.Y / ItemHeight;
                if (index >= 0 && index < Items.Count)
                {
                    SelectedIndex = index;
                }

                base.OnMouseDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Color.Transparent);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                for (int i = 0; i < Items.Count; i++)
                {
                    var bounds = new Rectangle(0, i * ItemHeight, Width, ItemHeight);
                    bool selected = i == _selectedIndex;
                    bool hovered = i == _hoverIndex;

                    // Background highlights matching Simplicity's QListWidget stylesheet
                    if (selected)
                    {
                        using var bg = new SolidBrush(Color.FromArgb(30, 255, 255, 255));
                        e.Graphics.FillRectangle(bg, bounds);
                    }
                    else if (hovered)
                    {
                        using var bg = new SolidBrush(Color.FromArgb(15, 255, 255, 255));
                        e.Graphics.FillRectangle(bg, bounds);
                    }

                    Color textColor;
                    Font textFont;
                    if (selected)
                    {
                        textColor = Color.White;
                        textFont = new Font(Font.FontFamily, Font.Size, FontStyle.Bold);
                    }
                    else if (hovered)
                    {
                        textColor = Color.FromArgb(220, 220, 220);
                        textFont = Font;
                    }
                    else
                    {
                        // Default nav text: #EFE2BA (CocCream) matching Simplicity
                        textColor = CocCream;
                        textFont = Font;
                    }

                    TextRenderer.DrawText(
                        e.Graphics,
                        Items[i]?.ToString() ?? string.Empty,
                        textFont,
                        new Rectangle(bounds.Left + 10, bounds.Top, bounds.Width - 14, bounds.Height),
                        textColor,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            int size = radius * 2;
            if (size > rect.Width) size = rect.Width;
            if (size > rect.Height) size = rect.Height;

            path.AddArc(rect.X, rect.Y, size, size, 180, 90);
            path.AddArc(rect.Right - size, rect.Y, size, size, 270, 90);
            path.AddArc(rect.Right - size, rect.Bottom - size, size, size, 0, 90);
            path.AddArc(rect.X, rect.Bottom - size, size, size, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color GetRealBackColor(Control control)
        {
            Control? curr = control;
            while (curr != null)
            {
                if (curr.BackColor != Color.Transparent && !curr.BackColor.IsEmpty)
                {
                    return curr.BackColor;
                }
                curr = curr.Parent;
            }
            return AppBack;
        }

        private sealed class RawContentPanel : Panel
        {
            public RawContentPanel()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }
        }

        private sealed class SimplicityGroupBox : GroupBox
        {
            public SimplicityGroupBox()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.SupportsTransparentBackColor |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer,
                    true);
                DoubleBuffered = true;
                BackColor = Color.Transparent;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                InvokePaintBackground(this, e);

                string upperText = Text.ToUpperInvariant();
                Size textSize = TextRenderer.MeasureText(upperText, Font);

                var borderRect = new Rectangle(0, textSize.Height / 2, Width - 1, Height - textSize.Height / 2 - 1);
                using (var path = GetRoundedPath(borderRect, 4))
                {
                    using (var brush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                    {
                        e.Graphics.FillPath(brush, path);
                    }

                    // Exclude the text area from the drawing clip region so the border line doesn't intersect the text
                    var textRect = new Rectangle(16, 0, textSize.Width + 8, textSize.Height);
                    e.Graphics.ExcludeClip(textRect);

                    Color borderColor = CocCream;
                    using (var pen = new Pen(borderColor, 1))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }

                    // Reset clip region so we can draw the text cleanly
                    e.Graphics.ResetClip();

                    var textDrawRect = new Rectangle(20, 0, textSize.Width, textSize.Height);
                    TextRenderer.DrawText(
                        e.Graphics,
                        upperText,
                        Font,
                        textDrawRect,
                        CocCream,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                }
            }
        }

        private sealed class CardPanel : Panel
        {
            public CardPanel()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(GetRealBackColor(this));

                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = GetRoundedPath(rect, 4))
                {
                    using (var brush = new SolidBrush(Surface))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                    using (var pen = new Pen(Border, 1))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }
        }

        private sealed class CardTablePanel : TableLayoutPanel
        {
            public CardTablePanel()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(GetRealBackColor(this));

                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = GetRoundedPath(rect, 4))
                {
                    using (var brush = new SolidBrush(Surface))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                    using (var pen = new Pen(Border, 1))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }
        }

        private sealed class RawInputContainer : Panel
        {
            private readonly Control _input;
            private bool _focused;
            private bool _hovered;

            public RawInputContainer(Control input)
            {
                _input = input;
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

                BackColor = Color.Transparent;
                Padding = new Padding(8, 4, 8, 4);

                Controls.Add(input);

                input.GotFocus += (s, e) => { _focused = true; Invalidate(); UpdateColors(); };
                input.LostFocus += (s, e) => { _focused = false; Invalidate(); UpdateColors(); };
                input.MouseEnter += (s, e) => { _hovered = true; Invalidate(); UpdateColors(); };
                input.MouseLeave += (s, e) => { _hovered = false; Invalidate(); UpdateColors(); };
                input.EnabledChanged += (s, e) => { Invalidate(); UpdateColors(); };

                SizeChanged += (s, e) => { LayoutInput(); };
                LayoutInput();
                UpdateColors();
            }

            private void LayoutInput()
            {
                int inputY = (Height - _input.Height) / 2;
                _input.Location = new Point(6, inputY);
                _input.Width = Width - 12;
            }

            private void UpdateColors()
            {
                Color backColor = !_input.Enabled ? DisabledSurface : Color.FromArgb(200, 200, 200);
                _input.BackColor = backColor;
                _input.ForeColor = !_input.Enabled ? TextMuted : Color.Black;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                InvokePaintBackground(this, e);

                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = GetRoundedPath(rect, 4))
                {
                    Color backColor = !_input.Enabled ? DisabledSurface : Color.FromArgb(200, 200, 200);
                    using (var brush = new SolidBrush(backColor))
                    {
                        e.Graphics.FillPath(brush, path);
                    }

                    Color borderColor = !_input.Enabled ? DisabledBorder : (_focused ? PrimaryOrange : (_hovered ? BorderStrong : Color.FromArgb(100, 100, 100)));
                    using (var pen = new Pen(borderColor, _focused ? 1.5f : 1f))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }
        }

        private sealed class RawCheckBox : CheckBox
        {
            private bool _hovered;

            public RawCheckBox()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.SupportsTransparentBackColor |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer,
                    true);
                DoubleBuffered = true;
                BackColor = Color.Transparent;
                FlatStyle = FlatStyle.Flat;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                _hovered = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hovered = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                InvokePaintBackground(this, e);

                int boxSize = 16;
                int y = (Height - boxSize) / 2;

                var boxRect = new Rectangle(0, y, boxSize, boxSize);
                // Standard square checkbox like Simplicity
                Color backColor = !Enabled ? DisabledSurface : (Checked ? Color.FromArgb(50, 50, 50) : Color.FromArgb(40, 40, 40));
                using (var brush = new SolidBrush(backColor))
                {
                    e.Graphics.FillRectangle(brush, boxRect);
                }

                Color borderColor = !Enabled ? DisabledBorder : (_hovered ? BorderStrong : Color.FromArgb(100, 100, 100));
                using (var pen = new Pen(borderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, boxRect);
                }

                if (Checked)
                {
                    using var pen = new Pen(Color.White, 2f);
                    e.Graphics.DrawLine(pen, 3, y + 8, 6, y + 12);
                    e.Graphics.DrawLine(pen, 6, y + 12, 13, y + 4);
                }

                string text = Text;
                if (!string.IsNullOrEmpty(text))
                {
                    int textX = boxSize + 8;
                    var textRect = new Rectangle(textX, 0, Width - textX, Height);
                    TextRenderer.DrawText(
                        e.Graphics,
                        text,
                        Font,
                        textRect,
                        Enabled ? TextMain : TextMuted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private sealed class RawStatusChip : Label
        {
            public RawStatusChip()
            {
                AutoSize = false;
                Size = new Size(110, 28);
                Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                TextAlign = ContentAlignment.MiddleCenter;
                DoubleBuffered = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(GetRealBackColor(this));

                string text = Text.ToUpperInvariant();
                Color backColor = Border;
                Color foreColor = TextMain;

                if (text == "RUNNING" || text == "ACTIVE")
                {
                    backColor = Color.FromArgb(159, 201, 162);
                    foreColor = Color.FromArgb(15, 15, 20);
                }
                else if (text == "PAUSED")
                {
                    backColor = Color.FromArgb(223, 168, 143);
                    foreColor = Color.FromArgb(15, 15, 20);
                }
                else if (text == "IDLE")
                {
                    backColor = Color.FromArgb(192, 133, 50);
                    foreColor = Color.White;
                }
                else if (text == "ERROR" || text == "STOPPED")
                {
                    backColor = Color.FromArgb(207, 45, 86);
                    foreColor = Color.White;
                }

                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = GetRoundedPath(rect, Height / 2))
                {
                    using (var brush = new SolidBrush(backColor))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    Font,
                    ClientRectangle,
                    foreColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

    }

    internal static class ControlPlacementExtensions
    {
        public static Control At(this Control control, int x, int y)
        {
            control.Location = new Point(x, y);
            return control;
        }
    }
}
