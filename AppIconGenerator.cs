using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace OptiFinish
{
    /// <summary>
    /// Provides the application icon from the embedded OptiFinish.png artwork
    /// (with a vector fallback) and exports a multi-resolution Windows .ico file.
    /// </summary>
    internal static class AppIconGenerator
    {
        private const string EmbeddedIconResource = "OptiFinish.OptiFinish.png";

        public static Icon CreateMemoryIcon(int size)
        {
            using (Bitmap bmp = RenderIconBitmap(size))
            {
                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        public static void GenerateIconFile(string outputPath)
        {
            int[] sizes = new int[] { 16, 32, 48, 64, 128, 256 };
            byte[][] payloads = new byte[sizes.Length][];

            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                using (Bitmap bmp = RenderIconBitmap(s))
                {
                    payloads[i] = s >= 256 ? EncodePngPayload(bmp) : EncodeBmpDibPayload(bmp);
                }
            }

            using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                // ICONDIR header
                bw.Write((ushort)0);
                bw.Write((ushort)1);
                bw.Write((ushort)sizes.Length);

                int offset = 6 + (16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
                    bw.Write((uint)payloads[i].Length);
                    bw.Write((uint)offset);
                    offset += payloads[i].Length;
                }

                for (int i = 0; i < sizes.Length; i++)
                {
                    bw.Write(payloads[i]);
                }
            }
        }

        private static byte[] EncodePngPayload(Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static byte[] EncodeBmpDibPayload(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            int andStride = ((w + 31) / 32) * 4;
            int xorSize = w * h * 4;
            int andSize = andStride * h;

            using (MemoryStream ms = new MemoryStream(40 + xorSize + andSize))
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                // BITMAPINFOHEADER (40 bytes); biHeight is 2*h for XOR + AND masks
                bw.Write((uint)40);
                bw.Write(w);
                bw.Write(h * 2);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)0);
                bw.Write((uint)(xorSize + andSize));
                bw.Write(0);
                bw.Write(0);
                bw.Write((uint)0);
                bw.Write((uint)0);

                // XOR mask: 32-bit BGRA bottom-up
                for (int y = h - 1; y >= 0; y--)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        bw.Write(c.B);
                        bw.Write(c.G);
                        bw.Write(c.R);
                        bw.Write(c.A);
                    }
                }

                // AND mask: 1-bpp bottom-up (bit=1 where transparent)
                byte[] row = new byte[andStride];
                for (int y = h - 1; y >= 0; y--)
                {
                    Array.Clear(row, 0, row.Length);
                    for (int x = 0; x < w; x++)
                    {
                        if (bmp.GetPixel(x, y).A < 128)
                        {
                            row[x >> 3] |= (byte)(0x80 >> (x & 7));
                        }
                    }
                    bw.Write(row);
                }

                return ms.ToArray();
            }
        }

        private static Bitmap LoadMasterBitmap()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream st = asm.GetManifestResourceStream(EmbeddedIconResource))
                {
                    if (st != null)
                    {
                        return new Bitmap(st);
                    }
                }
            }
            catch { }

            try
            {
                string[] candidateDirs = new string[]
                {
                    AppDomain.CurrentDomain.BaseDirectory,
                    Environment.CurrentDirectory
                };
                for (int i = 0; i < candidateDirs.Length; i++)
                {
                    if (string.IsNullOrEmpty(candidateDirs[i])) continue;
                    string diskPath = Path.Combine(candidateDirs[i], "OptiFinish.png");
                    if (File.Exists(diskPath))
                    {
                        using (FileStream fs = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            return new Bitmap(fs);
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static Bitmap RenderIconBitmap(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);

                using (Bitmap master = LoadMasterBitmap())
                {
                    if (master != null)
                    {
                        g.DrawImage(master, new Rectangle(0, 0, size, size), new Rectangle(0, 0, master.Width, master.Height), GraphicsUnit.Pixel);
                        return bmp;
                    }
                }

                // Vector fallback if embedded PNG is unavailable
                float pad = size * 0.05f;
                RectangleF rect = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
                float radius = size * 0.22f;

                using (GraphicsPath bgPath = RoundedRect(rect, radius))
                {
                    using (LinearGradientBrush bgBrush = new LinearGradientBrush(
                        rect,
                        Color.FromArgb(11, 18, 32),
                        Color.FromArgb(23, 37, 84),
                        45f))
                    {
                        g.FillPath(bgBrush, bgPath);
                    }

                    g.SetClip(bgPath);
                    float trackTop = rect.Top + rect.Height * 0.42f;
                    float trackH = rect.Height * 0.40f;
                    using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(210, 194, 65, 58)))
                    {
                        g.FillRectangle(trackBrush, rect.Left, trackTop, rect.Width, trackH);
                    }

                    using (Pen lanePen = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, size * 0.022f)))
                    {
                        for (int l = 0; l <= 3; l++)
                        {
                            float ly = trackTop + (trackH * l / 3f);
                            g.DrawLine(lanePen, rect.Left, ly, rect.Right, ly);
                        }
                    }

                    float slitX = rect.Left + rect.Width * 0.62f;
                    using (Pen cyanPen = new Pen(Color.FromArgb(190, 56, 189, 248), Math.Max(1.5f, size * 0.04f)))
                    {
                        g.DrawLine(cyanPen, slitX - size * 0.02f, rect.Top + size * 0.06f, slitX - size * 0.02f, rect.Bottom - size * 0.06f);
                    }
                    using (Pen goldPen = new Pen(Color.FromArgb(250, 204, 21), Math.Max(1.5f, size * 0.04f)))
                    {
                        g.DrawLine(goldPen, slitX + size * 0.02f, rect.Top + size * 0.06f, slitX + size * 0.02f, rect.Bottom - size * 0.06f);
                    }

                    g.ResetClip();

                    using (Pen borderPen = new Pen(Color.FromArgb(180, 100, 116, 139), Math.Max(1f, size * 0.035f)))
                    {
                        g.DrawPath(borderPen, bgPath);
                    }
                }
            }
            return bmp;
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
