using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace OptiFinish.Core
{
    /// <summary>
    /// Clean-room slit-scan image processing, motion energy peak detection,
    /// native FinishLynx (.i01/.i02) 16,000-byte block stream decoder,
    /// synthetic dual-camera race strip generator, and official annotated image exporter.
    /// </summary>
    public static class SlitScanEngine
    {
        private static readonly int[] Delta555Table = new int[]
        {
            3, 4, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25, 27, 28, 29
        };
        private static readonly int[] V3ChannelOrder = new int[] { 2, 1, 0, 3 };
        private static readonly int[] V3SmallBitsTable = new int[] { 1, 1, 2, 2, 3 };
        private static readonly Color[] TeamJerseyPalette = new Color[]
        {
            Color.FromArgb(220, 38, 38),   // Crimson Red
            Color.FromArgb(37, 99, 235),   // Royal Blue
            Color.FromArgb(22, 163, 74),   // Emerald Green
            Color.FromArgb(217, 119, 6),   // Texas Burnt Orange / Amber
            Color.FromArgb(147, 51, 234),  // Purple
            Color.FromArgb(13, 148, 136),  // Teal
            Color.FromArgb(225, 29, 72),   // Rose
            Color.FromArgb(79, 70, 229),   // Indigo
            Color.FromArgb(202, 138, 4),   // Gold
            Color.FromArgb(15, 118, 110)   // Deep Cyan
        };

        private static readonly Color[] SkinTonePalette = new Color[]
        {
            Color.FromArgb(141, 85, 36),
            Color.FromArgb(198, 134, 66),
            Color.FromArgb(224, 172, 105),
            Color.FromArgb(110, 62, 28),
            Color.FromArgb(241, 194, 125),
            Color.FromArgb(165, 105, 56)
        };

        /// <summary>
        /// Applies non-destructive Brightness, Contrast, and Gamma adjustments to a camera stream's RawStripBitmap
        /// using a fast 256-entry LUT and LockBits.
        /// </summary>
        public static void UpdateDisplayBitmap(SlitCameraStream stream)
        {
            if (stream == null || stream.RawStripBitmap == null) return;

            bool hasEnhanceRegions = false;
            if (stream.EnhanceRegions != null)
            {
                for (int rIdx = 0; rIdx < stream.EnhanceRegions.Count; rIdx++)
                {
                    if (stream.EnhanceRegions[rIdx].Enabled) { hasEnhanceRegions = true; break; }
                }
            }

            bool isNeutral = (stream.BrightnessOffset == 0 &&
                              stream.ContrastOffset == 0 &&
                              stream.BlackLevel == 0 &&
                              stream.WhiteBalanceRed == 0 &&
                              stream.WhiteBalanceGreen == 0 &&
                              stream.WhiteBalanceBlue == 0 &&
                              !stream.EnhanceEnabled &&
                              !hasEnhanceRegions &&
                              Math.Abs(stream.GammaValue - 1.0) < 0.005);

            if (isNeutral)
            {
                if (stream.DisplayStripBitmap != null && !ReferenceEquals(stream.DisplayStripBitmap, stream.RawStripBitmap))
                {
                    stream.DisplayStripBitmap.Dispose();
                }
                stream.DisplayStripBitmap = stream.RawStripBitmap;
                return;
            }

            Bitmap src = stream.RawStripBitmap;
            int w = src.Width;
            int h = src.Height;

            if (stream.DisplayStripBitmap == null ||
                ReferenceEquals(stream.DisplayStripBitmap, src) ||
                stream.DisplayStripBitmap.Width != w ||
                stream.DisplayStripBitmap.Height != h)
            {
                if (stream.DisplayStripBitmap != null && !ReferenceEquals(stream.DisplayStripBitmap, src))
                {
                    stream.DisplayStripBitmap.Dispose();
                }
                stream.DisplayStripBitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            }

            // Build 256-entry RGB channel LUTs (B=0, G=1, R=2 in 24bpp BGR)
            double effContrast = stream.ContrastOffset + (stream.EnhanceEnabled ? 22 : 0);
            double effBright = (stream.BrightnessOffset - stream.BlackLevel * 0.4 + (stream.EnhanceEnabled ? 8 : 0)) * 2.0;
            double contrastFactor = Math.Pow((100.0 + effContrast) / 100.0, 2.0);
            double invGamma = 1.0 / Math.Max(0.20, Math.Min(3.00, stream.GammaValue * (stream.EnhanceEnabled ? 0.90 : 1.0)));

            byte[] lutB = new byte[256];
            byte[] lutG = new byte[256];
            byte[] lutR = new byte[256];
            double[] wbGain = new double[]
            {
                1.0 + stream.WhiteBalanceBlue / 160.0,
                1.0 + stream.WhiteBalanceGreen / 160.0,
                1.0 + stream.WhiteBalanceRed / 160.0
            };

            for (int i = 0; i < 256; i++)
            {
                double baseV = Math.Pow(i / 255.0, invGamma);
                baseV = ((baseV - 0.5) * contrastFactor) + 0.5;
                double basePx = (baseV * 255.0) + effBright;

                double bPx = Math.Max(0.0, Math.Min(255.0, basePx * wbGain[0]));
                double gPx = Math.Max(0.0, Math.Min(255.0, basePx * wbGain[1]));
                double rPx = Math.Max(0.0, Math.Min(255.0, basePx * wbGain[2]));

                lutB[i] = (byte)Math.Round(bPx);
                lutG[i] = (byte)Math.Round(gPx);
                lutR[i] = (byte)Math.Round(rPx);
            }

            byte[] enhLut = null;
            if (hasEnhanceRegions)
            {
                enhLut = new byte[256];
                for (int i = 0; i < 256; i++)
                {
                    double ev = Math.Pow(i / 255.0, 0.72);
                    ev = ((ev - 0.5) * 1.45) + 0.5;
                    enhLut[i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(ev * 255.0)));
                }
            }

            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData srcData = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            BitmapData dstData = stream.DisplayStripBitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                int stride = Math.Abs(srcData.Stride);
                int bytes = stride * h;
                byte[] buffer = stream.ReusableDstPixels;
                if (buffer == null || buffer.Length < bytes)
                {
                    buffer = new byte[bytes];
                    stream.ReusableDstPixels = buffer;
                }
                Marshal.Copy(srcData.Scan0, buffer, 0, bytes);

                for (int y = 0; y < h; y++)
                {
                    bool rowEnhanced = false;
                    if (hasEnhanceRegions)
                    {
                        float yr = (float)y / Math.Max(1, h - 1);
                        for (int ri = 0; ri < stream.EnhanceRegions.Count; ri++)
                        {
                            EnhanceRegion er = stream.EnhanceRegions[ri];
                            if (er.Enabled && yr >= Math.Min(er.TopRatio, er.BottomRatio) && yr <= Math.Max(er.TopRatio, er.BottomRatio))
                            {
                                rowEnhanced = true;
                                break;
                            }
                        }
                    }

                    int rowOff = y * stride;
                    int pxCount = w * 3;
                    for (int x = 0; x + 2 < pxCount; x += 3)
                    {
                        int p = rowOff + x;
                        byte b = lutB[buffer[p]];
                        byte g = lutG[buffer[p + 1]];
                        byte r = lutR[buffer[p + 2]];
                        if (rowEnhanced && enhLut != null)
                        {
                            b = enhLut[b];
                            g = enhLut[g];
                            r = enhLut[r];
                        }
                        buffer[p] = b;
                        buffer[p + 1] = g;
                        buffer[p + 2] = r;
                    }
                }

                Marshal.Copy(buffer, 0, dstData.Scan0, bytes);
            }
            finally
            {
                src.UnlockBits(srcData);
                stream.DisplayStripBitmap.UnlockBits(dstData);
            }
        }

        public static void AutocropStream(SlitCameraStream stream, int leaderCols, int trailerCols)
        {
            if (stream == null || stream.RawStripBitmap == null) return;
            if (stream.ColumnMotionEnergy == null || stream.ColumnMotionEnergy.Length != stream.TotalColumns)
            {
                ComputeMotionEnergyProfile(stream);
            }
            float[] energy = stream.ColumnMotionEnergy;
            if (energy == null || energy.Length < 4) return;

            int first = -1;
            int last = -1;
            float threshold = 0.22f;
            for (int i = 0; i < energy.Length; i++)
            {
                if (energy[i] >= threshold)
                {
                    if (first < 0) first = i;
                    last = i;
                }
            }
            if (first < 0 || last <= first) return;

            int s = Math.Max(0, first - Math.Max(10, leaderCols));
            int e = Math.Min(stream.TotalColumns - 1, last + Math.Max(10, trailerCols));
            if (e - s < 20) return;

            if (stream.ActiveCrop != null) stream.CropUndoStack.Push(stream.ActiveCrop);
            stream.ActiveCrop = new CropWindow(s, e, 0f, 1f);
        }

        public static void CropToResultsStream(RaceEventDocument doc, SlitCameraStream stream, double leaderSec, double trailerSec, bool selectedOnly, CompetitorEntry selectedComp)
        {
            if (doc == null || stream == null || stream.TotalColumns < 10) return;
            double minT = double.MaxValue;
            double maxT = double.MinValue;

            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                CompetitorEntry c = doc.Competitors[i];
                if (!c.HasValidNumericTime) continue;
                if (selectedOnly && !ReferenceEquals(c, selectedComp)) continue;
                if (c.RawTimeSeconds < minT) minT = c.RawTimeSeconds;
                if (c.RawTimeSeconds > maxT) maxT = c.RawTimeSeconds;
            }
            if (minT >= double.MaxValue || maxT < minT) return;

            double colA = stream.TimeSecondsToColumn(minT - Math.Max(0.05, leaderSec));
            double colB = stream.TimeSecondsToColumn(maxT + Math.Max(0.05, trailerSec));
            int s = Math.Max(0, (int)Math.Floor(Math.Min(colA, colB)));
            int e = Math.Min(stream.TotalColumns - 1, (int)Math.Ceiling(Math.Max(colA, colB)));
            if (e - s < 16) return;

            if (stream.ActiveCrop != null) stream.CropUndoStack.Push(stream.ActiveCrop);
            stream.ActiveCrop = new CropWindow(s, e, 0f, 1f);
        }

        public static void UncropStream(SlitCameraStream stream)
        {
            if (stream == null) return;
            if (stream.CropUndoStack != null && stream.CropUndoStack.Count > 0)
            {
                stream.ActiveCrop = stream.CropUndoStack.Pop();
            }
            else
            {
                stream.ActiveCrop = null;
            }
        }

        public static void SampleWhiteBalanceAt(SlitCameraStream stream, double timeSeconds, float verticalRatio)
        {
            if (stream == null || stream.RawStripBitmap == null) return;
            int col = Math.Max(0, Math.Min(stream.TotalColumns - 1, (int)Math.Round(stream.TimeSecondsToColumn(timeSeconds))));
            int y = Math.Max(0, Math.Min(stream.RawStripBitmap.Height - 1, (int)Math.Round(verticalRatio * (stream.RawStripBitmap.Height - 1))));
            Color px = stream.RawStripBitmap.GetPixel(col, y);
            double avg = (px.R + px.G + px.B) / 3.0;
            if (avg < 8.0) return;

            stream.WhiteBalanceRed = (int)Math.Max(-80, Math.Min(80, Math.Round(avg - px.R)));
            stream.WhiteBalanceGreen = (int)Math.Max(-80, Math.Min(80, Math.Round(avg - px.G)));
            stream.WhiteBalanceBlue = (int)Math.Max(-80, Math.Min(80, Math.Round(avg - px.B)));
            UpdateDisplayBitmap(stream);
        }

        /// <summary>
        /// Computes automatic contrast/gamma enhancement parameters from the strip luminance distribution.
        /// Uses a single-row reusable buffer to avoid Large Object Heap allocations.
        /// </summary>
        public static void AutoEnhanceStream(SlitCameraStream stream)
        {
            if (stream == null || stream.RawStripBitmap == null) return;

            Bitmap src = stream.RawStripBitmap;
            int w = src.Width;
            int h = src.Height;
            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData data = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            long sumLum = 0;
            long count = 0;
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] rowBuf = new byte[stride];
                long baseAddr = data.Scan0.ToInt64();

                for (int y = 0; y < h; y += 4)
                {
                    Marshal.Copy(new IntPtr(baseAddr + (long)y * stride), rowBuf, 0, stride);
                    for (int x = 0; x < w; x += 4)
                    {
                        int idx = x * 3;
                        int lum = (rowBuf[idx] + rowBuf[idx + 1] * 2 + rowBuf[idx + 2]) >> 2;
                        sumLum += lum;
                        count++;
                    }
                }
            }
            finally
            {
                src.UnlockBits(data);
            }

            double avg = count > 0 ? (double)sumLum / count : 128.0;
            stream.BrightnessOffset = (int)Math.Max(-35, Math.Min(35, Math.Round((136.0 - avg) * 0.35)));
            stream.ContrastOffset = 14;
            stream.GammaValue = avg < 120.0 ? 1.18 : 1.08;
            UpdateDisplayBitmap(stream);
        }

        /// <summary>
        /// Computes the 1D horizontal motion/foreground energy profile across all scan columns
        /// using cache-friendly row-major traversal and reusable pixel buffers.
        /// </summary>
        public static void ComputeMotionEnergyProfile(SlitCameraStream stream)
        {
            if (stream == null || stream.RawStripBitmap == null) return;

            Bitmap bmp = stream.RawStripBitmap;
            int w = bmp.Width;
            int h = bmp.Height;
            float[] energy = new float[w];
            long[] colDiffSums = new long[w];

            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] rowBuf = new byte[stride];
                long baseAddr = data.Scan0.ToInt64();
                int refColOffset = Math.Min(2, w - 1) * 3;

                for (int y = 8; y < h - 8; y += 2)
                {
                    Marshal.Copy(new IntPtr(baseAddr + (long)y * stride), rowBuf, 0, stride);
                    int refB = rowBuf[refColOffset];
                    int refG = rowBuf[refColOffset + 1];
                    int refR = rowBuf[refColOffset + 2];

                    int idx = 0;
                    for (int x = 0; x < w; x++, idx += 3)
                    {
                        int db = rowBuf[idx] - refB;
                        if (db < 0) db = -db;
                        int dg = rowBuf[idx + 1] - refG;
                        if (dg < 0) dg = -dg;
                        int dr = rowBuf[idx + 2] - refR;
                        if (dr < 0) dr = -dr;
                        colDiffSums[x] += (db + dg + dr);
                    }
                }

                float invNorm = 1.0f / (Math.Max(1, (h - 16) / 2) * 3.0f);
                for (int x = 0; x < w; x++)
                {
                    energy[x] = colDiffSums[x] * invNorm;
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            stream.ColumnMotionEnergy = energy;
        }

        /// <summary>
        /// Refines a competitor's vertical ratio within a lane interval [minRatio, maxRatio]
        /// by locating the peak foreground contrast around the evaluated column.
        /// </summary>
        public static float DetectBestVerticalTorsoRatio(SlitCameraStream stream, double timeSec, float minRatio, float maxRatio, float fallbackRatio)
        {
            if (stream == null || stream.RawStripBitmap == null) return fallbackRatio;
            Bitmap bmp = stream.RawStripBitmap;
            int w = bmp.Width;
            int h = bmp.Height;
            if (w < 4 || h < 16) return fallbackRatio;

            int centerCol = (int)Math.Round(stream.TimeSecondsToColumn(timeSec));
            if (centerCol < 1 || centerCol >= w - 1) return fallbackRatio;

            int yMin = Math.Max(4, (int)(minRatio * h));
            int yMax = Math.Min(h - 5, (int)(maxRatio * h));
            if (yMax - yMin < 8) return fallbackRatio;

            // Margin inside the lane boundaries so we don't snap to white lane lines
            int margin = Math.Max(2, (yMax - yMin) / 6);
            yMin += margin;
            yMax -= margin;

            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                long baseAddr = data.Scan0.ToInt64();
                byte[] rowBuf = new byte[stride];
                int refCol = Math.Min(2, w - 1) * 3;
                int c0 = Math.Max(0, centerCol - 1) * 3;
                int c1 = centerCol * 3;
                int c2 = Math.Min(w - 1, centerCol + 1) * 3;

                int bestY = (yMin + yMax) / 2;
                int bestScore = -1;

                for (int y = yMin; y <= yMax; y += 2)
                {
                    Marshal.Copy(new IntPtr(baseAddr + (long)y * stride), rowBuf, 0, stride);
                    int rb = rowBuf[refCol], rg = rowBuf[refCol + 1], rr = rowBuf[refCol + 2];
                    int s1 = Math.Abs(rowBuf[c1] - rb) + Math.Abs(rowBuf[c1 + 1] - rg) + Math.Abs(rowBuf[c1 + 2] - rr);
                    int s0 = Math.Abs(rowBuf[c0] - rb) + Math.Abs(rowBuf[c0 + 1] - rg) + Math.Abs(rowBuf[c0 + 2] - rr);
                    int s2 = Math.Abs(rowBuf[c2] - rb) + Math.Abs(rowBuf[c2 + 1] - rg) + Math.Abs(rowBuf[c2 + 2] - rr);
                    int score = s1 * 2 + s0 + s2;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestY = y;
                    }
                }

                if (bestScore < 60) return fallbackRatio;
                return Math.Max(0.04f, Math.Min(0.96f, (float)bestY / h));
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private static ushort ReadU16AtDword(byte[] block, int blockOffset, int dwordIdx, int subWord)
        {
            int idx = (dwordIdx * 2 + subWord) * 2;
            if (idx < 0 || idx > 15998 || blockOffset + idx + 2 > block.Length) return 0;
            return (ushort)(block[blockOffset + idx] | (block[blockOffset + idx + 1] << 8));
        }

        private static short ReadI16AtDword(byte[] block, int blockOffset, int dwordIdx, int subWord)
        {
            return (short)ReadU16AtDword(block, blockOffset, dwordIdx, subWord);
        }

        private static uint ReadU32AtDword(byte[] block, int blockOffset, int dwordIdx)
        {
            int idx = dwordIdx * 4;
            if (idx < 0 || idx > 15996 || blockOffset + idx + 4 > block.Length) return 0;
            return (uint)(block[blockOffset + idx]
                        | (block[blockOffset + idx + 1] << 8)
                        | (block[blockOffset + idx + 2] << 16)
                        | (block[blockOffset + idx + 3] << 24));
        }

        private static void RefillBitBuffer16(byte[] block, ref int bytePos, int maxBytePos, ref uint bitBuf, ref int bitCnt)
        {
            if (bitCnt < 16)
            {
                uint w = 0;
                if (bytePos + 2 <= maxBytePos && bytePos + 2 <= block.Length)
                {
                    w = (uint)(block[bytePos] | (block[bytePos + 1] << 8));
                }
                bitBuf |= (w << bitCnt);
                bytePos += 2;
                bitCnt += 16;
            }
        }

        private static int DecodeRgb555DeltaChannel(ref uint bitBuf, ref int bitCnt, int baseVal)
        {
            if ((bitBuf & 1u) == 0)
            {
                bitBuf >>= 1;
                bitCnt -= 1;
                return baseVal & 0x1f;
            }
            if ((bitBuf & 2u) == 0)
            {
                int delta = (bitBuf & 4u) != 0 ? 0x1f : 1;
                bitBuf >>= 3;
                bitCnt -= 3;
                return (baseVal + delta) & 0x1f;
            }
            if ((bitBuf & 4u) == 0)
            {
                int delta = (bitBuf & 8u) != 0 ? 0x1e : 2;
                bitBuf >>= 4;
                bitCnt -= 4;
                return (baseVal + delta) & 0x1f;
            }
            int tblIdx = (int)((bitBuf >> 3) & 0x0fu);
            bitBuf >>= 7;
            bitCnt -= 7;
            return (baseVal + Delta555Table[tblIdx]) & 0x1f;
        }

        /// <summary>
        /// Natively decodes a FinishLynx .i01 or .i02 16,000-byte block stream into a SlitCameraStream.
        /// Supports Format v2 (RGB555 keyframe + pair/single RLE-Huffman delta),
        /// Format v3 (bit-packed 8-row differential block coder + 2/3 vertical companding),
        /// and Format v4 (IdentiLynx embedded JFIF/JPEG frame stream).
        /// </summary>
        public static SlitCameraStream LoadNativeLynxImageStream(
            string filePath,
            string cameraName,
            string positionLabel,
            int sensorHeight,
            int sensorMode,
            int sampleRateHz,
            long startMasterTicks,
            float[] calibratedLaneBoundaries)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
            byte[] fileBytes = File.ReadAllBytes(filePath);
            int numBlocks = fileBytes.Length / 16000;
            if (numBlocks < 2) return null;

            ushort w0First = ReadU16AtDword(fileBytes, 16000, 0, 0);
            ushort w1First = ReadU16AtDword(fileBytes, 16000, 0, 1);

            // Detect IdentiLynx JPEG frame stream (w1 == 0x0040)
            if ((w1First & 0x0040) != 0)
            {
                return DecodeIdentiLynxJpegStream(
                    fileBytes, numBlocks, filePath, cameraName, positionLabel, sampleRateHz, startMasterTicks);
            }

            // Count total columns across all 16,000-byte blocks (starting at block 1)
            int totalCols = 0;
            for (int blk = 1; blk < numBlocks; blk++)
            {
                int blkOff = blk * 16000;
                int blkCols = ReadU16AtDword(fileBytes, blkOff, 0, 0) & 0x3ff;
                if (blkCols > 0) totalCols += blkCols;
            }
            if (totalCols <= 0) return null;

            bool isV3 = ((w1First & 0x07) >= 3 && (w1First & 0x07) <= 7);
            int fmt = w1First & 0x0f;

            // Auto-detect sensor height if not provided by .evn
            if (sensorHeight <= 0)
            {
                short r0Len = ReadI16AtDword(fileBytes, 16000, 2, 0);
                if (!isV3)
                {
                    int hdrBase0 = ((w1First >> 6) & 3) + 1;
                    int tsStep0 = ((w1First >> 4) & 3) + 1;
                    int payloadDwords = Math.Max(125, r0Len - (hdrBase0 + tsStep0));
                    sensorHeight = payloadDwords * 2;
                }
                else
                {
                    sensorHeight = r0Len > 2500 ? 2000 : (r0Len > 820 ? 1360 : 1100);
                }
            }

            int decodeHeight = sensorHeight;
            bool expandVertical3Over2 = (isV3 && sensorMode == 4);
            if (expandVertical3Over2)
            {
                decodeHeight = ((sensorHeight * 2 + 3) / 3 + 7) & ~7;
            }
            else if (isV3)
            {
                decodeHeight = (sensorHeight + 7) & ~7;
            }

            int outHeight = sensorHeight;
            Bitmap rawBmp = new Bitmap(totalCols, outHeight, PixelFormat.Format24bppRgb);
            Rectangle rect = new Rectangle(0, 0, totalCols, outHeight);
            BitmapData bmpData = rawBmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            double[] colTimestamps = new double[totalCols];
            double tickScale = isV3 ? 8.0 : 5.0; // 10,000 Hz (8/80000) for v3, 16,000 Hz (5/80000) for v2
            if (sampleRateHz <= 0)
            {
                sampleRateHz = isV3 ? 2000 : 1000;
            }

            try
            {
                int stride = Math.Abs(bmpData.Stride);
                byte[] imgBytes = new byte[stride * outHeight];
                int globalCol = 0;

                ushort[] prevCol555 = new ushort[decodeHeight];
                ushort[] col0_555 = new ushort[decodeHeight];
                ushort[] col1_555 = new ushort[decodeHeight];

                // V3 scratch buffer: [10 cols][decodeHeight][4 channels]
                byte[] v3Cols = isV3 ? new byte[10 * decodeHeight * 4] : null;
                byte[] colPred = isV3 ? new byte[10 * 4] : null;
                byte[] rowDelta = isV3 ? new byte[8 * 4] : null;

                for (int blk = 1; blk < numBlocks && globalCol < totalCols; blk++)
                {
                    int blkOff = blk * 16000;
                    int blkCols = ReadU16AtDword(fileBytes, blkOff, 0, 0) & 0x3ff;
                    if (blkCols <= 0) continue;

                    ushort w1 = ReadU16AtDword(fileBytes, blkOff, 0, 1);
                    int hdrBase = ((w1 >> 6) & 3) + 1;
                    int tsStep = ((w1 >> 4) & 3) + 1;
                    int colGroup = ((w1 >> 8) & 3) + 1;

                    int d = 2;
                    int decodedInBlk = 0;

                    while (decodedInBlk < blkCols && d < 4000 && globalCol < totalCols)
                    {
                        int recLen = ReadI16AtDword(fileBytes, blkOff, d, 0);
                        if (recLen <= 0) recLen = 1;
                        if (d + recLen > 4000) recLen = 4000 - d;

                        short recHdr = ReadI16AtDword(fileBytes, blkOff, d, 1);
                        int recType = recHdr & 0x0f;
                        int recCols = ((recHdr >> 4) & 0x0f) + 1;
                        if (!isV3 && recCols > 2) recCols = 2;
                        if (isV3 && recCols > 10) recCols = 10;

                        int vStep = Math.Min(11, (recHdr >> 8) & 0x0f) + 1;
                        int hdrDwords = (recCols + (colGroup > 1 ? 1 : 0)) * tsStep + hdrBase;
                        int payloadByteStart = blkOff + (d + hdrDwords) * 4;
                        int payloadByteEnd = blkOff + (d + recLen) * 4;

                        if (payloadByteStart >= fileBytes.Length || payloadByteStart >= payloadByteEnd)
                        {
                            break;
                        }

                        int actualColsInRec = 1;

                        if (!isV3)
                        {
                            // Format v2: RGB555 (0x4c5e18 .. 0x4c667d)
                            int bytePos = payloadByteStart;
                            uint bitBuf = 0;
                            int bitCnt = 0;

                            if (recType == 1)
                            {
                                // Uncompressed RGB555 keyframe column
                                for (int y = 0; y < decodeHeight; y += vStep)
                                {
                                    ushort wVal = 0;
                                    if (bytePos + 2 <= payloadByteEnd)
                                    {
                                        wVal = (ushort)(fileBytes[bytePos] | (fileBytes[bytePos + 1] << 8));
                                        bytePos += 2;
                                    }
                                    for (int vs = 0; vs < vStep && (y + vs) < decodeHeight; vs++)
                                    {
                                        col0_555[y + vs] = wVal;
                                    }
                                }
                                Buffer.BlockCopy(col0_555, 0, prevCol555, 0, decodeHeight * 2);
                                WriteRgb555ColumnToBgr24(col0_555, imgBytes, stride, globalCol, outHeight);
                                actualColsInRec = 1;
                            }
                            else if (recType == 2)
                            {
                                // Pair-column RGB555 Huffman delta
                                RefillBitBuffer16(fileBytes, ref bytePos, payloadByteEnd, ref bitBuf, ref bitCnt);
                                for (int y = 0; y < decodeHeight; y += vStep)
                                {
                                    ushort w0Out, w1Out;
                                    if ((bitBuf & 1u) == 0)
                                    {
                                        w0Out = prevCol555[y];
                                        w1Out = prevCol555[y];
                                        bitBuf >>= 1;
                                        bitCnt -= 1;
                                        RefillBitBuffer16(fileBytes, ref bytePos, payloadByteEnd, ref bitBuf, ref bitCnt);
                                    }
                                    else
                                    {
                                        bitBuf >>= 1;
                                        bitCnt -= 1;
                                        ushort baseW = prevCol555[y];
                                        w0Out = 0;
                                        w1Out = 0;
                                        for (int shift = 10; shift >= 0; shift -= 5)
                                        {
                                            int cBase = (baseW >> shift) & 0x1f;
                                            int c0 = DecodeRgb555DeltaChannel(ref bitBuf, ref bitCnt, cBase);
                                            w0Out |= (ushort)(c0 << shift);
                                            int c1 = DecodeRgb555DeltaChannel(ref bitBuf, ref bitCnt, c0);
                                            w1Out |= (ushort)(c1 << shift);
                                            RefillBitBuffer16(fileBytes, ref bytePos, payloadByteEnd, ref bitBuf, ref bitCnt);
                                        }
                                    }
                                    for (int vs = 0; vs < vStep && (y + vs) < decodeHeight; vs++)
                                    {
                                        col0_555[y + vs] = w0Out;
                                        col1_555[y + vs] = w1Out;
                                    }
                                }
                                Buffer.BlockCopy(col1_555, 0, prevCol555, 0, decodeHeight * 2);
                                WriteRgb555ColumnToBgr24(col0_555, imgBytes, stride, globalCol, outHeight);
                                if (globalCol + 1 < totalCols)
                                {
                                    WriteRgb555ColumnToBgr24(col1_555, imgBytes, stride, globalCol + 1, outHeight);
                                }
                                actualColsInRec = 2;
                            }
                            else
                            {
                                // Single-column RGB555 delta (recType == 0)
                                RefillBitBuffer16(fileBytes, ref bytePos, payloadByteEnd, ref bitBuf, ref bitCnt);
                                for (int y = 0; y < decodeHeight; y++)
                                {
                                    RefillBitBuffer16(fileBytes, ref bytePos, payloadByteEnd, ref bitBuf, ref bitCnt);
                                    int wVal = prevCol555[y];
                                    if ((bitBuf & 7u) == 0)
                                    {
                                        bitBuf >>= 3;
                                        bitCnt -= 3;
                                    }
                                    else
                                    {
                                        // Red (bits 10..14)
                                        if ((bitBuf & 1u) == 0) { bitBuf >>= 1; bitCnt -= 1; }
                                        else if ((bitBuf & 7u) == 1u) { wVal = (wVal & 0x03ff) | ((wVal + 0x0800) & 0x7c00); bitBuf >>= 3; bitCnt -= 3; }
                                        else if ((bitBuf & 7u) == 5u) { wVal = (wVal & 0x03ff) | ((wVal - 0x0800) & 0x7c00); bitBuf >>= 3; bitCnt -= 3; }
                                        else { wVal = (wVal & 0x03ff) | ((int)(bitBuf << 8) & 0x7c00); bitBuf >>= 7; bitCnt -= 7; }

                                        // Green (bits 5..9)
                                        if ((bitBuf & 1u) == 0) { bitBuf >>= 1; bitCnt -= 1; }
                                        else if ((bitBuf & 7u) == 1u) { wVal = (wVal & 0x7c1f) | ((wVal + 0x0040) & 0x03e0); bitBuf >>= 3; bitCnt -= 3; }
                                        else if ((bitBuf & 7u) == 5u) { wVal = (wVal & 0x7c1f) | ((wVal - 0x0040) & 0x03e0); bitBuf >>= 3; bitCnt -= 3; }
                                        else { wVal = (wVal & 0x7c1f) | ((int)(bitBuf << 3) & 0x03e0); bitBuf >>= 7; bitCnt -= 7; }

                                        RefillBitBuffer16(fileBytes, ref bytePos, payloadByteEnd, ref bitBuf, ref bitCnt);

                                        // Blue (bits 0..4)
                                        if ((bitBuf & 1u) == 0) { bitBuf >>= 1; bitCnt -= 1; }
                                        else if ((bitBuf & 7u) == 1u) { wVal = (wVal & 0x7fe0) | ((wVal + 2) & 0x001f); bitBuf >>= 3; bitCnt -= 3; }
                                        else if ((bitBuf & 7u) == 5u) { wVal = (wVal & 0x7fe0) | ((wVal - 2) & 0x001f); bitBuf >>= 3; bitCnt -= 3; }
                                        else { wVal = (wVal & 0x7fe0) | ((int)(bitBuf >> 2) & 0x001f); bitBuf >>= 7; bitCnt -= 7; }
                                    }
                                    col0_555[y] = (ushort)wVal;
                                }
                                Buffer.BlockCopy(col0_555, 0, prevCol555, 0, decodeHeight * 2);
                                WriteRgb555ColumnToBgr24(col0_555, imgBytes, stride, globalCol, outHeight);
                                actualColsInRec = 1;
                            }
                        }
                        else
                        {
                            // Format v3: 8-row block differential coder (0x4c51d8 .. 0x4c5d2e)
                            int bc = w1 & 7;
                            bool isGray = (w1 & 8) != 0;
                            int shiftLeft = 8 - bc;
                            uint maskBc = (1u << bc) - 1u;
                            int smallBits = V3SmallBitsTable[bc - 3];
                            int bias = ((1 << (smallBits - 1)) << shiftLeft) & 0xff;
                            uint maskSmall = (1u << smallBits) - 1u;

                            int numCh = (recType & 2) != 0 ? 4 : 3;
                            int cleanRecType = recType & ~2;

                            int bytePos = payloadByteStart;
                            uint bitBuf = 0;
                            int bitCnt = 0;
                            RefillBitBuffer16(fileBytes, ref bytePos, payloadByteEnd, ref bitBuf, ref bitCnt);

                            if (cleanRecType == 1)
                            {
                                Array.Clear(colPred, 0, 4);
                                int blocksY = decodeHeight >> 3;
                                for (int blkY = 0; blkY < blocksY; blkY++)
                                {
                                    for (int chIdx = 0; chIdx < numCh; chIdx++)
                                    {
                                        int ch = V3ChannelOrder[chIdx];
                                        for (int r = 0; r < 8; r += 2)
                                        {
                                            byte v0 = (byte)(((bitBuf & maskBc) << shiftLeft) & 0xff);
                                            colPred[ch] = (byte)(colPred[ch] + v0);
                                            v3Cols[((blkY * 8 + r) * 4) + ch] = colPred[ch];
                                            bitBuf >>= bc;
                                            bitCnt -= bc;

                                            byte v1 = (byte)(((bitBuf & maskBc) << shiftLeft) & 0xff);
                                            colPred[ch] = (byte)(colPred[ch] + v1);
                                            v3Cols[((blkY * 8 + r + 1) * 4) + ch] = colPred[ch];
                                            bitBuf >>= bc;
                                            bitCnt -= bc;

                                            RefillBitBuffer16(fileBytes, ref bytePos, payloadByteEnd, ref bitBuf, ref bitCnt);
                                        }
                                    }
                                }
                                actualColsInRec = 1;
                            }
                            else
                            {
                                Array.Clear(colPred, 0, recCols * 4);
                                int blocksY = decodeHeight >> 3;
                                for (int blkY = 0; blkY < blocksY; blkY++)
                                {
                                    Array.Clear(rowDelta, 0, 32);
                                    for (int cIdx = 0; cIdx < recCols; cIdx++)
                                    {
                                        int colBase = cIdx * decodeHeight * 4;
                                        int predBase = cIdx * 4;
                                        for (int chIdx = 0; chIdx < numCh; chIdx++)
                                        {
                                            int ch = V3ChannelOrder[chIdx];
                                            if ((bitBuf & 1u) != 0)
                                            {
                                                bitBuf >>= 1;
                                                bitCnt -= 1;
                                                RefillBitBuffer16(fileBytes, ref bytePos, payloadByteEnd, ref bitBuf, ref bitCnt);
                                                for (int r = 0; r < 8; r++)
                                                {
                                                    colPred[predBase + ch] = (byte)(colPred[predBase + ch] + rowDelta[r * 4 + ch]);
                                                    v3Cols[colBase + ((blkY * 8 + r) * 4) + ch] = colPred[predBase + ch];
                                                }
                                            }
                                            else if ((bitBuf & 2u) != 0)
                                            {
                                                bitBuf >>= 2;
                                                bitCnt -= 2;
                                                for (int r = 0; r < 8; r += 2)
                                                {
                                                    int dv0 = (int)((bitBuf & maskBc) << shiftLeft) - bias;
                                                    rowDelta[r * 4 + ch] = (byte)(rowDelta[r * 4 + ch] + dv0);
                                                    colPred[predBase + ch] = (byte)(colPred[predBase + ch] + rowDelta[r * 4 + ch]);
                                                    v3Cols[colBase + ((blkY * 8 + r) * 4) + ch] = colPred[predBase + ch];
                                                    bitBuf >>= bc;
                                                    bitCnt -= bc;

                                                    int dv1 = (int)((bitBuf & maskBc) << shiftLeft) - bias;
                                                    rowDelta[(r + 1) * 4 + ch] = (byte)(rowDelta[(r + 1) * 4 + ch] + dv1);
                                                    colPred[predBase + ch] = (byte)(colPred[predBase + ch] + rowDelta[(r + 1) * 4 + ch]);
                                                    v3Cols[colBase + ((blkY * 8 + r + 1) * 4) + ch] = colPred[predBase + ch];
                                                    bitBuf >>= bc;
                                                    bitCnt -= bc;

                                                    RefillBitBuffer16(fileBytes, ref bytePos, payloadByteEnd, ref bitBuf, ref bitCnt);
                                                }
                                            }
                                            else
                                            {
                                                bitBuf >>= 2;
                                                bitCnt -= 2;
                                                for (int r = 0; r < 8; r += 2)
                                                {
                                                    for (int sub = 0; sub < 2; sub++)
                                                    {
                                                        int rr = r + sub;
                                                        if ((bitBuf & 1u) != 0)
                                                        {
                                                            bitBuf >>= 1;
                                                            bitCnt -= 1;
                                                        }
                                                        else
                                                        {
                                                            bitBuf >>= 1;
                                                            bitCnt -= 1;
                                                            int dv = (int)((bitBuf & maskSmall) << shiftLeft) - bias;
                                                            if ((dv & 0x80) == 0)
                                                            {
                                                                dv = (dv + (1 << shiftLeft)) & 0xff;
                                                            }
                                                            rowDelta[rr * 4 + ch] = (byte)(rowDelta[rr * 4 + ch] + dv);
                                                            bitBuf >>= smallBits;
                                                            bitCnt -= smallBits;
                                                        }
                                                        colPred[predBase + ch] = (byte)(colPred[predBase + ch] + rowDelta[rr * 4 + ch]);
                                                        v3Cols[colBase + ((blkY * 8 + rr) * 4) + ch] = colPred[predBase + ch];
                                                    }
                                                    RefillBitBuffer16(fileBytes, ref bytePos, payloadByteEnd, ref bitBuf, ref bitCnt);
                                                }
                                            }
                                        }
                                    }
                                }
                                actualColsInRec = recCols;
                            }

                            // Bit-replication to 8 bits and write to BGR24 image buffer
                            for (int cIdx = 0; cIdx < actualColsInRec && (globalCol + cIdx) < totalCols; cIdx++)
                            {
                                int dstCol = globalCol + cIdx;
                                int colBase = cIdx * decodeHeight * 4;
                                if (!expandVertical3Over2)
                                {
                                    for (int y = 0; y < outHeight && y < decodeHeight; y++)
                                    {
                                        int pOff = colBase + y * 4;
                                        byte r8 = v3Cols[pOff + 2];
                                        r8 |= (byte)(r8 >> bc);
                                        byte g8, b8;
                                        if (isGray)
                                        {
                                            g8 = r8;
                                            b8 = r8;
                                        }
                                        else
                                        {
                                            g8 = v3Cols[pOff + 1];
                                            g8 |= (byte)(g8 >> bc);
                                            b8 = v3Cols[pOff + 0];
                                            b8 |= (byte)(b8 >> bc);
                                        }
                                        int dstIdx = y * stride + dstCol * 3;
                                        imgBytes[dstIdx] = b8;
                                        imgBytes[dstIdx + 1] = g8;
                                        imgBytes[dstIdx + 2] = r8;
                                    }
                                }
                                else
                                {
                                    // 2/3 -> 3/2 vertical expansion (0x4c6a8b)
                                    for (int yOut = 0; yOut < outHeight; yOut++)
                                    {
                                        int ySrc = (yOut * 2) / 3;
                                        int yNext = Math.Min(decodeHeight - 1, ySrc + 1);
                                        int rem = (yOut * 2) % 3;
                                        int p0 = colBase + Math.Min(decodeHeight - 1, ySrc) * 4;
                                        int p1 = colBase + yNext * 4;

                                        byte b0 = v3Cols[p0], b1 = v3Cols[p1];
                                        byte g0 = v3Cols[p0 + 1], g1 = v3Cols[p1 + 1];
                                        byte r0 = v3Cols[p0 + 2], r1 = v3Cols[p1 + 2];
                                        b0 |= (byte)(b0 >> bc); b1 |= (byte)(b1 >> bc);
                                        g0 |= (byte)(g0 >> bc); g1 |= (byte)(g1 >> bc);
                                        r0 |= (byte)(r0 >> bc); r1 |= (byte)(r1 >> bc);

                                        byte bOut = rem == 0 ? b0 : (byte)((b0 * (3 - rem) + b1 * rem) / 3);
                                        byte gOut = rem == 0 ? g0 : (byte)((g0 * (3 - rem) + g1 * rem) / 3);
                                        byte rOut = rem == 0 ? r0 : (byte)((r0 * (3 - rem) + r1 * rem) / 3);

                                        int dstIdx = yOut * stride + dstCol * 3;
                                        imgBytes[dstIdx] = bOut;
                                        imgBytes[dstIdx + 1] = gOut;
                                        imgBytes[dstIdx + 2] = rOut;
                                    }
                                }
                            }
                        }

                        // Extract hardware column timestamps for actualColsInRec (0x4c433c .. 0x4c4577)
                        for (int cIdx = 0; cIdx < actualColsInRec && (globalCol + cIdx) < totalCols; cIdx++)
                        {
                            int tsIdx = cIdx / colGroup;
                            int tsRem = cIdx % colGroup;
                            uint rawTs0 = ReadU32AtDword(fileBytes, blkOff, d + hdrBase + tsIdx * tsStep);
                            double masterTicks = rawTs0 * tickScale;
                            if (tsStep == 2)
                            {
                                uint subWord = ReadU32AtDword(fileBytes, blkOff, d + hdrBase + tsIdx * tsStep + 1);
                                uint hiByte = (subWord >> 16) & 0xffu;
                                if ((hiByte & 0x80u) != 0)
                                {
                                    masterTicks += ((hiByte & 0x7fu) * 2) / 25.0;
                                }
                            }
                            if (tsRem != 0)
                            {
                                uint rawTs1 = ReadU32AtDword(fileBytes, blkOff, d + hdrBase + (tsIdx + 1) * tsStep);
                                double masterTicks1 = rawTs1 * tickScale;
                                masterTicks = 0.5 * (masterTicks + masterTicks1);
                            }
                            double sec = (masterTicks - startMasterTicks) / 80000.0;
                            colTimestamps[globalCol + cIdx] = sec;
                        }

                        globalCol += actualColsInRec;
                        decodedInBlk += actualColsInRec;
                        d += recLen;
                    }
                }

                // Ensure monotonic non-decreasing timestamps for binary search
                for (int i = 1; i < totalCols; i++)
                {
                    if (colTimestamps[i] <= colTimestamps[i - 1])
                    {
                        colTimestamps[i] = colTimestamps[i - 1] + (1.0 / Math.Max(100, sampleRateHz));
                    }
                }

                Marshal.Copy(imgBytes, 0, bmpData.Scan0, imgBytes.Length);
            }
            finally
            {
                rawBmp.UnlockBits(bmpData);
            }

            SlitCameraStream stream = new SlitCameraStream
            {
                CameraId = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
                DisplayName = string.IsNullOrEmpty(cameraName) ? Path.GetFileName(filePath) : cameraName,
                PositionLabel = string.IsNullOrEmpty(positionLabel) ? "FinishLine" : positionLabel,
                IsEnabled = true,
                SampleRateHz = sampleRateHz,
                FrameHeightPx = outHeight,
                CaptureStartSeconds = colTimestamps.Length > 0 ? colTimestamps[0] : 0.0,
                Orientation = StripOrientation.LeftToRight,
                RawStripBitmap = rawBmp,
                DisplayStripBitmap = rawBmp,
                ColumnTimestamps = colTimestamps,
                CalibratedLaneBoundaries = calibratedLaneBoundaries,
                SourceFilePath = filePath,
                NativeFormatSummary = string.Format(CultureInfo.InvariantCulture,
                    "Lynx {0} ({1}x{2} px, {3} Hz, {4} blocks)",
                    isV3 ? "EtherLynx 24-bit 8xBlock" : "EtherLynx RGB555 RLE",
                    totalCols, outHeight, sampleRateHz, numBlocks - 1)
            };

            ComputeMotionEnergyProfile(stream);
            return stream;
        }

        private static void WriteRgb555ColumnToBgr24(ushort[] col555, byte[] imgBytes, int stride, int colX, int height)
        {
            int xOff = colX * 3;
            for (int y = 0; y < height && y < col555.Length; y++)
            {
                int w = col555[y];
                int b = (w & 0x1f) << 3;
                int g = ((w >> 5) & 0x1f) << 3;
                int r = ((w >> 10) & 0x1f) << 3;
                b |= (b >> 5);
                g |= (g >> 5);
                r |= (r >> 5);
                int dst = y * stride + xOff;
                imgBytes[dst] = (byte)b;
                imgBytes[dst + 1] = (byte)g;
                imgBytes[dst + 2] = (byte)r;
            }
        }

        private static SlitCameraStream DecodeIdentiLynxJpegStream(
            byte[] fileBytes,
            int numBlocks,
            string filePath,
            string cameraName,
            string positionLabel,
            int sampleRateHz,
            long startMasterTicks)
        {
            List<byte[]> frameJpegBuffers = new List<byte[]>();
            List<double> frameTimes = new List<double>();

            int blk = 1;
            while (blk < numBlocks)
            {
                int blkOff = blk * 16000;
                ushort w1 = ReadU16AtDword(fileBytes, blkOff, 0, 1);
                if ((w1 & 0x0040) == 0) { blk++; continue; }

                bool isStart = false;
                bool prependSoi = false;
                if (blkOff + 24 <= fileBytes.Length)
                {
                    if (fileBytes[blkOff + 20] == 0xFF && fileBytes[blkOff + 21] == 0xD8)
                    {
                        isStart = true;
                        prependSoi = false;
                    }
                    else if (fileBytes[blkOff + 20] == 0x10 && fileBytes[blkOff + 21] == 0x4A &&
                             fileBytes[blkOff + 22] == 0x46 && fileBytes[blkOff + 23] == 0x49)
                    {
                        isStart = true;
                        prependSoi = true;
                    }
                }

                if (!isStart)
                {
                    blk++;
                    continue;
                }

                short recDwords = BitConverter.ToInt16(fileBytes, blkOff + 8);
                uint rawTs = BitConverter.ToUInt32(fileBytes, blkOff + 16);
                double sec = (rawTs * 80.0 - startMasterTicks) / 80000.0;

                using (MemoryStream ms = new MemoryStream())
                {
                    if (prependSoi)
                    {
                        ms.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 }, 0, 5);
                    }
                    int firstChunkLen = Math.Min(16000 - 20, Math.Max(0, recDwords * 4 - 12));
                    ms.Write(fileBytes, blkOff + 20, firstChunkLen);

                    int nextBlk = blk + 1;
                    while (nextBlk < numBlocks)
                    {
                        int nOff = nextBlk * 16000;
                        if (nOff + 24 <= fileBytes.Length)
                        {
                            if ((fileBytes[nOff + 20] == 0xFF && fileBytes[nOff + 21] == 0xD8) ||
                                (fileBytes[nOff + 20] == 0x10 && fileBytes[nOff + 21] == 0x4A &&
                                 fileBytes[nOff + 22] == 0x46 && fileBytes[nOff + 23] == 0x49))
                            {
                                break;
                            }
                        }
                        short nextRecDwords = BitConverter.ToInt16(fileBytes, nOff + 8);
                        int contChunkLen = Math.Min(16000 - 20, Math.Max(0, nextRecDwords * 4 - 12));
                        ms.Write(fileBytes, nOff + 20, contChunkLen);
                        nextBlk++;
                    }

                    byte[] fullJpeg = ms.ToArray();
                    int eoi = -1;
                    for (int k = fullJpeg.Length - 2; k >= 20; k--)
                    {
                        if (fullJpeg[k] == 0xFF && fullJpeg[k + 1] == 0xD9)
                        {
                            eoi = k;
                            break;
                        }
                    }

                    if (eoi > 0)
                    {
                        byte[] validJpeg = new byte[eoi + 2];
                        Array.Copy(fullJpeg, validJpeg, eoi + 2);
                        frameJpegBuffers.Add(validJpeg);
                        frameTimes.Add(sec);
                    }

                    blk = nextBlk;
                }
            }

            if (frameJpegBuffers.Count == 0) return null;

            int fw = 640;
            int fh = 480;
            try
            {
                using (MemoryStream ms = new MemoryStream(frameJpegBuffers[0]))
                using (Image img = Image.FromStream(ms, false, false))
                {
                    fw = img.Width;
                    fh = img.Height;
                }
            }
            catch
            {
                // Fallback default resolution
            }

            // Create a preview strip overview bitmap
            int previewFramesCount = Math.Min(frameJpegBuffers.Count, 32);
            int stepFrames = Math.Max(1, frameJpegBuffers.Count / previewFramesCount);
            int stripSliceW = 24;
            int totalW = previewFramesCount * stripSliceW;
            Bitmap previewStrip = new Bitmap(Math.Max(100, totalW), Math.Min(600, fh), PixelFormat.Format24bppRgb);
            double[] colTimestamps = new double[previewStrip.Width];

            using (Graphics g = Graphics.FromImage(previewStrip))
            {
                g.InterpolationMode = InterpolationMode.Bilinear;
                for (int i = 0; i < previewFramesCount; i++)
                {
                    int frameIdx = Math.Min(frameJpegBuffers.Count - 1, i * stepFrames);
                    try
                    {
                        using (MemoryStream ms = new MemoryStream(frameJpegBuffers[frameIdx]))
                        using (Image fImg = Image.FromStream(ms, false, false))
                        {
                            int dstX = i * stripSliceW;
                            int srcX = Math.Max(0, (fImg.Width - stripSliceW) / 2);
                            g.DrawImage(fImg,
                                new Rectangle(dstX, 0, stripSliceW, previewStrip.Height),
                                new Rectangle(srcX, 0, stripSliceW, fImg.Height),
                                GraphicsUnit.Pixel);

                            double t0 = frameTimes[frameIdx];
                            double t1 = (frameIdx + 1 < frameTimes.Count) ? frameTimes[frameIdx + 1] : (t0 + 0.05);
                            for (int s = 0; s < stripSliceW && (dstX + s) < previewStrip.Width; s++)
                            {
                                colTimestamps[dstX + s] = t0 + (t1 - t0) * ((double)s / stripSliceW);
                            }
                        }
                    }
                    catch
                    {
                        // Skip corrupted preview frame
                    }
                }
            }

            int detectedFps = 20;
            if (frameTimes.Count > 1)
            {
                double totalSpan = frameTimes[frameTimes.Count - 1] - frameTimes[0];
                if (totalSpan > 0.01)
                {
                    detectedFps = (int)Math.Max(1, Math.Round((frameTimes.Count - 1) / totalSpan));
                }
            }
            int effectiveFps = (sampleRateHz > 0 && sampleRateHz <= 120) ? sampleRateHz : (detectedFps > 0 ? detectedFps : 20);

            SlitCameraStream stream = new SlitCameraStream
            {
                CameraId = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
                DisplayName = string.IsNullOrEmpty(cameraName) ? "IdentiLynx Video" : cameraName,
                PositionLabel = string.IsNullOrEmpty(positionLabel) ? "Head-On Video" : positionLabel,
                IsEnabled = true,
                IsVideo = true,
                VideoFrameWidth = fw,
                VideoFrameHeight = fh,
                VideoFrameTimestamps = frameTimes,
                VideoFrameJpegBuffers = frameJpegBuffers,
                ActiveVideoFrameIndex = 0,
                SampleRateHz = effectiveFps,
                FrameHeightPx = fh,
                CaptureStartSeconds = frameTimes[0],
                Orientation = StripOrientation.LeftToRight,
                RawStripBitmap = previewStrip,
                DisplayStripBitmap = previewStrip,
                ColumnTimestamps = colTimestamps,
                SourceFilePath = filePath,
                NativeFormatSummary = string.Format(CultureInfo.InvariantCulture,
                    "IdentiLynx Video ({0} frames, {1}x{2} px @ {3} fps)", frameJpegBuffers.Count, fw, fh,
                    effectiveFps)
            };

            ComputeMotionEnergyProfile(stream);
            return stream;
        }

        /// <summary>
        /// Finds the next (or previous) foreground object leading edge column from the current column.
        /// </summary>
        public static int FindNextObjectColumn(SlitCameraStream stream, int currentColumn, int direction)
        {
            if (stream == null || stream.RawStripBitmap == null) return currentColumn;
            if (stream.ColumnMotionEnergy == null || stream.ColumnMotionEnergy.Length != stream.TotalColumns)
            {
                ComputeMotionEnergyProfile(stream);
            }

            float[] e = stream.ColumnMotionEnergy;
            int w = e.Length;
            if (w < 10) return currentColumn;

            float threshold = 18.0f;
            int step = direction >= 0 ? 1 : -1;
            int start = currentColumn + (step * 28);

            // First skip out of any currently active object region
            int x = Math.Max(0, Math.Min(w - 1, start));
            while (x >= 0 && x < w && e[x] > threshold * 1.4f)
            {
                x += step;
            }

            // Now scan until we cross the leading edge of the next runner
            while (x >= 2 && x < w - 2)
            {
                if (e[x] >= threshold && e[x + step] >= threshold * 1.15f)
                {
                    return x;
                }
                x += step;
            }

            return currentColumn;
        }

        /// <summary>
        /// Generates synchronized dual-camera slit-scan strips (Camera 1 Inside & Camera 2 Outside)
        /// for a RaceEventDocument based on its competitors and race parameters.
        /// </summary>
        public static void BuildSynthesizedCameraStrips(RaceEventDocument doc)
        {
            if (doc == null) return;

            for (int i = 0; i < doc.Cameras.Count; i++)
            {
                doc.Cameras[i].Dispose();
            }
            doc.Cameras.Clear();

            // Determine time window covering all valid finishers
            double minTime = double.MaxValue;
            double maxTime = double.MinValue;
            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                CompetitorEntry c = doc.Competitors[i];
                if (c.HasValidNumericTime)
                {
                    if (c.RawTimeSeconds < minTime) minTime = c.RawTimeSeconds;
                    if (c.RawTimeSeconds > maxTime) maxTime = c.RawTimeSeconds;
                }
            }

            if (minTime == double.MaxValue)
            {
                // Default window when start list is untimed: synthesize plausible target times for the visual strip
                minTime = Math.Max(9.5, doc.DistanceMeters / 9.2);
                maxTime = minTime + Math.Max(0.8, doc.DistanceMeters * 0.008);
            }

            double span = Math.Max(0.60, maxTime - minTime);
            // Choose sample rate so strip width is between 1400 and 3600 columns
            int sampleRateHz = span <= 1.8 ? 1000 : (span <= 4.5 ? 500 : (span <= 12.0 ? 250 : 120));
            double padBefore = Math.Max(0.18, Math.Min(0.45, span * 0.18));
            double padAfter = Math.Max(0.28, Math.Min(0.65, span * 0.24));

            double captureStartSec = Math.Max(0.0, Math.Floor((minTime - padBefore) * 100.0) / 100.0);
            double captureEndSec = Math.Ceiling((maxTime + padAfter) * 100.0) / 100.0;
            int totalColumns = Math.Max(900, Math.Min(4200, (int)Math.Round((captureEndSec - captureStartSec) * sampleRateHz)));
            int heightPx = 560;

            // Camera 1: Inside Primary Slit-Scan
            SlitCameraStream camInside = new SlitCameraStream
            {
                CameraId = "ETH-2000-IN",
                DisplayName = "Inside Finish Camera",
                PositionLabel = "Inside",
                IsEnabled = true,
                SampleRateHz = sampleRateHz,
                FrameHeightPx = heightPx,
                CaptureStartSeconds = captureStartSec,
                Orientation = StripOrientation.LeftToRight,
                GainPercent = 100
            };
            camInside.RawStripBitmap = RenderSlitScanStripBitmap(doc, camInside, totalColumns, heightPx, false);
            camInside.DisplayStripBitmap = camInside.RawStripBitmap;
            ComputeMotionEnergyProfile(camInside);
            doc.Cameras.Add(camInside);

            // Camera 2: Outside Reverse-Angle Slit-Scan
            SlitCameraStream camOutside = new SlitCameraStream
            {
                CameraId = "ETH-2000-OUT",
                DisplayName = "Outside Finish Camera",
                PositionLabel = "Outside",
                IsEnabled = true,
                SampleRateHz = sampleRateHz,
                FrameHeightPx = heightPx,
                CaptureStartSeconds = captureStartSec,
                Orientation = StripOrientation.LeftToRight,
                GainPercent = 100
            };
            camOutside.RawStripBitmap = RenderSlitScanStripBitmap(doc, camOutside, totalColumns, heightPx, true);
            camOutside.DisplayStripBitmap = camOutside.RawStripBitmap;
            ComputeMotionEnergyProfile(camOutside);
            doc.Cameras.Add(camOutside);

            // Ensure all timed competitors have their evaluated image coordinates linked
            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                CompetitorEntry c = doc.Competitors[i];
                if (c.HasValidNumericTime && !c.HasImageEvaluation)
                {
                    c.HasImageEvaluation = true;
                    c.EvaluatedTimeSeconds = c.RawTimeSeconds;
                    int effLane = c.Lane > 0 ? c.Lane : ((i % doc.LaneCount) + 1);
                    c.EvaluatedVerticalRatio = doc.LaneToCenterRatio(effLane);
                    c.EvaluatedCameraIndex = 0;
                }
            }
        }

        private static Bitmap RenderSlitScanStripBitmap(
            RaceEventDocument doc,
            SlitCameraStream stream,
            int width,
            int height,
            bool isOutsideCamera)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                float topCurbY = height * doc.LaneTopRatio;
                float bottomCurbY = height * doc.LaneBottomRatio;
                float trackHeight = bottomCurbY - topCurbY;
                int lanes = Math.Max(1, doc.LaneCount);
                float laneHeight = trackHeight / lanes;

                // 1. Infield grass / stadium border at top & bottom
                using (LinearGradientBrush topBrush = new LinearGradientBrush(
                    new RectangleF(0, 0, width, Math.Max(4f, topCurbY)),
                    Color.FromArgb(24, 62, 42),
                    Color.FromArgb(36, 86, 56),
                    90f))
                {
                    g.FillRectangle(topBrush, 0, 0, width, topCurbY);
                }

                using (LinearGradientBrush botBrush = new LinearGradientBrush(
                    new RectangleF(0, bottomCurbY, width, Math.Max(4f, height - bottomCurbY)),
                    Color.FromArgb(38, 44, 54),
                    Color.FromArgb(22, 27, 34),
                    90f))
                {
                    g.FillRectangle(botBrush, 0, bottomCurbY, width, height - bottomCurbY);
                }

                // 2. White finish-line curb rails
                using (SolidBrush curbBrush = new SolidBrush(Color.FromArgb(236, 240, 245)))
                {
                    g.FillRectangle(curbBrush, 0, topCurbY - 4f, width, 4f);
                    g.FillRectangle(curbBrush, 0, bottomCurbY, width, 4f);
                }

                // 3. Synthetic athletics track lanes (slit-scan illuminated finish line band)
                for (int ln = 0; ln < lanes; ln++)
                {
                    float ly = topCurbY + ln * laneHeight;
                    bool alt = (ln % 2 == 1);
                    Color baseTrack = isOutsideCamera
                        ? (alt ? Color.FromArgb(164, 58, 48) : Color.FromArgb(176, 64, 54))
                        : (alt ? Color.FromArgb(172, 62, 52) : Color.FromArgb(184, 70, 58));

                    // In a slit-scan camera aligned with the white 5cm finish line, the background has a bright
                    // matte track-white/terracotta blend so dark and colored jerseys have high contrast
                    Color slitLitTrack = Color.FromArgb(
                        (baseTrack.R + 215) / 2,
                        (baseTrack.G + 210) / 2,
                        (baseTrack.B + 210) / 2);

                    using (LinearGradientBrush laneBrush = new LinearGradientBrush(
                        new RectangleF(0, ly, width, laneHeight + 1f),
                        slitLitTrack,
                        Color.FromArgb(slitLitTrack.R - 12, slitLitTrack.G - 12, slitLitTrack.B - 12),
                        90f))
                    {
                        g.FillRectangle(laneBrush, 0, ly, width, laneHeight + 0.5f);
                    }

                    // Subtle horizontal sensor scan lines inside each lane
                    using (Pen microLine = new Pen(Color.FromArgb(22, 0, 0, 0), 1f))
                    {
                        g.DrawLine(microLine, 0, ly + laneHeight * 0.33f, width, ly + laneHeight * 0.33f);
                        g.DrawLine(microLine, 0, ly + laneHeight * 0.67f, width, ly + laneHeight * 0.67f);
                    }

                    // Crisp white 5cm lane boundary line
                    using (SolidBrush lineBrush = new SolidBrush(Color.FromArgb(245, 248, 252)))
                    {
                        g.FillRectangle(lineBrush, 0, ly - 1.5f, width, 3f);
                    }
                }

                // Bottom lane line
                using (SolidBrush lineBrush = new SolidBrush(Color.FromArgb(245, 248, 252)))
                {
                    g.FillRectangle(lineBrush, 0, bottomCurbY - 1.5f, width, 3f);
                }

                // 4. Render each athlete crossing the finish slit plane
                for (int i = 0; i < doc.Competitors.Count; i++)
                {
                    CompetitorEntry comp = doc.Competitors[i];
                    if (comp.Status != CompetitorStatus.Valid) continue;

                    double finishSec = comp.HasValidNumericTime
                        ? comp.RawTimeSeconds
                        : (stream.CaptureStartSeconds + 0.25 + i * 0.14);

                    double chestCol = stream.TimeSecondsToColumn(finishSec);
                    int lane = comp.Lane > 0 ? comp.Lane : ((i % lanes) + 1);
                    if (lane < 1) lane = 1;
                    if (lane > lanes) lane = lanes;

                    float laneCenterY = topCurbY + (lane - 0.5f) * laneHeight;
                    if (isOutsideCamera)
                    {
                        // Slight perspective offset for the outside camera
                        laneCenterY += (lane - (lanes * 0.5f)) * 1.8f;
                    }

                    int colorSeed = Math.Abs((comp.Affiliation ?? "").GetHashCode() + i * 17);
                    Color jerseyColor = TeamJerseyPalette[colorSeed % TeamJerseyPalette.Length];
                    Color skinColor = SkinTonePalette[(colorSeed / 3) % SkinTonePalette.Length];

                    DrawSlitScanAthlete(
                        g,
                        (float)chestCol,
                        laneCenterY,
                        laneHeight,
                        stream.SampleRateHz,
                        jerseyColor,
                        skinColor,
                        comp.BibId,
                        lane,
                        i,
                        isOutsideCamera);
                }
            }

            return bmp;
        }

        /// <summary>
        /// Draws a single athlete with realistic temporal slit-scan geometry:
        /// - The leading chest/pectoral edge is positioned at exact pixel column chestX.
        /// - The body extends to the right (later in time) from chestX.
        /// </summary>
        private static void DrawSlitScanAthlete(
            Graphics g,
            float chestX,
            float centerY,
            float laneHeight,
            int sampleRateHz,
            Color jerseyColor,
            Color skinColor,
            string bibId,
            int laneNumber,
            int seedIndex,
            bool isOutsideCam)
        {
            // Temporal width of torso crossing the line (~0.032s to 0.048s depending on speed)
            float timeScale = Math.Max(18f, Math.Min(48f, sampleRateHz * 0.038f));
            float torsoW = timeScale;
            float hScale = Math.Max(36f, Math.Min(78f, laneHeight * 0.92f));

            float headY = centerY - hScale * 0.34f;
            float chestY = centerY - hScale * 0.10f;
            float hipY = centerY + hScale * 0.14f;
            float footY = centerY + hScale * 0.44f;

            // Ground shadow under feet
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(55, 15, 23, 42)))
            {
                g.FillEllipse(shadowBrush, chestX - torsoW * 0.15f, footY - 4f, torsoW * 1.65f, 8f);
            }

            // Trailing leg (extended behind, crossing later in time: chestX + 0.55*torsoW .. chestX + 1.45*torsoW)
            using (Pen legPen = new Pen(skinColor, Math.Max(4f, hScale * 0.095f)))
            {
                legPen.StartCap = LineCap.Round;
                legPen.EndCap = LineCap.Round;
                PointF hipBack = new PointF(chestX + torsoW * 0.68f, hipY);
                PointF kneeBack = new PointF(chestX + torsoW * 1.05f, hipY + (footY - hipY) * 0.52f);
                PointF ankleBack = new PointF(chestX + torsoW * 1.42f, footY - 3f);
                g.DrawLines(legPen, new PointF[] { hipBack, kneeBack, ankleBack });

                // Leading leg (stepping through under/ahead of hip)
                PointF hipFront = new PointF(chestX + torsoW * 0.25f, hipY);
                PointF kneeFront = new PointF(chestX - torsoW * 0.04f, hipY + (footY - hipY) * 0.48f);
                PointF ankleFront = new PointF(chestX + torsoW * 0.18f, footY);
                g.DrawLines(legPen, new PointF[] { hipFront, kneeFront, ankleFront });
            }

            // Track spikes (shoes)
            using (SolidBrush shoeBrush = new SolidBrush(Color.FromArgb(248, 250, 252)))
            using (Pen shoeTrim = new Pen(jerseyColor, 1.5f))
            {
                RectangleF frontShoe = new RectangleF(chestX + torsoW * 0.02f, footY - 3f, torsoW * 0.28f, 6f);
                RectangleF backShoe = new RectangleF(chestX + torsoW * 1.28f, footY - 6f, torsoW * 0.26f, 6f);
                g.FillEllipse(shoeBrush, frontShoe);
                g.DrawEllipse(shoeTrim, frontShoe);
                g.FillEllipse(shoeBrush, backShoe);
                g.DrawEllipse(shoeTrim, backShoe);
            }

            // Running shorts (chestX + 0.14*torsoW .. chestX + 0.86*torsoW)
            Color shortsColor = Color.FromArgb(
                Math.Max(15, jerseyColor.R / 3),
                Math.Max(15, jerseyColor.G / 3),
                Math.Max(20, jerseyColor.B / 3));
            using (SolidBrush shortsBrush = new SolidBrush(shortsColor))
            {
                PointF[] shortsPoly = new PointF[]
                {
                    new PointF(chestX + torsoW * 0.10f, hipY - hScale * 0.06f),
                    new PointF(chestX + torsoW * 0.86f, hipY - hScale * 0.04f),
                    new PointF(chestX + torsoW * 0.92f, hipY + hScale * 0.14f),
                    new PointF(chestX + torsoW * 0.08f, hipY + hScale * 0.12f)
                };
                g.FillPolygon(shortsBrush, shortsPoly);
            }

            // Athletic Torso / Singlet:
            // CRITICAL: Leading chest edge touches EXACTLY chestX at chestY!
            using (GraphicsPath torsoPath = new GraphicsPath())
            {
                PointF[] torsoPts = new PointF[]
                {
                    new PointF(chestX, chestY),                                         // Exact leading pectoral/chest point!
                    new PointF(chestX + torsoW * 0.14f, headY + hScale * 0.10f),        // Front shoulder/clavicle
                    new PointF(chestX + torsoW * 0.78f, headY + hScale * 0.13f),        // Upper back
                    new PointF(chestX + torsoW * 0.84f, hipY - hScale * 0.03f),         // Lower back/waist
                    new PointF(chestX + torsoW * 0.12f, hipY - hScale * 0.04f)          // Abdomen
                };
                torsoPath.AddClosedCurve(torsoPts, 0.18f);

                using (LinearGradientBrush jerseyBrush = new LinearGradientBrush(
                    new RectangleF(chestX - 2f, headY, torsoW + 4f, hScale * 0.5f),
                    ShiftLuminance(jerseyColor, 28),
                    ShiftLuminance(jerseyColor, -28),
                    0f))
                {
                    g.FillPath(jerseyBrush, torsoPath);
                }

                // Crisp edge highlight on the leading chest so the operator can see the exact torso tangent
                using (Pen torsoEdgePen = new Pen(Color.FromArgb(215, 255, 255, 255), 1.2f))
                {
                    g.DrawPath(torsoEdgePen, torsoPath);
                }
            }

            // Head & Hair (leaning slightly at the finish, just above shoulder)
            float headW = torsoW * 0.44f;
            float headH = hScale * 0.18f;
            float headLeft = chestX + torsoW * 0.02f;
            RectangleF headRect = new RectangleF(headLeft, headY - headH * 0.35f, headW, headH);
            using (SolidBrush skinBrush = new SolidBrush(skinColor))
            {
                g.FillEllipse(skinBrush, headRect);
            }
            using (SolidBrush hairBrush = new SolidBrush(Color.FromArgb(28, 25, 23)))
            {
                g.FillPie(hairBrush, headRect.X, headRect.Y, headRect.Width, headRect.Height, 190f, 175f);
            }

            // Swinging Arm (curved temporal slit-scan arm stroke)
            using (Pen armPen = new Pen(skinColor, Math.Max(3.2f, hScale * 0.075f)))
            {
                armPen.StartCap = LineCap.Round;
                armPen.EndCap = LineCap.Round;
                float armDir = ((seedIndex % 2 == 0) ^ isOutsideCam) ? -1f : 1f;
                PointF shoulder = new PointF(chestX + torsoW * 0.34f, chestY - hScale * 0.04f);
                PointF elbow = new PointF(chestX + torsoW * (0.38f + 0.25f * armDir), chestY + hScale * 0.08f);
                PointF hand = new PointF(chestX + torsoW * (0.12f + 0.35f * armDir), chestY + hScale * 0.02f);
                g.DrawCurve(armPen, new PointF[] { shoulder, elbow, hand }, 0.35f);
            }

            // White Bib Number Patch on Torso/Hip
            string labelText = !string.IsNullOrEmpty(bibId) ? bibId : laneNumber.ToString(CultureInfo.InvariantCulture);
            RectangleF bibRect = new RectangleF(
                chestX + torsoW * 0.18f,
                chestY - hScale * 0.03f,
                Math.Max(16f, torsoW * 0.56f),
                Math.Max(11f, hScale * 0.18f));

            using (SolidBrush bibBg = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (Pen bibBorder = new Pen(Color.FromArgb(140, 30, 41, 59), 1f))
            {
                g.FillRectangle(bibBg, bibRect);
                g.DrawRectangle(bibBorder, bibRect.X, bibRect.Y, bibRect.Width, bibRect.Height);
            }

            using (Font bibFont = new Font("Segoe UI", Math.Max(6.5f, Math.Min(9.5f, bibRect.Height * 0.62f)), FontStyle.Bold))
            using (SolidBrush bibTextBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(labelText, bibFont, bibTextBrush, bibRect, sf);
            }
        }

        public static CropWindow ResolveEffectiveCrop(RaceEventDocument doc, SlitCameraStream cam, CropWindow cropOverride)
        {
            if (cropOverride != null) return cropOverride;
            if (cam != null && cam.ActiveCrop != null) return cam.ActiveCrop;
            if (doc != null && cam != null && cam.TotalColumns > 1 &&
                (doc.MarkedStartFrameTime.HasValue || doc.MarkedEndFrameTime.HasValue))
            {
                double t0 = doc.MarkedStartFrameTime ?? cam.ColumnToTimeSeconds(0);
                double t1 = doc.MarkedEndFrameTime ?? cam.ColumnToTimeSeconds(cam.TotalColumns - 1);
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

        private static bool MatchesResultsRange(int placeOrIndex, string rangeSpec)
        {
            if (string.IsNullOrWhiteSpace(rangeSpec)) return true;
            string[] tokens = rangeSpec.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i].Trim();
                int dash = tok.IndexOf('-');
                if (dash > 0)
                {
                    int a, b;
                    if (int.TryParse(tok.Substring(0, dash), out a) && int.TryParse(tok.Substring(dash + 1), out b))
                    {
                        int lo = Math.Min(a, b);
                        int hi = Math.Max(a, b);
                        if (placeOrIndex >= lo && placeOrIndex <= hi) return true;
                    }
                }
                else
                {
                    int v;
                    if (int.TryParse(tok, out v) && v == placeOrIndex) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Renders a direct cropped/selected image export (used by SelectionPopup -> Export... and Image -> Export...).
        /// </summary>
        public static Bitmap RenderDirectExportBitmap(RaceEventDocument doc, int cameraIndex, CropWindow cropOverride, bool includeOverlays)
        {
            if (doc == null || doc.Cameras.Count == 0)
            {
                return new Bitmap(640, 480, PixelFormat.Format24bppRgb);
            }

            int camIdx = Math.Max(0, Math.Min(doc.Cameras.Count - 1, cameraIndex));
            SlitCameraStream cam = doc.Cameras[camIdx];

            if (cam.IsVideo)
            {
                Bitmap frame = cam.GetVideoFrame(cam.ActiveVideoFrameIndex);
                if (frame == null) return new Bitmap(640, 480, PixelFormat.Format24bppRgb);
                double vScale = Math.Max(0.1, Math.Min(2.0, LynxExtendedConfig.Instance.ImageExportScale / 100.0));
                int vw = Math.Max(16, (int)Math.Round(frame.Width * vScale));
                int vh = Math.Max(16, (int)Math.Round(frame.Height * vScale));
                Bitmap vb = new Bitmap(vw, vh, PixelFormat.Format24bppRgb);
                using (Graphics vg = Graphics.FromImage(vb))
                {
                    vg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    vg.DrawImage(frame, new Rectangle(0, 0, vw, vh));
                }
                return vb;
            }

            UpdateDisplayBitmap(cam);
            Bitmap strip = cam.DisplayStripBitmap ?? cam.RawStripBitmap;
            if (strip == null) return new Bitmap(640, 480, PixelFormat.Format24bppRgb);

            CropWindow effCrop = ResolveEffectiveCrop(doc, cam, cropOverride);
            int sCol = effCrop != null ? Math.Max(0, Math.Min(strip.Width - 2, effCrop.StartCol)) : 0;
            int eCol = effCrop != null ? Math.Max(sCol + 1, Math.Min(strip.Width - 1, effCrop.EndCol)) : (strip.Width - 1);
            float vTop = effCrop != null ? Math.Max(0f, Math.Min(0.95f, effCrop.TopRatio)) : 0f;
            float vBot = effCrop != null ? Math.Max(vTop + 0.05f, Math.Min(1f, effCrop.BottomRatio)) : 1f;

            int srcW = Math.Max(2, eCol - sCol + 1);
            float srcY = vTop * strip.Height;
            float srcH = Math.Max(2f, (vBot - vTop) * strip.Height);

            double scale = Math.Max(0.1, Math.Min(4.0, LynxExtendedConfig.Instance.ImageExportScale / 100.0));
            double vStretch = Math.Max(0.5, Math.Min(3.0, cam.VerticalStretch > 0.1 ? cam.VerticalStretch : 1.0));
            int outW = Math.Max(16, Math.Min(8000, (int)Math.Round(srcW * scale)));
            int outH = Math.Max(16, Math.Min(4000, (int)Math.Round(srcH * scale * vStretch)));

            Bitmap outBmp = new Bitmap(outW, outH, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(outBmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.InterpolationMode = scale > 1.05 ? InterpolationMode.HighQualityBicubic : InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                RectangleF srcRect = new RectangleF(sCol, srcY, srcW, srcH);
                RectangleF dstRect = new RectangleF(0f, 0f, outW, outH);
                g.DrawImage(strip, dstRect, srcRect, GraphicsUnit.Pixel);

                if (includeOverlays)
                {
                    // Draw evaluated hash lines that intersect [sCol..eCol]
                    doc.RecalculatePlaces();
                    Color hashCol = LynxExtendedConfig.ColorFromName(LynxExtendedConfig.Instance.HashColorName, Color.Red);
                    using (Pen hashPen = new Pen(Color.FromArgb(220, hashCol), Math.Max(1f, LynxExtendedConfig.Instance.HashWidth)))
                    using (Font tagFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                    {
                        for (int i = 0; i < doc.Competitors.Count; i++)
                        {
                            CompetitorEntry c = doc.Competitors[i];
                            if (!c.HasValidNumericTime) continue;
                            double col = cam.TimeSecondsToColumn(c.RawTimeSeconds);
                            if (col < sCol || col > eCol) continue;
                            float px = (float)(((col - sCol) / Math.Max(1.0, eCol - sCol)) * outW);
                            g.DrawLine(hashPen, px, 0f, px, outH);

                            if (c.EvaluatedVerticalRatio >= vTop && c.EvaluatedVerticalRatio <= vBot)
                            {
                                float py = ((c.EvaluatedVerticalRatio - vTop) / Math.Max(0.01f, vBot - vTop)) * outH;
                                string tag = string.Format(CultureInfo.InvariantCulture, "#{0} {1} ({2})",
                                    c.Place > 0 ? c.Place.ToString(CultureInfo.InvariantCulture) : "-",
                                    !string.IsNullOrEmpty(c.LastName) ? c.LastName : c.BibId,
                                    c.GetFormattedOfficialTime(doc.Precision));
                                SizeF sz = g.MeasureString(tag, tagFont);
                                float bx = Math.Max(2f, Math.Min(outW - sz.Width - 10f, px + 3f));
                                float by = Math.Max(2f, Math.Min(outH - sz.Height - 6f, py - sz.Height * 0.5f));
                                using (SolidBrush pbg = new SolidBrush(Color.FromArgb(200, 15, 23, 42)))
                                {
                                    g.FillRectangle(pbg, bx, by, sz.Width + 6f, sz.Height + 3f);
                                    g.DrawString(tag, tagFont, Brushes.White, bx + 3f, by + 1.5f);
                                }
                            }
                        }
                    }

                    // Draw user overlays within cropped bounds
                    for (int oi = 0; oi < doc.Overlays.Count; oi++)
                    {
                        ImageOverlayItem ov = doc.Overlays[oi];
                        if (ov == null || !ov.Visible) continue;
                        int grpIdx = Math.Max(0, Math.Min(8, ov.GroupIndex));
                        if (doc.OverlayGroupVisible != null && !doc.OverlayGroupVisible[grpIdx]) continue;
                        double col = cam.TimeSecondsToColumn(ov.TimeSeconds);
                        if (ov.Kind != ImageOverlayKind.EventName && (col < sCol || col > eCol)) continue;
                        float sx = ov.Kind == ImageOverlayKind.EventName ? 8f : (float)(((col - sCol) / Math.Max(1.0, eCol - sCol)) * outW);
                        float sy = ov.Kind == ImageOverlayKind.EventName ? 8f : ((ov.VerticalRatio - vTop) / Math.Max(0.01f, vBot - vTop)) * outH;
                        using (Font ovFont = new Font(!string.IsNullOrEmpty(ov.FontName) ? ov.FontName : "Arial", Math.Max(7f, Math.Min(28f, ov.FontSizePt > 0 ? ov.FontSizePt : 10f)), ov.FontBold ? FontStyle.Bold : FontStyle.Regular))
                        using (SolidBrush fg = new SolidBrush(ov.OverlayColor.A > 0 ? ov.OverlayColor : Color.Yellow))
                        using (SolidBrush bg = new SolidBrush(Color.FromArgb(180, 15, 23, 42)))
                        {
                            if (ov.ShowLine)
                            {
                                using (Pen lp = new Pen(fg.Color, 1.4f)) g.DrawLine(lp, sx, 0f, sx, outH);
                            }
                            SizeF osz = g.MeasureString(ov.Text, ovFont);
                            g.FillRectangle(bg, sx, sy, osz.Width + 6f, osz.Height + 3f);
                            g.DrawString(ov.Text, ovFont, fg, sx + 3f, sy + 1.5f);
                        }
                    }
                }
            }
            return outBmp;
        }

        /// <summary>
        /// Renders a complete, official annotated photo-finish composite image (honoring active crop/selection
        /// and PrintDialogSettings: Zoom, Smooth, DPI, ImageHeight, LaneBars, PrintResults, MaxResults, Range).
        /// </summary>
        public static Bitmap RenderAnnotatedExportBitmap(RaceEventDocument doc, int cameraIndex)
        {
            return RenderAnnotatedExportBitmap(doc, cameraIndex, null, LynxExtendedConfig.Instance.PrintDialogOptions);
        }

        public static Bitmap RenderAnnotatedExportBitmap(RaceEventDocument doc, int cameraIndex, CropWindow cropOverride, PrintDialogSettings printCfg)
        {
            if (doc == null || doc.Cameras.Count == 0)
            {
                return new Bitmap(1200, 700, PixelFormat.Format24bppRgb);
            }

            if (printCfg == null) printCfg = new PrintDialogSettings();

            int camIdx = Math.Max(0, Math.Min(doc.Cameras.Count - 1, cameraIndex));
            SlitCameraStream cam = doc.Cameras[camIdx];

            if (cam.IsVideo)
            {
                Bitmap frame = cam.GetVideoFrame(cam.ActiveVideoFrameIndex);
                if (frame == null) return new Bitmap(960, 640, PixelFormat.Format24bppRgb);
                Bitmap vb = new Bitmap(frame.Width, frame.Height + 76, PixelFormat.Format24bppRgb);
                using (Graphics vg = Graphics.FromImage(vb))
                using (Font fTitle = new Font("Segoe UI", 13f, FontStyle.Bold))
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(15, 23, 42)))
                {
                    vg.Clear(Color.White);
                    vg.FillRectangle(dark, 0, 0, vb.Width, 64);
                    vg.DrawString(doc.EventName + " — IdentiLynx Video Frame", fTitle, Brushes.White, 14f, 16f);
                    vg.DrawImage(frame, 0, 70, frame.Width, frame.Height);
                }
                return vb;
            }

            UpdateDisplayBitmap(cam);
            Bitmap strip = cam.DisplayStripBitmap ?? cam.RawStripBitmap;
            if (strip == null) return new Bitmap(1200, 700, PixelFormat.Format24bppRgb);

            CropWindow effCrop = ResolveEffectiveCrop(doc, cam, cropOverride);
            int sCol = effCrop != null ? Math.Max(0, Math.Min(strip.Width - 2, effCrop.StartCol)) : 0;
            int eCol = effCrop != null ? Math.Max(sCol + 1, Math.Min(strip.Width - 1, effCrop.EndCol)) : (strip.Width - 1);
            float vTop = effCrop != null ? Math.Max(0f, Math.Min(0.95f, effCrop.TopRatio)) : 0f;
            float vBot = effCrop != null ? Math.Max(vTop + 0.05f, Math.Min(1f, effCrop.BottomRatio)) : 1f;

            int srcW = Math.Max(2, eCol - sCol + 1);
            float srcY = vTop * strip.Height;
            float srcH = Math.Max(2f, (vBot - vTop) * strip.Height);

            double tStart = cam.ColumnToTimeSeconds(sCol);
            double tEnd = cam.ColumnToTimeSeconds(eCol);
            double tMin = Math.Min(tStart, tEnd);
            double tMax = Math.Max(tStart, tEnd);

            doc.RecalculatePlaces();
            List<CompetitorEntry> sorted = new List<CompetitorEntry>();
            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                sorted.Add(doc.Competitors[i]);
            }
            sorted.Sort(delegate(CompetitorEntry a, CompetitorEntry b)
            {
                if (a.HasValidNumericTime && b.HasValidNumericTime) return a.RawTimeSeconds.CompareTo(b.RawTimeSeconds);
                if (a.HasValidNumericTime) return -1;
                if (b.HasValidNumericTime) return 1;
                return a.Lane.CompareTo(b.Lane);
            });

            List<CompetitorEntry> printableRows = new List<CompetitorEntry>();
            if (printCfg.PrintResultsOn)
            {
                int maxCap = Math.Max(1, printCfg.MaxResults);
                for (int i = 0; i < sorted.Count && printableRows.Count < maxCap; i++)
                {
                    int placeNum = sorted[i].Place > 0 ? sorted[i].Place : (i + 1);
                    if (MatchesResultsRange(placeNum, printCfg.ResultsRange))
                    {
                        printableRows.Add(sorted[i]);
                    }
                }
                if (printableRows.Count == 0)
                {
                    if (sorted.Count > 0)
                    {
                        for (int i = 0; i < sorted.Count; i++)
                        {
                            if (sorted[i].HasValidNumericTime) printableRows.Add(sorted[i]);
                        }
                        if (printableRows.Count == 0) printableRows.Add(sorted[0]);
                    }
                    else if (tMin <= 0.1 && tMax >= -0.1)
                    {
                        printableRows.Add(new CompetitorEntry
                        {
                            Place = 1,
                            Lane = 0,
                            BibId = "",
                            FirstName = "",
                            LastName = "",
                            Affiliation = "",
                            RawTimeSeconds = -0.0003,
                            EvaluatedTimeSeconds = -0.0003,
                            PrecisionOverride = TimePrecision.TenThousandths,
                            Status = CompetitorStatus.Valid,
                            HasImageEvaluation = true
                        });
                    }
                }
            }

            int zoom = Math.Max(1, Math.Min(8, printCfg.Zoom));
            int dpiFactor = printCfg.LimitResOn ? 1 : (printCfg.Dpi >= 1200 ? 2 : 1);
            int barW = printCfg.LaneBarsOn ? 24 : 0;
            int rawTargetW = Math.Max(880, Math.Min(3200, srcW * zoom * dpiFactor));
            int outW = Math.Max(1240, Math.Min(3680, rawTargetW + barW * 2 + 180));
            int maxInnerW = outW - barW * 2 - 160;
            int stripDrawW = rawTargetW < maxInnerW ? Math.Max(980, rawTargetW) : maxInnerW;
            stripDrawW = Math.Min(maxInnerW, stripDrawW);
            int stripLeft = (outW - stripDrawW) / 2;

            double heightScale = Math.Max(0.4, Math.Min(2.5, printCfg.UseCentimeters ? (printCfg.ImageHeight / 10.87) : (printCfg.ImageHeight / 4.28)));
            double vStretch = Math.Max(0.5, Math.Min(3.0, cam.VerticalStretch > 0.1 ? cam.VerticalStretch : 1.0));

            int headerH = 128;
            int stripTop = headerH + 22;
            int tableRowH = 30;
            int tableH = printCfg.PrintResultsOn ? (44 + Math.Max(1, printableRows.Count) * tableRowH + 28) : 16;
            int footerH = 44;

            int stripH = Math.Max(220, Math.Min(1600, (int)Math.Round(srcH * heightScale * vStretch)));
            int minPortraitH = (outW <= 1400) ? (int)Math.Round(outW * (842.0 / 595.0)) : 0;
            if (outW <= 1400 && srcH >= 700)
            {
                int availPortraitStripH = Math.Max(stripH, minPortraitH - stripTop - 52 - tableH - footerH - 20);
                int scaledPortrait = (int)Math.Round(availPortraitStripH * Math.Min(1.25, heightScale));
                stripH = Math.Max(stripH, Math.Min(availPortraitStripH, scaledPortrait));
            }
            int neededOutH = stripTop + stripH + 52 + tableH + footerH + 16;
            int outH = Math.Max(minPortraitH, neededOutH);

            Bitmap composite = new Bitmap(outW, outH, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(composite))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.White);

                // 1. Official White-Page Header (matching FinishLynx printout / afsaf.pdf)
                using (Font metaFont = new Font("Segoe UI", 9.5f, FontStyle.Regular))
                using (Font h1Font = new Font("Segoe UI", 12.5f, FontStyle.Bold))
                using (Font h2Font = new Font("Segoe UI", 11.5f, FontStyle.Bold))
                using (Font h3Font = new Font("Segoe UI", 11f, FontStyle.Bold))
                using (StringFormat centerFmt = new StringFormat { Alignment = StringAlignment.Center })
                using (StringFormat rightFmt = new StringFormat { Alignment = StringAlignment.Far })
                {
                    string printStamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                    g.DrawString(printStamp, metaFont, Brushes.Black, 20f, 18f);

                    string topLine = !string.IsNullOrEmpty(doc.CompetitionName)
                        ? (doc.CompetitionName + " - " + doc.EventName)
                        : doc.EventName;
                    g.DrawString(topLine, h1Font, Brushes.Black, new RectangleF(180f, 16f, outW - 360f, 28f), centerFmt);

                    string camLine = (!string.IsNullOrEmpty(cam.PositionLabel) ? cam.PositionLabel : "Photofinish") + " - " + cam.DisplayName;
                    g.DrawString(camLine, h2Font, Brushes.Black, new RectangleF(180f, 48f, outW - 360f, 26f), centerFmt);

                    string startLine = string.Format(CultureInfo.InvariantCulture, "Start: {0:dd/MM/yyyy HH:mm:ss.ff}", doc.GunTimeOfDay);
                    g.DrawString(startLine, h3Font, Brushes.Black, new RectangleF(180f, 80f, outW - 360f, 24f), centerFmt);

                    string serial = (cam.Profile != null && !string.IsNullOrEmpty(cam.Profile.SerialNumber))
                        ? cam.Profile.SerialNumber : "";
                    if (!string.IsNullOrEmpty(serial))
                    {
                        g.DrawString("Camera S/N: " + serial, metaFont, Brushes.Black, new RectangleF(outW - 240f, 48f, 220f, 22f), rightFmt);
                    }
                    if (doc.HasWindReading)
                    {
                        g.DrawString("Wind: " + doc.GetWindFormatted(), metaFont, Brushes.Black, new RectangleF(outW - 240f, 74f, 220f, 22f), rightFmt);
                    }
                }

                // 2. Draw Centered Cropped / Selected Strip Image
                Rectangle stripRect = new Rectangle(stripLeft, stripTop, stripDrawW, stripH);
                InterpolationMode prevInterp = g.InterpolationMode;
                g.InterpolationMode = printCfg.Smooth ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(strip,
                    new RectangleF(stripRect.Left, stripRect.Top, stripRect.Width, stripRect.Height),
                    new RectangleF(sCol, srcY, srcW, srcH),
                    GraphicsUnit.Pixel);
                g.InterpolationMode = prevInterp;
                g.PixelOffsetMode = PixelOffsetMode.Default;

                // 3. Left & Right Gray Lane Bars + Lane Boundary Lines (honors vertical crop vTop..vBot & ReverseLaneOrder)
                float[] calibBounds = cam.CalibratedLaneBoundaries ?? doc.CalibratedLaneBoundaries;
                bool hasCalib = (calibBounds != null && calibBounds.Length >= 2);
                int numLanes = hasCalib ? (calibBounds.Length - 1) : Math.Max(1, doc.LaneCount);
                if (printCfg.LaneBarsOn)
                {
                    Rectangle leftBar = new Rectangle(stripRect.Left - barW, stripRect.Top, barW, stripRect.Height);
                    Rectangle rightBar = new Rectangle(stripRect.Right, stripRect.Top, barW, stripRect.Height);
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(204, 204, 204)))
                    using (Font laneFont = new Font("Segoe UI", 10.5f, FontStyle.Regular))
                    using (StringFormat sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        g.FillRectangle(barBrush, leftBar);
                        g.FillRectangle(barBrush, rightBar);

                        for (int ln = 1; ln <= numLanes; ln++)
                        {
                            int dispLane = cam.ReverseLaneOrder ? (numLanes - ln + 1) : ln;
                            float r0 = hasCalib
                                ? calibBounds[ln - 1]
                                : (doc.LaneTopRatio + (doc.LaneBottomRatio - doc.LaneTopRatio) * ((float)(ln - 1) / Math.Max(1, numLanes)));
                            float r1 = hasCalib
                                ? calibBounds[ln]
                                : (doc.LaneTopRatio + (doc.LaneBottomRatio - doc.LaneTopRatio) * ((float)ln / Math.Max(1, numLanes)));
                            if (r1 < vTop || r0 > vBot) continue;

                            float clampedR0 = Math.Max(vTop, r0);
                            float clampedR1 = Math.Min(vBot, r1);
                            float y0 = stripRect.Top + ((clampedR0 - vTop) / Math.Max(0.01f, vBot - vTop)) * stripRect.Height;
                            float y1 = stripRect.Top + ((clampedR1 - vTop) / Math.Max(0.01f, vBot - vTop)) * stripRect.Height;
                            string laneStr = dispLane.ToString(CultureInfo.InvariantCulture);

                            g.DrawString(laneStr, laneFont, Brushes.Black,
                                new RectangleF(leftBar.Left, y0, leftBar.Width, Math.Max(14f, y1 - y0)), sfCenter);
                            g.DrawString(laneStr, laneFont, Brushes.Black,
                                new RectangleF(rightBar.Left, y0, rightBar.Width, Math.Max(14f, y1 - y0)), sfCenter);

                            if (ln < numLanes && r1 > vTop && r1 < vBot)
                            {
                                g.DrawLine(Pens.Black, leftBar.Left, y1, rightBar.Right, y1);
                            }
                        }
                    }
                }

                // Top & Bottom Black Time Ticks + Labels calibrated strictly to [sCol..eCol]
                double spanSec = Math.Max(0.001, tMax - tMin);
                double stepSec = spanSec <= 0.15 ? 0.01
                               : spanSec <= 0.45 ? 0.02
                               : spanSec <= 1.20 ? 0.05
                               : spanSec <= 3.00 ? 0.10
                               : spanSec <= 8.00 ? 0.25 : 0.50;
                double firstTick = Math.Ceiling(tMin / stepSec) * stepSec;

                using (Pen tickPen = new Pen(Color.Black, 1.4f))
                using (Font tickFont = new Font("Segoe UI", 9f, FontStyle.Regular))
                {
                    float zeroPx = -9999f;
                    if (tMin <= 0.0 && tMax >= 0.0)
                    {
                        double zeroCol = cam.TimeSecondsToColumn(0.0);
                        zeroPx = stripRect.Left + (float)(((zeroCol - sCol) / Math.Max(1.0, eCol - sCol)) * stripRect.Width);
                        if (zeroPx >= stripRect.Left && zeroPx <= stripRect.Right)
                        {
                            g.DrawLine(tickPen, zeroPx, stripRect.Top - 7, zeroPx, stripRect.Top);
                            g.DrawLine(tickPen, zeroPx, stripRect.Bottom, zeroPx, stripRect.Bottom + 7);
                            g.DrawString("0.00", tickFont, Brushes.Black, zeroPx - 14f, stripRect.Bottom + 9f);
                        }
                    }

                    float lastLabelPx = -9999f;
                    for (double t = firstTick; t <= tMax + stepSec * 0.25; t += stepSec)
                    {
                        double col = cam.TimeSecondsToColumn(t);
                        float px = stripRect.Left + (float)(((col - sCol) / Math.Max(1.0, eCol - sCol)) * stripRect.Width);
                        if (px < stripRect.Left || px > stripRect.Right) continue;
                        if (zeroPx > -9000f && Math.Abs(px - zeroPx) < 6f) continue;

                        if (stepSec >= 0.02 || Math.Abs((long)Math.Round(t * 100.0)) % 2 == 0)
                        {
                            g.DrawLine(tickPen, px, stripRect.Top - 7, px, stripRect.Top);
                            g.DrawLine(tickPen, px, stripRect.Bottom, px, stripRect.Bottom + 7);
                        }

                        if (zeroPx > -9000f && Math.Abs(px - zeroPx) < 52f) continue;
                        if (Math.Abs(px - lastLabelPx) < 64f) continue;

                        if (Math.Abs(t) >= 0.005)
                        {
                            string lbl = stepSec < 0.05 ? t.ToString("0.00", CultureInfo.InvariantCulture) : t.ToString("0.00", CultureInfo.InvariantCulture);
                            g.DrawString(lbl, tickFont, Brushes.Black, px - 14f, stripRect.Bottom + 9f);
                            lastLabelPx = px;
                        }
                    }
                }

                // 4. Draw Evaluated Competitor Hash Lines & Labels (skip for Zero Control Lane=0 rows so gun flash remains clean)
                using (Pen evalLinePen = new Pen(Color.FromArgb(230, 239, 68, 68), Math.Max(1.2f, LynxExtendedConfig.Instance.HashWidth)))
                using (Font tagFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                {
                    for (int i = 0; i < doc.Competitors.Count; i++)
                    {
                        CompetitorEntry c = doc.Competitors[i];
                        if (!c.HasValidNumericTime) continue;
                        if (c.Lane == 0 && string.IsNullOrEmpty(c.LastName)) continue;

                        double col = cam.TimeSecondsToColumn(c.RawTimeSeconds);
                        if (col < sCol || col > eCol) continue;
                        float px = stripRect.Left + (float)(((col - sCol) / Math.Max(1.0, eCol - sCol)) * stripRect.Width);
                        if (px < stripRect.Left || px > stripRect.Right) continue;

                        g.DrawLine(evalLinePen, px, stripRect.Top, px, stripRect.Bottom);

                        if (c.EvaluatedVerticalRatio >= vTop && c.EvaluatedVerticalRatio <= vBot)
                        {
                            float py = stripRect.Top + ((c.EvaluatedVerticalRatio - vTop) / Math.Max(0.01f, vBot - vTop)) * stripRect.Height;
                            string tag = string.Format(CultureInfo.InvariantCulture, "#{0} {1} ({2})",
                                c.Place, c.LastName, c.GetFormattedOfficialTime(doc.Precision));
                            SizeF sz = g.MeasureString(tag, tagFont);
                            float pillX = Math.Max(stripRect.Left + 2f, Math.Min(stripRect.Right - sz.Width - 10f, px + 4f));
                            float pillY = Math.Max(stripRect.Top + 4f, Math.Min(stripRect.Bottom - sz.Height - 6f, py - sz.Height - 2f));
                            RectangleF pill = new RectangleF(pillX, pillY, sz.Width + 8f, sz.Height + 4f);
                            using (SolidBrush pillBg = new SolidBrush(Color.FromArgb(225, 15, 23, 42)))
                            using (Pen pillBorder = new Pen(Color.FromArgb(248, 113, 113), 1f))
                            using (SolidBrush pillFg = new SolidBrush(Color.White))
                            {
                                g.FillRectangle(pillBg, pill);
                                g.DrawRectangle(pillBorder, pill.X, pill.Y, pill.Width, pill.Height);
                                g.DrawString(tag, tagFont, pillFg, pill.X + 4f, pill.Y + 2f);
                            }
                        }
                    }
                }

                // Overlays clipped to cropped area
                if (doc.Overlays != null)
                {
                    for (int oi = 0; oi < doc.Overlays.Count; oi++)
                    {
                        ImageOverlayItem ov = doc.Overlays[oi];
                        if (ov == null || !ov.Visible) continue;
                        int grpIdx = Math.Max(0, Math.Min(8, ov.GroupIndex));
                        if (doc.OverlayGroupVisible != null && !doc.OverlayGroupVisible[grpIdx]) continue;
                        double col = cam.TimeSecondsToColumn(ov.TimeSeconds);
                        if (ov.Kind != ImageOverlayKind.EventName && (col < sCol || col > eCol)) continue;
                        float sx = ov.Kind == ImageOverlayKind.EventName ? (stripRect.Left + 8f) : (stripRect.Left + (float)(((col - sCol) / Math.Max(1.0, eCol - sCol)) * stripRect.Width));
                        float sy = ov.Kind == ImageOverlayKind.EventName ? (stripRect.Top + 8f) : (stripRect.Top + ((ov.VerticalRatio - vTop) / Math.Max(0.01f, vBot - vTop)) * stripRect.Height);
                        using (Font ovFont = new Font(!string.IsNullOrEmpty(ov.FontName) ? ov.FontName : "Arial", Math.Max(7f, Math.Min(28f, ov.FontSizePt > 0 ? ov.FontSizePt : 10f)), ov.FontBold ? FontStyle.Bold : FontStyle.Regular))
                        using (SolidBrush fg = new SolidBrush(ov.OverlayColor.A > 0 ? ov.OverlayColor : Color.Yellow))
                        using (SolidBrush bg = new SolidBrush(Color.FromArgb(190, 15, 23, 42)))
                        {
                            SizeF osz = g.MeasureString(ov.Text, ovFont);
                            g.FillRectangle(bg, sx, sy, osz.Width + 6f, osz.Height + 3f);
                            g.DrawString(ov.Text, ovFont, fg, sx + 3f, sy + 1.5f);
                        }
                    }
                }

                // 5. Bottom Results Table (FinishLynx printout style matching afsaf.pdf)
                if (printCfg.PrintResultsOn)
                {
                    int tableTop = Math.Max(stripRect.Bottom + 44, outH - footerH - tableH - 12);
                    double tw = outW - 40.0;
                    float[] colX = new float[]
                    {
                        20f,
                        (float)(20.0 + tw * 0.060),
                        (float)(20.0 + tw * 0.122),
                        (float)(20.0 + tw * 0.160),
                        (float)(20.0 + tw * 0.298),
                        (float)(20.0 + tw * 0.545),
                        (float)(20.0 + tw * 0.775),
                        (float)(20.0 + tw * 0.892)
                    };
                    float[] colW = new float[]
                    {
                        (float)(tw * 0.052),
                        (float)(tw * 0.054),
                        (float)(tw * 0.030),
                        (float)(tw * 0.130),
                        (float)(tw * 0.238),
                        (float)(tw * 0.220),
                        (float)(tw * 0.108),
                        (float)(tw * 0.108)
                    };
                    string[] headers = new string[] { "Status", "Id", "Or", "First Name", "Last Name", "Affiliation", "<N/A>", "<N/A>" };

                    using (Font hdrFont = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                    using (Font rowFont = new Font("Segoe UI", 10f, FontStyle.Regular))
                    using (Pen rulePen = new Pen(Color.Black, 1.4f))
                    using (Pen segPen = new Pen(Color.Black, 1f))
                    using (StringFormat leftFmt = new StringFormat { Alignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    using (StringFormat rightFmt = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap })
                    {
                        for (int c = 0; c < headers.Length; c++)
                        {
                            StringFormat fmt = (c >= 6) ? rightFmt : leftFmt;
                            g.DrawString(headers[c], hdrFont, Brushes.Black, new RectangleF(colX[c], tableTop, colW[c], 24f), fmt);
                        }
                        g.DrawLine(rulePen, 20f, tableTop + 26f, outW - 20f, tableTop + 26f);

                        int ry = tableTop + 32;
                        for (int r = 0; r < printableRows.Count; r++)
                        {
                            CompetitorEntry comp = printableRows[r];
                            TimePrecision effPrec = comp.PrecisionOverride ?? doc.Precision;
                            string timeText = comp.GetFormattedOfficialTime(effPrec);
                            string deltaText = doc.FormatDeltaForCompetitor(comp, doc.DeltaMode);
                            if (string.IsNullOrEmpty(deltaText) && comp.HasValidNumericTime)
                            {
                                deltaText = CompetitorEntry.FormatSeconds(comp.RawTimeSeconds, effPrec);
                            }

                            string[] cells = new string[]
                            {
                                comp.GetPlaceDisplay(),
                                comp.BibId ?? "",
                                comp.Lane.ToString(CultureInfo.InvariantCulture),
                                comp.FirstName ?? "",
                                comp.LastName ?? "",
                                comp.Affiliation ?? "",
                                timeText,
                                deltaText
                            };

                            for (int c = 0; c < cells.Length; c++)
                            {
                                StringFormat fmt = (c >= 6) ? rightFmt : leftFmt;
                                g.DrawString(cells[c], rowFont, Brushes.Black, new RectangleF(colX[c], ry + 3f, colW[c], tableRowH - 6f), fmt);
                                g.DrawLine(segPen, colX[c], ry + tableRowH - 2f, colX[c] + colW[c], ry + tableRowH - 2f);
                            }
                            ry += tableRowH;
                        }
                    }
                }

                // 6. Official Bottom Footer
                using (Font footFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
                using (StringFormat rightFmt = new StringFormat { Alignment = StringAlignment.Far })
                {
                    float footerY = outH - 32f;
                    g.DrawString("Copyright (c) 1991-2024 Lynx System Developers, Inc.", footFont, Brushes.Black, 20f, footerY);
                    string sponsor = !string.IsNullOrEmpty(doc.SponsorName) ? doc.SponsorName : "OptiFinish Official Timing";
                    g.DrawString(sponsor, footFont, Brushes.Black, new RectangleF(outW - 360f, footerY, 340f, 20f), rightFmt);
                }
            }
            return composite;
        }

        public static void SaveBitmapToFile(Bitmap bmp, string filePath, int jpegQuality)
        {
            if (bmp == null || string.IsNullOrEmpty(filePath)) return;
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".pdf")
            {
                SaveBitmapToPdfFile(bmp, filePath, bmp.Width >= bmp.Height);
                return;
            }
            if (ext == ".tga")
            {
                SaveTga24(bmp, filePath);
                return;
            }
            if (ext == ".jpg" || ext == ".jpeg")
            {
                ImageCodecInfo jpegCodec = null;
                ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
                for (int i = 0; i < codecs.Length; i++)
                {
                    if (codecs[i].FormatID == ImageFormat.Jpeg.Guid)
                    {
                        jpegCodec = codecs[i];
                        break;
                    }
                }
                if (jpegCodec != null)
                {
                    using (EncoderParameters ep = new EncoderParameters(1))
                    {
                        long q = Math.Max(10L, Math.Min(100L, jpegQuality > 0 ? jpegQuality : 85));
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, q);
                        bmp.Save(filePath, jpegCodec, ep);
                        return;
                    }
                }
                bmp.Save(filePath, ImageFormat.Jpeg);
                return;
            }
            if (ext == ".bmp")
            {
                bmp.Save(filePath, ImageFormat.Bmp);
                return;
            }
            bmp.Save(filePath, ImageFormat.Png);
        }

        private static void SaveTga24(Bitmap bmp, string filePath)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            byte[] header = new byte[18];
            header[2] = 2; // Uncompressed True-Color
            header[12] = (byte)(w & 0xFF);
            header[13] = (byte)((w >> 8) & 0xFF);
            header[14] = (byte)(h & 0xFF);
            header[15] = (byte)((h >> 8) & 0xFF);
            header[16] = 24;
            header[17] = 0x20; // Top-to-bottom origin

            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = Math.Abs(bd.Stride);
                int rowBytes = w * 3;
                byte[] pixels = new byte[h * rowBytes];
                byte[] line = new byte[stride];
                long basePtr = bd.Scan0.ToInt64();
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(new IntPtr(basePtr + (long)y * stride), line, 0, rowBytes);
                    Buffer.BlockCopy(line, 0, pixels, y * rowBytes, rowBytes);
                }
                using (FileStream fs = File.Create(filePath))
                {
                    fs.Write(header, 0, 18);
                    fs.Write(pixels, 0, pixels.Length);
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
        }

        /// <summary>
        /// Writes a standard single-page PDF 1.4 file embedding the bitmap via DCTDecode (JPEG),
        /// completely bypassing the Windows Print Spooler so PDF export works even when all physical printers are offline.
        /// </summary>
        public static void SaveBitmapToPdfFile(Bitmap bmp, string filePath, bool landscape)
        {
            if (bmp == null || string.IsNullOrEmpty(filePath)) return;
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            int imgW = Math.Max(1, bmp.Width);
            int imgH = Math.Max(1, bmp.Height);
            byte[] jpgBytes;
            using (Bitmap rgb = new Bitmap(imgW, imgH, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(rgb))
                {
                    g.Clear(Color.White);
                    g.DrawImage(bmp, 0, 0, imgW, imgH);
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    ImageCodecInfo jpegCodec = null;
                    ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
                    for (int i = 0; i < codecs.Length; i++)
                    {
                        if (codecs[i].FormatID == ImageFormat.Jpeg.Guid)
                        {
                            jpegCodec = codecs[i];
                            break;
                        }
                    }
                    if (jpegCodec != null)
                    {
                        using (EncoderParameters ep = new EncoderParameters(1))
                        {
                            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                            rgb.Save(ms, jpegCodec, ep);
                        }
                    }
                    else
                    {
                        rgb.Save(ms, ImageFormat.Jpeg);
                    }
                    jpgBytes = ms.ToArray();
                }
            }

            double pageW = landscape ? 842.0 : 595.0;
            double pageH = landscape ? 595.0 : 842.0;
            double margin = 24.0;
            double availW = pageW - margin * 2.0;
            double availH = pageH - margin * 2.0;
            double scale = Math.Min(availW / imgW, availH / imgH);
            double drawW = imgW * scale;
            double drawH = imgH * scale;
            double drawX = margin + (availW - drawW) / 2.0;
            double drawY = margin + (availH - drawH) / 2.0;

            string contentStr = string.Format(CultureInfo.InvariantCulture,
                "q\n{0:0.###} 0 0 {1:0.###} {2:0.###} {3:0.###} cm\n/Im0 Do\nQ\n",
                drawW, drawH, drawX, drawY);
            byte[] contentBytes = System.Text.Encoding.ASCII.GetBytes(contentStr);

            long[] offsets = new long[6];
            using (FileStream fs = File.Create(filePath))
            {
                byte[] hdr = new byte[]
                {
                    (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'4', (byte)'\n',
                    (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n'
                };
                fs.Write(hdr, 0, hdr.Length);

                Action<string> writeAscii = delegate(string s)
                {
                    byte[] b = System.Text.Encoding.ASCII.GetBytes(s);
                    fs.Write(b, 0, b.Length);
                };

                offsets[1] = fs.Position;
                writeAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                offsets[2] = fs.Position;
                writeAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

                offsets[3] = fs.Position;
                writeAscii(string.Format(CultureInfo.InvariantCulture,
                    "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.###} {1:0.###}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n",
                    pageW, pageH));

                offsets[4] = fs.Position;
                writeAscii(string.Format(CultureInfo.InvariantCulture,
                    "4 0 obj\n<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>\nstream\n",
                    imgW, imgH, jpgBytes.Length));
                fs.Write(jpgBytes, 0, jpgBytes.Length);
                writeAscii("\nendstream\nendobj\n");

                offsets[5] = fs.Position;
                writeAscii(string.Format(CultureInfo.InvariantCulture,
                    "5 0 obj\n<< /Length {0} >>\nstream\n", contentBytes.Length));
                fs.Write(contentBytes, 0, contentBytes.Length);
                writeAscii("endstream\nendobj\n");

                long xrefPos = fs.Position;
                writeAscii("xref\n0 6\n0000000000 65535 f \n");
                for (int i = 1; i <= 5; i++)
                {
                    writeAscii(string.Format(CultureInfo.InvariantCulture, "{0:0000000000} 00000 n \n", offsets[i]));
                }
                writeAscii(string.Format(CultureInfo.InvariantCulture,
                    "trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{0}\n%%EOF\n", xrefPos));
            }
        }

        /// <summary>
        /// Exports the selected/cropped slit-scan region or IdentiLynx video stream to a standard MJPEG RIFF .avi video file.
        /// </summary>
        public static void ExportVideoSequence(RaceEventDocument doc, int cameraIndex, CropWindow cropOverride, string filePath)
        {
            if (doc == null || doc.Cameras.Count == 0 || string.IsNullOrEmpty(filePath)) return;
            int camIdx = Math.Max(0, Math.Min(doc.Cameras.Count - 1, cameraIndex));
            SlitCameraStream cam = doc.Cameras[camIdx];

            List<byte[]> jpegFrames = new List<byte[]>();
            int frameW = 640;
            int frameH = 480;
            int fps = 15;

            ImageCodecInfo jpegCodec = null;
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < codecs.Length; i++)
            {
                if (codecs[i].FormatID == ImageFormat.Jpeg.Guid) { jpegCodec = codecs[i]; break; }
            }

            if (cam.IsVideo && cam.VideoFrameJpegBuffers != null && cam.VideoFrameJpegBuffers.Count > 0)
            {
                fps = Math.Max(5, Math.Min(30, cam.SampleRateHz > 0 ? cam.SampleRateHz : 15));
                frameW = Math.Max(64, cam.VideoFrameWidth > 0 ? cam.VideoFrameWidth : 640);
                frameH = Math.Max(64, cam.VideoFrameHeight > 0 ? cam.VideoFrameHeight : 480);
                for (int i = 0; i < cam.VideoFrameJpegBuffers.Count; i++)
                {
                    jpegFrames.Add(cam.VideoFrameJpegBuffers[i]);
                }
            }
            else
            {
                using (Bitmap cropped = RenderDirectExportBitmap(doc, camIdx, cropOverride, true))
                {
                    frameW = Math.Min(800, Math.Max(320, cropped.Width));
                    frameH = Math.Min(600, Math.Max(240, cropped.Height));
                    frameW = (frameW + 3) & ~3;
                    frameH = (frameH + 3) & ~3;

                    int winW = Math.Min(cropped.Width, frameW);
                    int numFrames = cropped.Width > winW + 40 ? 45 : 24;
                    using (Bitmap canvas = new Bitmap(frameW, frameH, PixelFormat.Format24bppRgb))
                    using (Graphics g = Graphics.FromImage(canvas))
                    {
                        g.InterpolationMode = InterpolationMode.Bilinear;
                        for (int f = 0; f < numFrames; f++)
                        {
                            double t = numFrames > 1 ? (double)f / (numFrames - 1) : 0.0;
                            float srcX = (float)(t * Math.Max(0, cropped.Width - winW));
                            g.Clear(Color.Black);
                            g.DrawImage(cropped, new RectangleF(0, 0, frameW, frameH), new RectangleF(srcX, 0, winW, cropped.Height), GraphicsUnit.Pixel);
                            using (MemoryStream ms = new MemoryStream())
                            {
                                if (jpegCodec != null)
                                {
                                    using (EncoderParameters ep = new EncoderParameters(1))
                                    {
                                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Max(30, Math.Min(100, LynxExtendedConfig.Instance.VideoExportJpegQuality)));
                                        canvas.Save(ms, jpegCodec, ep);
                                    }
                                }
                                else
                                {
                                    canvas.Save(ms, ImageFormat.Jpeg);
                                }
                                jpegFrames.Add(ms.ToArray());
                            }
                        }
                    }
                }
            }

            if (jpegFrames.Count > 0)
            {
                WriteMjpegAvi(filePath, jpegFrames, frameW, frameH, fps);
            }
        }

        private static void WriteMjpegAvi(string filePath, List<byte[]> frames, int width, int height, int fps)
        {
            using (FileStream fs = File.Create(filePath))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                uint numFrames = (uint)frames.Count;
                uint usecPerFrame = (uint)(1000000 / Math.Max(1, fps));
                uint maxBuf = 0;
                for (int i = 0; i < frames.Count; i++) if ((uint)frames[i].Length > maxBuf) maxBuf = (uint)frames[i].Length;

                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                long riffSizePos = fs.Position;
                bw.Write((uint)0);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("AVI "));

                // LIST 'hdrl'
                bw.Write(System.Text.Encoding.ASCII.GetBytes("LIST"));
                long hdrlSizePos = fs.Position;
                bw.Write((uint)0);
                long hdrlStart = fs.Position;
                bw.Write(System.Text.Encoding.ASCII.GetBytes("hdrl"));

                // 'avih' (56 bytes)
                bw.Write(System.Text.Encoding.ASCII.GetBytes("avih"));
                bw.Write((uint)56);
                bw.Write(usecPerFrame);
                bw.Write((uint)(maxBuf * fps));
                bw.Write((uint)0);
                bw.Write((uint)0x10); // AVIF_HASINDEX
                bw.Write(numFrames);
                bw.Write((uint)0);
                bw.Write((uint)1);
                bw.Write(maxBuf);
                bw.Write((uint)width);
                bw.Write((uint)height);
                bw.Write((uint)0); bw.Write((uint)0); bw.Write((uint)0); bw.Write((uint)0);

                // LIST 'strl'
                bw.Write(System.Text.Encoding.ASCII.GetBytes("LIST"));
                long strlSizePos = fs.Position;
                bw.Write((uint)0);
                long strlStart = fs.Position;
                bw.Write(System.Text.Encoding.ASCII.GetBytes("strl"));

                // 'strh' (56 bytes)
                bw.Write(System.Text.Encoding.ASCII.GetBytes("strh"));
                bw.Write((uint)56);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("vids"));
                bw.Write(System.Text.Encoding.ASCII.GetBytes("MJPG"));
                bw.Write((uint)0);
                bw.Write((ushort)0); bw.Write((ushort)0);
                bw.Write((uint)0);
                bw.Write((uint)1);
                bw.Write((uint)fps);
                bw.Write((uint)0);
                bw.Write(numFrames);
                bw.Write(maxBuf);
                bw.Write((uint)0);
                bw.Write((uint)0);
                bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((ushort)width); bw.Write((ushort)height);

                // 'strf' (40 bytes BITMAPINFOHEADER)
                bw.Write(System.Text.Encoding.ASCII.GetBytes("strf"));
                bw.Write((uint)40);
                bw.Write((uint)40);
                bw.Write((int)width);
                bw.Write((int)height);
                bw.Write((ushort)1);
                bw.Write((ushort)24);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("MJPG"));
                bw.Write((uint)(width * height * 3));
                bw.Write((int)0); bw.Write((int)0); bw.Write((uint)0); bw.Write((uint)0);

                long strlEnd = fs.Position;
                fs.Position = strlSizePos; bw.Write((uint)(strlEnd - strlStart)); fs.Position = strlEnd;
                long hdrlEnd = fs.Position;
                fs.Position = hdrlSizePos; bw.Write((uint)(hdrlEnd - hdrlStart)); fs.Position = hdrlEnd;

                // LIST 'movi'
                bw.Write(System.Text.Encoding.ASCII.GetBytes("LIST"));
                long moviSizePos = fs.Position;
                bw.Write((uint)0);
                long moviStart = fs.Position;
                bw.Write(System.Text.Encoding.ASCII.GetBytes("movi"));

                uint[] offsets = new uint[frames.Count];
                uint[] lengths = new uint[frames.Count];
                for (int i = 0; i < frames.Count; i++)
                {
                    offsets[i] = (uint)(fs.Position - moviStart);
                    byte[] fBytes = frames[i];
                    lengths[i] = (uint)fBytes.Length;
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("00dc"));
                    bw.Write((uint)fBytes.Length);
                    bw.Write(fBytes);
                    if ((fBytes.Length & 1) != 0) bw.Write((byte)0);
                }

                long moviEnd = fs.Position;
                fs.Position = moviSizePos; bw.Write((uint)(moviEnd - moviStart)); fs.Position = moviEnd;

                // 'idx1'
                bw.Write(System.Text.Encoding.ASCII.GetBytes("idx1"));
                bw.Write((uint)(frames.Count * 16));
                for (int i = 0; i < frames.Count; i++)
                {
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("00dc"));
                    bw.Write((uint)0x10);
                    bw.Write(offsets[i]);
                    bw.Write(lengths[i]);
                }

                long riffEnd = fs.Position;
                fs.Position = riffSizePos;
                bw.Write((uint)(riffEnd - 8));
            }
        }

        private static Color ShiftLuminance(Color c, int delta)
        {
            int r = Math.Max(0, Math.Min(255, c.R + delta));
            int g = Math.Max(0, Math.Min(255, c.G + delta));
            int b = Math.Max(0, Math.Min(255, c.B + delta));
            return Color.FromArgb(c.A, r, g, b);
        }
    }
}
