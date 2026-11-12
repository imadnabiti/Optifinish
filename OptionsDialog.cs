using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO.Ports;
using System.Windows.Forms;
using OptiFinish.Core;

namespace OptiFinish
{
    /// <summary>
    /// Full 8-tab FinishLynx Options dialog (General, Event, Image, Results, Database, Scoreboard, Wind, LapTime).
    /// Preserves all OptiFinish HardwareTransceiver and AutoSave configuration controls.
    /// </summary>
    public sealed class OptionsDialog : Form
    {
        private readonly HardwareTransceiver _hardware;
        private TabControl _tabs;
        private Button _btnOk;
        private Button _btnCancel;

        // General Tab
        private TextBox _txtCompName, _txtCompSponsor, _txtFontSize;
        private ComboBox _cbCaptureBtn, _cbCamNetwork, _cbFontName, _cbLanguage, _cbProfile;
        private RadioButton _rbHwNone, _rbHwEth, _rbHwDetect;
        private CheckBox _chkFontBold, _chkEnableShowLines, _chkEnableLaneGrid;

        // Event Tab
        private readonly RadioButton[] _rbLayouts = new RadioButton[6];
        private RadioButton _rbSingleOff, _rbSingleOn, _rbTtOff, _rbTtOn;
        private RadioButton _rbTplNone, _rbTplChoose, _rbTplDefault;
        private TextBox _txtDefaultTpl, _txtEventDir, _txtBackupDir;
        private RadioButton _rbNameTitle, _rbNameErh;
        private CheckBox _chkAutoSave;
        private NumericUpDown _numAutoSaveInterval;

        // Image Tab
        private readonly CheckBox[] _chkImgIcons = new CheckBox[17];
        private RadioButton _rbLaneLeft, _rbLaneRight, _rbLaneBoth;
        private RadioButton _rbTrackOff, _rbTrackSame, _rbTrackAll;
        private ComboBox _cbHashColor, _cbShowLinesColor, _cbSplitsMode, _cbPrintLinesColor, _cbTimeGapLines;
        private TextBox _txtHashWidth, _txtHashCrossWidth, _txtSelW, _txtSelH;
        private TextBox _txtImgQuality, _txtImgScale, _txtVidQuality, _txtVidScale, _txtVidSpeed;
        private CheckBox _chkPrintSelected, _chkSmoothZoom;

        // Results Tab
        private readonly CheckBox[] _chkResIcons = new CheckBox[13];
        private ListBox _lstFields;
        private Button _btnToggleField, _btnFieldUp, _btnFieldDown;
        private ComboBox _cbFieldJustify, _cbDefaultPrecision, _cbRoundMethod;
        private RadioButton _rbLogBreaksOff, _rbLogBreaksOn, _rbPrintBreaksOff, _rbPrintBreaksOn;
        private TextBox _txtLengthTime, _txtPackLeader, _txtPaceDist;
        private CheckBox _chkSuppressMin, _chkRoundMethod;

        // Database Tab
        private RadioButton _rbDbNone, _rbDbFiles, _rbDbSerial;
        private RadioButton _rbDbSingle, _rbDbUnicode, _rbDbNative;
        private ComboBox _cbDbPrecision, _cbDbSerial, _cbDbBaud, _cbDbBits, _cbDbParity, _cbDbStop, _cbReacTimePort;
        private CheckBox _chkDbSuppressMin, _chkLifDate, _chkLifText, _chkLifFilters;
        private TextBox _txtDataIn, _txtDataOut, _txtLifText;
        private RadioButton _rbSerV1, _rbSerV2, _rbWriteLifOff, _rbWriteLifOn;
        private RadioButton _rbLifSingle, _rbLifUnicode, _rbLifNative;

        // Scoreboard Tab
        private ListBox _lstScoreboards;
        private ComboBox _cbScbScript, _cbScbPort, _cbScbBaud, _cbScbBits, _cbScbParity, _cbScbStop, _cbScbPrecision;
        private TextBox _txtScbName, _txtScbPauseTime, _txtScbOffset, _txtScbPageSize, _txtScbPageMax, _txtScbPageTime;
        private RadioButton _rbScbSingle, _rbScbUnicode, _rbScbNative;
        private RadioButton _rbScbRtOff, _rbScbRtNormal, _rbScbRtRaw;
        private RadioButton _rbScbResOff, _rbScbResAuto, _rbScbResManual;
        private CheckBox _chkScbAutoPause, _chkScbAutoPauseCap, _chkScbAutoFinish, _chkScbAutoFinishCap;
        private CheckBox _chkScbPaging, _chkScbSuppressMin, _chkScbAutoExport, _chkEnableSerial, _chkEnableUdp;
        private Label _lblPortStatus;

        // Wind Tab
        private ComboBox _cbWindModule, _cbWindPort, _cbWindBaud, _cbWindBits, _cbWindParity, _cbWindStop, _cbWindMode;
        private TextBox _txtWindStart, _txtWindDuration;
        private RadioButton _rbWindRemOff, _rbWindRemOn, _rbWindWarnOff, _rbWindWarnOn;

        // LapTime Tab
        private ListBox _lstLapDevices;
        private ComboBox _cbLapType, _cbLapPort, _cbLapBaud, _cbLapBits, _cbLapParity, _cbLapStop, _cbLapResultKey;
        private TextBox _txtLapName, _txtLapSelectedSplits, _txtLapOffset, _txtLapMaskStart, _txtLapMaskDup, _txtLapMapFile;
        private RadioButton _rbLapAll, _rbLapSel, _rbLapExt, _rbLapInt, _rbLapNone;
        private CheckBox _chkLapCreateRes, _chkLapFillPlace;
        private RadioButton _rbLapSortCum, _rbLapSortLast, _rbLapSortBest, _rbLapFinalLap, _rbLapFinalOff;

        public OptionsDialog(HardwareTransceiver hardware) : this(hardware, 0) { }

        public OptionsDialog(HardwareTransceiver hardware, int initialTabIndex)
        {
            _hardware = hardware;
            Text = Loc.Opt("Title", "Options");
            Size = new Size(575, 515);
            MinimumSize = new Size(575, 515);
            MaximumSize = new Size(575, 515);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            BackColor = SystemColors.Control;
            ForeColor = SystemColors.ControlText;

            BuildUi();
            LoadFromConfig();
            if (initialTabIndex >= 0 && initialTabIndex < _tabs.TabCount)
            {
                _tabs.SelectedIndex = initialTabIndex;
            }
        }

        private void BuildUi()
        {
            _tabs = new TabControl
            {
                Location = new Point(10, 10),
                Size = new Size(538, 426)
            };

            _tabs.TabPages.Add(BuildGeneralTab());
            _tabs.TabPages.Add(BuildEventTab());
            _tabs.TabPages.Add(BuildImageTab());
            _tabs.TabPages.Add(BuildResultsTab());
            _tabs.TabPages.Add(BuildDatabaseTab());
            _tabs.TabPages.Add(BuildScoreboardTab());
            _tabs.TabPages.Add(BuildWindTab());
            _tabs.TabPages.Add(BuildLapTimeTab());
            Controls.Add(_tabs);

            _btnOk = new Button
            {
                Text = Loc.Dlg("Ok", "OK"),
                Size = new Size(92, 26),
                Location = new Point(176, 444),
                DialogResult = DialogResult.OK
            };
            _btnOk.Click += delegate { SaveToConfig(); };

            _btnCancel = new Button
            {
                Text = Loc.Dlg("Cancel", "Cancel"),
                Size = new Size(92, 26),
                Location = new Point(282, 444),
                DialogResult = DialogResult.Cancel
            };

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);
        }

        private static Label MkLbl(string txt, int x, int y)
        {
            return new Label { Text = txt, AutoSize = true, Location = new Point(x, y) };
        }

        private static TextBox MkTb(int x, int y, int w)
        {
            return new TextBox { Location = new Point(x, y - 3), Width = w, Height = 21 };
        }

        private static ComboBox MkCb(int x, int y, int w, params string[] items)
        {
            ComboBox cb = new ComboBox { Location = new Point(x, y - 3), Width = w, DropDownStyle = ComboBoxStyle.DropDownList };
            if (items != null && items.Length > 0) cb.Items.AddRange(items);
            return cb;
        }

        private static RadioButton MkRb(string txt, int x, int y, Control parent)
        {
            RadioButton rb = new RadioButton { Text = txt, AutoSize = true, Location = new Point(x, y) };
            parent.Controls.Add(rb);
            return rb;
        }

        private static Button MkBrowseBtn(int x, int y, TextBox targetBox, string desc)
        {
            Button btn = new Button { Text = Loc.Dlg("Browse", "Browse..."), Size = new Size(76, 23), Location = new Point(x, y - 4) };
            btn.Click += delegate
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = desc;
                    if (!string.IsNullOrEmpty(targetBox.Text)) fbd.SelectedPath = targetBox.Text;
                    if (fbd.ShowDialog() == DialogResult.OK) targetBox.Text = fbd.SelectedPath;
                }
            };
            return btn;
        }

        private static void PopulatePortCombo(ComboBox cb)
        {
            cb.Items.Clear();
            cb.Items.Add("<none>");
            cb.Items.Add("UDP:127.0.0.1:41605");
            try
            {
                string[] ports = SerialPort.GetPortNames();
                for (int i = 0; i < ports.Length; i++) cb.Items.Add(ports[i]);
            }
            catch { }
            cb.SelectedIndex = 0;
        }

        private TabPage BuildGeneralTab()
        {
            TabPage tp = new TabPage(Loc.Opt("General\\Title", "General")) { BackColor = SystemColors.Control };
            tp.Controls.Add(MkLbl(Loc.Opt("General\\CompName", "Competition Name:"), 18, 22));
            _txtCompName = MkTb(162, 22, 290);
            tp.Controls.Add(_txtCompName);

            tp.Controls.Add(MkLbl(Loc.Opt("General\\CompSponsor", "Competition Sponsor:"), 18, 52));
            _txtCompSponsor = MkTb(162, 52, 290);
            tp.Controls.Add(_txtCompSponsor);

            tp.Controls.Add(MkLbl("Capture Button:", 18, 84));
            _cbCaptureBtn = MkCb(162, 84, 150, "Keyboard", "LynxCapture", "External Switch", "Serial");
            tp.Controls.Add(_cbCaptureBtn);

            Panel pHw = new Panel { Location = new Point(160, 108), Size = new Size(340, 26) };
            tp.Controls.Add(MkLbl("Hardware Type:*", 18, 114));
            _rbHwNone = MkRb("None", 0, 4, pHw);
            _rbHwEth = MkRb("Ethernet", 76, 4, pHw);
            _rbHwDetect = MkRb("Detect", 168, 4, pHw);
            tp.Controls.Add(pHw);

            tp.Controls.Add(MkLbl("Camera Network:*", 18, 146));
            _cbCamNetwork = MkCb(162, 146, 150, "<not specified>", "192.168.0.x (Default)", "10.0.0.x", "Auto-Detect");
            tp.Controls.Add(_cbCamNetwork);

            tp.Controls.Add(MkLbl("Font:*", 18, 178));
            _cbFontName = MkCb(162, 178, 175, "<default>", "Segoe UI", "Tahoma", "Arial", "MS Sans Serif", "Consolas");
            _txtFontSize = MkTb(346, 178, 44);
            _chkFontBold = new CheckBox { Text = "Bold", AutoSize = true, Location = new Point(400, 178) };
            tp.Controls.Add(_cbFontName);
            tp.Controls.Add(_txtFontSize);
            tp.Controls.Add(_chkFontBold);

            tp.Controls.Add(MkLbl(Loc.Opt("General\\Language", "Language:*"), 18, 210));
            _cbLanguage = MkCb(162, 210, 195);
            foreach (string lang in Loc.AvailableLanguages)
            {
                _cbLanguage.Items.Add(lang);
            }
            if (_cbLanguage.Items.Count == 0) _cbLanguage.Items.Add("English (US)");
            tp.Controls.Add(_cbLanguage);

            tp.Controls.Add(MkLbl("Profile:*", 18, 242));
            _cbProfile = MkCb(162, 242, 175, "<standard>", "Athletics", "Cycling", "Swimming / Rowing", "Motorsport");
            tp.Controls.Add(_cbProfile);

            _chkEnableLaneGrid = new CheckBox { Text = "Show Track Lane Lines by default", AutoSize = true, Location = new Point(162, 276) };
            _chkEnableShowLines = new CheckBox { Text = "Show Evaluated Time Hash Lines by default", AutoSize = true, Location = new Point(162, 298) };
            tp.Controls.Add(_chkEnableLaneGrid);
            tp.Controls.Add(_chkEnableShowLines);

            tp.Controls.Add(MkLbl(Loc.Opt("General\\Restart", "*Changes to this setting take effect cleanly on OK / next start."), 18, 336));
            return tp;
        }

        private TabPage BuildEventTab()
        {
            TabPage tp = new TabPage(Loc.Opt("Event\\Title", "Event")) { BackColor = SystemColors.Control };
            tp.Controls.Add(MkLbl("Window Layout:", 18, 22));
            for (int i = 0; i < 6; i++)
            {
                int x = 148 + i * 62;
                RadioButton rb = new RadioButton { Location = new Point(x, 18), Size = new Size(16, 22) };
                PictureBox pic = new PictureBox
                {
                    Location = new Point(x + 17, 17),
                    Size = new Size(32, 24),
                    BorderStyle = BorderStyle.FixedSingle,
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    Image = LynxIcons.CreateLayoutPreview(i),
                    Cursor = Cursors.Hand
                };
                int idx = i;
                pic.Click += delegate { _rbLayouts[idx].Checked = true; };
                _rbLayouts[i] = rb;
                tp.Controls.Add(rb);
                tp.Controls.Add(pic);
            }

            Panel pSingle = new Panel { Location = new Point(146, 52), Size = new Size(200, 24) };
            tp.Controls.Add(MkLbl("Single Event Mode:", 18, 56));
            _rbSingleOff = MkRb("Off", 0, 3, pSingle);
            _rbSingleOn = MkRb("On", 66, 3, pSingle);
            tp.Controls.Add(pSingle);

            Panel pTt = new Panel { Location = new Point(146, 80), Size = new Size(200, 24) };
            tp.Controls.Add(MkLbl("Time Trial Mode:", 18, 84));
            _rbTtOff = MkRb("Off", 0, 3, pTt);
            _rbTtOn = MkRb("On", 66, 3, pTt);
            tp.Controls.Add(pTt);

            Panel pTpl = new Panel { Location = new Point(146, 110), Size = new Size(360, 24) };
            tp.Controls.Add(MkLbl("Event Template:", 18, 114));
            _rbTplNone = MkRb("None", 0, 3, pTpl);
            _rbTplChoose = MkRb("Always Choose", 78, 3, pTpl);
            _rbTplDefault = MkRb("Use Default", 198, 3, pTpl);
            tp.Controls.Add(pTpl);

            tp.Controls.Add(MkLbl("Default Template:", 18, 146));
            _txtDefaultTpl = MkTb(148, 146, 270);
            Button btnTplBrowse = new Button { Text = "Browse...", Size = new Size(76, 23), Location = new Point(426, 142) };
            btnTplBrowse.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Event Files (*.evn;*.optr)|*.evn;*.optr|All Files|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK) _txtDefaultTpl.Text = ofd.FileName;
                }
            };
            tp.Controls.Add(_txtDefaultTpl);
            tp.Controls.Add(btnTplBrowse);

            Panel pName = new Panel { Location = new Point(146, 174), Size = new Size(360, 24) };
            tp.Controls.Add(MkLbl("Default Event Name:", 18, 178));
            _rbNameTitle = MkRb("Event Title", 0, 3, pName);
            _rbNameErh = MkRb("Event-Round-Heat Numbers", 104, 3, pName);
            tp.Controls.Add(pName);

            tp.Controls.Add(MkLbl("Event Directory:", 18, 212));
            _txtEventDir = MkTb(148, 212, 270);
            tp.Controls.Add(_txtEventDir);
            tp.Controls.Add(MkBrowseBtn(426, 212, _txtEventDir, "Select Event Directory"));

            tp.Controls.Add(MkLbl("Backup Directory:", 18, 246));
            _txtBackupDir = MkTb(148, 246, 270);
            tp.Controls.Add(_txtBackupDir);
            tp.Controls.Add(MkBrowseBtn(426, 246, _txtBackupDir, "Select Backup Directory"));

            _chkAutoSave = new CheckBox { Text = "Auto Save every", AutoSize = true, Location = new Point(148, 286) };
            _numAutoSaveInterval = new NumericUpDown { Location = new Point(268, 283), Width = 60, Minimum = 5, Maximum = 3600, Value = 30 };
            tp.Controls.Add(_chkAutoSave);
            tp.Controls.Add(_numAutoSaveInterval);
            tp.Controls.Add(MkLbl("seconds", 336, 287));

            return tp;
        }

        private TabPage BuildImageTab()
        {
            TabPage tp = new TabPage(Loc.Opt("Image\\Title", "Image")) { BackColor = SystemColors.Control };
            tp.Controls.Add(MkLbl("Tool Bar:", 16, 16));

            string[] iconKeys = new string[]
            {
                "IdentifyID", "Orient", "ZoomOut", "ZoomIn", "GoToEvent",
                "Scroll", "Uncrop", "Autocrop", "FindNext", "FindID", "Enhance", "QuickAdjust",
                "Print", "PrintBitmap", "Maximize", "OpenIris", "Play"
            };
            // Organize in 3 clean rows matching OPT_Img_Tool Bar.png
            // Row 0: [IdentifyID] [Orient] [Zoom -/+] [Zoom Drop] [Rate]
            // Row 1: [Crop] [Uncrop] [Autocrop] [Scroll] [FindNext] [Enhance] [QuickAdjust]
            // Row 2: [Print] [PrintBitmap] [Maximize] [Iris] [Video]
            int[][] layoutCoords = new int[][]
            {
                new int[]{0, 96, 12}, new int[]{1, 162, 12}, new int[]{2, 224, 12}, new int[]{3, 304, 12}, new int[]{4, 396, 12},
                new int[]{5, 96, 40}, new int[]{6, 150, 40}, new int[]{7, 204, 40}, new int[]{8, 258, 40}, new int[]{9, 312, 40}, new int[]{10, 366, 40}, new int[]{11, 420, 40},
                new int[]{12, 96, 68}, new int[]{13, 150, 68}, new int[]{14, 204, 68}, new int[]{15, 258, 68}, new int[]{16, 330, 68}
            };
            string[] tips = new string[]
            {
                "Identify By (Id/Lane/License/None)", "Image Orientation (Left/Right)", "Zoom Out / In Buttons (- / +)", "Zoom Preset Dropdown", "Frame Rate / Aspect Ratio Box",
                "Scroll New Image", "Uncrop", "Autocrop", "Find Next Object", "Crop / Find Selection", "Enhance / Unenhance", "Quick Adjustments",
                "Print Image", "Print to Bitmap", "Maximize Pane / Toggle Grid", "Iris Open / Close / Auto", "Video Frame Playback Controls"
            };
            string[] fallbackText = new string[]
            {
                "ID", "<-", "-+", "100%", "1000", "Crop", "Uncrop", "Auto", "Scroll", "Find", "Enh", "Adj", "Prn", "Bmp", "Max", "Iris", "Vid"
            };

            ToolTip tt = new ToolTip();
            for (int i = 0; i < layoutCoords.Length; i++)
            {
                int idx = layoutCoords[i][0];
                int bx = layoutCoords[i][1];
                int by = layoutCoords[i][2];
                CheckBox chk = new CheckBox { Location = new Point(bx, by + 2), Size = new Size(16, 18) };
                PictureBox pic = new PictureBox
                {
                    Location = new Point(bx + 17, by),
                    Size = new Size(22, 22),
                    BorderStyle = BorderStyle.Fixed3D,
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    Image = LynxIcons.Get(iconKeys[idx])
                };
                if (pic.Image == null || idx == 3 || idx == 4 || idx == 16)
                {
                    Label lb = new Label
                    {
                        Text = fallbackText[idx],
                        Location = new Point(bx + 17, by + 2),
                        Size = new Size(idx == 3 ? 42 : 34, 20),
                        BorderStyle = BorderStyle.FixedSingle,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI", 7.2f)
                    };
                    tp.Controls.Add(lb);
                    tt.SetToolTip(lb, tips[idx]);
                }
                else
                {
                    tp.Controls.Add(pic);
                    tt.SetToolTip(pic, tips[idx]);
                }
                tt.SetToolTip(chk, tips[idx]);
                _chkImgIcons[idx] = chk;
                tp.Controls.Add(chk);
            }

            Panel pLane = new Panel { Location = new Point(126, 98), Size = new Size(270, 22) };
            tp.Controls.Add(MkLbl("Lane Bar:", 16, 102));
            _rbLaneLeft = MkRb("Left", 0, 2, pLane);
            _rbLaneRight = MkRb("Right", 68, 2, pLane);
            _rbLaneBoth = MkRb("Both", 142, 2, pLane);
            tp.Controls.Add(pLane);

            Panel pTrack = new Panel { Location = new Point(126, 124), Size = new Size(340, 22) };
            tp.Controls.Add(MkLbl("Time Tracking:", 16, 128));
            _rbTrackOff = MkRb("Off", 0, 2, pTrack);
            _rbTrackSame = MkRb("Same Camera", 68, 2, pTrack);
            _rbTrackAll = MkRb("All Cameras", 184, 2, pTrack);
            tp.Controls.Add(pTrack);

            tp.Controls.Add(MkLbl("Hash Color:", 16, 156));
            _cbHashColor = MkCb(126, 156, 88, "Red", "Yellow", "Cyan", "White", "Black", "Green");
            tp.Controls.Add(_cbHashColor);

            tp.Controls.Add(MkLbl("Hash Size: Width", 16, 184));
            _txtHashWidth = MkTb(126, 184, 38);
            tp.Controls.Add(_txtHashWidth);
            tp.Controls.Add(MkLbl("Cross Width", 182, 184));
            _txtHashCrossWidth = MkTb(262, 184, 42);
            tp.Controls.Add(_txtHashCrossWidth);
            _chkSmoothZoom = new CheckBox { Text = "Smooth Zoom", AutoSize = true, Location = new Point(328, 183) };
            tp.Controls.Add(_chkSmoothZoom);

            tp.Controls.Add(MkLbl("Show Lines:", 16, 212));
            _cbShowLinesColor = MkCb(126, 212, 88, "Black", "Red", "Yellow", "White", "Cyan", "Green");
            tp.Controls.Add(_cbShowLinesColor);
            tp.Controls.Add(MkLbl("Splits", 228, 212));
            _cbSplitsMode = MkCb(268, 212, 82, "Off", "On");
            tp.Controls.Add(_cbSplitsMode);

            tp.Controls.Add(MkLbl("Print Lines:", 16, 240));
            _cbPrintLinesColor = MkCb(126, 240, 88, "Black", "Red", "Yellow", "White");
            tp.Controls.Add(_cbPrintLinesColor);
            _chkPrintSelected = new CheckBox { Text = "Selected by default", AutoSize = true, Location = new Point(228, 239) };
            tp.Controls.Add(_chkPrintSelected);

            tp.Controls.Add(MkLbl("Time Gap Lines:", 16, 268));
            _cbTimeGapLines = MkCb(126, 268, 88, "Off", "1.0s", "0.5s", "0.25s", "0.1s");
            tp.Controls.Add(_cbTimeGapLines);

            tp.Controls.Add(MkLbl("Selection:", 16, 296));
            tp.Controls.Add(MkLbl("Fixed Width", 126, 296));
            _txtSelW = MkTb(202, 296, 44);
            tp.Controls.Add(_txtSelW);
            tp.Controls.Add(MkLbl("Fixed Height", 260, 296));
            _txtSelH = MkTb(342, 296, 44);
            tp.Controls.Add(_txtSelH);

            tp.Controls.Add(MkLbl("Image Export:", 16, 326));
            tp.Controls.Add(MkLbl("JPEG Quality", 126, 326));
            _txtImgQuality = MkTb(208, 326, 38);
            tp.Controls.Add(_txtImgQuality);
            tp.Controls.Add(MkLbl("Scale", 260, 326));
            _txtImgScale = MkTb(300, 326, 42);
            tp.Controls.Add(_txtImgScale);

            tp.Controls.Add(MkLbl("Video Export:", 16, 354));
            tp.Controls.Add(MkLbl("JPEG Quality", 126, 354));
            _txtVidQuality = MkTb(208, 354, 38);
            tp.Controls.Add(_txtVidQuality);
            tp.Controls.Add(MkLbl("Scale", 260, 354));
            _txtVidScale = MkTb(300, 354, 42);
            tp.Controls.Add(_txtVidScale);
            tp.Controls.Add(MkLbl("Speed", 356, 354));
            _txtVidSpeed = MkTb(400, 354, 42);
            tp.Controls.Add(_txtVidSpeed);

            return tp;
        }

        private TabPage BuildResultsTab()
        {
            TabPage tp = new TabPage(Loc.Opt("Results\\Title", "Results")) { BackColor = SystemColors.Control };
            tp.Controls.Add(MkLbl("Tool Bar:", 16, 16));

            string[] resKeys = new string[]
            {
                "NewResult", "DeleteResult", "FindID", "Filters", "PackLeader", "PrevEvent", "GoToEvent",
                "NextEvent", "ReacTime", "Print", "AcceptSplit", "InsertSplit", "DeleteSplit"
            };
            string[] resTips = new string[]
            {
                "New Result", "Delete Result", "Find ID", "Results Filters", "Auto Pack Leaders",
                "Previous Schedule Event", "Goto Schedule Event", "Next Schedule Event",
                "Import ReacTime", "Print Results", "Accept Split", "Insert Split", "Mask / Delete Split"
            };
            ToolTip tt = new ToolTip();
            for (int i = 0; i < 13; i++)
            {
                int col = i < 7 ? i : (i - 7);
                int row = i < 7 ? 0 : 1;
                int bx = 110 + col * 58;
                int by = 12 + row * 28;
                CheckBox chk = new CheckBox { Location = new Point(bx, by + 2), Size = new Size(16, 18) };
                PictureBox pic = new PictureBox
                {
                    Location = new Point(bx + 17, by),
                    Size = new Size(22, 22),
                    BorderStyle = BorderStyle.Fixed3D,
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    Image = LynxIcons.Get(resKeys[i])
                };
                tt.SetToolTip(chk, resTips[i]);
                tt.SetToolTip(pic, resTips[i]);
                _chkResIcons[i] = chk;
                tp.Controls.Add(chk);
                tp.Controls.Add(pic);
            }

            tp.Controls.Add(MkLbl("Fields:", 16, 80));
            _lstFields = new ListBox { Location = new Point(110, 76), Size = new Size(160, 82) };
            _btnToggleField = new Button { Text = "Disable", Location = new Point(282, 76), Size = new Size(100, 24) };
            _cbFieldJustify = MkCb(282, 108, 100, "Justify Left", "Justify Center", "Justify Right");
            _btnFieldUp = new Button { Text = "Up", Location = new Point(392, 76), Size = new Size(56, 24) };
            _btnFieldDown = new Button { Text = "Down", Location = new Point(392, 106), Size = new Size(56, 24) };

            _lstFields.SelectedIndexChanged += delegate
            {
                ResultFieldConfig sel = _lstFields.SelectedItem as ResultFieldConfig;
                if (sel == null) return;
                _btnToggleField.Text = sel.Enabled ? "Disable" : "Enable";
                _cbFieldJustify.SelectedItem = "Justify " + sel.Justify;
            };
            _btnToggleField.Click += delegate
            {
                int idx = _lstFields.SelectedIndex;
                ResultFieldConfig sel = _lstFields.SelectedItem as ResultFieldConfig;
                if (sel == null) return;
                sel.Enabled = !sel.Enabled;
                _lstFields.Items[idx] = sel;
                _btnToggleField.Text = sel.Enabled ? "Disable" : "Enable";
            };
            _cbFieldJustify.SelectedIndexChanged += delegate
            {
                ResultFieldConfig sel = _lstFields.SelectedItem as ResultFieldConfig;
                if (sel == null || _cbFieldJustify.SelectedItem == null) return;
                sel.Justify = _cbFieldJustify.SelectedItem.ToString().Replace("Justify ", "");
            };
            _btnFieldUp.Click += delegate { MoveListItem(_lstFields, -1); };
            _btnFieldDown.Click += delegate { MoveListItem(_lstFields, 1); };

            tp.Controls.Add(_lstFields);
            tp.Controls.Add(_btnToggleField);
            tp.Controls.Add(_cbFieldJustify);
            tp.Controls.Add(_btnFieldUp);
            tp.Controls.Add(_btnFieldDown);

            Panel pLog = new Panel { Location = new Point(110, 168), Size = new Size(150, 22) };
            tp.Controls.Add(MkLbl("Log Breaks:", 16, 172));
            _rbLogBreaksOff = MkRb("Off", 0, 2, pLog);
            _rbLogBreaksOn = MkRb("On", 64, 2, pLog);
            tp.Controls.Add(pLog);

            Panel pPrn = new Panel { Location = new Point(110, 194), Size = new Size(150, 22) };
            tp.Controls.Add(MkLbl("Print Breaks:", 16, 198));
            _rbPrintBreaksOff = MkRb("Off", 0, 2, pPrn);
            _rbPrintBreaksOn = MkRb("On", 64, 2, pPrn);
            tp.Controls.Add(pPrn);

            tp.Controls.Add(MkLbl("Length Time:", 16, 226));
            _txtLengthTime = MkTb(110, 226, 72);
            tp.Controls.Add(_txtLengthTime);

            tp.Controls.Add(MkLbl("Pack Leader:", 16, 254));
            _txtPackLeader = MkTb(110, 254, 72);
            tp.Controls.Add(_txtPackLeader);

            tp.Controls.Add(MkLbl("Default Precision:", 16, 282));
            _cbDefaultPrecision = MkCb(136, 282, 126, "Seconds", "Fifths", "Tenths", "Hundredths", "Thousandths", "TenThousandths");
            tp.Controls.Add(_cbDefaultPrecision);
            _chkSuppressMin = new CheckBox { Text = "Suppress minute", AutoSize = true, Location = new Point(282, 281) };
            tp.Controls.Add(_chkSuppressMin);

            tp.Controls.Add(MkLbl("Pace Distance:", 16, 310));
            _txtPaceDist = MkTb(136, 310, 82);
            tp.Controls.Add(_txtPaceDist);

            tp.Controls.Add(MkLbl("Round Method:*", 16, 338));
            _chkRoundMethod = new CheckBox { Location = new Point(118, 338), Size = new Size(18, 18) };
            _cbRoundMethod = MkCb(140, 338, 160, "Up (1.3 -> 2.0)", "Nearest", "Down");
            _chkRoundMethod.CheckedChanged += delegate { _cbRoundMethod.Enabled = _chkRoundMethod.Checked; };
            tp.Controls.Add(_chkRoundMethod);
            tp.Controls.Add(_cbRoundMethod);
            tp.Controls.Add(MkLbl("*Changes to this setting take effect the next time OptiFinish is run.", 16, 368));
            return tp;
        }

        private static void MoveListItem(ListBox lb, int delta)
        {
            int idx = lb.SelectedIndex;
            if (idx < 0) return;
            int target = idx + delta;
            if (target < 0 || target >= lb.Items.Count) return;
            object item = lb.Items[idx];
            lb.Items.RemoveAt(idx);
            lb.Items.Insert(target, item);
            lb.SelectedIndex = target;
        }

        private TabPage BuildDatabaseTab()
        {
            TabPage tp = new TabPage(Loc.Opt("Database\\Title", "Database")) { BackColor = SystemColors.Control };
            Panel pSrc = new Panel { Location = new Point(140, 14), Size = new Size(340, 22) };
            tp.Controls.Add(MkLbl("Data Source:", 18, 18));
            _rbDbNone = MkRb("None", 0, 2, pSrc);
            _rbDbFiles = MkRb("Files", 78, 2, pSrc);
            _rbDbSerial = MkRb("Serial Port", 150, 2, pSrc);
            tp.Controls.Add(pSrc);

            Panel pCode = new Panel { Location = new Point(140, 42), Size = new Size(340, 22) };
            tp.Controls.Add(MkLbl("Code Set:", 18, 46));
            _rbDbSingle = MkRb("Single Byte", 0, 2, pCode);
            _rbDbUnicode = MkRb("Unicode", 96, 2, pCode);
            _rbDbNative = MkRb("Native", 180, 2, pCode);
            tp.Controls.Add(pCode);

            tp.Controls.Add(MkLbl("Time Precision:", 18, 74));
            _cbDbPrecision = MkCb(142, 74, 118, "<on screen>", "Tenths", "Hundredths", "Thousandths");
            _chkDbSuppressMin = new CheckBox { Text = "Suppress minute", AutoSize = true, Location = new Point(276, 73) };
            tp.Controls.Add(_cbDbPrecision);
            tp.Controls.Add(_chkDbSuppressMin);

            tp.Controls.Add(MkLbl("Input Directory:", 18, 104));
            _txtDataIn = MkTb(142, 104, 272);
            tp.Controls.Add(_txtDataIn);
            tp.Controls.Add(MkBrowseBtn(422, 104, _txtDataIn, "Input Directory (.ppl / .evt / .sch)"));

            tp.Controls.Add(MkLbl("Output Directory:", 18, 134));
            _txtDataOut = MkTb(142, 134, 272);
            tp.Controls.Add(_txtDataOut);
            tp.Controls.Add(MkBrowseBtn(422, 134, _txtDataOut, "Output Directory (.lif / exports)"));

            tp.Controls.Add(MkLbl("Serial Port:", 18, 168));
            _cbDbSerial = MkCb(142, 168, 118);
            PopulatePortCombo(_cbDbSerial);
            tp.Controls.Add(_cbDbSerial);
            tp.Controls.Add(MkLbl("Baud", 274, 168));
            _cbDbBaud = MkCb(316, 168, 72, "4800", "9600", "19200", "38400", "57600", "115200");
            tp.Controls.Add(_cbDbBaud);

            tp.Controls.Add(MkLbl("Data Bits", 142, 198));
            _cbDbBits = MkCb(202, 198, 46, "7", "8");
            tp.Controls.Add(_cbDbBits);
            tp.Controls.Add(MkLbl("Parity", 258, 198));
            _cbDbParity = MkCb(298, 198, 66, "None", "Even", "Odd");
            tp.Controls.Add(_cbDbParity);
            tp.Controls.Add(MkLbl("Stop Bits", 374, 198));
            _cbDbStop = MkCb(432, 198, 52, "1.0", "1.5", "2.0");
            tp.Controls.Add(_cbDbStop);

            Panel pSv = new Panel { Location = new Point(140, 224), Size = new Size(260, 22) };
            tp.Controls.Add(MkLbl("Serial Version:", 18, 228));
            _rbSerV1 = MkRb("1", 0, 2, pSv);
            _rbSerV2 = MkRb("2 (Checksum)", 58, 2, pSv);
            tp.Controls.Add(pSv);

            Panel pWl = new Panel { Location = new Point(140, 252), Size = new Size(200, 22) };
            tp.Controls.Add(MkLbl("Write LIF File:", 18, 256));
            _rbWriteLifOff = MkRb("Off", 0, 2, pWl);
            _rbWriteLifOn = MkRb("On", 64, 2, pWl);
            tp.Controls.Add(pWl);

            tp.Controls.Add(MkLbl("LIF File Name:", 18, 286));
            _chkLifDate = new CheckBox { Text = "Date", AutoSize = true, Location = new Point(142, 285) };
            _chkLifText = new CheckBox { Text = "Text", AutoSize = true, Location = new Point(204, 285) };
            _txtLifText = MkTb(258, 286, 102);
            _chkLifFilters = new CheckBox { Text = "Filters", AutoSize = true, Location = new Point(368, 285) };
            tp.Controls.Add(_chkLifDate);
            tp.Controls.Add(_chkLifText);
            tp.Controls.Add(_txtLifText);
            tp.Controls.Add(_chkLifFilters);

            Panel pLc = new Panel { Location = new Point(140, 314), Size = new Size(340, 22) };
            tp.Controls.Add(MkLbl("LIF Code Set:", 18, 318));
            _rbLifSingle = MkRb("Single Byte", 0, 2, pLc);
            _rbLifUnicode = MkRb("Unicode", 96, 2, pLc);
            _rbLifNative = MkRb("Native", 180, 2, pLc);
            tp.Controls.Add(pLc);

            tp.Controls.Add(MkLbl("ReacTime Port:", 18, 348));
            _cbReacTimePort = MkCb(142, 348, 126);
            PopulatePortCombo(_cbReacTimePort);
            _cbReacTimePort.Items.Insert(0, "Files");
            tp.Controls.Add(_cbReacTimePort);
            return tp;
        }

        private TabPage BuildScoreboardTab()
        {
            TabPage tp = new TabPage(Loc.Opt("Scoreboard\\Title", "Scoreboard")) { BackColor = SystemColors.Control };
            tp.Controls.Add(MkLbl("Scoreboard            Status", 18, 10));
            _lstScoreboards = new ListBox { Location = new Point(18, 28), Size = new Size(310, 56) };
            Button btnNewScb = new Button { Text = "New", Location = new Point(340, 28), Size = new Size(92, 24) };
            Button btnDelScb = new Button { Text = "Delete", Location = new Point(340, 56), Size = new Size(92, 24) };
            btnNewScb.Click += delegate
            {
                ScoreboardOutputConfig scb = new ScoreboardOutputConfig { Name = "Scoreboard " + (LynxExtendedConfig.Instance.Scoreboards.Count + 1) };
                LynxExtendedConfig.Instance.Scoreboards.Add(scb);
                _lstScoreboards.Items.Add(scb.Name + "    " + scb.StatusText);
                _lstScoreboards.SelectedIndex = _lstScoreboards.Items.Count - 1;
            };
            btnDelScb.Click += delegate
            {
                int idx = _lstScoreboards.SelectedIndex;
                if (idx >= 0 && LynxExtendedConfig.Instance.Scoreboards.Count > 1)
                {
                    LynxExtendedConfig.Instance.Scoreboards.RemoveAt(idx);
                    _lstScoreboards.Items.RemoveAt(idx);
                    _lstScoreboards.SelectedIndex = Math.Max(0, idx - 1);
                }
            };
            tp.Controls.Add(_lstScoreboards);
            tp.Controls.Add(btnNewScb);
            tp.Controls.Add(btnDelScb);

            tp.Controls.Add(MkLbl("Script:", 18, 96));
            _cbScbScript = MkCb(112, 96, 190,
                "ResulTV.lss", "1line.lss", "2line.lss", "Adaptive32x128.lss", "Dak.lss",
                "DakMDP.lss", "Gill_2x2.lss", "LynxTV.lss", "Omega-9.lss", "Spectrum.lss",
                "uGraphLED.lss", "VeriLight.lss", "OptiFinish.lss");
            tp.Controls.Add(_cbScbScript);
            tp.Controls.Add(MkLbl("Name:", 314, 96));
            _txtScbName = MkTb(360, 96, 130);
            tp.Controls.Add(_txtScbName);

            Panel pCs = new Panel { Location = new Point(110, 122), Size = new Size(340, 22) };
            tp.Controls.Add(MkLbl("Code Set:", 18, 126));
            _rbScbSingle = MkRb("Single Byte", 0, 2, pCs);
            _rbScbUnicode = MkRb("Unicode", 96, 2, pCs);
            _rbScbNative = MkRb("Native", 180, 2, pCs);
            tp.Controls.Add(pCs);

            tp.Controls.Add(MkLbl("Serial Port:", 18, 152));
            _cbScbPort = MkCb(112, 152, 126);
            PopulatePortCombo(_cbScbPort);
            tp.Controls.Add(_cbScbPort);
            tp.Controls.Add(MkLbl("Baud", 252, 152));
            _cbScbBaud = MkCb(294, 152, 72, "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200");
            tp.Controls.Add(_cbScbBaud);

            Button btnTestPort = new Button { Text = "Test COM", Size = new Size(76, 22), Location = new Point(378, 149) };
            _lblPortStatus = MkLbl("", 460, 153);
            btnTestPort.Click += delegate
            {
                if (_hardware == null) return;
                string selPort = _cbScbPort.SelectedItem != null ? _cbScbPort.SelectedItem.ToString() : "";
                if (selPort.StartsWith("<") || selPort.StartsWith("UDP:"))
                {
                    _hardware.TransmitRunningTime(10.49, "+0.8 m/s");
                    _lblPortStatus.Text = "UDP OK";
                    return;
                }
                int baud = 9600;
                int.TryParse(_cbScbBaud.SelectedItem != null ? _cbScbBaud.SelectedItem.ToString() : "9600", out baud);
                string err;
                if (_hardware.TryOpenSerial(selPort, baud, out err))
                {
                    _hardware.TransmitRunningTime(0.0, "+0.0 m/s");
                    _lblPortStatus.Text = "OK";
                    _lblPortStatus.ForeColor = Color.Green;
                }
                else
                {
                    _lblPortStatus.Text = "ERR";
                    _lblPortStatus.ForeColor = Color.Red;
                }
            };
            tp.Controls.Add(btnTestPort);
            tp.Controls.Add(_lblPortStatus);

            tp.Controls.Add(MkLbl("Data Bits", 112, 180));
            _cbScbBits = MkCb(172, 180, 46, "7", "8");
            tp.Controls.Add(_cbScbBits);
            tp.Controls.Add(MkLbl("Parity", 228, 180));
            _cbScbParity = MkCb(268, 180, 66, "None", "Even", "Odd");
            tp.Controls.Add(_cbScbParity);
            tp.Controls.Add(MkLbl("Stop Bits", 344, 180));
            _cbScbStop = MkCb(404, 180, 52, "1.0", "1.5", "2.0");
            tp.Controls.Add(_cbScbStop);

            Panel pRt = new Panel { Location = new Point(110, 206), Size = new Size(270, 22) };
            tp.Controls.Add(MkLbl("Running Time:", 18, 210));
            _rbScbRtOff = MkRb("Off", 0, 2, pRt);
            _rbScbRtNormal = MkRb("Normal", 64, 2, pRt);
            _rbScbRtRaw = MkRb("Raw", 142, 2, pRt);
            tp.Controls.Add(pRt);

            tp.Controls.Add(MkLbl("Pause Time", 112, 236));
            _txtScbPauseTime = MkTb(186, 236, 48);
            tp.Controls.Add(_txtScbPauseTime);
            tp.Controls.Add(MkLbl("Offset", 248, 236));
            _txtScbOffset = MkTb(294, 236, 54);
            tp.Controls.Add(_txtScbOffset);

            tp.Controls.Add(MkLbl("Auto Break:", 112, 262));
            _chkScbAutoPause = new CheckBox { Text = "Pause", AutoSize = true, Location = new Point(186, 261) };
            _chkScbAutoPauseCap = new CheckBox { Text = "if capturing", AutoSize = true, Location = new Point(246, 261) };
            _chkScbAutoFinish = new CheckBox { Text = "Finish", AutoSize = true, Location = new Point(332, 261) };
            _chkScbAutoFinishCap = new CheckBox { Text = "if capturing", AutoSize = true, Location = new Point(390, 261) };
            tp.Controls.Add(_chkScbAutoPause);
            tp.Controls.Add(_chkScbAutoPauseCap);
            tp.Controls.Add(_chkScbAutoFinish);
            tp.Controls.Add(_chkScbAutoFinishCap);

            Panel pRes = new Panel { Location = new Point(110, 286), Size = new Size(280, 22) };
            tp.Controls.Add(MkLbl("Results:", 18, 290));
            _rbScbResOff = MkRb("Off", 0, 2, pRes);
            _rbScbResAuto = MkRb("Auto", 64, 2, pRes);
            _rbScbResManual = MkRb("Manual", 142, 2, pRes);
            tp.Controls.Add(pRes);

            _chkScbPaging = new CheckBox { Text = "Paging:   Size", AutoSize = true, Location = new Point(112, 314) };
            _txtScbPageSize = MkTb(206, 314, 36);
            tp.Controls.Add(_chkScbPaging);
            tp.Controls.Add(_txtScbPageSize);
            tp.Controls.Add(MkLbl("Max", 252, 314));
            _txtScbPageMax = MkTb(284, 314, 36);
            tp.Controls.Add(_txtScbPageMax);
            tp.Controls.Add(MkLbl("Time", 332, 314));
            _txtScbPageTime = MkTb(370, 314, 40);
            tp.Controls.Add(_txtScbPageTime);

            tp.Controls.Add(MkLbl("Time Precision:", 112, 342));
            _cbScbPrecision = MkCb(206, 342, 108, "<on screen>", "Tenths", "Hundredths", "Thousandths");
            _chkScbSuppressMin = new CheckBox { Text = "Suppress minute", AutoSize = true, Location = new Point(324, 341) };
            tp.Controls.Add(_cbScbPrecision);
            tp.Controls.Add(_chkScbSuppressMin);

            _chkEnableSerial = new CheckBox { Text = "COM Live", AutoSize = true, Location = new Point(112, 370) };
            _chkEnableUdp = new CheckBox { Text = "UDP Broadcast (127.0.0.1:41605)", AutoSize = true, Location = new Point(196, 370) };
            _chkScbAutoExport = new CheckBox { Text = "Auto-Export Image", AutoSize = true, Location = new Point(392, 370) };
            tp.Controls.Add(_chkEnableSerial);
            tp.Controls.Add(_chkEnableUdp);
            tp.Controls.Add(_chkScbAutoExport);

            return tp;
        }

        private TabPage BuildWindTab()
        {
            TabPage tp = new TabPage(Loc.Opt("Wind\\Title", "Wind")) { BackColor = SystemColors.Control };
            tp.Controls.Add(MkLbl("Wind Module:*", 18, 22));
            _cbWindModule = MkCb(140, 22, 136, "Off", "Gill Ultrasonic", "Lynx Ultrasonic", "Sonic", "Manual");
            tp.Controls.Add(_cbWindModule);

            tp.Controls.Add(MkLbl("Serial Port:", 18, 54));
            _cbWindPort = MkCb(140, 54, 136);
            PopulatePortCombo(_cbWindPort);
            tp.Controls.Add(_cbWindPort);
            tp.Controls.Add(MkLbl("Baud", 292, 54));
            _cbWindBaud = MkCb(334, 54, 72, "1200", "2400", "4800", "9600", "19200");
            tp.Controls.Add(_cbWindBaud);

            tp.Controls.Add(MkLbl("Data Bits", 140, 84));
            _cbWindBits = MkCb(200, 84, 46, "7", "8");
            tp.Controls.Add(_cbWindBits);
            tp.Controls.Add(MkLbl("Parity", 256, 84));
            _cbWindParity = MkCb(296, 84, 66, "None", "Even", "Odd");
            tp.Controls.Add(_cbWindParity);
            tp.Controls.Add(MkLbl("Stop Bits", 372, 84));
            _cbWindStop = MkCb(432, 84, 52, "1.0", "1.5", "2.0");
            tp.Controls.Add(_cbWindStop);

            tp.Controls.Add(MkLbl("Default Mode:", 18, 118));
            _cbWindMode = MkCb(140, 118, 120, "Manual", "Auto 10s (100m)", "Auto 13s (Hurdles)", "Auto 5s (200m)");
            tp.Controls.Add(_cbWindMode);
            tp.Controls.Add(MkLbl("Start", 272, 118));
            _txtWindStart = MkTb(310, 118, 48);
            tp.Controls.Add(_txtWindStart);
            tp.Controls.Add(MkLbl("Duration", 370, 118));
            _txtWindDuration = MkTb(428, 118, 48);
            tp.Controls.Add(_txtWindDuration);

            Panel pRem = new Panel { Location = new Point(138, 146), Size = new Size(180, 22) };
            tp.Controls.Add(MkLbl("Reminder:", 18, 150));
            _rbWindRemOff = MkRb("Off", 0, 2, pRem);
            _rbWindRemOn = MkRb("On", 64, 2, pRem);
            tp.Controls.Add(pRem);

            Panel pWarn = new Panel { Location = new Point(138, 174), Size = new Size(180, 22) };
            tp.Controls.Add(MkLbl("Warn If Offline:", 18, 178));
            _rbWindWarnOff = MkRb("Off", 0, 2, pWarn);
            _rbWindWarnOn = MkRb("On", 64, 2, pWarn);
            tp.Controls.Add(pWarn);

            tp.Controls.Add(MkLbl("*Changes to this setting take effect the next time OptiFinish is run.", 18, 224));
            return tp;
        }

        private TabPage BuildLapTimeTab()
        {
            TabPage tp = new TabPage(Loc.Opt("LapTime\\Title", "LapTime")) { BackColor = SystemColors.Control };
            tp.Controls.Add(MkLbl("LapTime Device        Status", 18, 10));
            _lstLapDevices = new ListBox { Location = new Point(18, 28), Size = new Size(310, 56) };
            Button btnNewLap = new Button { Text = "New", Location = new Point(340, 28), Size = new Size(92, 24) };
            Button btnDelLap = new Button { Text = "Delete", Location = new Point(340, 56), Size = new Size(92, 24) };
            btnNewLap.Click += delegate
            {
                LapTimeDeviceConfig d = new LapTimeDeviceConfig { Name = "LapTime " + (LynxExtendedConfig.Instance.LapTimeDevices.Count + 1) };
                LynxExtendedConfig.Instance.LapTimeDevices.Add(d);
                _lstLapDevices.Items.Add(d.Name + "    " + d.StatusText);
                _lstLapDevices.SelectedIndex = _lstLapDevices.Items.Count - 1;
            };
            btnDelLap.Click += delegate
            {
                int idx = _lstLapDevices.SelectedIndex;
                if (idx >= 0 && LynxExtendedConfig.Instance.LapTimeDevices.Count > 1)
                {
                    LynxExtendedConfig.Instance.LapTimeDevices.RemoveAt(idx);
                    _lstLapDevices.Items.RemoveAt(idx);
                    _lstLapDevices.SelectedIndex = Math.Max(0, idx - 1);
                }
            };
            tp.Controls.Add(_lstLapDevices);
            tp.Controls.Add(btnNewLap);
            tp.Controls.Add(btnDelLap);

            tp.Controls.Add(MkLbl("Device:", 18, 96));
            _cbLapType = MkCb(112, 96, 130, "From Image", "MYLAPS / Chip", "LynxPad", "Serial", "File");
            tp.Controls.Add(_cbLapType);
            tp.Controls.Add(MkLbl("Name:", 254, 96));
            _txtLapName = MkTb(300, 96, 130);
            tp.Controls.Add(_txtLapName);

            tp.Controls.Add(MkLbl("Serial Port:", 18, 124));
            _cbLapPort = MkCb(112, 124, 120);
            PopulatePortCombo(_cbLapPort);
            tp.Controls.Add(_cbLapPort);
            tp.Controls.Add(MkLbl("Baud", 244, 124));
            _cbLapBaud = MkCb(286, 124, 72, "4800", "9600", "19200", "38400", "57600");
            tp.Controls.Add(_cbLapBaud);

            tp.Controls.Add(MkLbl("Data Bits", 112, 152));
            _cbLapBits = MkCb(172, 152, 46, "7", "8");
            tp.Controls.Add(_cbLapBits);
            tp.Controls.Add(MkLbl("Parity", 228, 152));
            _cbLapParity = MkCb(268, 152, 66, "None", "Even", "Odd");
            tp.Controls.Add(_cbLapParity);
            tp.Controls.Add(MkLbl("Stop Bits", 344, 152));
            _cbLapStop = MkCb(404, 152, 52, "1.0", "1.5", "2.0");
            tp.Controls.Add(_cbLapStop);

            Panel pAct = new Panel { Location = new Point(110, 178), Size = new Size(340, 22) };
            tp.Controls.Add(MkLbl("Active Splits:", 18, 182));
            _rbLapAll = MkRb("All", 0, 2, pAct);
            _rbLapSel = MkRb("Selected:", 64, 2, pAct);
            _txtLapSelectedSplits = new TextBox { Location = new Point(150, 0), Width = 175 };
            pAct.Controls.Add(_txtLapSelectedSplits);
            tp.Controls.Add(pAct);

            Panel pSync = new Panel { Location = new Point(110, 204), Size = new Size(280, 22) };
            tp.Controls.Add(MkLbl("Sync. Source:", 18, 208));
            _rbLapExt = MkRb("External", 0, 2, pSync);
            _rbLapInt = MkRb("Internal", 78, 2, pSync);
            _rbLapNone = MkRb("None", 156, 2, pSync);
            tp.Controls.Add(pSync);

            tp.Controls.Add(MkLbl("Time Offset:", 18, 236));
            _txtLapOffset = MkTb(112, 236, 72);
            tp.Controls.Add(_txtLapOffset);

            Label sep = new Label { BorderStyle = BorderStyle.Fixed3D, Location = new Point(18, 260), Size = new Size(476, 2) };
            tp.Controls.Add(sep);

            tp.Controls.Add(MkLbl("Masks:", 18, 274));
            tp.Controls.Add(MkLbl("Start", 112, 274));
            _txtLapMaskStart = MkTb(152, 274, 54);
            tp.Controls.Add(_txtLapMaskStart);
            tp.Controls.Add(MkLbl("Duplicate", 218, 274));
            _txtLapMaskDup = MkTb(284, 274, 54);
            tp.Controls.Add(_txtLapMaskDup);

            tp.Controls.Add(MkLbl("Results:", 18, 302));
            tp.Controls.Add(MkLbl("Key", 112, 302));
            _cbLapResultKey = MkCb(148, 302, 86, "Lane", "Id", "License");
            _chkLapCreateRes = new CheckBox { Text = "Create results", AutoSize = true, Location = new Point(246, 301) };
            _chkLapFillPlace = new CheckBox { Text = "Fill in place", AutoSize = true, Location = new Point(356, 301) };
            tp.Controls.Add(_cbLapResultKey);
            tp.Controls.Add(_chkLapCreateRes);
            tp.Controls.Add(_chkLapFillPlace);

            Panel pSort = new Panel { Location = new Point(110, 326), Size = new Size(300, 22) };
            tp.Controls.Add(MkLbl("Sort By Split:", 18, 330));
            _rbLapSortCum = MkRb("Cumulative", 0, 2, pSort);
            _rbLapSortLast = MkRb("Last", 94, 2, pSort);
            _rbLapSortBest = MkRb("Best", 162, 2, pSort);
            tp.Controls.Add(pSort);

            Panel pFin = new Panel { Location = new Point(110, 350), Size = new Size(260, 22) };
            tp.Controls.Add(MkLbl("Final Split:", 18, 354));
            _rbLapFinalLap = MkRb("LapTime", 0, 2, pFin);
            _rbLapFinalOff = MkRb("Official Time", 94, 2, pFin);
            tp.Controls.Add(pFin);

            tp.Controls.Add(MkLbl("Map File:", 18, 380));
            _txtLapMapFile = MkTb(112, 380, 286);
            Button btnMapBrowse = new Button { Text = "Browse...", Location = new Point(406, 376), Size = new Size(80, 23) };
            btnMapBrowse.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Map Files (*.map;*.txt;*.csv)|*.map;*.txt;*.csv|All Files|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK) _txtLapMapFile.Text = ofd.FileName;
                }
            };
            tp.Controls.Add(_txtLapMapFile);
            tp.Controls.Add(btnMapBrowse);
            return tp;
        }

        private static void SelectComboText(ComboBox cb, string text, int fallbackIndex)
        {
            if (cb == null || cb.Items.Count == 0) return;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (string.Equals(cb.Items[i].ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedIndex = i;
                    return;
                }
            }
            cb.SelectedIndex = Math.Max(0, Math.Min(cb.Items.Count - 1, fallbackIndex));
        }

        private void LoadFromConfig()
        {
            LynxConfigService cfg = LynxConfigService.Instance;
            LynxExtendedConfig ext = LynxExtendedConfig.Instance;

            // General
            _txtCompName.Text = cfg.CompetitionName ?? "";
            _txtCompSponsor.Text = cfg.CompetitionSponsor ?? "";
            SelectComboText(_cbCaptureBtn, cfg.CaptureButton, 0);
            _rbHwNone.Checked = (cfg.HardwareType == 0);
            _rbHwEth.Checked = (cfg.HardwareType == 1);
            _rbHwDetect.Checked = (cfg.HardwareType >= 2);
            SelectComboText(_cbCamNetwork, cfg.CameraNetwork, 0);
            SelectComboText(_cbFontName, cfg.FontName, 0);
            _txtFontSize.Text = cfg.FontSize.ToString(CultureInfo.InvariantCulture);
            _chkFontBold.Checked = cfg.FontBold;
            SelectComboText(_cbLanguage, Loc.CurrentLanguage, 0);
            SelectComboText(_cbProfile, cfg.Profile, 0);
            _chkEnableLaneGrid.Checked = cfg.ShowLaneGrid;
            _chkEnableShowLines.Checked = cfg.ShowEvaluatedLines;

            // Event
            int lay = Math.Max(0, Math.Min(5, cfg.LayoutSelection));
            _rbLayouts[lay].Checked = true;
            _rbSingleOn.Checked = ext.SingleEventMode; _rbSingleOff.Checked = !ext.SingleEventMode;
            _rbTtOn.Checked = ext.TimeTrialMode; _rbTtOff.Checked = !ext.TimeTrialMode;
            _rbTplNone.Checked = (cfg.EventTemplateMode == 0);
            _rbTplChoose.Checked = (cfg.EventTemplateMode == 1);
            _rbTplDefault.Checked = (cfg.EventTemplateMode == 2);
            _txtDefaultTpl.Text = cfg.DefaultTemplate ?? "";
            _rbNameTitle.Checked = (cfg.DefaultEventNameMode == 0);
            _rbNameErh.Checked = (cfg.DefaultEventNameMode != 0);
            _txtEventDir.Text = cfg.EventDir ?? "";
            _txtBackupDir.Text = cfg.BackupDir ?? "";
            _chkAutoSave.Checked = cfg.AutoSave;
            _numAutoSaveInterval.Value = Math.Max(5, Math.Min(3600, cfg.AutoSaveIntervalSeconds));

            // Image
            for (int i = 0; i < _chkImgIcons.Length; i++) _chkImgIcons[i].Checked = ext.ImageToolbarVisible[i];
            _rbLaneLeft.Checked = (ext.LaneBarSide == LaneBarPosition.Left);
            _rbLaneRight.Checked = (ext.LaneBarSide == LaneBarPosition.Right);
            _rbLaneBoth.Checked = (ext.LaneBarSide == LaneBarPosition.Both);
            _rbTrackOff.Checked = (ext.TimeTrackingMode == 0);
            _rbTrackSame.Checked = (ext.TimeTrackingMode == 1);
            _rbTrackAll.Checked = (ext.TimeTrackingMode >= 2);
            SelectComboText(_cbHashColor, ext.HashColorName, 0);
            _txtHashWidth.Text = ext.HashWidth.ToString(CultureInfo.InvariantCulture);
            _txtHashCrossWidth.Text = ext.HashCrossWidth.ToString(CultureInfo.InvariantCulture);
            _chkSmoothZoom.Checked = ext.SmoothZoom;
            SelectComboText(_cbShowLinesColor, ext.ShowLinesColorName, 0);
            SelectComboText(_cbSplitsMode, ext.ShowSplitsMode, 0);
            SelectComboText(_cbPrintLinesColor, ext.PrintLinesColorName, 0);
            _chkPrintSelected.Checked = ext.PrintSelectedByDefault;
            SelectComboText(_cbTimeGapLines, ext.TimeGapLinesMode, 0);
            _txtSelW.Text = ext.SelectionFixedWidth.ToString(CultureInfo.InvariantCulture);
            _txtSelH.Text = ext.SelectionFixedHeight.ToString(CultureInfo.InvariantCulture);
            _txtImgQuality.Text = ext.ImageExportJpegQuality.ToString(CultureInfo.InvariantCulture);
            _txtImgScale.Text = ext.ImageExportScale.ToString(CultureInfo.InvariantCulture);
            _txtVidQuality.Text = ext.VideoExportJpegQuality.ToString(CultureInfo.InvariantCulture);
            _txtVidScale.Text = ext.VideoExportScale.ToString(CultureInfo.InvariantCulture);
            _txtVidSpeed.Text = ext.VideoExportSpeed.ToString(CultureInfo.InvariantCulture);

            // Results
            for (int i = 0; i < _chkResIcons.Length; i++) _chkResIcons[i].Checked = ext.ResultsToolbarVisible[i];
            _lstFields.Items.Clear();
            for (int i = 0; i < ext.ResultFields.Count; i++) _lstFields.Items.Add(ext.ResultFields[i]);
            if (_lstFields.Items.Count > 0) _lstFields.SelectedIndex = 0;
            _rbLogBreaksOn.Checked = ext.LogBreaks; _rbLogBreaksOff.Checked = !ext.LogBreaks;
            _rbPrintBreaksOn.Checked = ext.PrintBreaks; _rbPrintBreaksOff.Checked = !ext.PrintBreaks;
            _txtLengthTime.Text = ext.LengthTimeSeconds.ToString("0.000", CultureInfo.InvariantCulture);
            _txtPackLeader.Text = cfg.AutoPackLeaders ? "1.000" : "0.000";
            SelectComboText(_cbDefaultPrecision, cfg.ResultsPrecision.ToString(), 3);
            _chkSuppressMin.Checked = ext.SuppressMinute;
            _txtPaceDist.Text = ext.PaceDistanceMeters.ToString("0", CultureInfo.InvariantCulture);
            _chkRoundMethod.Checked = ext.RoundMethodOverrideEnabled;
            _cbRoundMethod.Enabled = _chkRoundMethod.Checked;
            SelectComboText(_cbRoundMethod, ext.RoundMethodMode, 0);

            // Database
            _rbDbNone.Checked = (cfg.DataSourceMode == 0);
            _rbDbFiles.Checked = (cfg.DataSourceMode == 1);
            _rbDbSerial.Checked = (cfg.DataSourceMode >= 2);
            _rbDbSingle.Checked = (cfg.CodeSet == 0);
            _rbDbUnicode.Checked = (cfg.CodeSet == 1);
            _rbDbNative.Checked = (cfg.CodeSet >= 2);
            SelectComboText(_cbDbPrecision, ext.DatabaseTimePrecision, 0);
            _chkDbSuppressMin.Checked = ext.DatabaseSuppressMinute;
            _txtDataIn.Text = cfg.DataInDir ?? "";
            _txtDataOut.Text = cfg.DataOutDir ?? "";
            SelectComboText(_cbDbSerial, ext.DatabaseSerialPort, 0);
            SelectComboText(_cbDbBaud, ext.DatabaseBaud.ToString(CultureInfo.InvariantCulture), 1);
            SelectComboText(_cbDbBits, ext.DatabaseDataBits.ToString(CultureInfo.InvariantCulture), 1);
            SelectComboText(_cbDbParity, ext.DatabaseParity, 0);
            SelectComboText(_cbDbStop, ext.DatabaseStopBits, 0);
            _rbSerV1.Checked = (ext.DatabaseSerialVersion != 2);
            _rbSerV2.Checked = (ext.DatabaseSerialVersion == 2);
            _rbWriteLifOn.Checked = cfg.WriteLif; _rbWriteLifOff.Checked = !cfg.WriteLif;
            _chkLifDate.Checked = ext.LifIncludeDate;
            _chkLifText.Checked = ext.LifIncludeText;
            _txtLifText.Text = ext.LifCustomText ?? "";
            _chkLifFilters.Checked = ext.LifIncludeFilters;
            _rbLifSingle.Checked = true;
            SelectComboText(_cbReacTimePort, ext.ReacTimePort, 0);

            // Scoreboard
            _lstScoreboards.Items.Clear();
            for (int i = 0; i < ext.Scoreboards.Count; i++)
            {
                _lstScoreboards.Items.Add(ext.Scoreboards[i].Name + "    " + ext.Scoreboards[i].StatusText);
            }
            if (_lstScoreboards.Items.Count > 0) _lstScoreboards.SelectedIndex = 0;
            ScoreboardOutputConfig scb = ext.Scoreboards[0];
            SelectComboText(_cbScbScript, cfg.OutputScript ?? scb.ScriptFile, 0);
            _txtScbName.Text = scb.Name;
            _rbScbSingle.Checked = true;
            string portSel = (_hardware != null && !string.IsNullOrEmpty(_hardware.SerialPortName)) ? _hardware.SerialPortName : cfg.OutputSerialPort;
            SelectComboText(_cbScbPort, portSel, 0);
            SelectComboText(_cbScbBaud, cfg.OutputBaudRate.ToString(CultureInfo.InvariantCulture), 3);
            SelectComboText(_cbScbBits, cfg.OutputDataBits.ToString(CultureInfo.InvariantCulture), 1);
            SelectComboText(_cbScbParity, cfg.OutputParity ?? "None", 0);
            SelectComboText(_cbScbStop, cfg.OutputStopBits ?? "1.0", 0);
            _rbScbRtNormal.Checked = (scb.RunningTimeMode == "Normal");
            _rbScbRtRaw.Checked = (scb.RunningTimeMode == "Raw");
            _rbScbRtOff.Checked = (!_rbScbRtNormal.Checked && !_rbScbRtRaw.Checked);
            _txtScbPauseTime.Text = scb.PauseTime.ToString("0.0", CultureInfo.InvariantCulture);
            _txtScbOffset.Text = scb.OffsetSeconds.ToString("0.000", CultureInfo.InvariantCulture);
            _chkScbAutoPause.Checked = scb.AutoBreakPause;
            _chkScbAutoPauseCap.Checked = scb.AutoBreakPauseIfCapturing;
            _chkScbAutoFinish.Checked = scb.AutoBreakFinish;
            _chkScbAutoFinishCap.Checked = scb.AutoBreakFinishIfCapturing;
            _rbScbResAuto.Checked = (scb.ResultsMode == "Auto");
            _rbScbResManual.Checked = (scb.ResultsMode == "Manual");
            _rbScbResOff.Checked = (!_rbScbResAuto.Checked && !_rbScbResManual.Checked);
            _chkScbPaging.Checked = scb.PagingEnabled;
            _txtScbPageSize.Text = scb.PagingSize.ToString(CultureInfo.InvariantCulture);
            _txtScbPageMax.Text = scb.PagingMax ?? "";
            _txtScbPageTime.Text = scb.PagingTime.ToString("0.0", CultureInfo.InvariantCulture);
            SelectComboText(_cbScbPrecision, scb.TimePrecision, 0);
            _chkScbSuppressMin.Checked = scb.SuppressMinute;
            _chkScbAutoExport.Checked = cfg.OutputAutoExportImage;
            if (_hardware != null)
            {
                _chkEnableSerial.Checked = _hardware.EnableSerialScoreboard;
                _chkEnableUdp.Checked = _hardware.EnableUdpBroadcast;
            }

            // Wind
            SelectComboText(_cbWindModule, ext.WindModule, 0);
            SelectComboText(_cbWindPort, ext.WindSerialPort, 0);
            SelectComboText(_cbWindBaud, ext.WindBaud.ToString(CultureInfo.InvariantCulture), 3);
            SelectComboText(_cbWindBits, ext.WindDataBits.ToString(CultureInfo.InvariantCulture), 0);
            SelectComboText(_cbWindParity, ext.WindParity, 0);
            SelectComboText(_cbWindStop, ext.WindStopBits, 0);
            _cbWindMode.SelectedIndex = Math.Max(0, Math.Min(3, (int)ext.WindDefaultMode));
            _txtWindStart.Text = ext.WindDefaultStart.ToString("0.0", CultureInfo.InvariantCulture);
            _txtWindDuration.Text = ext.WindDefaultDuration.ToString("0.0", CultureInfo.InvariantCulture);
            _rbWindRemOn.Checked = ext.WindReminder; _rbWindRemOff.Checked = !ext.WindReminder;
            _rbWindWarnOn.Checked = ext.WindWarnIfOffline; _rbWindWarnOff.Checked = !ext.WindWarnIfOffline;

            // LapTime
            _lstLapDevices.Items.Clear();
            for (int i = 0; i < ext.LapTimeDevices.Count; i++)
            {
                _lstLapDevices.Items.Add(ext.LapTimeDevices[i].Name + "    " + ext.LapTimeDevices[i].StatusText);
            }
            if (_lstLapDevices.Items.Count > 0) _lstLapDevices.SelectedIndex = 0;
            LapTimeDeviceConfig ltd = ext.LapTimeDevices[0];
            SelectComboText(_cbLapType, ltd.DeviceType, 0);
            _txtLapName.Text = ltd.Name;
            SelectComboText(_cbLapPort, ltd.SerialPort, 0);
            SelectComboText(_cbLapBaud, ltd.BaudRate.ToString(CultureInfo.InvariantCulture), 1);
            SelectComboText(_cbLapBits, ltd.DataBits.ToString(CultureInfo.InvariantCulture), 1);
            SelectComboText(_cbLapParity, ltd.Parity, 0);
            SelectComboText(_cbLapStop, ltd.StopBits, 0);
            _rbLapAll.Checked = ltd.ActiveSplitsAll; _rbLapSel.Checked = !ltd.ActiveSplitsAll;
            _txtLapSelectedSplits.Text = ltd.SelectedSplits ?? "";
            _rbLapExt.Checked = (ltd.SyncSource == "External");
            _rbLapInt.Checked = (ltd.SyncSource == "Internal");
            _rbLapNone.Checked = (!_rbLapExt.Checked && !_rbLapInt.Checked);
            _txtLapOffset.Text = ltd.TimeOffsetSeconds.ToString("0.000", CultureInfo.InvariantCulture);
            _txtLapMaskStart.Text = ext.LapTimeMaskStartSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            _txtLapMaskDup.Text = ext.LapTimeMaskDuplicateSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            SelectComboText(_cbLapResultKey, ext.LapTimeResultKey, 0);
            _chkLapCreateRes.Checked = ext.LapTimeCreateResults;
            _chkLapFillPlace.Checked = ext.LapTimeFillInPlace;
            _rbLapSortCum.Checked = (ext.LapTimeSortBySplit == "Cumulative");
            _rbLapSortLast.Checked = (ext.LapTimeSortBySplit == "Last");
            _rbLapSortBest.Checked = (ext.LapTimeSortBySplit == "Best");
            _rbLapFinalLap.Checked = (ext.LapTimeFinalSplit == "LapTime");
            _rbLapFinalOff.Checked = !_rbLapFinalLap.Checked;
            _txtLapMapFile.Text = ext.LapTimeMapFile ?? "";
        }

        private void SaveToConfig()
        {
            LynxConfigService cfg = LynxConfigService.Instance;
            LynxExtendedConfig ext = LynxExtendedConfig.Instance;
            int iv;
            double dv;

            // General
            cfg.CompetitionName = _txtCompName.Text.Trim();
            cfg.CompetitionSponsor = _txtCompSponsor.Text.Trim();
            cfg.CaptureButton = _cbCaptureBtn.SelectedItem != null ? _cbCaptureBtn.SelectedItem.ToString() : "Keyboard";
            cfg.HardwareType = _rbHwNone.Checked ? 0 : (_rbHwEth.Checked ? 1 : 2);
            cfg.CameraNetwork = _cbCamNetwork.SelectedItem != null ? _cbCamNetwork.SelectedItem.ToString() : "<not specified>";
            cfg.FontName = _cbFontName.SelectedItem != null ? _cbFontName.SelectedItem.ToString() : "<default>";
            if (int.TryParse(_txtFontSize.Text, out iv)) cfg.FontSize = Math.Max(8, Math.Min(24, iv));
            cfg.FontBold = _chkFontBold.Checked;
            cfg.Language = _cbLanguage.SelectedItem != null ? _cbLanguage.SelectedItem.ToString() : "English (US)";
            cfg.Profile = _cbProfile.SelectedItem != null ? _cbProfile.SelectedItem.ToString() : "<standard>";
            cfg.ShowLaneGrid = _chkEnableLaneGrid.Checked;
            cfg.ShowEvaluatedLines = _chkEnableShowLines.Checked;

            // Event
            for (int i = 0; i < 6; i++) { if (_rbLayouts[i].Checked) cfg.LayoutSelection = i; }
            ext.SingleEventMode = _rbSingleOn.Checked;
            ext.TimeTrialMode = _rbTtOn.Checked;
            cfg.EventTemplateMode = _rbTplNone.Checked ? 0 : (_rbTplChoose.Checked ? 1 : 2);
            cfg.DefaultTemplate = _txtDefaultTpl.Text.Trim();
            cfg.DefaultEventNameMode = _rbNameTitle.Checked ? 0 : 1;
            cfg.EventDir = _txtEventDir.Text.Trim();
            cfg.BackupDir = _txtBackupDir.Text.Trim();
            cfg.AutoSave = _chkAutoSave.Checked;
            cfg.AutoSaveIntervalSeconds = (int)_numAutoSaveInterval.Value;

            // Image
            for (int i = 0; i < _chkImgIcons.Length; i++) ext.ImageToolbarVisible[i] = _chkImgIcons[i].Checked;
            ext.LaneBarSide = _rbLaneRight.Checked ? LaneBarPosition.Right : (_rbLaneBoth.Checked ? LaneBarPosition.Both : LaneBarPosition.Left);
            ext.TimeTrackingMode = _rbTrackOff.Checked ? 0 : (_rbTrackSame.Checked ? 1 : 2);
            if (_cbHashColor.SelectedItem != null) ext.HashColorName = _cbHashColor.SelectedItem.ToString();
            if (int.TryParse(_txtHashWidth.Text, out iv)) ext.HashWidth = Math.Max(1, Math.Min(5, iv));
            if (int.TryParse(_txtHashCrossWidth.Text, out iv)) ext.HashCrossWidth = Math.Max(5, Math.Min(80, iv));
            ext.SmoothZoom = _chkSmoothZoom.Checked;
            if (_cbShowLinesColor.SelectedItem != null) ext.ShowLinesColorName = _cbShowLinesColor.SelectedItem.ToString();
            if (_cbSplitsMode.SelectedItem != null) ext.ShowSplitsMode = _cbSplitsMode.SelectedItem.ToString();
            if (_cbPrintLinesColor.SelectedItem != null) ext.PrintLinesColorName = _cbPrintLinesColor.SelectedItem.ToString();
            ext.PrintSelectedByDefault = _chkPrintSelected.Checked;
            if (_cbTimeGapLines.SelectedItem != null) ext.TimeGapLinesMode = _cbTimeGapLines.SelectedItem.ToString();
            if (int.TryParse(_txtSelW.Text, out iv)) ext.SelectionFixedWidth = iv;
            if (int.TryParse(_txtSelH.Text, out iv)) ext.SelectionFixedHeight = iv;

            // Results
            for (int i = 0; i < _chkResIcons.Length; i++) ext.ResultsToolbarVisible[i] = _chkResIcons[i].Checked;
            ext.ResultFields.Clear();
            for (int i = 0; i < _lstFields.Items.Count; i++)
            {
                ResultFieldConfig rfc = _lstFields.Items[i] as ResultFieldConfig;
                if (rfc != null) ext.ResultFields.Add(rfc);
            }
            ext.LogBreaks = _rbLogBreaksOn.Checked;
            ext.PrintBreaks = _rbPrintBreaksOn.Checked;
            if (double.TryParse(_txtLengthTime.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv) && dv > 0) ext.LengthTimeSeconds = dv;
            if (double.TryParse(_txtPackLeader.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) cfg.AutoPackLeaders = (dv > 0.0);
            if (_cbDefaultPrecision.SelectedItem != null)
            {
                TimePrecision parsedPrec;
                if (Enum.TryParse(_cbDefaultPrecision.SelectedItem.ToString(), out parsedPrec)) cfg.ResultsPrecision = parsedPrec;
            }
            ext.SuppressMinute = _chkSuppressMin.Checked;
            if (double.TryParse(_txtPaceDist.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) ext.PaceDistanceMeters = dv;
            ext.RoundMethodOverrideEnabled = _chkRoundMethod.Checked;
            if (_cbRoundMethod.SelectedItem != null) ext.RoundMethodMode = _cbRoundMethod.SelectedItem.ToString();

            // Database
            cfg.DataSourceMode = _rbDbNone.Checked ? 0 : (_rbDbFiles.Checked ? 1 : 2);
            cfg.CodeSet = _rbDbSingle.Checked ? 0 : (_rbDbUnicode.Checked ? 1 : 2);
            if (_cbDbPrecision.SelectedItem != null) ext.DatabaseTimePrecision = _cbDbPrecision.SelectedItem.ToString();
            ext.DatabaseSuppressMinute = _chkDbSuppressMin.Checked;
            cfg.DataInDir = _txtDataIn.Text.Trim();
            cfg.DataOutDir = _txtDataOut.Text.Trim();
            cfg.WriteLif = _rbWriteLifOn.Checked;
            if (_cbDbSerial.SelectedItem != null) ext.DatabaseSerialPort = _cbDbSerial.SelectedItem.ToString();
            if (_cbDbBaud.SelectedItem != null && int.TryParse(_cbDbBaud.SelectedItem.ToString(), out iv)) ext.DatabaseBaud = iv;
            if (_cbReacTimePort.SelectedItem != null) ext.ReacTimePort = _cbReacTimePort.SelectedItem.ToString();

            // Scoreboard
            ScoreboardOutputConfig scb = ext.Scoreboards[0];
            scb.Name = _txtScbName.Text.Trim();
            if (_cbScbScript.SelectedItem != null) { cfg.OutputScript = _cbScbScript.SelectedItem.ToString(); scb.ScriptFile = cfg.OutputScript; }
            string sp = _cbScbPort.SelectedItem != null ? _cbScbPort.SelectedItem.ToString() : "<none>";
            cfg.OutputSerialPort = sp;
            scb.SerialPort = sp;
            if (_cbScbBaud.SelectedItem != null && int.TryParse(_cbScbBaud.SelectedItem.ToString(), out iv)) { cfg.OutputBaudRate = iv; scb.BaudRate = iv; }
            if (_cbScbBits.SelectedItem != null && int.TryParse(_cbScbBits.SelectedItem.ToString(), out iv)) { cfg.OutputDataBits = iv; scb.DataBits = iv; }
            if (_cbScbParity.SelectedItem != null) { cfg.OutputParity = _cbScbParity.SelectedItem.ToString(); scb.Parity = cfg.OutputParity; }
            if (_cbScbStop.SelectedItem != null) { cfg.OutputStopBits = _cbScbStop.SelectedItem.ToString(); scb.StopBits = cfg.OutputStopBits; }
            scb.RunningTimeMode = _rbScbRtNormal.Checked ? "Normal" : (_rbScbRtRaw.Checked ? "Raw" : "Off");
            scb.ResultsMode = _rbScbResAuto.Checked ? "Auto" : (_rbScbResManual.Checked ? "Manual" : "Off");
            scb.PagingEnabled = _chkScbPaging.Checked;
            if (int.TryParse(_txtScbPageSize.Text, out iv)) scb.PagingSize = iv;
            if (double.TryParse(_txtScbPageTime.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) scb.PagingTime = dv;
            cfg.OutputAutoExportImage = _chkScbAutoExport.Checked;

            if (_hardware != null)
            {
                _hardware.EnableSerialScoreboard = _chkEnableSerial.Checked;
                _hardware.EnableUdpBroadcast = _chkEnableUdp.Checked;
                _hardware.SerialPortName = sp;
                _hardware.BaudRate = cfg.OutputBaudRate;
            }

            // Wind
            if (_cbWindModule.SelectedItem != null) ext.WindModule = _cbWindModule.SelectedItem.ToString();
            if (_cbWindPort.SelectedItem != null) ext.WindSerialPort = _cbWindPort.SelectedItem.ToString();
            if (_cbWindBaud.SelectedItem != null && int.TryParse(_cbWindBaud.SelectedItem.ToString(), out iv)) ext.WindBaud = iv;
            ext.WindDefaultMode = (WindWindowMode)Math.Max(0, _cbWindMode.SelectedIndex);
            if (double.TryParse(_txtWindDuration.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) ext.WindDefaultDuration = dv;
            ext.WindReminder = _rbWindRemOn.Checked;
            ext.WindWarnIfOffline = _rbWindWarnOn.Checked;

            // LapTime
            LapTimeDeviceConfig ltd = ext.LapTimeDevices[0];
            ltd.Name = _txtLapName.Text.Trim();
            if (_cbLapType.SelectedItem != null) ltd.DeviceType = _cbLapType.SelectedItem.ToString();
            if (_cbLapPort.SelectedItem != null) ltd.SerialPort = _cbLapPort.SelectedItem.ToString();
            ltd.ActiveSplitsAll = _rbLapAll.Checked;
            ltd.SelectedSplits = _txtLapSelectedSplits.Text;
            if (double.TryParse(_txtLapMaskStart.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) ext.LapTimeMaskStartSeconds = dv;
            if (double.TryParse(_txtLapMaskDup.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) ext.LapTimeMaskDuplicateSeconds = dv;
            if (_cbLapResultKey.SelectedItem != null) ext.LapTimeResultKey = _cbLapResultKey.SelectedItem.ToString();
            ext.LapTimeCreateResults = _chkLapCreateRes.Checked;
            ext.LapTimeFillInPlace = _chkLapFillPlace.Checked;
            ext.LapTimeSortBySplit = _rbLapSortLast.Checked ? "Last" : (_rbLapSortBest.Checked ? "Best" : "Cumulative");
            ext.LapTimeFinalSplit = _rbLapFinalLap.Checked ? "LapTime" : "Official Time";
            ext.LapTimeMapFile = _txtLapMapFile.Text.Trim();

            cfg.Save();
            ext.Save();
            Loc.SetLanguage(cfg.Language);
        }
    }
}
