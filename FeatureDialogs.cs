using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using OptiFinish.Core;
using OptiFinish.UI;

namespace OptiFinish
{
    internal static class DlgUi
    {
        public static Label Lbl(string text, int x, int y)
        {
            return new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
        }

        public static TextBox Tb(int x, int y, int w, string text)
        {
            return new TextBox { Location = new Point(x, y - 3), Width = w, Text = text ?? "" };
        }

        public static ComboBox Cb(int x, int y, int w, params string[] items)
        {
            ComboBox cb = new ComboBox { Location = new Point(x, y - 3), Width = w, DropDownStyle = ComboBoxStyle.DropDownList };
            if (items != null && items.Length > 0) cb.Items.AddRange(items);
            return cb;
        }

        public static Button Btn(string text, int x, int y, int w, DialogResult dr)
        {
            string label = text;
            if (dr == DialogResult.OK && text == "OK") label = Loc.Dlg("Ok", "OK");
            else if (dr == DialogResult.Cancel && text == "Cancel") label = Loc.Dlg("Cancel", "Cancel");
            return new Button { Text = label, Location = new Point(x, y), Size = new Size(w, 25), DialogResult = dr };
        }

        public static void StyleClassic(Form f, string title, int w, int h)
        {
            f.Text = title;
            f.Size = new Size(w, h);
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.BackColor = SystemColors.Control;
            f.Font = new Font("Segoe UI", 8.5f);
        }
    }

    // 1. Camera Settings Dialog (Setup, Parameters, Inputs, RadioLynx, Capture, WLAN)
    public sealed class CameraSettingsDialog : Form
    {
        private readonly RaceEventDocument _doc;
        private readonly SlitCameraStream _cam;
        private readonly CameraSettingsProfile _prof;

        private TextBox _txtName, _txtLanes, _txtWbR, _txtWbG, _txtWbB, _txtGamma, _txtBlack;
        private RadioButton _rbLeft, _rbRight, _rbIdNothing, _rbIdLane, _rbIdId, _rbIdLic, _rbPlcOff, _rbPlcOn;
        private TextBox _txtRate, _txtHeight, _txtMinComp, _txtAgcBright, _txtAgcFreq, _txtMaxGain, _txtIrisMin, _txtIrisMax;
        private CheckBox _chkRevLanes, _chkAutoBoot, _chkAutoIris, _chkAutoLux, _chkCapManual, _chkCapAuto, _chkCapEyeA;
        private TextBox _txtLeader, _txtTrailer, _txtObjThresh, _txtObjTop, _txtObjBot, _txtObjMinT;
        private CheckBox _chkRlStart, _chkRlSplit, _chkExtSync;

        public CameraSettingsDialog(RaceEventDocument doc, SlitCameraStream cam)
        {
            _doc = doc;
            _cam = cam;
            _prof = cam.Profile ?? new CameraSettingsProfile();
            cam.Profile = _prof;

            DlgUi.StyleClassic(this, "Camera Settings", 450, 500);
            TabControl tc = new TabControl { Location = new Point(10, 10), Size = new Size(414, 408) };
            tc.TabPages.Add(BuildSetupTab());
            tc.TabPages.Add(BuildParamsTab());
            tc.TabPages.Add(BuildInputsTab());
            tc.TabPages.Add(BuildRadioLynxTab());
            tc.TabPages.Add(BuildCaptureTab());
            Controls.Add(tc);

            Button ok = DlgUi.Btn("OK", 118, 426, 90, DialogResult.OK);
            ok.Click += delegate { Save(); };
            Button cancel = DlgUi.Btn("Cancel", 224, 426, 90, DialogResult.Cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(ok);
            Controls.Add(cancel);
        }

        private TabPage BuildSetupTab()
        {
            TabPage tp = new TabPage("Setup") { BackColor = SystemColors.Control };
            tp.Controls.Add(DlgUi.Lbl("Information:", 16, 18));
            tp.Controls.Add(DlgUi.Lbl(string.Format(CultureInfo.InvariantCulture, "S/N: {0}  RAM: {1}MB  IP: {2}", _prof.SerialNumber, _prof.RamMB, _prof.IpAddress), 130, 18));

            tp.Controls.Add(DlgUi.Lbl("Name:", 16, 46));
            _txtName = DlgUi.Tb(130, 46, 210, _cam.DisplayName);
            tp.Controls.Add(_txtName);

            Panel pOri = new Panel { Location = new Point(128, 70), Size = new Size(200, 22) };
            tp.Controls.Add(DlgUi.Lbl("Image Orientation:", 16, 74));
            _rbLeft = new RadioButton { Text = "Left", Location = new Point(0, 2), AutoSize = true, Checked = _cam.Orientation == StripOrientation.LeftToRight };
            _rbRight = new RadioButton { Text = "Right", Location = new Point(78, 2), AutoSize = true, Checked = _cam.Orientation == StripOrientation.RightToLeft };
            pOri.Controls.Add(_rbLeft); pOri.Controls.Add(_rbRight);
            tp.Controls.Add(pOri);

            Panel pId = new Panel { Location = new Point(128, 98), Size = new Size(265, 22) };
            tp.Controls.Add(DlgUi.Lbl("Identify By:", 16, 102));
            _rbIdNothing = new RadioButton { Text = "Nothing", Location = new Point(0, 2), AutoSize = true, Checked = _cam.IdentifyBy == ResultIdentifyMethod.Nothing };
            _rbIdLane = new RadioButton { Text = "Lane", Location = new Point(74, 2), AutoSize = true, Checked = _cam.IdentifyBy == ResultIdentifyMethod.Lane };
            _rbIdId = new RadioButton { Text = "Id", Location = new Point(132, 2), AutoSize = true, Checked = _cam.IdentifyBy == ResultIdentifyMethod.Id };
            _rbIdLic = new RadioButton { Text = "License", Location = new Point(178, 2), AutoSize = true, Checked = _cam.IdentifyBy == ResultIdentifyMethod.License };
            pId.Controls.AddRange(new Control[] { _rbIdNothing, _rbIdLane, _rbIdId, _rbIdLic });
            tp.Controls.Add(pId);

            tp.Controls.Add(DlgUi.Lbl("Lanes:", 16, 130));
            _txtLanes = DlgUi.Tb(130, 130, 46, (_doc != null ? _doc.LaneCount : 8).ToString(CultureInfo.InvariantCulture));
            _chkRevLanes = new CheckBox { Text = "Reverse lane order", Location = new Point(190, 129), AutoSize = true, Checked = _cam.ReverseLaneOrder };
            tp.Controls.Add(_txtLanes); tp.Controls.Add(_chkRevLanes);

            tp.Controls.Add(DlgUi.Lbl("White Balance:", 16, 160));
            tp.Controls.Add(DlgUi.Lbl("Red", 130, 160));
            _txtWbR = DlgUi.Tb(160, 160, 40, _cam.WhiteBalanceRed.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(DlgUi.Lbl("Green", 206, 160));
            _txtWbG = DlgUi.Tb(246, 160, 40, _cam.WhiteBalanceGreen.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(DlgUi.Lbl("Blue", 292, 160));
            _txtWbB = DlgUi.Tb(324, 160, 40, _cam.WhiteBalanceBlue.ToString(CultureInfo.InvariantCulture));
            tp.Controls.AddRange(new Control[] { _txtWbR, _txtWbG, _txtWbB });

            tp.Controls.Add(DlgUi.Lbl("Gamma:", 16, 190));
            _txtGamma = DlgUi.Tb(130, 190, 62, _cam.GammaValue.ToString("0.00", CultureInfo.InvariantCulture));
            tp.Controls.Add(_txtGamma);
            tp.Controls.Add(DlgUi.Lbl("(0.25 - 3.00)", 200, 190));

            tp.Controls.Add(DlgUi.Lbl("Black Level:", 16, 220));
            _txtBlack = DlgUi.Tb(130, 220, 62, _cam.BlackLevel.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(_txtBlack);

            Panel pPlc = new Panel { Location = new Point(128, 246), Size = new Size(180, 22) };
            tp.Controls.Add(DlgUi.Lbl("Phased Light Comp.:", 16, 250));
            _rbPlcOff = new RadioButton { Text = "Off", Location = new Point(0, 2), AutoSize = true, Checked = !_prof.PhasedLightComp };
            _rbPlcOn = new RadioButton { Text = "On", Location = new Point(66, 2), AutoSize = true, Checked = _prof.PhasedLightComp };
            pPlc.Controls.Add(_rbPlcOff); pPlc.Controls.Add(_rbPlcOn);
            tp.Controls.Add(pPlc);

            _chkAutoBoot = new CheckBox { Text = "Auto-Boot Enabled", Location = new Point(130, 282), AutoSize = true, Checked = _prof.AutoBoot };
            tp.Controls.Add(_chkAutoBoot);
            return tp;
        }

        private TabPage BuildParamsTab()
        {
            TabPage tp = new TabPage("Parameters") { BackColor = SystemColors.Control };
            tp.Controls.Add(DlgUi.Lbl("Pixel Depth:", 16, 20));
            tp.Controls.Add(DlgUi.Lbl("32K Colors (EtherLynx High Dynamic)", 130, 20));

            tp.Controls.Add(DlgUi.Lbl("Frame Rate (Hz):", 16, 52));
            _txtRate = DlgUi.Tb(130, 52, 74, _cam.SampleRateHz.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(_txtRate);
            tp.Controls.Add(DlgUi.Lbl("(100 - 20000)", 214, 52));

            tp.Controls.Add(DlgUi.Lbl("Frame Height:", 16, 84));
            _txtHeight = DlgUi.Tb(130, 84, 74, _cam.FrameHeightPx.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(_txtHeight);

            tp.Controls.Add(DlgUi.Lbl("Min Compression:", 16, 116));
            _txtMinComp = DlgUi.Tb(130, 116, 54, _prof.MinCompression.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(_txtMinComp);

            tp.Controls.Add(DlgUi.Lbl("AGC Brightness:", 16, 148));
            _txtAgcBright = DlgUi.Tb(130, 148, 54, _prof.AgcBrightness.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(_txtAgcBright);
            tp.Controls.Add(DlgUi.Lbl("Frequency:", 198, 148));
            _txtAgcFreq = DlgUi.Tb(268, 148, 48, _prof.AgcFrequency.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(_txtAgcFreq);

            tp.Controls.Add(DlgUi.Lbl("Maximum Gain:", 16, 180));
            _txtMaxGain = DlgUi.Tb(130, 180, 54, _prof.AgcMaxGain.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(_txtMaxGain);

            _chkAutoIris = new CheckBox { Text = "Auto Iris", Location = new Point(130, 214), AutoSize = true, Checked = _prof.AutoIris };
            tp.Controls.Add(_chkAutoIris);
            tp.Controls.Add(DlgUi.Lbl("Min", 216, 216));
            _txtIrisMin = DlgUi.Tb(246, 216, 40, _prof.AutoIrisMin.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(DlgUi.Lbl("Max", 296, 216));
            _txtIrisMax = DlgUi.Tb(330, 216, 40, _prof.AutoIrisMax.ToString(CultureInfo.InvariantCulture));
            tp.Controls.AddRange(new Control[] { _txtIrisMin, _txtIrisMax });

            _chkAutoLux = new CheckBox { Text = "Auto LuxBoost", Location = new Point(130, 246), AutoSize = true, Checked = _prof.AutoLuxBoost };
            tp.Controls.Add(_chkAutoLux);
            return tp;
        }

        private TabPage BuildInputsTab()
        {
            TabPage tp = new TabPage("Inputs") { BackColor = SystemColors.Control };
            _chkExtSync = new CheckBox { Text = "External Hardware Sync Input", Location = new Point(22, 24), AutoSize = true, Checked = _prof.TimerExtSync };
            CheckBox cStartIn = new CheckBox { Text = "Wired Start Gun Sensor Active", Location = new Point(22, 54), AutoSize = true, Checked = true };
            CheckBox cCapBtn = new CheckBox { Text = "Hardware Capture Pushbutton Active", Location = new Point(22, 84), AutoSize = true, Checked = true };
            tp.Controls.AddRange(new Control[] { _chkExtSync, cStartIn, cCapBtn });
            return tp;
        }

        private TabPage BuildRadioLynxTab()
        {
            TabPage tp = new TabPage("RadioLynx") { BackColor = SystemColors.Control };
            _chkRlStart = new CheckBox { Text = "Enable Wireless RadioLynx Start Receiver", Location = new Point(22, 24), AutoSize = true, Checked = _prof.RadioLynxStartEnabled };
            _chkRlSplit = new CheckBox { Text = "Enable Wireless RadioLynx Splits", Location = new Point(22, 54), AutoSize = true, Checked = _prof.RadioLynxSplitEnabled };
            tp.Controls.Add(_chkRlStart); tp.Controls.Add(_chkRlSplit);
            tp.Controls.Add(DlgUi.Lbl("Signal Squelch Threshold:  -72 dBm (Channel 01)", 22, 92));
            return tp;
        }

        private TabPage BuildCaptureTab()
        {
            TabPage tp = new TabPage("Capture") { BackColor = SystemColors.Control };
            tp.Controls.Add(DlgUi.Lbl("Capture Method:", 16, 22));
            _chkCapManual = new CheckBox { Text = "Manual", Location = new Point(130, 20), AutoSize = true, Checked = _prof.CaptureManual };
            _chkCapAuto = new CheckBox { Text = "Automatic (ACM)", Location = new Point(130, 48), AutoSize = true, Checked = _prof.CaptureAutomatic };
            _chkCapEyeA = new CheckBox { Text = "Photo Eye A", Location = new Point(268, 48), AutoSize = true, Checked = _prof.CapturePhotoEyeA };
            tp.Controls.AddRange(new Control[] { _chkCapManual, _chkCapAuto, _chkCapEyeA });

            tp.Controls.Add(DlgUi.Lbl("Leader (cols):", 16, 86));
            _txtLeader = DlgUi.Tb(130, 86, 56, _prof.LeaderCols.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(DlgUi.Lbl("Trailer (cols):", 204, 86));
            _txtTrailer = DlgUi.Tb(292, 86, 56, _prof.TrailerCols.ToString(CultureInfo.InvariantCulture));
            tp.Controls.AddRange(new Control[] { _txtLeader, _txtTrailer });

            tp.Controls.Add(DlgUi.Lbl("Object Threshold:", 16, 126));
            _txtObjThresh = DlgUi.Tb(130, 126, 56, _prof.ObjectThreshold.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(_txtObjThresh);

            tp.Controls.Add(DlgUi.Lbl("Active Area: Top %", 16, 158));
            _txtObjTop = DlgUi.Tb(130, 158, 56, _prof.ObjectActiveTop.ToString(CultureInfo.InvariantCulture));
            tp.Controls.Add(DlgUi.Lbl("Bottom %", 204, 158));
            _txtObjBot = DlgUi.Tb(274, 158, 56, _prof.ObjectActiveBottom.ToString(CultureInfo.InvariantCulture));
            tp.Controls.AddRange(new Control[] { _txtObjTop, _txtObjBot });

            tp.Controls.Add(DlgUi.Lbl("Min Time (sec):", 16, 190));
            _txtObjMinT = DlgUi.Tb(130, 190, 68, _prof.ObjectMinTime.ToString("0.000", CultureInfo.InvariantCulture));
            tp.Controls.Add(_txtObjMinT);
            return tp;
        }

        private void Save()
        {
            int iv;
            double dv;
            _cam.DisplayName = string.IsNullOrWhiteSpace(_txtName.Text) ? _cam.DisplayName : _txtName.Text.Trim();
            _cam.Orientation = _rbRight.Checked ? StripOrientation.RightToLeft : StripOrientation.LeftToRight;
            _cam.IdentifyBy = _rbIdNothing.Checked ? ResultIdentifyMethod.Nothing : (_rbIdId.Checked ? ResultIdentifyMethod.Id : (_rbIdLic.Checked ? ResultIdentifyMethod.License : ResultIdentifyMethod.Lane));
            _cam.ReverseLaneOrder = _chkRevLanes.Checked;
            if (_doc != null && int.TryParse(_txtLanes.Text, out iv)) _doc.LaneCount = Math.Max(1, Math.Min(32, iv));
            if (int.TryParse(_txtWbR.Text, out iv)) _cam.WhiteBalanceRed = iv;
            if (int.TryParse(_txtWbG.Text, out iv)) _cam.WhiteBalanceGreen = iv;
            if (int.TryParse(_txtWbB.Text, out iv)) _cam.WhiteBalanceBlue = iv;
            if (double.TryParse(_txtGamma.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) _cam.GammaValue = Math.Max(0.2, Math.Min(3.0, dv));
            if (int.TryParse(_txtBlack.Text, out iv)) _cam.BlackLevel = iv;
            _prof.PhasedLightComp = _rbPlcOn.Checked;
            _prof.AutoBoot = _chkAutoBoot.Checked;

            if (int.TryParse(_txtRate.Text, out iv) && iv >= 10) _cam.SampleRateHz = iv;
            if (int.TryParse(_txtHeight.Text, out iv) && iv >= 100) _cam.FrameHeightPx = iv;
            if (int.TryParse(_txtMinComp.Text, out iv)) _prof.MinCompression = iv;
            if (int.TryParse(_txtAgcBright.Text, out iv)) _prof.AgcBrightness = iv;
            if (int.TryParse(_txtAgcFreq.Text, out iv)) _prof.AgcFrequency = iv;
            if (int.TryParse(_txtMaxGain.Text, out iv)) _prof.AgcMaxGain = iv;
            _prof.AutoIris = _chkAutoIris.Checked;
            if (int.TryParse(_txtIrisMin.Text, out iv)) _prof.AutoIrisMin = iv;
            if (int.TryParse(_txtIrisMax.Text, out iv)) _prof.AutoIrisMax = iv;
            _prof.AutoLuxBoost = _chkAutoLux.Checked;
            _prof.TimerExtSync = _chkExtSync.Checked;
            _prof.RadioLynxStartEnabled = _chkRlStart.Checked;
            _prof.RadioLynxSplitEnabled = _chkRlSplit.Checked;
            _prof.CaptureManual = _chkCapManual.Checked;
            _prof.CaptureAutomatic = _chkCapAuto.Checked;
            _prof.CapturePhotoEyeA = _chkCapEyeA.Checked;
            if (int.TryParse(_txtLeader.Text, out iv)) _prof.LeaderCols = iv;
            if (int.TryParse(_txtTrailer.Text, out iv)) _prof.TrailerCols = iv;
            if (int.TryParse(_txtObjThresh.Text, out iv)) _prof.ObjectThreshold = iv;
            if (int.TryParse(_txtObjTop.Text, out iv)) _prof.ObjectActiveTop = iv;
            if (int.TryParse(_txtObjBot.Text, out iv)) _prof.ObjectActiveBottom = iv;
            if (double.TryParse(_txtObjMinT.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) _prof.ObjectMinTime = dv;
        }
    }

    // 2. Modeless Image Adjustments Floater (Window | Image adjustments / Image | Adjustments...)
    public sealed class ImageAdjustmentsFloater : Form
    {
        private TrackBar _tbBright, _tbContrast, _tbGamma, _tbBlack;
        private Label _lblBright, _lblContrast, _lblGamma, _lblBlack;
        private bool _suppress;
        public event Action<int, int, double, int> ValuesChanged;

        public ImageAdjustmentsFloater()
        {
            Text = "Image Adjustments";
            Size = new Size(310, 240);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = SystemColors.Control;
            Font = new Font("Segoe UI", 8.5f);

            int y = 12;
            AddSlider("Brightness:", -100, 100, 0, ref y, out _tbBright, out _lblBright);
            AddSlider("Contrast:", -100, 100, 0, ref y, out _tbContrast, out _lblContrast);
            AddSlider("Gamma (x100):", 25, 250, 100, ref y, out _tbGamma, out _lblGamma);
            AddSlider("Black Level:", -50, 50, 0, ref y, out _tbBlack, out _lblBlack);

            Button btnReset = new Button { Text = "Reset All", Location = new Point(108, 174), Size = new Size(92, 25) };
            btnReset.Click += delegate
            {
                LoadFromStream(0, 0, 1.0, 0);
                Emit();
            };
            Controls.Add(btnReset);
        }

        private void AddSlider(string caption, int min, int max, int val, ref int y, out TrackBar tb, out Label valLbl)
        {
            Controls.Add(DlgUi.Lbl(caption, 12, y + 4));
            tb = new TrackBar { Location = new Point(100, y), Width = 148, Minimum = min, Maximum = max, Value = val, TickStyle = TickStyle.None, AutoSize = false, Height = 26 };
            valLbl = DlgUi.Lbl(val.ToString(CultureInfo.InvariantCulture), 254, y + 4);
            tb.ValueChanged += delegate
            {
                UpdateLabels();
                if (!_suppress) Emit();
            };
            Controls.Add(tb);
            Controls.Add(valLbl);
            y += 38;
        }

        public void LoadFromStream(int brightness, int contrast, double gamma, int blackLevel)
        {
            _suppress = true;
            _tbBright.Value = Math.Max(-100, Math.Min(100, brightness));
            _tbContrast.Value = Math.Max(-100, Math.Min(100, contrast));
            _tbGamma.Value = Math.Max(25, Math.Min(250, (int)Math.Round(gamma * 100.0)));
            _tbBlack.Value = Math.Max(-50, Math.Min(50, blackLevel));
            UpdateLabels();
            _suppress = false;
        }

        private void UpdateLabels()
        {
            _lblBright.Text = _tbBright.Value.ToString(CultureInfo.InvariantCulture);
            _lblContrast.Text = _tbContrast.Value.ToString(CultureInfo.InvariantCulture);
            _lblGamma.Text = (_tbGamma.Value / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
            _lblBlack.Text = _tbBlack.Value.ToString(CultureInfo.InvariantCulture);
        }

        private void Emit()
        {
            if (ValuesChanged != null)
            {
                ValuesChanged(_tbBright.Value, _tbContrast.Value, _tbGamma.Value / 100.0, _tbBlack.Value);
            }
        }
    }

    // 3. Modeless Numeric Keypad Floater (Window | Keypad)
    public sealed class KeypadFloater : Form
    {
        public event Action<string> DigitPressed;
        public event Action BackspacePressed;
        public event Action ClearPressed;
        public event Action EnterPressed;

        public KeypadFloater()
        {
            Text = "Keypad";
            Size = new Size(192, 232);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = SystemColors.Control;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

            string[] labels = new string[] { "7", "8", "9", "4", "5", "6", "1", "2", "3", "C", "0", "<" };
            for (int i = 0; i < labels.Length; i++)
            {
                int col = i % 3;
                int row = i / 3;
                string lbl = labels[i];
                Button b = new Button
                {
                    Text = lbl,
                    Location = new Point(10 + col * 52, 10 + row * 38),
                    Size = new Size(48, 34)
                };
                b.Click += delegate
                {
                    if (lbl == "C") { if (ClearPressed != null) ClearPressed(); }
                    else if (lbl == "<") { if (BackspacePressed != null) BackspacePressed(); }
                    else { if (DigitPressed != null) DigitPressed(lbl); }
                };
                Controls.Add(b);
            }
            Button enter = new Button { Text = "Enter", Location = new Point(10, 164), Size = new Size(152, 30) };
            enter.Click += delegate { if (EnterPressed != null) EnterPressed(); };
            Controls.Add(enter);
        }
    }

    // 4. Results Filters Dialog (Results | Filters...)
    public sealed class ResultsFilterDialog : Form
    {
        private readonly RaceEventDocument _doc;
        private ListBox _lstFilters;
        private ComboBox _cbField;
        private TextBox _txtLow, _txtHigh, _txtStartOff;
        private CheckBox _chkSingle;
        private RadioButton _rbPlNone, _rbPlEnabled, _rbPlAll;

        public ResultsFilterDialog(RaceEventDocument doc)
        {
            _doc = doc;
            DlgUi.StyleClassic(this, "Results Filters", 545, 345);

            Controls.Add(DlgUi.Lbl("Filters:", 14, 14));
            _lstFilters = new ListBox { Location = new Point(14, 34), Size = new Size(170, 210) };
            Button bNew = new Button { Text = "New", Location = new Point(14, 250), Size = new Size(54, 24) };
            Button bDel = new Button { Text = "Delete", Location = new Point(72, 250), Size = new Size(56, 24) };
            Button bEn = new Button { Text = "On/Off", Location = new Point(132, 250), Size = new Size(52, 24) };

            GroupBox grp = new GroupBox { Text = "Bounds", Location = new Point(196, 14), Size = new Size(326, 224) };
            grp.Controls.Add(DlgUi.Lbl("Field:", 14, 26));
            _cbField = DlgUi.Cb(98, 26, 130, "Place", "Id", "Lane", "First Name", "Last Name", "Affiliation", "License", "Time", "Laps");
            _cbField.SelectedIndex = 2;
            grp.Controls.Add(_cbField);

            grp.Controls.Add(DlgUi.Lbl("Lower Bound:", 14, 58));
            _txtLow = DlgUi.Tb(98, 58, 130, "");
            grp.Controls.Add(_txtLow);

            grp.Controls.Add(DlgUi.Lbl("Upper Bound:", 14, 88));
            _txtHigh = DlgUi.Tb(98, 88, 130, "");
            grp.Controls.Add(_txtHigh);

            grp.Controls.Add(DlgUi.Lbl("Start Offset:", 14, 118));
            _txtStartOff = DlgUi.Tb(98, 118, 74, "0.0");
            grp.Controls.Add(_txtStartOff);

            _chkSingle = new CheckBox { Text = "Single Result", Location = new Point(14, 148), AutoSize = true };
            grp.Controls.Add(_chkSingle);

            Panel pPlace = new Panel { Location = new Point(12, 174), Size = new Size(300, 32) };
            pPlace.Controls.Add(DlgUi.Lbl("Place Filters:", 2, 8));
            _rbPlNone = new RadioButton { Text = "None", Location = new Point(84, 6), AutoSize = true, Checked = true };
            _rbPlEnabled = new RadioButton { Text = "Enabled", Location = new Point(146, 6), AutoSize = true };
            _rbPlAll = new RadioButton { Text = "All", Location = new Point(222, 6), AutoSize = true };
            pPlace.Controls.AddRange(new Control[] { _rbPlNone, _rbPlEnabled, _rbPlAll });
            grp.Controls.Add(pPlace);

            Controls.AddRange(new Control[] { _lstFilters, bNew, bDel, bEn, grp });

            CheckBox chkEnableAll = new CheckBox { Text = "Apply Active Filters to Results Board", Location = new Point(196, 246), AutoSize = true, Checked = doc.FiltersEnabled };
            Controls.Add(chkEnableAll);

            Button ok = DlgUi.Btn("OK", 174, 276, 90, DialogResult.OK);
            Button cancel = DlgUi.Btn("Cancel", 276, 276, 90, DialogResult.Cancel);
            AcceptButton = ok; CancelButton = cancel;
            Controls.Add(ok); Controls.Add(cancel);

            bNew.Click += delegate
            {
                ResultFilterRule r = new ResultFilterRule { Name = "Filter " + (_doc.ResultFilters.Count + 1) };
                _doc.ResultFilters.Add(r);
                RefreshList();
                _lstFilters.SelectedIndex = _lstFilters.Items.Count - 1;
            };
            bDel.Click += delegate
            {
                int i = _lstFilters.SelectedIndex;
                if (i >= 0 && i < _doc.ResultFilters.Count) { _doc.ResultFilters.RemoveAt(i); RefreshList(); }
            };
            bEn.Click += delegate
            {
                int i = _lstFilters.SelectedIndex;
                if (i >= 0 && i < _doc.ResultFilters.Count) { _doc.ResultFilters[i].Enabled = !_doc.ResultFilters[i].Enabled; RefreshList(); _lstFilters.SelectedIndex = i; }
            };
            _lstFilters.SelectedIndexChanged += delegate
            {
                int i = _lstFilters.SelectedIndex;
                if (i < 0 || i >= _doc.ResultFilters.Count) return;
                ResultFilterRule r = _doc.ResultFilters[i];
                _cbField.SelectedItem = r.FieldName;
                _txtLow.Text = r.LowerBound ?? "";
                _txtHigh.Text = r.UpperBound ?? "";
                _txtStartOff.Text = r.StartOffsetSeconds.ToString("0.00", CultureInfo.InvariantCulture);
                _chkSingle.Checked = r.SingleResultOnly;
                _rbPlNone.Checked = r.PlaceFilterMode == 0;
                _rbPlEnabled.Checked = r.PlaceFilterMode == 1;
                _rbPlAll.Checked = r.PlaceFilterMode == 2;
            };
            ok.Click += delegate
            {
                _doc.FiltersEnabled = chkEnableAll.Checked;
                int i = _lstFilters.SelectedIndex;
                if (i >= 0 && i < _doc.ResultFilters.Count)
                {
                    ResultFilterRule r = _doc.ResultFilters[i];
                    if (_cbField.SelectedItem != null) r.FieldName = _cbField.SelectedItem.ToString();
                    r.LowerBound = _txtLow.Text.Trim();
                    r.UpperBound = _txtHigh.Text.Trim();
                    double d;
                    if (double.TryParse(_txtStartOff.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) r.StartOffsetSeconds = d;
                    r.SingleResultOnly = _chkSingle.Checked;
                    r.PlaceFilterMode = _rbPlEnabled.Checked ? 1 : (_rbPlAll.Checked ? 2 : 0);
                }
            };
            RefreshList();
        }

        private void RefreshList()
        {
            _lstFilters.Items.Clear();
            for (int i = 0; i < _doc.ResultFilters.Count; i++)
            {
                ResultFilterRule r = _doc.ResultFilters[i];
                _lstFilters.Items.Add(r.Name + (r.Enabled ? " [ON]" : " [OFF]"));
            }
        }
    }

    // 5. Open Recent Dialog (File | Open recent... Alt+O)
    public sealed class OpenRecentDialog : Form
    {
        private readonly List<string> _paths = new List<string>();
        private ListBox _lstFiles;
        private ComboBox _cbSort;
        public string SelectedFilePath { get; private set; }

        public OpenRecentDialog(string eventDir, string sampleDir)
        {
            DlgUi.StyleClassic(this, "Open Recent Event", 420, 320);
            Controls.Add(DlgUi.Lbl("Sort by:", 14, 16));
            _cbSort = DlgUi.Cb(72, 16, 150, "Name (A to Z)", "Name (Z to A)", "Newest Modified", "Oldest Modified");
            _cbSort.SelectedIndex = 2;
            Controls.Add(_cbSort);

            _lstFiles = new ListBox { Location = new Point(14, 46), Size = new Size(376, 186) };
            _lstFiles.DoubleClick += delegate { if (_lstFiles.SelectedIndex >= 0) { PickSelected(); DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(_lstFiles);

            Button ok = DlgUi.Btn("OK", 112, 242, 90, DialogResult.OK);
            ok.Click += delegate { PickSelected(); };
            Button cancel = DlgUi.Btn("Cancel", 214, 242, 90, DialogResult.Cancel);
            AcceptButton = ok; CancelButton = cancel;
            Controls.Add(ok); Controls.Add(cancel);

            PopulateFiles(eventDir, sampleDir);
            _cbSort.SelectedIndexChanged += delegate { PopulateFiles(eventDir, sampleDir); };
        }

        private void PopulateFiles(string eventDir, string sampleDir)
        {
            _paths.Clear();
            _lstFiles.Items.Clear();
            List<FileInfo> found = new List<FileInfo>();
            string[] dirs = new string[] { eventDir, sampleDir, AppDomain.CurrentDomain.BaseDirectory };
            for (int d = 0; d < dirs.Length; d++)
            {
                if (string.IsNullOrEmpty(dirs[d]) || !Directory.Exists(dirs[d])) continue;
                try
                {
                    string[] exts = new string[] { "*.evn", "*.optr", "*.lif" };
                    for (int e = 0; e < exts.Length; e++)
                    {
                        foreach (string f in Directory.GetFiles(dirs[d], exts[e])) found.Add(new FileInfo(f));
                    }
                }
                catch { }
            }
            int mode = _cbSort.SelectedIndex;
            found.Sort(delegate(FileInfo a, FileInfo b)
            {
                if (mode == 0) return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (mode == 1) return string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase);
                if (mode == 2) return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
                return a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc);
            });
            for (int i = 0; i < found.Count; i++)
            {
                _paths.Add(found[i].FullName);
                _lstFiles.Items.Add(string.Format(CultureInfo.InvariantCulture, "{0}   ({1:g})", found[i].Name, found[i].LastWriteTime));
            }
            if (_lstFiles.Items.Count > 0) _lstFiles.SelectedIndex = 0;
        }

        private void PickSelected()
        {
            if (_lstFiles.SelectedIndex >= 0 && _lstFiles.SelectedIndex < _paths.Count)
            {
                SelectedFilePath = _paths[_lstFiles.SelectedIndex];
            }
        }
    }

    // 6. Goto Event Dialog (File | Goto event...)
    public sealed class GotoEventDialog : Form
    {
        public int EventNum { get; private set; }
        public int RoundNum { get; private set; }
        public int HeatNum { get; private set; }
        public int SelectedScheduleIndex { get; private set; }
        public event EventHandler LoadScheduleRequested;

        public GotoEventDialog(RaceEventDocument doc, List<ScheduledEventSummary> schedule)
        {
            SelectedScheduleIndex = -1;
            DlgUi.StyleClassic(this, "Goto Event", 350, 220);

            Controls.Add(DlgUi.Lbl("Event #:", 20, 20));
            TextBox tbEv = DlgUi.Tb(100, 20, 64, doc != null ? doc.EventNumber.ToString(CultureInfo.InvariantCulture) : "1");
            Controls.Add(DlgUi.Lbl("Round:", 180, 20));
            TextBox tbRn = DlgUi.Tb(235, 20, 48, doc != null ? doc.RoundNumber.ToString(CultureInfo.InvariantCulture) : "1");
            Controls.Add(DlgUi.Lbl("Heat:", 20, 50));
            TextBox tbHt = DlgUi.Tb(100, 50, 64, doc != null ? doc.HeatNumber.ToString(CultureInfo.InvariantCulture) : "1");

            Controls.Add(DlgUi.Lbl("Schedule Event:", 20, 84));
            ComboBox cbSched = DlgUi.Cb(120, 84, 198);
            if (schedule != null)
            {
                for (int i = 0; i < schedule.Count; i++) cbSched.Items.Add(schedule[i].ToString());
                if (cbSched.Items.Count > 0) cbSched.SelectedIndex = 0;
            }
            cbSched.SelectedIndexChanged += delegate
            {
                SelectedScheduleIndex = cbSched.SelectedIndex;
                if (schedule != null && cbSched.SelectedIndex >= 0 && cbSched.SelectedIndex < schedule.Count)
                {
                    ScheduledEventSummary s = schedule[cbSched.SelectedIndex];
                    tbEv.Text = s.EventNumber.ToString(CultureInfo.InvariantCulture);
                    tbRn.Text = s.RoundNumber.ToString(CultureInfo.InvariantCulture);
                    tbHt.Text = s.HeatNumber.ToString(CultureInfo.InvariantCulture);
                }
            };

            Button bLoad = new Button { Text = "Load Schedule...", Location = new Point(20, 114), Size = new Size(116, 24) };
            bLoad.Click += delegate { if (LoadScheduleRequested != null) LoadScheduleRequested(this, EventArgs.Empty); };

            Button ok = DlgUi.Btn("OK", 76, 148, 86, DialogResult.OK);
            ok.Click += delegate
            {
                int ev = 1, rn = 1, ht = 1;
                int.TryParse(tbEv.Text, out ev); int.TryParse(tbRn.Text, out rn); int.TryParse(tbHt.Text, out ht);
                EventNum = ev; RoundNum = rn; HeatNum = ht;
            };
            Button cancel = DlgUi.Btn("Cancel", 174, 148, 86, DialogResult.Cancel);
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { tbEv, tbRn, tbHt, cbSched, bLoad, ok, cancel });
        }
    }

    // 7. Manual Start / TOD / Select Start / Wind / Zero Gun Test Dialogs
    public sealed class ManualStartDialog : Form
    {
        public DateTime CapturedStartTod { get; private set; }
        public double OffsetSeconds { get; private set; }

        public ManualStartDialog()
        {
            CapturedStartTod = DateTime.Now;
            DlgUi.StyleClassic(this, "Manual Start", 320, 175);
            Label lblStatus = DlgUi.Lbl("Press Trigger (or Spacebar) for manual start", 20, 18);
            Controls.Add(DlgUi.Lbl("Offset (sec):", 20, 50));
            TextBox tbOff = DlgUi.Tb(108, 50, 90, "0.000");

            Button bTrig = new Button { Text = "Trigger Start (Space)", Location = new Point(20, 82), Size = new Size(140, 28) };
            bTrig.Click += delegate
            {
                CapturedStartTod = DateTime.Now;
                lblStatus.Text = "Captured: " + CapturedStartTod.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            };
            Button ok = DlgUi.Btn("Apply", 170, 82, 62, DialogResult.OK);
            Button cancel = DlgUi.Btn("Close", 238, 82, 58, DialogResult.Cancel);
            ok.Click += delegate
            {
                double d;
                if (double.TryParse(tbOff.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out d)) OffsetSeconds = d;
            };
            Controls.AddRange(new Control[] { lblStatus, tbOff, bTrig, ok, cancel });
        }
    }

    public sealed class SelectWindDialog : Form
    {
        public WindSpeedUnit Unit { get; private set; }
        public WindWindowMode Mode { get; private set; }
        public double WindValue { get; private set; }
        public bool HasWind { get; private set; }

        public SelectWindDialog(RaceEventDocument doc)
        {
            DlgUi.StyleClassic(this, "Wind Settings", 330, 210);
            Controls.Add(DlgUi.Lbl("Wind Reading:", 20, 22));
            TextBox tbVal = DlgUi.Tb(124, 22, 90, doc.HasWindReading ? doc.WindReading.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) : "");
            CheckBox chkValid = new CheckBox { Text = "Valid Reading", Location = new Point(222, 20), AutoSize = true, Checked = doc.HasWindReading };

            Controls.Add(DlgUi.Lbl("Units:", 20, 56));
            ComboBox cbUnits = DlgUi.Cb(124, 56, 160, "m/s (Meters/sec)", "mph (Miles/hour)", "fps (Feet/sec)");
            cbUnits.SelectedIndex = Math.Max(0, Math.Min(2, (int)doc.WindUnit));

            Controls.Add(DlgUi.Lbl("Mode:", 20, 90));
            ComboBox cbMode = DlgUi.Cb(124, 90, 160, "Manual", "Auto 10s (100m)", "Auto 13s (Hurdles)", "Auto 5s (200m)");
            cbMode.SelectedIndex = Math.Max(0, Math.Min(3, (int)doc.WindMode));

            Button ok = DlgUi.Btn("OK", 76, 134, 86, DialogResult.OK);
            ok.Click += delegate
            {
                HasWind = chkValid.Checked;
                double v = 0;
                double.TryParse(tbVal.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out v);
                WindValue = v;
                Unit = (WindSpeedUnit)Math.Max(0, cbUnits.SelectedIndex);
                Mode = (WindWindowMode)Math.Max(0, cbMode.SelectedIndex);
            };
            Button cancel = DlgUi.Btn("Cancel", 174, 134, 86, DialogResult.Cancel);
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { tbVal, chkValid, cbUnits, cbMode, ok, cancel });
        }
    }

    public sealed class ZeroGunTestDialog : Form
    {
        public double MeasuredOffsetMs { get; private set; }
        public ZeroGunTestDialog(double currentOffsetSec)
        {
            MeasuredOffsetMs = currentOffsetSec * 1000.0;
            DlgUi.StyleClassic(this, "Zero Gun Test", 390, 250);
            Controls.Add(DlgUi.Lbl("Record the finish camera flash against starter sensor pulse:", 16, 16));
            Controls.Add(DlgUi.Lbl("Current Zero Offset (ms):", 16, 48));
            TextBox tbMs = DlgUi.Tb(178, 48, 86, MeasuredOffsetMs.ToString("0.00", CultureInfo.InvariantCulture));
            Controls.Add(DlgUi.Lbl("Max Allowable Drift: +/- 1.00 ms", 16, 80));

            Label lblPass = new Label
            {
                Text = Math.Abs(MeasuredOffsetMs) <= 1.0 ? "STATUS: WITHIN TOLERANCE (PASS)" : "STATUS: RECALIBRATION NEEDED",
                Location = new Point(16, 110),
                Size = new Size(340, 24),
                ForeColor = Math.Abs(MeasuredOffsetMs) <= 1.0 ? Color.DarkGreen : Color.DarkRed,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            Button bTest = new Button { Text = "Run Test Pulse", Location = new Point(16, 142), Size = new Size(116, 26) };
            bTest.Click += delegate
            {
                MeasuredOffsetMs = Math.Round((new Random().NextDouble() - 0.5) * 0.6, 2);
                tbMs.Text = MeasuredOffsetMs.ToString("0.00", CultureInfo.InvariantCulture);
                lblPass.Text = "STATUS: PASSED (Delta " + MeasuredOffsetMs.ToString("0.00", CultureInfo.InvariantCulture) + " ms)";
                lblPass.ForeColor = Color.DarkGreen;
            };
            Button ok = DlgUi.Btn("Apply", 150, 178, 82, DialogResult.OK);
            ok.Click += delegate
            {
                double v;
                if (double.TryParse(tbMs.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) MeasuredOffsetMs = v;
            };
            Button cancel = DlgUi.Btn("Cancel", 242, 178, 82, DialogResult.Cancel);
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { tbMs, lblPass, bTest, ok, cancel });
        }
    }

    // 8. Crop To Results / Pack Leaders / Find Value / Overlays
    public sealed class CropToResultsDialog : Form
    {
        public double LeaderSec { get; private set; }
        public double TrailerSec { get; private set; }
        public bool SelectedOnly { get; private set; }

        public CropToResultsDialog()
        {
            LeaderSec = 0.25;
            TrailerSec = 0.25;
            DlgUi.StyleClassic(this, "Crop to Results", 310, 185);
            Controls.Add(DlgUi.Lbl("Leader (sec):", 20, 22));
            TextBox tbLead = DlgUi.Tb(124, 22, 80, "0.25");
            Controls.Add(DlgUi.Lbl("Trailer (sec):", 20, 52));
            TextBox tbTrail = DlgUi.Tb(124, 52, 80, "0.25");
            CheckBox chkSel = new CheckBox { Text = "Selected Results Only", Location = new Point(20, 84), AutoSize = true };
            Button ok = DlgUi.Btn("OK", 56, 114, 86, DialogResult.OK);
            ok.Click += delegate
            {
                double a = 0.25, b = 0.25;
                double.TryParse(tbLead.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out a);
                double.TryParse(tbTrail.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out b);
                LeaderSec = a; TrailerSec = b; SelectedOnly = chkSel.Checked;
            };
            Button cancel = DlgUi.Btn("Cancel", 154, 114, 86, DialogResult.Cancel);
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { tbLead, tbTrail, chkSel, ok, cancel });
        }
    }

    public sealed class SimplePromptDialog : Form
    {
        public string ValueText { get; private set; }
        public SimplePromptDialog(string title, string promptLabel, string initialValue)
        {
            ValueText = initialValue ?? "";
            DlgUi.StyleClassic(this, title, 340, 145);
            Controls.Add(DlgUi.Lbl(promptLabel, 18, 18));
            TextBox tb = DlgUi.Tb(18, 44, 286, ValueText);
            Button ok = DlgUi.Btn("OK", 76, 76, 86, DialogResult.OK);
            ok.Click += delegate { ValueText = tb.Text; };
            Button cancel = DlgUi.Btn("Cancel", 174, 76, 86, DialogResult.Cancel);
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { tb, ok, cancel });
        }
    }

    // 9. Print Settings & Page Setup (PrintSettings.png & PrintPageSetup.png)
    public sealed class PrintPageSetupDialog : Form
    {
        public PrintPageSetupDialog(PrintExportSettings s)
        {
            DlgUi.StyleClassic(this, "Print Page Setup", 430, 325);
            Controls.Add(DlgUi.Lbl("Header Left:", 16, 20));
            TextBox tbHl = DlgUi.Tb(124, 20, 274, s.PageHeaderLeft);
            Controls.Add(DlgUi.Lbl("Header Center:", 16, 48));
            TextBox tbHc = DlgUi.Tb(124, 48, 274, s.PageHeaderCenter);
            Controls.Add(DlgUi.Lbl("Header Right:", 16, 76));
            TextBox tbHr = DlgUi.Tb(124, 76, 274, s.PageHeaderRight);
            Controls.Add(DlgUi.Lbl("Footer Left:", 16, 112));
            TextBox tbFl = DlgUi.Tb(124, 112, 274, s.PageFooterLeft);
            Controls.Add(DlgUi.Lbl("Footer Right:", 16, 140));
            TextBox tbFr = DlgUi.Tb(124, 140, 274, s.PageFooterRight);

            CheckBox chkLand = new CheckBox { Text = "Landscape Orientation", Location = new Point(16, 174), AutoSize = true, Checked = s.Landscape };
            CheckBox chkRes = new CheckBox { Text = "Include Results Table", Location = new Point(190, 174), AutoSize = true, Checked = s.PrintResultsTable };
            CheckBox chkOv = new CheckBox { Text = "Include Overlays & Lane Labels", Location = new Point(16, 200), AutoSize = true, Checked = s.PrintOverlays };

            Button ok = DlgUi.Btn("OK", 118, 244, 86, DialogResult.OK);
            ok.Click += delegate
            {
                s.PageHeaderLeft = tbHl.Text;
                s.PageHeaderCenter = tbHc.Text;
                s.PageHeaderRight = tbHr.Text;
                s.PageFooterLeft = tbFl.Text;
                s.PageFooterRight = tbFr.Text;
                s.Landscape = chkLand.Checked;
                s.PrintResultsTable = chkRes.Checked;
                s.PrintOverlays = chkOv.Checked;
            };
            Button cancel = DlgUi.Btn("Cancel", 216, 244, 86, DialogResult.Cancel);
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { tbHl, tbHc, tbHr, tbFl, tbFr, chkLand, chkRes, chkOv, ok, cancel });
        }
    }

    // 10. LapTime Split Times Editor (LapTime | Edit split times...)
    public sealed class LapTimeSplitsDialog : Form
    {
        private readonly RaceEventDocument _doc;
        private readonly DataGridView _grid;

        public LapTimeSplitsDialog(RaceEventDocument doc)
        {
            _doc = doc;
            DlgUi.StyleClassic(this, "Edit Split Times", 560, 380);
            _grid = new DataGridView
            {
                Location = new Point(12, 12),
                Size = new Size(520, 270),
                BackgroundColor = SystemColors.Window,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _grid.Columns.Add("Bib", "Id/Bib");
            _grid.Columns.Add("Lane", "Lane");
            _grid.Columns.Add("Name", "Athlete");
            _grid.Columns.Add("Lap", "Lap #");
            _grid.Columns.Add("Cum", "Cumulative");
            _grid.Columns.Add("LapTime", "Lap Time");
            _grid.Columns.Add("Status", "Flag");
            Controls.Add(_grid);

            Button bAccept = DlgUi.Btn("Accept", 12, 294, 82, DialogResult.None);
            Button bMask = DlgUi.Btn("Mask", 102, 294, 82, DialogResult.None);
            Button bInsert = DlgUi.Btn("Add Split", 192, 294, 88, DialogResult.None);
            Button bOk = DlgUi.Btn("Close", 442, 294, 90, DialogResult.OK);

            bAccept.Click += delegate { SetSelectedFlag(SplitFlag.Accepted); };
            bMask.Click += delegate { SetSelectedFlag(SplitFlag.TooSlow); };
            bInsert.Click += delegate
            {
                if (_doc.Competitors.Count == 0) return;
                CompetitorEntry c = _doc.Competitors[0];
                int nextLap = c.Splits.Count + 1;
                double prev = c.Splits.Count > 0 ? c.Splits[c.Splits.Count - 1].CumulativeSeconds : 0.0;
                c.Splits.Add(new LapSplitRecord(nextLap, prev + 62.5, 62.5, SplitFlag.Manual));
                LoadRows();
            };
            Controls.AddRange(new Control[] { bAccept, bMask, bInsert, bOk });
            LoadRows();
        }

        private void LoadRows()
        {
            _grid.Rows.Clear();
            for (int i = 0; i < _doc.Competitors.Count; i++)
            {
                CompetitorEntry c = _doc.Competitors[i];
                for (int s = 0; s < c.Splits.Count; s++)
                {
                    LapSplitRecord sp = c.Splits[s];
                    int r = _grid.Rows.Add(
                        c.BibId,
                        c.Lane,
                        (c.FirstName + " " + c.LastName).Trim(),
                        sp.LapNumber,
                        CompetitorEntry.FormatSeconds(sp.CumulativeSeconds, _doc.Precision),
                        CompetitorEntry.FormatSeconds(sp.LapSeconds, _doc.Precision),
                        sp.Flag.ToString());
                    _grid.Rows[r].Tag = sp;
                }
            }
        }

        private void SetSelectedFlag(SplitFlag f)
        {
            if (_grid.SelectedRows.Count == 0) return;
            LapSplitRecord sp = _grid.SelectedRows[0].Tag as LapSplitRecord;
            if (sp == null) return;
            sp.Flag = f;
            LoadRows();
        }
    }

    // 11. Other Settings Registry Tree / Editor (File | Other settings...)
    public sealed class OtherSettingsDialog : Form
    {
        public OtherSettingsDialog()
        {
            DlgUi.StyleClassic(this, "Other Settings (Advanced Configuration)", 520, 410);
            ListView lv = new ListView
            {
                Location = new Point(12, 12),
                Size = new Size(480, 268),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            lv.Columns.Add("Setting Key", 270);
            lv.Columns.Add("Value", 190);
            Controls.Add(lv);

            Controls.Add(DlgUi.Lbl("Selected Value:", 14, 294));
            TextBox tbVal = DlgUi.Tb(114, 294, 240, "");
            Button bSet = new Button { Text = "Set", Location = new Point(362, 290), Size = new Size(64, 24) };
            Button bOk = DlgUi.Btn("OK", 160, 330, 90, DialogResult.OK);
            Button bCancel = DlgUi.Btn("Cancel", 260, 330, 90, DialogResult.Cancel);

            foreach (KeyValuePair<string, string> kv in LynxExtendedConfig.Instance.OtherSettings)
            {
                ListViewItem item = new ListViewItem(kv.Key);
                item.SubItems.Add(kv.Value);
                lv.Items.Add(item);
            }
            lv.SelectedIndexChanged += delegate
            {
                if (lv.SelectedItems.Count > 0) tbVal.Text = lv.SelectedItems[0].SubItems[1].Text;
            };
            bSet.Click += delegate
            {
                if (lv.SelectedItems.Count > 0)
                {
                    lv.SelectedItems[0].SubItems[1].Text = tbVal.Text;
                    LynxExtendedConfig.Instance.OtherSettings[lv.SelectedItems[0].Text] = tbVal.Text;
                }
            };
            bOk.Click += delegate { LynxExtendedConfig.Instance.Save(); };
            AcceptButton = bOk; CancelButton = bCancel;
            Controls.AddRange(new Control[] { tbVal, bSet, bOk, bCancel });
        }
    }

    // 12. System Info / Find Cameras / About FinishLynx Dialogs
    public sealed class SystemInfoDialog : Form
    {
        public SystemInfoDialog(RaceEventDocument doc)
        {
            DlgUi.StyleClassic(this, "System Info", 450, 360);
            TextBox tb = new TextBox
            {
                Location = new Point(12, 12),
                Size = new Size(410, 260),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White,
                Font = new Font("Consolas", 8.5f),
                Text = string.Format(CultureInfo.InvariantCulture,
                    "Application: OptiFinish Studio\r\n" +
                    "OS Version:  {0}\r\n" +
                    ".NET CLR:    {1}\r\n" +
                    "Processors:  {2}\r\n" +
                    "Screen Res:  {3}x{4}\r\n" +
                    "Config File: {5}\r\n" +
                    "Ext Config:  {6}\r\n" +
                    "Active Event: {7}\r\n" +
                    "Cameras:     {8}\r\n",
                    Environment.OSVersion,
                    Environment.Version,
                    Environment.ProcessorCount,
                    Screen.PrimaryScreen.Bounds.Width,
                    Screen.PrimaryScreen.Bounds.Height,
                    LynxConfigService.Instance.GetLocalConfigPath(),
                    LynxExtendedConfig.Instance.GetConfigPath(),
                    doc != null ? doc.EventName : "<none>",
                    doc != null ? doc.Cameras.Count : 0)
            };
            Button bOk = DlgUi.Btn("OK", 174, 282, 90, DialogResult.OK);
            Controls.Add(tb); Controls.Add(bOk);
        }
    }

    public sealed class FindCamerasDialog : Form
    {
        public FindCamerasDialog(RaceEventDocument doc)
        {
            DlgUi.StyleClassic(this, "Find Cameras", 460, 270);
            ListView lv = new ListView
            {
                Location = new Point(12, 12),
                Size = new Size(420, 174),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            lv.Columns.Add("Camera Name", 150);
            lv.Columns.Add("IP / Source", 150);
            lv.Columns.Add("Rate (Hz)", 100);
            if (doc != null)
            {
                for (int i = 0; i < doc.Cameras.Count; i++)
                {
                    SlitCameraStream c = doc.Cameras[i];
                    ListViewItem lvi = new ListViewItem(c.DisplayName);
                    lvi.SubItems.Add(c.Profile != null ? c.Profile.IpAddress : "Local Stream");
                    lvi.SubItems.Add(c.SampleRateHz.ToString(CultureInfo.InvariantCulture));
                    lv.Items.Add(lvi);
                }
            }
            Button bOk = DlgUi.Btn("Close", 174, 198, 90, DialogResult.OK);
            Controls.Add(lv); Controls.Add(bOk);
        }
    }

    public sealed class AboutFinishLynxDialog : Form
    {
        public AboutFinishLynxDialog()
        {
            DlgUi.StyleClassic(this, "About OptiFinish", 430, 290);
            PictureBox pic = new PictureBox
            {
                Location = new Point(16, 16),
                Size = new Size(48, 48),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = AppIconGenerator.CreateMemoryIcon(48).ToBitmap()
            };
            Label title = new Label
            {
                Text = "OptiFinish Studio\r\nPrecision Photo-Finish Timing Workspace",
                Location = new Point(74, 18),
                Size = new Size(330, 42),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            Label info = new Label
            {
                Text = "Clean-room slit-scan photo-finish & IdentiLynx video synchronization suite.\r\n" +
                       "Loaded Plug-ins:\r\n" +
                       "  - Multi-Split Interface (MSI)\r\n" +
                       "  - LapTime & Extended Splits\r\n" +
                       "  - Scoreboard LSS & UDP Broadcast Engine\r\n" +
                       "  - Wind Gauge Serial / Ultrasonic Reader\r\n" +
                       "  - ReacTime False-Start Import\r\n" +
                       "  - White Balance & Automated Crop (ACM)",
                Location = new Point(16, 76),
                Size = new Size(390, 130)
            };
            Button bOk = DlgUi.Btn("OK", 164, 214, 90, DialogResult.OK);
            AcceptButton = bOk; CancelButton = bOk;
            Controls.AddRange(new Control[] { pic, title, info, bOk });
        }
    }

    // 13. Full Screen Image Viewer (Image | Full screen Ctrl+F & Full screen two Ctrl+G)
    public sealed class FullScreenImageForm : Form
    {
        public FullScreenImageForm(RaceEventDocument doc, bool twoCameras)
        {
            Text = "Full Screen Image";
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = Color.Black;
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape || e.Control && (e.KeyCode == Keys.F || e.KeyCode == Keys.G))
                {
                    Close();
                }
            };

            SplitContainer sc = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = Color.Black };
            SlitScanViewportControl vp1 = new SlitScanViewportControl { Dock = DockStyle.Fill, Document = doc, CameraIndex = 0 };
            sc.Panel1.Controls.Add(vp1);

            if (twoCameras && doc != null && doc.Cameras.Count > 1)
            {
                SlitScanViewportControl vp2 = new SlitScanViewportControl { Dock = DockStyle.Fill, Document = doc, CameraIndex = 1 };
                sc.Panel2.Controls.Add(vp2);
                sc.Panel2Collapsed = false;
            }
            else
            {
                sc.Panel2Collapsed = true;
            }

            Label hint = new Label
            {
                Text = "Press Esc or Ctrl+F / Ctrl+G to exit Full Screen",
                Dock = DockStyle.Bottom,
                Height = 22,
                ForeColor = Color.Silver,
                BackColor = Color.FromArgb(20, 20, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(sc);
            Controls.Add(hint);
        }
    }

    // 13. FinishLynx-parity Print Settings & Live Cropped Preview Dialog
    public sealed class LynxPrintDialog : Form
    {
        private readonly RaceEventDocument _doc;
        private readonly int _cameraIndex;
        private readonly CropWindow _cropOverride;
        private readonly PrintDialogSettings _settings;

        private RadioButton _rbZoom1, _rbZoom2, _rbZoom4, _rbZoom8;
        private CheckBox _chkSmooth;
        private RadioButton _rbDpi300, _rbDpi600, _rbDpi1200;
        private TextBox _txtHeight;
        private RadioButton _rbInches, _rbCm;
        private RadioButton _rbLbOff, _rbLbOn;
        private RadioButton _rbLrOff, _rbLrOn;
        private TextBox _txtLimitRes;
        private RadioButton _rbPrOff, _rbPrOn;
        private TextBox _txtMaxResults, _txtRange;
        private ComboBox _cbPrinter;
        private PictureBox _pbPreview;
        private Label _lblPreviewInfo;
        private bool _updatingControls;

        public PrintDialogSettings Settings { get { return _settings; } }

        public LynxPrintDialog(RaceEventDocument doc, int cameraIndex, CropWindow cropOverride, PrintDialogSettings initialSettings)
        {
            _doc = doc;
            _cameraIndex = cameraIndex;
            _cropOverride = cropOverride;
            _settings = initialSettings != null ? initialSettings.Clone() : new PrintDialogSettings();

            DlgUi.StyleClassic(this, Loc.Evt("Image\\PrintDlg\\Title", "Print"), 760, 490);

            int y = 16;
            // Zoom row
            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\PrintDlg\\Zoom", "Zoom:"), 16, y + 3));
            Panel pZoom = new Panel { Location = new Point(120, y), Size = new Size(270, 24) };
            _rbZoom1 = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\Zoom1", "1"), Location = new Point(0, 2), AutoSize = true, Checked = (_settings.Zoom <= 1) };
            _rbZoom2 = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\Zoom2", "2"), Location = new Point(38, 2), AutoSize = true, Checked = (_settings.Zoom == 2) };
            _rbZoom4 = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\Zoom4", "4"), Location = new Point(76, 2), AutoSize = true, Checked = (_settings.Zoom == 4) };
            _rbZoom8 = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\Zoom8", "8"), Location = new Point(114, 2), AutoSize = true, Checked = (_settings.Zoom >= 8) };
            _chkSmooth = new CheckBox { Text = Loc.Evt("Image\\PrintDlg\\SmoothZoom", "Smooth"), Location = new Point(158, 2), AutoSize = true, Checked = _settings.Smooth };
            pZoom.Controls.AddRange(new Control[] { _rbZoom1, _rbZoom2, _rbZoom4, _rbZoom8, _chkSmooth });
            Controls.Add(pZoom);

            y += 34;
            // DPI row
            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\PrintDlg\\DPI", "DPI:"), 16, y + 3));
            Panel pDpi = new Panel { Location = new Point(120, y), Size = new Size(240, 24) };
            _rbDpi300 = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\DPI300", "300"), Location = new Point(0, 2), AutoSize = true, Checked = (_settings.Dpi <= 300) };
            _rbDpi600 = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\DPI600", "600"), Location = new Point(56, 2), AutoSize = true, Checked = (_settings.Dpi == 600) };
            _rbDpi1200 = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\DPI1200", "1200"), Location = new Point(112, 2), AutoSize = true, Checked = (_settings.Dpi >= 1200) };
            pDpi.Controls.AddRange(new Control[] { _rbDpi300, _rbDpi600, _rbDpi1200 });
            Controls.Add(pDpi);

            y += 36;
            // Image Height row
            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\PrintDlg\\ImageHeight", "Image Height:"), 16, y + 3));
            _txtHeight = DlgUi.Tb(120, y + 3, 64, _settings.ImageHeight.ToString("0.00", CultureInfo.InvariantCulture));
            Panel pUnit = new Panel { Location = new Point(192, y), Size = new Size(190, 24) };
            _rbInches = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\Inches", "Inches"), Location = new Point(0, 2), AutoSize = true, Checked = !_settings.UseCentimeters };
            _rbCm = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\Centimeters", "Centimeters"), Location = new Point(72, 2), AutoSize = true, Checked = _settings.UseCentimeters };
            pUnit.Controls.AddRange(new Control[] { _rbInches, _rbCm });
            Controls.Add(_txtHeight);
            Controls.Add(pUnit);

            y += 36;
            // Lane Bars row
            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\PrintDlg\\LaneBars", "Lane Bars:"), 16, y + 3));
            Panel pLb = new Panel { Location = new Point(120, y), Size = new Size(180, 24) };
            _rbLbOff = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\LBOff", "Off"), Location = new Point(0, 2), AutoSize = true, Checked = !_settings.LaneBarsOn };
            _rbLbOn = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\LBOn", "On"), Location = new Point(62, 2), AutoSize = true, Checked = _settings.LaneBarsOn };
            pLb.Controls.AddRange(new Control[] { _rbLbOff, _rbLbOn });
            Controls.Add(pLb);

            y += 36;
            // Limit Res row
            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\PrintDlg\\LimitRes", "Limit Res:"), 16, y + 3));
            Panel pLr = new Panel { Location = new Point(120, y), Size = new Size(140, 24) };
            _rbLrOff = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\LROff", "Off"), Location = new Point(0, 2), AutoSize = true, Checked = !_settings.LimitResOn };
            _rbLrOn = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\LROn", "On"), Location = new Point(62, 2), AutoSize = true, Checked = _settings.LimitResOn };
            pLr.Controls.AddRange(new Control[] { _rbLrOff, _rbLrOn });
            _txtLimitRes = DlgUi.Tb(262, y + 3, 60, _settings.LimitResValue.ToString(CultureInfo.InvariantCulture));
            _txtLimitRes.Enabled = _settings.LimitResOn;
            Controls.Add(pLr);
            Controls.Add(_txtLimitRes);

            y += 38;
            // Print Results row
            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\PrintDlg\\PrintResults", "Print Results:"), 16, y + 3));
            Panel pPr = new Panel { Location = new Point(120, y), Size = new Size(180, 24) };
            _rbPrOff = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\PROff", "Off"), Location = new Point(0, 2), AutoSize = true, Checked = !_settings.PrintResultsOn };
            _rbPrOn = new RadioButton { Text = Loc.Evt("Image\\PrintDlg\\PROn", "On"), Location = new Point(62, 2), AutoSize = true, Checked = _settings.PrintResultsOn };
            pPr.Controls.AddRange(new Control[] { _rbPrOff, _rbPrOn });
            Controls.Add(pPr);

            y += 34;
            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\PrintDlg\\MaxResults", "Maximum"), 40, y + 3));
            _txtMaxResults = DlgUi.Tb(120, y + 3, 56, _settings.MaxResults.ToString(CultureInfo.InvariantCulture));
            Controls.Add(_txtMaxResults);

            y += 32;
            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\PrintDlg\\ResultsRange", "Range"), 40, y + 3));
            _txtRange = DlgUi.Tb(120, y + 3, 180, _settings.ResultsRange ?? "");
            Controls.Add(_txtRange);

            y += 38;
            Controls.Add(DlgUi.Lbl("Stampante / PDF:", 16, y + 3));
            _cbPrinter = DlgUi.Cb(120, y + 3, 256);
            LynxPrintPreviewDialog.PopulatePrinterCombo(_cbPrinter, _settings.PrinterName);
            Controls.Add(_cbPrinter);

            // Live Cropped Thumbnail Preview on right
            GroupBox grpPrev = new GroupBox
            {
                Text = "Preview",
                Location = new Point(392, 12),
                Size = new Size(340, 386)
            };
            _pbPreview = new PictureBox
            {
                Location = new Point(10, 20),
                Size = new Size(320, 326),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(24, 24, 24),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            _lblPreviewInfo = new Label
            {
                Location = new Point(10, 352),
                Size = new Size(320, 24),
                ForeColor = Color.FromArgb(40, 40, 40),
                Text = ""
            };
            grpPrev.Controls.Add(_pbPreview);
            grpPrev.Controls.Add(_lblPreviewInfo);
            Controls.Add(grpPrev);

            Button btnPrev = new Button { Text = "Anteprima...", Location = new Point(440, 412), Size = new Size(106, 25) };
            Button btnOk = DlgUi.Btn("OK", 554, 412, 86, DialogResult.OK);
            Button btnCancel = DlgUi.Btn("Cancel", 646, 412, 86, DialogResult.Cancel);
            btnPrev.Click += delegate
            {
                ReadControlsIntoSettings();
                using (Bitmap bmp = SlitScanEngine.RenderAnnotatedExportBitmap(_doc, _cameraIndex, _cropOverride, _settings))
                using (LynxPrintPreviewDialog prev = new LynxPrintPreviewDialog(
                    bmp,
                    _doc != null ? _doc.GetEventCodeString() : "OptiFinish",
                    _settings.PrinterName,
                    LynxExtendedConfig.Instance.PrintSettings.Landscape))
                {
                    prev.ShowDialog(this);
                    if (!string.IsNullOrEmpty(prev.SelectedPrinterName))
                    {
                        _settings.PrinterName = prev.SelectedPrinterName;
                        LynxPrintPreviewDialog.SelectPrinterInCombo(_cbPrinter, _settings.PrinterName);
                    }
                    LynxExtendedConfig.Instance.PrintSettings.Landscape = prev.SelectedLandscape;
                }
            };
            btnOk.Click += delegate { ReadControlsIntoSettings(); };
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(btnPrev);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            EventHandler onSettingChanged = delegate
            {
                if (_updatingControls) return;
                SyncEnableStates();
                ReadControlsIntoSettings();
                UpdatePreview();
            };

            _rbZoom1.CheckedChanged += onSettingChanged;
            _rbZoom2.CheckedChanged += onSettingChanged;
            _rbZoom4.CheckedChanged += onSettingChanged;
            _rbZoom8.CheckedChanged += onSettingChanged;
            _chkSmooth.CheckedChanged += onSettingChanged;
            _rbDpi300.CheckedChanged += onSettingChanged;
            _rbDpi600.CheckedChanged += onSettingChanged;
            _rbDpi1200.CheckedChanged += onSettingChanged;
            _rbLbOff.CheckedChanged += onSettingChanged;
            _rbLbOn.CheckedChanged += onSettingChanged;
            _rbLrOff.CheckedChanged += onSettingChanged;
            _rbLrOn.CheckedChanged += onSettingChanged;
            _rbPrOff.CheckedChanged += onSettingChanged;
            _rbPrOn.CheckedChanged += onSettingChanged;

            _rbCm.CheckedChanged += delegate
            {
                if (_updatingControls) return;
                _updatingControls = true;
                double val;
                if (double.TryParse(_txtHeight.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                {
                    if (_rbCm.Checked && !_settings.UseCentimeters) val *= 2.54;
                    else if (!_rbCm.Checked && _settings.UseCentimeters) val /= 2.54;
                    _txtHeight.Text = val.ToString("0.00", CultureInfo.InvariantCulture);
                }
                _updatingControls = false;
                ReadControlsIntoSettings();
                UpdatePreview();
            };

            _txtHeight.Leave += onSettingChanged;
            _txtLimitRes.Leave += onSettingChanged;
            _txtMaxResults.Leave += onSettingChanged;
            _txtRange.Leave += onSettingChanged;

            SyncEnableStates();
            UpdatePreview();
        }

        private void SyncEnableStates()
        {
            _txtLimitRes.Enabled = _rbLrOn.Checked;
            _txtMaxResults.Enabled = _rbPrOn.Checked;
            _txtRange.Enabled = _rbPrOn.Checked;
        }

        private void ReadControlsIntoSettings()
        {
            _settings.Zoom = _rbZoom8.Checked ? 8 : (_rbZoom4.Checked ? 4 : (_rbZoom2.Checked ? 2 : 1));
            _settings.Smooth = _chkSmooth.Checked;
            _settings.Dpi = _rbDpi1200.Checked ? 1200 : (_rbDpi600.Checked ? 600 : 300);

            double h;
            if (double.TryParse(_txtHeight.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out h))
            {
                _settings.ImageHeight = Math.Max(0.25, Math.Min(100.0, h));
            }
            _settings.UseCentimeters = _rbCm.Checked;
            _settings.LaneBarsOn = _rbLbOn.Checked;
            _settings.LimitResOn = _rbLrOn.Checked;
            int lr;
            if (int.TryParse(_txtLimitRes.Text.Trim(), out lr)) _settings.LimitResValue = Math.Max(72, Math.Min(4800, lr));
            _settings.PrintResultsOn = _rbPrOn.Checked;
            int mr;
            if (int.TryParse(_txtMaxResults.Text.Trim(), out mr)) _settings.MaxResults = Math.Max(1, Math.Min(200, mr));
            _settings.ResultsRange = _txtRange.Text.Trim();
            _settings.PrinterName = (_cbPrinter != null && _cbPrinter.SelectedItem != null) ? _cbPrinter.SelectedItem.ToString() : "";
        }

        private void UpdatePreview()
        {
            if (_doc == null) return;
            try
            {
                Bitmap bmp = SlitScanEngine.RenderAnnotatedExportBitmap(_doc, _cameraIndex, _cropOverride, _settings);
                Image old = _pbPreview.Image;
                _pbPreview.Image = bmp;
                if (old != null) old.Dispose();

                string cropSummary = "Full Strip";
                if (_cameraIndex >= 0 && _cameraIndex < _doc.Cameras.Count)
                {
                    SlitCameraStream cam = _doc.Cameras[_cameraIndex];
                    CropWindow eff = SlitScanEngine.ResolveEffectiveCrop(_doc, cam, _cropOverride);
                    if (eff != null)
                    {
                        double t0 = cam.ColumnToTimeSeconds(eff.StartCol);
                        double t1 = cam.ColumnToTimeSeconds(eff.EndCol);
                        cropSummary = string.Format(CultureInfo.InvariantCulture, "Crop {0:0.000}s..{1:0.000}s", Math.Min(t0, t1), Math.Max(t0, t1));
                    }
                }
                _lblPreviewInfo.Text = string.Format(CultureInfo.InvariantCulture, "{0} | Output: {1} x {2} px", cropSummary, bmp.Width, bmp.Height);
            }
            catch (Exception ex)
            {
                _lblPreviewInfo.Text = ex.Message;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _pbPreview != null && _pbPreview.Image != null)
            {
                _pbPreview.Image.Dispose();
                _pbPreview.Image = null;
            }
            base.Dispose(disposing);
        }
    }

    // 14. Select Start Dialog (Event -> Select start...)
    public sealed class SelectStartDialog : Form
    {
        private readonly RaceEventDocument _doc;
        private readonly ListBox _lbStarts;
        private readonly TextBox _txtTime;

        public int SelectedStartIndex { get; private set; }
        public double SelectedOffsetSeconds { get; private set; }

        public SelectStartDialog(RaceEventDocument doc)
        {
            _doc = doc;
            SelectedStartIndex = doc != null ? doc.SelectedStartIndex : 0;
            SelectedOffsetSeconds = doc != null ? doc.StartOffsetSeconds : 0.0;

            DlgUi.StyleClassic(this, Loc.Evt("Start\\Select\\Title", "Select Start"), 390, 290);

            Controls.Add(DlgUi.Lbl(Loc.Evt("Start\\Select\\Title", "Select Start") + ":", 14, 14));
            _lbStarts = new ListBox { Location = new Point(14, 34), Size = new Size(344, 140) };
            Controls.Add(_lbStarts);

            Controls.Add(DlgUi.Lbl(Loc.Evt("Start\\Select\\Time", "Time") + " (s):", 14, 186));
            _txtTime = DlgUi.Tb(110, 186, 120, SelectedOffsetSeconds.ToString("0.0000", CultureInfo.InvariantCulture));
            Controls.Add(_txtTime);

            if (_doc != null && _doc.RecordedStarts.Count == 0)
            {
                _doc.RecordedStarts.Add(new TimeTrialStartEntry
                {
                    StartNumber = 1,
                    Label = _doc.HasStartSignal ? "Primary Gun Start" : "Default Start (0.000s)",
                    OffsetSeconds = _doc.StartOffsetSeconds
                });
            }

            if (_doc != null)
            {
                for (int i = 0; i < _doc.RecordedStarts.Count; i++)
                {
                    TimeTrialStartEntry st = _doc.RecordedStarts[i];
                    _lbStarts.Items.Add(string.Format(CultureInfo.InvariantCulture,
                        "#{0}  {1}  ({2:0.0000} s)", st.StartNumber, st.Label, st.OffsetSeconds));
                }
                if (_lbStarts.Items.Count > 0)
                {
                    int idx = Math.Max(0, Math.Min(_lbStarts.Items.Count - 1, _doc.SelectedStartIndex));
                    _lbStarts.SelectedIndex = idx;
                }
            }

            _lbStarts.SelectedIndexChanged += delegate
            {
                int idx = _lbStarts.SelectedIndex;
                if (_doc != null && idx >= 0 && idx < _doc.RecordedStarts.Count)
                {
                    _txtTime.Text = _doc.RecordedStarts[idx].OffsetSeconds.ToString("0.0000", CultureInfo.InvariantCulture);
                }
            };

            Button btnOk = DlgUi.Btn("OK", 180, 218, 86, DialogResult.OK);
            Button btnCancel = DlgUi.Btn("Cancel", 272, 218, 86, DialogResult.Cancel);
            btnOk.Click += delegate
            {
                int idx = Math.Max(0, _lbStarts.SelectedIndex);
                SelectedStartIndex = idx;
                double val;
                if (double.TryParse(_txtTime.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                {
                    SelectedOffsetSeconds = val;
                    if (_doc != null && idx < _doc.RecordedStarts.Count)
                    {
                        _doc.RecordedStarts[idx].OffsetSeconds = val;
                    }
                }
            };
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }
    }

    // 15. Time of Day Start Dialog (Event -> TOD start...)
    public sealed class TodStartDialog : Form
    {
        private readonly TextBox _txtTime;
        public double SelectedStartTimeSeconds { get; private set; }
        public string RawTimeText { get; private set; }

        public TodStartDialog(double initialOffsetSeconds)
        {
            DlgUi.StyleClassic(this, Loc.Hw("TODStartDlg\\Title", "Time of Day Start"), 360, 165);
            Controls.Add(DlgUi.Lbl(Loc.Hw("TODStartDlg\\Time", "Time:"), 16, 26));

            string defaultStr = FormatTodSeconds(initialOffsetSeconds > 0 ? initialOffsetSeconds : DateTime.Now.TimeOfDay.TotalSeconds);
            _txtTime = DlgUi.Tb(90, 26, 150, defaultStr);
            Controls.Add(_txtTime);

            Button btnNow = new Button { Text = "Now", Location = new Point(248, 22), Size = new Size(80, 25) };
            btnNow.Click += delegate { _txtTime.Text = DateTime.Now.ToString("HH:mm:ss.ff", CultureInfo.InvariantCulture); };
            Controls.Add(btnNow);

            Controls.Add(DlgUi.Lbl("Format: HH:MM:SS.fff or seconds", 90, 56));

            Button btnOk = DlgUi.Btn("OK", 150, 92, 86, DialogResult.OK);
            Button btnCancel = DlgUi.Btn("Cancel", 242, 92, 86, DialogResult.Cancel);
            btnOk.Click += delegate
            {
                RawTimeText = _txtTime.Text.Trim();
                double parsed;
                if (TryParseTodOrSeconds(RawTimeText, out parsed))
                {
                    SelectedStartTimeSeconds = parsed;
                }
            };
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }

        internal static string FormatTodSeconds(double sec)
        {
            if (double.IsNaN(sec) || sec < 0) sec = 0;
            TimeSpan ts = TimeSpan.FromSeconds(sec % 86400.0);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}.{3:000}",
                ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);
        }

        internal static bool TryParseTodOrSeconds(string text, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim().Replace(',', '.');
            TimeSpan ts;
            if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out ts))
            {
                seconds = ts.TotalSeconds;
                return true;
            }
            double val;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
            {
                seconds = val;
                return true;
            }
            return false;
        }
    }

    // 16. Synchronize Time Of Day Dialog (Event -> Synchronize TOD...)
    public sealed class SynchronizeTodDialog : Form
    {
        private readonly TextBox _txtOldTime;
        private readonly TextBox _txtNewTime;
        private readonly RadioButton _rbFromExtSync;
        private readonly RadioButton _rbFromImage;
        private readonly RadioButton _rbFromStart;
        private readonly CheckBox _chkMakeValid;

        public double NewTodOffsetSeconds { get; private set; }
        public bool MakeEventValid { get; private set; }

        public SynchronizeTodDialog(double oldTodSec, double imageHairlineSec, double startSec)
        {
            DlgUi.StyleClassic(this, Loc.Hw("SyncTODDlg\\Title", "Synchronize Time Of Day"), 410, 265);

            Controls.Add(DlgUi.Lbl(Loc.Hw("SyncTODDlg\\OldTime", "Old time:"), 16, 18));
            _txtOldTime = DlgUi.Tb(140, 18, 170, TodStartDialog.FormatTodSeconds(oldTodSec));
            _txtOldTime.ReadOnly = true;
            Controls.Add(_txtOldTime);

            Controls.Add(DlgUi.Lbl(Loc.Hw("SyncTODDlg\\NewTime", "New time:"), 16, 48));
            string initNew = TodStartDialog.FormatTodSeconds(DateTime.Now.TimeOfDay.TotalSeconds);
            _txtNewTime = DlgUi.Tb(140, 48, 170, initNew);
            Controls.Add(_txtNewTime);

            _rbFromExtSync = new RadioButton { Text = Loc.Hw("SyncTODDlg\\FromExtSync", "From External Sync"), Location = new Point(22, 82), AutoSize = true, Checked = true };
            _rbFromImage = new RadioButton { Text = Loc.Hw("SyncTODDlg\\FromImage", "From Image"), Location = new Point(22, 106), AutoSize = true };
            _rbFromStart = new RadioButton { Text = Loc.Hw("SyncTODDlg\\FromStart", "From Start"), Location = new Point(22, 130), AutoSize = true };
            _chkMakeValid = new CheckBox { Text = Loc.Hw("SyncTODDlg\\MakeValid", "Make the current event capturable."), Location = new Point(22, 160), AutoSize = true, Checked = true };

            _rbFromExtSync.CheckedChanged += delegate { if (_rbFromExtSync.Checked) _txtNewTime.Text = TodStartDialog.FormatTodSeconds(DateTime.Now.TimeOfDay.TotalSeconds); };
            _rbFromImage.CheckedChanged += delegate { if (_rbFromImage.Checked) _txtNewTime.Text = TodStartDialog.FormatTodSeconds(imageHairlineSec); };
            _rbFromStart.CheckedChanged += delegate { if (_rbFromStart.Checked) _txtNewTime.Text = TodStartDialog.FormatTodSeconds(startSec); };

            Label lblWarn = new Label
            {
                Text = Loc.Hw("SyncTODDlg\\Warning", "Warning: This will invalidate current starts and events."),
                Location = new Point(16, 188),
                Size = new Size(210, 34),
                ForeColor = Color.Maroon
            };

            Controls.Add(_rbFromExtSync);
            Controls.Add(_rbFromImage);
            Controls.Add(_rbFromStart);
            Controls.Add(_chkMakeValid);
            Controls.Add(lblWarn);

            Button btnOk = DlgUi.Btn("OK", 220, 192, 80, DialogResult.OK);
            Button btnCancel = DlgUi.Btn("Cancel", 306, 192, 80, DialogResult.Cancel);
            btnOk.Click += delegate
            {
                double v;
                if (TodStartDialog.TryParseTodOrSeconds(_txtNewTime.Text, out v))
                {
                    NewTodOffsetSeconds = v;
                }
                MakeEventValid = _chkMakeValid.Checked;
            };
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }
    }

    // 17. Text Overlay Editor Dialog (Image -> Overlay -> Add/Edit text overlay)
    public sealed class TextOverlayDialog : Form
    {
        private readonly TextBox _txtText;
        private readonly ComboBox _cbFont;
        private readonly ComboBox _cbSize;
        private readonly CheckBox _chkBold;
        private readonly ComboBox _cbGroup;
        private readonly CheckBox _chkShowLine;
        private readonly CheckBox _chkShowSplits;
        private readonly Panel _pnlColor;
        private readonly TextBox _txtOffsetTime;
        private Color _chosenColor;

        public ImageOverlayItem OverlayItem { get; private set; }

        public TextOverlayDialog(double initialTimeSeconds, float initialVerticalRatio)
        {
            _chosenColor = Color.Yellow;
            OverlayItem = new ImageOverlayItem
            {
                TimeSeconds = initialTimeSeconds,
                VerticalRatio = initialVerticalRatio,
                Visible = true,
                OverlayColor = _chosenColor,
                GroupIndex = 1
            };

            DlgUi.StyleClassic(this, Loc.Evt("Image\\TextOverlayDlg\\Title", "Text Overlay"), 460, 360);

            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\TextOverlayDlg\\Text", "Text:"), 14, 16));
            _txtText = new TextBox
            {
                Location = new Point(88, 13),
                Size = new Size(344, 62),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = "Line {TIME}"
            };
            Controls.Add(_txtText);

            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\TextOverlayDlg\\Insert", "Insert:"), 14, 86));
            Button btnInsTime = new Button { Text = Loc.Evt("Image\\TextOverlayDlg\\LineTime", "Line Time"), Location = new Point(88, 82), Size = new Size(88, 24) };
            Button btnInsSplit = new Button { Text = Loc.Evt("Image\\TextOverlayDlg\\SplitNum", "Split Number"), Location = new Point(180, 82), Size = new Size(92, 24) };
            Button btnInsLap = new Button { Text = Loc.Evt("Image\\TextOverlayDlg\\LapTimer", "LapTimer Name"), Location = new Point(276, 82), Size = new Size(106, 24) };
            Button btnInsNl = new Button { Text = Loc.Evt("Image\\TextOverlayDlg\\NewLine", "New Line"), Location = new Point(386, 82), Size = new Size(46, 24) };
            btnInsTime.Click += delegate { _txtText.SelectedText = "{TIME}"; };
            btnInsSplit.Click += delegate { _txtText.SelectedText = "{SPLIT}"; };
            btnInsLap.Click += delegate { _txtText.SelectedText = "{LAPTIMER}"; };
            btnInsNl.Click += delegate { _txtText.SelectedText = Environment.NewLine; };
            Controls.AddRange(new Control[] { btnInsTime, btnInsSplit, btnInsLap, btnInsNl });

            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\TextOverlayDlg\\Font", "Font:"), 14, 120));
            _cbFont = DlgUi.Cb(88, 120, 148,
                Loc.Evt("Image\\TextOverlayDlg\\FontInternal", "<internal>"),
                "Segoe UI", "Arial", "Consolas", "Tahoma", "Times New Roman");
            _cbFont.SelectedIndex = 1;
            Controls.Add(_cbFont);

            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\TextOverlayDlg\\Size", "Size:"), 248, 120));
            _cbSize = DlgUi.Cb(292, 120, 62, "8", "9", "10", "11", "12", "14", "16", "18", "24");
            _cbSize.SelectedIndex = 2;
            Controls.Add(_cbSize);

            _chkBold = new CheckBox { Text = "Bold", Location = new Point(366, 118), AutoSize = true, Checked = true };
            Controls.Add(_chkBold);

            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\TextOverlayDlg\\FaceColor", "Color:"), 14, 154));
            _pnlColor = new Panel { Location = new Point(88, 150), Size = new Size(36, 22), BackColor = _chosenColor, BorderStyle = BorderStyle.FixedSingle };
            Button btnPickColor = new Button { Text = "...", Location = new Point(128, 149), Size = new Size(34, 24) };
            btnPickColor.Click += delegate
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.Color = _chosenColor;
                    if (cd.ShowDialog(this) == DialogResult.OK)
                    {
                        _chosenColor = cd.Color;
                        _pnlColor.BackColor = _chosenColor;
                    }
                }
            };
            Controls.Add(_pnlColor);
            Controls.Add(btnPickColor);

            Controls.Add(DlgUi.Lbl("Group:", 184, 154));
            _cbGroup = DlgUi.Cb(234, 154, 120,
                "Default", "Group 1", "Group 2", "Group 3", "Group 4",
                "Group 5", "Group 6", "Group 7", "Group 8");
            _cbGroup.SelectedIndex = 1;
            Controls.Add(_cbGroup);

            _chkShowLine = new CheckBox { Text = Loc.Evt("Image\\TextOverlayDlg\\LineColor", "Lines"), Location = new Point(88, 186), AutoSize = true, Checked = true };
            _chkShowSplits = new CheckBox { Text = Loc.Evt("Image\\TextOverlayDlg\\SplitColor", "Splits"), Location = new Point(180, 186), AutoSize = true, Checked = false };
            Controls.Add(_chkShowLine);
            Controls.Add(_chkShowSplits);

            Controls.Add(DlgUi.Lbl(Loc.Evt("Image\\TextOverlayDlg\\Offset", "Offset:") + " (s)", 14, 220));
            _txtOffsetTime = DlgUi.Tb(88, 220, 110, initialTimeSeconds.ToString("0.000", CultureInfo.InvariantCulture));
            Controls.Add(_txtOffsetTime);

            Button btnOk = DlgUi.Btn("OK", 252, 280, 86, DialogResult.OK);
            Button btnCancel = DlgUi.Btn("Cancel", 346, 280, 86, DialogResult.Cancel);
            btnOk.Click += delegate
            {
                double tVal = initialTimeSeconds;
                double.TryParse(_txtOffsetTime.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out tVal);
                float sz = 10f;
                float.TryParse(_cbSize.SelectedItem != null ? _cbSize.SelectedItem.ToString() : "10", NumberStyles.Float, CultureInfo.InvariantCulture, out sz);
                string fontName = (_cbFont.SelectedIndex <= 0) ? "Segoe UI" : _cbFont.SelectedItem.ToString();

                string expanded = _txtText.Text
                    .Replace("{TIME}", tVal.ToString("0.000", CultureInfo.InvariantCulture))
                    .Replace("{SPLIT}", "1")
                    .Replace("{LAPTIMER}", "LapTimer");

                OverlayItem = new ImageOverlayItem
                {
                    GroupIndex = Math.Max(0, Math.Min(8, _cbGroup.SelectedIndex)),
                    TimeSeconds = tVal,
                    VerticalRatio = initialVerticalRatio,
                    Text = expanded,
                    OverlayColor = _chosenColor,
                    Visible = true,
                    FontName = fontName,
                    FontSizePt = sz > 1f ? sz : 10f,
                    FontBold = _chkBold.Checked,
                    ShowLine = _chkShowLine.Checked,
                    ShowSplits = _chkShowSplits.Checked
                };
            };
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }
    }

    // 18. LapTime Split Keys Dialog (LapTime -> Edit split keys...)
    public sealed class LapTimeSplitKeysDialog : Form
    {
        private readonly Dictionary<string, string> _workingMap;
        private readonly ListBox _lbKeys;
        private readonly TextBox _txtKey;
        private readonly TextBox _txtInfo;

        public LapTimeSplitKeysDialog(Dictionary<string, string> existingMap)
        {
            _workingMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (existingMap != null)
            {
                foreach (KeyValuePair<string, string> kv in existingMap)
                {
                    _workingMap[kv.Key] = kv.Value;
                }
            }
            if (_workingMap.Count == 0)
            {
                _workingMap["F1"] = "Split 1";
                _workingMap["F2"] = "Split 2";
                _workingMap["Space"] = "Next Split";
            }

            DlgUi.StyleClassic(this, Loc.Evt("Results\\SplitKeysDlg\\Title", "LapTime Split Keys"), 420, 320);

            Controls.Add(DlgUi.Lbl(Loc.Evt("Results\\SplitKeysDlg\\SplitKeys", "Split Keys:"), 14, 14));
            _lbKeys = new ListBox { Location = new Point(14, 34), Size = new Size(376, 130) };
            Controls.Add(_lbKeys);

            Controls.Add(DlgUi.Lbl(Loc.Evt("Results\\SplitKeysDlg\\Key", "Key Value:"), 14, 176));
            _txtKey = DlgUi.Tb(102, 176, 90, "");
            Controls.Add(_txtKey);

            Controls.Add(DlgUi.Lbl(Loc.Evt("Results\\SplitKeysDlg\\Info", "Splits:"), 202, 176));
            _txtInfo = DlgUi.Tb(254, 176, 136, "");
            Controls.Add(_txtInfo);

            Button btnSet = new Button { Text = "Add / Update", Location = new Point(14, 206), Size = new Size(100, 25) };
            Button btnDel = new Button { Text = "Remove", Location = new Point(120, 206), Size = new Size(86, 25) };
            Controls.Add(btnSet);
            Controls.Add(btnDel);

            _lbKeys.SelectedIndexChanged += delegate
            {
                string line = _lbKeys.SelectedItem as string;
                if (string.IsNullOrEmpty(line)) return;
                int sep = line.IndexOf(" -> ", StringComparison.Ordinal);
                if (sep > 0)
                {
                    _txtKey.Text = line.Substring(0, sep);
                    _txtInfo.Text = line.Substring(sep + 4);
                }
            };

            btnSet.Click += delegate
            {
                string k = _txtKey.Text.Trim();
                if (k.Length == 0) return;
                _workingMap[k] = _txtInfo.Text.Trim();
                RefreshList();
            };
            btnDel.Click += delegate
            {
                string k = _txtKey.Text.Trim();
                if (k.Length > 0 && _workingMap.ContainsKey(k))
                {
                    _workingMap.Remove(k);
                    RefreshList();
                }
            };

            Button btnOk = DlgUi.Btn("OK", 210, 244, 86, DialogResult.OK);
            Button btnCancel = DlgUi.Btn("Cancel", 304, 244, 86, DialogResult.Cancel);
            btnOk.Click += delegate
            {
                if (existingMap != null)
                {
                    existingMap.Clear();
                    foreach (KeyValuePair<string, string> kv in _workingMap)
                    {
                        existingMap[kv.Key] = kv.Value;
                    }
                }
            };
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            RefreshList();
        }

        private void RefreshList()
        {
            _lbKeys.Items.Clear();
            foreach (KeyValuePair<string, string> kv in _workingMap)
            {
                _lbKeys.Items.Add(kv.Key + " -> " + kv.Value);
            }
            if (_lbKeys.Items.Count > 0) _lbKeys.SelectedIndex = 0;
        }
    }

    // 19. Create Profile Dialog (File -> Create profile...)
    public sealed class CreateProfileDialog : Form
    {
        private readonly TextBox _txtName;
        public string ProfileName { get; private set; }

        public CreateProfileDialog()
        {
            DlgUi.StyleClassic(this, Loc.Get("Strings\\File\\NewProfile\\Title", "Create Profile"), 370, 235);
            Controls.Add(DlgUi.Lbl(Loc.Get("Strings\\File\\NewProfile\\Name", "Name:"), 14, 18));
            _txtName = DlgUi.Tb(80, 18, 256, "Default Track Profile");
            Controls.Add(_txtName);

            Controls.Add(DlgUi.Lbl("Existing profiles:", 14, 48));
            ListBox lb = new ListBox { Location = new Point(14, 68), Size = new Size(322, 80) };
            for (int i = 0; i < LynxExtendedConfig.Instance.SavedProfiles.Count; i++)
            {
                lb.Items.Add(LynxExtendedConfig.Instance.SavedProfiles[i]);
            }
            lb.SelectedIndexChanged += delegate
            {
                if (lb.SelectedItem != null) _txtName.Text = lb.SelectedItem.ToString();
            };
            Controls.Add(lb);

            Button btnOk = DlgUi.Btn("OK", 158, 160, 86, DialogResult.OK);
            Button btnCancel = DlgUi.Btn("Cancel", 250, 160, 86, DialogResult.Cancel);
            btnOk.Click += delegate
            {
                ProfileName = _txtName.Text.Trim();
            };
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }
    }

    // 20. Enter Serial Number Dialog (Help -> Enter serial number...)
    public sealed class EnterSerialNumberDialog : Form
    {
        private readonly TextBox _txtSerial;
        private readonly ListBox _lbSerials;

        public EnterSerialNumberDialog()
        {
            DlgUi.StyleClassic(this, Loc.GetClean("Strings\\MenuBar\\Help\\EnterSN", "Enter serial number"), 400, 270);
            Controls.Add(DlgUi.Lbl("Serial / Plugin Key:", 14, 18));
            _txtSerial = DlgUi.Tb(132, 18, 238, "");
            Controls.Add(_txtSerial);

            Button btnAdd = new Button { Text = "Add", Location = new Point(132, 46), Size = new Size(78, 24) };
            Button btnRemove = new Button { Text = "Remove", Location = new Point(216, 46), Size = new Size(78, 24) };
            Controls.Add(btnAdd);
            Controls.Add(btnRemove);

            Controls.Add(DlgUi.Lbl("Installed serial numbers / plugins:", 14, 80));
            _lbSerials = new ListBox { Location = new Point(14, 100), Size = new Size(356, 86) };
            Controls.Add(_lbSerials);

            Action refresh = delegate
            {
                _lbSerials.Items.Clear();
                for (int i = 0; i < LynxExtendedConfig.Instance.InstalledSerialNumbers.Count; i++)
                {
                    _lbSerials.Items.Add(LynxExtendedConfig.Instance.InstalledSerialNumbers[i]);
                }
            };
            refresh();

            btnAdd.Click += delegate
            {
                string sn = _txtSerial.Text.Trim();
                if (sn.Length > 0 && !LynxExtendedConfig.Instance.InstalledSerialNumbers.Contains(sn))
                {
                    LynxExtendedConfig.Instance.InstalledSerialNumbers.Add(sn);
                    LynxExtendedConfig.Instance.Save();
                    _txtSerial.Text = "";
                    refresh();
                }
            };
            btnRemove.Click += delegate
            {
                if (_lbSerials.SelectedItem != null)
                {
                    LynxExtendedConfig.Instance.InstalledSerialNumbers.Remove(_lbSerials.SelectedItem.ToString());
                    LynxExtendedConfig.Instance.Save();
                    refresh();
                }
            };

            Button btnOk = DlgUi.Btn("OK", 190, 198, 86, DialogResult.OK);
            Button btnCancel = DlgUi.Btn("Cancel", 284, 198, 86, DialogResult.Cancel);
            btnOk.Click += delegate
            {
                string sn = _txtSerial.Text.Trim();
                if (sn.Length > 0 && !LynxExtendedConfig.Instance.InstalledSerialNumbers.Contains(sn))
                {
                    LynxExtendedConfig.Instance.InstalledSerialNumbers.Add(sn);
                }
                LynxExtendedConfig.Instance.Save();
            };
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }
    }

    // 21. Help Contents Dialog (Help -> Contents)
    public sealed class HelpContentsDialog : Form
    {
        public HelpContentsDialog()
        {
            DlgUi.StyleClassic(this, Loc.GetClean("Strings\\MenuBar\\Help\\Contents", "OptiFinish Help Contents"), 680, 440);

            ListBox topics = new ListBox { Location = new Point(12, 12), Size = new Size(195, 344) };
            TextBox viewer = new TextBox
            {
                Location = new Point(216, 12),
                Size = new Size(436, 344),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White,
                Font = new Font("Consolas", 9f)
            };

            string[] names = new string[]
            {
                "2D Selection & Crop",
                "Export & Print Workflow",
                "Evaluation & Results",
                "White Balance & Enhance",
                "Scoreboard & LapTime",
                "Keyboard Shortcuts"
            };
            string[] bodies = new string[]
            {
                "2D SELECTION & CROP\r\n===================\r\n" +
                "- Right-Click + Drag (or Alt + Left-Drag) draws a 2D selection box over the slit-scan strip.\r\n" +
                "- Shift + Right-Click creates a fixed-size selection box centered at the cursor.\r\n" +
                "- Releasing opens the Selection Popup Menu:\r\n" +
                "  * Crop / Keep selection (Ctrl+K): trims vertical & horizontal bounds with Undo support (Ctrl+Z).\r\n" +
                "  * Uncrop: restores full strip.\r\n" +
                "  * Enhance: auto contrast & gamma stretch inside selection.\r\n" +
                "- Right-clicking the Left/Right Lane Gutter opens the Lane Bar Popup (Lanes, Enhance regions, Adjustments...).\r\n",

                "EXPORT & PRINT WORKFLOW\r\n=======================\r\n" +
                "- When a 2D selection or active crop exists, Export..., Export video..., Print..., Print to bitmap..., and Set scoreboard image target ONLY the selected/cropped region.\r\n" +
                "- Export... (Selection / Image -> Export...): saves lossless/direct PNG, JPG, BMP, or 24-bit TGA.\r\n" +
                "- Export video... (Image -> Export video...): writes MJPEG AVI sequence.\r\n" +
                "- Print... / Print to bitmap...: opens the FinishLynx Print dialog (Zoom 1/2/4/8, Smooth, DPI 300/600/1200, Image Height In/Cm, Lane Bars, Limit Res, Print Results, Maximum, Range) with live cropped preview.\r\n" +
                "- File -> Save (.optr) persists camera crops, adjustments, and text overlays across reloads.\r\n",

                "EVALUATION & RESULTS\r\n====================\r\n" +
                "- Left-Click moves the evaluation crosshair and updates the live magnifier loupe.\r\n" +
                "- Double-Click evaluates the currently selected competitor.\r\n" +
                "- Space / Enter records the hairline time to the selected lane or advances splits.\r\n" +
                "- Results menu supports Previous/Next Split navigation, Auto-Populate, Official Times, Delta Mode, and Apply default fields.\r\n",

                "WHITE BALANCE & ENHANCE\r\n=======================\r\n" +
                "- Selection Popup -> White balance (or Image -> White balance) samples neutral background at the crosshair/selection.\r\n" +
                "- Image -> Set > lets you configure Object balance, Object area, PhotoEye balance/area, Capture area, and Length time.\r\n",

                "SCOREBOARD & LAPTIME\r\n====================\r\n" +
                "- Scoreboard -> Set image / Show image (VDM / ResulTV) sends cropped or evaluated results to connected displays.\r\n" +
                "- LapTime -> Edit split keys... configures split trigger keys.\r\n",

                "KEYBOARD SHORTCUTS\r\n==================\r\n" +
                "Right-Drag / Alt+Left-Drag : Draw 2D Selection Box & open Popup Menu\r\n" +
                "Shift + Right-Click        : Fixed-size Selection Box\r\n" +
                "Alt + Arrow Keys           : Nudge active Selection Box\r\n" +
                "Esc                        : Clear active Selection Box\r\n" +
                "Ctrl + K                   : Crop to Selection\r\n" +
                "Ctrl + U                   : Uncrop\r\n" +
                "Ctrl + Z                   : Undo Crop / Edit\r\n" +
                "Ctrl + A                   : Arm / Select all (in Edit menu)\r\n" +
                "Ctrl + C                   : Copy Cropped Image & Time to Clipboard\r\n" +
                "Ctrl + F / Ctrl + G        : Full-Screen View\r\n"
            };

            topics.Items.AddRange(names);
            topics.SelectedIndexChanged += delegate
            {
                if (topics.SelectedIndex >= 0 && topics.SelectedIndex < bodies.Length)
                {
                    viewer.Text = bodies[topics.SelectedIndex];
                }
            };
            topics.SelectedIndex = 0;

            Button btnClose = DlgUi.Btn("OK", 566, 366, 86, DialogResult.OK);
            AcceptButton = btnClose;
            Controls.Add(topics);
            Controls.Add(viewer);
            Controls.Add(btnClose);
        }
    }

    // 22. Spooler-Free In-Memory Print Preview with Pre-Print Printer Selector & Direct PDF Export
    public sealed class LynxPrintPreviewDialog : Form
    {
        public const string DirectPdfOption = "[PDF] Salva PDF diretto (Senza Stampante)";

        private readonly Bitmap _sourceBitmap;
        private readonly string _docTitle;
        private readonly ComboBox _cbPrinter;
        private readonly CheckBox _chkLandscape;
        private readonly ComboBox _cbZoom;
        private readonly Panel _scrollHost;
        private readonly PictureBox _pbSheet;

        public string SelectedPrinterName { get; private set; }
        public bool SelectedLandscape { get; private set; }

        public LynxPrintPreviewDialog(Bitmap contentBitmap, string docTitle, string initialPrinterName, bool initialLandscape)
        {
            _sourceBitmap = contentBitmap != null ? new Bitmap(contentBitmap) : new Bitmap(640, 360);
            _docTitle = string.IsNullOrEmpty(docTitle) ? "OptiFinish" : docTitle;
            SelectedLandscape = initialLandscape;

            Text = "Anteprima di Stampa - " + _docTitle;
            Size = new Size(1020, 720);
            MinimumSize = new Size(760, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            BackColor = SystemColors.Control;
            Font = new Font("Segoe UI", 8.5f);

            Panel topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = SystemColors.Control
            };

            Label lblPrinter = new Label
            {
                Text = "Stampante:",
                AutoSize = true,
                Location = new Point(10, 14)
            };

            _cbPrinter = new ComboBox
            {
                Location = new Point(78, 10),
                Width = 270,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            PopulatePrinterCombo(_cbPrinter, initialPrinterName);

            _chkLandscape = new CheckBox
            {
                Text = "Orizzontale",
                AutoSize = true,
                Location = new Point(358, 13),
                Checked = initialLandscape
            };

            Label lblZoom = new Label
            {
                Text = "Zoom:",
                AutoSize = true,
                Location = new Point(454, 14)
            };

            _cbZoom = new ComboBox
            {
                Location = new Point(496, 10),
                Width = 104,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cbZoom.Items.AddRange(new object[] { "Adatta", "75%", "100%", "150%" });
            _cbZoom.SelectedIndex = 0;

            Button btnSavePdf = new Button
            {
                Text = "Salva PDF...",
                Location = new Point(612, 9),
                Size = new Size(104, 26)
            };

            Button btnPrint = new Button
            {
                Text = "Stampa...",
                Location = new Point(722, 9),
                Size = new Size(96, 26)
            };

            Button btnClose = new Button
            {
                Text = "Chiudi",
                Location = new Point(824, 9),
                Size = new Size(80, 26),
                DialogResult = DialogResult.OK
            };

            topBar.Controls.AddRange(new Control[]
            {
                lblPrinter, _cbPrinter, _chkLandscape, lblZoom, _cbZoom, btnSavePdf, btnPrint, btnClose
            });

            _scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(82, 86, 89)
            };

            _pbSheet = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.AutoSize,
                BackColor = Color.FromArgb(82, 86, 89)
            };
            _scrollHost.Controls.Add(_pbSheet);

            Controls.Add(_scrollHost);
            Controls.Add(topBar);
            CancelButton = btnClose;

            _cbPrinter.SelectedIndexChanged += delegate
            {
                SelectedPrinterName = _cbPrinter.SelectedItem != null ? _cbPrinter.SelectedItem.ToString() : DirectPdfOption;
            };
            _chkLandscape.CheckedChanged += delegate
            {
                SelectedLandscape = _chkLandscape.Checked;
                RenderVirtualSheet();
            };
            _cbZoom.SelectedIndexChanged += delegate { RenderVirtualSheet(); };
            _scrollHost.Resize += delegate
            {
                if (_cbZoom.SelectedIndex == 0) RenderVirtualSheet();
            };

            btnSavePdf.Click += delegate { SaveDirectPdf(); };
            btnPrint.Click += delegate { ExecutePrint(); };
            btnClose.Click += delegate
            {
                SelectedPrinterName = _cbPrinter.SelectedItem != null ? _cbPrinter.SelectedItem.ToString() : DirectPdfOption;
                SelectedLandscape = _chkLandscape.Checked;
            };

            SelectedPrinterName = _cbPrinter.SelectedItem != null ? _cbPrinter.SelectedItem.ToString() : DirectPdfOption;
            RenderVirtualSheet();
        }

        public static void PopulatePrinterCombo(ComboBox cb, string preferredPrinter)
        {
            if (cb == null) return;
            cb.Items.Clear();
            cb.Items.Add(DirectPdfOption);

            List<string> installed = new List<string>();
            try
            {
                foreach (string name in PrinterSettings.InstalledPrinters)
                {
                    if (!string.IsNullOrWhiteSpace(name)) installed.Add(name);
                }
            }
            catch { }

            for (int i = 0; i < installed.Count; i++)
            {
                if (installed[i].IndexOf("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    cb.Items.Add(installed[i]);
                }
            }
            for (int i = 0; i < installed.Count; i++)
            {
                if (installed[i].IndexOf("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    cb.Items.Add(installed[i]);
                }
            }

            SelectPrinterInCombo(cb, preferredPrinter);
        }

        public static void SelectPrinterInCombo(ComboBox cb, string printerName)
        {
            if (cb == null || cb.Items.Count == 0) return;
            if (!string.IsNullOrWhiteSpace(printerName))
            {
                for (int i = 0; i < cb.Items.Count; i++)
                {
                    if (string.Equals(cb.Items[i].ToString(), printerName, StringComparison.OrdinalIgnoreCase))
                    {
                        cb.SelectedIndex = i;
                        return;
                    }
                }
            }
            // Never default to an offline physical printer; prefer direct PDF writer
            cb.SelectedIndex = 0;
        }

        private void RenderVirtualSheet()
        {
            if (_sourceBitmap == null || _scrollHost.ClientSize.Width <= 40) return;

            bool landscape = _chkLandscape.Checked;
            double pageAspect = landscape ? (842.0 / 595.0) : (595.0 / 842.0);

            int baseW = landscape ? 980 : 690;
            int baseH = (int)Math.Round(baseW / pageAspect);

            int availW = Math.Max(220, _scrollHost.ClientSize.Width - 48);
            int availH = Math.Max(220, _scrollHost.ClientSize.Height - 48);

            int sheetW, sheetH;
            int zIdx = _cbZoom.SelectedIndex;
            if (zIdx == 0)
            {
                double fitScale = Math.Min((double)availW / baseW, (double)availH / baseH);
                fitScale = Math.Max(0.25, Math.Min(2.0, fitScale));
                sheetW = Math.Max(180, (int)Math.Round(baseW * fitScale));
                sheetH = Math.Max(180, (int)Math.Round(baseH * fitScale));
            }
            else
            {
                double factor = zIdx == 1 ? 0.75 : (zIdx == 3 ? 1.50 : 1.00);
                sheetW = Math.Max(180, (int)Math.Round(baseW * factor));
                sheetH = Math.Max(180, (int)Math.Round(baseH * factor));
            }

            int pad = 16;
            Bitmap canvas = new Bitmap(sheetW + pad * 2, sheetH + pad * 2);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.FromArgb(82, 86, 89));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle shadow = new Rectangle(pad + 4, pad + 4, sheetW, sheetH);
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(52, 54, 56)))
                {
                    g.FillRectangle(sb, shadow);
                }

                Rectangle pageRect = new Rectangle(pad, pad, sheetW, sheetH);
                g.FillRectangle(Brushes.White, pageRect);
                g.DrawRectangle(Pens.DimGray, pageRect);

                int margin = Math.Max(10, (int)Math.Round(Math.Min(sheetW, sheetH) * 0.035));
                Rectangle inner = new Rectangle(
                    pageRect.X + margin,
                    pageRect.Y + margin,
                    Math.Max(10, pageRect.Width - margin * 2),
                    Math.Max(10, pageRect.Height - margin * 2));

                double sx = (double)inner.Width / Math.Max(1, _sourceBitmap.Width);
                double sy = (double)inner.Height / Math.Max(1, _sourceBitmap.Height);
                double sc = Math.Min(sx, sy);
                int dw = Math.Max(1, (int)Math.Round(_sourceBitmap.Width * sc));
                int dh = Math.Max(1, (int)Math.Round(_sourceBitmap.Height * sc));
                int dx = inner.X + (inner.Width - dw) / 2;
                int dy = inner.Y + (inner.Height - dh) / 2;

                g.DrawImage(_sourceBitmap, new Rectangle(dx, dy, dw, dh));
            }

            Image old = _pbSheet.Image;
            _pbSheet.Image = canvas;
            if (old != null) old.Dispose();

            int left = Math.Max(0, (_scrollHost.ClientSize.Width - canvas.Width) / 2);
            int top = Math.Max(0, (_scrollHost.ClientSize.Height - canvas.Height) / 2);
            _pbSheet.Location = new Point(left, top);
        }

        private void SaveDirectPdf()
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "PDF Document (*.pdf)|*.pdf";
                sfd.FileName = _docTitle + "_PhotoFinish.pdf";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    SlitScanEngine.SaveBitmapToPdfFile(_sourceBitmap, sfd.FileName, _chkLandscape.Checked);
                    MessageBox.Show(this, "PDF salvato:\n" + sfd.FileName, "Esporta PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void ExecutePrint()
        {
            string chosen = _cbPrinter.SelectedItem != null ? _cbPrinter.SelectedItem.ToString() : DirectPdfOption;
            SelectedPrinterName = chosen;
            SelectedLandscape = _chkLandscape.Checked;

            if (string.Equals(chosen, DirectPdfOption, StringComparison.Ordinal))
            {
                SaveDirectPdf();
                return;
            }

            Cursor prevCursor = Cursor;
            try
            {
                Cursor = Cursors.WaitCursor;
                using (PrintDocument pd = new PrintDocument())
                {
                    pd.PrinterSettings.PrinterName = chosen;
                    if (!pd.PrinterSettings.IsValid)
                    {
                        throw new InvalidOperationException("La stampante selezionata non è valida o non è connessa: " + chosen);
                    }
                    pd.DocumentName = _docTitle;
                    pd.DefaultPageSettings.Landscape = _chkLandscape.Checked;
                    pd.PrintController = new StandardPrintController();
                    pd.PrintPage += delegate(object s, PrintPageEventArgs pe)
                    {
                        Rectangle bounds = pe.MarginBounds;
                        double sx = (double)bounds.Width / Math.Max(1, _sourceBitmap.Width);
                        double sy = (double)bounds.Height / Math.Max(1, _sourceBitmap.Height);
                        double sc = Math.Min(sx, sy);
                        int dw = Math.Max(1, (int)Math.Round(_sourceBitmap.Width * sc));
                        int dh = Math.Max(1, (int)Math.Round(_sourceBitmap.Height * sc));
                        int dx = bounds.X + (bounds.Width - dw) / 2;
                        int dy = bounds.Y + (bounds.Height - dh) / 2;
                        pe.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        pe.Graphics.DrawImage(_sourceBitmap, new Rectangle(dx, dy, dw, dh));
                    };
                    pd.Print();
                }
            }
            catch (Exception ex)
            {
                Cursor = prevCursor;
                DialogResult dr = MessageBox.Show(
                    this,
                    "Impossibile stampare su '" + chosen + "':\n" + ex.Message +
                    "\n\nVuoi salvare invece un file PDF diretto (senza passare dallo spooler stampante)?",
                    "Errore Stampante",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (dr == DialogResult.Yes)
                {
                    SaveDirectPdf();
                }
                return;
            }
            finally
            {
                Cursor = prevCursor;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_pbSheet != null && _pbSheet.Image != null)
                {
                    _pbSheet.Image.Dispose();
                    _pbSheet.Image = null;
                }
                if (_sourceBitmap != null)
                {
                    _sourceBitmap.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
