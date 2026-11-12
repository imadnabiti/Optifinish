using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace OptiFinish.Core
{
    /// <summary>
    /// Configuration manager for OptiFinish and FinishLynx:
    /// Manages database input (.evt, .ppl), export (.lif), event (.evn, .i01), and backup paths,
    /// window layouts, camera adjustments, and outputs synchronization.
    /// Interoperates directly with C:\Lynx\Lynx.cfg and local OptiFinish.cfg.
    /// </summary>
    public sealed class LynxConfigService
    {
        private static LynxConfigService _instance;
        public static LynxConfigService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LynxConfigService();
                    _instance.Load();
                }
                return _instance;
            }
        }

        // Database Paths & Options
        public string DataInDir { get; set; }
        public string DataOutDir { get; set; }
        public int DataSourceMode { get; set; } // 0 = None, 1 = Files
        public int CodeSet { get; set; }        // 0 = Single Byte, 1 = Unicode, 2 = Native
        public bool WriteLif { get; set; }
        public bool AutoSaveLif { get; set; }

        // Event & Backup Paths
        public string EventDir { get; set; }
        public string BackupDir { get; set; }
        public bool AutoSave { get; set; }
        public int AutoSaveIntervalSeconds { get; set; }
        public int LayoutSelection { get; set; } // 0..5
        public int DefaultEventNameMode { get; set; } // 0 = Event Title, 1 = Numbers
        public int EventTemplateMode { get; set; }    // 0 = None, 1 = Always Choose, 2 = Use Default
        public string DefaultTemplate { get; set; }

        // General
        public string CompetitionName { get; set; }
        public string CompetitionSponsor { get; set; }
        public string CaptureButton { get; set; }
        public int HardwareType { get; set; } // 0 = None, 1 = Ethernet, 2 = Detect
        public string CameraNetwork { get; set; }
        public string FontName { get; set; }
        public float FontSize { get; set; }
        public bool FontBold { get; set; }
        public string Language { get; set; }
        public string Profile { get; set; }

        // Image & Display Adjustments
        public int Brightness { get; set; }
        public int Contrast { get; set; }
        public double Gamma { get; set; }
        public bool IdentiLynxTimeTrack { get; set; }
        public int IdentiLynxFrameSkip { get; set; }
        public int IdentiLynxPlayMaxFast { get; set; }
        public int IdentiLynxPlayMaxSlow { get; set; }
        public bool ShowLaneGrid { get; set; }
        public bool ShowEvaluatedLines { get; set; }

        // Outputs / Scoreboard
        public string OutputScript { get; set; }
        public string OutputSerialPort { get; set; }
        public int OutputBaudRate { get; set; }
        public int OutputDataBits { get; set; }
        public string OutputParity { get; set; }
        public string OutputStopBits { get; set; }
        public bool OutputAutoExportImage { get; set; }

        // Results
        public TimePrecision ResultsPrecision { get; set; }
        public DeltaReferenceMode DeltaMode { get; set; }
        public bool AutoPackLeaders { get; set; }

        public LynxConfigService()
        {
            SetDefaults();
        }

        public void SetDefaults()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string lynxDir = @"C:\Lynx";
            bool hasLynx = Directory.Exists(lynxDir);

            DataInDir = hasLynx ? lynxDir : Path.Combine(baseDir, "SampleData");
            DataOutDir = hasLynx ? lynxDir : Path.Combine(baseDir, "SampleData");
            DataSourceMode = 1; // Files
            CodeSet = 0;        // Single Byte
            WriteLif = true;
            AutoSaveLif = true;

            EventDir = hasLynx ? lynxDir : Path.Combine(baseDir, "SampleData");
            BackupDir = hasLynx ? Path.Combine(lynxDir, "temp") : Path.Combine(baseDir, "Backup");
            AutoSave = true;
            AutoSaveIntervalSeconds = 60;
            LayoutSelection = 0; // Layout 0: Standard (Summary top-left, Results top-right, Cameras bottom)
            DefaultEventNameMode = 0;
            EventTemplateMode = 0;
            DefaultTemplate = "";

            CompetitionName = "Athletics Championship";
            CompetitionSponsor = "OptiFinish Official Timing";
            CaptureButton = "Keyboard";
            HardwareType = 2; // Detect
            CameraNetwork = "<not specified>";
            FontName = "Segoe UI";
            FontSize = 10f;
            FontBold = false;
            Language = "English (US)";
            Profile = "<standard>";

            Brightness = 0;
            Contrast = 0;
            Gamma = 1.00;
            IdentiLynxTimeTrack = true;
            IdentiLynxFrameSkip = 1500;
            IdentiLynxPlayMaxFast = 16;
            IdentiLynxPlayMaxSlow = 2;
            ShowLaneGrid = true;
            ShowEvaluatedLines = true;

            OutputScript = "1line.lss";
            OutputSerialPort = "<none>";
            OutputBaudRate = 9600;
            OutputDataBits = 8;
            OutputParity = "None";
            OutputStopBits = "1.0";
            OutputAutoExportImage = false;

            ResultsPrecision = TimePrecision.Hundredths;
            DeltaMode = DeltaReferenceMode.FromWinner;
            AutoPackLeaders = true;
        }

        public string GetLocalConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OptiFinish.cfg");
        }

        public string GetLynxCfgPath()
        {
            return @"C:\Lynx\Lynx.cfg";
        }

        public void Load()
        {
            // 1. Try reading C:\Lynx\Lynx.cfg first if available
            string lynxCfg = GetLynxCfgPath();
            if (File.Exists(lynxCfg))
            {
                try
                {
                    ParseLynxCfgFile(lynxCfg);
                }
                catch
                {
                    // Fall back to defaults
                }
            }

            // 2. Override with local OptiFinish.cfg if present
            string localCfg = GetLocalConfigPath();
            if (File.Exists(localCfg))
            {
                try
                {
                    ParseLocalIniFile(localCfg);
                }
                catch
                {
                    // Ignore local syntax errors
                }
            }

            // Sanity check directories
            EnsureDirectories();
        }

        private void EnsureDirectories()
        {
            try
            {
                if (!string.IsNullOrEmpty(BackupDir) && !Directory.Exists(BackupDir))
                {
                    Directory.CreateDirectory(BackupDir);
                }
            }
            catch { }
        }

        private void ParseLynxCfgFile(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath, Encoding.Default);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";")) continue;

                int eqIdx = line.IndexOf('=');
                if (eqIdx < 0) continue;

                string key = line.Substring(0, eqIdx).Trim();
                string val = line.Substring(eqIdx + 1).Trim();

                if (key.StartsWith(@"\Database\File\DataInDir", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(val)) DataInDir = val;
                }
                else if (key.StartsWith(@"\Database\File\DataOutDir", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(val)) DataOutDir = val;
                }
                else if (key.StartsWith(@"\Database\File\WriteLIF", StringComparison.OrdinalIgnoreCase))
                {
                    WriteLif = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else if (key.StartsWith(@"\Database\LIF\AutoSave", StringComparison.OrdinalIgnoreCase))
                {
                    AutoSaveLif = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else if (key.StartsWith(@"\Database\Mode", StringComparison.OrdinalIgnoreCase))
                {
                    int m;
                    if (int.TryParse(val, out m)) DataSourceMode = m;
                }
                else if (key.StartsWith(@"\Database\CharMode", StringComparison.OrdinalIgnoreCase))
                {
                    int cm;
                    if (int.TryParse(val, out cm)) CodeSet = cm;
                }
                else if (key.StartsWith(@"\Event\Directory", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(val)) EventDir = val;
                }
                else if (key.StartsWith(@"\Event\BackupDir", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(val)) BackupDir = val;
                }
                else if (key.StartsWith(@"\Event\AutoSave", StringComparison.OrdinalIgnoreCase))
                {
                    AutoSave = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else if (key.StartsWith(@"\Event\Layout\Selection", StringComparison.OrdinalIgnoreCase))
                {
                    int l;
                    if (int.TryParse(val, out l)) LayoutSelection = Math.Max(0, Math.Min(5, l));
                }
                else if (key.StartsWith(@"\Competition\Name", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(val)) CompetitionName = val;
                }
                else if (key.StartsWith(@"\Competition\Sponsor", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(val)) CompetitionSponsor = val;
                }
                else if (key.StartsWith(@"\Event\Image\QuickAdjust\Brightness", StringComparison.OrdinalIgnoreCase))
                {
                    int b;
                    if (int.TryParse(val, out b)) Brightness = b;
                }
                else if (key.StartsWith(@"\Event\Image\QuickAdjust\Contrast", StringComparison.OrdinalIgnoreCase))
                {
                    int c;
                    if (int.TryParse(val, out c)) Contrast = c;
                }
                else if (key.StartsWith(@"\Event\Image\QuickAdjust\Gamma", StringComparison.OrdinalIgnoreCase))
                {
                    int g;
                    if (int.TryParse(val, out g))
                    {
                        if (g == 100 || g == 0) Gamma = 1.0;
                        else if (g > 0) Gamma = g / 100.0;
                        else Gamma = Math.Max(0.2, 1.0 + (g / 100.0));
                    }
                }
                else if (key.StartsWith(@"\Event\Image\IdentiLynx\TimeTrack", StringComparison.OrdinalIgnoreCase))
                {
                    IdentiLynxTimeTrack = val != "0";
                }
                else if (key.StartsWith(@"\Scoreboards\1\Script", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(val)) OutputScript = val;
                }
                else if (key.StartsWith(@"\Scoreboards\1\SerialPort", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(val)) OutputSerialPort = val;
                }
            }
        }

        private void ParseLocalIniFile(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;

                int eqIdx = line.IndexOf('=');
                if (eqIdx < 0) continue;

                string k = line.Substring(0, eqIdx).Trim();
                string v = line.Substring(eqIdx + 1).Trim();

                switch (k)
                {
                    case "DataInDir": DataInDir = v; break;
                    case "DataOutDir": DataOutDir = v; break;
                    case "EventDir": EventDir = v; break;
                    case "BackupDir": BackupDir = v; break;
                    case "AutoSave": AutoSave = (v == "1" || v.ToLowerInvariant() == "true"); break;
                    case "AutoSaveIntervalSeconds": int asi; if (int.TryParse(v, out asi) && asi > 0) AutoSaveIntervalSeconds = asi; break;
                    case "LayoutSelection": int l; if (int.TryParse(v, out l)) LayoutSelection = Math.Max(0, Math.Min(5, l)); break;
                    case "CompetitionName": CompetitionName = v; break;
                    case "CompetitionSponsor": CompetitionSponsor = v; break;
                    case "CaptureButton": CaptureButton = v; break;
                    case "HardwareType": int ht; if (int.TryParse(v, out ht)) HardwareType = ht; break;
                    case "CameraNetwork": CameraNetwork = v; break;
                    case "FontName": FontName = v; break;
                    case "FontSize": float fs; if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out fs) && fs > 5) FontSize = fs; break;
                    case "FontBold": FontBold = (v == "1" || v.ToLowerInvariant() == "true"); break;
                    case "Language": Language = v; break;
                    case "Brightness": int b; if (int.TryParse(v, out b)) Brightness = b; break;
                    case "Contrast": int c; if (int.TryParse(v, out c)) Contrast = c; break;
                    case "Gamma": double g; if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out g) && g > 0.1) Gamma = g; break;
                    case "IdentiLynxTimeTrack": IdentiLynxTimeTrack = (v == "1" || v.ToLowerInvariant() == "true"); break;
                    case "ShowLaneGrid": ShowLaneGrid = (v == "1" || v.ToLowerInvariant() == "true"); break;
                    case "ShowEvaluatedLines": ShowEvaluatedLines = (v == "1" || v.ToLowerInvariant() == "true"); break;
                    case "OutputScript": OutputScript = v; break;
                    case "OutputSerialPort": OutputSerialPort = v; break;
                    case "OutputBaudRate": int br; if (int.TryParse(v, out br) && br > 0) OutputBaudRate = br; break;
                    case "WriteLif": WriteLif = (v == "1" || v.ToLowerInvariant() == "true"); break;
                    case "AutoSaveLif": AutoSaveLif = (v == "1" || v.ToLowerInvariant() == "true"); break;
                }
            }
        }

        public void Save()
        {
            EnsureDirectories();

            // 1. Save local OptiFinish.cfg
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# OptiFinish Configuration File");
            sb.AppendLine("# Auto-generated on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine("[Paths]");
            sb.AppendLine("DataInDir=" + DataInDir);
            sb.AppendLine("DataOutDir=" + DataOutDir);
            sb.AppendLine("EventDir=" + EventDir);
            sb.AppendLine("BackupDir=" + BackupDir);
            sb.AppendLine();
            sb.AppendLine("[Event]");
            sb.AppendLine("AutoSave=" + (AutoSave ? "1" : "0"));
            sb.AppendLine("AutoSaveIntervalSeconds=" + AutoSaveIntervalSeconds);
            sb.AppendLine("LayoutSelection=" + LayoutSelection);
            sb.AppendLine("DefaultEventNameMode=" + DefaultEventNameMode);
            sb.AppendLine("EventTemplateMode=" + EventTemplateMode);
            sb.AppendLine("DefaultTemplate=" + DefaultTemplate);
            sb.AppendLine();
            sb.AppendLine("[General]");
            sb.AppendLine("CompetitionName=" + CompetitionName);
            sb.AppendLine("CompetitionSponsor=" + CompetitionSponsor);
            sb.AppendLine("CaptureButton=" + CaptureButton);
            sb.AppendLine("HardwareType=" + HardwareType);
            sb.AppendLine("CameraNetwork=" + CameraNetwork);
            sb.AppendLine("FontName=" + FontName);
            sb.AppendLine("FontSize=" + FontSize.ToString("0.0", CultureInfo.InvariantCulture));
            sb.AppendLine("FontBold=" + (FontBold ? "1" : "0"));
            sb.AppendLine("Language=" + Language);
            sb.AppendLine();
            sb.AppendLine("[Image]");
            sb.AppendLine("Brightness=" + Brightness);
            sb.AppendLine("Contrast=" + Contrast);
            sb.AppendLine("Gamma=" + Gamma.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("IdentiLynxTimeTrack=" + (IdentiLynxTimeTrack ? "1" : "0"));
            sb.AppendLine("ShowLaneGrid=" + (ShowLaneGrid ? "1" : "0"));
            sb.AppendLine("ShowEvaluatedLines=" + (ShowEvaluatedLines ? "1" : "0"));
            sb.AppendLine();
            sb.AppendLine("[Outputs]");
            sb.AppendLine("OutputScript=" + OutputScript);
            sb.AppendLine("OutputSerialPort=" + OutputSerialPort);
            sb.AppendLine("OutputBaudRate=" + OutputBaudRate);
            sb.AppendLine("OutputDataBits=" + OutputDataBits);
            sb.AppendLine("OutputParity=" + OutputParity);
            sb.AppendLine("OutputStopBits=" + OutputStopBits);
            sb.AppendLine("OutputAutoExportImage=" + (OutputAutoExportImage ? "1" : "0"));
            sb.AppendLine();
            sb.AppendLine("[Database]");
            sb.AppendLine("DataSourceMode=" + DataSourceMode);
            sb.AppendLine("CodeSet=" + CodeSet);
            sb.AppendLine("WriteLif=" + (WriteLif ? "1" : "0"));
            sb.AppendLine("AutoSaveLif=" + (AutoSaveLif ? "1" : "0"));

            try
            {
                File.WriteAllText(GetLocalConfigPath(), sb.ToString(), Encoding.UTF8);
            }
            catch { }

            // 2. If C:\Lynx\Lynx.cfg exists and is writable, update key path entries
            string lynxCfg = GetLynxCfgPath();
            if (File.Exists(lynxCfg))
            {
                try
                {
                    UpdateLynxCfgFile(lynxCfg);
                }
                catch { }
            }
        }

        private void UpdateLynxCfgFile(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath, Encoding.Default);
            List<string> updated = new List<string>(lines.Length);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith(@"\Database\File\DataInDir:String,1=", StringComparison.OrdinalIgnoreCase))
                {
                    updated.Add(@"\Database\File\DataInDir:String,1=" + DataInDir);
                }
                else if (line.StartsWith(@"\Database\File\DataOutDir:String,1=", StringComparison.OrdinalIgnoreCase))
                {
                    updated.Add(@"\Database\File\DataOutDir:String,1=" + DataOutDir);
                }
                else if (line.StartsWith(@"\Event\Directory:String,1=", StringComparison.OrdinalIgnoreCase))
                {
                    updated.Add(@"\Event\Directory:String,1=" + EventDir);
                }
                else if (line.StartsWith(@"\Event\BackupDir:String,1=", StringComparison.OrdinalIgnoreCase))
                {
                    updated.Add(@"\Event\BackupDir:String,1=" + BackupDir);
                }
                else if (line.StartsWith(@"\Event\AutoSave:Int,1,,=", StringComparison.OrdinalIgnoreCase))
                {
                    updated.Add(@"\Event\AutoSave:Int,1,,=" + (AutoSave ? "1" : "0"));
                }
                else if (line.StartsWith(@"\Event\Layout\Selection:Int,1,,=", StringComparison.OrdinalIgnoreCase))
                {
                    updated.Add(@"\Event\Layout\Selection:Int,1,,=" + LayoutSelection);
                }
                else if (line.StartsWith(@"\Competition\Name:String,1=", StringComparison.OrdinalIgnoreCase))
                {
                    updated.Add(@"\Competition\Name:String,1=" + CompetitionName);
                }
                else if (line.StartsWith(@"\Competition\Sponsor:String,1=", StringComparison.OrdinalIgnoreCase))
                {
                    updated.Add(@"\Competition\Sponsor:String,1=" + CompetitionSponsor);
                }
                else
                {
                    updated.Add(line);
                }
            }

            File.WriteAllLines(filePath, updated.ToArray(), Encoding.Default);
        }

        /// <summary>
        /// Performs an automatic background save of the active race event document:
        /// 1. Saves .lif file to DataOutDir (official results export)
        /// 2. Saves backup copies of .evn to BackupDir
        /// </summary>
        public bool PerformAutosave(RaceEventDocument doc, out string report)
        {
            report = "";
            if (doc == null) return false;

            StringBuilder sb = new StringBuilder();
            try
            {
                EnsureDirectories();

                string baseName = !string.IsNullOrEmpty(doc.EventName)
                    ? doc.EventName
                    : string.Format(CultureInfo.InvariantCulture, "{0:D3}-{1}-{2:D2}", doc.EventNumber, doc.RoundNumber, doc.HeatNumber);

                // Strip invalid filename chars
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    baseName = baseName.Replace(c, '_');
                }

                // 1. Export official .lif to DataOutDir if enabled
                if (WriteLif || AutoSaveLif)
                {
                    string targetDir = !string.IsNullOrEmpty(DataOutDir) && Directory.Exists(DataOutDir)
                        ? DataOutDir
                        : Path.GetDirectoryName(doc.SourceFilePath);

                    if (string.IsNullOrEmpty(targetDir)) targetDir = AppDomain.CurrentDomain.BaseDirectory;

                    string lifPath = Path.Combine(targetDir, baseName + ".lif");
                    MeetDatabaseService.ExportResultsLifOrCsv(doc, lifPath);
                    sb.Append("Exported .lif -> " + Path.GetFileName(lifPath) + "; ");
                }

                // 2. Backup event file to BackupDir
                if (!string.IsNullOrEmpty(BackupDir) && Directory.Exists(BackupDir))
                {
                    string backupEvn = Path.Combine(BackupDir, baseName + "_backup.evn");
                    if (File.Exists(doc.SourceFilePath))
                    {
                        File.Copy(doc.SourceFilePath, backupEvn, true);
                        sb.Append("Backed up .evn -> " + Path.GetFileName(backupEvn));
                    }
                }

                report = sb.ToString();
                return true;
            }
            catch (Exception ex)
            {
                report = "Autosave failed: " + ex.Message;
                return false;
            }
        }
    }
}
