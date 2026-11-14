using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace OptiFinish.Core
{
    public enum CompetitorStatus
    {
        Valid = 0,
        DNS = 1,       // Did Not Start
        FS = 2,        // False Start
        DNF = 3,       // Did Not Finish
        DQ = 4,        // Disqualified
        SCR = 5,       // Scratched
        ADV = 6        // Advanced
    }

    public enum TimePrecision
    {
        Seconds = 0,
        Tenths = 1,
        Hundredths = 2,
        Thousandths = 3,
        TenThousandths = 4,
        Fifths = 5
    }

    public enum DeltaReferenceMode
    {
        FromWinner = 0,
        FromPrevious = 1,
        FromNext = 2,
        LengthsAthletics = 3,
        LengthsCycling = 4,
        LengthsRowing = 5,
        CustomLength = 6
    }

    public enum ResultIdentifyMethod
    {
        Nothing = 0,
        Lane = 1,
        Id = 2,
        License = 3
    }

    public enum LaneBarPosition
    {
        Left = 0,
        Right = 1,
        Both = 2
    }

    public enum ImageOverlayKind
    {
        Text = 0,
        ClosestTime = 1,
        HashTime = 2,
        Lanes = 3,
        LineLabels = 4,
        EventName = 5
    }

    public sealed class ImageOverlayItem
    {
        public ImageOverlayKind Kind { get; set; }
        public int GroupIndex { get; set; }
        public double TimeSeconds { get; set; }
        public float VerticalRatio { get; set; }
        public string Text { get; set; }
        public Color OverlayColor { get; set; }
        public bool Visible { get; set; }
        public string FontName { get; set; }
        public float FontSizePt { get; set; }
        public bool FontBold { get; set; }
        public bool ShowLine { get; set; }
        public bool ShowSplits { get; set; }

        public ImageOverlayItem()
        {
            GroupIndex = 1;
            VerticalRatio = 0.1f;
            Text = "";
            OverlayColor = Color.Yellow;
            Visible = true;
            FontName = "Arial";
            FontSizePt = 12f;
            FontBold = true;
            ShowLine = true;
            ShowSplits = false;
        }
    }

    public sealed class PrintDialogSettings
    {
        public int Zoom { get; set; } // 1, 2, 4, 8
        public bool Smooth { get; set; }
        public int Dpi { get; set; } // 300, 600, 1200
        public double ImageHeight { get; set; } // default 4.28
        public bool UseCentimeters { get; set; } // false = Inches, true = Centimeters
        public bool LaneBarsOn { get; set; }
        public bool LimitResOn { get; set; }
        public int LimitResValue { get; set; } // default 300
        public bool PrintResultsOn { get; set; }
        public int MaxResults { get; set; } // default 10
        public string ResultsRange { get; set; } // e.g. "" or "1-3" or "1,3,5"
        public string PrinterName { get; set; } // "" = safe PDF / prompt default

        public PrintDialogSettings()
        {
            Zoom = 1;
            Smooth = true;
            Dpi = 300;
            ImageHeight = 4.28;
            UseCentimeters = false;
            LaneBarsOn = true;
            LimitResOn = true;
            LimitResValue = 300;
            PrintResultsOn = false;
            MaxResults = 10;
            ResultsRange = "";
            PrinterName = "";
        }

        public PrintDialogSettings Clone()
        {
            return new PrintDialogSettings
            {
                Zoom = Zoom,
                Smooth = Smooth,
                Dpi = Dpi,
                ImageHeight = ImageHeight,
                UseCentimeters = UseCentimeters,
                LaneBarsOn = LaneBarsOn,
                LimitResOn = LimitResOn,
                LimitResValue = LimitResValue,
                PrintResultsOn = PrintResultsOn,
                MaxResults = MaxResults,
                ResultsRange = ResultsRange ?? "",
                PrinterName = PrinterName ?? ""
            };
        }
    }

    public sealed class CropWindow
    {
        public int StartCol { get; set; }
        public int EndCol { get; set; }
        public float TopRatio { get; set; }
        public float BottomRatio { get; set; }

        public CropWindow(int startCol, int endCol, float topRatio, float bottomRatio)
        {
            StartCol = startCol;
            EndCol = endCol;
            TopRatio = topRatio;
            BottomRatio = bottomRatio;
        }
    }

    public sealed class EnhanceRegion
    {
        public float TopRatio { get; set; }
        public float BottomRatio { get; set; }
        public bool Enabled { get; set; }
        public double GammaBoost { get; set; }

        public EnhanceRegion(float top, float bottom)
        {
            TopRatio = Math.Max(0f, Math.Min(1f, top));
            BottomRatio = Math.Max(0f, Math.Min(1f, bottom));
            Enabled = true;
            GammaBoost = 0.65;
        }
    }

    public sealed class SplitLocationDef
    {
        public string Name { get; set; }
        public double CaptureStartSeconds { get; set; }
        public double CaptureDurationSeconds { get; set; }
        public int CameraIndex { get; set; }

        public SplitLocationDef()
        {
            Name = "Finish";
        }
    }

    public sealed class TimeTrialStartEntry
    {
        public int StartNumber { get; set; }
        public string Label { get; set; }
        public DateTime GunTimeOfDay { get; set; }
        public double OffsetSeconds { get; set; }
        public StartSignalKind Source { get; set; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Label)
                ? string.Format(CultureInfo.InvariantCulture, "Start {0} ({1:HH:mm:ss.ff})", StartNumber, GunTimeOfDay)
                : Label;
        }
    }

    public sealed class ResultFilterRule
    {
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public string FieldName { get; set; }
        public string LowerBound { get; set; }
        public string UpperBound { get; set; }
        public double StartOffsetSeconds { get; set; }
        public bool SingleResultOnly { get; set; }
        public int PlaceFilterMode { get; set; } // 0=None, 1=Enabled, 2=All

        public ResultFilterRule()
        {
            Name = "Filter 1";
            Enabled = true;
            FieldName = "Lane";
            LowerBound = "";
            UpperBound = "";
        }
    }

    public sealed class CameraSettingsProfile
    {
        public string SerialNumber { get; set; }
        public int RamMB { get; set; }
        public string IpAddress { get; set; }
        public int PixelDepthIndex { get; set; } // 0=512,1=4K,2=32K,3=256K,4=2M
        public string PixelMode { get; set; }
        public bool CompressionOn { get; set; }
        public int MinCompression { get; set; }
        public bool DoubleRate { get; set; }
        public bool FrameOffsetCenter { get; set; }
        public int FrameOffsetValue { get; set; }
        public int LuxBoost { get; set; }
        public bool GainMethodAgc { get; set; }
        public int AgcBrightness { get; set; }
        public int AgcFrequency { get; set; }
        public int AgcMaxGain { get; set; }
        public bool AutoIris { get; set; }
        public int AutoIrisMin { get; set; }
        public int AutoIrisMax { get; set; }
        public bool AutoLuxBoost { get; set; }
        public int AutoLuxMin { get; set; }
        public int AutoLuxMax { get; set; }
        public bool PhasedLightComp { get; set; }
        public bool IrisTypeP { get; set; }
        public bool SensorFilterIR { get; set; }
        public bool AutoBoot { get; set; }
        public int BlackLevel { get; set; }
        public int WhiteBalanceRed { get; set; }
        public int WhiteBalanceGreen { get; set; }
        public int WhiteBalanceBlue { get; set; }
        public bool CaptureManual { get; set; }
        public bool CaptureTimed { get; set; }
        public bool CapturePreCropped { get; set; }
        public bool CaptureAutomatic { get; set; }
        public bool CapturePhotoEyeA { get; set; }
        public int LeaderCols { get; set; }
        public int TrailerCols { get; set; }
        public int ObjectThreshold { get; set; }
        public int ObjectActiveTop { get; set; }
        public int ObjectActiveBottom { get; set; }
        public int ObjectSize { get; set; }
        public double ObjectMinTime { get; set; }
        public int PhotoEyeThreshold { get; set; }
        public int PhotoEyeActiveTop { get; set; }
        public int PhotoEyeActiveBottom { get; set; }
        public double LengthTimeSeconds { get; set; }
        public bool RadioLynxStartEnabled { get; set; }
        public bool RadioLynxSplitEnabled { get; set; }
        public int RadioLynxSensitivity { get; set; }
        public bool TimerExtSync { get; set; }

        public CameraSettingsProfile()
        {
            SerialNumber = "6000003";
            RamMB = 1024;
            IpAddress = "192.168.0.6";
            PixelDepthIndex = 2;
            PixelMode = "Zoom 100%";
            CompressionOn = true;
            MinCompression = 65;
            LuxBoost = 1;
            FrameOffsetCenter = true;
            GainMethodAgc = true;
            AgcBrightness = 60;
            AgcFrequency = 10;
            AgcMaxGain = 150;
            AutoIris = true;
            AutoIrisMin = 20;
            AutoIrisMax = 40;
            AutoLuxMin = 60;
            AutoLuxMax = 120;
            SensorFilterIR = true;
            AutoBoot = true;
            WhiteBalanceRed = 109;
            WhiteBalanceGreen = 0;
            WhiteBalanceBlue = 67;
            CaptureManual = true;
            LeaderCols = 25;
            TrailerCols = 25;
            ObjectThreshold = 10;
            ObjectActiveTop = 0;
            ObjectActiveBottom = 100;
            PhotoEyeThreshold = 15;
            PhotoEyeActiveBottom = 100;
            LengthTimeSeconds = 10.0;
        }
    }

    public enum SpeedMeasurementUnit
    {
        MetersPerSecond = 0,
        KilometersPerHour = 1,
        MilesPerHour = 2
    }

    public enum WindSpeedUnit
    {
        MetersPerSecond = 0,
        MilesPerHour = 1,
        FeetPerSecond = 2
    }

    public enum WindWindowMode
    {
        Manual = 0,
        Auto10s_100m = 1,
        Auto13s_Hurdles = 2,
        Auto5s_200m = 3
    }

    public enum StartSignalKind
    {
        ElectronicSensor = 0,
        ManualTrigger = 1,
        ImageOriginLine = 2,
        TimeOfDay = 3
    }

    public enum StripOrientation
    {
        LeftToRight = 0, // Time increases to the right (0.00s on left)
        RightToLeft = 1  // Time increases to the left (0.00s on right)
    }

    public enum SplitFlag
    {
        Accepted = 0,
        TooFast = 1,
        TooSlow = 2,
        Manual = 3
    }

    public sealed class LapSplitRecord
    {
        public int LapNumber { get; set; }
        public double CumulativeSeconds { get; set; }
        public double LapSeconds { get; set; }
        public SplitFlag Flag { get; set; }

        public LapSplitRecord()
        {
            LapNumber = 1;
            Flag = SplitFlag.Accepted;
        }

        public LapSplitRecord(int lapNumber, double cumulativeSec, double lapSec, SplitFlag flag)
        {
            LapNumber = lapNumber;
            CumulativeSeconds = cumulativeSec;
            LapSeconds = lapSec;
            Flag = flag;
        }
    }

    /// <summary>
    /// Represents a single 1D-over-time slit-scan camera stream (e.g., Inside Finish Camera or Outside Reverse Camera).
    /// </summary>
    public sealed class SlitCameraStream : IDisposable
    {
        public string CameraId { get; set; }
        public string DisplayName { get; set; }
        public string PositionLabel { get; set; }
        public bool IsEnabled { get; set; }
        public int SampleRateHz { get; set; }
        public int FrameHeightPx { get; set; }
        public double CaptureStartSeconds { get; set; }
        public StripOrientation Orientation { get; set; }
        public int GainPercent { get; set; }

        // Non-destructive display adjustments
        public int BrightnessOffset { get; set; }   // -100 .. +100
        public int ContrastOffset { get; set; }     // -100 .. +100
        public double GammaValue { get; set; }      // 0.20 .. 3.00 (1.00 = neutral)
        public double VerticalStretch { get; set; } // 0.50 .. 3.00 (1.00 = 100%)
        public int WhiteBalanceRed { get; set; }
        public int WhiteBalanceGreen { get; set; }
        public int WhiteBalanceBlue { get; set; }
        public int BlackLevel { get; set; }
        public bool EnhanceEnabled { get; set; }
        public bool AutoScrollNewImage { get; set; }
        public bool ReverseLaneOrder { get; set; }
        public ResultIdentifyMethod IdentifyBy { get; set; }
        public CameraSettingsProfile Profile { get; set; }
        public CropWindow ActiveCrop { get; set; }
        public Stack<CropWindow> CropUndoStack { get; private set; }
        public List<EnhanceRegion> EnhanceRegions { get; private set; }
        public bool IsCropped { get { return ActiveCrop != null; } }

        // Raw and adjusted bitmaps (X = scan column, Y = finish line vertical pixel)
        public Bitmap RawStripBitmap { get; set; }
        public Bitmap DisplayStripBitmap { get; set; }
        public float[] ColumnMotionEnergy { get; set; }

        // Native FinishLynx (.i01 / .i02) hardware stream metadata & non-uniform column timestamps
        public double[] ColumnTimestamps { get; set; }
        public float[] CalibratedLaneBoundaries { get; set; }
        public string SourceFilePath { get; set; }
        public string NativeFormatSummary { get; set; }
        internal byte[] ReusableDstPixels { get; set; }

        // IdentiLynx 2D Video stream extensions
        public bool IsVideo { get; set; }
        public int VideoFrameWidth { get; set; }
        public int VideoFrameHeight { get; set; }
        public List<double> VideoFrameTimestamps { get; set; }
        public List<byte[]> VideoFrameJpegBuffers { get; set; }
        public int ActiveVideoFrameIndex { get; set; }

        private readonly Dictionary<int, Bitmap> _videoFrameLruCache = new Dictionary<int, Bitmap>();
        private readonly List<int> _videoFrameLruOrder = new List<int>();
        private const int MaxCachedVideoFrames = 16;

        public int TotalColumns
        {
            get { return RawStripBitmap != null ? RawStripBitmap.Width : 0; }
        }

        public double CaptureDurationSeconds
        {
            get
            {
                if (ColumnTimestamps != null && ColumnTimestamps.Length > 1)
                {
                    double span = ColumnTimestamps[ColumnTimestamps.Length - 1] - ColumnTimestamps[0];
                    if (span > 0.0) return span;
                }
                if (SampleRateHz <= 0 || TotalColumns <= 0) return 0.0;
                return (double)TotalColumns / SampleRateHz;
            }
        }

        public SlitCameraStream()
        {
            CameraId = "CAM-1";
            DisplayName = "Primary Inside Slit";
            PositionLabel = "Inside";
            IsEnabled = true;
            SampleRateHz = 1000;
            FrameHeightPx = 560;
            CaptureStartSeconds = 0.0;
            Orientation = StripOrientation.LeftToRight;
            GainPercent = 100;
            BrightnessOffset = 0;
            ContrastOffset = 0;
            GammaValue = 1.00;
            VerticalStretch = 1.00;
            AutoScrollNewImage = true;
            IdentifyBy = ResultIdentifyMethod.Lane;
            Profile = new CameraSettingsProfile();
            CropUndoStack = new Stack<CropWindow>();
            EnhanceRegions = new List<EnhanceRegion>();
            SourceFilePath = "";
            NativeFormatSummary = "";
        }

        /// <summary>
        /// Converts a scan-line column index (0 .. Width-1) to race time in seconds.
        /// Uses exact hardware column timestamps when available from native .i01/.i02 blocks.
        /// </summary>
        public double ColumnToTimeSeconds(double columnX)
        {
            int w = TotalColumns;
            double effectiveCol = columnX;
            if (Orientation == StripOrientation.RightToLeft && w > 0)
            {
                effectiveCol = (w - 1) - columnX;
            }

            double[] ts = ColumnTimestamps;
            if (ts != null && ts.Length > 1)
            {
                int n = ts.Length;
                double step = SampleRateHz > 0 ? (1.0 / SampleRateHz) : ((ts[n - 1] - ts[0]) / Math.Max(1, n - 1));
                if (effectiveCol <= 0.0)
                {
                    return ts[0] + effectiveCol * step;
                }
                if (effectiveCol >= n - 1)
                {
                    return ts[n - 1] + (effectiveCol - (n - 1)) * step;
                }
                int idx = (int)effectiveCol;
                double frac = effectiveCol - idx;
                return ts[idx] + frac * (ts[idx + 1] - ts[idx]);
            }

            if (SampleRateHz <= 0) return CaptureStartSeconds;
            return CaptureStartSeconds + (effectiveCol / SampleRateHz);
        }

        /// <summary>
        /// Converts a race timestamp in seconds to a scan-line column coordinate.
        /// Uses binary search over hardware column timestamps when available.
        /// </summary>
        public double TimeSecondsToColumn(double timeSec)
        {
            int w = TotalColumns;
            double[] ts = ColumnTimestamps;
            double effectiveCol;

            if (ts != null && ts.Length > 1)
            {
                int n = ts.Length;
                double step = SampleRateHz > 0 ? (1.0 / SampleRateHz) : ((ts[n - 1] - ts[0]) / Math.Max(1, n - 1));
                if (step <= 1e-9) step = 0.001;

                if (timeSec <= ts[0])
                {
                    effectiveCol = (timeSec - ts[0]) / step;
                }
                else if (timeSec >= ts[n - 1])
                {
                    effectiveCol = (n - 1) + (timeSec - ts[n - 1]) / step;
                }
                else
                {
                    int pos = Array.BinarySearch(ts, timeSec);
                    if (pos >= 0)
                    {
                        effectiveCol = pos;
                    }
                    else
                    {
                        int ins = ~pos;
                        int lo = Math.Max(0, ins - 1);
                        int hi = Math.Min(n - 1, ins);
                        double dt = ts[hi] - ts[lo];
                        double frac = dt > 1e-9 ? (timeSec - ts[lo]) / dt : 0.0;
                        effectiveCol = lo + frac;
                    }
                }
            }
            else
            {
                if (SampleRateHz <= 0) return 0.0;
                effectiveCol = (timeSec - CaptureStartSeconds) * SampleRateHz;
            }

            if (Orientation == StripOrientation.RightToLeft && w > 0)
            {
                return (w - 1) - effectiveCol;
            }
            return effectiveCol;
        }

        public int TotalVideoFrames
        {
            get { return VideoFrameJpegBuffers != null ? VideoFrameJpegBuffers.Count : 0; }
        }

        public double ActiveVideoTimeSeconds
        {
            get
            {
                if (VideoFrameTimestamps != null && ActiveVideoFrameIndex >= 0 && ActiveVideoFrameIndex < VideoFrameTimestamps.Count)
                {
                    return VideoFrameTimestamps[ActiveVideoFrameIndex];
                }
                return CaptureStartSeconds;
            }
        }

        public Bitmap GetVideoFrame(int frameIndex)
        {
            if (VideoFrameJpegBuffers == null || frameIndex < 0 || frameIndex >= VideoFrameJpegBuffers.Count)
            {
                return null;
            }

            lock (_videoFrameLruCache)
            {
                Bitmap cachedBmp;
                if (_videoFrameLruCache.TryGetValue(frameIndex, out cachedBmp) && cachedBmp != null)
                {
                    _videoFrameLruOrder.Remove(frameIndex);
                    _videoFrameLruOrder.Add(frameIndex);
                    return cachedBmp;
                }

                byte[] jpegBytes = VideoFrameJpegBuffers[frameIndex];
                if (jpegBytes == null || jpegBytes.Length < 16) return null;

                try
                {
                    using (MemoryStream ms = new MemoryStream(jpegBytes))
                    using (Image rawImg = Image.FromStream(ms, false, false))
                    {
                        Bitmap bmp = new Bitmap(rawImg.Width, rawImg.Height, PixelFormat.Format24bppRgb);
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.DrawImage(rawImg, 0, 0, rawImg.Width, rawImg.Height);
                        }

                        _videoFrameLruCache[frameIndex] = bmp;
                        _videoFrameLruOrder.Add(frameIndex);

                        if (_videoFrameLruOrder.Count > MaxCachedVideoFrames)
                        {
                            int oldestKey = _videoFrameLruOrder[0];
                            _videoFrameLruOrder.RemoveAt(0);
                            Bitmap oldBmp;
                            if (_videoFrameLruCache.TryGetValue(oldestKey, out oldBmp))
                            {
                                _videoFrameLruCache.Remove(oldestKey);
                                oldBmp.Dispose();
                            }
                        }

                        return bmp;
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        public int GetFrameIndexForTime(double timeSeconds)
        {
            if (VideoFrameTimestamps == null || VideoFrameTimestamps.Count == 0) return 0;
            int count = VideoFrameTimestamps.Count;
            if (timeSeconds <= VideoFrameTimestamps[0]) return 0;
            if (timeSeconds >= VideoFrameTimestamps[count - 1]) return count - 1;

            int lo = 0;
            int hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                double midVal = VideoFrameTimestamps[mid];
                if (Math.Abs(midVal - timeSeconds) < 0.001) return mid;
                if (midVal < timeSeconds) lo = mid + 1;
                else hi = mid - 1;
            }

            int best = Math.Max(0, Math.Min(count - 1, lo));
            if (best > 0 && Math.Abs(VideoFrameTimestamps[best - 1] - timeSeconds) < Math.Abs(VideoFrameTimestamps[best] - timeSeconds))
            {
                best = best - 1;
            }
            return best;
        }

        public Bitmap GetVideoFrameForTime(double timeSeconds, out int bestIndex)
        {
            bestIndex = GetFrameIndexForTime(timeSeconds);
            ActiveVideoFrameIndex = bestIndex;
            return GetVideoFrame(bestIndex);
        }

        public void ClearVideoCache()
        {
            lock (_videoFrameLruCache)
            {
                foreach (KeyValuePair<int, Bitmap> kvp in _videoFrameLruCache)
                {
                    if (kvp.Value != null) kvp.Value.Dispose();
                }
                _videoFrameLruCache.Clear();
                _videoFrameLruOrder.Clear();
            }
        }

        public void Dispose()
        {
            ClearVideoCache();
            if (DisplayStripBitmap != null && !ReferenceEquals(DisplayStripBitmap, RawStripBitmap))
            {
                DisplayStripBitmap.Dispose();
                DisplayStripBitmap = null;
            }
            if (RawStripBitmap != null)
            {
                RawStripBitmap.Dispose();
                RawStripBitmap = null;
            }
        }
    }

    /// <summary>
    /// Represents a competitor entry and their evaluated timing data.
    /// </summary>
    public sealed class CompetitorEntry
    {
        public int Place { get; set; }
        public int Lane { get; set; }
        public string BibId { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string Affiliation { get; set; }
        public string License { get; set; }
        public string User1 { get; set; }
        public string User2 { get; set; }
        public string User3 { get; set; }

        /// <summary>
        /// Raw evaluated time in seconds (NaN or negative when not yet timed).
        /// </summary>
        public double RawTimeSeconds { get; set; }
        public string CustomTimeText { get; set; }
        public bool IsManualTime { get; set; }
        public double ReactionTimeSeconds { get; set; }
        public CompetitorStatus Status { get; set; }
        public bool IsPackLeader { get; set; }

        // Evaluation coordinates on the slit-scan image
        public bool HasImageEvaluation { get; set; }
        public double EvaluatedTimeSeconds { get; set; }
        public float EvaluatedVerticalRatio { get; set; } // 0.0 (top) .. 1.0 (bottom)
        public int EvaluatedCameraIndex { get; set; }
        public TimePrecision? PrecisionOverride { get; set; }

        public List<LapSplitRecord> Splits { get; private set; }

        public double GetCumulativeSplitSeconds()
        {
            if (Splits == null || Splits.Count == 0) return double.NaN;
            return Splits[Splits.Count - 1].CumulativeSeconds;
        }

        public double GetLastSplitSeconds()
        {
            if (Splits == null || Splits.Count == 0) return double.NaN;
            return Splits[Splits.Count - 1].LapSeconds;
        }

        public double GetBestSplitSeconds()
        {
            if (Splits == null || Splits.Count == 0) return double.NaN;
            double best = double.MaxValue;
            for (int i = 0; i < Splits.Count; i++)
            {
                if (Splits[i].LapSeconds > 0 && Splits[i].LapSeconds < best) best = Splits[i].LapSeconds;
            }
            return best < double.MaxValue ? best : double.NaN;
        }

        public bool HasValidNumericTime
        {
            get { return Status == CompetitorStatus.Valid && !double.IsNaN(RawTimeSeconds) && RawTimeSeconds > 0.0; }
        }

        public CompetitorEntry()
        {
            BibId = "";
            LastName = "";
            FirstName = "";
            Affiliation = "";
            License = "";
            User1 = "";
            User2 = "";
            User3 = "";
            RawTimeSeconds = double.NaN;
            CustomTimeText = "";
            ReactionTimeSeconds = double.NaN;
            Status = CompetitorStatus.Valid;
            EvaluatedVerticalRatio = 0.5f;
            Splits = new List<LapSplitRecord>();
        }

        /// <summary>
        /// Applies World Athletics Rule 165 (TR16) photo-finish ceiling rounding:
        /// Any fraction beyond the displayed precision rounds UP to the next unit when Precision is Hundredths/Tenths/Seconds.
        /// </summary>
        public static double ComputeRoundedTime(double rawSeconds, TimePrecision precision)
        {
            if (double.IsNaN(rawSeconds) || rawSeconds <= 0.0) return double.NaN;

            // Quantize to 0.1 ms (1/10000 s) first to eliminate IEEE-754 floating-point noise
            long tenThousandths = (long)Math.Round(rawSeconds * 10000.0);

            switch (precision)
            {
                case TimePrecision.Seconds:
                    return Math.Ceiling(tenThousandths / 10000.0);
                case TimePrecision.Fifths:
                    return Math.Ceiling(tenThousandths / 2000.0) * 0.2;
                case TimePrecision.Tenths:
                    return Math.Ceiling(tenThousandths / 1000.0) / 10.0;
                case TimePrecision.Hundredths:
                    return Math.Ceiling(tenThousandths / 100.0) / 100.0;
                case TimePrecision.Thousandths:
                    return Math.Ceiling(tenThousandths / 10.0) / 1000.0;
                case TimePrecision.TenThousandths:
                default:
                    return tenThousandths / 10000.0;
            }
        }

        public static string FormatSeconds(double seconds, TimePrecision precision)
        {
            if (double.IsNaN(seconds) || seconds <= 0.0) return "";

            double rounded = ComputeRoundedTime(seconds, precision);
            int decimals = (precision == TimePrecision.Fifths) ? 1 : Math.Max(0, Math.Min(4, (int)precision));
            string secFormat = decimals == 0 ? "0" : ("0." + new string('0', decimals));
            string paddedSecFormat = decimals == 0 ? "00" : ("00." + new string('0', decimals));

            if (rounded >= 3600.0)
            {
                int hrs = (int)(rounded / 3600.0);
                double rem = rounded - (hrs * 3600.0);
                int mins = (int)(rem / 60.0);
                double secs = rem - (mins * 60.0);
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2}",
                    hrs, mins, secs.ToString(paddedSecFormat, CultureInfo.InvariantCulture));
            }
            if (rounded >= 60.0)
            {
                int mins = (int)(rounded / 60.0);
                double secs = rounded - (mins * 60.0);
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1}",
                    mins, secs.ToString(paddedSecFormat, CultureInfo.InvariantCulture));
            }
            return rounded.ToString(secFormat, CultureInfo.InvariantCulture);
        }

        public string GetFormattedOfficialTime(TimePrecision precision)
        {
            if (Status != CompetitorStatus.Valid)
            {
                return string.IsNullOrEmpty(CustomTimeText) ? "" : CustomTimeText;
            }
            if (!string.IsNullOrEmpty(CustomTimeText) && (double.IsNaN(RawTimeSeconds) || RawTimeSeconds <= 0.0))
            {
                return CustomTimeText;
            }
            return FormatSeconds(RawTimeSeconds, precision);
        }

        public string GetPlaceDisplay()
        {
            if (Status != CompetitorStatus.Valid)
            {
                return Status.ToString();
            }
            return Place > 0 ? Place.ToString(CultureInfo.InvariantCulture) : "";
        }

        public string GetFormattedSpeed(double distanceMeters, SpeedMeasurementUnit unit)
        {
            if (!HasValidNumericTime || distanceMeters <= 0.0) return "";
            double mps = distanceMeters / RawTimeSeconds;
            switch (unit)
            {
                case SpeedMeasurementUnit.KilometersPerHour:
                    return (mps * 3.6).ToString("0.00", CultureInfo.InvariantCulture) + " km/h";
                case SpeedMeasurementUnit.MilesPerHour:
                    return (mps * 2.236936).ToString("0.00", CultureInfo.InvariantCulture) + " mph";
                case SpeedMeasurementUnit.MetersPerSecond:
                default:
                    return mps.ToString("0.00", CultureInfo.InvariantCulture) + " m/s";
            }
        }

        public string GetFormattedPace(double distanceMeters)
        {
            if (!HasValidNumericTime || distanceMeters <= 0.0) return "";
            double secPerKm = (RawTimeSeconds / distanceMeters) * 1000.0;
            int mins = (int)(secPerKm / 60.0);
            double secs = secPerKm - (mins * 60.0);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00.0}/km", mins, secs);
        }
    }

    /// <summary>
    /// Text annotation overlay placed on the slit-scan image.
    /// </summary>
    public sealed class ImageTextAnnotation
    {
        public double TimeSeconds { get; set; }
        public float VerticalRatio { get; set; }
        public string Text { get; set; }
    }

    /// <summary>
    /// Represents a complete race event with metadata, start signal, wind, cameras, and results.
    /// </summary>
    public sealed class RaceEventDocument : IDisposable
    {
        public int EventNumber { get; set; }
        public int RoundNumber { get; set; }
        public int HeatNumber { get; set; }
        public string EventName { get; set; }
        public double DistanceMeters { get; set; }
        public string CompetitionName { get; set; }
        public string SponsorName { get; set; }

        // Start & Capture State
        public bool IsArmed { get; set; }
        public bool IsCapturing { get; set; }
        public bool HasStartSignal { get; set; }
        public DateTime GunTimeOfDay { get; set; }
        public StartSignalKind StartSource { get; set; }
        public double StartOffsetSeconds { get; set; }

        // Wind Gauge State
        public double WindReading { get; set; }
        public bool HasWindReading { get; set; }
        public WindSpeedUnit WindUnit { get; set; }
        public WindWindowMode WindMode { get; set; }

        // LapTime Settings
        public int TotalLaps { get; set; }
        public int LapsPerSplit { get; set; }
        public double MinValidSplitSeconds { get; set; }
        public double MaxValidSplitSeconds { get; set; }

        // Display & Evaluation Options
        public int LaneCount { get; set; }
        public float LaneTopRatio { get; set; }    // Top of Lane 1 on the image (0..1)
        public float LaneBottomRatio { get; set; } // Bottom of Lane N on the image (0..1)
        public TimePrecision Precision { get; set; }
        public DeltaReferenceMode DeltaMode { get; set; }
        public DeltaReferenceMode Delta2Mode { get; set; }
        public DeltaReferenceMode Delta3Mode { get; set; }
        public SpeedMeasurementUnit SpeedUnit { get; set; }
        public double LengthUnitSeconds { get; set; } // For length delta calculation (e.g. 0.16s)

        // Extended FinishLynx Event/Image/Scoreboard/Filter state
        public bool SingleEventMode { get; set; }
        public bool IsTimeTrialMode { get; set; }
        public bool SmoothZoom { get; set; }
        public bool ShowEventNameOverlay { get; set; }
        public bool RetainOverlays { get; set; }
        public LaneBarPosition LaneBarSide { get; set; }
        public bool ShowTimeGapLines { get; set; }
        public double TimeGapIntervalSeconds { get; set; }
        public bool ShowSplitLines { get; set; }
        public Color ShowLinesColor { get; set; }
        public double? MarkedStartFrameTime { get; set; }
        public double? MarkedEndFrameTime { get; set; }
        public bool ScoreboardInitialized { get; set; }
        public bool ScoreboardRunning { get; set; }
        public bool ScoreboardHeld { get; set; }
        public bool ScoreboardShowTod { get; set; }
        public bool Scoreboard24Hour { get; set; }
        public string ScoreboardCustomMessage { get; set; }
        public bool FiltersEnabled { get; set; }
        public int SelectedStartIndex { get; set; }
        public int ActiveCaptureSplitIndex { get; set; }

        public List<SlitCameraStream> Cameras { get; private set; }
        public List<CompetitorEntry> Competitors { get; private set; }
        public List<ImageTextAnnotation> Annotations { get; private set; }
        public List<ImageOverlayItem> Overlays { get; private set; }
        public List<double> SplitMarkerSeconds { get; private set; }
        public List<SplitLocationDef> SplitLocations { get; private set; }
        public List<TimeTrialStartEntry> RecordedStarts { get; private set; }
        public List<ResultFilterRule> ResultFilters { get; private set; }
        public bool[] OverlayGroupVisible { get; private set; }
        public Dictionary<string, string> LapTimeSplitKeyMap { get; private set; }
        public Stack<Action> UndoActions { get; private set; }
        public float[] CalibratedLaneBoundaries { get; set; }
        public string SourceFilePath { get; set; }
        private List<CompetitorEntry> _cachedOrderedTimed;

        public RaceEventDocument()
        {
            EventNumber = 1;
            RoundNumber = 1;
            HeatNumber = 1;
            EventName = "100 Meter Dash";
            DistanceMeters = 100.0;
            CompetitionName = "National Athletics Championships";
            SponsorName = "OptiFinish Official Timing";
            GunTimeOfDay = DateTime.Now;
            HasStartSignal = true;
            StartSource = StartSignalKind.ElectronicSensor;
            WindReading = 0.8;
            HasWindReading = true;
            WindUnit = WindSpeedUnit.MetersPerSecond;
            WindMode = WindWindowMode.Auto10s_100m;
            TotalLaps = 1;
            LapsPerSplit = 1;
            MinValidSplitSeconds = 10.0;
            MaxValidSplitSeconds = 120.0;
            LaneCount = 8;
            LaneTopRatio = 0.12f;
            LaneBottomRatio = 0.92f;
            Precision = TimePrecision.Hundredths;
            DeltaMode = DeltaReferenceMode.FromWinner;
            Delta2Mode = DeltaReferenceMode.FromPrevious;
            Delta3Mode = DeltaReferenceMode.LengthsAthletics;
            SpeedUnit = SpeedMeasurementUnit.MetersPerSecond;
            LengthUnitSeconds = 0.20;
            SingleEventMode = true;
            LaneBarSide = LaneBarPosition.Left;
            TimeGapIntervalSeconds = 1.0;
            ShowLinesColor = Color.Yellow;
            ScoreboardCustomMessage = "";
            SourceFilePath = "";

            Cameras = new List<SlitCameraStream>();
            Competitors = new List<CompetitorEntry>();
            Annotations = new List<ImageTextAnnotation>();
            Overlays = new List<ImageOverlayItem>();
            SplitMarkerSeconds = new List<double>();
            SplitLocations = new List<SplitLocationDef>();
            SplitLocations.Add(new SplitLocationDef { Name = "Finish", CaptureStartSeconds = 0.0, CaptureDurationSeconds = 0.0, CameraIndex = 0 });
            RecordedStarts = new List<TimeTrialStartEntry>();
            ResultFilters = new List<ResultFilterRule>();
            OverlayGroupVisible = new bool[9];
            for (int gi = 0; gi < OverlayGroupVisible.Length; gi++) OverlayGroupVisible[gi] = true;
            LapTimeSplitKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            UndoActions = new Stack<Action>();
            _cachedOrderedTimed = new List<CompetitorEntry>();
        }

        public string GetEventCodeString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:000}-{1}-{2:00}", EventNumber, RoundNumber, HeatNumber);
        }

        public string GetWindFormatted()
        {
            if (!HasWindReading) return "NWI";
            string unitStr = WindUnit == WindSpeedUnit.MetersPerSecond ? "m/s"
                           : WindUnit == WindSpeedUnit.MilesPerHour ? "mph" : "fps";
            string sign = WindReading >= 0 ? "+" : "";
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:0.0} {2}", sign, WindReading, unitStr);
        }

        /// <summary>
        /// Maps a vertical image ratio (0..1) to a track lane number (1..LaneCount).
        /// Supports perspective-calibrated lane boundaries from native FinishLynx .evn files.
        /// </summary>
        public int RatioToLane(float verticalRatio)
        {
            if (CalibratedLaneBoundaries != null && CalibratedLaneBoundaries.Length >= 2)
            {
                int nLanes = CalibratedLaneBoundaries.Length - 1;
                for (int i = 0; i < nLanes; i++)
                {
                    if (verticalRatio <= CalibratedLaneBoundaries[i + 1])
                    {
                        return i + 1;
                    }
                }
                return nLanes;
            }
            if (LaneCount <= 0) return 1;
            float span = LaneBottomRatio - LaneTopRatio;
            if (span <= 0.05f) return 1;
            float rel = (verticalRatio - LaneTopRatio) / span;
            int lane = (int)Math.Floor(rel * LaneCount) + 1;
            if (lane < 1) lane = 1;
            if (lane > LaneCount) lane = LaneCount;
            return lane;
        }

        /// <summary>
        /// Maps a track lane number (1..LaneCount) to the center vertical ratio (0..1) of that lane.
        /// Supports perspective-calibrated lane boundaries from native FinishLynx .evn files.
        /// </summary>
        public float LaneToCenterRatio(int lane)
        {
            if (CalibratedLaneBoundaries != null && CalibratedLaneBoundaries.Length >= 2)
            {
                int nLanes = CalibratedLaneBoundaries.Length - 1;
                int idx = Math.Max(0, Math.Min(nLanes - 1, lane - 1));
                return 0.5f * (CalibratedLaneBoundaries[idx] + CalibratedLaneBoundaries[idx + 1]);
            }
            if (LaneCount <= 0) return 0.5f;
            int clamped = Math.Max(1, Math.Min(LaneCount, lane));
            float span = LaneBottomRatio - LaneTopRatio;
            return LaneTopRatio + span * ((clamped - 0.5f) / LaneCount);
        }

        /// <summary>
        /// Recalculates official places (handling ties at current precision and status codes).
        /// </summary>
        public void RecalculatePlaces()
        {
            _cachedOrderedTimed.Clear();

            for (int i = 0; i < Competitors.Count; i++)
            {
                CompetitorEntry c = Competitors[i];
                if (c.Status != CompetitorStatus.Valid)
                {
                    c.Place = 0;
                }
                else if (c.HasValidNumericTime)
                {
                    _cachedOrderedTimed.Add(c);
                }
                else
                {
                    c.Place = 0;
                }
            }

            // Sort valid timed competitors by raw time ascending
            _cachedOrderedTimed.Sort(delegate(CompetitorEntry a, CompetitorEntry b)
            {
                return a.RawTimeSeconds.CompareTo(b.RawTimeSeconds);
            });

            for (int i = 0; i < _cachedOrderedTimed.Count; i++)
            {
                if (i > 0)
                {
                    // Check exact tie at TenThousandths (0.0001s)
                    long prevTicks = (long)Math.Round(_cachedOrderedTimed[i - 1].RawTimeSeconds * 10000.0);
                    long currTicks = (long)Math.Round(_cachedOrderedTimed[i].RawTimeSeconds * 10000.0);
                    if (prevTicks == currTicks)
                    {
                        _cachedOrderedTimed[i].Place = _cachedOrderedTimed[i - 1].Place;
                        continue;
                    }
                }
                _cachedOrderedTimed[i].Place = i + 1;
            }
        }

        /// <summary>
        /// Formats the Delta column for a competitor according to the active DeltaReferenceMode.
        /// </summary>
        public string FormatDeltaForCompetitor(CompetitorEntry target)
        {
            return FormatDeltaForCompetitor(target, DeltaMode);
        }

        public string FormatDeltaForCompetitor(CompetitorEntry target, DeltaReferenceMode mode)
        {
            if (target == null || !target.HasValidNumericTime) return "";

            if (_cachedOrderedTimed == null || _cachedOrderedTimed.Count == 0)
            {
                RecalculatePlaces();
            }
            List<CompetitorEntry> ordered = _cachedOrderedTimed;

            int idx = ordered.IndexOf(target);
            if (idx < 0) return "";

            double tTarget = CompetitorEntry.ComputeRoundedTime(target.RawTimeSeconds, Precision);
            double diff = 0.0;

            switch (mode)
            {
                case DeltaReferenceMode.FromWinner:
                    if (idx == 0) return CompetitorEntry.FormatSeconds(target.RawTimeSeconds, Precision);
                    diff = tTarget - CompetitorEntry.ComputeRoundedTime(ordered[0].RawTimeSeconds, Precision);
                    return "+" + diff.ToString("0.00", CultureInfo.InvariantCulture);

                case DeltaReferenceMode.FromPrevious:
                    if (idx == 0) return CompetitorEntry.FormatSeconds(target.RawTimeSeconds, Precision);
                    diff = tTarget - CompetitorEntry.ComputeRoundedTime(ordered[idx - 1].RawTimeSeconds, Precision);
                    return "+" + diff.ToString("0.00", CultureInfo.InvariantCulture);

                case DeltaReferenceMode.FromNext:
                    if (idx >= ordered.Count - 1) return "0.00";
                    diff = CompetitorEntry.ComputeRoundedTime(ordered[idx + 1].RawTimeSeconds, Precision) - tTarget;
                    return "-" + diff.ToString("0.00", CultureInfo.InvariantCulture);

                case DeltaReferenceMode.LengthsAthletics:
                case DeltaReferenceMode.LengthsCycling:
                case DeltaReferenceMode.LengthsRowing:
                case DeltaReferenceMode.CustomLength:
                    if (idx == 0) return "WIN";
                    double rawDiff = target.RawTimeSeconds - ordered[idx - 1].RawTimeSeconds;
                    double unitSec = mode == DeltaReferenceMode.LengthsCycling ? 0.12
                                   : mode == DeltaReferenceMode.LengthsRowing ? 1.80
                                   : (LengthUnitSeconds > 0.01 ? LengthUnitSeconds : 0.16);
                    double lengths = rawDiff / unitSec;
                    if (lengths < 0.03) return "Dead Heat";
                    if (lengths < 0.12) return "Chest";
                    if (lengths < 0.30) return "Shoulder";
                    return lengths.ToString("0.0", CultureInfo.InvariantCulture) + " L";
            }
            return "";
        }

        public bool MatchesFilters(CompetitorEntry c)
        {
            if (!FiltersEnabled || ResultFilters == null || ResultFilters.Count == 0 || c == null) return true;
            for (int i = 0; i < ResultFilters.Count; i++)
            {
                ResultFilterRule rule = ResultFilters[i];
                if (rule == null || !rule.Enabled) continue;

                if (rule.PlaceFilterMode == 1 && c.Place <= 0) return false;
                string val = "";
                string f = (rule.FieldName ?? "").ToLowerInvariant();
                if (f == "place") val = c.Place > 0 ? c.Place.ToString(CultureInfo.InvariantCulture) : "";
                else if (f == "lane") val = c.Lane > 0 ? c.Lane.ToString(CultureInfo.InvariantCulture) : "";
                else if (f == "id") val = c.BibId ?? "";
                else if (f == "first name" || f == "firstname") val = c.FirstName ?? "";
                else if (f == "last name" || f == "lastname") val = c.LastName ?? "";
                else if (f == "affiliation") val = c.Affiliation ?? "";
                else if (f == "license") val = c.License ?? "";
                else if (f == "time") val = c.HasValidNumericTime ? c.RawTimeSeconds.ToString(CultureInfo.InvariantCulture) : "";
                else if (f == "laps") val = c.Splits.Count.ToString(CultureInfo.InvariantCulture);

                if (!string.IsNullOrEmpty(rule.LowerBound))
                {
                    double dv, dlo;
                    if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out dv) &&
                        double.TryParse(rule.LowerBound, NumberStyles.Any, CultureInfo.InvariantCulture, out dlo))
                    {
                        if (dv < dlo) return false;
                    }
                    else if (string.Compare(val, rule.LowerBound, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }
                }
                if (!string.IsNullOrEmpty(rule.UpperBound))
                {
                    double dv, dup;
                    if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out dv) &&
                        double.TryParse(rule.UpperBound, NumberStyles.Any, CultureInfo.InvariantCulture, out dup))
                    {
                        if (dv > dup) return false;
                    }
                    else if (string.Compare(val, rule.UpperBound, StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public void Dispose()
        {
            for (int i = 0; i < Cameras.Count; i++)
            {
                Cameras[i].Dispose();
            }
            Cameras.Clear();
        }
    }

    /// <summary>
    /// Represents a lookup entry in the athlete directory (.ppl equivalent).
    /// </summary>
    public sealed class DirectoryAthlete
    {
        public string BibId { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string Affiliation { get; set; }
        public string License { get; set; }
    }

    /// <summary>
    /// Represents a scheduled event entry in the meet schedule (.sch / .evt equivalent).
    /// </summary>
    public sealed class ScheduledEventSummary
    {
        public int EventNumber { get; set; }
        public int RoundNumber { get; set; }
        public int HeatNumber { get; set; }
        public string EventName { get; set; }
        public double DistanceMeters { get; set; }
        public List<CompetitorEntry> StartList { get; private set; }

        public ScheduledEventSummary()
        {
            EventName = "";
            DistanceMeters = 100.0;
            StartList = new List<CompetitorEntry>();
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "Ev {0} R{1} H{2} — {3} ({4} athletes)",
                EventNumber, RoundNumber, HeatNumber, EventName, StartList.Count);
        }
    }
}
