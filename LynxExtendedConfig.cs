using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using OptiFinish.Core;

namespace OptiFinish
{
    public sealed class ResultFieldConfig
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public bool Enabled { get; set; }
        public string Justify { get; set; } // "Left", "Center", "Right"

        public ResultFieldConfig(string key, string displayName, bool enabled, string justify)
        {
            Key = key;
            DisplayName = displayName;
            Enabled = enabled;
            Justify = justify;
        }

        public override string ToString()
        {
            return DisplayName + (Enabled ? "" : " (Disabled)");
        }
    }

    public sealed class ScoreboardOutputConfig
    {
        public string Name { get; set; }
        public string StatusText { get; set; }
        public string ScriptFile { get; set; }
        public string CodeSet { get; set; }
        public string SerialPort { get; set; }
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public string Parity { get; set; }
        public string StopBits { get; set; }
        public string RunningTimeMode { get; set; } // "Off", "Normal", "Raw"
        public double PauseTime { get; set; }
        public double OffsetSeconds { get; set; }
        public bool AutoBreakPause { get; set; }
        public bool AutoBreakPauseIfCapturing { get; set; }
        public bool AutoBreakFinish { get; set; }
        public bool AutoBreakFinishIfCapturing { get; set; }
        public string ResultsMode { get; set; } // "Off", "Auto", "Manual"
        public bool PagingEnabled { get; set; }
        public int PagingSize { get; set; }
        public string PagingMax { get; set; }
        public double PagingTime { get; set; }
        public string TimePrecision { get; set; }
        public bool SuppressMinute { get; set; }
        public bool AutoExportImage { get; set; }

        public ScoreboardOutputConfig()
        {
            Name = "Scoreboard";
            StatusText = "Not Running";
            ScriptFile = "ResulTV.lss";
            CodeSet = "Single Byte";
            SerialPort = "<none>";
            BaudRate = 9600;
            DataBits = 8;
            Parity = "None";
            StopBits = "1.0";
            RunningTimeMode = "Normal";
            PauseTime = 3.0;
            OffsetSeconds = 0.0;
            ResultsMode = "Auto";
            PagingEnabled = true;
            PagingSize = 8;
            PagingMax = "";
            PagingTime = 5.0;
            TimePrecision = "<on screen>";
        }
    }

    public sealed class LapTimeDeviceConfig
    {
        public string Name { get; set; }
        public string StatusText { get; set; }
        public string DeviceType { get; set; } // "From Image", "Chip System", "LynxPad", "Serial", "File"
        public string SerialPort { get; set; }
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public string Parity { get; set; }
        public string StopBits { get; set; }
        public bool ActiveSplitsAll { get; set; }
        public string SelectedSplits { get; set; }
        public string SyncSource { get; set; } // "External", "Internal", "None"
        public double TimeOffsetSeconds { get; set; }

        public LapTimeDeviceConfig()
        {
            Name = "LapTime";
            StatusText = "Running";
            DeviceType = "From Image";
            SerialPort = "<none>";
            BaudRate = 9600;
            DataBits = 8;
            Parity = "None";
            StopBits = "1.0";
            ActiveSplitsAll = true;
            SelectedSplits = "";
            SyncSource = "None";
            TimeOffsetSeconds = 0.0;
        }
    }

    public sealed class PrintExportSettings
    {
        public string PageHeaderLeft { get; set; }
        public string PageHeaderCenter { get; set; }
        public string PageHeaderRight { get; set; }
        public string PageFooterLeft { get; set; }
        public string PageFooterCenter { get; set; }
        public string PageFooterRight { get; set; }
        public double MarginTop { get; set; }
        public double MarginBottom { get; set; }
        public double MarginLeft { get; set; }
        public double MarginRight { get; set; }
        public bool Landscape { get; set; }
        public bool PrintOverlays { get; set; }
        public bool PrintLaneNumbers { get; set; }
        public bool PrintTimeRuler { get; set; }
        public bool PrintResultsTable { get; set; }
        public int MaxImagesPerPage { get; set; }

        public PrintExportSettings()
        {
            PageHeaderLeft = "%E  (%N-%R-%H)";
            PageHeaderCenter = "%C";
            PageHeaderRight = "Wind: %W";
            PageFooterLeft = "OptiFinish";
            PageFooterCenter = "Page %P";
            PageFooterRight = "%D %T";
            MarginTop = 0.5;
            MarginBottom = 0.5;
            MarginLeft = 0.5;
            MarginRight = 0.5;
            Landscape = true;
            PrintOverlays = true;
            PrintLaneNumbers = true;
            PrintTimeRuler = true;
            PrintResultsTable = true;
            MaxImagesPerPage = 1;
        }
    }

    /// <summary>
    /// Full FinishLynx configuration state covering all 8 Options tabs, toolbar customization,
    /// 21 Results fields, Scoreboard/Wind/LapTime profiles, Print setup, and Other Settings registry tree.
    /// Synchronizes shared properties with LynxConfigService.Instance cleanly.
    /// </summary>
    public sealed class LynxExtendedConfig
    {
        private static readonly LynxExtendedConfig _instance = new LynxExtendedConfig();
        public static LynxExtendedConfig Instance { get { return _instance; } }

        // Event options extras
        public bool SingleEventMode { get; set; }
        public bool TimeTrialMode { get; set; }

        // Image tab (OPT_Image.png)
        public bool[] ImageToolbarVisible { get; private set; } // 17 checkboxes matching OPT_Img_Tool Bar.png
        public LaneBarPosition LaneBarSide { get; set; }
        public int TimeTrackingMode { get; set; } // 0=Off, 1=Same Camera, 2=All Cameras
        public string HashColorName { get; set; }
        public int HashWidth { get; set; }
        public int HashCrossWidth { get; set; }
        public string ShowLinesColorName { get; set; }
        public string ShowSplitsMode { get; set; }
        public string PrintLinesColorName { get; set; }
        public bool PrintSelectedByDefault { get; set; }
        public string TimeGapLinesMode { get; set; }
        public int SelectionFixedWidth { get; set; }
        public int SelectionFixedHeight { get; set; }
        public int ImageExportJpegQuality { get; set; }
        public int ImageExportScale { get; set; }
        public int VideoExportJpegQuality { get; set; }
        public int VideoExportScale { get; set; }
        public int VideoExportSpeed { get; set; }
        public bool SmoothZoom { get; set; }
        public bool AutoCropOnStop { get; set; }

        // Results tab (OPT_Results.png)
        public bool[] ResultsToolbarVisible { get; private set; } // 13 checkboxes matching OPT_RZ_ToolBar.png
        public List<ResultFieldConfig> ResultFields { get; private set; }
        public bool LogBreaks { get; set; }
        public bool PrintBreaks { get; set; }
        public double LengthTimeSeconds { get; set; }
        public bool SuppressMinute { get; set; }
        public double PaceDistanceMeters { get; set; }
        public bool RoundMethodOverrideEnabled { get; set; }
        public string RoundMethodMode { get; set; }

        // Database extras (OPT_Database.png)
        public string DatabaseTimePrecision { get; set; }
        public bool DatabaseSuppressMinute { get; set; }
        public string DatabaseSerialPort { get; set; }
        public int DatabaseBaud { get; set; }
        public int DatabaseDataBits { get; set; }
        public string DatabaseParity { get; set; }
        public string DatabaseStopBits { get; set; }
        public int DatabaseSerialVersion { get; set; }
        public bool LifIncludeDate { get; set; }
        public bool LifIncludeText { get; set; }
        public string LifCustomText { get; set; }
        public bool LifIncludeFilters { get; set; }
        public string LifCodeSet { get; set; }
        public string ReacTimePort { get; set; }

        // Scoreboard tab (OPT_Scoreboard.png)
        public List<ScoreboardOutputConfig> Scoreboards { get; private set; }
        public int SelectedScoreboardIndex { get; set; }

        // Wind tab (OPT_Wind.png)
        public string WindModule { get; set; }
        public string WindSerialPort { get; set; }
        public int WindBaud { get; set; }
        public int WindDataBits { get; set; }
        public string WindParity { get; set; }
        public string WindStopBits { get; set; }
        public WindWindowMode WindDefaultMode { get; set; }
        public double WindDefaultStart { get; set; }
        public double WindDefaultDuration { get; set; }
        public bool WindReminder { get; set; }
        public bool WindWarnIfOffline { get; set; }

        // LapTime tab (OPT_LapTime_FromImage.png)
        public List<LapTimeDeviceConfig> LapTimeDevices { get; private set; }
        public int SelectedLapTimeIndex { get; set; }
        public double LapTimeMaskStartSeconds { get; set; }
        public double LapTimeMaskDuplicateSeconds { get; set; }
        public string LapTimeResultKey { get; set; }
        public bool LapTimeCreateResults { get; set; }
        public bool LapTimeFillInPlace { get; set; }
        public string LapTimeSortBySplit { get; set; } // "Cumulative", "Last", "Best"
        public string LapTimeFinalSplit { get; set; }  // "LapTime", "Official Time"
        public string LapTimeMapFile { get; set; }

        // Print + Overlays
        public PrintExportSettings PrintSettings { get; private set; }
        public Dictionary<string, string> OtherSettings { get; private set; }

        private LynxExtendedConfig()
        {
            SingleEventMode = true;
            TimeTrialMode = false;

            // Default toolbar visibility matching real FinishLynx (194200 + OPT_Img_Tool Bar.png defaults)
            ImageToolbarVisible = new bool[]
            {
                true,  // 0: Identify By (14)
                false, // 1: Orientation
                true,  // 2: Zoom Out / In (- / +)
                true,  // 3: Zoom % Dropdown
                true,  // 4: Rate / Aspect Ratio Box
                true,  // 5: Scroll New Image
                true,  // 6: Uncrop
                true,  // 7: Autocrop
                true,  // 8: Find Next Object
                false, // 9: Crop Selection
                true,  // 10: Enhance / Unenhance
                true,  // 11: Quick Adjust (Luminosity)
                true,  // 12: Print
                false, // 13: Print to Bitmap
                true,  // 14: Maximize Pane / Toggle Grid
                false, // 15: Iris controls (Open / Close / Auto)
                true   // 16: Video frame controls (when video)
            };

            LaneBarSide = LaneBarPosition.Left;
            TimeTrackingMode = 2; // All Cameras
            HashColorName = "Red";
            HashWidth = 1;
            HashCrossWidth = 13;
            ShowLinesColorName = "Black";
            ShowSplitsMode = "Off";
            PrintLinesColorName = "Black";
            PrintSelectedByDefault = false;
            TimeGapLinesMode = "Off";
            SelectionFixedWidth = 320;
            SelectionFixedHeight = 240;
            ImageExportJpegQuality = 85;
            ImageExportScale = 100;
            VideoExportJpegQuality = 75;
            VideoExportScale = 100;
            VideoExportSpeed = 100;

            ResultsToolbarVisible = new bool[]
            {
                true, true, true, true, true, true, true,
                true, true, true, true, true, true
            };

            ResultFields = new List<ResultFieldConfig>
            {
                new ResultFieldConfig("Place", "Place", true, "Right"),
                new ResultFieldConfig("Id", "Id", true, "Left"),
                new ResultFieldConfig("Lane", "Lane", true, "Right"),
                new ResultFieldConfig("FirstName", "First Name", true, "Left"),
                new ResultFieldConfig("LastName", "Last Name", true, "Left"),
                new ResultFieldConfig("Affiliation", "Affiliation", true, "Left"),
                new ResultFieldConfig("License", "License", false, "Left"),
                new ResultFieldConfig("Time", "Time", true, "Right"),
                new ResultFieldConfig("Delta", "Delta Time", true, "Right"),
                new ResultFieldConfig("ReacTime", "ReacTime", true, "Right"),
                new ResultFieldConfig("CumSplit", "Cum. Split Time", true, "Right"),
                new ResultFieldConfig("LastSplit", "Last Split Time", false, "Right"),
                new ResultFieldConfig("BestSplit", "Best Split Time", false, "Right"),
                new ResultFieldConfig("Laps", "Laps", true, "Right"),
                new ResultFieldConfig("Speed", "Speed", true, "Right"),
                new ResultFieldConfig("Pace", "Pace", false, "Right"),
                new ResultFieldConfig("User1", "User 1", false, "Left"),
                new ResultFieldConfig("User2", "User 2", false, "Left"),
                new ResultFieldConfig("User3", "User 3", false, "Left"),
                new ResultFieldConfig("Delta2", "Delta Time 2", false, "Right"),
                new ResultFieldConfig("Delta3", "Delta Time 3", false, "Right")
            };

            LengthTimeSeconds = 0.200;
            PaceDistanceMeters = 1000.0;
            RoundMethodMode = "Up (1.3 -> 2.0)";

            DatabaseTimePrecision = "<on screen>";
            DatabaseSerialPort = "<none>";
            DatabaseBaud = 9600;
            DatabaseDataBits = 8;
            DatabaseParity = "None";
            DatabaseStopBits = "1.0";
            DatabaseSerialVersion = 1;
            LifCodeSet = "Single Byte";
            ReacTimePort = "Files";

            Scoreboards = new List<ScoreboardOutputConfig> { new ScoreboardOutputConfig() };
            WindModule = "Off";
            WindSerialPort = "<none>";
            WindBaud = 9600;
            WindDataBits = 7;
            WindParity = "None";
            WindStopBits = "1.0";
            WindDefaultMode = WindWindowMode.Auto10s_100m;
            WindDefaultStart = 0.0;
            WindDefaultDuration = 10.0;
            WindWarnIfOffline = true;

            LapTimeDevices = new List<LapTimeDeviceConfig> { new LapTimeDeviceConfig() };
            LapTimeMaskStartSeconds = 3.0;
            LapTimeMaskDuplicateSeconds = 3.0;
            LapTimeResultKey = "Lane";
            LapTimeCreateResults = true;
            LapTimeSortBySplit = "Cumulative";
            LapTimeFinalSplit = "Official Time";
            LapTimeMapFile = "";

            PrintSettings = new PrintExportSettings();
            OtherSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            PopulateDefaultOtherSettings();
            Load();
        }

        private void PopulateDefaultOtherSettings()
        {
            OtherSettings["Camera\\DefaultRate"] = "1000";
            OtherSettings["Camera\\BufferSizeMB"] = "256";
            OtherSettings["Camera\\ZeroGunOffsetMs"] = "0.0";
            OtherSettings["Event\\Layout"] = "0";
            OtherSettings["Event\\SingleEventMode"] = "1";
            OtherSettings["Event\\TimeTrialMode"] = "0";
            OtherSettings["Image\\SmoothZoom"] = "0";
            OtherSettings["Image\\AutoCropLeader"] = "25";
            OtherSettings["Image\\AutoCropTrailer"] = "25";
            OtherSettings["Image\\ResultPopupMax"] = "12";
            OtherSettings["Image\\EvalLineColor"] = "#FFCC00";
            OtherSettings["Results\\DeltaLengthSec"] = "0.200";
            OtherSettings["Results\\PackLeaderSec"] = "1.000";
            OtherSettings["Results\\Precision"] = "Hundredths";
            OtherSettings["Scoreboard\\UdpBroadcastPort"] = "41605";
            OtherSettings["Scoreboard\\UdpHost"] = "127.0.0.1";
            OtherSettings["Wind\\AutoClearOnNewEvent"] = "1";
            OtherSettings["LapTime\\AutoAcceptSplits"] = "1";
        }

        public static Color ColorFromName(string name, Color fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            switch (name.Trim().ToLowerInvariant())
            {
                case "red": return Color.Red;
                case "black": return Color.Black;
                case "white": return Color.White;
                case "yellow": return Color.Yellow;
                case "green": return Color.Lime;
                case "blue": return Color.DeepSkyBlue;
                case "cyan": return Color.Cyan;
                case "magenta": return Color.Magenta;
                case "orange": return Color.Orange;
                default: return fallback;
            }
        }

        public string GetConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OptiFinish.ext.cfg");
        }

        public void Load()
        {
            string p = GetConfigPath();
            if (!File.Exists(p)) return;
            try
            {
                string[] lines = File.ReadAllLines(p, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i].Trim();
                    if (string.IsNullOrEmpty(ln) || ln.StartsWith("#")) continue;
                    int eq = ln.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = ln.Substring(0, eq).Trim();
                    string v = ln.Substring(eq + 1).Trim();
                    ApplyKv(k, v);
                }
            }
            catch { }
        }

        private void ApplyKv(string k, string v)
        {
            int iv;
            double dv;
            if (k == "SingleEventMode") SingleEventMode = v == "1";
            else if (k == "TimeTrialMode") TimeTrialMode = v == "1";
            else if (k == "LaneBarSide" && int.TryParse(v, out iv)) LaneBarSide = (LaneBarPosition)Math.Max(0, Math.Min(2, iv));
            else if (k == "TimeTrackingMode" && int.TryParse(v, out iv)) TimeTrackingMode = Math.Max(0, Math.Min(2, iv));
            else if (k == "HashColorName") HashColorName = v;
            else if (k == "HashWidth" && int.TryParse(v, out iv)) HashWidth = Math.Max(1, Math.Min(5, iv));
            else if (k == "HashCrossWidth" && int.TryParse(v, out iv)) HashCrossWidth = Math.Max(5, Math.Min(80, iv));
            else if (k == "ShowLinesColorName") ShowLinesColorName = v;
            else if (k == "ShowSplitsMode") ShowSplitsMode = v;
            else if (k == "PrintLinesColorName") PrintLinesColorName = v;
            else if (k == "PrintSelectedByDefault") PrintSelectedByDefault = v == "1";
            else if (k == "TimeGapLinesMode") TimeGapLinesMode = v;
            else if (k == "SmoothZoom") SmoothZoom = v == "1";
            else if (k == "LengthTimeSeconds" && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) LengthTimeSeconds = dv;
            else if (k == "SuppressMinute") SuppressMinute = v == "1";
            else if (k == "PaceDistanceMeters" && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) PaceDistanceMeters = dv;
            else if (k == "WindModule") WindModule = v;
            else if (k == "WindSerialPort") WindSerialPort = v;
            else if (k == "WindBaud" && int.TryParse(v, out iv)) WindBaud = iv;
            else if (k == "WindDefaultDuration" && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) WindDefaultDuration = dv;
            else if (k == "WindReminder") WindReminder = v == "1";
            else if (k == "WindWarnIfOffline") WindWarnIfOffline = v == "1";
            else if (k == "LapTimeMaskStart" && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) LapTimeMaskStartSeconds = dv;
            else if (k == "LapTimeMaskDup" && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) LapTimeMaskDuplicateSeconds = dv;
            else if (k == "LapTimeResultKey") LapTimeResultKey = v;
            else if (k == "LapTimeCreateResults") LapTimeCreateResults = v == "1";
            else if (k == "LapTimeFillInPlace") LapTimeFillInPlace = v == "1";
            else if (k == "ImageToolbarMask")
            {
                string[] parts = v.Split(',');
                for (int i = 0; i < Math.Min(parts.Length, ImageToolbarVisible.Length); i++) ImageToolbarVisible[i] = parts[i] == "1";
            }
            else if (k == "ResultsToolbarMask")
            {
                string[] parts = v.Split(',');
                for (int i = 0; i < Math.Min(parts.Length, ResultsToolbarVisible.Length); i++) ResultsToolbarVisible[i] = parts[i] == "1";
            }
            else if (k.StartsWith("Field."))
            {
                string fk = k.Substring(6);
                string[] parts = v.Split('|');
                for (int i = 0; i < ResultFields.Count; i++)
                {
                    if (string.Equals(ResultFields[i].Key, fk, StringComparison.OrdinalIgnoreCase))
                    {
                        if (parts.Length > 0) ResultFields[i].Enabled = parts[0] == "1";
                        if (parts.Length > 1) ResultFields[i].Justify = parts[1];
                        break;
                    }
                }
            }
            else if (k.StartsWith("Other."))
            {
                OtherSettings[k.Substring(6)] = v;
            }
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# OptiFinish Extended FinishLynx Configuration");
                sb.AppendLine("SingleEventMode=" + (SingleEventMode ? "1" : "0"));
                sb.AppendLine("TimeTrialMode=" + (TimeTrialMode ? "1" : "0"));
                sb.AppendLine("LaneBarSide=" + ((int)LaneBarSide).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("TimeTrackingMode=" + TimeTrackingMode.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("HashColorName=" + HashColorName);
                sb.AppendLine("HashWidth=" + HashWidth.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("HashCrossWidth=" + HashCrossWidth.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("ShowLinesColorName=" + ShowLinesColorName);
                sb.AppendLine("ShowSplitsMode=" + ShowSplitsMode);
                sb.AppendLine("PrintLinesColorName=" + PrintLinesColorName);
                sb.AppendLine("PrintSelectedByDefault=" + (PrintSelectedByDefault ? "1" : "0"));
                sb.AppendLine("TimeGapLinesMode=" + TimeGapLinesMode);
                sb.AppendLine("SmoothZoom=" + (SmoothZoom ? "1" : "0"));
                sb.AppendLine("LengthTimeSeconds=" + LengthTimeSeconds.ToString("0.000", CultureInfo.InvariantCulture));
                sb.AppendLine("SuppressMinute=" + (SuppressMinute ? "1" : "0"));
                sb.AppendLine("PaceDistanceMeters=" + PaceDistanceMeters.ToString("0.0", CultureInfo.InvariantCulture));
                sb.AppendLine("WindModule=" + WindModule);
                sb.AppendLine("WindSerialPort=" + WindSerialPort);
                sb.AppendLine("WindBaud=" + WindBaud.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("WindDefaultDuration=" + WindDefaultDuration.ToString("0.0", CultureInfo.InvariantCulture));
                sb.AppendLine("WindReminder=" + (WindReminder ? "1" : "0"));
                sb.AppendLine("WindWarnIfOffline=" + (WindWarnIfOffline ? "1" : "0"));
                sb.AppendLine("LapTimeMaskStart=" + LapTimeMaskStartSeconds.ToString("0.0", CultureInfo.InvariantCulture));
                sb.AppendLine("LapTimeMaskDup=" + LapTimeMaskDuplicateSeconds.ToString("0.0", CultureInfo.InvariantCulture));
                sb.AppendLine("LapTimeResultKey=" + LapTimeResultKey);
                sb.AppendLine("LapTimeCreateResults=" + (LapTimeCreateResults ? "1" : "0"));
                sb.AppendLine("LapTimeFillInPlace=" + (LapTimeFillInPlace ? "1" : "0"));

                List<string> imgFlags = new List<string>();
                for (int i = 0; i < ImageToolbarVisible.Length; i++) imgFlags.Add(ImageToolbarVisible[i] ? "1" : "0");
                sb.AppendLine("ImageToolbarMask=" + string.Join(",", imgFlags.ToArray()));

                List<string> resFlags = new List<string>();
                for (int i = 0; i < ResultsToolbarVisible.Length; i++) resFlags.Add(ResultsToolbarVisible[i] ? "1" : "0");
                sb.AppendLine("ResultsToolbarMask=" + string.Join(",", resFlags.ToArray()));

                for (int i = 0; i < ResultFields.Count; i++)
                {
                    ResultFieldConfig rf = ResultFields[i];
                    sb.AppendLine("Field." + rf.Key + "=" + (rf.Enabled ? "1" : "0") + "|" + rf.Justify);
                }
                foreach (KeyValuePair<string, string> kv in OtherSettings)
                {
                    sb.AppendLine("Other." + kv.Key + "=" + kv.Value);
                }

                File.WriteAllText(GetConfigPath(), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
