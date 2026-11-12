using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using OptiFinish.Core;
using OptiFinish.UI;

namespace OptiFinish
{
    public sealed class MainWindow : Form
    {
        private readonly HardwareTransceiver _hardware;
        private readonly List<RaceEventDocument> _openEvents = new List<RaceEventDocument>();
        private readonly List<EventWindowControl> _eventControls = new List<EventWindowControl>();
        private List<ScheduledEventSummary> _schedule = new List<ScheduledEventSummary>();
        private List<DirectoryAthlete> _directory = new List<DirectoryAthlete>();
        private int _scheduleIndex = -1;

        private Panel _workspaceHost;
        private HardwareControlPanel _hwcPanel;
        private Control _activeView;
        private int _workspaceArrangeMode; // 0=Maximized, 1=Cascade, 2=TileH, 3=TileV
        private ImageAdjustmentsFloater _adjFloater;
        private KeypadFloater _keypadFloater;

        // Top Master Icon Bar Controls
        private Panel _topToolBar;
        private LynxIconButton _btnArmTop, _btnCaptureTop;
        private ComboBox _cbStarts, _cbOpenEvents, _cbQuickPresets;
        private TextBox _txtRunTime;
        private Label _lblRunStatus, _lblSerialStatus;

        // Bottom Dual Status Bar (Help Zone + Status Buffer Zone)
        private StatusStrip _statusBar;
        private ToolStripStatusLabel _lblHelpZone, _lblBufferText;
        private ToolStripControlHost _bufferHost;
        private ProgressBar _pbBuffer;

        // Menu dynamic window list
        private ToolStripMenuItem _mnuWindow;
        private ToolStripMenuItem _mnuRetention, _mnuShowEvOverlay, _mnuSmoothZoom, _mnuScrollNew;
        private ToolStripMenuItem _mnuScbTod, _mnuScb24h, _mnuScbStopTime;

        // Timers
        private Timer _runClockTimer;
        private Timer _autoSaveTimer;
        private bool _runClockRunning;
        private bool _runClockPaused;
        private bool _clockShowTod;
        private DateTime _runClockStartTime;
        private double _pausedElapsedSec;
        private double _simulatedFreezeElapsedSec;
        private bool _suppressEventCombo;

        public RaceEventDocument ActiveDocument
        {
            get
            {
                EventWindowControl ew = ActiveEventControl;
                if (ew != null && ew.Document != null) return ew.Document;
                return _openEvents.Count > 0 ? _openEvents[_openEvents.Count - 1] : null;
            }
        }

        public EventWindowControl ActiveEventControl
        {
            get
            {
                EventWindowControl cur = _activeView as EventWindowControl;
                if (cur != null && !cur.IsDisposed) return cur;
                for (int i = _eventControls.Count - 1; i >= 0; i--)
                {
                    if (!_eventControls[i].IsDisposed) return _eventControls[i];
                }
                return null;
            }
        }

        public MainWindow()
        {
            _hardware = new HardwareTransceiver();
            ApplyConfigToHardware();
            Loc.SetLanguage(LynxConfigService.Instance.Language);

            Text = "OptiFinish";
            Size = new Size(1380, 860);
            MinimumSize = new Size(1024, 680);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = SystemColors.AppWorkspace;
            Font = new Font("Segoe UI", 8.5f);
            KeyPreview = true;

            try { Icon = AppIconGenerator.CreateMemoryIcon(32); }
            catch { }

            _workspaceHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SystemColors.AppWorkspace
            };

            BuildBottomStatusBar();
            BuildTopMasterToolbar();
            BuildMenuBar();

            // WinForms Dock priority: index 0 = innermost (Fill), higher indices = outermost (Top / Bottom)
            Controls.Clear();
            Controls.Add(_workspaceHost);
            Controls.Add(_statusBar);
            Controls.Add(_topToolBar);
            if (MainMenuStrip != null) Controls.Add(MainMenuStrip);

            SetupTimers();
            LoadInitialStartupData();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            EnsureHardwareControlPanel();
            if (_openEvents.Count > 0 && _eventControls.Count == 0)
            {
                OpenEventMdiWindow(_openEvents[0], true);
            }
            UpdateTitleAndStatus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_runClockTimer != null) { _runClockTimer.Stop(); _runClockTimer.Dispose(); }
                if (_autoSaveTimer != null) { _autoSaveTimer.Stop(); _autoSaveTimer.Dispose(); }
                if (_adjFloater != null && !_adjFloater.IsDisposed) _adjFloater.Dispose();
                if (_keypadFloater != null && !_keypadFloater.IsDisposed) _keypadFloater.Dispose();
                for (int i = 0; i < _openEvents.Count; i++) _openEvents[i].Dispose();
                _openEvents.Clear();
                if (_hardware != null) _hardware.Dispose();
            }
            base.Dispose(disposing);
        }

        private void SetupTimers()
        {
            _runClockTimer = new Timer { Interval = 65 };
            _runClockTimer.Tick += OnRunClockTick;

            _autoSaveTimer = new Timer { Interval = Math.Max(5, LynxConfigService.Instance.AutoSaveIntervalSeconds) * 1000 };
            _autoSaveTimer.Tick += delegate
            {
                if (LynxConfigService.Instance.AutoSave && ActiveDocument != null)
                {
                    string rep;
                    LynxConfigService.Instance.PerformAutosave(ActiveDocument, out rep);
                }
            };
            if (LynxConfigService.Instance.AutoSave) _autoSaveTimer.Start();
        }

        private void LoadInitialStartupData()
        {
            string sampleDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleData");
            List<string> nativeEvns = MeetDatabaseService.DiscoverNativeLynxEventFiles(sampleDir);
            RaceEventDocument initialDoc = null;
            if (nativeEvns.Count > 0)
            {
                try { initialDoc = MeetDatabaseService.LoadNativeLynxEventFile(nativeEvns[0]); }
                catch { initialDoc = null; }
            }
            if (initialDoc == null)
            {
                initialDoc = MeetDatabaseService.CreateSample100mHurdlesWomen();
            }
            _openEvents.Add(initialDoc);
            Text = "OptiFinish - [" + initialDoc.EventName + "]";
            SyncTopLists();
        }

        // ===================== MENU BAR (9 Authentic Localized Menus) =====================
        private void BuildMenuBar()
        {
            if (MainMenuStrip != null)
            {
                Controls.Remove(MainMenuStrip);
                MainMenuStrip.Dispose();
                MainMenuStrip = null;
            }
            MenuStrip ms = new MenuStrip
            {
                Dock = DockStyle.Top,
                RenderMode = ToolStripRenderMode.System,
                BackColor = SystemColors.MenuBar,
                ShowItemToolTips = true
            };

            // 1. FILE
            ToolStripMenuItem mFile = new ToolStripMenuItem(Loc.Menu("File\\Title", "&File"));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\New", "&New"), Keys.Control | Keys.N, delegate { CreateNewBlankEvent(); }));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\Open", "&Open..."), Keys.Control | Keys.O, delegate { OpenEventDialog(); }));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\QuickOpen", "Open &recent..."), Keys.Alt | Keys.O, delegate { OpenRecentDialogFlow(); }));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\CloseWindow", "Close &window"), Keys.Control | Keys.W, delegate { CloseActiveMdiEvent(); }));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\Save", "&Save"), Keys.Control | Keys.S, delegate { SaveActiveEvent(false); }));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\SaveAs", "Save &as..."), Keys.None, delegate { SaveActiveEvent(true); }));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\SaveLIF", "Save &LIF..."), Keys.None, delegate { ExportLifDialog(); }));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\Reload", "&Reload"), Keys.Control | Keys.L, delegate { ReloadActiveEvent(); }));
            mFile.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem mPresets = new ToolStripMenuItem("Sample &Presets");
            mPresets.DropDownItems.Add("100m Hurdles Women", null, delegate { LoadPreset(0); });
            mPresets.DropDownItems.Add("100m Men Dead Heat", null, delegate { LoadPreset(1); });
            mPresets.DropDownItems.Add("1500m Distance Run", null, delegate { LoadPreset(2); });
            string sDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleData");
            List<string> evns = MeetDatabaseService.DiscoverNativeLynxEventFiles(sDir);
            if (evns.Count > 0)
            {
                mPresets.DropDownItems.Add(new ToolStripSeparator());
                for (int i = 0; i < evns.Count; i++)
                {
                    string ep = evns[i];
                    mPresets.DropDownItems.Add("Native: " + Path.GetFileName(ep), null, delegate { OpenEventFilePath(ep); });
                }
            }
            mFile.DropDownItems.Add(mPresets);
            mFile.DropDownItems.Add(MkMenu("Load &Schedule (.evt)...", Keys.None, delegate { LoadScheduleFileDialog(); }));
            mFile.DropDownItems.Add(MkMenu("Load &Directory (.ppl)...", Keys.None, delegate { LoadDirectoryFileDialog(); }));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\PreviousEvent", "&Previous event"), Keys.None, delegate { NavigateSchedule(-1); }));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\GotoEvent", "&Goto event..."), Keys.None, delegate { OpenGotoEventDialog(); }));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\NextEvent", "Ne&xt event"), Keys.None, delegate { NavigateSchedule(1); }));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\Backup", "&Backup events"), Keys.None, delegate { BackupAllEvents(); }));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\Options", "Op&tions..."), Keys.None, delegate { OpenOptions(0); }));
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\OtherSettings", "Other settings..."), Keys.None, delegate { using (OtherSettingsDialog d = new OtherSettingsDialog()) d.ShowDialog(this); }));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(MkMenu(Loc.Menu("File\\Exit", "E&xit"), Keys.None, delegate { Close(); }));

            // 2. EDIT
            ToolStripMenuItem mEdit = new ToolStripMenuItem(Loc.Menu("Edit\\Title", "&Edit"));
            mEdit.DropDownItems.Add(MkMenu(Loc.Menu("Edit\\Undo", "&Undo"), Keys.Control | Keys.Z, delegate { SendKeys.Send("^z"); }));
            mEdit.DropDownItems.Add(MkMenu(Loc.Menu("Edit\\Cut", "Cu&t"), Keys.Control | Keys.X, delegate { SendKeys.Send("^x"); }));
            mEdit.DropDownItems.Add(MkMenu(Loc.Menu("Edit\\Copy", "&Copy Time"), Keys.Control | Keys.C, delegate { CopyActiveTimeToClipboard(); }));
            mEdit.DropDownItems.Add(MkMenu(Loc.Menu("Edit\\Paste", "&Paste"), Keys.Control | Keys.V, delegate { SendKeys.Send("^v"); }));
            mEdit.DropDownItems.Add(MkMenu(Loc.Menu("Edit\\Clear", "C&lear Annotation"), Keys.None, delegate { ClearActiveAnnotations(); }));

            // 3. EVENT
            ToolStripMenuItem mEvent = new ToolStripMenuItem(Loc.Menu("Event\\Title", "E&vent"));
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\GetInfo", "&Get info..."), Keys.None, delegate { OpenSelectedCameraSettings(); }));
            mEvent.DropDownItems.Add(new ToolStripSeparator());
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\Arm", "&Arm"), Keys.Control | Keys.A, delegate { SetArmedState(true); }));
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\Disarm", "Disarm"), Keys.None, delegate { SetArmedState(false); }));
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\ImageStart", "&Image start"), Keys.None, delegate { ApplyImageStartFromCrosshair(); }));
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\ManualStart", "&Manual start..."), Keys.None, delegate { OpenManualStartDialog(); }));
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\TODStart", "T&OD start..."), Keys.None, delegate { TriggerTimeOfDayStart(); }));
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\SetStart", "Apply current start"), Keys.Control | Keys.T, delegate { ApplySelectedStartToEvent(); }));
            mEvent.DropDownItems.Add(new ToolStripSeparator());
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\ManualWind", "Manual &wind..."), Keys.Alt | Keys.W, delegate { OpenWindDialog(); }));
            mEvent.DropDownItems.Add(MkMenu("Simulate Ultrasonic &Wind", Keys.None, delegate { SimulateWindReading(); }));
            mEvent.DropDownItems.Add(new ToolStripSeparator());
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\ToggleCapture", "Toggle &capture"), Keys.Alt | Keys.T, delegate { ToggleCaptureState(); }));
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\AdvanceCaptLeg", "Advance capture"), Keys.None, delegate { if (ActiveEventControl != null) ActiveEventControl.AdvanceCaptureSplit(); }));
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\AddLeg", "Add split location..."), Keys.None, delegate { AddSplitLocationFlow(); }));
            mEvent.DropDownItems.Add(new ToolStripSeparator());
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\ZeroTest", "&Zero gun test..."), Keys.None, delegate { OpenZeroGunTestDialog(); }));
            mEvent.DropDownItems.Add(MkMenu(Loc.Menu("Event\\Options", "Op&tions..."), Keys.None, delegate { OpenOptions(1); }));

            // 4. IMAGE
            ToolStripMenuItem mImage = new ToolStripMenuItem(Loc.Menu("Image\\Title", "&Image"));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\ZoomOut", "Zoom &out"), Keys.Control | Keys.OemMinus, delegate { if (ActiveEventControl != null) ActiveEventControl.ZoomStep(-1); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\ZoomIn", "Zoom &in"), Keys.Control | Keys.Oemplus, delegate { if (ActiveEventControl != null) ActiveEventControl.ZoomStep(1); }));
            _mnuSmoothZoom = MkMenu(Loc.Menu("Image\\SmoothZoom", "Smooth &zoom"), Keys.None, delegate { ToggleSmoothZoom(); });
            _mnuScrollNew = MkMenu(Loc.Menu("Image\\ScrollNewImage", "Scroll new image"), Keys.None, delegate { ToggleScrollNewImage(); });
            _mnuScrollNew.ShortcutKeyDisplayString = "Tab";
            mImage.DropDownItems.Add(_mnuSmoothZoom);
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\FullScreen", "Full &screen"), Keys.Control | Keys.F, delegate { OpenFullScreen(false); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\FullScreen2", "Full screen &two"), Keys.Control | Keys.G, delegate { OpenFullScreen(true); }));
            mImage.DropDownItems.Add(_mnuScrollNew);
            mImage.DropDownItems.Add(new ToolStripSeparator());
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\Crop", "&Crop"), Keys.Alt | Keys.C, delegate { ActiveVpAction(vp => vp.CropToCurrentZoomOrSelection()); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\KeepSelection", "&Keep selection"), Keys.None, delegate { ActiveVpAction(vp => vp.KeepSelectionCrop()); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\Uncrop", "&Uncrop"), Keys.None, delegate { ActiveVpAction(vp => vp.UncropActiveStream()); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\Autocrop", "&Autocrop"), Keys.None, delegate { ActiveVpAction(vp => vp.AutocropActiveStream(35, 35)); }));
            mImage.DropDownItems.Add(MkMenu("Crop to &results...", Keys.None, delegate { OpenCropToResultsDialog(); }));
            mImage.DropDownItems.Add(MkMenu("Mark start frame", Keys.None, delegate { MarkFrame(true); }));
            mImage.DropDownItems.Add(MkMenu("Mark end frame", Keys.None, delegate { MarkFrame(false); }));
            mImage.DropDownItems.Add(MkMenu("Clear frame selection", Keys.None, delegate { ClearMarkedFrames(); }));
            mImage.DropDownItems.Add(new ToolStripSeparator());
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\FindObject", "Find next &object"), Keys.None, delegate { if (ActiveEventControl != null) ActiveEventControl.FindObjectInDirection(ActiveEventControl.ActiveCameraIndex, 1); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\FindTime", "Find &time..."), Keys.None, delegate { FindTimeDialogFlow(); }));

            ToolStripMenuItem mPrec = new ToolStripMenuItem(Loc.Menu("Image\\Precision\\Title", "Precision"));
            string[] precNames = new string[]
            {
                Loc.Menu("Image\\Precision\\Seconds", "Seconds"),
                Loc.Menu("Image\\Precision\\Fifths", "Fifths"),
                Loc.Menu("Image\\Precision\\Tenths", "Tenths"),
                Loc.Menu("Image\\Precision\\Hundredths", "Hundredths"),
                Loc.Menu("Image\\Precision\\Thousandths", "Thousandths"),
                Loc.Menu("Image\\Precision\\TenThousandths", "TenThousandths")
            };
            TimePrecision[] precVals = new TimePrecision[] { TimePrecision.Seconds, TimePrecision.Fifths, TimePrecision.Tenths, TimePrecision.Hundredths, TimePrecision.Thousandths, TimePrecision.TenThousandths };
            for (int pi = 0; pi < precVals.Length; pi++)
            {
                TimePrecision cap = precVals[pi];
                mPrec.DropDownItems.Add(precNames[pi], null, delegate { SetEventPrecision(cap); });
            }
            mImage.DropDownItems.Add(mPrec);
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\Adjustments", "A&djustments..."), Keys.None, delegate { OpenImageAdjustmentsFloater(); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\Enhance", "&Enhance"), Keys.None, delegate { ActiveVpAction(vp => vp.ToggleEnhanceAtActiveBand()); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\Unenhance", "U&nenhance"), Keys.None, delegate { ActiveVpAction(vp => vp.ClearEnhance()); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\WhiteBalance", "White &balance (Sample Point)"), Keys.Alt | Keys.B, delegate { ActiveVpAction(vp => vp.SampleWhiteBalanceAtCursor()); }));
            mImage.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem mOv = new ToolStripMenuItem(Loc.Menu("Image\\Overlay\\Title", "Over&lay"));
            mOv.DropDownItems.Add(Loc.Menu("Image\\Overlay\\Text", "Text..."), null, delegate { AddTextOverlayFlow(); });
            mOv.DropDownItems.Add(Loc.Menu("Image\\Overlay\\Time", "Closest time"), null, delegate { AddQuickTimeOverlay(ImageOverlayKind.ClosestTime); });
            mOv.DropDownItems.Add("Hash time", null, delegate { AddQuickTimeOverlay(ImageOverlayKind.HashTime); });
            mOv.DropDownItems.Add(Loc.Menu("Image\\Overlay\\Lanes", "Line labels..."), null, delegate { AddLineLabelOverlayFlow(); });
            _mnuShowEvOverlay = MkMenu(Loc.Menu("Image\\Overlay\\NameAlways", "Show event name"), Keys.None, delegate { ToggleEventNameOverlay(); });
            mOv.DropDownItems.Add(_mnuShowEvOverlay);
            mImage.DropDownItems.Add(mOv);
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\RemoveOverlays", "&Remove overlays"), Keys.Control | Keys.R, delegate { RemoveAllOverlays(); }));
            _mnuRetention = MkMenu("Retain overlays", Keys.None, delegate { ToggleRetainOverlays(); });
            mImage.DropDownItems.Add(_mnuRetention);
            mImage.DropDownItems.Add(new ToolStripSeparator());
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\ExportBitmap", "Export Annotated Image..."), Keys.None, delegate { ExportAnnotatedImageDialog(); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\Options", "Op&tions..."), Keys.None, delegate { OpenOptions(2); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\PageSetup", "Pa&ge setup..."), Keys.None, delegate { OpenPageSetupDialog(); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\Print", "&Print..."), Keys.None, delegate { PrintAnnotatedImagePreview(); }));
            mImage.DropDownItems.Add(MkMenu(Loc.Menu("Image\\PrintToBitmap", "Print to bitmap..."), Keys.None, delegate { ExportAnnotatedImageDialog(); }));

            // 5. RESULTS
            ToolStripMenuItem mResults = new ToolStripMenuItem(Loc.Menu("Results\\Title", "&Results"));
            mResults.DropDownItems.Add(MkMenu(Loc.Menu("Results\\NewResult", "&New result"), Keys.None, delegate { if (ActiveEventControl != null) ActiveEventControl.AddNewResultRow(); }));
            mResults.DropDownItems.Add(MkMenu(Loc.Menu("Results\\DeleteResult", "&Delete result"), Keys.None, delegate { if (ActiveEventControl != null) ActiveEventControl.DeleteSelectedResultRow(); }));
            mResults.DropDownItems.Add(MkMenu("&Edit result", Keys.F2, delegate { if (ActiveEventControl != null) ActiveEventControl.EditSelectedResultCell(); }));
            mResults.DropDownItems.Add(MkMenu(Loc.Menu("Results\\Find", "Find &id..."), Keys.None, delegate { OpenFindIdDialog(); }));
            mResults.DropDownItems.Add(MkMenu("Previous image", Keys.Shift | Keys.Up, delegate { if (ActiveEventControl != null) ActiveEventControl.NavigateResultSelection(-1); }));
            mResults.DropDownItems.Add(MkMenu("Next image", Keys.Shift | Keys.Down, delegate { if (ActiveEventControl != null) ActiveEventControl.NavigateResultSelection(1); }));
            mResults.DropDownItems.Add(new ToolStripSeparator());
            mResults.DropDownItems.Add(MkMenu("&Auto-Evaluate Motion Peaks", Keys.None, delegate { AutoEvaluateActiveStrip(); }));
            mResults.DropDownItems.Add(MkMenu(Loc.Menu("Results\\Filters", "&Filters..."), Keys.None, delegate { OpenResultsFilterDialog(); }));
            mResults.DropDownItems.Add(MkMenu(Loc.Menu("Results\\AutoPackLeaders", "Set pack &leaders..."), Keys.None, delegate { SetPackLeadersFlow(); }));
            mResults.DropDownItems.Add(new ToolStripSeparator());
            mResults.DropDownItems.Add(MkMenu(Loc.Menu("Results\\Options", "Op&tions..."), Keys.None, delegate { OpenOptions(3); }));
            mResults.DropDownItems.Add(MkMenu(Loc.Menu("Results\\ImportReacTime", "Import ReacTime data..."), Keys.None, delegate { ImportReacTimeFlow(); }));
            mResults.DropDownItems.Add(MkMenu(Loc.Menu("Results\\PageSetup", "Pa&ge setup..."), Keys.None, delegate { OpenPageSetupDialog(); }));
            mResults.DropDownItems.Add(MkMenu(Loc.Menu("Results\\Print", "&Print results..."), Keys.None, delegate { PrintAnnotatedImagePreview(); }));

            // 6. SCOREBOARD
            ToolStripMenuItem mScb = new ToolStripMenuItem(Loc.Menu("Scoreboard\\Title", "&Scoreboard"));
            mScb.DropDownItems.Add(MkMenu(Loc.Menu("Scoreboard\\Initialize", "&Initialize"), Keys.Alt | Keys.I, delegate { SetScoreboardState(true, false, false); }));
            mScb.DropDownItems.Add(MkMenu(Loc.Menu("Scoreboard\\Run", "&Run"), Keys.Alt | Keys.R, delegate { SetScoreboardState(true, true, false); }));
            mScb.DropDownItems.Add(MkMenu(Loc.Menu("Scoreboard\\Pause", "&Pause"), Keys.Alt | Keys.P, delegate { PauseRunningClock(); }));
            mScb.DropDownItems.Add(MkMenu(Loc.Menu("Scoreboard\\Finish", "&Finish"), Keys.Alt | Keys.F, delegate { FinishRunningClock(); }));
            mScb.DropDownItems.Add(MkMenu(Loc.Menu("Scoreboard\\Stop", "&Stop"), Keys.Alt | Keys.S, delegate { StopRunningClock(); }));
            mScb.DropDownItems.Add(MkMenu("&Hold / Display results", Keys.Alt | Keys.H, delegate { ToggleScoreboardHold(); }));
            mScb.DropDownItems.Add(MkMenu(Loc.Menu("Scoreboard\\RefreshAll", "R&efresh all / Broadcast"), Keys.Alt | Keys.E, delegate { TransmitScoreboardNow(); }));
            mScb.DropDownItems.Add(new ToolStripSeparator());
            _mnuScbTod = MkMenu(Loc.Menu("Scoreboard\\TimeOfDay", "Show time of day"), Keys.None, delegate { _clockShowTod = !_clockShowTod; if (_mnuScbTod != null) _mnuScbTod.Checked = _clockShowTod; UpdateClockText(); });
            _mnuScb24h = MkMenu(Loc.Menu("Scoreboard\\TimeOfDay24Hour", "Use 24 hour format"), Keys.None, delegate { _mnuScb24h.Checked = !_mnuScb24h.Checked; UpdateClockText(); });
            _mnuScb24h.Checked = true;
            _mnuScbStopTime = MkMenu("Stop on-screen time", Keys.None, delegate { StopRunningClock(); });
            mScb.DropDownItems.Add(_mnuScbTod);
            mScb.DropDownItems.Add(_mnuScb24h);
            mScb.DropDownItems.Add(_mnuScbStopTime);
            mScb.DropDownItems.Add(new ToolStripSeparator());
            mScb.DropDownItems.Add(MkMenu("Edit &message...", Keys.None, delegate { EditScoreboardMessageFlow(); }));
            mScb.DropDownItems.Add(MkMenu("Send message", Keys.None, delegate { TransmitScoreboardNow(); }));
            mScb.DropDownItems.Add(MkMenu(Loc.Menu("Scoreboard\\Options", "Op&tions..."), Keys.None, delegate { OpenOptions(5); }));

            // 7. LAPTIME
            ToolStripMenuItem mLap = new ToolStripMenuItem(Loc.Menu("Results\\Splits\\Title", "&LapTime"));
            mLap.DropDownItems.Add(MkMenu(Loc.Menu("Results\\AcceptSplit", "&Accept split time"), Keys.None, delegate { if (ActiveEventControl != null) ActiveEventControl.AcceptSelectedSplit(); }));
            mLap.DropDownItems.Add(MkMenu(Loc.Menu("Results\\AddSplit", "&Insert split time"), Keys.None, delegate { if (ActiveEventControl != null) ActiveEventControl.InsertSplitForSelected(); }));
            mLap.DropDownItems.Add(MkMenu(Loc.Menu("Results\\DeleteSplit", "&Mask split time"), Keys.None, delegate { if (ActiveEventControl != null) ActiveEventControl.MaskSelectedSplit(); }));
            mLap.DropDownItems.Add(MkMenu(Loc.Menu("Results\\SplitDialog", "&Edit split times..."), Keys.None, delegate { OpenLapTimeSplitsDialog(); }));
            mLap.DropDownItems.Add(new ToolStripSeparator());
            mLap.DropDownItems.Add(MkMenu("&Use current event", Keys.Alt | Keys.L, delegate { SetHelp("LapTime linked to active event: " + (ActiveDocument != null ? ActiveDocument.EventName : "")); }));
            mLap.DropDownItems.Add(MkMenu("Export &split times CSV...", Keys.None, delegate { ExportSplitCsvDialog(); }));
            mLap.DropDownItems.Add(MkMenu("Op&tions...", Keys.None, delegate { OpenOptions(7); }));

            // 8. WINDOW
            _mnuWindow = new ToolStripMenuItem(Loc.Menu("Window\\Title", "&Window"));
            _mnuWindow.DropDownOpening += delegate { RebuildWindowMenu(); };

            // 9. HELP
            ToolStripMenuItem mHelp = new ToolStripMenuItem(Loc.Menu("Help\\Title", "&Help"));
            mHelp.DropDownItems.Add(MkMenu("&Keyboard Shortcuts...", Keys.F1, delegate { ShowShortcutsDialog(); }));
            mHelp.DropDownItems.Add(new ToolStripSeparator());
            mHelp.DropDownItems.Add(MkMenu(Loc.Menu("Help\\FindCameras", "&Find cameras..."), Keys.None, delegate { using (FindCamerasDialog d = new FindCamerasDialog(ActiveDocument)) d.ShowDialog(this); }));
            mHelp.DropDownItems.Add(MkMenu(Loc.Menu("Help\\SystemInfo", "&System info..."), Keys.None, delegate { using (SystemInfoDialog d = new SystemInfoDialog(ActiveDocument)) d.ShowDialog(this); }));
            mHelp.DropDownItems.Add(MkMenu(Loc.Menu("Help\\About", "&About OptiFinish..."), Keys.None, delegate { using (AboutFinishLynxDialog d = new AboutFinishLynxDialog()) d.ShowDialog(this); }));

            ms.Items.AddRange(new ToolStripItem[] { mFile, mEdit, mEvent, mImage, mResults, mScb, mLap, _mnuWindow, mHelp });

            // Classic FinishLynx MDI child window buttons on far right of menu bar (- [] X)
            ToolStripMenuItem mWinClose = new ToolStripMenuItem("x") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Close Active Event Window (Ctrl+W)" };
            mWinClose.Click += delegate { CloseActiveMdiEvent(); };
            ToolStripMenuItem mWinToggleTile = new ToolStripMenuItem("[]") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Restore / Maximize Child Windows" };
            mWinToggleTile.Click += delegate { ApplyWorkspaceArrange(_workspaceArrangeMode == 0 ? 1 : 0); };
            ToolStripMenuItem mWinMin = new ToolStripMenuItem("_") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Switch to Hardware Control (Alt+1)" };
            mWinMin.Click += delegate { ShowHardwareControlView(); };
            ms.Items.Add(mWinClose);
            ms.Items.Add(mWinToggleTile);
            ms.Items.Add(mWinMin);

            MainMenuStrip = ms;
            Controls.Add(ms);
            ms.SendToBack();
        }

        private void ApplyConfigToHardware()
        {
            LynxConfigService cfg = LynxConfigService.Instance;
            _hardware.SerialPortName = cfg.OutputSerialPort;
            _hardware.BaudRate = cfg.OutputBaudRate;
            string err;
            if (!string.IsNullOrEmpty(cfg.OutputSerialPort) && !cfg.OutputSerialPort.StartsWith("<") && !cfg.OutputSerialPort.StartsWith("UDP:"))
            {
                _hardware.TryOpenSerial(cfg.OutputSerialPort, cfg.OutputBaudRate, out err);
            }
        }

        private ToolStripMenuItem MkMenu(string text, Keys shortcut, Action act)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            if (shortcut != Keys.None)
            {
                try { mi.ShortcutKeys = shortcut; }
                catch { mi.ShortcutKeyDisplayString = shortcut.ToString(); }
            }
            if (act != null) mi.Click += delegate { act(); };
            return mi;
        }

        private void RebuildWindowMenu()
        {
            _mnuWindow.DropDownItems.Clear();
            _mnuWindow.DropDownItems.Add(Loc.Menu("Window\\Cascade", "&Cascade"), null, delegate { ApplyWorkspaceArrange(1); });
            _mnuWindow.DropDownItems.Add(Loc.Menu("Window\\TileHorizontal", "Tile &horizontal"), null, delegate { ApplyWorkspaceArrange(2); });
            _mnuWindow.DropDownItems.Add(Loc.Menu("Window\\TileVertical", "Tile &vertical"), null, delegate { ApplyWorkspaceArrange(3); });
            _mnuWindow.DropDownItems.Add("&Maximize active", null, delegate { ApplyWorkspaceArrange(0); });
            _mnuWindow.DropDownItems.Add(new ToolStripSeparator());
            _mnuWindow.DropDownItems.Add(Loc.Menu("Window\\Keypad", "&Keypad"), null, delegate { OpenKeypadFloater(); });
            _mnuWindow.DropDownItems.Add(Loc.Menu("Window\\ImageAdjustments", "&Image adjustments"), null, delegate { OpenImageAdjustmentsFloater(); });
            _mnuWindow.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem mHwc = new ToolStripMenuItem(Loc.Hw("Title", "Hardware Control")) { ShortcutKeys = Keys.Alt | Keys.D1 };
            mHwc.Checked = (_activeView == _hwcPanel);
            mHwc.Click += delegate { ShowHardwareControlView(); };
            _mnuWindow.DropDownItems.Add(mHwc);

            for (int i = 0; i < _eventControls.Count; i++)
            {
                EventWindowControl ew = _eventControls[i];
                if (ew.IsDisposed) continue;
                ToolStripMenuItem mEv = new ToolStripMenuItem((i + 1) + "  " + FormatEventTabTitle(ew.Document));
                if (i == 0) mEv.ShortcutKeys = Keys.Alt | Keys.D2;
                else if (i == 1) mEv.ShortcutKeys = Keys.Alt | Keys.D3;
                mEv.Checked = (_activeView == ew);
                mEv.Click += delegate { ActivateWorkspaceView(ew); };
                _mnuWindow.DropDownItems.Add(mEv);
            }
        }

        // ===================== TOP MASTER TOOLBAR =====================
        private void BuildTopMasterToolbar()
        {
            _topToolBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = SystemColors.Control,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(2, 3, 2, 2)
            };

            int x = 3;
            _topToolBar.Controls.Add(MkTopBtn("NewEvent", x, 31, "New Event (Ctrl+N)", delegate { CreateNewBlankEvent(); })); x += 34;
            _topToolBar.Controls.Add(MkTopBtn("PrevEventBig", x, 31, "Previous Event in Schedule", delegate { NavigateSchedule(-1); })); x += 34;
            _topToolBar.Controls.Add(MkTopBtn("ChooseEventBig", x, 31, "Goto / Choose Scheduled Event...", delegate { OpenGotoEventDialog(); })); x += 34;
            _topToolBar.Controls.Add(MkTopBtn("NextEventBig", x, 31, "Next Event in Schedule", delegate { NavigateSchedule(1); })); x += 34;
            _topToolBar.Controls.Add(MkTopBtn("ManualStart", x, 31, "Manual Start Signal...", delegate { OpenManualStartDialog(); })); x += 35;

            _cbStarts = new ComboBox
            {
                Location = new Point(x, 8),
                Width = 128,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cbStarts.Items.Add("(no starts)");
            _cbStarts.SelectedIndex = 0;
            _topToolBar.Controls.Add(_cbStarts);
            x += 133;

            _topToolBar.Controls.Add(MkTopBtn("ApplyStart", x, 31, "Apply Start to Selected Event (Ctrl+T)", delegate { ApplySelectedStartToEvent(); })); x += 34;

            _btnArmTop = MkTopBtn("Arm", x, 31, "Arm Active Event (Ctrl+A)", delegate { ToggleArmState(); });
            _topToolBar.Controls.Add(_btnArmTop);
            x += 34;

            _cbOpenEvents = new ComboBox
            {
                Location = new Point(x, 8),
                Width = 280,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cbOpenEvents.SelectedIndexChanged += delegate
            {
                if (_suppressEventCombo) return;
                int idx = _cbOpenEvents.SelectedIndex;
                if (idx >= 0 && idx < _eventControls.Count && !_eventControls[idx].IsDisposed)
                {
                    ActivateWorkspaceView(_eventControls[idx]);
                }
            };
            _topToolBar.Controls.Add(_cbOpenEvents);
            x += 285;

            _btnCaptureTop = MkTopBtn("InactiveCapture", x, 31, "Toggle Capture (Alt+T)", delegate { ToggleCaptureState(); });
            _topToolBar.Controls.Add(_btnCaptureTop);
            x += 35;

            _txtRunTime = new TextBox
            {
                Location = new Point(x, 7),
                Size = new Size(76, 24),
                Text = "0.0",
                ReadOnly = true,
                TextAlign = HorizontalAlignment.Right,
                BackColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                Font = new Font("Consolas", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _txtRunTime.Click += delegate { _clockShowTod = !_clockShowTod; UpdateClockText(); };
            _topToolBar.Controls.Add(_txtRunTime);
            x += 82;

            _lblRunStatus = new Label
            {
                Location = new Point(x, 11),
                AutoSize = true,
                Text = "Ready",
                ForeColor = Color.FromArgb(40, 40, 40),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            _topToolBar.Controls.Add(_lblRunStatus);

            // Right-aligned compact OptiFinish quick helpers (Presets + AutoEval + COM status)
            FlowLayoutPanel rightQuick = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0, 5, 4, 0),
                BackColor = Color.Transparent
            };

            _cbQuickPresets = new ComboBox
            {
                Width = 175,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(2, 2, 4, 0)
            };
            _cbQuickPresets.Items.AddRange(new object[]
            {
                "Preset: 100m Hurdles Women",
                "Preset: 100m Men Dead Heat",
                "Preset: 1500m Distance Run"
            });
            string sDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleData");
            List<string> evns = MeetDatabaseService.DiscoverNativeLynxEventFiles(sDir);
            for (int i = 0; i < evns.Count; i++)
            {
                _cbQuickPresets.Items.Add("Native: " + Path.GetFileName(evns[i]));
            }
            if (_cbQuickPresets.Items.Count > 0) _cbQuickPresets.SelectedIndex = evns.Count > 0 ? 3 : 0;
            _cbQuickPresets.SelectedIndexChanged += delegate
            {
                if (_suppressEventCombo) return;
                int sel = _cbQuickPresets.SelectedIndex;
                if (sel < 3) LoadPreset(sel);
                else if (sel - 3 < evns.Count) OpenEventFilePath(evns[sel - 3]);
            };

            Button btnAutoEval = new Button
            {
                Text = "Auto-Eval",
                Size = new Size(74, 24),
                Margin = new Padding(2, 1, 4, 0)
            };
            btnAutoEval.Click += delegate { AutoEvaluateActiveStrip(); };

            _lblSerialStatus = new Label
            {
                Text = _hardware.IsSerialConnected ? ("COM: " + _hardware.SerialPortName) : "UDP Ready",
                AutoSize = true,
                Padding = new Padding(4, 5, 4, 0),
                ForeColor = Color.DarkGreen,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _lblSerialStatus.Click += delegate { OpenOptions(5); };

            rightQuick.Controls.Add(_cbQuickPresets);
            rightQuick.Controls.Add(btnAutoEval);
            rightQuick.Controls.Add(_lblSerialStatus);
            _topToolBar.Controls.Add(rightQuick);

            Controls.Add(_topToolBar);
        }

        private LynxIconButton MkTopBtn(string key, int x, int size, string hint, Action onClick)
        {
            LynxIconButton b = new LynxIconButton(key, size, size, hint);
            b.Location = new Point(x, 4);
            b.StatusHintHover += SetHelp;
            b.Click += delegate { if (onClick != null) onClick(); };
            return b;
        }

        // ===================== BOTTOM DUAL STATUS BAR =====================
        private void BuildBottomStatusBar()
        {
            _statusBar = new StatusStrip
            {
                Dock = DockStyle.Bottom,
                SizingGrip = true,
                RenderMode = ToolStripRenderMode.System,
                BackColor = SystemColors.Control
            };

            _lblHelpZone = new ToolStripStatusLabel
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Ready. Left-click/drag hairline to inspect, Double-click/Enter to evaluate, Shift+Arrow to adjust results."
            };
            _pbBuffer = new ProgressBar { Size = new Size(135, 16), Minimum = 0, Maximum = 100, Value = 100 };
            _bufferHost = new ToolStripControlHost(_pbBuffer);
            _lblBufferText = new ToolStripStatusLabel { Text = "100% | 100%", AutoSize = true };

            _statusBar.Items.Add(_lblHelpZone);
            _statusBar.Items.Add(_bufferHost);
            _statusBar.Items.Add(_lblBufferText);
            Controls.Add(_statusBar);
        }

        private void SetHelp(string msg)
        {
            if (_lblHelpZone == null) return;
            _lblHelpZone.Text = string.IsNullOrEmpty(msg)
                ? "OptiFinish Ready  |  Alt+1 Hardware Control  |  Alt+2 Event Window"
                : msg;
        }

        // ===================== WORKSPACE VIEW MANAGEMENT (MAXIMIZED / CASCADE / TILE) =====================
        private void EnsureHardwareControlPanel()
        {
            if (_hwcPanel == null || _hwcPanel.IsDisposed)
            {
                _hwcPanel = new HardwareControlPanel { Dock = DockStyle.Fill };
                _hwcPanel.SetDocument(ActiveDocument);
                _hwcPanel.StatusHintRequested += SetHelp;
                _hwcPanel.CameraSettingsChanged += delegate
                {
                    if (ActiveEventControl != null) ActiveEventControl.RefreshAll();
                };
            }
        }

        public void ShowHardwareControlView()
        {
            EnsureHardwareControlPanel();
            _hwcPanel.SetDocument(ActiveDocument);
            ActivateWorkspaceView(_hwcPanel);
        }

        private void ActivateWorkspaceView(Control view)
        {
            if (view == null) return;
            _activeView = view;
            _workspaceArrangeMode = 0;
            RebuildWorkspaceLayout();
            UpdateTitleAndStatus();
        }

        private void ApplyWorkspaceArrange(int mode)
        {
            _workspaceArrangeMode = mode;
            RebuildWorkspaceLayout();
        }

        private void RebuildWorkspaceLayout()
        {
            _workspaceHost.SuspendLayout();
            try
            {
                _workspaceHost.Controls.Clear();
                if (_workspaceArrangeMode == 0)
                {
                    Control target = _activeView ?? (_eventControls.Count > 0 ? (Control)_eventControls[0] : _hwcPanel);
                    if (target != null)
                    {
                        target.Dock = DockStyle.Fill;
                        _workspaceHost.Controls.Add(target);
                    }
                }
                else
                {
                    List<Control> views = new List<Control>();
                    EnsureHardwareControlPanel();
                    views.Add(_hwcPanel);
                    for (int i = 0; i < _eventControls.Count; i++) views.Add(_eventControls[i]);

                    int n = views.Count;
                    int w = Math.Max(300, _workspaceHost.ClientSize.Width);
                    int h = Math.Max(200, _workspaceHost.ClientSize.Height);

                    for (int i = 0; i < n; i++)
                    {
                        Control v = views[i];
                        string title = ReferenceEquals(v, _hwcPanel) ? "Hardware Control" : FormatEventTabTitle(((EventWindowControl)v).Document);
                        Panel frame = new Panel
                        {
                            BorderStyle = BorderStyle.FixedSingle,
                            BackColor = SystemColors.Control
                        };
                        if (_workspaceArrangeMode == 1) // Cascade
                        {
                            frame.SetBounds(16 * i, 16 * i, Math.Max(360, w - 36 * Math.Max(1, n)), Math.Max(260, h - 36 * Math.Max(1, n)));
                        }
                        else if (_workspaceArrangeMode == 2) // Tile Horizontal
                        {
                            int rh = h / n;
                            frame.SetBounds(0, i * rh, w, i == n - 1 ? (h - i * rh) : rh);
                        }
                        else // Tile Vertical
                        {
                            int cw = w / n;
                            frame.SetBounds(i * cw, 0, i == n - 1 ? (w - i * cw) : cw, h);
                        }

                        Panel cap = new Panel { Dock = DockStyle.Top, Height = 20, BackColor = ReferenceEquals(v, _activeView) ? Color.FromArgb(10, 36, 106) : SystemColors.InactiveCaption };
                        Label capLbl = new Label
                        {
                            Text = title,
                            Dock = DockStyle.Fill,
                            ForeColor = ReferenceEquals(v, _activeView) ? Color.White : SystemColors.InactiveCaptionText,
                            TextAlign = ContentAlignment.MiddleLeft,
                            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                            Padding = new Padding(4, 0, 0, 0)
                        };
                        Control captured = v;
                        capLbl.DoubleClick += delegate { ActivateWorkspaceView(captured); };
                        cap.Controls.Add(capLbl);

                        v.Dock = DockStyle.Fill;
                        frame.Controls.Add(v);
                        frame.Controls.Add(cap);
                        _workspaceHost.Controls.Add(frame);
                        frame.BringToFront();
                    }
                }
            }
            finally
            {
                _workspaceHost.ResumeLayout(true);
            }
        }

        private void OpenEventMdiWindow(RaceEventDocument doc, bool maximize)
        {
            if (doc == null) return;
            if (!_openEvents.Contains(doc)) _openEvents.Add(doc);

            EventWindowControl ew = new EventWindowControl { Dock = DockStyle.Fill };
            ew.LoadDocument(doc);
            ew.StatusHintChanged += SetHelp;
            ew.DocumentModified += delegate { SyncTopLists(); UpdateTitleAndStatus(); };
            ew.OpenCameraSettingsRequested += delegate { OpenSelectedCameraSettings(); };
            ew.OpenImageAdjustmentsRequested += delegate { OpenImageAdjustmentsFloater(); };
            ew.OpenFiltersRequested += delegate { OpenResultsFilterDialog(); };
            ew.OpenPackLeadersRequested += delegate { SetPackLeadersFlow(); };
            ew.OpenFindIdRequested += delegate { OpenFindIdDialog(); };
            ew.OpenGotoScheduleRequested += delegate { OpenGotoEventDialog(); };
            ew.PrevScheduleRequested += delegate { NavigateSchedule(-1); };
            ew.NextScheduleRequested += delegate { NavigateSchedule(1); };
            ew.ImportReacTimeRequested += delegate { ImportReacTimeFlow(); };
            ew.PrintResultsRequested += delegate { PrintAnnotatedImagePreview(); };
            ew.PrintImageRequested += delegate { PrintAnnotatedImagePreview(); };
            ew.PrintBitmapRequested += delegate { ExportAnnotatedImageDialog(); };
            ew.ManualWindRequested += delegate { OpenWindDialog(); };
            ew.ManualStartRequested += delegate { OpenManualStartDialog(); };
            ew.ArmToggleRequested += delegate { ToggleArmState(); };
            ew.CaptureToggleRequested += delegate { ToggleCaptureState(); };

            _eventControls.Add(ew);
            if (_hwcPanel != null) _hwcPanel.SetDocument(doc);
            ActivateWorkspaceView(ew);
            SyncTopLists();
        }

        private string FormatEventTabTitle(RaceEventDocument doc)
        {
            if (doc == null) return "Event";
            return LynxConfigService.Instance.DefaultEventNameMode == 1
                ? doc.GetEventCodeString()
                : doc.EventName;
        }

        private void ReplaceActiveOrOpenEvent(RaceEventDocument doc)
        {
            EventWindowControl cur = ActiveEventControl;
            if (cur != null && (LynxExtendedConfig.Instance.SingleEventMode || cur.Document == null))
            {
                RaceEventDocument old = cur.Document;
                int idx = _openEvents.IndexOf(old);
                if (idx >= 0) _openEvents[idx] = doc;
                else _openEvents.Add(doc);
                cur.LoadDocument(doc);
                if (old != null && !ReferenceEquals(old, doc)) old.Dispose();
                if (_hwcPanel != null) _hwcPanel.SetDocument(doc);
                ActivateWorkspaceView(cur);
                SyncTopLists();
            }
            else
            {
                OpenEventMdiWindow(doc, true);
            }
        }

        private void CloseActiveMdiEvent()
        {
            EventWindowControl cur = _activeView as EventWindowControl;
            if (cur != null)
            {
                RaceEventDocument doc = cur.Document;
                _eventControls.Remove(cur);
                cur.Dispose();
                if (doc != null)
                {
                    _openEvents.Remove(doc);
                    doc.Dispose();
                }
                _activeView = _eventControls.Count > 0 ? (Control)_eventControls[_eventControls.Count - 1] : _hwcPanel;
                RebuildWorkspaceLayout();
                SyncTopLists();
                UpdateTitleAndStatus();
            }
        }

        private void SyncTopLists()
        {
            _suppressEventCombo = true;
            try
            {
                if (_cbOpenEvents != null)
                {
                    _cbOpenEvents.Items.Clear();
                    if (_openEvents.Count == 0)
                    {
                        _cbOpenEvents.Items.Add("(no capturable events)");
                        _cbOpenEvents.SelectedIndex = 0;
                    }
                    else
                    {
                        for (int i = 0; i < _openEvents.Count; i++)
                        {
                            RaceEventDocument d = _openEvents[i];
                            string flags = string.Format(CultureInfo.InvariantCulture, "[{0}{1}s{2}] {3}",
                                d.IsArmed ? "a" : "-",
                                d.IsCapturing ? "c" : "-",
                                d.Competitors.Exists(c => c.Splits.Count > 0) ? "l" : "-",
                                d.EventName);
                            _cbOpenEvents.Items.Add(flags);
                        }
                        int sel = _openEvents.IndexOf(ActiveDocument);
                        _cbOpenEvents.SelectedIndex = sel >= 0 ? sel : 0;
                    }
                }

                RaceEventDocument active = ActiveDocument;
                if (_cbStarts != null)
                {
                    _cbStarts.Items.Clear();
                    if (active == null || active.RecordedStarts.Count == 0)
                    {
                        _cbStarts.Items.Add("(no starts)");
                        _cbStarts.SelectedIndex = 0;
                    }
                    else
                    {
                        for (int i = 0; i < active.RecordedStarts.Count; i++) _cbStarts.Items.Add(active.RecordedStarts[i].ToString());
                        _cbStarts.SelectedIndex = Math.Max(0, Math.Min(active.RecordedStarts.Count - 1, active.SelectedStartIndex));
                    }
                }
            }
            finally
            {
                _suppressEventCombo = false;
            }
        }

        private void UpdateTitleAndStatus()
        {
            RaceEventDocument doc = ActiveDocument;
            string sub = ReferenceEquals(_activeView, _hwcPanel)
                ? "Hardware Control"
                : (doc != null ? FormatEventTabTitle(doc) : "Workspace");
            Text = "OptiFinish - [" + sub + "]";

            if (_btnArmTop != null) _btnArmTop.Checked = (doc != null && doc.IsArmed);
            if (_btnCaptureTop != null)
            {
                if (doc != null && doc.IsCapturing)
                    _btnCaptureTop.IconKey = (_runClockRunning ? "Capturing" : "ActiveCapture");
                else
                    _btnCaptureTop.IconKey = "InactiveCapture";
            }
            if (_lblRunStatus != null)
            {
                _lblRunStatus.Text = _runClockRunning ? (_runClockPaused ? "Paused" : "Running")
                                   : (doc != null && doc.IsArmed ? "Armed" : "Ready");
            }
            if (_mnuRetention != null && doc != null) _mnuRetention.Checked = doc.RetainOverlays;
            if (_mnuShowEvOverlay != null && doc != null) _mnuShowEvOverlay.Checked = doc.ShowEventNameOverlay;
            if (_mnuSmoothZoom != null && doc != null) _mnuSmoothZoom.Checked = doc.SmoothZoom;
        }

        // ===================== CORE COMMAND HANDLERS =====================
        private void CreateNewBlankEvent()
        {
            RaceEventDocument d = new RaceEventDocument
            {
                EventNumber = _openEvents.Count + 1,
                EventName = "Event " + (_openEvents.Count + 1)
            };
            for (int i = 1; i <= 8; i++) d.Competitors.Add(new CompetitorEntry { Lane = i, BibId = (100 + i).ToString(CultureInfo.InvariantCulture) });
            SlitScanEngine.BuildSynthesizedCameraStrips(d);
            OpenEventMdiWindow(d, true);
        }

        private void OpenEventDialog()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Event Files (*.evn;*.optr;*.lif;*.csv)|*.evn;*.optr;*.lif;*.csv|All Files|*.*";
                string sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleData");
                if (Directory.Exists(sample)) ofd.InitialDirectory = sample;
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    OpenEventFilePath(ofd.FileName);
                }
            }
        }

        private void OpenRecentDialogFlow()
        {
            string sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleData");
            using (OpenRecentDialog d = new OpenRecentDialog(LynxConfigService.Instance.EventDir, sample))
            {
                if (d.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(d.SelectedFilePath))
                {
                    OpenEventFilePath(d.SelectedFilePath);
                }
            }
        }

        private void OpenEventFilePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                SetHelp("Loading " + Path.GetFileName(path) + "...");
                string ext = Path.GetExtension(path).ToLowerInvariant();
                RaceEventDocument doc = (ext == ".evn")
                    ? MeetDatabaseService.LoadNativeLynxEventFile(path)
                    : MeetDatabaseService.LoadResultsFile(path);
                if (doc != null)
                {
                    if (doc.Cameras.Count == 0) SlitScanEngine.BuildSynthesizedCameraStrips(doc);
                    ReplaceActiveOrOpenEvent(doc);
                    SetHelp("Loaded: " + path);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to open event:\n" + ex.Message, "Open Event", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadPreset(int idx)
        {
            RaceEventDocument d = idx == 1 ? MeetDatabaseService.CreateSample100mMenDeadHeat()
                                : (idx == 2 ? MeetDatabaseService.CreateSample1500mDistanceRun() : MeetDatabaseService.CreateSample100mHurdlesWomen());
            ReplaceActiveOrOpenEvent(d);
            SetHelp("Loaded Preset: " + d.EventName);
        }

        private void SaveActiveEvent(bool promptPath)
        {
            RaceEventDocument doc = ActiveDocument;
            if (doc == null) return;
            string path = doc.SourceFilePath;
            if (promptPath || string.IsNullOrEmpty(path) || path.EndsWith(".evn", StringComparison.OrdinalIgnoreCase))
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "OptiFinish Race Package (*.optr)|*.optr|LIF Official Results (*.lif)|*.lif";
                    sfd.FileName = doc.GetEventCodeString() + ".optr";
                    if (sfd.ShowDialog(this) != DialogResult.OK) return;
                    path = sfd.FileName;
                }
            }
            if (path.EndsWith(".lif", StringComparison.OrdinalIgnoreCase))
            {
                MeetDatabaseService.ExportResultsLifOrCsv(doc, path);
            }
            else
            {
                MeetDatabaseService.SaveNativeRacePackage(doc, path);
                doc.SourceFilePath = path;
            }
            SetHelp("Saved: " + path);
        }

        private void ReloadActiveEvent()
        {
            if (ActiveDocument != null && !string.IsNullOrEmpty(ActiveDocument.SourceFilePath) && File.Exists(ActiveDocument.SourceFilePath))
            {
                OpenEventFilePath(ActiveDocument.SourceFilePath);
            }
        }

        private void ExportLifDialog()
        {
            RaceEventDocument doc = ActiveDocument;
            if (doc == null) return;
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "FinishLynx LIF (*.lif)|*.lif|CSV Results (*.csv)|*.csv";
                sfd.FileName = doc.GetEventCodeString() + ".lif";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    MeetDatabaseService.ExportResultsLifOrCsv(doc, sfd.FileName);
                    SetHelp("Exported Results: " + sfd.FileName);
                }
            }
        }

        private void LoadScheduleFileDialog()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Meet Schedule (*.evt;*.sch)|*.evt;*.sch|All Files|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    _schedule = MeetDatabaseService.LoadMeetEventsFile(ofd.FileName);
                    _scheduleIndex = _schedule.Count > 0 ? 0 : -1;
                    SetHelp("Loaded Schedule: " + _schedule.Count + " event(s).");
                }
            }
        }

        private void LoadDirectoryFileDialog()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Athlete Directory (*.ppl;*.csv)|*.ppl;*.csv|All Files|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    _directory = MeetDatabaseService.LoadAthleteDirectory(ofd.FileName);
                    SetHelp("Loaded Directory: " + _directory.Count + " athletes.");
                }
            }
        }

        private void NavigateSchedule(int delta)
        {
            if (_schedule == null || _schedule.Count == 0)
            {
                SetHelp("No .evt schedule loaded. Use File | Load Schedule (.evt).");
                return;
            }
            _scheduleIndex = Math.Max(0, Math.Min(_schedule.Count - 1, _scheduleIndex + delta));
            RaceEventDocument d = MeetDatabaseService.CreateEventFromScheduleSummary(_schedule[_scheduleIndex]);
            SlitScanEngine.BuildSynthesizedCameraStrips(d);
            ReplaceActiveOrOpenEvent(d);
        }

        private void OpenGotoEventDialog()
        {
            using (GotoEventDialog dlg = new GotoEventDialog(ActiveDocument, _schedule))
            {
                dlg.LoadScheduleRequested += delegate { LoadScheduleFileDialog(); };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    if (dlg.SelectedScheduleIndex >= 0 && dlg.SelectedScheduleIndex < _schedule.Count)
                    {
                        _scheduleIndex = dlg.SelectedScheduleIndex;
                        NavigateSchedule(0);
                    }
                    else if (ActiveDocument != null)
                    {
                        ActiveDocument.EventNumber = dlg.EventNum;
                        ActiveDocument.RoundNumber = dlg.RoundNum;
                        ActiveDocument.HeatNumber = dlg.HeatNum;
                        if (ActiveEventControl != null) ActiveEventControl.RefreshInformationGrid();
                    }
                }
            }
        }

        private void BackupAllEvents()
        {
            string res = MeetDatabaseService.BackupEventsToDirectory(_openEvents, LynxConfigService.Instance.BackupDir);
            MessageBox.Show(this, "Backed up open events to:\n" + res, "Backup Events", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ===================== START / ARM / CAPTURE / RUNNING CLOCK =====================
        private void SetArmedState(bool armed)
        {
            if (ActiveDocument == null) return;
            ActiveDocument.IsArmed = armed;
            if (ActiveEventControl != null) ActiveEventControl.RefreshInformationGrid();
            SyncTopLists();
            UpdateTitleAndStatus();
        }

        private void ToggleArmState()
        {
            if (ActiveDocument == null) return;
            SetArmedState(!ActiveDocument.IsArmed);
        }

        private void ToggleCaptureState()
        {
            if (ActiveDocument == null) return;
            ActiveDocument.IsCapturing = !ActiveDocument.IsCapturing;
            if (ActiveDocument.IsCapturing && !_runClockRunning)
            {
                SetScoreboardState(true, true, false);
            }
            else if (!ActiveDocument.IsCapturing && _runClockRunning)
            {
                FinishRunningClock();
            }
            if (ActiveEventControl != null) ActiveEventControl.RefreshInformationGrid();
            SyncTopLists();
            UpdateTitleAndStatus();
        }

        private void OpenManualStartDialog()
        {
            using (ManualStartDialog d = new ManualStartDialog())
            {
                if (d.ShowDialog(this) == DialogResult.OK && ActiveDocument != null)
                {
                    int n = ActiveDocument.RecordedStarts.Count + 1;
                    ActiveDocument.RecordedStarts.Add(new TimeTrialStartEntry
                    {
                        StartNumber = n,
                        GunTimeOfDay = d.CapturedStartTod,
                        OffsetSeconds = d.OffsetSeconds,
                        Source = StartSignalKind.ManualTrigger,
                        Label = string.Format(CultureInfo.InvariantCulture, "Start {0} ({1:HH:mm:ss.ff})", n, d.CapturedStartTod)
                    });
                    ActiveDocument.SelectedStartIndex = ActiveDocument.RecordedStarts.Count - 1;
                    ApplySelectedStartToEvent();
                    SetScoreboardState(true, true, false);
                }
            }
        }

        private void TriggerTimeOfDayStart()
        {
            if (ActiveDocument == null) return;
            DateTime now = DateTime.Now;
            ActiveDocument.RecordedStarts.Add(new TimeTrialStartEntry
            {
                StartNumber = ActiveDocument.RecordedStarts.Count + 1,
                GunTimeOfDay = now,
                Source = StartSignalKind.TimeOfDay
            });
            ActiveDocument.SelectedStartIndex = ActiveDocument.RecordedStarts.Count - 1;
            ApplySelectedStartToEvent();
        }

        private void ApplyImageStartFromCrosshair()
        {
            if (ActiveDocument == null || ActiveEventControl == null || ActiveEventControl.ActiveViewport == null) return;
            double off = ActiveEventControl.ActiveViewport.CursorTimeSeconds;
            ActiveDocument.StartOffsetSeconds += off;
            ActiveDocument.StartSource = StartSignalKind.ImageOriginLine;
            ActiveDocument.HasStartSignal = true;
            ActiveEventControl.RefreshAll();
        }

        private void ApplySelectedStartToEvent()
        {
            if (ActiveDocument == null) return;
            if (ActiveDocument.RecordedStarts.Count > 0)
            {
                int idx = Math.Max(0, Math.Min(ActiveDocument.RecordedStarts.Count - 1, _cbStarts != null ? _cbStarts.SelectedIndex : 0));
                TimeTrialStartEntry st = ActiveDocument.RecordedStarts[idx];
                ActiveDocument.GunTimeOfDay = st.GunTimeOfDay;
                ActiveDocument.StartOffsetSeconds = st.OffsetSeconds;
                ActiveDocument.StartSource = st.Source;
                ActiveDocument.HasStartSignal = true;
                ActiveDocument.IsArmed = false;
            }
            if (ActiveEventControl != null) ActiveEventControl.RefreshInformationGrid();
            SyncTopLists();
            UpdateTitleAndStatus();
        }

        private void SetScoreboardState(bool init, bool running, bool held)
        {
            if (ActiveDocument != null)
            {
                ActiveDocument.ScoreboardInitialized = init;
                ActiveDocument.ScoreboardRunning = running;
                ActiveDocument.ScoreboardHeld = held;
            }
            if (running)
            {
                _runClockStartTime = DateTime.Now;
                _runClockRunning = true;
                _runClockPaused = false;
                _pausedElapsedSec = 0.0;
                _runClockTimer.Start();
            }
            else if (init)
            {
                _runClockTimer.Stop();
                _runClockRunning = false;
                _txtRunTime.Text = "0.0";
            }
            UpdateTitleAndStatus();
        }

        private void PauseRunningClock()
        {
            if (!_runClockRunning) return;
            _runClockPaused = !_runClockPaused;
            if (_runClockPaused)
            {
                _pausedElapsedSec += (DateTime.Now - _runClockStartTime).TotalSeconds;
                _simulatedFreezeElapsedSec = _pausedElapsedSec;
            }
            else
            {
                _runClockStartTime = DateTime.Now;
            }
            UpdateClockText();
            UpdateTitleAndStatus();
        }

        private void FinishRunningClock()
        {
            if (_runClockRunning)
            {
                _pausedElapsedSec += (DateTime.Now - _runClockStartTime).TotalSeconds;
            }
            _runClockTimer.Stop();
            _runClockRunning = false;
            _runClockPaused = false;
            TransmitScoreboardNow();
            UpdateTitleAndStatus();
        }

        private void StopRunningClock()
        {
            _runClockTimer.Stop();
            _runClockRunning = false;
            _runClockPaused = false;
            _pausedElapsedSec = 0.0;
            _txtRunTime.Text = "0.0";
            UpdateTitleAndStatus();
        }

        private void OnRunClockTick(object sender, EventArgs e)
        {
            UpdateClockText();
            if (_hardware != null && !_runClockPaused)
            {
                double elapsed = _pausedElapsedSec + (DateTime.Now - _runClockStartTime).TotalSeconds;
                _hardware.TransmitRunningTime(elapsed, ActiveDocument != null ? ActiveDocument.GetWindFormatted() : "NWI");
            }
        }

        private void UpdateClockText()
        {
            if (_txtRunTime == null) return;
            if (_clockShowTod)
            {
                _txtRunTime.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                return;
            }
            double elapsed = _runClockPaused ? _simulatedFreezeElapsedSec : (_runClockRunning ? (_pausedElapsedSec + (DateTime.Now - _runClockStartTime).TotalSeconds) : _pausedElapsedSec);
            _txtRunTime.Text = elapsed.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private void ToggleScoreboardHold()
        {
            if (ActiveDocument == null) return;
            ActiveDocument.ScoreboardHeld = !ActiveDocument.ScoreboardHeld;
            TransmitScoreboardNow();
        }

        private void TransmitScoreboardNow()
        {
            if (ActiveDocument == null || _hardware == null) return;
            _hardware.TransmitOfficialResults(ActiveDocument);
            SetHelp("Scoreboard Transmitted: " + _hardware.LastTransmittedPayload);
        }

        // ===================== WIND / CALIBRATION / EVALUATION / OVERLAYS =====================
        private void OpenWindDialog()
        {
            if (ActiveDocument == null) return;
            using (SelectWindDialog d = new SelectWindDialog(ActiveDocument))
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    ActiveDocument.HasWindReading = d.HasWind;
                    ActiveDocument.WindReading = d.WindValue;
                    ActiveDocument.WindUnit = d.Unit;
                    ActiveDocument.WindMode = d.Mode;
                    if (ActiveEventControl != null) ActiveEventControl.RefreshInformationGrid();
                    TransmitScoreboardNow();
                }
            }
        }

        private void SimulateWindReading()
        {
            if (ActiveDocument == null) return;
            Random rnd = new Random();
            ActiveDocument.HasWindReading = true;
            ActiveDocument.WindReading = Math.Round((rnd.NextDouble() * 4.4) - 1.6, 1);
            if (ActiveEventControl != null) ActiveEventControl.RefreshInformationGrid();
            TransmitScoreboardNow();
            SetHelp("Simulated Ultrasonic Wind Gauge: " + ActiveDocument.GetWindFormatted());
        }

        private void OpenZeroGunTestDialog()
        {
            if (ActiveDocument == null) return;
            using (ZeroGunTestDialog d = new ZeroGunTestDialog(ActiveDocument.StartOffsetSeconds))
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    ActiveDocument.StartOffsetSeconds = d.MeasuredOffsetMs / 1000.0;
                    if (ActiveEventControl != null) ActiveEventControl.RefreshInformationGrid();
                }
            }
        }

        private void AddSplitLocationFlow()
        {
            if (ActiveDocument == null) return;
            using (SimplePromptDialog d = new SimplePromptDialog("Add Split Location", "Split Name (e.g. 200m, Bell):", "Split " + ActiveDocument.SplitLocations.Count))
            {
                if (d.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(d.ValueText))
                {
                    ActiveDocument.SplitLocations.Add(new SplitLocationDef { Name = d.ValueText.Trim(), CaptureDurationSeconds = 10.0 });
                    if (ActiveEventControl != null) ActiveEventControl.RefreshInformationGrid();
                }
            }
        }

        private void AutoEvaluateActiveStrip()
        {
            RaceEventDocument doc = ActiveDocument;
            EventWindowControl ew = ActiveEventControl;
            if (doc == null || ew == null || ew.ActiveViewport == null || ew.ActiveViewport.ActiveStream == null) return;
            SlitCameraStream cam = ew.ActiveViewport.ActiveStream;
            if (cam.IsVideo) return;

            SlitScanEngine.ComputeMotionEnergyProfile(cam);
            int assigned = 0;
            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                CompetitorEntry c = doc.Competitors[i];
                if (c.Status != CompetitorStatus.Valid) continue;
                int seedCol = (int)Math.Max(10, Math.Min(cam.TotalColumns - 10, cam.TotalColumns * (0.15 + 0.08 * i)));
                int peakCol = SlitScanEngine.FindNextObjectColumn(cam, seedCol, 1);
                if (peakCol > 0)
                {
                    c.RawTimeSeconds = cam.ColumnToTimeSeconds(peakCol);
                    c.EvaluatedTimeSeconds = c.RawTimeSeconds;
                    c.EvaluatedVerticalRatio = c.Lane > 0 ? doc.LaneToCenterRatio(c.Lane) : (0.15f + 0.09f * (i % Math.Max(1, doc.LaneCount)));
                    c.HasImageEvaluation = true;
                    assigned++;
                }
            }
            ew.RefreshResultsGrid();
            ew.InvalidateViewports();
            SetHelp("Auto-Evaluated " + assigned + " motion peaks on " + cam.DisplayName);
        }

        // ===================== IMAGE & RESULTS POPUPS / FLOATERS =====================
        private void ActiveVpAction(Action<SlitScanViewportControl> act)
        {
            EventWindowControl ew = ActiveEventControl;
            if (ew != null && ew.ActiveViewport != null && act != null) act(ew.ActiveViewport);
        }

        private void OpenSelectedCameraSettings()
        {
            RaceEventDocument doc = ActiveDocument;
            EventWindowControl ew = ActiveEventControl;
            if (doc == null || doc.Cameras.Count == 0) return;
            int idx = ew != null ? ew.ActiveCameraIndex : 0;
            SlitCameraStream cam = doc.Cameras[Math.Max(0, Math.Min(doc.Cameras.Count - 1, idx))];
            using (CameraSettingsDialog d = new CameraSettingsDialog(doc, cam))
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    SlitScanEngine.UpdateDisplayBitmap(cam);
                    if (ew != null) ew.RefreshAll();
                }
            }
        }

        private void OpenImageAdjustmentsFloater()
        {
            if (_adjFloater == null || _adjFloater.IsDisposed)
            {
                _adjFloater = new ImageAdjustmentsFloater();
                _adjFloater.ValuesChanged += delegate(int br, int ct, double gm, int bl)
                {
                    EventWindowControl ew = ActiveEventControl;
                    if (ew == null || ew.ActiveViewport == null || ew.ActiveViewport.ActiveStream == null) return;
                    SlitCameraStream cam = ew.ActiveViewport.ActiveStream;
                    cam.BrightnessOffset = br;
                    cam.ContrastOffset = ct;
                    cam.GammaValue = gm;
                    cam.BlackLevel = bl;
                    SlitScanEngine.UpdateDisplayBitmap(cam);
                    ew.InvalidateViewports();
                };
            }
            if (ActiveEventControl != null && ActiveEventControl.ActiveViewport != null && ActiveEventControl.ActiveViewport.ActiveStream != null)
            {
                SlitCameraStream s = ActiveEventControl.ActiveViewport.ActiveStream;
                _adjFloater.LoadFromStream(s.BrightnessOffset, s.ContrastOffset, s.GammaValue, s.BlackLevel);
            }
            _adjFloater.Show(this);
        }

        private void OpenKeypadFloater()
        {
            if (_keypadFloater == null || _keypadFloater.IsDisposed)
            {
                _keypadFloater = new KeypadFloater();
                _keypadFloater.DigitPressed += delegate(string d) { if (ActiveEventControl != null) ActiveEventControl.AppendKeypadDigit(d); };
                _keypadFloater.BackspacePressed += delegate { if (ActiveEventControl != null) ActiveEventControl.BackspaceKeypad(); };
                _keypadFloater.ClearPressed += delegate { if (ActiveEventControl != null) ActiveEventControl.ClearKeypad(); };
                _keypadFloater.EnterPressed += delegate { if (ActiveEventControl != null) ActiveEventControl.CommitEvalBoxEntry(ActiveEventControl.ActiveCameraIndex); };
            }
            _keypadFloater.Show(this);
        }

        private void OpenOptions(int tabIndex)
        {
            string prevLang = Loc.CurrentLanguage;
            using (OptionsDialog dlg = new OptionsDialog(_hardware, tabIndex))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    ApplyConfigToHardware();
                    _lblSerialStatus.Text = _hardware.IsSerialConnected ? ("COM: " + _hardware.SerialPortName) : "UDP Ready";
                    if (!string.Equals(prevLang, Loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        BuildMenuBar();
                    }
                    if (ActiveEventControl != null)
                    {
                        if (ActiveDocument != null)
                        {
                            ActiveDocument.LaneBarSide = LynxExtendedConfig.Instance.LaneBarSide;
                            ActiveDocument.SmoothZoom = LynxExtendedConfig.Instance.SmoothZoom;
                        }
                        ActiveEventControl.ApplyLayoutPreset(LynxConfigService.Instance.LayoutSelection);
                        ActiveEventControl.RebuildResultsToolbar();
                        ActiveEventControl.RefreshAll();
                    }
                    UpdateTitleAndStatus();
                }
            }
        }

        private void OpenResultsFilterDialog()
        {
            if (ActiveDocument == null) return;
            using (ResultsFilterDialog d = new ResultsFilterDialog(ActiveDocument))
            {
                if (d.ShowDialog(this) == DialogResult.OK && ActiveEventControl != null) ActiveEventControl.RefreshResultsGrid();
            }
        }

        private void OpenCropToResultsDialog()
        {
            RaceEventDocument doc = ActiveDocument;
            EventWindowControl ew = ActiveEventControl;
            if (doc == null || ew == null || ew.ActiveViewport == null || ew.ActiveViewport.ActiveStream == null) return;
            using (CropToResultsDialog d = new CropToResultsDialog())
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    SlitScanEngine.CropToResultsStream(doc, ew.ActiveViewport.ActiveStream, d.LeaderSec, d.TrailerSec, d.SelectedOnly, ew.GetSelectedCompetitor());
                    ew.RefreshCameraBars();
                    ew.InvalidateViewports();
                }
            }
        }

        private void FindTimeDialogFlow()
        {
            if (ActiveEventControl == null || ActiveEventControl.ActiveViewport == null) return;
            using (SimplePromptDialog d = new SimplePromptDialog("Find Time", "Enter target time in seconds (e.g. 12.84):", ActiveEventControl.ActiveViewport.CursorTimeSeconds.ToString("0.00", CultureInfo.InvariantCulture)))
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    double t = MeetDatabaseService.ParseFormattedTime(d.ValueText);
                    if (!double.IsNaN(t)) ActiveEventControl.ActiveViewport.CenterOnTime(t);
                }
            }
        }

        private void OpenFindIdDialog()
        {
            if (ActiveEventControl == null) return;
            using (SimplePromptDialog d = new SimplePromptDialog("Find Competitor", "Enter Bib ID or Last Name:", ""))
            {
                if (d.ShowDialog(this) == DialogResult.OK) ActiveEventControl.SelectCompetitorByIdOrName(d.ValueText);
            }
        }

        private void SetPackLeadersFlow()
        {
            if (ActiveDocument == null) return;
            using (SimplePromptDialog d = new SimplePromptDialog("Set Pack Leaders", "Minimum time gap between packs (seconds):", "1.00"))
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    double gap;
                    if (!double.TryParse(d.ValueText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out gap)) gap = 1.0;
                    ActiveDocument.RecalculatePlaces();
                    double prev = -999;
                    foreach (CompetitorEntry c in ActiveDocument.Competitors)
                    {
                        c.IsPackLeader = c.HasValidNumericTime && (prev < 0 || (c.RawTimeSeconds - prev) >= gap);
                        if (c.HasValidNumericTime) prev = c.RawTimeSeconds;
                    }
                    if (ActiveEventControl != null) ActiveEventControl.RefreshResultsGrid();
                }
            }
        }

        private void AddTextOverlayFlow()
        {
            if (ActiveDocument == null || ActiveEventControl == null || ActiveEventControl.ActiveViewport == null) return;
            using (SimplePromptDialog d = new SimplePromptDialog("Add Text Overlay", "Overlay text at cursor:", "Official Finish"))
            {
                if (d.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(d.ValueText))
                {
                    ActiveDocument.Overlays.Add(new ImageOverlayItem
                    {
                        Kind = ImageOverlayKind.Text,
                        Text = d.ValueText.Trim(),
                        TimeSeconds = ActiveEventControl.ActiveViewport.CursorTimeSeconds,
                        VerticalRatio = ActiveEventControl.ActiveViewport.CursorVerticalRatio
                    });
                    ActiveEventControl.InvalidateViewports();
                }
            }
        }

        private void AddQuickTimeOverlay(ImageOverlayKind kind)
        {
            if (ActiveDocument == null || ActiveEventControl == null || ActiveEventControl.ActiveViewport == null) return;
            double t = ActiveEventControl.ActiveViewport.CursorTimeSeconds;
            ActiveDocument.Overlays.Add(new ImageOverlayItem
            {
                Kind = kind,
                Text = CompetitorEntry.FormatSeconds(t, ActiveDocument.Precision),
                TimeSeconds = t,
                VerticalRatio = 0.08f
            });
            ActiveEventControl.InvalidateViewports();
        }

        private void AddLineLabelOverlayFlow()
        {
            if (ActiveDocument == null) return;
            using (SimplePromptDialog d = new SimplePromptDialog("Line Labels Overlay", "Label text (e.g. Track Lane Numbers Added):", "Lanes 1-" + ActiveDocument.LaneCount))
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    for (int ln = 1; ln <= ActiveDocument.LaneCount; ln++)
                    {
                        ActiveDocument.Overlays.Add(new ImageOverlayItem
                        {
                            Kind = ImageOverlayKind.LineLabels,
                            Text = "Lane " + ln,
                            TimeSeconds = ActiveEventControl != null && ActiveEventControl.ActiveViewport != null ? ActiveEventControl.ActiveViewport.CursorTimeSeconds : 0.1,
                            VerticalRatio = ActiveDocument.LaneToCenterRatio(ln)
                        });
                    }
                    if (ActiveEventControl != null) ActiveEventControl.InvalidateViewports();
                }
            }
        }

        private void RemoveAllOverlays()
        {
            if (ActiveDocument == null) return;
            ActiveDocument.Overlays.Clear();
            ActiveDocument.Annotations.Clear();
            ActiveDocument.ShowEventNameOverlay = false;
            if (ActiveEventControl != null) ActiveEventControl.InvalidateViewports();
            UpdateTitleAndStatus();
        }

        private void ToggleEventNameOverlay() { if (ActiveDocument != null) { ActiveDocument.ShowEventNameOverlay = !ActiveDocument.ShowEventNameOverlay; if (ActiveEventControl != null) ActiveEventControl.InvalidateViewports(); UpdateTitleAndStatus(); } }
        private void ToggleRetainOverlays() { if (ActiveDocument != null) { ActiveDocument.RetainOverlays = !ActiveDocument.RetainOverlays; UpdateTitleAndStatus(); } }
        private void ToggleSmoothZoom() { if (ActiveDocument != null) { ActiveDocument.SmoothZoom = !ActiveDocument.SmoothZoom; if (ActiveEventControl != null) ActiveEventControl.InvalidateViewports(); UpdateTitleAndStatus(); } }
        private void ToggleScrollNewImage() { ActiveVpAction(vp => vp.AutoScrollCapture = !vp.AutoScrollCapture); }
        private void MarkFrame(bool isStart) { if (ActiveDocument != null && ActiveEventControl != null && ActiveEventControl.ActiveViewport != null) { if (isStart) ActiveDocument.MarkedStartFrameTime = ActiveEventControl.ActiveViewport.CursorTimeSeconds; else ActiveDocument.MarkedEndFrameTime = ActiveEventControl.ActiveViewport.CursorTimeSeconds; ActiveEventControl.InvalidateViewports(); } }
        private void ClearMarkedFrames() { if (ActiveDocument != null) { ActiveDocument.MarkedStartFrameTime = null; ActiveDocument.MarkedEndFrameTime = null; if (ActiveEventControl != null) ActiveEventControl.InvalidateViewports(); } }
        private void SetEventPrecision(TimePrecision p) { if (ActiveDocument != null) { ActiveDocument.Precision = p; if (ActiveEventControl != null) ActiveEventControl.RefreshAll(); } }
        private void ClearActiveAnnotations() { if (ActiveDocument != null) { ActiveDocument.Annotations.Clear(); if (ActiveEventControl != null) ActiveEventControl.InvalidateViewports(); } }
        private void CopyActiveTimeToClipboard() { if (ActiveDocument != null && ActiveEventControl != null && ActiveEventControl.ActiveViewport != null) Clipboard.SetText(CompetitorEntry.FormatSeconds(ActiveEventControl.ActiveViewport.CursorTimeSeconds, ActiveDocument.Precision)); }

        private void OpenFullScreen(bool two)
        {
            if (ActiveDocument == null) return;
            using (FullScreenImageForm fs = new FullScreenImageForm(ActiveDocument, two))
            {
                fs.ShowDialog(this);
            }
        }

        private void ImportReacTimeFlow()
        {
            if (ActiveDocument == null) return;
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "ReacTime Data (*.csv;*.txt;*.dat)|*.csv;*.txt;*.dat|All Files|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    int n = MeetDatabaseService.ImportReacTimeData(ActiveDocument, ofd.FileName);
                    if (ActiveEventControl != null) ActiveEventControl.RefreshResultsGrid();
                    SetHelp("Imported " + n + " ReacTime entries.");
                }
            }
        }

        private void OpenLapTimeSplitsDialog()
        {
            if (ActiveDocument == null) return;
            using (LapTimeSplitsDialog d = new LapTimeSplitsDialog(ActiveDocument))
            {
                d.ShowDialog(this);
                if (ActiveEventControl != null) ActiveEventControl.RefreshResultsGrid();
            }
        }

        private void ExportSplitCsvDialog()
        {
            if (ActiveDocument == null) return;
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "Split Times CSV (*.csv)|*.csv";
                sfd.FileName = ActiveDocument.GetEventCodeString() + "_splits.csv";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    MeetDatabaseService.ExportSplitTimesCsv(ActiveDocument, sfd.FileName);
                }
            }
        }

        private void EditScoreboardMessageFlow()
        {
            if (ActiveDocument == null) return;
            using (SimplePromptDialog d = new SimplePromptDialog("Scoreboard Custom Message", "Message text to broadcast:", ActiveDocument.ScoreboardCustomMessage))
            {
                if (d.ShowDialog(this) == DialogResult.OK) ActiveDocument.ScoreboardCustomMessage = d.ValueText;
            }
        }

        private void OpenPageSetupDialog()
        {
            using (PrintPageSetupDialog d = new PrintPageSetupDialog(LynxExtendedConfig.Instance.PrintSettings)) d.ShowDialog(this);
        }

        private void ExportAnnotatedImageDialog()
        {
            if (ActiveDocument == null) return;
            int ci = ActiveEventControl != null ? ActiveEventControl.ActiveCameraIndex : 0;
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG Official Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg";
                sfd.FileName = ActiveDocument.GetEventCodeString() + "_PhotoFinish.png";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    using (Bitmap bmp = SlitScanEngine.RenderAnnotatedExportBitmap(ActiveDocument, ci))
                    {
                        bmp.Save(sfd.FileName);
                    }
                    SetHelp("Exported annotated image: " + sfd.FileName);
                }
            }
        }

        private void PrintAnnotatedImagePreview()
        {
            if (ActiveDocument == null) return;
            int ci = ActiveEventControl != null ? ActiveEventControl.ActiveCameraIndex : 0;
            using (PrintPreviewDialog ppd = new PrintPreviewDialog())
            using (System.Drawing.Printing.PrintDocument pd = new System.Drawing.Printing.PrintDocument())
            {
                pd.DefaultPageSettings.Landscape = LynxExtendedConfig.Instance.PrintSettings.Landscape;
                pd.PrintPage += delegate(object s, System.Drawing.Printing.PrintPageEventArgs pe)
                {
                    using (Bitmap bmp = SlitScanEngine.RenderAnnotatedExportBitmap(ActiveDocument, ci))
                    {
                        pe.Graphics.DrawImage(bmp, pe.MarginBounds);
                    }
                };
                ppd.Document = pd;
                ppd.Width = 980; ppd.Height = 680;
                ppd.ShowDialog(this);
            }
        }

        private void ShowShortcutsDialog()
        {
            MessageBox.Show(this,
                "OptiFinish Keyboard Shortcuts:\n\n" +
                "Ctrl+N: New Event\nCtrl+O / Alt+O: Open / Open Recent\nCtrl+S: Save Event\n" +
                "Alt+1: Hardware Control Window\nAlt+2 / Alt+3: Event Windows\n" +
                "Ctrl+A: Arm Event   |   Alt+T: Toggle Capture\n" +
                "Ctrl+Plus / Ctrl+Minus: Zoom In / Out\n" +
                "Alt+C: Crop   |   Alt+B: White Balance   |   Ctrl+R: Remove Overlays\n" +
                "Ctrl+F / Ctrl+G: Full Screen (1 or 2 Cameras)\n" +
                "F2: Edit Selected Result   |   Left/Right Arrows: Step 1 Hairline Column\n" +
                "Right-Drag on Image: Zoom/Selection Box   |   Right-Click: Lane & Actions Menu",
                "OptiFinish Shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
