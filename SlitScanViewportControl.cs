using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;
using OptiFinish.Core;

namespace OptiFinish.UI
{
    public sealed class HairlineChangedEventArgs : EventArgs
    {
        public double TimeSeconds { get; private set; }
        public float VerticalRatio { get; private set; }
        public int Lane { get; private set; }

        public HairlineChangedEventArgs(double timeSeconds, float verticalRatio, int lane)
        {
            TimeSeconds = timeSeconds;
            VerticalRatio = verticalRatio;
            Lane = lane;
        }
    }

    /// <summary>
    /// High-precision, double-buffered slit-scan photo-finish evaluation canvas
    /// with time ruler, lane gutter, synchronized hairline + crosshair, zoom/pan, and evaluated line overlays.
    /// </summary>
    public sealed class SlitScanViewportControl : Control
    {
        private const int RulerHeightPx = 28;
        private const int LaneGutterWidthPx = 42;
        private const int ScrollBarHeightPx = 12;

        // Cached GDI resources to avoid per-frame handle allocations in OnPaint
        private static readonly Font EmptyStateFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        private static readonly SolidBrush EmptyStateBrush = new SolidBrush(Color.FromArgb(148, 163, 184));
        private static readonly StringFormat CenteredFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        private static readonly Pen LaneGridPen = CreateDashedPen(Color.FromArgb(110, 56, 189, 248), 1f);
        private static readonly Pen EvalHashPen = new Pen(Color.FromArgb(215, 244, 63, 94), 1.6f);
        private static readonly Font BadgeFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        private static readonly SolidBrush BadgeBgBrush = new SolidBrush(Color.FromArgb(215, 15, 23, 42));
        private static readonly Pen BadgeBorderPen = new Pen(Color.FromArgb(244, 63, 94), 1f);
        private static readonly SolidBrush BadgeFgBrush = new SolidBrush(Color.White);
        private static readonly Font AnnotationFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly SolidBrush AnnotationBgBrush = new SolidBrush(Color.FromArgb(220, 234, 179, 8));
        private static readonly SolidBrush AnnotationFgBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
        private static readonly SolidBrush ActiveLaneHighlightBrush = new SolidBrush(Color.FromArgb(24, 250, 204, 21));
        private static readonly Pen HairlineShadowPen = new Pen(Color.FromArgb(160, 0, 0, 0), 3f);
        private static readonly Pen HairlineGoldPen = new Pen(Color.FromArgb(250, 204, 21), 1.5f);
        private static readonly Pen CrosshairShadowPen = new Pen(Color.FromArgb(170, 0, 0, 0), 3f);
        private static readonly Pen CrosshairCyanPen = new Pen(Color.FromArgb(56, 189, 248), 1.6f);
        private static readonly Font HudFont = new Font("Consolas", 8.5f, FontStyle.Bold);
        private static readonly SolidBrush HudBgBrush = new SolidBrush(Color.FromArgb(235, 15, 23, 42));
        private static readonly Pen HudBorderPen = new Pen(Color.FromArgb(250, 204, 21), 1.2f);
        private static readonly SolidBrush HudFgBrush = new SolidBrush(Color.FromArgb(254, 240, 138));
        private static readonly SolidBrush ZoomBoxFillBrush = new SolidBrush(Color.FromArgb(50, 56, 189, 248));
        private static readonly Pen ZoomBoxBorderPen = new Pen(Color.FromArgb(220, 56, 189, 248), 1.5f);
        private static readonly SolidBrush DarkNavyBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
        private static readonly Pen SubtleBorderPen = new Pen(Color.FromArgb(51, 65, 85), 1f);
        private static readonly Font LanePillFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly SolidBrush HoveredPillBgBrush = new SolidBrush(Color.FromArgb(250, 204, 21));
        private static readonly SolidBrush NormalPillFgBrush = new SolidBrush(Color.FromArgb(203, 213, 225));
        private static readonly SolidBrush RulerBgBrush = new SolidBrush(Color.FromArgb(30, 41, 59));
        private static readonly Font CamBadgeFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        private static readonly SolidBrush CamBadgeFgBrush = new SolidBrush(Color.FromArgb(56, 189, 248));
        private static readonly Pen MinorTickPen = new Pen(Color.FromArgb(100, 148, 163, 184), 1f);
        private static readonly Pen MajorTickPen = new Pen(Color.FromArgb(226, 232, 240), 1.2f);
        private static readonly Pen SplitMarkerPen = new Pen(Color.FromArgb(200, 34, 197, 94), 1.4f);
        private static readonly Font TickLabelFont = new Font("Consolas", 8f, FontStyle.Bold);
        private static readonly SolidBrush TickLabelBrush = new SolidBrush(Color.FromArgb(241, 245, 249));
        private static readonly SolidBrush ScrollThumbBrush = new SolidBrush(Color.FromArgb(71, 85, 105));

        private static Pen CreateDashedPen(Color color, float width)
        {
            Pen p = new Pen(color, width);
            p.DashStyle = DashStyle.Dash;
            return p;
        }

        private RaceEventDocument _document;
        private int _cameraIndex;

        private double _zoomFactor = 1.0;        // 0.25 .. 16.0
        private double _scrollColumnOffset = 0.0; // Leftmost visible strip column
        private double _cursorTimeSeconds = 12.8432;
        private float _cursorVerticalRatio = 0.5f;

        private bool _showLaneGrid = true;
        private bool _showEvaluatedLines = true;
        private bool _autoScrollCapture = true;

        private bool _isDraggingHairline;
        private bool _isPanning;
        private Point _panStartMouse;
        private double _panStartColumnOffset;

        private bool _isSelectingZoomBox;
        private Point _zoomBoxStart;
        private Point _zoomBoxCurrent;
        private bool _hasActiveSelectionBox;
        private bool _fixedSelectionClickMode;

        // IdentiLynx 2D Video panning state
        private PointF _videoPanOffset = PointF.Empty;
        private Point _videoPanStartMouse = Point.Empty;
        private PointF _videoPanStartOffset = PointF.Empty;
        private bool _isPanningVideo;

        public event EventHandler<HairlineChangedEventArgs> HairlineMoved;
        public event EventHandler<HairlineChangedEventArgs> QuickEvaluateRequested;
        public event EventHandler<double> SetZeroOriginRequested;
        public event EventHandler<EventArgs> ExportSelectionRequested;
        public event EventHandler<EventArgs> ExportVideoSelectionRequested;
        public event EventHandler<EventArgs> PrintSelectionRequested;
        public event EventHandler<EventArgs> PrintBitmapSelectionRequested;
        public event EventHandler<EventArgs> SetScoreboardImageRequested;
        public event EventHandler<EventArgs> OpenAdjustmentsRequested;

        public RaceEventDocument Document
        {
            get { return _document; }
            set
            {
                _document = value;
                ClampScrollAndCursor();
                Invalidate();
            }
        }

        public int CameraIndex
        {
            get { return _cameraIndex; }
            set
            {
                _cameraIndex = value;
                Invalidate();
            }
        }

        public SlitCameraStream ActiveStream
        {
            get
            {
                if (_document == null || _cameraIndex < 0 || _cameraIndex >= _document.Cameras.Count) return null;
                return _document.Cameras[_cameraIndex];
            }
        }

        public double ZoomFactor
        {
            get { return _zoomFactor; }
            set
            {
                double clamped = Math.Max(0.25, Math.Min(16.0, value));
                if (Math.Abs(_zoomFactor - clamped) > 0.001)
                {
                    SlitCameraStream cam = ActiveStream;
                    double centerCol = cam != null ? cam.TimeSecondsToColumn(_cursorTimeSeconds) : _scrollColumnOffset;
                    double viewColsBefore = GetVisibleStripPixelWidth() / _zoomFactor;
                    _zoomFactor = clamped;
                    double viewColsAfter = GetVisibleStripPixelWidth() / _zoomFactor;
                    _scrollColumnOffset = centerCol - (viewColsAfter * 0.5);
                    ClampScrollAndCursor();
                    Invalidate();
                }
            }
        }

        public double CursorTimeSeconds
        {
            get { return _cursorTimeSeconds; }
            set
            {
                _cursorTimeSeconds = value;
                SlitCameraStream cam = ActiveStream;
                if (cam != null && cam.IsVideo)
                {
                    int bestIdx;
                    cam.GetVideoFrameForTime(value, out bestIdx);
                    cam.ActiveVideoFrameIndex = bestIdx;
                }
                Invalidate();
            }
        }

        public float CursorVerticalRatio
        {
            get { return _cursorVerticalRatio; }
            set
            {
                _cursorVerticalRatio = Math.Max(0.0f, Math.Min(1.0f, value));
                Invalidate();
            }
        }

        public bool ShowLaneGrid
        {
            get { return _showLaneGrid; }
            set { _showLaneGrid = value; Invalidate(); }
        }

        public bool ShowEvaluatedLines
        {
            get { return _showEvaluatedLines; }
            set { _showEvaluatedLines = value; Invalidate(); }
        }

        public bool AutoScrollCapture
        {
            get { return _autoScrollCapture; }
            set { _autoScrollCapture = value; }
        }

        public SlitScanViewportControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);
            BackColor = Color.FromArgb(15, 23, 42);
            Cursor = Cursors.Cross;
        }

        public void CenterOnTime(double timeSeconds)
        {
            _cursorTimeSeconds = timeSeconds;
            SlitCameraStream cam = ActiveStream;
            if (cam != null)
            {
                if (cam.IsVideo)
                {
                    int bestIdx;
                    cam.GetVideoFrameForTime(timeSeconds, out bestIdx);
                    cam.ActiveVideoFrameIndex = bestIdx;
                    Invalidate();
                    return;
                }
                double targetCol = cam.TimeSecondsToColumn(timeSeconds);
                double visibleCols = GetVisibleStripPixelWidth() / _zoomFactor;
                _scrollColumnOffset = targetCol - (visibleCols * 0.5);
                ClampScrollAndCursor();
            }
            Invalidate();
        }

        public void StepVideoFrame(int delta)
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null || !cam.IsVideo || cam.TotalVideoFrames == 0) return;
            int newIdx = Math.Max(0, Math.Min(cam.TotalVideoFrames - 1, cam.ActiveVideoFrameIndex + delta));
            cam.ActiveVideoFrameIndex = newIdx;
            if (cam.VideoFrameTimestamps != null && newIdx < cam.VideoFrameTimestamps.Count)
            {
                _cursorTimeSeconds = cam.VideoFrameTimestamps[newIdx];
            }
            RaiseHairlineMoved();
            Invalidate();
        }

        public void JumpToVideoFrame(int index)
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null || !cam.IsVideo || cam.TotalVideoFrames == 0) return;
            int newIdx = Math.Max(0, Math.Min(cam.TotalVideoFrames - 1, index));
            cam.ActiveVideoFrameIndex = newIdx;
            if (cam.VideoFrameTimestamps != null && newIdx < cam.VideoFrameTimestamps.Count)
            {
                _cursorTimeSeconds = cam.VideoFrameTimestamps[newIdx];
            }
            RaiseHairlineMoved();
            Invalidate();
        }

        public void StepHairlineColumns(int deltaColumns)
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null || cam.SampleRateHz <= 0) return;

            double currentCol = cam.TimeSecondsToColumn(_cursorTimeSeconds);
            double nextCol = Math.Max(0, Math.Min(Math.Max(1, cam.TotalColumns - 1), currentCol + deltaColumns));
            _cursorTimeSeconds = cam.ColumnToTimeSeconds(nextCol);

            // Keep hairline inside visible viewport
            double visibleCols = GetVisibleStripPixelWidth() / _zoomFactor;
            if (nextCol < _scrollColumnOffset + visibleCols * 0.08)
            {
                _scrollColumnOffset = Math.Max(0, nextCol - visibleCols * 0.25);
            }
            else if (nextCol > _scrollColumnOffset + visibleCols * 0.92)
            {
                _scrollColumnOffset = nextCol - visibleCols * 0.75;
            }
            ClampScrollAndCursor();
            RaiseHairlineMoved();
            Invalidate();
        }

        public void PageScroll(int direction)
        {
            double visibleCols = GetVisibleStripPixelWidth() / _zoomFactor;
            _scrollColumnOffset += direction * (visibleCols * 0.80);
            ClampScrollAndCursor();
            Invalidate();
        }

        public void JumpToStart()
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null) return;
            _scrollColumnOffset = 0;
            _cursorTimeSeconds = cam.ColumnToTimeSeconds(0);
            ClampScrollAndCursor();
            RaiseHairlineMoved();
            Invalidate();
        }

        public void JumpToEnd()
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null) return;
            _cursorTimeSeconds = cam.ColumnToTimeSeconds(Math.Max(0, cam.TotalColumns - 1));
            double visibleCols = GetVisibleStripPixelWidth() / _zoomFactor;
            _scrollColumnOffset = Math.Max(0, cam.TotalColumns - visibleCols);
            ClampScrollAndCursor();
            RaiseHairlineMoved();
            Invalidate();
        }

        private int GetVisibleStripPixelWidth()
        {
            return Math.Max(64, Width - LaneGutterWidthPx);
        }

        private Rectangle GetImageCanvasRect()
        {
            SlitCameraStream cam = ActiveStream;
            if (cam != null && cam.IsVideo)
            {
                return new Rectangle(0, 0, Math.Max(10, Width), Math.Max(10, Height - ScrollBarHeightPx));
            }
            return new Rectangle(
                LaneGutterWidthPx,
                RulerHeightPx,
                Math.Max(10, Width - LaneGutterWidthPx),
                Math.Max(10, Height - RulerHeightPx - ScrollBarHeightPx));
        }

        private void ClampScrollAndCursor()
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null || cam.TotalColumns <= 0)
            {
                _scrollColumnOffset = 0;
                return;
            }

            double minCol, maxCol;
            GetEffectiveColumnBounds(cam, out minCol, out maxCol);
            double visibleCols = GetVisibleStripPixelWidth() / _zoomFactor;
            double maxOffset = Math.Max(minCol, maxCol - visibleCols * 0.35);
            if (_scrollColumnOffset < minCol) _scrollColumnOffset = minCol;
            if (_scrollColumnOffset > maxOffset) _scrollColumnOffset = maxOffset;
        }

        private CropWindow _lastBoxSelectionCrop;
        public event EventHandler<EventArgs> SelectionActionCompleted;

        public bool HasActiveSelectionBox
        {
            get { return _hasActiveSelectionBox && _lastBoxSelectionCrop != null; }
        }

        public CropWindow ActiveSelectionCrop
        {
            get { return _hasActiveSelectionBox ? _lastBoxSelectionCrop : null; }
        }

        private void GetEffectiveColumnBounds(SlitCameraStream cam, out double minCol, out double maxCol)
        {
            minCol = 0.0;
            maxCol = cam != null ? Math.Max(1.0, (double)cam.TotalColumns) : 1.0;
            if (cam != null && cam.ActiveCrop != null)
            {
                minCol = Math.Max(0, Math.Min(cam.TotalColumns - 2, cam.ActiveCrop.StartCol));
                maxCol = Math.Max(minCol + 2, Math.Min(cam.TotalColumns, cam.ActiveCrop.EndCol));
            }
        }

        public CropWindow GetEffectiveExportCrop()
        {
            if (_hasActiveSelectionBox && _lastBoxSelectionCrop != null)
            {
                return _lastBoxSelectionCrop;
            }
            SlitCameraStream cam = ActiveStream;
            if (cam != null && cam.ActiveCrop != null)
            {
                return cam.ActiveCrop;
            }
            if (_document != null && cam != null && cam.TotalColumns > 1 &&
                (_document.MarkedStartFrameTime.HasValue || _document.MarkedEndFrameTime.HasValue))
            {
                double t0 = _document.MarkedStartFrameTime ?? cam.ColumnToTimeSeconds(0);
                double t1 = _document.MarkedEndFrameTime ?? cam.ColumnToTimeSeconds(cam.TotalColumns - 1);
                double cA = cam.TimeSecondsToColumn(t0);
                double cB = cam.TimeSecondsToColumn(t1);
                int sCol = Math.Max(0, (int)Math.Floor(Math.Min(cA, cB)));
                int eCol = Math.Min(cam.TotalColumns - 1, (int)Math.Ceiling(Math.Max(cA, cB)));
                if (eCol > sCol + 1)
                {
                    return new CropWindow(sCol, eCol, 0f, 1f);
                }
            }
            return null;
        }

        public void ClearSelectionBox()
        {
            _hasActiveSelectionBox = false;
            _lastBoxSelectionCrop = null;
            _isSelectingZoomBox = false;
            Invalidate();
            if (SelectionActionCompleted != null) SelectionActionCompleted(this, EventArgs.Empty);
        }

        public void SelectAllStrip()
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null || cam.IsVideo || cam.TotalColumns < 2) return;
            double minCol, maxCol;
            GetEffectiveColumnBounds(cam, out minCol, out maxCol);
            float vTop = cam.ActiveCrop != null ? cam.ActiveCrop.TopRatio : 0f;
            float vBot = cam.ActiveCrop != null ? cam.ActiveCrop.BottomRatio : 1f;
            _lastBoxSelectionCrop = new CropWindow((int)minCol, (int)Math.Max(minCol + 1, maxCol - 1), vTop, vBot);
            _hasActiveSelectionBox = true;
            Invalidate();
            if (SelectionActionCompleted != null) SelectionActionCompleted(this, EventArgs.Empty);
        }

        public void ZoomToActiveSelection()
        {
            SlitCameraStream cam = ActiveStream;
            CropWindow sel = _lastBoxSelectionCrop;
            if (cam == null || sel == null) return;
            Rectangle imgRect = GetImageCanvasRect();
            double colSpan = Math.Max(10.0, sel.EndCol - sel.StartCol);
            _zoomFactor = Math.Max(0.25, Math.Min(16.0, imgRect.Width / colSpan));
            _scrollColumnOffset = sel.StartCol;
            ClampScrollAndCursor();
            Invalidate();
        }

        public void CropToCurrentZoomOrSelection()
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null || cam.IsVideo || cam.TotalColumns < 10) return;

            CropWindow crop = (_hasActiveSelectionBox && _lastBoxSelectionCrop != null) ? _lastBoxSelectionCrop : null;
            if (crop == null && _document != null && (_document.MarkedStartFrameTime.HasValue || _document.MarkedEndFrameTime.HasValue))
            {
                crop = GetEffectiveExportCrop();
            }
            if (crop == null)
            {
                Rectangle imgRect = GetImageCanvasRect();
                double visibleCols = Math.Max(16.0, imgRect.Width / Math.Max(0.05, _zoomFactor));
                int s = Math.Max(0, (int)Math.Floor(_scrollColumnOffset));
                int e = Math.Min(cam.TotalColumns - 1, (int)Math.Ceiling(s + visibleCols));
                if (e - s < 10) return;
                float vTop = cam.ActiveCrop != null ? cam.ActiveCrop.TopRatio : 0f;
                float vBot = cam.ActiveCrop != null ? cam.ActiveCrop.BottomRatio : 1f;
                crop = new CropWindow(s, e, vTop, vBot);
            }

            CropWindow prevCrop = cam.ActiveCrop;
            if (cam.ActiveCrop != null) cam.CropUndoStack.Push(cam.ActiveCrop);
            if (_document != null)
            {
                _document.UndoActions.Push(delegate
                {
                    cam.ActiveCrop = prevCrop;
                    _scrollColumnOffset = prevCrop != null ? prevCrop.StartCol : 0.0;
                    ClampScrollAndCursor();
                    Invalidate();
                    if (SelectionActionCompleted != null) SelectionActionCompleted(this, EventArgs.Empty);
                });
            }

            cam.ActiveCrop = crop;
            _hasActiveSelectionBox = false;
            _lastBoxSelectionCrop = null;

            Rectangle viewRect = GetImageCanvasRect();
            double newSpan = Math.Max(16.0, crop.EndCol - crop.StartCol);
            _zoomFactor = Math.Max(0.25, Math.Min(16.0, viewRect.Width / newSpan));
            _scrollColumnOffset = crop.StartCol;
            _cursorTimeSeconds = cam.ColumnToTimeSeconds(0.5 * (crop.StartCol + crop.EndCol));
            ClampScrollAndCursor();
            Invalidate();
            if (SelectionActionCompleted != null) SelectionActionCompleted(this, EventArgs.Empty);
        }

        public void KeepSelectionCrop()
        {
            CropToCurrentZoomOrSelection();
        }

        public void UncropActiveStream()
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null) return;
            SlitScanEngine.UncropStream(cam);
            _hasActiveSelectionBox = false;
            _lastBoxSelectionCrop = null;
            _scrollColumnOffset = cam.ActiveCrop != null ? cam.ActiveCrop.StartCol : 0.0;
            ClampScrollAndCursor();
            Invalidate();
            if (SelectionActionCompleted != null) SelectionActionCompleted(this, EventArgs.Empty);
        }

        public void AutocropActiveStream(int leaderCols, int trailerCols)
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null || cam.IsVideo) return;
            CropWindow prevCrop = cam.ActiveCrop;
            SlitScanEngine.AutocropStream(cam, leaderCols, trailerCols);
            _hasActiveSelectionBox = false;
            _lastBoxSelectionCrop = null;
            if (_document != null && cam.ActiveCrop != prevCrop)
            {
                _document.UndoActions.Push(delegate
                {
                    cam.ActiveCrop = prevCrop;
                    _scrollColumnOffset = prevCrop != null ? prevCrop.StartCol : 0.0;
                    ClampScrollAndCursor();
                    Invalidate();
                    if (SelectionActionCompleted != null) SelectionActionCompleted(this, EventArgs.Empty);
                });
            }
            if (cam.ActiveCrop != null)
            {
                _scrollColumnOffset = cam.ActiveCrop.StartCol;
                ClampScrollAndCursor();
            }
            Invalidate();
            if (SelectionActionCompleted != null) SelectionActionCompleted(this, EventArgs.Empty);
        }

        public void SampleWhiteBalanceAtCursor()
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null || cam.IsVideo) return;
            SlitScanEngine.SampleWhiteBalanceAt(cam, _cursorTimeSeconds, _cursorVerticalRatio);
            Invalidate();
            if (SelectionActionCompleted != null) SelectionActionCompleted(this, EventArgs.Empty);
        }

        public void ToggleEnhanceAtActiveBand()
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null || cam.IsVideo) return;
            if (_hasActiveSelectionBox && _lastBoxSelectionCrop != null)
            {
                cam.EnhanceRegions.Add(new EnhanceRegion(_lastBoxSelectionCrop.TopRatio, _lastBoxSelectionCrop.BottomRatio));
            }
            else if (_lastBoxSelectionCrop != null)
            {
                cam.EnhanceRegions.Add(new EnhanceRegion(_lastBoxSelectionCrop.TopRatio, _lastBoxSelectionCrop.BottomRatio));
                _lastBoxSelectionCrop = null;
            }
            else
            {
                cam.EnhanceEnabled = !cam.EnhanceEnabled;
            }
            SlitScanEngine.UpdateDisplayBitmap(cam);
            Invalidate();
            if (SelectionActionCompleted != null) SelectionActionCompleted(this, EventArgs.Empty);
        }

        public void ClearEnhance()
        {
            SlitCameraStream cam = ActiveStream;
            if (cam == null) return;
            cam.EnhanceEnabled = false;
            cam.EnhanceRegions.Clear();
            SlitScanEngine.UpdateDisplayBitmap(cam);
            Invalidate();
            if (SelectionActionCompleted != null) SelectionActionCompleted(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.FromArgb(15, 23, 42));

            SlitCameraStream cam = ActiveStream;
            Rectangle imgRect = GetImageCanvasRect();
            bool hasRightLaneBar = (_document != null && (_document.LaneBarSide == LaneBarPosition.Right || _document.LaneBarSide == LaneBarPosition.Both));
            if (hasRightLaneBar && imgRect.Width > LaneGutterWidthPx * 2)
            {
                imgRect = new Rectangle(
                    _document.LaneBarSide == LaneBarPosition.Right ? 0 : LaneGutterWidthPx,
                    imgRect.Top,
                    _document.LaneBarSide == LaneBarPosition.Right ? (Width - LaneGutterWidthPx) : (imgRect.Width - LaneGutterWidthPx),
                    imgRect.Height);
            }

            if (_document == null || cam == null || (cam.RawStripBitmap == null && !cam.IsVideo))
            {
                g.DrawString("No Slit-Scan Image Loaded", EmptyStateFont, EmptyStateBrush, ClientRectangle, CenteredFormat);
                return;
            }

            if (cam.IsVideo)
            {
                DrawVideoFrameMode(g, cam, imgRect);
                return;
            }

            Bitmap strip = cam.DisplayStripBitmap ?? cam.RawStripBitmap;

            double minCol, maxCol;
            GetEffectiveColumnBounds(cam, out minCol, out maxCol);

            // Vertical crop bounds (Keep Selection)
            float vTop = cam.ActiveCrop != null ? Math.Max(0f, Math.Min(0.95f, cam.ActiveCrop.TopRatio)) : 0f;
            float vBot = cam.ActiveCrop != null ? Math.Max(vTop + 0.05f, Math.Min(1f, cam.ActiveCrop.BottomRatio)) : 1f;

            // 1. Draw visible slice of the slit-scan bitmap (respecting active Crop & SmoothZoom)
            double visibleCols = imgRect.Width / _zoomFactor;
            double drawStartCol = Math.Max(minCol, _scrollColumnOffset);
            float srcX = (float)drawStartCol;
            float srcW = (float)Math.Max(0.0, Math.Min(maxCol - drawStartCol, visibleCols));
            float dstW = (float)(srcW * _zoomFactor);

            if (srcW > 0 && dstW > 0)
            {
                InterpolationMode prevInterp = g.InterpolationMode;
                g.InterpolationMode = _document.SmoothZoom
                    ? InterpolationMode.HighQualityBicubic
                    : (_zoomFactor >= 2.5 ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear);
                g.PixelOffsetMode = PixelOffsetMode.Half;

                float srcY = vTop * strip.Height;
                float srcH = Math.Max(1f, (vBot - vTop) * strip.Height);
                RectangleF dstRect = new RectangleF(imgRect.Left, imgRect.Top, dstW, imgRect.Height);
                RectangleF srcRect = new RectangleF(srcX, srcY, srcW, srcH);
                g.DrawImage(strip, dstRect, srcRect, GraphicsUnit.Pixel);

                g.InterpolationMode = prevInterp;
                g.PixelOffsetMode = PixelOffsetMode.Default;
            }

            // 1b. Draw Marked Start / End Frame region shading
            if (_document.MarkedStartFrameTime.HasValue || _document.MarkedEndFrameTime.HasValue)
            {
                double stT = _document.MarkedStartFrameTime ?? cam.ColumnToTimeSeconds(minCol);
                double enT = _document.MarkedEndFrameTime ?? cam.ColumnToTimeSeconds(maxCol - 1);
                float x1 = imgRect.Left + (float)((cam.TimeSecondsToColumn(stT) - _scrollColumnOffset) * _zoomFactor);
                float x2 = imgRect.Left + (float)((cam.TimeSecondsToColumn(enT) - _scrollColumnOffset) * _zoomFactor);
                float fLeft = Math.Max(imgRect.Left, Math.Min(x1, x2));
                float fRight = Math.Min(imgRect.Right, Math.Max(x1, x2));
                if (fRight > fLeft)
                {
                    using (SolidBrush frameShade = new SolidBrush(Color.FromArgb(40, 34, 197, 94)))
                    using (Pen framePen = new Pen(Color.Lime, 1.5f) { DashStyle = DashStyle.Dash })
                    {
                        g.FillRectangle(frameShade, fLeft, imgRect.Top, fRight - fLeft, imgRect.Height);
                        g.DrawLine(framePen, x1, imgRect.Top, x1, imgRect.Bottom);
                        g.DrawLine(framePen, x2, imgRect.Top, x2, imgRect.Bottom);
                    }
                }
            }

            // 2. Draw Horizontal Track Lane Overlay Lines (supports perspective-calibrated boundaries from .evn)
            float[] calibBounds = cam.CalibratedLaneBoundaries ?? _document.CalibratedLaneBoundaries;
            bool hasCalibLanes = (calibBounds != null && calibBounds.Length >= 2);
            int lanes = hasCalibLanes ? (calibBounds.Length - 1) : Math.Max(1, _document.LaneCount);

            if (_showLaneGrid)
            {
                for (int ln = 0; ln <= lanes; ln++)
                {
                    float r = hasCalibLanes
                        ? calibBounds[ln]
                        : (_document.LaneTopRatio + (_document.LaneBottomRatio - _document.LaneTopRatio) * ((float)ln / Math.Max(1, lanes)));
                    float ly = StripRatioToScreenY(r, imgRect, vTop, vBot);
                    if (ly >= imgRect.Top && ly <= imgRect.Bottom)
                    {
                        g.DrawLine(LaneGridPen, imgRect.Left, ly, imgRect.Right, ly);
                    }
                }
            }

            // 2b. Time Gap Lines
            if (_document.ShowTimeGapLines && _document.TimeGapIntervalSeconds >= 0.05)
            {
                double t0 = cam.ColumnToTimeSeconds(drawStartCol);
                double t1 = cam.ColumnToTimeSeconds(drawStartCol + visibleCols);
                double tLo = Math.Min(t0, t1);
                double tHi = Math.Max(t0, t1);
                double step = _document.TimeGapIntervalSeconds;
                double firstGap = Math.Ceiling(tLo / step) * step;
                using (Pen gapPen = new Pen(Color.FromArgb(140, 56, 189, 248), 1f) { DashStyle = DashStyle.Dot })
                {
                    for (double gt = firstGap; gt <= tHi; gt += step)
                    {
                        float gx = imgRect.Left + (float)((cam.TimeSecondsToColumn(gt) - _scrollColumnOffset) * _zoomFactor);
                        if (gx >= imgRect.Left && gx <= imgRect.Right)
                        {
                            g.DrawLine(gapPen, gx, imgRect.Top, gx, imgRect.Bottom);
                        }
                    }
                }
            }

            // 3. Draw Evaluated Competitor Hash Lines & Callout Badges
            if (_showEvaluatedLines)
            {
                using (Pen customShowPen = new Pen(_document.ShowLinesColor.A > 0 ? _document.ShowLinesColor : Color.OrangeRed, 1.4f))
                {
                    Pen linePen = (_document.ShowLinesColor != Color.Yellow && _document.ShowLinesColor != Color.Black) ? customShowPen : EvalHashPen;
                    for (int i = 0; i < _document.Competitors.Count; i++)
                    {
                        CompetitorEntry c = _document.Competitors[i];
                        if (!c.HasValidNumericTime) continue;

                        double col = cam.TimeSecondsToColumn(c.RawTimeSeconds);
                        float sx = imgRect.Left + (float)((col - _scrollColumnOffset) * _zoomFactor);
                        if (sx < imgRect.Left || sx > imgRect.Right) continue;

                        g.DrawLine(linePen, sx, imgRect.Top, sx, imgRect.Bottom);

                        float sy = StripRatioToScreenY(c.EvaluatedVerticalRatio, imgRect, vTop, vBot);
                        if (sy < imgRect.Top - 16f || sy > imgRect.Bottom + 16f) continue;

                        string badgeText = string.Format(CultureInfo.InvariantCulture, "#{0} {1} {2}",
                            c.Place > 0 ? c.Place.ToString(CultureInfo.InvariantCulture) : "-",
                            !string.IsNullOrEmpty(c.LastName) ? c.LastName : ("Bib " + c.BibId),
                            c.GetFormattedOfficialTime(_document.Precision));

                        SizeF bsz = g.MeasureString(badgeText, BadgeFont);
                        float bx = Math.Min(imgRect.Right - bsz.Width - 10f, sx + 4f);
                        float by = Math.Max(imgRect.Top + 4f, Math.Min(imgRect.Bottom - bsz.Height - 6f, sy - bsz.Height * 0.5f));
                        RectangleF badgeRect = new RectangleF(bx, by, bsz.Width + 8f, bsz.Height + 3f);

                        g.FillRectangle(BadgeBgBrush, badgeRect);
                        g.DrawRectangle(BadgeBorderPen, badgeRect.X, badgeRect.Y, badgeRect.Width, badgeRect.Height);
                        g.DrawString(badgeText, BadgeFont, BadgeFgBrush, badgeRect.X + 4f, badgeRect.Y + 1.5f);
                    }
                }
            }

            // 4. Draw User Text Annotations + FinishLynx Overlays (ClosestTime, HashTime, EventName, LineLabels)
            for (int i = 0; i < _document.Annotations.Count; i++)
            {
                ImageTextAnnotation ann = _document.Annotations[i];
                double col = cam.TimeSecondsToColumn(ann.TimeSeconds);
                float sx = imgRect.Left + (float)((col - _scrollColumnOffset) * _zoomFactor);
                if (sx < imgRect.Left || sx > imgRect.Right) continue;
                float sy = StripRatioToScreenY(ann.VerticalRatio, imgRect, vTop, vBot);

                SizeF asz = g.MeasureString(ann.Text, AnnotationFont);
                g.FillRectangle(AnnotationBgBrush, sx, sy, asz.Width + 6f, asz.Height + 2f);
                g.DrawString(ann.Text, AnnotationFont, AnnotationFgBrush, sx + 3f, sy + 1f);
            }

            if (_document.Overlays != null && _document.Overlays.Count > 0)
            {
                for (int oi = 0; oi < _document.Overlays.Count; oi++)
                {
                    ImageOverlayItem ov = _document.Overlays[oi];
                    if (ov == null || !ov.Visible) continue;
                    int grpIdx = Math.Max(0, Math.Min(8, ov.GroupIndex));
                    if (_document.OverlayGroupVisible != null && !_document.OverlayGroupVisible[grpIdx]) continue;
                    double col = cam.TimeSecondsToColumn(ov.TimeSeconds);
                    float sx = imgRect.Left + (float)((col - _scrollColumnOffset) * _zoomFactor);
                    float sy = StripRatioToScreenY(ov.VerticalRatio, imgRect, vTop, vBot);
                    if (ov.Kind == ImageOverlayKind.EventName)
                    {
                        sx = imgRect.Left + 10f;
                        sy = imgRect.Top + 10f;
                    }
                    else if (sx < imgRect.Left - 80f || sx > imgRect.Right + 80f)
                    {
                        continue;
                    }
                    using (SolidBrush fg = new SolidBrush(ov.OverlayColor.A > 0 ? ov.OverlayColor : Color.Yellow))
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(190, 15, 23, 42)))
                    using (Font ovFont = new Font(!string.IsNullOrEmpty(ov.FontName) ? ov.FontName : "Arial", Math.Max(7f, Math.Min(28f, ov.FontSizePt > 0 ? ov.FontSizePt : 9f)), ov.FontBold ? FontStyle.Bold : FontStyle.Regular))
                    {
                        if (ov.Kind == ImageOverlayKind.ClosestTime || ov.Kind == ImageOverlayKind.HashTime || ov.ShowLine)
                        {
                            using (Pen hp = new Pen(fg.Color, 1.5f)) g.DrawLine(hp, sx, imgRect.Top, sx, imgRect.Bottom);
                        }
                        SizeF sz = g.MeasureString(ov.Text, ovFont);
                        g.FillRectangle(bg, sx, sy, sz.Width + 8f, sz.Height + 4f);
                        g.DrawString(ov.Text, ovFont, fg, sx + 4f, sy + 2f);
                    }
                }
            }

            if (_document.ShowEventNameOverlay)
            {
                string evBanner = string.Format(CultureInfo.InvariantCulture, "{0} ({1})",
                    _document.EventName, _document.GetEventCodeString());
                SizeF esz = g.MeasureString(evBanner, HudFont);
                using (SolidBrush eb = new SolidBrush(Color.FromArgb(195, 15, 23, 42)))
                {
                    g.FillRectangle(eb, imgRect.Left + 8f, imgRect.Top + 8f, esz.Width + 10f, esz.Height + 4f);
                    g.DrawString(evBanner, HudFont, HudFgBrush, imgRect.Left + 13f, imgRect.Top + 10f);
                }
            }

            // 5. Draw Active Precision Hairline (Vertical Hash Line + Horizontal Crosshair)
            double cursorCol = cam.TimeSecondsToColumn(_cursorTimeSeconds);
            float hairlineX = imgRect.Left + (float)((cursorCol - _scrollColumnOffset) * _zoomFactor);
            float crosshairY = StripRatioToScreenY(_cursorVerticalRatio, imgRect, vTop, vBot);
            int activeLane = _document.RatioToLane(_cursorVerticalRatio);

            if (hairlineX >= imgRect.Left && hairlineX <= imgRect.Right)
            {
                // Highlighted lane band under crosshair
                int clampedLane = Math.Max(1, Math.Min(lanes, activeLane));
                float rTopLane = hasCalibLanes
                    ? calibBounds[clampedLane - 1]
                    : (_document.LaneTopRatio + (_document.LaneBottomRatio - _document.LaneTopRatio) * ((float)(clampedLane - 1) / Math.Max(1, lanes)));
                float rBotLane = hasCalibLanes
                    ? calibBounds[clampedLane]
                    : (_document.LaneTopRatio + (_document.LaneBottomRatio - _document.LaneTopRatio) * ((float)clampedLane / Math.Max(1, lanes)));
                float activeLaneTop = Math.Max(imgRect.Top, StripRatioToScreenY(rTopLane, imgRect, vTop, vBot));
                float activeLaneBot = Math.Min(imgRect.Bottom, StripRatioToScreenY(rBotLane, imgRect, vTop, vBot));

                if (activeLaneBot > activeLaneTop)
                {
                    g.FillRectangle(ActiveLaneHighlightBrush, imgRect.Left, activeLaneTop, imgRect.Width, Math.Max(2f, activeLaneBot - activeLaneTop));
                }

                // Subtle outer contrast shadow for the 1px hairline so it is visible on both white and dark pixels
                g.DrawLine(HairlineShadowPen, hairlineX, imgRect.Top, hairlineX, imgRect.Bottom);
                g.DrawLine(HairlineGoldPen, hairlineX, imgRect.Top, hairlineX, imgRect.Bottom);

                // Horizontal Crosshair bar & target circle
                if (crosshairY >= imgRect.Top && crosshairY <= imgRect.Bottom)
                {
                    float chHalfW = 26f;
                    g.DrawLine(CrosshairShadowPen, hairlineX - chHalfW, crosshairY, hairlineX + chHalfW, crosshairY);
                    g.DrawLine(CrosshairCyanPen, hairlineX - chHalfW, crosshairY, hairlineX + chHalfW, crosshairY);
                    g.DrawEllipse(CrosshairCyanPen, hairlineX - 7f, crosshairY - 7f, 14f, 14f);
                }

                // Floating HUD Time + Lane Readout Pill at top of Hairline
                string hudText = string.Format(CultureInfo.InvariantCulture,
                    "{0:0.0000}s  (Off: {1})  •  L{2}{3}",
                    _cursorTimeSeconds,
                    CompetitorEntry.FormatSeconds(_cursorTimeSeconds, _document.Precision),
                    activeLane,
                    cam.IsCropped ? " [CROP]" : "");
                SizeF hsz = g.MeasureString(hudText, HudFont);
                float hx = Math.Max(imgRect.Left + 4f, Math.Min(imgRect.Right - hsz.Width - 12f, hairlineX - hsz.Width * 0.5f));
                RectangleF hudRect = new RectangleF(hx, imgRect.Top + 6f, hsz.Width + 10f, hsz.Height + 4f);
                g.FillRectangle(HudBgBrush, hudRect);
                g.DrawRectangle(HudBorderPen, hudRect.X, hudRect.Y, hudRect.Width, hudRect.Height);
                g.DrawString(hudText, HudFont, HudFgBrush, hudRect.X + 5f, hudRect.Y + 2f);
            }

            // 6. Live or Persistent FinishLynx 2D Selection Box (Right-Drag or Shift+Right-Click)
            if (_isSelectingZoomBox)
            {
                int zx = Math.Max(imgRect.Left, Math.Min(_zoomBoxStart.X, _zoomBoxCurrent.X));
                int zy = Math.Max(imgRect.Top, Math.Min(_zoomBoxStart.Y, _zoomBoxCurrent.Y));
                int zr = Math.Min(imgRect.Right, Math.Max(_zoomBoxStart.X, _zoomBoxCurrent.X));
                int zb = Math.Min(imgRect.Bottom, Math.Max(_zoomBoxStart.Y, _zoomBoxCurrent.Y));
                int zw = Math.Max(1, zr - zx);
                int zh = Math.Max(1, zb - zy);
                DrawSelectionMarquee(g, zx, zy, zw, zh, cam, imgRect, vTop, vBot, true);
            }
            else if (_hasActiveSelectionBox && _lastBoxSelectionCrop != null)
            {
                float sx0 = imgRect.Left + (float)((_lastBoxSelectionCrop.StartCol - _scrollColumnOffset) * _zoomFactor);
                float sx1 = imgRect.Left + (float)((_lastBoxSelectionCrop.EndCol - _scrollColumnOffset) * _zoomFactor);
                float sy0 = StripRatioToScreenY(_lastBoxSelectionCrop.TopRatio, imgRect, vTop, vBot);
                float sy1 = StripRatioToScreenY(_lastBoxSelectionCrop.BottomRatio, imgRect, vTop, vBot);
                int zx = (int)Math.Round(Math.Max(imgRect.Left, Math.Min(sx0, sx1)));
                int zr = (int)Math.Round(Math.Min(imgRect.Right, Math.Max(sx0, sx1)));
                int zy = (int)Math.Round(Math.Max(imgRect.Top, Math.Min(sy0, sy1)));
                int zb = (int)Math.Round(Math.Min(imgRect.Bottom, Math.Max(sy0, sy1)));
                if (zr > zx + 2 && zb > zy + 2)
                {
                    DrawSelectionMarquee(g, zx, zy, zr - zx, zb - zy, cam, imgRect, vTop, vBot, false);
                }
            }

            // 7. Draw Lane Gutter Bar (Left, Right, or Both) + Enhance Regions
            bool drawLeftBar = (_document.LaneBarSide == LaneBarPosition.Left || _document.LaneBarSide == LaneBarPosition.Both);
            bool drawRightBar = (_document.LaneBarSide == LaneBarPosition.Right || _document.LaneBarSide == LaneBarPosition.Both);
            int[] gutterXs = (drawLeftBar && drawRightBar) ? new int[] { 0, Width - LaneGutterWidthPx }
                           : (drawRightBar ? new int[] { Width - LaneGutterWidthPx } : new int[] { 0 });

            for (int gxIdx = 0; gxIdx < gutterXs.Length; gxIdx++)
            {
                int gx = gutterXs[gxIdx];
                g.FillRectangle(DarkNavyBrush, gx, 0, LaneGutterWidthPx, Height);
                g.DrawLine(SubtleBorderPen, gx + (gx == 0 ? LaneGutterWidthPx - 1 : 0), 0, gx + (gx == 0 ? LaneGutterWidthPx - 1 : 0), Height);

                // Enhance region strips on gutter
                if (cam.EnhanceRegions != null)
                {
                    using (SolidBrush enhBarBrush = new SolidBrush(Color.FromArgb(140, 34, 197, 94)))
                    {
                        for (int ri = 0; ri < cam.EnhanceRegions.Count; ri++)
                        {
                            EnhanceRegion er = cam.EnhanceRegions[ri];
                            if (!er.Enabled) continue;
                            float ry1 = StripRatioToScreenY(er.TopRatio, imgRect, vTop, vBot);
                            float ry2 = StripRatioToScreenY(er.BottomRatio, imgRect, vTop, vBot);
                            g.FillRectangle(enhBarBrush, gx + 1f, Math.Min(ry1, ry2), 4f, Math.Max(4f, Math.Abs(ry2 - ry1)));
                        }
                    }
                }

                for (int ln = 1; ln <= lanes; ln++)
                {
                    float rLaneCenter = hasCalibLanes
                        ? 0.5f * (calibBounds[ln - 1] + calibBounds[ln])
                        : (_document.LaneTopRatio + (_document.LaneBottomRatio - _document.LaneTopRatio) * ((ln - 0.5f) / Math.Max(1, lanes)));
                    float ly = StripRatioToScreenY(rLaneCenter, imgRect, vTop, vBot);
                    if (ly < imgRect.Top - 8f || ly > imgRect.Bottom + 8f) continue;
                    bool isHovered = (ln == activeLane);
                    RectangleF pill = new RectangleF(gx + 4f, ly - 10f, LaneGutterWidthPx - 8f, 20f);
                    if (isHovered)
                    {
                        g.FillRectangle(HoveredPillBgBrush, pill);
                        g.DrawString("L" + ln, LanePillFont, DarkNavyBrush, pill, CenteredFormat);
                    }
                    else
                    {
                        g.DrawString("L" + ln, LanePillFont, NormalPillFgBrush, pill, CenteredFormat);
                    }
                }
            }

            // 8. Draw Top Time Scale Ruler
            g.FillRectangle(RulerBgBrush, 0, 0, Width, RulerHeightPx);
            g.DrawLine(SubtleBorderPen, 0, RulerHeightPx - 1, Width, RulerHeightPx - 1);

            // Camera position badge in top-left corner of ruler
            g.DrawString(cam.PositionLabel.ToUpperInvariant(), CamBadgeFont, CamBadgeFgBrush, new RectangleF(0, 0, LaneGutterWidthPx, RulerHeightPx), CenteredFormat);

            double tLeft = cam.ColumnToTimeSeconds(_scrollColumnOffset);
            double tRight = cam.ColumnToTimeSeconds(_scrollColumnOffset + visibleCols);
            double tMin = Math.Min(tLeft, tRight);
            double tMax = Math.Max(tLeft, tRight);
            double spanSec = Math.Max(0.001, tMax - tMin);

            double majorStep = spanSec <= 0.15 ? 0.01
                             : spanSec <= 0.45 ? 0.02
                             : spanSec <= 1.20 ? 0.05
                             : spanSec <= 3.00 ? 0.10
                             : spanSec <= 8.00 ? 0.25 : 0.50;
            double minorStep = majorStep / 5.0;

            double firstMinor = Math.Floor(tMin / minorStep) * minorStep;
            float lastMajorLabelX = -9999f;
            for (double t = firstMinor; t <= tMax + minorStep; t += minorStep)
            {
                double col = cam.TimeSecondsToColumn(t);
                float sx = imgRect.Left + (float)((col - _scrollColumnOffset) * _zoomFactor);
                if (sx < imgRect.Left || sx > imgRect.Right) continue;

                long ratioIndex = (long)Math.Round(t / majorStep);
                bool isMajor = Math.Abs(t - (ratioIndex * majorStep)) < (minorStep * 0.25);

                if (isMajor)
                {
                    string label = majorStep < 0.05
                        ? t.ToString("0.000", CultureInfo.InvariantCulture) + "s"
                        : t.ToString("0.00", CultureInfo.InvariantCulture) + "s";
                    float minGap = Math.Max(48f, g.MeasureString(label, TickLabelFont).Width + 10f);
                    if (Math.Abs(sx - lastMajorLabelX) < minGap) continue;
                    g.DrawLine(MajorTickPen, sx, RulerHeightPx - 10, sx, RulerHeightPx - 1);
                    g.DrawString(label, TickLabelFont, TickLabelBrush, sx + 3f, 3f);
                    lastMajorLabelX = sx;
                }
                else
                {
                    if (Math.Abs(sx - lastMajorLabelX) < 6f) continue;
                    g.DrawLine(MinorTickPen, sx, RulerHeightPx - 5, sx, RulerHeightPx - 1);
                }
            }

            // Draw .spl split markers on the ruler (and strip if ShowSplitLines enabled)
            if (_document.SplitMarkerSeconds != null && _document.SplitMarkerSeconds.Count > 0)
            {
                for (int s = 0; s < _document.SplitMarkerSeconds.Count; s++)
                {
                    double spSec = _document.SplitMarkerSeconds[s];
                    if (spSec < tMin || spSec > tMax) continue;
                    double col = cam.TimeSecondsToColumn(spSec);
                    float sx = imgRect.Left + (float)((col - _scrollColumnOffset) * _zoomFactor);
                    if (sx >= imgRect.Left && sx <= imgRect.Right)
                    {
                        g.DrawLine(SplitMarkerPen, sx, 2, sx, RulerHeightPx - 1);
                        if (_document.ShowSplitLines)
                        {
                            using (Pen spBody = new Pen(Color.FromArgb(140, 244, 63, 94), 1f) { DashStyle = DashStyle.Dash })
                            {
                                g.DrawLine(spBody, sx, imgRect.Top, sx, imgRect.Bottom);
                            }
                        }
                    }
                }
            }

            // 9. Draw Bottom Mini Scrollbar Track
            Rectangle scrollRect = new Rectangle(drawLeftBar ? LaneGutterWidthPx : 0, Height - ScrollBarHeightPx, imgRect.Width, ScrollBarHeightPx);
            g.FillRectangle(DarkNavyBrush, scrollRect);
            double spanCols = Math.Max(1.0, maxCol - minCol);
            if (spanCols > 0)
            {
                float thumbRatioStart = (float)Math.Max(0.0, Math.Min(1.0, (_scrollColumnOffset - minCol) / spanCols));
                float thumbRatioWidth = (float)Math.Max(0.04, Math.Min(1.0 - thumbRatioStart, visibleCols / spanCols));
                RectangleF thumbRect = new RectangleF(
                    scrollRect.Left + thumbRatioStart * scrollRect.Width,
                    scrollRect.Top + 2f,
                    Math.Max(24f, thumbRatioWidth * scrollRect.Width),
                    ScrollBarHeightPx - 4f);
                g.FillRectangle(ScrollThumbBrush, thumbRect);
            }
        }

        private void DrawVideoFrameMode(Graphics g, SlitCameraStream cam, Rectangle imgRect)
        {
            g.Clear(Color.FromArgb(15, 23, 42));

            Bitmap frame = cam.GetVideoFrame(cam.ActiveVideoFrameIndex);
            if (frame == null)
            {
                g.DrawString("Seeking IdentiLynx Video Frame...", EmptyStateFont, EmptyStateBrush, ClientRectangle, CenteredFormat);
                return;
            }

            int fw = frame.Width;
            int fh = frame.Height;
            float fitScale = Math.Min((float)imgRect.Width / fw, (float)imgRect.Height / fh);
            float scale = fitScale * (float)_zoomFactor;
            float dw = fw * scale;
            float dh = fh * scale;
            float dx = imgRect.Left + (imgRect.Width - dw) * 0.5f + _videoPanOffset.X;
            float dy = imgRect.Top + (imgRect.Height - dh) * 0.5f + _videoPanOffset.Y;
            RectangleF dstRect = new RectangleF(dx, dy, dw, dh);

            InterpolationMode prevInterp = g.InterpolationMode;
            g.InterpolationMode = _zoomFactor > 1.5 ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(frame, dstRect);
            g.InterpolationMode = prevInterp;
            g.PixelOffsetMode = PixelOffsetMode.Default;

            // Draw Finish Line Reference Crosshair / Target Line if enabled
            if (_showEvaluatedLines)
            {
                float finishLineX = dx + dw * 0.5f;
                if (finishLineX >= imgRect.Left && finishLineX <= imgRect.Right)
                {
                    g.DrawLine(HairlineShadowPen, finishLineX, imgRect.Top, finishLineX, imgRect.Bottom);
                    g.DrawLine(HairlineGoldPen, finishLineX, imgRect.Top, finishLineX, imgRect.Bottom);
                }
            }

            // HUD overlay badge (time, frame number, resolution, fps)
            double curTime = (cam.VideoFrameTimestamps != null && cam.ActiveVideoFrameIndex < cam.VideoFrameTimestamps.Count)
                ? cam.VideoFrameTimestamps[cam.ActiveVideoFrameIndex]
                : _cursorTimeSeconds;
            string hudText = string.Format(CultureInfo.InvariantCulture,
                "IdentiLynx: {0:0.000}s  |  Frame {1}/{2}  |  {3}x{4} @ {5} fps",
                curTime,
                cam.ActiveVideoFrameIndex + 1,
                cam.TotalVideoFrames,
                fw, fh,
                cam.SampleRateHz > 0 ? cam.SampleRateHz : 20);

            SizeF hsz = g.MeasureString(hudText, BadgeFont);
            RectangleF hudRect = new RectangleF(imgRect.Left + 10f, imgRect.Bottom - hsz.Height - 14f, hsz.Width + 14f, hsz.Height + 6f);
            g.FillRectangle(BadgeBgBrush, hudRect);
            g.DrawRectangle(BadgeBorderPen, hudRect.X, hudRect.Y, hudRect.Width, hudRect.Height);
            g.DrawString(hudText, BadgeFont, BadgeFgBrush, hudRect.X + 7f, hudRect.Y + 3f);

            // Draw Bottom Video Scrubber Timeline
            DrawVideoScrubberBar(g, cam);
        }

        private void DrawVideoScrubberBar(Graphics g, SlitCameraStream cam)
        {
            int barY = Height - ScrollBarHeightPx;
            Rectangle barRect = new Rectangle(0, barY, Width, ScrollBarHeightPx);
            g.FillRectangle(DarkNavyBrush, barRect);

            if (cam.TotalVideoFrames <= 0) return;

            double ratio = (double)cam.ActiveVideoFrameIndex / Math.Max(1, cam.TotalVideoFrames - 1);
            float thumbX = (float)(ratio * Math.Max(10, Width - 24));
            RectangleF thumbRect = new RectangleF(thumbX, barY + 1, 24, ScrollBarHeightPx - 2);

            using (SolidBrush tb = new SolidBrush(Color.FromArgb(56, 189, 248)))
            {
                g.FillRectangle(tb, thumbRect);
            }
            g.DrawRectangle(Pens.White, thumbRect.X, thumbRect.Y, thumbRect.Width, thumbRect.Height);
        }

        private static float StripRatioToScreenY(float ratio, Rectangle imgRect, float vTop, float vBot)
        {
            return imgRect.Top + ((ratio - vTop) / Math.Max(0.01f, vBot - vTop)) * imgRect.Height;
        }

        private static float ScreenYToStripRatio(float y, Rectangle imgRect, float vTop, float vBot)
        {
            float rel = (y - imgRect.Top) / Math.Max(1f, (float)imgRect.Height);
            return Math.Max(0f, Math.Min(1f, vTop + rel * (vBot - vTop)));
        }

        private void DrawSelectionMarquee(Graphics g, int zx, int zy, int zw, int zh, SlitCameraStream cam, Rectangle imgRect, float vTop, float vBot, bool isDraggingNow)
        {
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(isDraggingNow ? 45 : 28, 56, 189, 248)))
            using (Pen outerDark = new Pen(Color.Black, 2f))
            using (Pen innerWhite = new Pen(Color.White, 1.2f))
            {
                g.FillRectangle(fill, zx, zy, zw, zh);
                g.DrawRectangle(outerDark, zx, zy, zw, zh);
                g.DrawRectangle(innerWhite, zx + 1, zy + 1, Math.Max(1, zw - 2), Math.Max(1, zh - 2));

                // Corner grip squares (FinishLynx signature selection handles)
                int hs = 4;
                Rectangle[] handles = new Rectangle[]
                {
                    new Rectangle(zx - 2, zy - 2, hs, hs),
                    new Rectangle(zx + zw - 2, zy - 2, hs, hs),
                    new Rectangle(zx - 2, zy + zh - 2, hs, hs),
                    new Rectangle(zx + zw - 2, zy + zh - 2, hs, hs)
                };
                for (int i = 0; i < handles.Length; i++)
                {
                    g.FillRectangle(Brushes.White, handles[i]);
                    g.DrawRectangle(Pens.Black, handles[i]);
                }

                if (cam != null && imgRect.Height > 10 && zw > 4 && zh > 4)
                {
                    double minCol, maxCol;
                    GetEffectiveColumnBounds(cam, out minCol, out maxCol);
                    int sCol = Math.Max((int)minCol, (int)Math.Floor(_scrollColumnOffset + (zx - imgRect.Left) / _zoomFactor));
                    int eCol = Math.Min((int)Math.Max(sCol + 1, maxCol - 1), (int)Math.Ceiling(_scrollColumnOffset + ((zx + zw) - imgRect.Left) / _zoomFactor));
                    float tr = ScreenYToStripRatio(zy, imgRect, vTop, vBot);
                    float br = Math.Max(tr + 0.02f, Math.Min(1f, ScreenYToStripRatio(zy + zh, imgRect, vTop, vBot)));

                    if (isDraggingNow)
                    {
                        _lastBoxSelectionCrop = new CropWindow(sCol, eCol, tr, br);
                    }

                    double tA = cam.ColumnToTimeSeconds(sCol);
                    double tB = cam.ColumnToTimeSeconds(eCol);
                    string selInfo = string.Format(CultureInfo.InvariantCulture, "SEL {0}px ({1:0.000}s–{2:0.000}s)",
                        Math.Max(1, eCol - sCol), Math.Min(tA, tB), Math.Max(tA, tB));
                    SizeF isz = g.MeasureString(selInfo, BadgeFont);
                    float ly = Math.Max(imgRect.Top + 2f, zy - isz.Height - 4f);
                    float lx = Math.Max(imgRect.Left + 2f, Math.Min(imgRect.Right - isz.Width - 10f, zx));
                    g.FillRectangle(BadgeBgBrush, lx, ly, isz.Width + 6f, isz.Height + 2f);
                    g.DrawString(selInfo, BadgeFont, HudFgBrush, lx + 3f, ly + 1f);
                }
            }
        }

        private bool IsPointInLaneGutter(Point pt)
        {
            if (_document == null) return false;
            bool drawLeftBar = (_document.LaneBarSide == LaneBarPosition.Left || _document.LaneBarSide == LaneBarPosition.Both);
            bool drawRightBar = (_document.LaneBarSide == LaneBarPosition.Right || _document.LaneBarSide == LaneBarPosition.Both);
            if (drawLeftBar && pt.X >= 0 && pt.X < LaneGutterWidthPx && pt.Y >= RulerHeightPx && pt.Y < Height - ScrollBarHeightPx)
                return true;
            if (drawRightBar && pt.X >= Width - LaneGutterWidthPx && pt.X <= Width && pt.Y >= RulerHeightPx && pt.Y < Height - ScrollBarHeightPx)
                return true;
            return false;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            Rectangle imgRect = GetImageCanvasRect();
            SlitCameraStream cam = ActiveStream;
            if (cam == null) return;

            // IdentiLynx 2D Video interaction mode
            if (cam.IsVideo)
            {
                if (e.Y >= Height - ScrollBarHeightPx)
                {
                    double ratio = (double)e.X / Math.Max(1, Width);
                    int frameIdx = (int)Math.Max(0, Math.Min(cam.TotalVideoFrames - 1, Math.Round(ratio * (cam.TotalVideoFrames - 1))));
                    JumpToVideoFrame(frameIdx);
                    return;
                }

                if (e.Button == MouseButtons.Left)
                {
                    if ((ModifierKeys & Keys.Control) == Keys.Control)
                    {
                        if (QuickEvaluateRequested != null && _document != null)
                        {
                            QuickEvaluateRequested(this, new HairlineChangedEventArgs(_cursorTimeSeconds, _cursorVerticalRatio, 1));
                        }
                        return;
                    }

                    _isPanningVideo = true;
                    _videoPanStartMouse = e.Location;
                    _videoPanStartOffset = _videoPanOffset;
                }
                else if (e.Button == MouseButtons.Right)
                {
                    _isSelectingZoomBox = true;
                    _zoomBoxStart = e.Location;
                    _zoomBoxCurrent = e.Location;
                }
                return;
            }

            // Lane bar gutter right-click popup (Lanes, Enhance regions, Adjustments...)
            if (IsPointInLaneGutter(e.Location) && e.Button == MouseButtons.Right)
            {
                ShowLaneBarContextMenu(e.Location);
                return;
            }

            // Check if clicking in bottom scrollbar
            if (e.Y >= Height - ScrollBarHeightPx && e.X >= LaneGutterWidthPx)
            {
                double ratio = (double)(e.X - LaneGutterWidthPx) / Math.Max(1, imgRect.Width);
                double visibleCols = imgRect.Width / _zoomFactor;
                _scrollColumnOffset = (ratio * cam.TotalColumns) - (visibleCols * 0.5);
                ClampScrollAndCursor();
                Invalidate();
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if ((ModifierKeys & Keys.Alt) == Keys.Alt)
                {
                    _isSelectingZoomBox = true;
                    _fixedSelectionClickMode = false;
                    _zoomBoxStart = e.Location;
                    _zoomBoxCurrent = e.Location;
                    return;
                }
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    UpdateHairlineFromMouse(e.Location);
                    if (QuickEvaluateRequested != null && _document != null)
                    {
                        int lane = _document.RatioToLane(_cursorVerticalRatio);
                        QuickEvaluateRequested(this, new HairlineChangedEventArgs(_cursorTimeSeconds, _cursorVerticalRatio, lane));
                    }
                    return;
                }

                _isDraggingHairline = true;
                UpdateHairlineFromMouse(e.Location);
            }
            else if (e.Button == MouseButtons.Middle)
            {
                _isPanning = true;
                _panStartMouse = e.Location;
                _panStartColumnOffset = _scrollColumnOffset;
                Cursor = Cursors.SizeWE;
            }
            else if (e.Button == MouseButtons.Right)
            {
                if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                {
                    // FinishLynx Shift+Right-Click: Fixed-Size Selection Box (Options -> Image -> Selection)
                    _fixedSelectionClickMode = true;
                    int fw = Math.Max(20, OptiFinish.LynxExtendedConfig.Instance.SelectionFixedWidth);
                    int fh = Math.Max(20, OptiFinish.LynxExtendedConfig.Instance.SelectionFixedHeight);
                    double minCol, maxCol;
                    GetEffectiveColumnBounds(cam, out minCol, out maxCol);
                    float vTop = cam.ActiveCrop != null ? Math.Max(0f, Math.Min(0.95f, cam.ActiveCrop.TopRatio)) : 0f;
                    float vBot = cam.ActiveCrop != null ? Math.Max(vTop + 0.05f, Math.Min(1f, cam.ActiveCrop.BottomRatio)) : 1f;

                    int halfW = (int)Math.Round(Math.Min(imgRect.Width * 0.48, fw * 0.5));
                    int halfH = (int)Math.Round(Math.Min(imgRect.Height * 0.48, fh * 0.5));
                    int zx = Math.Max(imgRect.Left, Math.Min(imgRect.Right - halfW * 2, e.X - halfW));
                    int zy = Math.Max(imgRect.Top, Math.Min(imgRect.Bottom - halfH * 2, e.Y - halfH));
                    int zw = Math.Max(12, Math.Min(imgRect.Right - zx, halfW * 2));
                    int zh = Math.Max(12, Math.Min(imgRect.Bottom - zy, halfH * 2));

                    int sCol = Math.Max((int)minCol, (int)Math.Floor(_scrollColumnOffset + (zx - imgRect.Left) / _zoomFactor));
                    int eCol = Math.Min((int)Math.Max(sCol + 4, maxCol - 1), (int)Math.Ceiling(_scrollColumnOffset + ((zx + zw) - imgRect.Left) / _zoomFactor));
                    float tr = ScreenYToStripRatio(zy, imgRect, vTop, vBot);
                    float br = Math.Max(tr + 0.04f, Math.Min(1f, ScreenYToStripRatio(zy + zh, imgRect, vTop, vBot)));

                    _lastBoxSelectionCrop = new CropWindow(sCol, eCol, tr, br);
                    _hasActiveSelectionBox = true;
                    Invalidate();
                    return;
                }

                _fixedSelectionClickMode = false;
                _isSelectingZoomBox = true;
                _zoomBoxStart = e.Location;
                _zoomBoxCurrent = e.Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            SlitCameraStream cam = ActiveStream;
            if (cam != null && cam.IsVideo)
            {
                if (_isPanningVideo)
                {
                    _videoPanOffset = new PointF(_videoPanStartOffset.X + (e.X - _videoPanStartMouse.X), _videoPanStartOffset.Y + (e.Y - _videoPanStartMouse.Y));
                    Invalidate();
                }
                else if (_isSelectingZoomBox)
                {
                    _zoomBoxCurrent = e.Location;
                    Invalidate();
                }
                return;
            }

            if (_isDraggingHairline)
            {
                UpdateHairlineFromMouse(e.Location);

                // Rolling auto-scroll when dragging near left/right edge
                Rectangle imgRect = GetImageCanvasRect();
                if (e.X < imgRect.Left + 16)
                {
                    _scrollColumnOffset -= (18.0 / _zoomFactor);
                    ClampScrollAndCursor();
                }
                else if (e.X > imgRect.Right - 16)
                {
                    _scrollColumnOffset += (18.0 / _zoomFactor);
                    ClampScrollAndCursor();
                }
                Invalidate();
            }
            else if (_isPanning)
            {
                int dx = e.X - _panStartMouse.X;
                _scrollColumnOffset = _panStartColumnOffset - (dx / _zoomFactor);
                ClampScrollAndCursor();
                Invalidate();
            }
            else if (_isSelectingZoomBox)
            {
                _zoomBoxCurrent = e.Location;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            SlitCameraStream cam = ActiveStream;
            if (cam != null && cam.IsVideo)
            {
                _isPanningVideo = false;
                if (e.Button == MouseButtons.Right && _isSelectingZoomBox)
                {
                    _isSelectingZoomBox = false;
                    int vdx = Math.Abs(e.X - _zoomBoxStart.X);
                    int vdy = Math.Abs(e.Y - _zoomBoxStart.Y);
                    if (vdx <= 6 && vdy <= 6)
                    {
                        _videoPanOffset = PointF.Empty;
                        Invalidate();
                    }
                    ShowContextToolsMenu(e.Location);
                }
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                _isDraggingHairline = false;
                if (_isSelectingZoomBox)
                {
                    _isSelectingZoomBox = false;
                    FinalizeSelectionBoxFromDrag(cam, GetImageCanvasRect(), e.Location);
                    Invalidate();
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                _isPanning = false;
                Cursor = Cursors.Cross;
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (_fixedSelectionClickMode)
                {
                    _fixedSelectionClickMode = false;
                    ShowContextToolsMenu(e.Location);
                    return;
                }
                if (_isSelectingZoomBox)
                {
                    _isSelectingZoomBox = false;
                    int dx = Math.Abs(e.X - _zoomBoxStart.X);
                    int dy = Math.Abs(e.Y - _zoomBoxStart.Y);
                    Rectangle imgRect = GetImageCanvasRect();

                    if ((dx > 8 || dy > 8) && cam != null)
                    {
                        FinalizeSelectionBoxFromDrag(cam, imgRect, e.Location);
                        Invalidate();
                        ShowContextToolsMenu(e.Location);
                    }
                    else
                    {
                        UpdateHairlineFromMouse(e.Location);
                        ShowContextToolsMenu(e.Location);
                    }
                }
            }
        }

        private void FinalizeSelectionBoxFromDrag(SlitCameraStream cam, Rectangle imgRect, Point endPt)
        {
            if (cam == null || cam.TotalColumns < 2 || imgRect.Height <= 4) return;

            int zx = Math.Max(imgRect.Left, Math.Min(_zoomBoxStart.X, endPt.X));
            int zy = Math.Max(imgRect.Top, Math.Min(_zoomBoxStart.Y, endPt.Y));
            int zr = Math.Min(imgRect.Right, Math.Max(_zoomBoxStart.X, endPt.X));
            int zb = Math.Min(imgRect.Bottom, Math.Max(_zoomBoxStart.Y, endPt.Y));
            int zw = Math.Max(6, zr - zx);
            int zh = Math.Max(6, zb - zy);

            double minCol, maxCol;
            GetEffectiveColumnBounds(cam, out minCol, out maxCol);
            float vTop = cam.ActiveCrop != null ? Math.Max(0f, Math.Min(0.95f, cam.ActiveCrop.TopRatio)) : 0f;
            float vBot = cam.ActiveCrop != null ? Math.Max(vTop + 0.05f, Math.Min(1f, cam.ActiveCrop.BottomRatio)) : 1f;

            int sCol = Math.Max((int)minCol, (int)Math.Floor(_scrollColumnOffset + (zx - imgRect.Left) / _zoomFactor));
            int eCol = Math.Min((int)Math.Max(sCol + 2, maxCol - 1), (int)Math.Ceiling(_scrollColumnOffset + ((zx + zw) - imgRect.Left) / _zoomFactor));
            float tr = ScreenYToStripRatio(zy, imgRect, vTop, vBot);
            float br = Math.Max(tr + 0.02f, Math.Min(1f, ScreenYToStripRatio(zy + zh, imgRect, vTop, vBot)));

            _lastBoxSelectionCrop = new CropWindow(sCol, eCol, tr, br);
            _hasActiveSelectionBox = true;
            _cursorTimeSeconds = cam.ColumnToTimeSeconds(0.5 * (sCol + eCol));
            _cursorVerticalRatio = 0.5f * (tr + br);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && _document != null)
            {
                UpdateHairlineFromMouse(e.Location);
                if (QuickEvaluateRequested != null)
                {
                    int lane = _document.RatioToLane(_cursorVerticalRatio);
                    QuickEvaluateRequested(this, new HairlineChangedEventArgs(_cursorTimeSeconds, _cursorVerticalRatio, lane));
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            SlitCameraStream cam = ActiveStream;
            if (cam != null && cam.IsVideo)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    double factor = e.Delta > 0 ? 1.25 : 0.80;
                    ZoomFactor = _zoomFactor * factor;
                }
                else
                {
                    StepVideoFrame(e.Delta > 0 ? 1 : -1);
                }
                return;
            }

            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                double factor = e.Delta > 0 ? 1.25 : 0.80;
                ZoomFactor = _zoomFactor * factor;
            }
            else
            {
                double visibleCols = GetVisibleStripPixelWidth() / _zoomFactor;
                double step = visibleCols * 0.15 * (e.Delta > 0 ? -1.0 : 1.0);
                _scrollColumnOffset += step;
                ClampScrollAndCursor();
                Invalidate();
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            if (keyCode == Keys.Left || keyCode == Keys.Right ||
                keyCode == Keys.Up || keyCode == Keys.Down ||
                keyCode == Keys.PageUp || keyCode == Keys.PageDown ||
                keyCode == Keys.Home || keyCode == Keys.End ||
                keyCode == Keys.Enter || keyCode == Keys.Escape)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Escape && _hasActiveSelectionBox)
            {
                ClearSelectionBox();
                e.Handled = true;
                return;
            }

            if (e.Alt && _hasActiveSelectionBox && _lastBoxSelectionCrop != null && ActiveStream != null)
            {
                int colShift = e.Shift ? 12 : 3;
                float rShift = e.Shift ? 0.05f : 0.015f;
                int span = Math.Max(4, _lastBoxSelectionCrop.EndCol - _lastBoxSelectionCrop.StartCol);
                float rSpan = Math.Max(0.02f, _lastBoxSelectionCrop.BottomRatio - _lastBoxSelectionCrop.TopRatio);
                double minCol, maxCol;
                GetEffectiveColumnBounds(ActiveStream, out minCol, out maxCol);

                if (e.KeyCode == Keys.Left)
                {
                    int ns = Math.Max((int)minCol, _lastBoxSelectionCrop.StartCol - colShift);
                    _lastBoxSelectionCrop = new CropWindow(ns, ns + span, _lastBoxSelectionCrop.TopRatio, _lastBoxSelectionCrop.BottomRatio);
                    Invalidate(); e.Handled = true; return;
                }
                if (e.KeyCode == Keys.Right)
                {
                    int ne = Math.Min((int)maxCol - 1, _lastBoxSelectionCrop.EndCol + colShift);
                    _lastBoxSelectionCrop = new CropWindow(Math.Max((int)minCol, ne - span), ne, _lastBoxSelectionCrop.TopRatio, _lastBoxSelectionCrop.BottomRatio);
                    Invalidate(); e.Handled = true; return;
                }
                if (e.KeyCode == Keys.Up)
                {
                    float nt = Math.Max(0f, _lastBoxSelectionCrop.TopRatio - rShift);
                    _lastBoxSelectionCrop = new CropWindow(_lastBoxSelectionCrop.StartCol, _lastBoxSelectionCrop.EndCol, nt, nt + rSpan);
                    Invalidate(); e.Handled = true; return;
                }
                if (e.KeyCode == Keys.Down)
                {
                    float nb = Math.Min(1f, _lastBoxSelectionCrop.BottomRatio + rShift);
                    _lastBoxSelectionCrop = new CropWindow(_lastBoxSelectionCrop.StartCol, _lastBoxSelectionCrop.EndCol, Math.Max(0f, nb - rSpan), nb);
                    Invalidate(); e.Handled = true; return;
                }
            }

            int step = e.Shift ? 10 : 1;

            if (e.KeyCode == Keys.Left)
            {
                StepHairlineColumns(-step);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right)
            {
                StepHairlineColumns(step);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up && _document != null)
            {
                int lane = Math.Max(1, _document.RatioToLane(_cursorVerticalRatio) - 1);
                _cursorVerticalRatio = _document.LaneToCenterRatio(lane);
                RaiseHairlineMoved();
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down && _document != null)
            {
                int lane = Math.Min(_document.LaneCount, _document.RatioToLane(_cursorVerticalRatio) + 1);
                _cursorVerticalRatio = _document.LaneToCenterRatio(lane);
                RaiseHairlineMoved();
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.PageUp)
            {
                PageScroll(-1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.PageDown)
            {
                PageScroll(1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Home)
            {
                JumpToStart();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.End)
            {
                JumpToEnd();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && _document != null && QuickEvaluateRequested != null)
            {
                int lane = _document.RatioToLane(_cursorVerticalRatio);
                QuickEvaluateRequested(this, new HairlineChangedEventArgs(_cursorTimeSeconds, _cursorVerticalRatio, lane));
                e.Handled = true;
            }
        }

        private void UpdateHairlineFromMouse(Point pt)
        {
            Rectangle imgRect = GetImageCanvasRect();
            SlitCameraStream cam = ActiveStream;
            if (cam == null || cam.TotalColumns <= 0) return;

            float vTop = cam.ActiveCrop != null ? Math.Max(0f, Math.Min(0.95f, cam.ActiveCrop.TopRatio)) : 0f;
            float vBot = cam.ActiveCrop != null ? Math.Max(vTop + 0.05f, Math.Min(1f, cam.ActiveCrop.BottomRatio)) : 1f;

            double relX = Math.Max(0, Math.Min(imgRect.Width, pt.X - imgRect.Left));
            double col = _scrollColumnOffset + (relX / _zoomFactor);
            col = Math.Max(0.0, Math.Min(cam.TotalColumns - 1, col));

            _cursorTimeSeconds = cam.ColumnToTimeSeconds(col);
            _cursorVerticalRatio = Math.Max(0.02f, Math.Min(0.98f, ScreenYToStripRatio(pt.Y, imgRect, vTop, vBot)));

            RaiseHairlineMoved();
            Invalidate();
        }

        private void RaiseHairlineMoved()
        {
            if (HairlineMoved != null && _document != null)
            {
                int lane = _document.RatioToLane(_cursorVerticalRatio);
                HairlineMoved(this, new HairlineChangedEventArgs(_cursorTimeSeconds, _cursorVerticalRatio, lane));
            }
        }

        private void ShowLaneBarContextMenu(Point location)
        {
            SlitCameraStream cam = ActiveStream;
            if (_document == null || cam == null) return;

            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem mLanes = new ToolStripMenuItem(Loc.Evt("Image\\LaneBar\\Popup\\Lanes", "Lanes"))
            {
                Checked = _showLaneGrid
            };
            mLanes.Click += delegate
            {
                _showLaneGrid = !_showLaneGrid;
                LynxConfigService.Instance.ShowLaneGrid = _showLaneGrid;
                Invalidate();
            };
            menu.Items.Add(mLanes);

            ToolStripMenuItem mEnhanceReg = new ToolStripMenuItem(Loc.Evt("Image\\LaneBar\\Popup\\EnhanceRegions", "Enhance regions"))
            {
                Checked = cam.EnhanceEnabled || (cam.EnhanceRegions != null && cam.EnhanceRegions.Count > 0)
            };
            mEnhanceReg.Click += delegate
            {
                if (cam.EnhanceRegions.Count > 0)
                {
                    cam.EnhanceRegions.Clear();
                }
                else
                {
                    cam.EnhanceEnabled = !cam.EnhanceEnabled;
                }
                SlitScanEngine.UpdateDisplayBitmap(cam);
                Invalidate();
                if (SelectionActionCompleted != null) SelectionActionCompleted(this, EventArgs.Empty);
            };
            menu.Items.Add(mEnhanceReg);

            ToolStripMenuItem mAdj = new ToolStripMenuItem(Loc.Evt("Image\\LaneBar\\Popup\\Adjustments", "Adjustments..."));
            mAdj.Click += delegate
            {
                if (OpenAdjustmentsRequested != null) OpenAdjustmentsRequested(this, EventArgs.Empty);
            };
            menu.Items.Add(mAdj);

            menu.Show(this, location);
        }

        private void ShowContextToolsMenu(Point location)
        {
            if (_document == null) return;
            SlitCameraStream cam = ActiveStream;
            int lane = _document.RatioToLane(_cursorVerticalRatio);

            ContextMenuStrip menu = new ContextMenuStrip();

            // FinishLynx Selection Popup Menu (Strings\Event\Image\SelectionPopup\*)
            if (cam != null && !cam.IsVideo)
            {
                ToolStripMenuItem mCrop = new ToolStripMenuItem(Loc.Evt("Image\\SelectionPopup\\Crop", "&Crop"));
                mCrop.Enabled = (_hasActiveSelectionBox || cam.TotalColumns >= 10);
                mCrop.Click += delegate { CropToCurrentZoomOrSelection(); };
                menu.Items.Add(mCrop);

                ToolStripMenuItem mKeep = new ToolStripMenuItem(Loc.Menu("Image\\KeepSelection", "&Keep selection"));
                mKeep.Enabled = _hasActiveSelectionBox;
                mKeep.Click += delegate { KeepSelectionCrop(); };
                menu.Items.Add(mKeep);

                if (cam.IsCropped)
                {
                    ToolStripMenuItem mUncrop = new ToolStripMenuItem(Loc.Menu("Image\\Uncrop", "&Uncrop"));
                    mUncrop.Click += delegate { UncropActiveStream(); };
                    menu.Items.Add(mUncrop);
                }

                ToolStripMenuItem mEnhance = new ToolStripMenuItem(Loc.Evt("Image\\SelectionPopup\\Enhance", "&Enhance"));
                mEnhance.Click += delegate { ToggleEnhanceAtActiveBand(); };
                menu.Items.Add(mEnhance);

                menu.Items.Add(new ToolStripSeparator());
            }

            ToolStripMenuItem mExport = new ToolStripMenuItem(Loc.Evt("Image\\SelectionPopup\\ExportBitmap", "E&xport..."));
            mExport.Click += delegate
            {
                if (ExportSelectionRequested != null) ExportSelectionRequested(this, EventArgs.Empty);
            };
            menu.Items.Add(mExport);

            ToolStripMenuItem mExportVideo = new ToolStripMenuItem(Loc.Evt("Image\\SelectionPopup\\ExportVideo", "Export &video..."));
            mExportVideo.Click += delegate
            {
                if (ExportVideoSelectionRequested != null) ExportVideoSelectionRequested(this, EventArgs.Empty);
            };
            menu.Items.Add(mExportVideo);

            ToolStripMenuItem mPrint = new ToolStripMenuItem(Loc.Evt("Image\\SelectionPopup\\Print", "&Print..."));
            mPrint.Click += delegate
            {
                if (PrintSelectionRequested != null) PrintSelectionRequested(this, EventArgs.Empty);
            };
            menu.Items.Add(mPrint);

            ToolStripMenuItem mPrintBmp = new ToolStripMenuItem(Loc.Evt("Image\\SelectionPopup\\PrintToBitmap", "Print to &bitmap..."));
            mPrintBmp.Click += delegate
            {
                if (PrintBitmapSelectionRequested != null) PrintBitmapSelectionRequested(this, EventArgs.Empty);
            };
            menu.Items.Add(mPrintBmp);

            ToolStripMenuItem mScbImg = new ToolStripMenuItem(Loc.Evt("Image\\SelectionPopup\\SetScbdImage", "Set scoreboard image"));
            mScbImg.Click += delegate
            {
                if (SetScoreboardImageRequested != null) SetScoreboardImageRequested(this, EventArgs.Empty);
            };
            menu.Items.Add(mScbImg);

            if (cam != null && !cam.IsVideo)
            {
                ToolStripMenuItem mWb = new ToolStripMenuItem(Loc.Evt("Image\\SelectionPopup\\WhiteBalance", "&White balance"));
                mWb.Click += delegate { SampleWhiteBalanceAtCursor(); };
                menu.Items.Add(mWb);
            }

            menu.Items.Add(new ToolStripSeparator());

            if (_hasActiveSelectionBox && _lastBoxSelectionCrop != null)
            {
                ToolStripMenuItem mZoomSel = new ToolStripMenuItem("Zoom to Selection");
                mZoomSel.Click += delegate { ZoomToActiveSelection(); };
                menu.Items.Add(mZoomSel);

                ToolStripMenuItem mClearSel = new ToolStripMenuItem("Clear Selection (Esc)");
                mClearSel.Click += delegate { ClearSelectionBox(); };
                menu.Items.Add(mClearSel);
                menu.Items.Add(new ToolStripSeparator());
            }

            ToolStripMenuItem itemEvalLane = new ToolStripMenuItem(
                string.Format(CultureInfo.InvariantCulture, "Assign {0:0.0000}s to Lane {1} Athlete (Double-Click / Enter)", _cursorTimeSeconds, lane));
            itemEvalLane.Font = new Font(menu.Font, FontStyle.Bold);
            itemEvalLane.Click += delegate
            {
                if (QuickEvaluateRequested != null)
                {
                    QuickEvaluateRequested(this, new HairlineChangedEventArgs(_cursorTimeSeconds, _cursorVerticalRatio, lane));
                }
            };
            menu.Items.Add(itemEvalLane);

            ToolStripMenuItem itemNextObj = new ToolStripMenuItem("Find Next Runner >>");
            itemNextObj.Click += delegate
            {
                if (cam != null)
                {
                    int curCol = (int)Math.Round(cam.TimeSecondsToColumn(_cursorTimeSeconds));
                    int nextCol = SlitScanEngine.FindNextObjectColumn(cam, curCol, 1);
                    CenterOnTime(cam.ColumnToTimeSeconds(nextCol));
                    RaiseHairlineMoved();
                }
            };
            menu.Items.Add(itemNextObj);

            ToolStripMenuItem itemPrevObj = new ToolStripMenuItem("<< Find Previous Runner");
            itemPrevObj.Click += delegate
            {
                if (cam != null)
                {
                    int curCol = (int)Math.Round(cam.TimeSecondsToColumn(_cursorTimeSeconds));
                    int prevCol = SlitScanEngine.FindNextObjectColumn(cam, curCol, -1);
                    CenterOnTime(cam.ColumnToTimeSeconds(prevCol));
                    RaiseHairlineMoved();
                }
            };
            menu.Items.Add(itemPrevObj);

            ToolStripMenuItem itemZeroHere = new ToolStripMenuItem("Align Image Start (Set 0.0000s Origin Here)");
            itemZeroHere.Click += delegate
            {
                if (SetZeroOriginRequested != null)
                {
                    SetZeroOriginRequested(this, _cursorTimeSeconds);
                }
            };
            menu.Items.Add(itemZeroHere);

            ToolStripMenuItem itemResetZoom = new ToolStripMenuItem("Reset Zoom (100%)");
            itemResetZoom.Click += delegate
            {
                ZoomFactor = 1.0;
            };
            menu.Items.Add(itemResetZoom);

            menu.Show(this, location);
        }
    }
}
