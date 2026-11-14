using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using OptiFinish.Core;
using OptiFinish.UI;

namespace OptiFinish
{
    /// <summary>
    /// Hosts the 3 authentic FinishLynx Event Window zones (Information Zone, Results Zone, Image Zone)
    /// with support for all 6 Window Layouts, FinishLynx toolbars, interactive header popups,
    /// IdentiLynx 2D video sync, and dual camera slit-scan inspection.
    /// </summary>
    public sealed class EventWindowControl : UserControl
    {
        private RaceEventDocument _doc;
        private SplitContainer _splitOuter;
        private SplitContainer _splitInner;

        // Zone Panels
        private Panel _zoneInfo;
        private Panel _zoneResults;
        private Panel _zoneImage;

        // Zone A: Information Zone
        private LynxIconButton _btnInfoCamSettings, _btnInfoAdvCap, _btnInfoArmBox, _btnInfoCapBox;
        private Label _lblBadgeS, _lblBadgeL;
        private DataGridView _dgvInfo;
        private bool _updatingInfoGrid;

        // Zone B: Results Zone
        private FlowLayoutPanel _rzToolbar;
        private readonly List<LynxIconButton> _rzButtons = new List<LynxIconButton>();
        private DataGridView _dgvResults;
        private bool _updatingResultsGrid;
        private int _sortColumnMode; // 0=Default, 1=Asc, 2=Desc
        private string _sortFieldKey = "Place";

        // Zone C: Image Zone (Camera 1 & Camera 2 panes)
        private SplitContainer _splitCameras;
        private Panel _paneCam1, _paneCam2;
        private Panel _barCam1, _barCam2;
        private SlitScanViewportControl _vp1, _vp2;
        private int _activeCamIdx = 0;

        // Per-camera interactive bar widgets
        private TextBox[] _txtFinishTime = new TextBox[2];
        private TextBox[] _txtEvalId = new TextBox[2];
        private LynxIconButton[] _btnIdentify = new LynxIconButton[2];
        private LynxIconButton[] _btnZoomOut = new LynxIconButton[2];
        private LynxIconButton[] _btnZoomIn = new LynxIconButton[2];
        private ComboBox[] _cbZoom = new ComboBox[2];
        private TextBox[] _txtRate = new TextBox[2];
        private LynxIconButton[] _btnOrient = new LynxIconButton[2];
        private LynxIconButton[] _btnCrop = new LynxIconButton[2];
        private LynxIconButton[] _btnUncrop = new LynxIconButton[2];
        private LynxIconButton[] _btnAutocrop = new LynxIconButton[2];
        private LynxIconButton[] _btnScroll = new LynxIconButton[2];
        private LynxIconButton[] _btnFindNext = new LynxIconButton[2];
        private LynxIconButton[] _btnEnhance = new LynxIconButton[2];
        private LynxIconButton[] _btnQuickAdj = new LynxIconButton[2];
        private LynxIconButton[] _btnPrintImg = new LynxIconButton[2];
        private LynxIconButton[] _btnPrintBmp = new LynxIconButton[2];
        private LynxIconButton[] _btnMaximize = new LynxIconButton[2];
        private Label[] _lblCamName = new Label[2];

        // IdentiLynx Video bar widgets (shown on video camera panes)
        private TextBox[] _txtVideoTime = new TextBox[2];
        private LynxIconButton[] _btnVidPlay = new LynxIconButton[2];
        private LynxIconButton[] _btnVidPrev = new LynxIconButton[2];
        private LynxIconButton[] _btnVidNext = new LynxIconButton[2];
        private ComboBox[] _cbVidSpeed = new ComboBox[2];
        private LynxIconButton[] _btnVidTrack = new LynxIconButton[2];
        private TrackBar[] _tbVidSlider = new TrackBar[2];
        private Timer _videoTimer;
        private bool _suppressVideoSlider;
        private int _playingVideoCam = -1;

        // Host Command Events (wired by MainWindow)
        public event Action<string> StatusHintChanged;
        public event EventHandler<EventArgs> DocumentModified;
        public event EventHandler<EventArgs> OpenCameraSettingsRequested;
        public event EventHandler<EventArgs> OpenImageAdjustmentsRequested;
        public event EventHandler<EventArgs> OpenFiltersRequested;
        public event EventHandler<EventArgs> OpenPackLeadersRequested;
        public event EventHandler<EventArgs> OpenFindIdRequested;
        public event EventHandler<EventArgs> OpenGotoScheduleRequested;
        public event EventHandler<EventArgs> PrevScheduleRequested;
        public event EventHandler<EventArgs> NextScheduleRequested;
        public event EventHandler<EventArgs> ImportReacTimeRequested;
        public event EventHandler<EventArgs> PrintResultsRequested;
        public event EventHandler<EventArgs> PrintImageRequested;
        public event EventHandler<EventArgs> PrintBitmapRequested;
        public event EventHandler<EventArgs> ExportSelectionRequested;
        public event EventHandler<EventArgs> ExportVideoRequested;
        public event EventHandler<EventArgs> SetScoreboardImageRequested;
        public event EventHandler<EventArgs> ManualWindRequested;
        public event EventHandler<EventArgs> ManualStartRequested;
        public event EventHandler<EventArgs> ArmToggleRequested;
        public event EventHandler<EventArgs> CaptureToggleRequested;

        public RaceEventDocument Document { get { return _doc; } }
        public int ActiveCameraIndex { get { return _activeCamIdx; } }
        public SlitScanViewportControl ActiveViewport { get { return _activeCamIdx == 1 ? _vp2 : _vp1; } }
        public SlitScanViewportControl Viewport1 { get { return _vp1; } }
        public SlitScanViewportControl Viewport2 { get { return _vp2; } }

        public EventWindowControl()
        {
            BackColor = SystemColors.Control;
            Font = new Font("Segoe UI", 8.5f);
            BuildUi();
            _videoTimer = new Timer { Interval = 45 };
            _videoTimer.Tick += OnVideoTimerTick;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _videoTimer != null)
            {
                _videoTimer.Stop();
                _videoTimer.Dispose();
                _videoTimer = null;
            }
            base.Dispose(disposing);
        }

        private void EmitHint(string hint)
        {
            if (StatusHintChanged != null) StatusHintChanged(hint ?? "");
        }

        private void NotifyModified()
        {
            if (DocumentModified != null) DocumentModified(this, EventArgs.Empty);
        }

        private LynxIconButton MkIconBtn(string key, int w, int h, string hint, Action onClick)
        {
            LynxIconButton b = new LynxIconButton(key, w, h, hint);
            b.StatusHintHover += EmitHint;
            b.Click += delegate { if (onClick != null) onClick(); };
            return b;
        }

        private void BuildUi()
        {
            _zoneInfo = BuildInformationZone();
            _zoneResults = BuildResultsZone();
            _zoneImage = BuildImageZone();

            _splitInner = new SplitContainer
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                SplitterWidth = 4,
                BackColor = SystemColors.ControlDark
            };
            _splitOuter = new SplitContainer
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                SplitterWidth = 4,
                BackColor = SystemColors.ControlDark
            };
            Controls.Add(_splitOuter);
            ApplyLayoutPreset(LynxConfigService.Instance.LayoutSelection);
        }

        public void ApplyLayoutPreset(int layout)
        {
            _splitOuter.Panel1.Controls.Clear();
            _splitOuter.Panel2.Controls.Clear();
            _splitInner.Panel1.Controls.Clear();
            _splitInner.Panel2.Controls.Clear();

            switch (layout)
            {
                case 1: // Top Narrow Info + Wide Results, Bottom Image
                    _splitOuter.Orientation = Orientation.Horizontal;
                    _splitInner.Orientation = Orientation.Vertical;
                    _splitInner.Panel1.Controls.Add(_zoneInfo);
                    _splitInner.Panel2.Controls.Add(_zoneResults);
                    _splitOuter.Panel1.Controls.Add(_splitInner);
                    _splitOuter.Panel2.Controls.Add(_zoneImage);
                    SafeSplitter(_splitOuter, 0.30);
                    SafeSplitter(_splitInner, 0.30);
                    break;
                case 2: // Left Image, Right Top Info + Bottom Results
                    _splitOuter.Orientation = Orientation.Vertical;
                    _splitInner.Orientation = Orientation.Horizontal;
                    _splitInner.Panel1.Controls.Add(_zoneInfo);
                    _splitInner.Panel2.Controls.Add(_zoneResults);
                    _splitOuter.Panel1.Controls.Add(_zoneImage);
                    _splitOuter.Panel2.Controls.Add(_splitInner);
                    SafeSplitter(_splitOuter, 0.56);
                    SafeSplitter(_splitInner, 0.44);
                    break;
                case 3: // Left Image, Right Top Results + Bottom Info
                    _splitOuter.Orientation = Orientation.Vertical;
                    _splitInner.Orientation = Orientation.Horizontal;
                    _splitInner.Panel1.Controls.Add(_zoneResults);
                    _splitInner.Panel2.Controls.Add(_zoneInfo);
                    _splitOuter.Panel1.Controls.Add(_zoneImage);
                    _splitOuter.Panel2.Controls.Add(_splitInner);
                    SafeSplitter(_splitOuter, 0.56);
                    SafeSplitter(_splitInner, 0.56);
                    break;
                case 4: // Left Top Info + Bottom Results, Right Image
                    _splitOuter.Orientation = Orientation.Vertical;
                    _splitInner.Orientation = Orientation.Horizontal;
                    _splitInner.Panel1.Controls.Add(_zoneInfo);
                    _splitInner.Panel2.Controls.Add(_zoneResults);
                    _splitOuter.Panel1.Controls.Add(_splitInner);
                    _splitOuter.Panel2.Controls.Add(_zoneImage);
                    SafeSplitter(_splitOuter, 0.44);
                    SafeSplitter(_splitInner, 0.44);
                    break;
                case 5: // Left Top Results + Bottom Info, Right Image
                    _splitOuter.Orientation = Orientation.Vertical;
                    _splitInner.Orientation = Orientation.Horizontal;
                    _splitInner.Panel1.Controls.Add(_zoneResults);
                    _splitInner.Panel2.Controls.Add(_zoneInfo);
                    _splitOuter.Panel1.Controls.Add(_splitInner);
                    _splitOuter.Panel2.Controls.Add(_zoneImage);
                    SafeSplitter(_splitOuter, 0.44);
                    SafeSplitter(_splitInner, 0.56);
                    break;
                default: // Layout 0: Classic FinishLynx (Top: Info Left 35% + Results Right 65%, Bottom: Image 100%)
                    _splitOuter.Orientation = Orientation.Horizontal;
                    _splitInner.Orientation = Orientation.Vertical;
                    _splitInner.Panel1.Controls.Add(_zoneInfo);
                    _splitInner.Panel2.Controls.Add(_zoneResults);
                    _splitOuter.Panel1.Controls.Add(_splitInner);
                    _splitOuter.Panel2.Controls.Add(_zoneImage);
                    SafeSplitter(_splitOuter, 0.36);
                    SafeSplitter(_splitInner, 0.36);
                    break;
            }
        }

        private static void SafeSplitter(SplitContainer sc, double ratio)
        {
            try
            {
                int total = sc.Orientation == Orientation.Horizontal ? Math.Max(200, sc.Height) : Math.Max(200, sc.Width);
                int dist = (int)Math.Round(total * ratio);
                if (dist >= 40 && dist <= total - 40) sc.SplitterDistance = dist;
            }
            catch { }
        }

        // ===================== ZONE A: INFORMATION ZONE =====================
        private Panel BuildInformationZone()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };

            Panel topBar = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = SystemColors.Control };
            _btnInfoCamSettings = MkIconBtn("CameraSettings", 22, 22, "Camera Settings (Get Info...)", delegate
            {
                if (OpenCameraSettingsRequested != null) OpenCameraSettingsRequested(this, EventArgs.Empty);
            });
            _btnInfoCamSettings.Location = new Point(2, 2);

            _btnInfoAdvCap = MkIconBtn("AdvCapture", 22, 22, "Advance Capture Split Location", delegate { AdvanceCaptureSplit(); });
            _btnInfoAdvCap.Location = new Point(26, 2);

            _btnInfoArmBox = MkIconBtn("ArmBox", 22, 22, "Armed Indicator (Click to Arm/Disarm Event)", delegate
            {
                if (ArmToggleRequested != null) ArmToggleRequested(this, EventArgs.Empty);
            });
            _btnInfoArmBox.Location = new Point(54, 2);

            _btnInfoCapBox = MkIconBtn("CaptureBox", 22, 22, "Capture Indicator (Click to Toggle Capture)", delegate
            {
                if (CaptureToggleRequested != null) CaptureToggleRequested(this, EventArgs.Empty);
            });
            _btnInfoCapBox.Location = new Point(78, 2);

            _lblBadgeS = new Label
            {
                Text = "s",
                Location = new Point(106, 4),
                Size = new Size(18, 18),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(224, 242, 254),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold)
            };
            _lblBadgeL = new Label
            {
                Text = "l",
                Location = new Point(126, 4),
                Size = new Size(18, 18),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(220, 252, 231),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold)
            };

            topBar.Controls.AddRange(new Control[] { _btnInfoCamSettings, _btnInfoAdvCap, _btnInfoArmBox, _btnInfoCapBox, _lblBadgeS, _lblBadgeL });

            _dgvInfo = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = SystemColors.ControlLight,
                BorderStyle = BorderStyle.Fixed3D,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersHeight = 20,
                RowTemplate = { Height = 19 },
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(190, 190, 190),
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            };
            _dgvInfo.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            _dgvInfo.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
            _dgvInfo.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.2f);
            _dgvInfo.DefaultCellStyle.BackColor = Color.FromArgb(242, 242, 240);
            _dgvInfo.DefaultCellStyle.SelectionBackColor = Color.White;
            _dgvInfo.DefaultCellStyle.SelectionForeColor = Color.Black;

            int[] infoW = new int[] { 20, 92, 68, 56, 76, 86, 56 };
            string[] infoHdrs = new string[] { "", "Event", "Event #", "Round", "Heat", "", "" };
            for (int i = 0; i < infoHdrs.Length; i++)
            {
                _dgvInfo.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = infoHdrs[i],
                    Width = infoW[i],
                    SortMode = DataGridViewColumnSortMode.NotSortable
                });
            }

            _dgvInfo.SelectionChanged += OnInfoRowSelected;
            _dgvInfo.CellClick += OnInfoCellClick;
            _dgvInfo.CellDoubleClick += OnInfoCellDoubleClick;
            _dgvInfo.CellEndEdit += OnInfoCellEndEdit;

            p.Controls.Add(_dgvInfo);
            p.Controls.Add(topBar);
            return p;
        }

        private void OnInfoRowSelected(object sender, EventArgs e)
        {
            if (_updatingInfoGrid || _dgvInfo.SelectedRows.Count == 0) return;
            string tag = _dgvInfo.SelectedRows[0].Tag as string ?? "";
            string[] h;
            if (tag == "EVENT") h = new string[] { "", Loc.Opt("Event\\Title", "Event"), Loc.Evt("EventNumber", "Event #"), Loc.Evt("Round", "Round"), Loc.Evt("Heat", "Heat"), Loc.Evt("Results\\Fields\\Distance", "Distance"), "" };
            else if (tag == "START") h = new string[] { "", Loc.Evt("Start\\Fields\\Name", "Start"), Loc.Evt("Start\\Fields\\Time", "Time"), Loc.Evt("Start\\Dialog\\Offset", "Offset").TrimEnd(':'), "Source", "", "" };
            else if (tag == "WIND") h = new string[] { "", Loc.Evt("Wind\\Fields\\Name", "Wind"), Loc.Evt("Wind\\Fields\\Reading", "Reading"), Loc.Evt("Wind\\Fields\\Units", "Units"), Loc.Evt("Wind\\Fields\\Mode", "Mode (s, dur)"), "", "" };
            else if (tag == "LAPTIME") h = new string[] { "", Loc.Opt("LapTime\\Title", "LapTime"), "Total Laps", "Laps/Split", "Too Fast", "Too Slow", "" };
            else if (tag.StartsWith("SPLIT:")) h = new string[] { "", "Split", "Start", "Duration", "", "", "" };
            else if (tag.StartsWith("CAM:")) h = new string[] { "", "Camera", "Rate", "Height", "Mode", "Gain", "Orientation" };
            else h = new string[] { "", Loc.Opt("Results\\Title", "Results"), Loc.Menu("Image\\Precision\\Title", "Precision"), Loc.Evt("Results\\Fields\\DeltaTime", "Delta"), Loc.Evt("Results\\Fields\\Speed", "Speed"), "", "" };

            for (int i = 0; i < Math.Min(_dgvInfo.Columns.Count, h.Length); i++)
            {
                _dgvInfo.Columns[i].HeaderText = h[i];
            }
        }

        private void SafeDefer(Action act)
        {
            if (act == null || IsDisposed) return;
            if (IsHandleCreated)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!IsDisposed) act();
                    });
                    return;
                }
                catch { }
            }
            act();
        }

        private void OnInfoCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_doc == null || e.RowIndex < 0) return;
            string tag = _dgvInfo.Rows[e.RowIndex].Tag as string ?? "";

            if (tag == "WIND" && (e.ColumnIndex == 3 || e.ColumnIndex == 4))
            {
                SafeDefer(delegate
                {
                    if (ManualWindRequested != null) ManualWindRequested(this, EventArgs.Empty);
                });
                return;
            }
            if (tag == "START" && (e.ColumnIndex >= 1))
            {
                SafeDefer(delegate
                {
                    if (ManualStartRequested != null) ManualStartRequested(this, EventArgs.Empty);
                });
                return;
            }
            if (tag.StartsWith("CAM:"))
            {
                int ci;
                if (int.TryParse(tag.Substring(4), out ci) && ci >= 0 && ci < _doc.Cameras.Count)
                {
                    _activeCamIdx = ci;
                    if (e.ColumnIndex == 0)
                    {
                        _doc.Cameras[ci].IsEnabled = !_doc.Cameras[ci].IsEnabled;
                        SafeDefer(delegate { RefreshInformationGrid(); });
                    }
                    else if (e.ColumnIndex == 6)
                    {
                        _doc.Cameras[ci].Orientation = _doc.Cameras[ci].Orientation == StripOrientation.LeftToRight
                            ? StripOrientation.RightToLeft : StripOrientation.LeftToRight;
                        SafeDefer(delegate
                        {
                            RefreshInformationGrid();
                            InvalidateViewports();
                        });
                    }
                }
            }
        }

        private void OnInfoCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_doc == null || e.RowIndex < 0) return;
            string tag = _dgvInfo.Rows[e.RowIndex].Tag as string ?? "";
            if (tag.StartsWith("CAM:"))
            {
                int ci;
                if (int.TryParse(tag.Substring(4), out ci)) _activeCamIdx = ci;
                SafeDefer(delegate
                {
                    if (OpenCameraSettingsRequested != null) OpenCameraSettingsRequested(this, EventArgs.Empty);
                });
            }
        }

        private void OnInfoCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_doc == null || _updatingInfoGrid || e.RowIndex < 0) return;
            string tag = _dgvInfo.Rows[e.RowIndex].Tag as string ?? "";
            object valObj = _dgvInfo.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            string val = valObj != null ? valObj.ToString().Trim() : "";
            int iv;
            double dv;

            if (tag == "EVENT")
            {
                if (e.ColumnIndex == 1 && !string.IsNullOrEmpty(val)) _doc.EventName = val;
                else if (e.ColumnIndex == 2 && int.TryParse(val, out iv)) _doc.EventNumber = iv;
                else if (e.ColumnIndex == 3 && int.TryParse(val, out iv)) _doc.RoundNumber = iv;
                else if (e.ColumnIndex == 4 && int.TryParse(val, out iv)) _doc.HeatNumber = iv;
                else if (e.ColumnIndex == 5 && double.TryParse(val.Replace("m", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) _doc.DistanceMeters = dv;
                SafeDefer(delegate
                {
                    RefreshAll();
                    NotifyModified();
                });
            }
            else if (tag == "WIND" && e.ColumnIndex == 2)
            {
                if (double.TryParse(val.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv))
                {
                    _doc.WindReading = dv;
                    _doc.HasWindReading = true;
                }
                else
                {
                    _doc.HasWindReading = false;
                }
                SafeDefer(delegate
                {
                    RefreshInformationGrid();
                    NotifyModified();
                });
            }
            else if (tag == "LAPTIME")
            {
                if (e.ColumnIndex == 2 && int.TryParse(val, out iv)) _doc.TotalLaps = Math.Max(1, iv);
                else if (e.ColumnIndex == 3 && int.TryParse(val, out iv)) _doc.LapsPerSplit = Math.Max(1, iv);
                else if (e.ColumnIndex == 4 && double.TryParse(val.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) _doc.MinValidSplitSeconds = dv;
                else if (e.ColumnIndex == 5 && double.TryParse(val.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) _doc.MaxValidSplitSeconds = dv;
                SafeDefer(delegate
                {
                    NotifyModified();
                });
            }
            else if (tag.StartsWith("CAM:"))
            {
                int ci;
                if (int.TryParse(tag.Substring(4), out ci) && ci >= 0 && ci < _doc.Cameras.Count)
                {
                    SlitCameraStream cam = _doc.Cameras[ci];
                    if (e.ColumnIndex == 2 && int.TryParse(val, out iv) && iv >= 10) cam.SampleRateHz = iv;
                    else if (e.ColumnIndex == 3 && int.TryParse(val, out iv) && iv >= 60) cam.FrameHeightPx = iv;
                    else if (e.ColumnIndex == 5 && int.TryParse(val, out iv)) cam.GainPercent = iv;
                    SafeDefer(delegate
                    {
                        RefreshCameraBars();
                        InvalidateViewports();
                        NotifyModified();
                    });
                }
            }
        }

        public void AdvanceCaptureSplit()
        {
            if (_doc == null || _doc.SplitLocations.Count <= 1) return;
            _doc.ActiveCaptureSplitIndex = (_doc.ActiveCaptureSplitIndex + 1) % _doc.SplitLocations.Count;
            RefreshInformationGrid();
        }

        // ===================== ZONE B: RESULTS ZONE =====================
        private Panel BuildResultsZone()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
            _rzToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 26,
                WrapContents = false,
                AutoScroll = true,
                BackColor = SystemColors.Control,
                Padding = new Padding(2, 2, 2, 0)
            };
            RebuildResultsToolbar();

            _dgvResults = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(228, 228, 224),
                BorderStyle = BorderStyle.Fixed3D,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                ColumnHeadersHeight = 21,
                RowTemplate = { Height = 19 },
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(195, 195, 195),
                EditMode = DataGridViewEditMode.EditOnF2
            };
            _dgvResults.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            _dgvResults.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
            _dgvResults.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.2f);
            _dgvResults.DefaultCellStyle.BackColor = Color.White;
            _dgvResults.DefaultCellStyle.SelectionBackColor = Color.White;
            _dgvResults.DefaultCellStyle.SelectionForeColor = Color.Black;
            _dgvResults.DefaultCellStyle.Font = new Font("Segoe UI", 8.5f);

            _dgvResults.SelectionChanged += OnResultSelectionChanged;
            _dgvResults.CellEndEdit += OnResultCellEndEdit;
            _dgvResults.ColumnHeaderMouseClick += OnResultHeaderClick;
            _dgvResults.CellMouseClick += OnResultCellMouseClick;

            p.Controls.Add(_dgvResults);
            p.Controls.Add(_rzToolbar);
            return p;
        }

        public void RebuildResultsToolbar()
        {
            _rzToolbar.Controls.Clear();
            _rzButtons.Clear();
            bool[] vis = LynxExtendedConfig.Instance.ResultsToolbarVisible;

            var specs = new[]
            {
                new { Idx = 0,  Key = "NewResult",    Hint = "New Result",                  Act = (Action)AddNewResultRow },
                new { Idx = 1,  Key = "DeleteResult", Hint = "Delete Selected Result",      Act = (Action)DeleteSelectedResultRow },
                new { Idx = 2,  Key = "FindID",       Hint = "Find Competitor ID...",       Act = (Action)delegate { if (OpenFindIdRequested != null) OpenFindIdRequested(this, EventArgs.Empty); } },
                new { Idx = 3,  Key = "Filters",      Hint = "Results Filters...",          Act = (Action)delegate { if (OpenFiltersRequested != null) OpenFiltersRequested(this, EventArgs.Empty); } },
                new { Idx = 4,  Key = "PackLeader",   Hint = "Set Pack Leaders",            Act = (Action)delegate { if (OpenPackLeadersRequested != null) OpenPackLeadersRequested(this, EventArgs.Empty); } },
                new { Idx = 5,  Key = "PrevEvent",    Hint = "Previous Scheduled Event",    Act = (Action)delegate { if (PrevScheduleRequested != null) PrevScheduleRequested(this, EventArgs.Empty); } },
                new { Idx = 6,  Key = "GoToEvent",    Hint = "Goto Scheduled Event...",     Act = (Action)delegate { if (OpenGotoScheduleRequested != null) OpenGotoScheduleRequested(this, EventArgs.Empty); } },
                new { Idx = 7,  Key = "NextEvent",    Hint = "Next Scheduled Event",        Act = (Action)delegate { if (NextScheduleRequested != null) NextScheduleRequested(this, EventArgs.Empty); } },
                new { Idx = 8,  Key = "ReacTime",     Hint = "Import ReacTime Data...",     Act = (Action)delegate { if (ImportReacTimeRequested != null) ImportReacTimeRequested(this, EventArgs.Empty); } },
                new { Idx = 9,  Key = "Print",        Hint = "Print Results",               Act = (Action)delegate { if (PrintResultsRequested != null) PrintResultsRequested(this, EventArgs.Empty); } },
                new { Idx = 10, Key = "AcceptSplit",  Hint = "Accept Split Time",           Act = (Action)delegate { AcceptSelectedSplit(); } },
                new { Idx = 11, Key = "InsertSplit",  Hint = "Insert Split Time",           Act = (Action)delegate { InsertSplitForSelected(); } },
                new { Idx = 12, Key = "DeleteSplit",  Hint = "Mask / Remove Last Split",    Act = (Action)delegate { MaskSelectedSplit(); } }
            };

            for (int i = 0; i < specs.Length; i++)
            {
                if (i == 10)
                {
                    Panel sep = new Panel { Size = new Size(8, 22), Margin = new Padding(0) };
                    _rzToolbar.Controls.Add(sep);
                }
                if (i < vis.Length && !vis[i]) continue;
                var sp = specs[i];
                LynxIconButton btn = MkIconBtn(sp.Key, 22, 22, sp.Hint, sp.Act);
                btn.Margin = new Padding(1, 0, 1, 0);
                _rzToolbar.Controls.Add(btn);
                _rzButtons.Add(btn);
            }
        }

        // ===================== ZONE C: IMAGE ZONE =====================
        private Panel BuildImageZone()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
            _splitCameras = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 4,
                BackColor = SystemColors.ControlDark
            };

            _vp1 = new SlitScanViewportControl { Dock = DockStyle.Fill, CameraIndex = 0 };
            _vp2 = new SlitScanViewportControl { Dock = DockStyle.Fill, CameraIndex = 1 };
            _vp1.HairlineMoved += OnViewportHairlineMoved;
            _vp2.HairlineMoved += OnViewportHairlineMoved;
            _vp1.QuickEvaluateRequested += OnViewportQuickEvaluate;
            _vp2.QuickEvaluateRequested += OnViewportQuickEvaluate;
            _vp1.SetZeroOriginRequested += OnViewportSetZeroOrigin;
            _vp2.SetZeroOriginRequested += OnViewportSetZeroOrigin;
            _vp1.SelectionActionCompleted += delegate { RefreshCameraBars(); if (DocumentModified != null) DocumentModified(this, EventArgs.Empty); };
            _vp2.SelectionActionCompleted += delegate { RefreshCameraBars(); if (DocumentModified != null) DocumentModified(this, EventArgs.Empty); };
            _vp1.ExportSelectionRequested += delegate { _activeCamIdx = 0; if (ExportSelectionRequested != null) ExportSelectionRequested(this, EventArgs.Empty); };
            _vp2.ExportSelectionRequested += delegate { _activeCamIdx = 1; if (ExportSelectionRequested != null) ExportSelectionRequested(this, EventArgs.Empty); };
            _vp1.ExportVideoSelectionRequested += delegate { _activeCamIdx = 0; if (ExportVideoRequested != null) ExportVideoRequested(this, EventArgs.Empty); };
            _vp2.ExportVideoSelectionRequested += delegate { _activeCamIdx = 1; if (ExportVideoRequested != null) ExportVideoRequested(this, EventArgs.Empty); };
            _vp1.PrintSelectionRequested += delegate { _activeCamIdx = 0; if (PrintImageRequested != null) PrintImageRequested(this, EventArgs.Empty); };
            _vp2.PrintSelectionRequested += delegate { _activeCamIdx = 1; if (PrintImageRequested != null) PrintImageRequested(this, EventArgs.Empty); };
            _vp1.PrintBitmapSelectionRequested += delegate { _activeCamIdx = 0; if (PrintBitmapRequested != null) PrintBitmapRequested(this, EventArgs.Empty); };
            _vp2.PrintBitmapSelectionRequested += delegate { _activeCamIdx = 1; if (PrintBitmapRequested != null) PrintBitmapRequested(this, EventArgs.Empty); };
            _vp1.SetScoreboardImageRequested += delegate { _activeCamIdx = 0; if (SetScoreboardImageRequested != null) SetScoreboardImageRequested(this, EventArgs.Empty); };
            _vp2.SetScoreboardImageRequested += delegate { _activeCamIdx = 1; if (SetScoreboardImageRequested != null) SetScoreboardImageRequested(this, EventArgs.Empty); };
            _vp1.OpenAdjustmentsRequested += delegate { _activeCamIdx = 0; if (OpenImageAdjustmentsRequested != null) OpenImageAdjustmentsRequested(this, EventArgs.Empty); };
            _vp2.OpenAdjustmentsRequested += delegate { _activeCamIdx = 1; if (OpenImageAdjustmentsRequested != null) OpenImageAdjustmentsRequested(this, EventArgs.Empty); };
            _vp1.Enter += delegate { _activeCamIdx = 0; };
            _vp2.Enter += delegate { _activeCamIdx = 1; };

            _paneCam1 = BuildCameraPane(0, _vp1, out _barCam1);
            _paneCam2 = BuildCameraPane(1, _vp2, out _barCam2);

            _splitCameras.Panel1.Controls.Add(_paneCam1);
            _splitCameras.Panel2.Controls.Add(_paneCam2);
            p.Controls.Add(_splitCameras);
            return p;
        }

        private Panel BuildCameraPane(int camIdx, SlitScanViewportControl vp, out Panel barPanel)
        {
            Panel pane = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
            barPanel = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = SystemColors.Control, AutoScroll = false };

            FlowLayoutPanel flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                AutoScroll = false,
                BackColor = SystemColors.Control,
                Padding = new Padding(2, 2, 2, 0)
            };

            // 1. Finish Time Box (clickable -> enters result for ID in Eval box)
            TextBox tbFinish = new TextBox
            {
                Width = 46,
                Height = 20,
                ReadOnly = true,
                Text = "0.00",
                BackColor = SystemColors.Window,
                TextAlign = HorizontalAlignment.Right,
                BorderStyle = BorderStyle.Fixed3D,
                Margin = new Padding(1, 1, 2, 0),
                Cursor = Cursors.Hand
            };
            tbFinish.Click += delegate
            {
                _activeCamIdx = camIdx;
                CommitEvalBoxEntry(camIdx);
            };
            _txtFinishTime[camIdx] = tbFinish;
            flow.Controls.Add(tbFinish);

            // 2. Eval ID Input Box
            TextBox tbEval = new TextBox
            {
                Width = 28,
                Height = 20,
                Text = "",
                BackColor = SystemColors.Window,
                BorderStyle = BorderStyle.Fixed3D,
                Margin = new Padding(1, 1, 2, 0)
            };
            tbEval.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter)
                {
                    ke.SuppressKeyPress = true;
                    _activeCamIdx = camIdx;
                    CommitEvalBoxEntry(camIdx);
                }
            };
            _txtEvalId[camIdx] = tbEval;
            flow.Controls.Add(tbEval);

            // 3. Video controls (hidden unless cam.IsVideo == true)
            TextBox tbVidT = new TextBox
            {
                Width = 44,
                Height = 20,
                ReadOnly = true,
                Text = "0.00",
                TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(1, 1, 2, 0),
                Visible = false
            };
            _txtVideoTime[camIdx] = tbVidT;
            flow.Controls.Add(tbVidT);

            LynxIconButton bPlay = MkIconBtn("Play", 20, 22, "Play / Pause IdentiLynx Video", delegate { ToggleVideoPlay(camIdx); });
            bPlay.Margin = new Padding(1, 0, 1, 0); bPlay.Visible = false;
            _btnVidPlay[camIdx] = bPlay; flow.Controls.Add(bPlay);

            LynxIconButton bPrevF = MkIconBtn("PrevFrame", 20, 22, "Previous Video Frame", delegate { _activeCamIdx = camIdx; StepVideoFrame(camIdx, -1); });
            bPrevF.Margin = new Padding(1, 0, 1, 0); bPrevF.Visible = false;
            _btnVidPrev[camIdx] = bPrevF; flow.Controls.Add(bPrevF);

            LynxIconButton bNextF = MkIconBtn("NextFrame", 20, 22, "Next Video Frame", delegate { _activeCamIdx = camIdx; StepVideoFrame(camIdx, 1); });
            bNextF.Margin = new Padding(1, 0, 1, 0); bNextF.Visible = false;
            _btnVidNext[camIdx] = bNextF; flow.Controls.Add(bNextF);

            ComboBox cbSpd = new ComboBox { Width = 44, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(1, 1, 2, 0), Visible = false };
            cbSpd.Items.AddRange(new object[] { "25", "50", "100", "200", "400" });
            cbSpd.SelectedIndex = 2;
            _cbVidSpeed[camIdx] = cbSpd; flow.Controls.Add(cbSpd);

            LynxIconButton bTrack = MkIconBtn("TimeTrack", 20, 22, "Sync Video Time Tracking with Hairline", delegate { _btnVidTrack[camIdx].Checked = !_btnVidTrack[camIdx].Checked; });
            bTrack.Checked = true;
            bTrack.Margin = new Padding(1, 0, 1, 0); bTrack.Visible = false;
            _btnVidTrack[camIdx] = bTrack; flow.Controls.Add(bTrack);

            TrackBar tbSlider = new TrackBar { Width = 62, Height = 20, AutoSize = false, TickStyle = TickStyle.None, Margin = new Padding(1, 1, 2, 0), Visible = false };
            tbSlider.ValueChanged += delegate
            {
                if (_suppressVideoSlider) return;
                SlitCameraStream cs = GetCam(camIdx);
                if (cs != null && cs.IsVideo)
                {
                    (camIdx == 0 ? _vp1 : _vp2).JumpToVideoFrame(tbSlider.Value);
                    SyncVideoReadout(camIdx);
                }
            };
            _tbVidSlider[camIdx] = tbSlider; flow.Controls.Add(tbSlider);

            // 4. Standard Image Pane Toolbar Icons
            LynxIconButton bId = MkIconBtn("IdentifyLane", 22, 22, "Identify By: Lane / Id / License / Nothing", delegate { CycleIdentifyBy(camIdx); });
            bId.Margin = new Padding(1, 0, 1, 0);
            _btnIdentify[camIdx] = bId; flow.Controls.Add(bId);

            LynxIconButton bOri = MkIconBtn("Orient", 22, 22, "Toggle Image Orientation (Left / Right)", delegate { ToggleOrientation(camIdx); });
            bOri.Margin = new Padding(1, 0, 1, 0);
            _btnOrient[camIdx] = bOri; flow.Controls.Add(bOri);

            LynxIconButton bZOut = MkIconBtn("ZoomOut", 22, 22, "Zoom Out (Ctrl+-)", delegate { _activeCamIdx = camIdx; ZoomStep(-1); });
            bZOut.Margin = new Padding(1, 0, 1, 0);
            _btnZoomOut[camIdx] = bZOut; flow.Controls.Add(bZOut);

            LynxIconButton bZIn = MkIconBtn("ZoomIn", 22, 22, "Zoom In (Ctrl++)", delegate { _activeCamIdx = camIdx; ZoomStep(1); });
            bZIn.Margin = new Padding(1, 0, 1, 0);
            _btnZoomIn[camIdx] = bZIn; flow.Controls.Add(bZIn);

            ComboBox cbZoom = new ComboBox { Width = 52, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(1, 1, 2, 0) };
            cbZoom.Items.AddRange(new object[] { "50", "100", "200", "400", "800" });
            cbZoom.SelectedIndex = 1;
            cbZoom.SelectedIndexChanged += delegate
            {
                double pct;
                if (cbZoom.SelectedItem != null && double.TryParse(cbZoom.SelectedItem.ToString(), out pct))
                {
                    (camIdx == 0 ? _vp1 : _vp2).ZoomFactor = Math.Max(0.25, pct / 100.0);
                }
            };
            _cbZoom[camIdx] = cbZoom; flow.Controls.Add(cbZoom);

            TextBox tbRate = new TextBox { Width = 42, Height = 20, Text = "1000", TextAlign = HorizontalAlignment.Right, Margin = new Padding(1, 1, 2, 0) };
            tbRate.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                int r;
                if (ke.KeyCode == Keys.Enter && int.TryParse(tbRate.Text, out r) && r >= 10)
                {
                    ke.SuppressKeyPress = true;
                    SlitCameraStream cs = GetCam(camIdx);
                    if (cs != null)
                    {
                        cs.SampleRateHz = r;
                        RefreshInformationGrid();
                        InvalidateViewports();
                        NotifyModified();
                    }
                }
            };
            _txtRate[camIdx] = tbRate; flow.Controls.Add(tbRate);

            LynxIconButton bCrop = MkIconBtn("Crop", 22, 22, "Crop Image (Alt+C)", delegate { _activeCamIdx = camIdx; ActiveViewport.CropToCurrentZoomOrSelection(); });
            bCrop.Margin = new Padding(1, 0, 1, 0);
            _btnCrop[camIdx] = bCrop; flow.Controls.Add(bCrop);

            LynxIconButton bUncrop = MkIconBtn("Uncrop", 22, 22, "Uncrop Image", delegate { _activeCamIdx = camIdx; ActiveViewport.UncropActiveStream(); });
            bUncrop.Margin = new Padding(1, 0, 1, 0);
            _btnUncrop[camIdx] = bUncrop; flow.Controls.Add(bUncrop);

            LynxIconButton bAutocrop = MkIconBtn("Autocrop", 22, 22, "Autocrop Motion Activity Bounds", delegate { _activeCamIdx = camIdx; ActiveViewport.AutocropActiveStream(35, 35); });
            bAutocrop.Margin = new Padding(1, 0, 1, 0);
            _btnAutocrop[camIdx] = bAutocrop; flow.Controls.Add(bAutocrop);

            LynxIconButton bScroll = MkIconBtn("Scroll", 22, 22, "Auto-Scroll New Image (Tab)", delegate
            {
                SlitScanViewportControl target = (camIdx == 0 ? _vp1 : _vp2);
                target.AutoScrollCapture = !target.AutoScrollCapture;
                _btnScroll[camIdx].Checked = target.AutoScrollCapture;
            });
            bScroll.Checked = true;
            bScroll.Margin = new Padding(1, 0, 1, 0);
            _btnScroll[camIdx] = bScroll; flow.Controls.Add(bScroll);

            LynxIconButton bFindNext = MkIconBtn("FindNext", 22, 22, "Find Next Object Peak in Image", delegate
            {
                _activeCamIdx = camIdx;
                FindObjectInDirection(camIdx, 1);
            });
            bFindNext.Margin = new Padding(1, 0, 1, 0);
            _btnFindNext[camIdx] = bFindNext; flow.Controls.Add(bFindNext);

            LynxIconButton bEnh = MkIconBtn("Enhance", 22, 22, "Toggle Image / Band Enhance", delegate { _activeCamIdx = camIdx; ActiveViewport.ToggleEnhanceAtActiveBand(); });
            bEnh.Margin = new Padding(1, 0, 1, 0);
            _btnEnhance[camIdx] = bEnh; flow.Controls.Add(bEnh);

            LynxIconButton bAdj = MkIconBtn("QuickAdjust", 22, 22, "Open Image Adjustments", delegate
            {
                _activeCamIdx = camIdx;
                if (OpenImageAdjustmentsRequested != null) OpenImageAdjustmentsRequested(this, EventArgs.Empty);
            });
            bAdj.Margin = new Padding(1, 0, 1, 0);
            _btnQuickAdj[camIdx] = bAdj; flow.Controls.Add(bAdj);

            LynxIconButton bPrn = MkIconBtn("Print", 22, 22, "Print Camera Image...", delegate
            {
                _activeCamIdx = camIdx;
                if (PrintImageRequested != null) PrintImageRequested(this, EventArgs.Empty);
            });
            bPrn.Margin = new Padding(1, 0, 1, 0);
            _btnPrintImg[camIdx] = bPrn; flow.Controls.Add(bPrn);

            LynxIconButton bBmp = MkIconBtn("PrintBitmap", 22, 22, "Print / Export Annotated Bitmap...", delegate
            {
                _activeCamIdx = camIdx;
                if (PrintBitmapRequested != null) PrintBitmapRequested(this, EventArgs.Empty);
            });
            bBmp.Margin = new Padding(1, 0, 1, 0);
            _btnPrintBmp[camIdx] = bBmp; flow.Controls.Add(bBmp);

            LynxIconButton bMax = MkIconBtn("Maximize", 22, 22, "Toggle Lane Grid / Pane Maximize", delegate
            {
                _activeCamIdx = camIdx;
                ToggleSingleCameraMaximize(camIdx);
            });
            bMax.Margin = new Padding(1, 0, 2, 0);
            _btnMaximize[camIdx] = bMax; flow.Controls.Add(bMax);

            Label lblCam = new Label
            {
                Text = "Camera " + (camIdx + 1),
                AutoSize = true,
                Location = new Point(0, 4),
                Margin = new Padding(4, 4, 4, 0),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            _lblCamName[camIdx] = lblCam; flow.Controls.Add(lblCam);

            barPanel.Controls.Add(flow);
            pane.Controls.Add(vp);
            pane.Controls.Add(barPanel);
            return pane;
        }

        private SlitCameraStream GetCam(int camIdx)
        {
            if (_doc == null || camIdx < 0 || camIdx >= _doc.Cameras.Count) return null;
            return _doc.Cameras[camIdx];
        }

        // ===================== DOCUMENT & SYNC LOGIC =====================
        public void LoadDocument(RaceEventDocument doc)
        {
            StopVideo();
            _doc = doc;
            _activeCamIdx = 0;

            _vp1.Document = doc; _vp1.CameraIndex = 0;
            _vp2.Document = doc; _vp2.CameraIndex = 1;

            bool hasCam2 = doc != null && doc.Cameras.Count > 1;
            _splitCameras.Panel2Collapsed = !hasCam2;
            if (hasCam2)
            {
                try { _splitCameras.SplitterDistance = Math.Max(200, _splitCameras.Width / 2); } catch { }
            }

            if (doc != null && doc.Cameras.Count > 0)
            {
                SlitScanEngine.UpdateDisplayBitmap(doc.Cameras[0]);
                if (hasCam2) SlitScanEngine.UpdateDisplayBitmap(doc.Cameras[1]);

                double firstTime = double.NaN;
                for (int i = 0; i < doc.Competitors.Count; i++)
                {
                    if (doc.Competitors[i].HasValidNumericTime) { firstTime = doc.Competitors[i].RawTimeSeconds; break; }
                }
                if (double.IsNaN(firstTime)) firstTime = doc.Cameras[0].CaptureStartSeconds + 0.05;
                _vp1.CenterOnTime(firstTime);
                if (hasCam2) _vp2.CenterOnTime(firstTime);
                if (_txtFinishTime[0] != null) _txtFinishTime[0].Text = CompetitorEntry.FormatSeconds(firstTime, doc.Precision);
            }

            RefreshAll();
        }

        public void RefreshAll()
        {
            RefreshInformationGrid();
            RefreshResultsGrid();
            RefreshCameraBars();
            InvalidateViewports();
        }

        public void RefreshInformationGrid()
        {
            if (_dgvInfo == null) return;
            _updatingInfoGrid = true;
            try
            {
                _dgvInfo.Rows.Clear();
                if (_doc == null) return;

                // Row 0: Event
                int rEv = _dgvInfo.Rows.Add("", _doc.EventName, _doc.EventNumber, _doc.RoundNumber, _doc.HeatNumber, _doc.DistanceMeters.ToString("0.##", CultureInfo.InvariantCulture) + "m", "");
                _dgvInfo.Rows[rEv].Tag = "EVENT";

                // Row 1: Start
                string startStr = _doc.HasStartSignal ? _doc.GunTimeOfDay.ToString("HH:mm:ss.ff", CultureInfo.InvariantCulture) : "<none>";
                string startSrc = _doc.StartSource == StartSignalKind.ElectronicSensor ? "(e)" : (_doc.StartSource == StartSignalKind.ManualTrigger ? "(m)" : "(i)");
                int rSt = _dgvInfo.Rows.Add("", Loc.Evt("Start\\Fields\\Name", "Start"), startStr + " " + startSrc, _doc.StartOffsetSeconds.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture), _doc.StartSource.ToString(), "", "");
                _dgvInfo.Rows[rSt].Tag = "START";

                // Row 2: Wind
                string wUnit = _doc.WindUnit == WindSpeedUnit.MetersPerSecond ? "m/s" : (_doc.WindUnit == WindSpeedUnit.MilesPerHour ? "mph" : "fps");
                string wMode = _doc.WindMode == WindWindowMode.Manual ? "Manual (0.0,10.0)" : _doc.WindMode.ToString();
                int rWd = _dgvInfo.Rows.Add("", Loc.Evt("Wind\\Fields\\Name", "Wind"), _doc.HasWindReading ? _doc.WindReading.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) : "NWI", wUnit, wMode, "", "");
                _dgvInfo.Rows[rWd].Tag = "WIND";

                // Row 3: LapTime
                int rLt = _dgvInfo.Rows.Add("", Loc.Opt("LapTime\\Title", "LapTime"), _doc.TotalLaps, _doc.LapsPerSplit, _doc.MinValidSplitSeconds.ToString("0.0", CultureInfo.InvariantCulture), _doc.MaxValidSplitSeconds.ToString("0.0", CultureInfo.InvariantCulture), "");
                _dgvInfo.Rows[rLt].Tag = "LAPTIME";

                // Split Location rows
                for (int s = 0; s < _doc.SplitLocations.Count; s++)
                {
                    SplitLocationDef sp = _doc.SplitLocations[s];
                    int rs = _dgvInfo.Rows.Add(s == _doc.ActiveCaptureSplitIndex ? ">" : "", sp.Name, sp.CaptureStartSeconds.ToString("0.0", CultureInfo.InvariantCulture), sp.CaptureDurationSeconds.ToString("0.0", CultureInfo.InvariantCulture), "", "", "");
                    _dgvInfo.Rows[rs].Tag = "SPLIT:" + s;
                }

                // Camera rows
                for (int i = 0; i < _doc.Cameras.Count; i++)
                {
                    SlitCameraStream cam = _doc.Cameras[i];
                    CameraSettingsProfile p = cam.Profile ?? new CameraSettingsProfile();
                    int rc = _dgvInfo.Rows.Add(
                        cam.IsEnabled ? "c" : "",
                        cam.DisplayName,
                        cam.SampleRateHz,
                        cam.FrameHeightPx,
                        cam.IsVideo ? "2D Video" : p.PixelMode,
                        string.Format(CultureInfo.InvariantCulture, "{0} ({1})", cam.GainPercent, p.AgcBrightness),
                        cam.Orientation == StripOrientation.RightToLeft ? "Right" : "Left");
                    _dgvInfo.Rows[rc].Tag = "CAM:" + i;
                    _dgvInfo.Rows[rc].Cells[0].Style.BackColor = cam.IsEnabled ? Color.Lime : Color.Red;
                    _dgvInfo.Rows[rc].Cells[0].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                }

                // Results summary row
                int rRes = _dgvInfo.Rows.Add("", Loc.Opt("Results\\Title", "Results"), _doc.Precision.ToString(), _doc.DeltaMode.ToString(), _doc.SpeedUnit.ToString(), "", "");
                _dgvInfo.Rows[rRes].Tag = "RESULTS";

                // Arm / Capture status icon indicators
                _btnInfoArmBox.BackColor = _doc.IsArmed ? Color.Yellow : SystemColors.Control;
                _btnInfoCapBox.BackColor = _doc.IsCapturing ? Color.Lime : SystemColors.Control;
            }
            catch (InvalidOperationException)
            {
                if (IsHandleCreated) SafeDefer(delegate { RefreshInformationGrid(); });
            }
            finally
            {
                _updatingInfoGrid = false;
            }
        }

        public void RefreshResultsGrid()
        {
            if (_dgvResults == null || _doc == null) return;
            _updatingResultsGrid = true;
            try
            {
                _doc.RecalculatePlaces();
                List<ResultFieldConfig> fields = LynxExtendedConfig.Instance.ResultFields;

                _dgvResults.Columns.Clear();
                for (int f = 0; f < fields.Count; f++)
                {
                    if (!fields[f].Enabled) continue;
                    DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn
                    {
                        Name = fields[f].Key,
                        HeaderText = Loc.FieldDisplayName(fields[f].Key, fields[f].DisplayName),
                        SortMode = DataGridViewColumnSortMode.Programmatic,
                        AutoSizeMode = DataGridViewAutoSizeColumnMode.None
                    };
                    if (fields[f].Key == "Place") { col.Width = 46; col.ReadOnly = true; }
                    else if (fields[f].Key == "Lane" || fields[f].Key == "Laps") col.Width = 42;
                    else if (fields[f].Key == "Id") col.Width = 48;
                    else if (fields[f].Key == "FirstName" || fields[f].Key == "LastName") col.Width = 104;
                    else if (fields[f].Key == "Affiliation") col.Width = 112;
                    else if (fields[f].Key == "Time") col.Width = 76;
                    else col.Width = 72;

                    if (fields[f].Justify == "Right") col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    else if (fields[f].Justify == "Center") col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    _dgvResults.Columns.Add(col);
                }

                List<CompetitorEntry> items = new List<CompetitorEntry>();
                for (int i = 0; i < _doc.Competitors.Count; i++)
                {
                    if (_doc.MatchesFilters(_doc.Competitors[i])) items.Add(_doc.Competitors[i]);
                }

                if (_sortColumnMode != 0)
                {
                    items.Sort(delegate(CompetitorEntry a, CompetitorEntry b)
                    {
                        int cmp = CompareByField(a, b, _sortFieldKey);
                        return _sortColumnMode == 2 ? -cmp : cmp;
                    });
                }

                _dgvResults.Rows.Clear();
                for (int i = 0; i < items.Count; i++)
                {
                    CompetitorEntry c = items[i];
                    TimePrecision effPrec = c.PrecisionOverride ?? _doc.Precision;
                    object[] vals = new object[_dgvResults.Columns.Count];
                    for (int ci = 0; ci < _dgvResults.Columns.Count; ci++)
                    {
                        vals[ci] = FormatCellValue(c, _dgvResults.Columns[ci].Name, effPrec);
                    }
                    int rIdx = _dgvResults.Rows.Add(vals);
                    _dgvResults.Rows[rIdx].Tag = c;
                }
            }
            catch (InvalidOperationException)
            {
                if (IsHandleCreated) SafeDefer(delegate { RefreshResultsGrid(); });
            }
            finally
            {
                _updatingResultsGrid = false;
            }
        }

        private string FormatCellValue(CompetitorEntry c, string key, TimePrecision effPrec)
        {
            if (key == "Place") return c.GetPlaceDisplay();
            if (key == "Id") return c.BibId ?? "";
            if (key == "Lane") return (c.Lane > 0 || c.HasValidNumericTime) ? c.Lane.ToString(CultureInfo.InvariantCulture) : "";
            if (key == "FirstName") return c.FirstName ?? "";
            if (key == "LastName") return c.LastName ?? "";
            if (key == "Affiliation") return c.Affiliation ?? "";
            if (key == "License") return c.License ?? "";
            if (key == "Time") return c.GetFormattedOfficialTime(effPrec);
            if (key == "Delta") return _doc.FormatDeltaForCompetitor(c, _doc.DeltaMode);
            if (key == "Delta2") return _doc.FormatDeltaForCompetitor(c, _doc.Delta2Mode);
            if (key == "Delta3") return _doc.FormatDeltaForCompetitor(c, _doc.Delta3Mode);
            if (key == "ReacTime") return (!double.IsNaN(c.ReactionTimeSeconds) && c.ReactionTimeSeconds > 0) ? c.ReactionTimeSeconds.ToString("0.000", CultureInfo.InvariantCulture) : "";
            if (key == "CumSplit") return CompetitorEntry.FormatSeconds(c.GetCumulativeSplitSeconds(), effPrec);
            if (key == "LastSplit") return CompetitorEntry.FormatSeconds(c.GetLastSplitSeconds(), effPrec);
            if (key == "BestSplit") return CompetitorEntry.FormatSeconds(c.GetBestSplitSeconds(), effPrec);
            if (key == "Laps") return c.Splits.Count > 0 ? c.Splits.Count.ToString(CultureInfo.InvariantCulture) : "0";
            if (key == "Speed") return c.GetFormattedSpeed(_doc.DistanceMeters, _doc.SpeedUnit);
            if (key == "Pace") return c.GetFormattedPace(_doc.DistanceMeters);
            if (key == "User1") return c.User1 ?? "";
            if (key == "User2") return c.User2 ?? "";
            if (key == "User3") return c.User3 ?? "";
            return "";
        }

        private static int CompareByField(CompetitorEntry a, CompetitorEntry b, string key)
        {
            if (key == "Place" || key == "Time")
            {
                double ta = a.HasValidNumericTime ? a.RawTimeSeconds : 999999.0;
                double tb = b.HasValidNumericTime ? b.RawTimeSeconds : 999999.0;
                return ta.CompareTo(tb);
            }
            if (key == "Lane") return a.Lane.CompareTo(b.Lane);
            if (key == "Id") return string.Compare(a.BibId, b.BibId, StringComparison.OrdinalIgnoreCase);
            if (key == "FirstName") return string.Compare(a.FirstName, b.FirstName, StringComparison.OrdinalIgnoreCase);
            if (key == "LastName") return string.Compare(a.LastName, b.LastName, StringComparison.OrdinalIgnoreCase);
            if (key == "Affiliation") return string.Compare(a.Affiliation, b.Affiliation, StringComparison.OrdinalIgnoreCase);
            return a.Lane.CompareTo(b.Lane);
        }

        public void RefreshCameraBars()
        {
            bool[] imgVis = LynxExtendedConfig.Instance.ImageToolbarVisible;
            for (int c = 0; c < 2; c++)
            {
                SlitCameraStream cam = GetCam(c);
                bool valid = cam != null;
                bool isVid = valid && cam.IsVideo;

                _lblCamName[c].Text = valid ? cam.DisplayName : ("Camera " + (c + 1));
                if (valid)
                {
                    _txtRate[c].Text = cam.SampleRateHz.ToString(CultureInfo.InvariantCulture);
                    _btnOrient[c].Checked = (cam.Orientation == StripOrientation.RightToLeft);
                    string idIcon = cam.IdentifyBy == ResultIdentifyMethod.Id ? "IdentifyID"
                                  : (cam.IdentifyBy == ResultIdentifyMethod.License ? "IdentifyLicense"
                                  : (cam.IdentifyBy == ResultIdentifyMethod.Nothing ? "IdentifyNothing" : "IdentifyLane"));
                    _btnIdentify[c].IconKey = idIcon;
                    _btnEnhance[c].Checked = cam.EnhanceEnabled;
                    _btnUncrop[c].Enabled = cam.IsCropped;
                }

                // Toggle toolbar button visibility per Options -> Image
                _btnIdentify[c].Visible = imgVis[0];
                _btnOrient[c].Visible = imgVis[1];
                _btnZoomOut[c].Visible = imgVis[2];
                _btnZoomIn[c].Visible = imgVis[2];
                _cbZoom[c].Visible = imgVis[3];
                _txtRate[c].Visible = imgVis[4] && !isVid;
                _btnScroll[c].Visible = imgVis[5];
                _btnUncrop[c].Visible = imgVis[6];
                _btnAutocrop[c].Visible = imgVis[7];
                _btnFindNext[c].Visible = imgVis[8];
                _btnCrop[c].Visible = imgVis[9];
                _btnEnhance[c].Visible = imgVis[10];
                _btnQuickAdj[c].Visible = imgVis[11];
                _btnPrintImg[c].Visible = imgVis[12];
                _btnPrintBmp[c].Visible = imgVis[13];
                _btnMaximize[c].Visible = imgVis[14];

                // Video controls
                _txtVideoTime[c].Visible = isVid;
                _btnVidPlay[c].Visible = isVid;
                _btnVidPrev[c].Visible = isVid;
                _btnVidNext[c].Visible = isVid;
                _cbVidSpeed[c].Visible = isVid;
                _btnVidTrack[c].Visible = isVid;
                _tbVidSlider[c].Visible = isVid;
                if (isVid && cam.TotalVideoFrames > 0)
                {
                    _suppressVideoSlider = true;
                    _tbVidSlider[c].Minimum = 0;
                    _tbVidSlider[c].Maximum = Math.Max(1, cam.TotalVideoFrames - 1);
                    _tbVidSlider[c].Value = Math.Max(0, Math.Min(_tbVidSlider[c].Maximum, cam.ActiveVideoFrameIndex));
                    _suppressVideoSlider = false;
                    SyncVideoReadout(c);
                }
            }
        }

        // ===================== VIEWPORT & RESULT INTERACTIONS =====================
        private void OnViewportHairlineMoved(object sender, HairlineChangedEventArgs e)
        {
            if (_doc == null) return;
            SlitScanViewportControl src = sender as SlitScanViewportControl;
            int idx = ReferenceEquals(src, _vp2) ? 1 : 0;
            _activeCamIdx = idx;

            _txtFinishTime[idx].Text = CompetitorEntry.FormatSeconds(e.TimeSeconds, _doc.Precision);
            SlitCameraStream cam = GetCam(idx);
            if (cam != null && cam.IdentifyBy == ResultIdentifyMethod.Lane)
            {
                _txtEvalId[idx].Text = e.Lane.ToString(CultureInfo.InvariantCulture);
            }

            // Synchronize other camera pane if TimeTrackingMode == All Cameras
            if (LynxExtendedConfig.Instance.TimeTrackingMode >= 2)
            {
                SlitScanViewportControl other = idx == 0 ? _vp2 : _vp1;
                if (!_splitCameras.Panel2Collapsed && other.ActiveStream != null)
                {
                    if (other.ActiveStream.IsVideo)
                    {
                        if (_btnVidTrack[other.CameraIndex].Checked)
                        {
                            int fIdx = other.ActiveStream.GetFrameIndexForTime(e.TimeSeconds);
                            other.JumpToVideoFrame(fIdx);
                            SyncVideoReadout(other.CameraIndex);
                        }
                    }
                    else
                    {
                        other.CursorTimeSeconds = e.TimeSeconds;
                        other.CursorVerticalRatio = e.VerticalRatio;
                        other.CenterOnTime(e.TimeSeconds);
                    }
                }
            }
        }

        private void OnViewportQuickEvaluate(object sender, HairlineChangedEventArgs e)
        {
            if (_doc == null) return;
            bool isZeroCtrl = (e.Lane == 0)
                || Math.Abs(e.TimeSeconds) < 0.25
                || (_doc.EventName != null && _doc.EventName.IndexOf("zero", StringComparison.OrdinalIgnoreCase) >= 0);
            int lane = isZeroCtrl ? 0 : e.Lane;
            CompetitorEntry target = null;
            if (isZeroCtrl)
            {
                for (int i = 0; i < _doc.Competitors.Count; i++)
                {
                    if (_doc.Competitors[i].Lane == 0 && string.IsNullOrEmpty(_doc.Competitors[i].LastName))
                    {
                        target = _doc.Competitors[i];
                        break;
                    }
                }
                if (target == null && _dgvResults.SelectedRows.Count > 0)
                {
                    target = _dgvResults.SelectedRows[0].Tag as CompetitorEntry;
                }
                if (target == null)
                {
                    target = new CompetitorEntry
                    {
                        Place = 1,
                        Lane = 0,
                        BibId = "",
                        FirstName = "",
                        LastName = "",
                        Affiliation = "",
                        PrecisionOverride = TimePrecision.TenThousandths
                    };
                    _doc.Competitors.Add(target);
                }
                target.PrecisionOverride = TimePrecision.TenThousandths;
                _doc.Precision = TimePrecision.TenThousandths;
            }
            else
            {
                for (int i = 0; i < _doc.Competitors.Count; i++)
                {
                    if (_doc.Competitors[i].Lane == lane) { target = _doc.Competitors[i]; break; }
                }
                if (target == null && _dgvResults.SelectedRows.Count > 0)
                {
                    target = _dgvResults.SelectedRows[0].Tag as CompetitorEntry;
                }
                if (target == null)
                {
                    target = new CompetitorEntry { Lane = lane, BibId = (100 + lane).ToString(CultureInfo.InvariantCulture), LastName = "Lane " + lane };
                    _doc.Competitors.Add(target);
                }
            }

            target.RawTimeSeconds = e.TimeSeconds;
            target.EvaluatedTimeSeconds = e.TimeSeconds;
            target.EvaluatedVerticalRatio = e.VerticalRatio;
            target.HasImageEvaluation = true;
            target.Status = CompetitorStatus.Valid;
            RefreshResultsGrid();
            InvalidateViewports();
            NotifyModified();
        }

        private void OnViewportSetZeroOrigin(object sender, double currentTimeSeconds)
        {
            if (_doc == null || currentTimeSeconds <= 0.0) return;
            double delta = -currentTimeSeconds;
            for (int i = 0; i < _doc.Cameras.Count; i++) _doc.Cameras[i].CaptureStartSeconds += delta;
            for (int i = 0; i < _doc.Competitors.Count; i++)
            {
                if (_doc.Competitors[i].HasValidNumericTime) _doc.Competitors[i].RawTimeSeconds = Math.Max(0.01, _doc.Competitors[i].RawTimeSeconds + delta);
            }
            RefreshAll();
            NotifyModified();
        }

        public void CommitEvalBoxEntry(int camIdx)
        {
            if (_doc == null) return;
            SlitScanViewportControl vp = camIdx == 1 ? _vp2 : _vp1;
            SlitCameraStream cam = GetCam(camIdx);
            if (cam == null) return;

            string keyVal = _txtEvalId[camIdx].Text.Trim();
            bool isZeroCtrl = (keyVal == "0")
                || Math.Abs(vp.CursorTimeSeconds) < 0.25
                || (_doc.EventName != null && _doc.EventName.IndexOf("zero", StringComparison.OrdinalIgnoreCase) >= 0);

            CompetitorEntry target = null;
            if (isZeroCtrl)
            {
                for (int i = 0; i < _doc.Competitors.Count; i++)
                {
                    if (_doc.Competitors[i].Lane == 0 && string.IsNullOrEmpty(_doc.Competitors[i].LastName))
                    {
                        target = _doc.Competitors[i];
                        break;
                    }
                }
                if (target == null && _dgvResults.SelectedRows.Count > 0)
                {
                    target = _dgvResults.SelectedRows[0].Tag as CompetitorEntry;
                }
                if (target == null)
                {
                    target = new CompetitorEntry
                    {
                        Place = 1,
                        Lane = 0,
                        BibId = "",
                        FirstName = "",
                        LastName = "",
                        Affiliation = "",
                        PrecisionOverride = TimePrecision.TenThousandths
                    };
                    _doc.Competitors.Add(target);
                }
                target.PrecisionOverride = TimePrecision.TenThousandths;
                _doc.Precision = TimePrecision.TenThousandths;
            }
            else
            {
                for (int i = 0; i < _doc.Competitors.Count; i++)
                {
                    CompetitorEntry c = _doc.Competitors[i];
                    if (cam.IdentifyBy == ResultIdentifyMethod.Id && string.Equals(c.BibId, keyVal, StringComparison.OrdinalIgnoreCase)) { target = c; break; }
                    if (cam.IdentifyBy == ResultIdentifyMethod.License && string.Equals(c.License, keyVal, StringComparison.OrdinalIgnoreCase)) { target = c; break; }
                    if (cam.IdentifyBy == ResultIdentifyMethod.Lane)
                    {
                        int ln;
                        if (int.TryParse(keyVal, out ln) && c.Lane == ln) { target = c; break; }
                    }
                }
                if (target == null && _dgvResults.SelectedRows.Count > 0)
                {
                    target = _dgvResults.SelectedRows[0].Tag as CompetitorEntry;
                }
                if (target == null)
                {
                    int ln = _doc.RatioToLane(vp.CursorVerticalRatio);
                    int parsedLn;
                    if (cam.IdentifyBy == ResultIdentifyMethod.Lane && int.TryParse(keyVal, out parsedLn)) ln = parsedLn;
                    target = new CompetitorEntry
                    {
                        Lane = ln,
                        BibId = cam.IdentifyBy == ResultIdentifyMethod.Id ? keyVal : (100 + ln).ToString(CultureInfo.InvariantCulture),
                        License = cam.IdentifyBy == ResultIdentifyMethod.License ? keyVal : "",
                        LastName = "Lane " + ln
                    };
                    _doc.Competitors.Add(target);
                }
            }

            target.RawTimeSeconds = vp.CursorTimeSeconds;
            target.EvaluatedTimeSeconds = vp.CursorTimeSeconds;
            target.EvaluatedVerticalRatio = vp.CursorVerticalRatio;
            target.EvaluatedCameraIndex = camIdx;
            target.HasImageEvaluation = true;
            target.Status = CompetitorStatus.Valid;
            RefreshResultsGrid();
            InvalidateViewports();
            NotifyModified();
        }

        public void AppendKeypadDigit(string digit)
        {
            TextBox tb = _txtEvalId[_activeCamIdx];
            if (tb == null) return;
            tb.Text = (tb.Text ?? "") + digit;
        }

        public void BackspaceKeypad()
        {
            TextBox tb = _txtEvalId[_activeCamIdx];
            if (tb != null && !string.IsNullOrEmpty(tb.Text)) tb.Text = tb.Text.Substring(0, tb.Text.Length - 1);
        }

        public void ClearKeypad()
        {
            TextBox tb = _txtEvalId[_activeCamIdx];
            if (tb != null) tb.Text = "";
        }

        private void OnResultSelectionChanged(object sender, EventArgs e)
        {
            if (_updatingResultsGrid || _doc == null || _dgvResults.SelectedRows.Count == 0) return;
            CompetitorEntry c = _dgvResults.SelectedRows[0].Tag as CompetitorEntry;
            if (c != null && c.HasValidNumericTime)
            {
                _vp1.CenterOnTime(c.RawTimeSeconds);
                if (!_splitCameras.Panel2Collapsed) _vp2.CenterOnTime(c.RawTimeSeconds);
            }
        }

        private void OnResultCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_updatingResultsGrid || _doc == null || e.RowIndex < 0) return;
            CompetitorEntry c = _dgvResults.Rows[e.RowIndex].Tag as CompetitorEntry;
            if (c == null) return;
            string colKey = _dgvResults.Columns[e.ColumnIndex].Name;
            string val = (_dgvResults.Rows[e.RowIndex].Cells[e.ColumnIndex].Value ?? "").ToString().Trim();
            int iv;
            if (colKey == "Lane" && int.TryParse(val, out iv)) c.Lane = iv;
            else if (colKey == "Id") c.BibId = val;
            else if (colKey == "FirstName") c.FirstName = val;
            else if (colKey == "LastName") c.LastName = val;
            else if (colKey == "Affiliation") c.Affiliation = val;
            else if (colKey == "License") c.License = val;
            else if (colKey == "User1") c.User1 = val;
            else if (colKey == "User2") c.User2 = val;
            else if (colKey == "User3") c.User3 = val;
            else if (colKey == "Time")
            {
                double t = MeetDatabaseService.ParseFormattedTime(val);
                if (!double.IsNaN(t)) { c.RawTimeSeconds = t; c.Status = CompetitorStatus.Valid; c.IsManualTime = true; }
                else if (string.IsNullOrEmpty(val)) { c.RawTimeSeconds = double.NaN; }
            }
            SafeDefer(delegate
            {
                RefreshResultsGrid();
                InvalidateViewports();
                NotifyModified();
            });
        }

        private static void AddCheckItem(ToolStripItemCollection items, string text, bool isChecked, EventHandler click)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text) { Checked = isChecked };
            mi.Click += click;
            items.Add(mi);
        }

        private void OnResultHeaderClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_doc == null || e.ColumnIndex < 0) return;
            string key = _dgvResults.Columns[e.ColumnIndex].Name;
            ContextMenuStrip menu = new ContextMenuStrip();

            if (key == "Time")
            {
                foreach (TimePrecision tp in new TimePrecision[] { TimePrecision.Seconds, TimePrecision.Fifths, TimePrecision.Tenths, TimePrecision.Hundredths, TimePrecision.Thousandths, TimePrecision.TenThousandths })
                {
                    TimePrecision cap = tp;
                    AddCheckItem(menu.Items, tp.ToString(), _doc.Precision == tp, delegate { _doc.Precision = cap; RefreshAll(); });
                }
            }
            else if (key == "Delta" || key == "Delta2" || key == "Delta3")
            {
                foreach (DeltaReferenceMode dm in new DeltaReferenceMode[] { DeltaReferenceMode.FromWinner, DeltaReferenceMode.FromPrevious, DeltaReferenceMode.FromNext, DeltaReferenceMode.LengthsAthletics, DeltaReferenceMode.LengthsCycling, DeltaReferenceMode.LengthsRowing })
                {
                    DeltaReferenceMode cap = dm;
                    DeltaReferenceMode cur = key == "Delta2" ? _doc.Delta2Mode : (key == "Delta3" ? _doc.Delta3Mode : _doc.DeltaMode);
                    AddCheckItem(menu.Items, dm.ToString(), cur == dm, delegate
                    {
                        if (key == "Delta2") _doc.Delta2Mode = cap;
                        else if (key == "Delta3") _doc.Delta3Mode = cap;
                        else _doc.DeltaMode = cap;
                        RefreshResultsGrid();
                    });
                }
            }
            else if (key == "Speed")
            {
                foreach (SpeedMeasurementUnit su in Enum.GetValues(typeof(SpeedMeasurementUnit)))
                {
                    SpeedMeasurementUnit cap = su;
                    AddCheckItem(menu.Items, su.ToString(), _doc.SpeedUnit == su, delegate { _doc.SpeedUnit = cap; RefreshResultsGrid(); });
                }
            }
            else
            {
                menu.Items.Add("Sort Ascending (" + key + ")", null, delegate { _sortFieldKey = key; _sortColumnMode = 1; RefreshResultsGrid(); });
                menu.Items.Add("Sort Descending (" + key + ")", null, delegate { _sortFieldKey = key; _sortColumnMode = 2; RefreshResultsGrid(); });
                menu.Items.Add("Default Order", null, delegate { _sortColumnMode = 0; RefreshResultsGrid(); });
                menu.Items.Add(new ToolStripSeparator());
                AddCheckItem(menu.Items, "Toggle Active Filters", _doc.FiltersEnabled, delegate { _doc.FiltersEnabled = !_doc.FiltersEnabled; RefreshResultsGrid(); });
            }
            menu.Show(Cursor.Position);
        }

        private void OnResultCellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_doc == null || e.RowIndex < 0 || e.Button != MouseButtons.Right) return;
            _dgvResults.Rows[e.RowIndex].Selected = true;
            CompetitorEntry c = _dgvResults.Rows[e.RowIndex].Tag as CompetitorEntry;
            if (c == null) return;

            ContextMenuStrip m = new ContextMenuStrip();
            foreach (CompetitorStatus st in Enum.GetValues(typeof(CompetitorStatus)))
            {
                CompetitorStatus cap = st;
                AddCheckItem(m.Items, "Status: " + st.ToString(), c.Status == st, delegate { c.Status = cap; RefreshResultsGrid(); NotifyModified(); });
            }
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem pMenu = new ToolStripMenuItem("Row Precision");
            pMenu.DropDownItems.Add("Use Event Default", null, delegate { c.PrecisionOverride = null; RefreshResultsGrid(); });
            foreach (TimePrecision tp in new TimePrecision[] { TimePrecision.Tenths, TimePrecision.Hundredths, TimePrecision.Thousandths, TimePrecision.TenThousandths })
            {
                TimePrecision cap = tp;
                AddCheckItem(pMenu.DropDownItems, tp.ToString(), c.PrecisionOverride == tp, delegate { c.PrecisionOverride = cap; RefreshResultsGrid(); });
            }
            m.Items.Add(pMenu);
            AddCheckItem(m.Items, "Toggle Pack Leader", c.IsPackLeader, delegate { c.IsPackLeader = !c.IsPackLeader; RefreshResultsGrid(); });
            m.Items.Add("Delete Row", null, delegate { DeleteSelectedResultRow(); });
            m.Show(Cursor.Position);
        }

        // ===================== PUBLIC RESULT / SPLIT / IMAGE ACTIONS =====================
        public void AddNewResultRow()
        {
            if (_doc == null) return;
            int nextLane = _doc.Competitors.Count + 1;
            _doc.Competitors.Add(new CompetitorEntry { Lane = nextLane, BibId = (100 + nextLane).ToString(CultureInfo.InvariantCulture) });
            RefreshResultsGrid();
            NotifyModified();
        }

        public void DeleteSelectedResultRow()
        {
            if (_doc == null || _dgvResults.SelectedRows.Count == 0) return;
            CompetitorEntry c = _dgvResults.SelectedRows[0].Tag as CompetitorEntry;
            if (c != null)
            {
                _doc.Competitors.Remove(c);
                RefreshResultsGrid();
                InvalidateViewports();
                NotifyModified();
            }
        }

        public void EditSelectedResultCell()
        {
            if (_dgvResults.CurrentCell != null) _dgvResults.BeginEdit(true);
        }

        public CompetitorEntry GetSelectedCompetitor()
        {
            if (_dgvResults.SelectedRows.Count == 0) return null;
            return _dgvResults.SelectedRows[0].Tag as CompetitorEntry;
        }

        public void SelectCompetitorByIdOrName(string query)
        {
            if (_doc == null || string.IsNullOrEmpty(query)) return;
            for (int i = 0; i < _dgvResults.Rows.Count; i++)
            {
                CompetitorEntry c = _dgvResults.Rows[i].Tag as CompetitorEntry;
                if (c == null) continue;
                if (string.Equals(c.BibId, query, StringComparison.OrdinalIgnoreCase) ||
                    (c.LastName ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _dgvResults.Rows[i].Selected = true;
                    _dgvResults.FirstDisplayedScrollingRowIndex = i;
                    return;
                }
            }
        }

        public void NavigateResultSelection(int delta)
        {
            if (_dgvResults.Rows.Count == 0) return;
            int cur = _dgvResults.SelectedRows.Count > 0 ? _dgvResults.SelectedRows[0].Index : 0;
            int next = Math.Max(0, Math.Min(_dgvResults.Rows.Count - 1, cur + delta));
            _dgvResults.Rows[next].Selected = true;
        }

        public void AcceptSelectedSplit()
        {
            CompetitorEntry c = GetSelectedCompetitor();
            if (c == null || c.Splits.Count == 0) return;
            c.Splits[c.Splits.Count - 1].Flag = SplitFlag.Accepted;
            RefreshResultsGrid();
        }

        public void InsertSplitForSelected()
        {
            CompetitorEntry c = GetSelectedCompetitor();
            if (c == null) return;
            double prev = c.Splits.Count > 0 ? c.Splits[c.Splits.Count - 1].CumulativeSeconds : 0.0;
            double t = ActiveViewport != null ? Math.Max(prev + 1.0, ActiveViewport.CursorTimeSeconds) : (prev + 60.0);
            c.Splits.Add(new LapSplitRecord(c.Splits.Count + 1, t, t - prev, SplitFlag.Manual));
            RefreshResultsGrid();
        }

        public void MaskSelectedSplit()
        {
            CompetitorEntry c = GetSelectedCompetitor();
            if (c == null || c.Splits.Count == 0) return;
            c.Splits.RemoveAt(c.Splits.Count - 1);
            RefreshResultsGrid();
        }

        public void CycleIdentifyBy(int camIdx)
        {
            SlitCameraStream cam = GetCam(camIdx);
            if (cam == null) return;
            cam.IdentifyBy = (ResultIdentifyMethod)(((int)cam.IdentifyBy + 1) % 4);
            RefreshCameraBars();
        }

        public void ToggleOrientation(int camIdx)
        {
            SlitCameraStream cam = GetCam(camIdx);
            if (cam == null) return;
            cam.Orientation = cam.Orientation == StripOrientation.LeftToRight ? StripOrientation.RightToLeft : StripOrientation.LeftToRight;
            RefreshInformationGrid();
            RefreshCameraBars();
            InvalidateViewports();
        }

        public void ZoomStep(int dir)
        {
            SlitScanViewportControl vp = ActiveViewport;
            if (vp == null) return;
            vp.ZoomFactor = dir > 0 ? Math.Min(16.0, vp.ZoomFactor * 1.4) : Math.Max(0.25, vp.ZoomFactor / 1.4);
        }

        public void FindObjectInDirection(int camIdx, int dir)
        {
            SlitScanViewportControl vp = camIdx == 1 ? _vp2 : _vp1;
            SlitCameraStream cam = GetCam(camIdx);
            if (vp == null || cam == null || cam.IsVideo) return;
            int col = (int)Math.Round(cam.TimeSecondsToColumn(vp.CursorTimeSeconds));
            int nextCol = SlitScanEngine.FindNextObjectColumn(cam, col, dir);
            if (nextCol >= 0)
            {
                vp.CenterOnTime(cam.ColumnToTimeSeconds(nextCol));
            }
        }

        private void ToggleSingleCameraMaximize(int camIdx)
        {
            if (_doc == null || _doc.Cameras.Count <= 1)
            {
                _vp1.ShowLaneGrid = !_vp1.ShowLaneGrid;
                return;
            }
            if (camIdx == 0) _splitCameras.Panel2Collapsed = !_splitCameras.Panel2Collapsed;
            else _splitCameras.Panel1Collapsed = !_splitCameras.Panel1Collapsed;
        }

        private void StepVideoFrame(int camIdx, int delta)
        {
            SlitScanViewportControl vp = camIdx == 1 ? _vp2 : _vp1;
            vp.StepVideoFrame(delta);
            SyncVideoReadout(camIdx);
        }

        private void ToggleVideoPlay(int camIdx)
        {
            if (_playingVideoCam == camIdx)
            {
                StopVideo();
            }
            else
            {
                _playingVideoCam = camIdx;
                _btnVidPlay[camIdx].IconKey = "Stop";
                _videoTimer.Start();
            }
        }

        private void StopVideo()
        {
            _videoTimer.Stop();
            for (int i = 0; i < 2; i++) _btnVidPlay[i].IconKey = "Play";
            _playingVideoCam = -1;
        }

        private void OnVideoTimerTick(object sender, EventArgs e)
        {
            if (_playingVideoCam < 0) return;
            SlitCameraStream cam = GetCam(_playingVideoCam);
            if (cam == null || !cam.IsVideo || cam.TotalVideoFrames <= 1) { StopVideo(); return; }
            int next = (cam.ActiveVideoFrameIndex + 1) % cam.TotalVideoFrames;
            (_playingVideoCam == 1 ? _vp2 : _vp1).JumpToVideoFrame(next);
            SyncVideoReadout(_playingVideoCam);
        }

        private void SyncVideoReadout(int camIdx)
        {
            SlitCameraStream cam = GetCam(camIdx);
            if (cam == null || !cam.IsVideo) return;
            _txtVideoTime[camIdx].Text = cam.ActiveVideoTimeSeconds.ToString("0.00", CultureInfo.InvariantCulture);
            _suppressVideoSlider = true;
            _tbVidSlider[camIdx].Value = Math.Max(_tbVidSlider[camIdx].Minimum, Math.Min(_tbVidSlider[camIdx].Maximum, cam.ActiveVideoFrameIndex));
            _suppressVideoSlider = false;
        }

        public void InvalidateViewports()
        {
            if (_vp1 != null) _vp1.Invalidate();
            if (_vp2 != null) _vp2.Invalidate();
        }

        public CropWindow GetActiveExportCrop()
        {
            SlitScanViewportControl vp = ActiveViewport;
            return vp != null ? vp.GetEffectiveExportCrop() : null;
        }

        public void ClearSelectionBox()
        {
            if (_vp1 != null) _vp1.ClearSelectionBox();
            if (_vp2 != null) _vp2.ClearSelectionBox();
        }

        public void SelectAllImage()
        {
            SlitScanViewportControl vp = ActiveViewport;
            if (vp != null) vp.SelectAllStrip();
        }

        public void PerformUndo()
        {
            if (_doc == null || _doc.UndoActions == null || _doc.UndoActions.Count == 0) return;
            Action undo = _doc.UndoActions.Pop();
            if (undo != null)
            {
                undo();
                RefreshAll();
                if (StatusHintChanged != null) StatusHintChanged("Undid last image / crop action.");
            }
        }

        public void NavigateSplit(int delta)
        {
            if (_doc == null) return;
            if (_doc.SplitLocations != null && _doc.SplitLocations.Count > 0)
            {
                int n = _doc.SplitLocations.Count;
                _doc.ActiveCaptureSplitIndex = ((_doc.ActiveCaptureSplitIndex + delta) % n + n) % n;
                RefreshInformationGrid();
                SplitLocationDef sp = _doc.SplitLocations[_doc.ActiveCaptureSplitIndex];
                if (ActiveViewport != null) ActiveViewport.CenterOnTime(sp.CaptureStartSeconds);
                if (StatusHintChanged != null) StatusHintChanged("Active split: " + sp.Name);
                return;
            }
            CompetitorEntry c = GetSelectedCompetitor();
            if (c != null && c.Splits != null && c.Splits.Count > 0 && ActiveViewport != null)
            {
                double curT = ActiveViewport.CursorTimeSeconds;
                int best = 0;
                if (delta > 0)
                {
                    best = c.Splits.Count - 1;
                    for (int i = 0; i < c.Splits.Count; i++)
                    {
                        if (c.Splits[i].CumulativeSeconds > curT + 0.002) { best = i; break; }
                    }
                }
                else
                {
                    best = 0;
                    for (int i = c.Splits.Count - 1; i >= 0; i--)
                    {
                        if (c.Splits[i].CumulativeSeconds < curT - 0.002) { best = i; break; }
                    }
                }
                ActiveViewport.CenterOnTime(c.Splits[best].CumulativeSeconds);
            }
        }

        public void ApplyDefaultResultFields()
        {
            string[] defaultOn = new string[]
            {
                "Place", "Id", "Lane", "FirstName", "LastName", "Affiliation",
                "Time", "Delta", "ReacTime", "CumSplit", "Laps", "Speed"
            };
            HashSet<string> enabledSet = new HashSet<string>(defaultOn, StringComparer.OrdinalIgnoreCase);
            List<ResultFieldConfig> fields = LynxExtendedConfig.Instance.ResultFields;
            for (int i = 0; i < fields.Count; i++)
            {
                fields[i].Enabled = enabledSet.Contains(fields[i].Key);
            }
            LynxExtendedConfig.Instance.Save();
            RefreshResultsGrid();
            if (StatusHintChanged != null) StatusHintChanged("Applied default result fields.");
        }

        public void DeleteSelectedObject()
        {
            SlitScanViewportControl vp = ActiveViewport;
            if (vp != null && vp.HasActiveSelectionBox)
            {
                vp.ClearSelectionBox();
                if (StatusHintChanged != null) StatusHintChanged("Cleared selection box.");
                return;
            }
            if (_doc != null && _doc.Overlays.Count > 0)
            {
                int idx = _doc.Overlays.Count - 1;
                ImageOverlayItem removed = _doc.Overlays[idx];
                _doc.Overlays.RemoveAt(idx);
                _doc.UndoActions.Push(delegate { _doc.Overlays.Add(removed); InvalidateViewports(); });
                InvalidateViewports();
                if (DocumentModified != null) DocumentModified(this, EventArgs.Empty);
                if (StatusHintChanged != null) StatusHintChanged("Removed last text overlay.");
                return;
            }
            if (_doc != null && _doc.Annotations.Count > 0)
            {
                _doc.Annotations.RemoveAt(_doc.Annotations.Count - 1);
                InvalidateViewports();
                if (DocumentModified != null) DocumentModified(this, EventArgs.Empty);
                if (StatusHintChanged != null) StatusHintChanged("Removed last annotation.");
                return;
            }
            if (_dgvResults != null && _dgvResults.SelectedRows.Count > 0)
            {
                DeleteSelectedResultRow();
            }
        }
    }
}
