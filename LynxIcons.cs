using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace OptiFinish
{
    /// <summary>
    /// Embedded FinishLynx icon sprite atlas loader and classic Win32 control button renderer.
    /// </summary>
    public static class LynxIcons
    {
        private static Bitmap _atlas;
        private static readonly object SyncLock = new object();
        private static readonly Dictionary<string, Rectangle> Rects = new Dictionary<string, Rectangle>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>(StringComparer.Ordinal);

        static LynxIcons()
        {
            Rects["NewEvent"] = new Rectangle(2, 2, 28, 28);
            Rects["PrevEventBig"] = new Rectangle(32, 2, 27, 27);
            Rects["ChooseEventBig"] = new Rectangle(61, 2, 27, 27);
            Rects["NextEventBig"] = new Rectangle(90, 2, 27, 27);
            Rects["ManualStart"] = new Rectangle(119, 2, 27, 27);
            Rects["ApplyStart"] = new Rectangle(148, 2, 28, 28);
            Rects["Arm"] = new Rectangle(178, 2, 28, 28);
            Rects["InactiveCapture"] = new Rectangle(208, 2, 30, 30);
            Rects["ActiveCapture"] = new Rectangle(240, 2, 30, 30);
            Rects["Capturing"] = new Rectangle(272, 2, 30, 30);
            Rects["CameraSettings"] = new Rectangle(304, 2, 20, 20);
            Rects["AdvCapture"] = new Rectangle(326, 2, 20, 20);
            Rects["ArmBox"] = new Rectangle(348, 2, 22, 22);
            Rects["CaptureBox"] = new Rectangle(372, 2, 22, 22);
            Rects["NewResult"] = new Rectangle(396, 2, 20, 20);
            Rects["DeleteResult"] = new Rectangle(418, 2, 20, 20);
            Rects["FindID"] = new Rectangle(440, 2, 20, 20);
            Rects["Filters"] = new Rectangle(462, 2, 20, 20);
            Rects["PackLeader"] = new Rectangle(484, 2, 20, 20);
            Rects["PrevEvent"] = new Rectangle(2, 34, 20, 20);
            Rects["GoToEvent"] = new Rectangle(24, 34, 20, 20);
            Rects["NextEvent"] = new Rectangle(46, 34, 20, 20);
            Rects["ReacTime"] = new Rectangle(68, 34, 20, 20);
            Rects["Print"] = new Rectangle(90, 34, 20, 20);
            Rects["AcceptSplit"] = new Rectangle(112, 34, 20, 20);
            Rects["InsertSplit"] = new Rectangle(134, 34, 20, 20);
            Rects["DeleteSplit"] = new Rectangle(156, 34, 20, 20);
            Rects["IdentifyID"] = new Rectangle(178, 34, 21, 21);
            Rects["IdentifyLane"] = new Rectangle(201, 34, 21, 21);
            Rects["IdentifyLicense"] = new Rectangle(224, 34, 21, 21);
            Rects["IdentifyNothing"] = new Rectangle(247, 34, 20, 20);
            Rects["Orient"] = new Rectangle(269, 34, 20, 20);
            Rects["ZoomOut"] = new Rectangle(291, 34, 20, 20);
            Rects["ZoomIn"] = new Rectangle(313, 34, 20, 20);
            Rects["Crop"] = new Rectangle(335, 34, 20, 20);
            Rects["Uncrop"] = new Rectangle(357, 34, 19, 19);
            Rects["Autocrop"] = new Rectangle(378, 34, 20, 20);
            Rects["Scroll"] = new Rectangle(400, 34, 20, 20);
            Rects["FindNext"] = new Rectangle(422, 34, 20, 20);
            Rects["Enhance"] = new Rectangle(444, 34, 20, 20);
            Rects["QuickAdjust"] = new Rectangle(466, 34, 20, 20);
            Rects["PrintBitmap"] = new Rectangle(488, 34, 20, 20);
            Rects["Maximize"] = new Rectangle(2, 57, 20, 20);
            Rects["OpenIris"] = new Rectangle(24, 57, 20, 20);
            Rects["CloseIris"] = new Rectangle(46, 57, 20, 20);
            Rects["AutoIris"] = new Rectangle(68, 57, 20, 20);
            Rects["Play"] = new Rectangle(90, 57, 20, 22);
            Rects["Stop"] = new Rectangle(112, 57, 20, 22);
            Rects["StartFrame"] = new Rectangle(134, 57, 20, 22);
            Rects["EndFrame"] = new Rectangle(156, 57, 19, 22);
            Rects["PrevFrame"] = new Rectangle(177, 57, 20, 22);
            Rects["NextFrame"] = new Rectangle(199, 57, 20, 22);
            Rects["ClearFrame"] = new Rectangle(221, 57, 20, 22);
            Rects["TimeTrack"] = new Rectangle(243, 57, 19, 19);
            Rects["Align1DRed"] = new Rectangle(264, 57, 20, 20);
            Rects["Align1DGreen"] = new Rectangle(286, 57, 20, 20);
            Rects["Align2DRed"] = new Rectangle(308, 57, 20, 20);
            Rects["Align2DGreen"] = new Rectangle(330, 57, 20, 20);
            Rects["EVF"] = new Rectangle(352, 57, 49, 22);
            Rects["FocusHelper"] = new Rectangle(403, 57, 64, 22);
            Rects["LevelGreen"] = new Rectangle(2, 81, 73, 22);
            Rects["LevelYellow"] = new Rectangle(77, 81, 73, 22);
            Rects["LevelRed"] = new Rectangle(152, 81, 73, 22);
            Rects["WBZero"] = new Rectangle(227, 81, 36, 20);
            Rects["WBSet"] = new Rectangle(265, 81, 36, 20);
            Rects["WBApply"] = new Rectangle(303, 81, 36, 20);
            Rects["WBRedLess"] = new Rectangle(341, 81, 18, 20);
            Rects["WBRedMore"] = new Rectangle(361, 81, 18, 20);
            Rects["WBGreenLess"] = new Rectangle(381, 81, 18, 20);
            Rects["WBGreenMore"] = new Rectangle(401, 81, 18, 20);
            Rects["WBBlueLess"] = new Rectangle(421, 81, 18, 20);
            Rects["WBBlueMore"] = new Rectangle(441, 81, 18, 20);
            Rects["CenterPositioner"] = new Rectangle(461, 81, 39, 24);
            Rects["AutoAlign"] = new Rectangle(2, 107, 39, 24);
            Rects["FocusNear"] = new Rectangle(43, 107, 24, 24);
            Rects["FocusFar"] = new Rectangle(69, 107, 24, 24);
            Rects["AutoFocus"] = new Rectangle(95, 107, 24, 24);
            Rects["AddSplit"] = new Rectangle(121, 107, 20, 20);
            Rects["LookupID"] = new Rectangle(143, 107, 20, 20);
            Rects["LynxLogo"] = new Rectangle(165, 107, 18, 18);
            Rects["RemoteCCW"] = new Rectangle(185, 107, 22, 22);
            Rects["RemoteUp"] = new Rectangle(209, 107, 22, 22);
            Rects["RemoteCW"] = new Rectangle(233, 107, 22, 22);
            Rects["RemoteLeft"] = new Rectangle(257, 107, 22, 22);
            Rects["RemoteDown"] = new Rectangle(281, 107, 22, 22);
            Rects["RemoteRight"] = new Rectangle(305, 107, 22, 22);
        }

        private static Bitmap EnsureAtlas()
        {
            if (_atlas != null) return _atlas;
            lock (SyncLock)
            {
                if (_atlas != null) return _atlas;
                try
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    using (Stream st = asm.GetManifestResourceStream("OptiFinish.LynxIcons.png"))
                    {
                        if (st != null)
                        {
                            using (Bitmap raw = new Bitmap(st))
                            {
                                _atlas = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
                                using (Graphics g = Graphics.FromImage(_atlas))
                                {
                                    g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
                                }
                            }
                        }
                    }
                }
                catch { }
                if (_atlas == null)
                {
                    string disk = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LynxIcons.png");
                    if (File.Exists(disk))
                    {
                        _atlas = new Bitmap(disk);
                    }
                }
                return _atlas;
            }
        }

        public static Bitmap Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (SyncLock)
            {
                Bitmap cached;
                if (Cache.TryGetValue(name, out cached)) return cached;
                Bitmap atlas = EnsureAtlas();
                Rectangle r;
                if (atlas == null || !Rects.TryGetValue(name, out r)) return null;
                Bitmap slice = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(slice))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(atlas, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
                }
                Cache[name] = slice;
                return slice;
            }
        }

        public static Bitmap CreateLayoutPreview(int layoutIndex)
        {
            Bitmap bmp = new Bitmap(30, 22, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Color cInfo = Color.FromArgb(255, 255, 0);
                Color cRes  = Color.FromArgb(0, 255, 255);
                Color cImg  = Color.FromArgb(0, 255, 0);
                g.Clear(Color.Black);
                Rectangle infoR, resR, imgR;
                switch (layoutIndex)
                {
                    case 1:  infoR = new Rectangle(1, 1, 9, 8);   resR = new Rectangle(11, 1, 18, 8);  imgR = new Rectangle(1, 10, 28, 11); break;
                    case 2:  infoR = new Rectangle(20, 1, 9, 8);  resR = new Rectangle(20, 10, 9, 11); imgR = new Rectangle(1, 1, 18, 20);  break;
                    case 3:  infoR = new Rectangle(20, 13, 9, 8); resR = new Rectangle(20, 1, 9, 11);  imgR = new Rectangle(1, 1, 18, 20);  break;
                    case 4:  infoR = new Rectangle(1, 1, 9, 8);   resR = new Rectangle(1, 10, 9, 11);  imgR = new Rectangle(11, 1, 18, 20); break;
                    case 5:  infoR = new Rectangle(1, 13, 9, 8);  resR = new Rectangle(1, 1, 9, 11);   imgR = new Rectangle(11, 1, 18, 20); break;
                    default: infoR = new Rectangle(1, 1, 11, 9);  resR = new Rectangle(13, 1, 16, 9);  imgR = new Rectangle(1, 11, 28, 10); break;
                }
                using (SolidBrush bi = new SolidBrush(cInfo))
                using (SolidBrush br = new SolidBrush(cRes))
                using (SolidBrush bm = new SolidBrush(cImg))
                {
                    g.FillRectangle(bi, infoR);
                    g.FillRectangle(br, resR);
                    g.FillRectangle(bm, imgR);
                }
            }
            return bmp;
        }

        public static Icon GetWindowIcon()
        {
            Bitmap cat = Get("LynxLogo");
            if (cat != null)
            {
                try { return Icon.FromHandle(cat.GetHicon()); }
                catch { }
            }
            return null;
        }
    }

    /// <summary>
    /// Classic Win32/Zinc-style 3D beveled icon button matching FinishLynx toolbars.
    /// </summary>
    public sealed class LynxIconButton : Control
    {
        private string _iconKey;
        private bool _checked;
        private bool _pressed;
        private bool _hovered;
        private string _helpHint = "";

        public string IconKey
        {
            get { return _iconKey; }
            set { _iconKey = value; Invalidate(); }
        }

        public bool Checked
        {
            get { return _checked; }
            set { if (_checked != value) { _checked = value; Invalidate(); } }
        }

        public string HelpHint
        {
            get { return _helpHint; }
            set { _helpHint = value ?? ""; }
        }

        public event Action<string> StatusHintHover;

        public LynxIconButton(string iconKey, int width, int height, string helpHint)
        {
            _iconKey = iconKey;
            _helpHint = helpHint ?? "";
            Size = new Size(width, height);
            BackColor = SystemColors.Control;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
            if (StatusHintHover != null && !string.IsNullOrEmpty(_helpHint)) StatusHintHover(_helpHint);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            Invalidate();
            if (StatusHintHover != null) StatusHintHover("");
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_pressed)
            {
                _pressed = false;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            bool down = (_pressed || _checked) && Enabled;

            using (SolidBrush bg = new SolidBrush(down ? Color.FromArgb(228, 228, 224) : (_hovered && Enabled ? Color.FromArgb(245, 245, 242) : SystemColors.Control)))
            {
                g.FillRectangle(bg, r);
            }

            // Classic Win32 3D double bevel
            Color tlOuter = down ? SystemColors.ControlDarkDark : SystemColors.ControlLightLight;
            Color brOuter = down ? SystemColors.ControlLightLight : SystemColors.ControlDarkDark;
            Color tlInner = down ? SystemColors.ControlDark : SystemColors.ControlLight;
            Color brInner = down ? SystemColors.ControlLight : SystemColors.ControlDark;

            using (Pen pTL = new Pen(tlOuter))
            using (Pen pBR = new Pen(brOuter))
            using (Pen pTL2 = new Pen(tlInner))
            using (Pen pBR2 = new Pen(brInner))
            {
                g.DrawLine(pTL, 0, 0, Width - 2, 0);
                g.DrawLine(pTL, 0, 0, 0, Height - 2);
                g.DrawLine(pBR, Width - 1, 0, Width - 1, Height - 1);
                g.DrawLine(pBR, 0, Height - 1, Width - 1, Height - 1);
                g.DrawLine(pTL2, 1, 1, Width - 3, 1);
                g.DrawLine(pTL2, 1, 1, 1, Height - 3);
                g.DrawLine(pBR2, Width - 2, 1, Width - 2, Height - 2);
                g.DrawLine(pBR2, 1, Height - 2, Width - 2, Height - 2);
            }

            Bitmap bmp = LynxIcons.Get(_iconKey);
            if (bmp != null)
            {
                int availW = Math.Max(4, Width - 4);
                int availH = Math.Max(4, Height - 4);
                int drawW = Math.Min(bmp.Width, availW);
                int drawH = Math.Min(bmp.Height, availH);
                int dx = (Width - drawW) / 2 + (down ? 1 : 0);
                int dy = (Height - drawH) / 2 + (down ? 1 : 0);

                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                if (Enabled)
                {
                    g.DrawImage(bmp, new Rectangle(dx, dy, drawW, drawH), new Rectangle(0, 0, bmp.Width, bmp.Height), GraphicsUnit.Pixel);
                }
                else
                {
                    ControlPaint.DrawImageDisabled(g, bmp, dx, dy, BackColor);
                }
            }
            else if (!string.IsNullOrEmpty(Text))
            {
                TextRenderer.DrawText(g, Text, Font, r, Enabled ? ForeColor : SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
