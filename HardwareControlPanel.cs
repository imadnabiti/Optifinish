using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using OptiFinish.Core;

namespace OptiFinish
{
    /// <summary>
    /// Authentic FinishLynx Hardware Control workspace panel (Window | Hardware Control / Alt+1):
    /// Camera status table, (i)/1D/2D/EVF/AEC bar, 1D/2D alignment canvas with Enhance/Lanes bar,
    /// Focus Helper, Level indicator, White Balance controls, and Remote Positioner & Iris pad.
    /// </summary>
    public sealed class HardwareControlPanel : UserControl
    {
        private RaceEventDocument _document;
        private int _selectedCamIndex;
        private bool _align1DOn;
        private bool _align2DOn = true;
        private bool _evfOn;
        private bool _aecOn;
        private bool _wbSetMode;
        private bool _focusHelperOn = true;
        private int _alignZoom = 100;
        private float _panX;
        private float _panY;
        private float _tiltDeg = 0.0f;
        private int _focusOffset;
        private int _irisLevel = 55;
        private bool _autoIris = true;

        private LynxIconButton _btnCamInfo, _btn1DAlign, _btn2DAlign;
        private CheckBox _chkEvf, _chkAec;
        private DataGridView _dgvCameras;
        private Panel _alignWorkspace;
        private PictureBox _alignCanvas;
        private LynxIconButton _btnLevel;

        public event EventHandler<EventArgs> CameraSettingsChanged;
        public event Action<string> StatusHintRequested;

        public HardwareControlPanel()
        {
            BackColor = SystemColors.Control;
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            BuildUi();
        }

        public void SetDocument(RaceEventDocument doc)
        {
            _document = doc;
            _selectedCamIndex = 0;
            RefreshCameraGrid();
            InvalidateCanvas();
        }

        public SlitCameraStream ActiveCamera
        {
            get
            {
                if (_document == null || _document.Cameras.Count == 0) return null;
                int idx = Math.Max(0, Math.Min(_document.Cameras.Count - 1, _selectedCamIndex));
                return _document.Cameras[idx];
            }
        }

        private LynxIconButton MkBtn(string iconKey, int w, int h, string hint, Action onClick)
        {
            LynxIconButton b = new LynxIconButton(iconKey, w, h, hint);
            b.StatusHintHover += delegate(string s)
            {
                if (StatusHintRequested != null) StatusHintRequested(s);
            };
            b.Click += delegate { if (onClick != null) onClick(); };
            return b;
        }

        private void BuildUi()
        {
            // Top bar: (i), (1D), (2D), [ ] EVF, [ ] AEC
            Panel topBar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = SystemColors.Control, Padding = new Padding(4, 3, 4, 2) };
            _btnCamInfo = MkBtn("CameraSettings", 24, 24, "Camera Settings (Alt+C / Setup)", delegate { OpenCameraSettings(); });
            _btnCamInfo.Location = new Point(4, 3);
            _btn1DAlign = MkBtn(_align1DOn ? "Align1DGreen" : "Align1DRed", 24, 24, "Toggle 1D Camera Alignment Mode", delegate
            {
                _align1DOn = !_align1DOn;
                if (_align1DOn) _align2DOn = false;
                UpdateAlignIcons();
                InvalidateCanvas();
            });
            _btn1DAlign.Location = new Point(31, 3);
            _btn2DAlign = MkBtn(_align2DOn ? "Align2DGreen" : "Align2DRed", 24, 24, "Toggle 2D EasyAlign Frame View", delegate
            {
                _align2DOn = !_align2DOn;
                if (_align2DOn) _align1DOn = false;
                UpdateAlignIcons();
                InvalidateCanvas();
            });
            _btn2DAlign.Location = new Point(58, 3);

            _chkEvf = new CheckBox { Text = "EVF", AutoSize = true, Location = new Point(92, 6) };
            _chkEvf.CheckedChanged += delegate { _evfOn = _chkEvf.Checked; InvalidateCanvas(); };
            _chkAec = new CheckBox { Text = "AEC", AutoSize = true, Location = new Point(144, 6) };
            _chkAec.CheckedChanged += delegate { _aecOn = _chkAec.Checked; InvalidateCanvas(); };

            topBar.Controls.AddRange(new Control[] { _btnCamInfo, _btn1DAlign, _btn2DAlign, _chkEvf, _chkAec });

            // Camera Grid
            _dgvCameras = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 148,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.Fixed3D,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                ColumnHeadersHeight = 22,
                RowTemplate = { Height = 20 },
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(215, 215, 215)
            };
            _dgvCameras.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            _dgvCameras.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
            string[] cols = new string[] { "Camera", "Capture", "Rate", "Height", "Mode", "Gain", "Orientation", "Power", "Ext. Sync" };
            int[] widths = new int[] { 150, 74, 70, 70, 110, 88, 92, 82, 88 };
            for (int i = 0; i < cols.Length; i++)
            {
                _dgvCameras.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = cols[i],
                    Width = widths[i],
                    SortMode = DataGridViewColumnSortMode.NotSortable
                });
            }
            _dgvCameras.SelectionChanged += delegate
            {
                if (_dgvCameras.SelectedRows.Count > 0)
                {
                    _selectedCamIndex = _dgvCameras.SelectedRows[0].Index;
                    InvalidateCanvas();
                }
            };
            _dgvCameras.CellDoubleClick += delegate { OpenCameraSettings(); };

            Splitter splitH = new Splitter { Dock = DockStyle.Top, Height = 5, BackColor = SystemColors.ControlDark };

            // Bottom alignment panel
            _alignWorkspace = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };

            // Alignment toolbar
            Panel alignBar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = SystemColors.Control };
            int ax = 4;
            LynxIconButton bZOut = MkBtn("ZoomOut", 22, 22, "Alignment Zoom Out", delegate { _alignZoom = Math.Max(50, _alignZoom - 25); InvalidateCanvas(); });
            bZOut.Location = new Point(ax, 4); ax += 24;
            LynxIconButton bZIn = MkBtn("ZoomIn", 22, 22, "Alignment Zoom In", delegate { _alignZoom = Math.Min(400, _alignZoom + 25); InvalidateCanvas(); });
            bZIn.Location = new Point(ax, 4); ax += 30;
            LynxIconButton bFocusHelp = MkBtn("FocusHelper", 68, 24, "Toggle Alignment Focus Helper", delegate { _focusHelperOn = !_focusHelperOn; InvalidateCanvas(); });
            bFocusHelp.Location = new Point(ax, 3); ax += 72;
            _btnLevel = MkBtn("LevelGreen", 77, 24, "Camera Spirit Level Indicator", delegate { _tiltDeg = 0.0f; UpdateLevelIcon(); InvalidateCanvas(); });
            _btnLevel.Location = new Point(ax, 3); ax += 82;

            LynxIconButton bWbZero = MkBtn("WBZero", 42, 24, "White Balance: Zero / Reset", delegate { AdjWb(0, 0, 0, true); });
            bWbZero.Location = new Point(ax, 3); ax += 44;
            LynxIconButton bWbSet = MkBtn("WBSet", 42, 24, "White Balance: Sample / Toggle Manual", delegate { _wbSetMode = !_wbSetMode; });
            bWbSet.Location = new Point(ax, 3); ax += 44;
            LynxIconButton bWbApply = MkBtn("WBApply", 42, 24, "White Balance: Apply to Active Event Stream", delegate
            {
                if (ActiveCamera != null) SlitScanEngine.UpdateDisplayBitmap(ActiveCamera);
                if (CameraSettingsChanged != null) CameraSettingsChanged(this, EventArgs.Empty);
            });
            bWbApply.Location = new Point(ax, 3); ax += 48;

            LynxIconButton bRLess = MkBtn("WBRedLess", 22, 22, "White Balance: Red -", delegate { AdjWb(-5, 0, 0, false); });
            bRLess.Location = new Point(ax, 4); ax += 24;
            LynxIconButton bRMore = MkBtn("WBRedMore", 22, 22, "White Balance: Red +", delegate { AdjWb(5, 0, 0, false); });
            bRMore.Location = new Point(ax, 4); ax += 26;
            LynxIconButton bGLess = MkBtn("WBGreenLess", 22, 22, "White Balance: Green -", delegate { AdjWb(0, -5, 0, false); });
            bGLess.Location = new Point(ax, 4); ax += 24;
            LynxIconButton bGMore = MkBtn("WBGreenMore", 22, 22, "White Balance: Green +", delegate { AdjWb(0, 5, 0, false); });
            bGMore.Location = new Point(ax, 4); ax += 26;
            LynxIconButton bBLess = MkBtn("WBBlueLess", 22, 22, "White Balance: Blue -", delegate { AdjWb(0, 0, -5, false); });
            bBLess.Location = new Point(ax, 4); ax += 24;
            LynxIconButton bBMore = MkBtn("WBBlueMore", 22, 22, "White Balance: Blue +", delegate { AdjWb(0, 0, 5, false); });
            bBMore.Location = new Point(ax, 4); ax += 28;

            alignBar.Controls.AddRange(new Control[]
            {
                bZOut, bZIn, bFocusHelp, _btnLevel,
                bWbZero, bWbSet, bWbApply,
                bRLess, bRMore, bGLess, bGMore, bBLess, bBMore
            });

            // Right-side Remote Positioner & Iris Dock (matches crop_hwc_remote.png / HWC_RemoteControls.jpg)
            Panel remoteDock = new Panel
            {
                Dock = DockStyle.Right,
                Width = 104,
                BackColor = SystemColors.Control,
                BorderStyle = BorderStyle.Fixed3D,
                Padding = new Padding(4)
            };
            int rx = 6, ry = 10, bw = 28, bh = 28, sp = 30;
            remoteDock.Controls.Add(MkPosBtn("RemoteCCW", rx + 0 * sp, ry + 0 * sp, bw, bh, "Rotate Camera Counter-Clockwise", delegate { _tiltDeg -= 0.4f; UpdateLevelIcon(); InvalidateCanvas(); }));
            remoteDock.Controls.Add(MkPosBtn("RemoteUp", rx + 1 * sp, ry + 0 * sp, bw, bh, "Tilt Camera Up", delegate { _panY -= 8f; InvalidateCanvas(); }));
            remoteDock.Controls.Add(MkPosBtn("RemoteCW", rx + 2 * sp, ry + 0 * sp, bw, bh, "Rotate Camera Clockwise", delegate { _tiltDeg += 0.4f; UpdateLevelIcon(); InvalidateCanvas(); }));

            remoteDock.Controls.Add(MkPosBtn("RemoteLeft", rx + 0 * sp, ry + 1 * sp, bw, bh, "Pan Camera Left", delegate { _panX -= 8f; InvalidateCanvas(); }));
            remoteDock.Controls.Add(MkPosBtn("RemoteDown", rx + 1 * sp, ry + 1 * sp, bw, bh, "Tilt Camera Down", delegate { _panY += 8f; InvalidateCanvas(); }));
            remoteDock.Controls.Add(MkPosBtn("RemoteRight", rx + 2 * sp, ry + 1 * sp, bw, bh, "Pan Camera Right", delegate { _panX += 8f; InvalidateCanvas(); }));

            remoteDock.Controls.Add(MkPosBtn("OpenIris", rx + 0 * sp, ry + 2 * sp, bw, bh, "Open Iris", delegate { _irisLevel = Math.Min(100, _irisLevel + 10); _autoIris = false; InvalidateCanvas(); }));
            remoteDock.Controls.Add(MkPosBtn("FocusNear", rx + 1 * sp, ry + 2 * sp, bw, bh, "Focus Near", delegate { _focusOffset -= 1; InvalidateCanvas(); }));
            remoteDock.Controls.Add(MkPosBtn("ZoomIn", rx + 2 * sp, ry + 2 * sp, bw, bh, "Zoom Lens In", delegate { _alignZoom = Math.Min(400, _alignZoom + 25); InvalidateCanvas(); }));

            remoteDock.Controls.Add(MkPosBtn("CloseIris", rx + 0 * sp, ry + 3 * sp, bw, bh, "Close Iris", delegate { _irisLevel = Math.Max(5, _irisLevel - 10); _autoIris = false; InvalidateCanvas(); }));
            remoteDock.Controls.Add(MkPosBtn("FocusFar", rx + 1 * sp, ry + 3 * sp, bw, bh, "Focus Far", delegate { _focusOffset += 1; InvalidateCanvas(); }));
            remoteDock.Controls.Add(MkPosBtn("ZoomOut", rx + 2 * sp, ry + 3 * sp, bw, bh, "Zoom Lens Out", delegate { _alignZoom = Math.Max(50, _alignZoom - 25); InvalidateCanvas(); }));

            remoteDock.Controls.Add(MkPosBtn("AutoIris", rx + 0 * sp, ry + 4 * sp, bw, bh, "Toggle Auto Iris", delegate { _autoIris = !_autoIris; _irisLevel = 60; InvalidateCanvas(); }));
            remoteDock.Controls.Add(MkPosBtn("CenterPositioner", rx, ry + 5 * sp + 2, 42, 28, "Center Remote Positioner", delegate { _panX = 0f; _panY = 0f; _tiltDeg = 0f; UpdateLevelIcon(); InvalidateCanvas(); }));
            remoteDock.Controls.Add(MkPosBtn("AutoAlign", rx + 46, ry + 5 * sp + 2, 42, 28, "Auto Align on Finish Line", delegate { _panX = 0f; _panY = 0f; _tiltDeg = 0f; _focusOffset = 0; UpdateLevelIcon(); InvalidateCanvas(); }));

            _alignCanvas = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 26, 38),
                BorderStyle = BorderStyle.Fixed3D
            };
            _alignCanvas.Paint += PaintAlignmentCanvas;
            _alignCanvas.Resize += delegate { _alignCanvas.Invalidate(); };

            _alignWorkspace.Controls.Add(_alignCanvas);
            _alignWorkspace.Controls.Add(remoteDock);
            _alignWorkspace.Controls.Add(alignBar);

            Controls.Add(_alignWorkspace);
            Controls.Add(splitH);
            Controls.Add(_dgvCameras);
            Controls.Add(topBar);
        }

        private LynxIconButton MkPosBtn(string iconKey, int x, int y, int w, int h, string hint, Action onClick)
        {
            LynxIconButton b = MkBtn(iconKey, w, h, hint, onClick);
            b.Location = new Point(x, y);
            return b;
        }

        private void UpdateAlignIcons()
        {
            _btn1DAlign.IconKey = _align1DOn ? "Align1DGreen" : "Align1DRed";
            _btn2DAlign.IconKey = _align2DOn ? "Align2DGreen" : "Align2DRed";
        }

        private void UpdateLevelIcon()
        {
            float abs = Math.Abs(_tiltDeg);
            _btnLevel.IconKey = abs < 0.3f ? "LevelGreen" : (abs < 1.0f ? "LevelYellow" : "LevelRed");
        }

        private void AdjWb(int dr, int dg, int db, bool reset)
        {
            SlitCameraStream cam = ActiveCamera;
            if (cam == null) return;
            if (reset)
            {
                cam.WhiteBalanceRed = 0;
                cam.WhiteBalanceGreen = 0;
                cam.WhiteBalanceBlue = 0;
            }
            else
            {
                cam.WhiteBalanceRed = Math.Max(-80, Math.Min(80, cam.WhiteBalanceRed + dr));
                cam.WhiteBalanceGreen = Math.Max(-80, Math.Min(80, cam.WhiteBalanceGreen + dg));
                cam.WhiteBalanceBlue = Math.Max(-80, Math.Min(80, cam.WhiteBalanceBlue + db));
            }
            SlitScanEngine.UpdateDisplayBitmap(cam);
            InvalidateCanvas();
            if (CameraSettingsChanged != null) CameraSettingsChanged(this, EventArgs.Empty);
        }

        public void OpenCameraSettings()
        {
            SlitCameraStream cam = ActiveCamera;
            if (cam == null)
            {
                MessageBox.Show(this, "No camera is currently active in the loaded event.", "Hardware Control", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (CameraSettingsDialog dlg = new CameraSettingsDialog(_document, cam))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshCameraGrid();
                    SlitScanEngine.UpdateDisplayBitmap(cam);
                    InvalidateCanvas();
                    if (CameraSettingsChanged != null) CameraSettingsChanged(this, EventArgs.Empty);
                }
            }
        }

        public void RefreshCameraGrid()
        {
            _dgvCameras.Rows.Clear();
            if (_document == null) return;
            for (int i = 0; i < _document.Cameras.Count; i++)
            {
                SlitCameraStream c = _document.Cameras[i];
                CameraSettingsProfile p = c.Profile ?? new CameraSettingsProfile();
                string capStr = c.IsEnabled ? (_document.IsCapturing ? "Capturing" : "Manual") : "Disabled";
                _dgvCameras.Rows.Add(
                    c.DisplayName,
                    capStr,
                    c.SampleRateHz.ToString(CultureInfo.InvariantCulture),
                    c.FrameHeightPx.ToString(CultureInfo.InvariantCulture),
                    p.PixelMode,
                    string.Format(CultureInfo.InvariantCulture, "{0} ({1})", p.AgcMaxGain, p.AgcBrightness),
                    c.Orientation == StripOrientation.RightToLeft ? "Right" : "Left",
                    "PoE / 100%",
                    p.TimerExtSync ? "Ext" : "Int"
                );
            }
            if (_dgvCameras.Rows.Count > 0 && _selectedCamIndex < _dgvCameras.Rows.Count)
            {
                _dgvCameras.Rows[_selectedCamIndex].Selected = true;
            }
        }

        private void InvalidateCanvas()
        {
            if (_alignCanvas != null) _alignCanvas.Invalidate();
        }

        private void PaintAlignmentCanvas(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = _alignCanvas.ClientRectangle;
            g.Clear(Color.FromArgb(24, 30, 42));

            SlitCameraStream cam = ActiveCamera;
            if (cam == null)
            {
                TextRenderer.DrawText(g, "No camera feed selected. Load an event or sample race.", Font, r, Color.Silver,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            // Left vertical Enhance/Lane bar matching Hardware Control screenshots
            int barW = 24;
            using (SolidBrush br = new SolidBrush(Color.FromArgb(212, 208, 200)))
            {
                g.FillRectangle(br, 0, 0, barW, r.Height);
            }
            Rectangle canvas = new Rectangle(barW, 0, Math.Max(40, r.Width - barW), r.Height);

            float scale = _alignZoom / 100f;
            GraphicsState state = g.Save();
            g.SetClip(canvas);
            g.TranslateTransform(canvas.Left + canvas.Width * 0.5f + _panX, canvas.Top + canvas.Height * 0.5f + _panY);
            g.RotateTransform(_tiltDeg);
            g.ScaleTransform(scale, scale);

            float halfW = canvas.Width * 0.5f;
            float halfH = canvas.Height * 0.5f;

            if (_align2DOn || cam.IsVideo)
            {
                // Draw 2D EasyAlign view (video frame or track finish-line visualization)
                Bitmap bmp = cam.IsVideo ? cam.GetVideoFrame(cam.ActiveVideoFrameIndex) : (cam.DisplayStripBitmap ?? cam.RawStripBitmap);
                if (bmp != null)
                {
                    g.DrawImage(bmp, new RectangleF(-halfW, -halfH, canvas.Width, canvas.Height));
                }
                // Classic FinishLynx EasyAlign green calibration grid + red vertical finish-line slit crosshair
                using (Pen gridPen = new Pen(Color.FromArgb(130, 0, 255, 128), 1f))
                {
                    int lanes = _document != null ? Math.Max(4, _document.LaneCount) : 8;
                    for (int ln = 0; ln <= lanes; ln++)
                    {
                        float ly = -halfH * 0.7f + (ln / (float)lanes) * (halfH * 1.4f);
                        g.DrawLine(gridPen, -halfW, ly, halfW, ly);
                    }
                }
                using (Pen slitPen = new Pen(Color.FromArgb(230, 255, 50, 50), 2f))
                {
                    g.DrawLine(slitPen, 0f, -halfH, 0f, halfH);
                }
            }
            else if (_align1DOn)
            {
                // 1D Vertical Slit Waveform & Live Intensity Profile
                using (SolidBrush bg = new SolidBrush(Color.Black))
                {
                    g.FillRectangle(bg, -halfW, -halfH, canvas.Width, canvas.Height);
                }
                using (Pen gridPen = new Pen(Color.FromArgb(60, 0, 255, 0), 1f))
                using (Pen wavePen = new Pen(Color.Lime, 2f))
                {
                    for (int k = -3; k <= 3; k++)
                    {
                        g.DrawLine(gridPen, -halfW, k * 40f, halfW, k * 40f);
                    }
                    Bitmap strip = cam.DisplayStripBitmap ?? cam.RawStripBitmap;
                    if (strip != null && strip.Height > 2)
                    {
                        int col = Math.Min(strip.Width - 1, Math.Max(0, strip.Width / 2));
                        PointF[] pts = new PointF[Math.Min(160, strip.Height)];
                        for (int i = 0; i < pts.Length; i++)
                        {
                            int sy = (int)((i / (float)(pts.Length - 1)) * (strip.Height - 1));
                            Color px = strip.GetPixel(col, sy);
                            float lum = (px.R + px.G + px.B) / (3f * 255f);
                            float pxX = -halfW * 0.7f + lum * canvas.Width * 0.65f;
                            float pyY = -halfH + (i / (float)(pts.Length - 1)) * canvas.Height;
                            pts[i] = new PointF(pxX, pyY);
                        }
                        g.DrawLines(wavePen, pts);
                    }
                }
            }
            g.Restore(state);

            // Overlay HUD status in alignment view
            string hud = string.Format(CultureInfo.InvariantCulture,
                "{0} | {1} | Zoom: {2}% | Tilt: {3:+0.0;-0.0;0.0} deg | Iris: {4}{5}% | Focus: {6} | WB R:{7} G:{8} B:{9}{10}{11}",
                cam.DisplayName,
                _align2DOn ? "2D EasyAlign" : (_align1DOn ? "1D Slit Align" : "Offline"),
                _alignZoom,
                _tiltDeg,
                _autoIris ? "Auto " : "",
                _irisLevel,
                _focusOffset,
                cam.WhiteBalanceRed, cam.WhiteBalanceGreen, cam.WhiteBalanceBlue,
                _evfOn ? " [EVF]" : "",
                _aecOn ? " [AEC]" : "");
            using (SolidBrush pill = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
            using (SolidBrush fg = new SolidBrush(Color.Lime))
            {
                g.FillRectangle(pill, canvas.Left + 8, canvas.Bottom - 26, canvas.Width - 16, 20);
                g.DrawString(hud, Font, fg, canvas.Left + 12, canvas.Bottom - 24);
            }

            if (_focusHelperOn)
            {
                int sharpPct = Math.Max(45, 98 - Math.Abs(_focusOffset) * 8);
                string focStr = string.Format(CultureInfo.InvariantCulture, "Focus Edge Sharpness: {0}%", sharpPct);
                using (SolidBrush fb = new SolidBrush(Color.FromArgb(180, 15, 23, 42)))
                using (SolidBrush ff = new SolidBrush(sharpPct > 85 ? Color.LightGreen : Color.Gold))
                {
                    g.FillRectangle(fb, canvas.Right - 190, 8, 180, 20);
                    g.DrawString(focStr, Font, ff, canvas.Right - 184, 10);
                }
            }
        }
    }
}
