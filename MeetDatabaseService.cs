using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace OptiFinish.Core
{
    /// <summary>
    /// Clean-room meet database manager: handles built-in race presets,
    /// standard delimited meet files (.evt, .ppl, .sch, .lif, .csv, .spl),
    /// native FinishLynx binary event packages (.evn + .i01/.i02), and native .optr race packages.
    /// </summary>
    public static class MeetDatabaseService
    {
        public static List<DirectoryAthlete> LoadAthleteDirectory(string pplFilePath)
        {
            List<DirectoryAthlete> list = new List<DirectoryAthlete>();
            if (string.IsNullOrEmpty(pplFilePath) || !File.Exists(pplFilePath)) return list;

            string[] lines = File.ReadAllLines(pplFilePath, Encoding.Default);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";")) continue;

                string[] parts = SplitCsvLine(line);
                if (parts.Length < 3) continue;

                DirectoryAthlete a = new DirectoryAthlete
                {
                    BibId = parts[0].Trim(),
                    LastName = parts.Length > 1 ? parts[1].Trim() : "",
                    FirstName = parts.Length > 2 ? parts[2].Trim() : "",
                    Affiliation = parts.Length > 3 ? parts[3].Trim() : "",
                    License = parts.Length > 4 ? parts[4].Trim() : ""
                };
                list.Add(a);
            }
            return list;
        }

        public static List<ScheduledEventSummary> LoadMeetEventsFile(string evtFilePath)
        {
            List<ScheduledEventSummary> events = new List<ScheduledEventSummary>();
            if (string.IsNullOrEmpty(evtFilePath) || !File.Exists(evtFilePath)) return events;

            string[] lines = File.ReadAllLines(evtFilePath, Encoding.Default);
            ScheduledEventSummary current = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                if (string.IsNullOrEmpty(raw)) continue;
                string trimmed = raw.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";")) continue;

                bool isParticipantRow = raw.StartsWith(",") || raw.StartsWith(" ") || raw.StartsWith("\t");
                string[] parts = SplitCsvLine(trimmed.TrimStart(',', ' ', '\t'));

                if (!isParticipantRow && parts.Length >= 4)
                {
                    int evNum, rndNum, htNum;
                    if (int.TryParse(parts[0].Trim(), out evNum) &&
                        int.TryParse(parts[1].Trim(), out rndNum) &&
                        int.TryParse(parts[2].Trim(), out htNum))
                    {
                        current = new ScheduledEventSummary
                        {
                            EventNumber = evNum,
                            RoundNumber = rndNum,
                            HeatNumber = htNum,
                            EventName = parts[3].Trim(),
                            DistanceMeters = InferDistanceFromEventName(parts[3].Trim())
                        };
                        if (parts.Length > 9)
                        {
                            double dist;
                            if (double.TryParse(parts[9].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out dist) && dist > 0)
                            {
                                current.DistanceMeters = dist;
                            }
                        }
                        events.Add(current);
                        continue;
                    }
                }

                if (current != null && parts.Length >= 3)
                {
                    int lane = 0;
                    if (parts.Length > 1) int.TryParse(parts[1].Trim(), out lane);

                    CompetitorEntry c = new CompetitorEntry
                    {
                        BibId = parts[0].Trim(),
                        Lane = lane,
                        LastName = parts.Length > 2 ? parts[2].Trim() : "",
                        FirstName = parts.Length > 3 ? parts[3].Trim() : "",
                        Affiliation = parts.Length > 4 ? parts[4].Trim() : "",
                        License = parts.Length > 6 ? parts[6].Trim() : ""
                    };
                    current.StartList.Add(c);
                }
            }

            return events;
        }

        public static RaceEventDocument CreateEventFromScheduleSummary(ScheduledEventSummary summary)
        {
            RaceEventDocument doc = new RaceEventDocument
            {
                EventNumber = summary.EventNumber,
                RoundNumber = summary.RoundNumber,
                HeatNumber = summary.HeatNumber,
                EventName = summary.EventName,
                DistanceMeters = summary.DistanceMeters,
                CompetitionName = "Scheduled Meet Event",
                GunTimeOfDay = DateTime.Now,
                HasStartSignal = true,
                WindReading = 0.6,
                HasWindReading = summary.DistanceMeters <= 200.0
            };

            Random rng = new Random(summary.EventNumber * 100 + summary.RoundNumber * 10 + summary.HeatNumber);
            double baseSec = summary.DistanceMeters <= 110.0 ? 11.20
                           : summary.DistanceMeters <= 200.0 ? 21.40
                           : summary.DistanceMeters <= 400.0 ? 48.20
                           : summary.DistanceMeters <= 800.0 ? 112.0
                           : (summary.DistanceMeters / 5.8);

            for (int i = 0; i < summary.StartList.Count; i++)
            {
                CompetitorEntry src = summary.StartList[i];
                double simTime = baseSec + (rng.NextDouble() * (baseSec * 0.045));
                double simReac = summary.DistanceMeters <= 400.0 ? (0.128 + rng.NextDouble() * 0.055) : double.NaN;

                CompetitorEntry copy = new CompetitorEntry
                {
                    BibId = src.BibId,
                    Lane = src.Lane > 0 ? src.Lane : (i + 1),
                    LastName = src.LastName,
                    FirstName = src.FirstName,
                    Affiliation = src.Affiliation,
                    License = src.License,
                    RawTimeSeconds = Math.Round(simTime, 4),
                    ReactionTimeSeconds = simReac,
                    Status = CompetitorStatus.Valid,
                    HasImageEvaluation = true,
                    EvaluatedTimeSeconds = Math.Round(simTime, 4)
                };
                doc.Competitors.Add(copy);
            }

            doc.LaneCount = Math.Max(8, doc.Competitors.Count);
            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                doc.Competitors[i].EvaluatedVerticalRatio = doc.LaneToCenterRatio(doc.Competitors[i].Lane);
            }

            doc.RecalculatePlaces();
            SlitScanEngine.BuildSynthesizedCameraStrips(doc);
            return doc;
        }

        /// <summary>
        /// Loads a standard comma-delimited results file (.lif or .csv) into a RaceEventDocument.
        /// </summary>
        public static RaceEventDocument LoadResultsFile(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath, Encoding.Default);
            RaceEventDocument doc = new RaceEventDocument
            {
                SourceFilePath = Path.GetFullPath(filePath)
            };
            doc.Competitors.Clear();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";")) continue;

                string[] parts = SplitCsvLine(line);
                if (i == 0 && parts.Length >= 4)
                {
                    int ev, rnd, ht;
                    if (int.TryParse(parts[0].Trim(), out ev)) doc.EventNumber = ev;
                    if (int.TryParse(parts[1].Trim(), out rnd)) doc.RoundNumber = rnd;
                    if (int.TryParse(parts[2].Trim(), out ht)) doc.HeatNumber = ht;
                    doc.EventName = parts[3].Trim();
                    if (parts.Length > 4 && !string.IsNullOrEmpty(parts[4].Trim()))
                    {
                        double w;
                        string wNorm = parts[4].Trim().Replace(',', '.');
                        if (double.TryParse(wNorm, NumberStyles.Any, CultureInfo.InvariantCulture, out w))
                        {
                            doc.WindReading = w;
                            doc.HasWindReading = true;
                        }
                    }
                    if (parts.Length > 9 && !string.IsNullOrEmpty(parts[9].Trim()))
                    {
                        double d;
                        if (double.TryParse(parts[9].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out d) && d > 0)
                        {
                            doc.DistanceMeters = d;
                        }
                    }
                    else
                    {
                        doc.DistanceMeters = InferDistanceFromEventName(doc.EventName);
                    }
                    continue;
                }

                if (parts.Length >= 6)
                {
                    CompetitorEntry c = new CompetitorEntry();
                    string placeToken = parts[0].Trim().ToUpperInvariant();
                    if (placeToken == "DNS") c.Status = CompetitorStatus.DNS;
                    else if (placeToken == "FS") c.Status = CompetitorStatus.FS;
                    else if (placeToken == "DNF") c.Status = CompetitorStatus.DNF;
                    else if (placeToken == "DQ") c.Status = CompetitorStatus.DQ;
                    else if (placeToken == "SCR") c.Status = CompetitorStatus.SCR;
                    else
                    {
                        int pl;
                        if (int.TryParse(placeToken, out pl)) c.Place = pl;
                    }

                    c.BibId = parts[1].Trim();
                    int ln;
                    if (int.TryParse(parts[2].Trim(), out ln)) c.Lane = ln;
                    c.LastName = parts[3].Trim();
                    c.FirstName = parts[4].Trim();
                    c.Affiliation = parts[5].Trim();

                    if (parts.Length > 6 && !string.IsNullOrEmpty(parts[6].Trim()))
                    {
                        double tSec = ParseFormattedTime(parts[6].Trim());
                        if (!double.IsNaN(tSec))
                        {
                            c.RawTimeSeconds = tSec;
                            c.HasImageEvaluation = true;
                            c.EvaluatedTimeSeconds = tSec;
                        }
                        else
                        {
                            c.CustomTimeText = parts[6].Trim();
                        }
                    }

                    if (parts.Length > 9 && !string.IsNullOrEmpty(parts[9].Trim()))
                    {
                        double rt;
                        string rtNorm = parts[9].Trim().Replace(',', '.');
                        if (double.TryParse(rtNorm, NumberStyles.Any, CultureInfo.InvariantCulture, out rt))
                        {
                            c.ReactionTimeSeconds = rt;
                        }
                    }

                    doc.Competitors.Add(c);
                }
            }

            int maxLane = 8;
            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                if (doc.Competitors[i].Lane > maxLane) maxLane = doc.Competitors[i].Lane;
            }
            doc.LaneCount = Math.Min(16, maxLane);

            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                int effLane = doc.Competitors[i].Lane > 0 ? doc.Competitors[i].Lane : ((i % doc.LaneCount) + 1);
                doc.Competitors[i].EvaluatedVerticalRatio = doc.LaneToCenterRatio(effLane);
            }

            doc.RecalculatePlaces();
            SlitScanEngine.BuildSynthesizedCameraStrips(doc);
            return doc;
        }

        private sealed class EvnStringToken
        {
            public int Offset;
            public int EndOffset;
            public string Value;
        }

        private sealed class EvnTimeToken
        {
            public int Offset;
            public ulong MasterTicks;
            public uint Divisor;
        }

        private sealed class EvnCameraDescriptor
        {
            public string CameraName;
            public string StreamExtension;
            public int HeaderOffset;
            public int SensorMode;
            public int SensorHeight;
            public int SampleRateHz;
            public float[] CalibratedLaneBoundaries;
            public string SerialNumber;
            public bool ReverseLaneOrder;
            public string PositionLabel;
        }

        private static bool IsStartToken(string val)
        {
            if (string.IsNullOrEmpty(val)) return false;
            return string.Equals(val, "Start", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "Partenza", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "Départ", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "Salida", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "Startzeit", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsResultsToken(string val)
        {
            if (string.IsNullOrEmpty(val)) return false;
            return string.Equals(val, "Results", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "Risultati", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "Résultats", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "Resultados", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "Ergebnisse", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Discovers native FinishLynx .evn race event files in the given directory and C:\Lynx.
        /// </summary>
        public static List<string> DiscoverNativeLynxEventFiles(string primaryDirectory)
        {
            List<string> results = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string baseAppDir = AppDomain.CurrentDomain.BaseDirectory;
            string sampleDataDir = Path.Combine(baseAppDir, "SampleData");
            string cfgEventDir = LynxConfigService.Instance.EventDir;

            string[] candidateDirs = new string[]
            {
                sampleDataDir,
                cfgEventDir,
                primaryDirectory,
                Path.Combine(primaryDirectory, "SampleData"),
                @"C:\Lynx",
                @"C:\Lynx\Samples\Athletics",
                @"C:\Lynx\Samples\IdentiLynx",
                @"C:\Lynx\Samples"
            };

            for (int d = 0; d < candidateDirs.Length; d++)
            {
                string dir = candidateDirs[d];
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                try
                {
                    string[] files = Directory.GetFiles(dir, "*.evn", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string full = Path.GetFullPath(files[i]);
                        if (!seen.Contains(full))
                        {
                            seen.Add(full);
                            results.Add(full);
                        }
                    }
                }
                catch { }
            }
            return results;
        }

        /// <summary>
        /// Loads a FinishLynx .spl lap/split marker file into a RaceEventDocument.
        /// </summary>
        public static void LoadSplitFile(string splFilePath, RaceEventDocument doc)
        {
            if (doc == null || string.IsNullOrEmpty(splFilePath) || !File.Exists(splFilePath)) return;
            doc.SplitMarkerSeconds.Clear();
            string[] lines = File.ReadAllLines(splFilePath, Encoding.Default);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";")) continue;
                string[] parts = line.Split(',');
                if (parts.Length >= 1)
                {
                    double sec;
                    string norm = parts[0].Trim().Replace(',', '.');
                    if (double.TryParse(norm, NumberStyles.Any, CultureInfo.InvariantCulture, out sec) && sec > 0)
                    {
                        doc.SplitMarkerSeconds.Add(sec);
                    }
                }
            }
            doc.SplitMarkerSeconds.Sort();
        }

        /// <summary>
        /// Loads a native FinishLynx binary .evn race event file along with its companion .i01/.i02 camera streams
        /// and .spl/.lif files. Implements the exact 80,000 Hz master tick clock and camera perspective tables.
        /// </summary>
        public static RaceEventDocument LoadNativeLynxEventFile(string evnFilePath)
        {
            if (string.IsNullOrEmpty(evnFilePath))
            {
                throw new ArgumentNullException("evnFilePath");
            }

            if (!File.Exists(evnFilePath))
            {
                string dir = Path.GetDirectoryName(evnFilePath);
                string fn = Path.GetFileName(evnFilePath);
                if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
                if (Directory.Exists(dir))
                {
                    string fnNorm = fn.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                    string[] candidates = Directory.GetFiles(dir, "*.evn");
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        string candNorm = Path.GetFileName(candidates[i]).Replace(" ", "").Replace("_", "").ToLowerInvariant();
                        if (candNorm == fnNorm)
                        {
                            evnFilePath = candidates[i];
                            break;
                        }
                    }
                }
            }

            if (!File.Exists(evnFilePath))
            {
                throw new FileNotFoundException("Native .evn file not found", evnFilePath);
            }

            byte[] data = File.ReadAllBytes(evnFilePath);
            RaceEventDocument doc = new RaceEventDocument
            {
                SourceFilePath = Path.GetFullPath(evnFilePath),
                Precision = TimePrecision.Hundredths,
                HasStartSignal = true
            };
            doc.Competitors.Clear();
            doc.Cameras.Clear();

            // 1. Parse all UTF-16LE tagged strings (05 00 01 10 <uint32 charCount>)
            List<EvnStringToken> strings = new List<EvnStringToken>();
            for (int i = 0; i + 8 <= data.Length; i++)
            {
                if (data[i] == 0x05 && data[i + 1] == 0x00 && data[i + 2] == 0x01 && data[i + 3] == 0x10)
                {
                    int charCount = BitConverter.ToInt32(data, i + 4);
                    if (charCount >= 0 && charCount <= 256 && i + 8 + charCount * 2 <= data.Length)
                    {
                        string val = charCount > 0 ? Encoding.Unicode.GetString(data, i + 8, charCount * 2) : string.Empty;
                        strings.Add(new EvnStringToken
                        {
                            Offset = i,
                            EndOffset = i + 8 + charCount * 2,
                            Value = val
                        });
                    }
                }
            }

            // 2. Parse all TTime records (01 00 02 00 <uint48 masterTicks> <uint32 divisor>)
            List<EvnTimeToken> timeTokens = new List<EvnTimeToken>();
            for (int i = 0; i + 14 <= data.Length; i++)
            {
                if (data[i] == 0x01 && data[i + 1] == 0x00 && data[i + 2] == 0x02 && data[i + 3] == 0x00)
                {
                    ulong lo32 = BitConverter.ToUInt32(data, i + 4);
                    ulong hi16 = BitConverter.ToUInt16(data, i + 8);
                    ulong masterTicks = lo32 | (hi16 << 32);
                    uint divisor = BitConverter.ToUInt32(data, i + 10);
                    if (divisor > 0 && divisor <= 80000)
                    {
                        timeTokens.Add(new EvnTimeToken
                        {
                            Offset = i,
                            MasterTicks = masterTicks,
                            Divisor = divisor
                        });
                    }
                }
            }

            // 3. Parse Event #, Round #, Heat #, CompetitionName, and EventName
            int evParsed, rndParsed, htParsed;
            if (strings.Count >= 5 &&
                int.TryParse(strings[0].Value.Trim(), out evParsed) &&
                int.TryParse(strings[1].Value.Trim(), out rndParsed) &&
                int.TryParse(strings[2].Value.Trim(), out htParsed) &&
                !string.IsNullOrEmpty(strings[4].Value))
            {
                doc.EventNumber = evParsed;
                doc.RoundNumber = rndParsed;
                doc.HeatNumber = htParsed;
                doc.CompetitionName = strings[3].Value;
                doc.EventName = strings[4].Value;
            }
            else
            {
                if (strings.Count > 0 && strings[0].Offset >= 12)
                {
                    int evNum = BitConverter.ToInt32(data, strings[0].Offset - 12);
                    int rndNum = BitConverter.ToInt32(data, strings[0].Offset - 8);
                    int htNum = BitConverter.ToInt32(data, strings[0].Offset - 4);
                    if (evNum > 0 && evNum < 100000) doc.EventNumber = evNum;
                    if (rndNum > 0 && rndNum < 1000) doc.RoundNumber = rndNum;
                    if (htNum > 0 && htNum < 10000) doc.HeatNumber = htNum;
                }
                if (strings.Count > 0) doc.CompetitionName = strings[0].Value;
                if (strings.Count > 1 && !string.IsNullOrEmpty(strings[1].Value))
                {
                    doc.EventName = strings[1].Value;
                }
                else
                {
                    doc.EventName = Path.GetFileNameWithoutExtension(evnFilePath);
                }
            }
            if (string.IsNullOrEmpty(doc.CompetitionName))
            {
                doc.CompetitionName = "Athletics Championship";
            }
            doc.DistanceMeters = InferDistanceFromEventName(doc.EventName);
            doc.HasWindReading = false;

            // 4. Locate 'Start' / 'Partenza' string and Gun Start TTime
            int startStringIdx = -1;
            int firstResultsStringIdx = -1;
            int lastResultsStringIdx = -1;
            string defaultPositionLabel = "";
            for (int i = 0; i < strings.Count; i++)
            {
                if (startStringIdx < 0 && IsStartToken(strings[i].Value))
                {
                    startStringIdx = i;
                }
                if (IsResultsToken(strings[i].Value))
                {
                    if (firstResultsStringIdx < 0) firstResultsStringIdx = i;
                    lastResultsStringIdx = i;
                }
            }
            if (firstResultsStringIdx > 0 && !string.IsNullOrEmpty(strings[firstResultsStringIdx - 1].Value))
            {
                defaultPositionLabel = strings[firstResultsStringIdx - 1].Value.Trim();
            }

            ulong gunStartMasterTicks = 0;
            if (startStringIdx >= 0)
            {
                int startOff = strings[startStringIdx].EndOffset;
                for (int t = 0; t < timeTokens.Count; t++)
                {
                    if (timeTokens[t].Offset >= startOff)
                    {
                        gunStartMasterTicks = timeTokens[t].MasterTicks;
                        double todSec = gunStartMasterTicks / 80000.0;
                        if (todSec >= 0 && todSec < 86400.0 * 2)
                        {
                            DateTime baseDate = File.GetLastWriteTime(evnFilePath).Date;
                            doc.GunTimeOfDay = baseDate.AddSeconds(todSec % 86400.0);
                        }
                        break;
                    }
                }
            }

            // 5. Locate all camera stream descriptors ("i01", "i02", "i03", "i04")
            List<EvnCameraDescriptor> camDescs = new List<EvnCameraDescriptor>();
            int firstCameraHeaderOffset = data.Length;
            int prevCamStreamEnd = lastResultsStringIdx >= 0 ? strings[lastResultsStringIdx].EndOffset : 0;

            for (int i = 0; i < strings.Count; i++)
            {
                string sv = strings[i].Value;
                if (sv.Length == 3 && (sv == "i01" || sv == "i02" || sv == "i03" || sv == "i04") && i >= 1)
                {
                    EvnCameraDescriptor cd = new EvnCameraDescriptor
                    {
                        CameraName = "Camera " + sv.ToUpperInvariant(),
                        StreamExtension = sv,
                        HeaderOffset = strings[i].Offset,
                        SensorMode = 0,
                        SensorHeight = 500,
                        SampleRateHz = 1000
                    };

                    int laneTableOffset = -1;
                    // Scan for 13 00 02 00 perspective lane boundary table in this camera's header region
                    for (int pos = prevCamStreamEnd; pos + 20 <= strings[i].Offset; pos++)
                    {
                        if (data[pos] == 0x13 && data[pos + 1] == 0x00 && data[pos + 2] == 0x02 && data[pos + 3] == 0x00)
                        {
                            laneTableOffset = pos;
                            int numLanes = BitConverter.ToInt32(data, pos + 12);
                            if (numLanes > 0 && numLanes <= 20 && pos + 16 + (numLanes + 1) * 4 <= strings[i].Offset)
                            {
                                float[] bounds = new float[numLanes + 1];
                                bool validBounds = true;
                                for (int b = 0; b <= numLanes; b++)
                                {
                                    int rawB = BitConverter.ToInt32(data, pos + 16 + b * 4);
                                    if (rawB < 0 || rawB > 10000 || (b > 0 && rawB < (int)(bounds[b - 1] * 10000)))
                                    {
                                        validBounds = false;
                                        break;
                                    }
                                    bounds[b] = rawB / 10000.0f;
                                }
                                if (validBounds && bounds[numLanes] > bounds[0])
                                {
                                    cd.CalibratedLaneBoundaries = bounds;
                                }
                            }
                            if (numLanes >= 0 && numLanes <= 20)
                            {
                                int endTbl = pos + 16 + (numLanes + 1) * 4;
                                if (endTbl + 13 <= strings[i].Offset && data[endTbl + 8] == 0x01)
                                {
                                    int sNum = BitConverter.ToInt32(data, endTbl + 9);
                                    if (sNum > 0)
                                    {
                                        cd.SerialNumber = sNum.ToString(CultureInfo.InvariantCulture);
                                    }
                                }
                            }
                            break;
                        }
                    }

                    // Scan for 5-byte tagged integers: 01 <sensorMode> 01 <offset> 01 <sensorHeight> 01 <sampleRateHz>
                    int modeTagOffset = -1;
                    int scanStart = laneTableOffset >= 0 ? laneTableOffset : prevCamStreamEnd;
                    for (int pos = scanStart; pos + 20 <= strings[i].Offset; pos++)
                    {
                        if (data[pos] == 0x01 && data[pos + 5] == 0x01 && data[pos + 10] == 0x01 && data[pos + 15] == 0x01)
                        {
                            int sMode = BitConverter.ToInt32(data, pos + 1);
                            int sOff = BitConverter.ToInt32(data, pos + 6);
                            int sHeight = BitConverter.ToInt32(data, pos + 11);
                            int sRate = BitConverter.ToInt32(data, pos + 16);
                            if (sMode >= 0 && sMode <= 4 && sOff >= 0 && sOff <= 4096 &&
                                sHeight >= 200 && sHeight <= 2048 && sRate >= 200 && sRate <= 20000)
                            {
                                cd.SensorMode = sMode;
                                cd.SensorHeight = sHeight;
                                cd.SampleRateHz = sRate;
                                modeTagOffset = pos;
                                break;
                            }
                        }
                    }

                    // Locate the camera name string immediately preceding laneTableOffset or modeTagOffset
                    int anchorOffset = laneTableOffset >= 0 ? laneTableOffset : (modeTagOffset >= 0 ? modeTagOffset : strings[i].Offset);
                    for (int s = i - 1; s >= 0; s--)
                    {
                        if (strings[s].Offset < prevCamStreamEnd) break;
                        if (strings[s].EndOffset <= anchorOffset && !string.IsNullOrEmpty(strings[s].Value))
                        {
                            cd.CameraName = strings[s].Value;
                            cd.HeaderOffset = strings[s].Offset;
                            break;
                        }
                    }

                    // Scan for 11 00 02 00 camera display/orientation block (laneOrderFlag at +36)
                    for (int pos = cd.HeaderOffset; pos + 40 <= strings[i].Offset; pos++)
                    {
                        if (data[pos] == 0x11 && data[pos + 1] == 0x00 && data[pos + 2] == 0x02 && data[pos + 3] == 0x00)
                        {
                            int laneOrderFlag = BitConverter.ToInt32(data, pos + 36);
                            cd.ReverseLaneOrder = (laneOrderFlag == 0);
                            break;
                        }
                    }

                    if (cd.HeaderOffset < firstCameraHeaderOffset)
                    {
                        firstCameraHeaderOffset = cd.HeaderOffset;
                    }

                    prevCamStreamEnd = strings[i].EndOffset;
                    camDescs.Add(cd);
                }
            }

            // 6. Parse competitor records between the last "Results" string and firstCameraHeaderOffset
            if (lastResultsStringIdx >= 0)
            {
                int idx = lastResultsStringIdx + 1;
                while (idx + 4 < strings.Count && strings[idx].Offset < firstCameraHeaderOffset)
                {
                    EvnStringToken sLast = strings[idx];
                    EvnStringToken sFirst = strings[idx + 1];
                    EvnStringToken sAffil = strings[idx + 2];
                    EvnStringToken sLane = strings[idx + 3];
                    EvnStringToken sBib = strings[idx + 4];

                    // Verify consecutive strings
                    if (sFirst.Offset != sLast.EndOffset ||
                        sAffil.Offset != sFirst.EndOffset ||
                        sLane.Offset != sAffil.EndOffset ||
                        sBib.Offset != sLane.EndOffset)
                    {
                        idx++;
                        continue;
                    }

                    int nextIdx = idx + 5;
                    while (nextIdx < strings.Count &&
                           strings[nextIdx].Offset == strings[nextIdx - 1].EndOffset &&
                           string.IsNullOrEmpty(strings[nextIdx].Value))
                    {
                        nextIdx++;
                    }

                    int tailStart = strings[nextIdx - 1].EndOffset;
                    int tailEnd = (nextIdx < strings.Count)
                        ? Math.Min(strings[nextIdx].Offset, firstCameraHeaderOffset)
                        : firstCameraHeaderOffset;

                    int laneNum = 0;
                    int.TryParse(sLane.Value.Trim(), out laneNum);

                    int placeCode = 0;
                    if (tailStart + 4 <= data.Length)
                    {
                        placeCode = BitConverter.ToInt32(data, tailStart);
                    }

                    // Collect TTime records inside competitor tail
                    List<EvnTimeToken> compTimes = new List<EvnTimeToken>();
                    for (int t = 0; t < timeTokens.Count; t++)
                    {
                        if (timeTokens[t].Offset >= tailStart && timeTokens[t].Offset < tailEnd)
                        {
                            compTimes.Add(timeTokens[t]);
                        }
                    }

                    CompetitorEntry comp = new CompetitorEntry
                    {
                        LastName = sLast.Value.Trim(),
                        FirstName = sFirst.Value.Trim(),
                        Affiliation = sAffil.Value.Trim(),
                        Lane = laneNum,
                        BibId = sBib.Value.Trim(),
                        Status = CompetitorStatus.Valid
                    };

                    if (compTimes.Count > 0 && compTimes[0].MasterTicks > 0 && compTimes[0].Divisor != 0xFFFFFFFFu)
                    {
                        double rawSec = ((long)compTimes[0].MasterTicks - (long)gunStartMasterTicks) / 80000.0;
                        comp.RawTimeSeconds = rawSec;
                        comp.EvaluatedTimeSeconds = rawSec;
                        comp.HasImageEvaluation = true;
                        comp.Place = placeCode > 0 && placeCode < 1000 ? placeCode : 0;

                        uint div = compTimes[0].Divisor;
                        if (div == 1 || div == 8) comp.PrecisionOverride = TimePrecision.TenThousandths;
                        else if (div == 80) comp.PrecisionOverride = TimePrecision.Thousandths;
                        else if (div == 800) comp.PrecisionOverride = TimePrecision.Hundredths;
                        else if (div == 8000) comp.PrecisionOverride = TimePrecision.Tenths;
                        else if (div == 80000) comp.PrecisionOverride = TimePrecision.Seconds;

                        if (comp.PrecisionOverride == TimePrecision.TenThousandths || Math.Abs(rawSec) < 0.1)
                        {
                            doc.Precision = TimePrecision.TenThousandths;
                        }
                    }
                    else
                    {
                        if (placeCode == -4) comp.Status = CompetitorStatus.DNF;
                        else if (placeCode == -2) comp.Status = CompetitorStatus.DNS;
                        else if (placeCode == -3) comp.Status = CompetitorStatus.DQ;
                        else if (compTimes.Count == 0) comp.Status = CompetitorStatus.DNF;
                    }

                    if (compTimes.Count > 1 && compTimes[1].Divisor == 80 && compTimes[1].MasterTicks > 0 && compTimes[1].MasterTicks < 80000)
                    {
                        comp.ReactionTimeSeconds = compTimes[1].MasterTicks / 80000.0;
                    }

                    doc.Competitors.Add(comp);
                    idx = nextIdx;
                }
            }

            // 7. Load companion .i01 / .i02 image streams
            string dirPath = Path.GetDirectoryName(evnFilePath);
            string baseName = Path.GetFileNameWithoutExtension(evnFilePath);

            for (int c = 0; c < camDescs.Count; c++)
            {
                EvnCameraDescriptor cd = camDescs[c];
                string streamPath = Path.Combine(dirPath, baseName + "." + cd.StreamExtension);
                if (!File.Exists(streamPath))
                {
                    string noSpacePath = Path.Combine(dirPath, baseName.Replace(" ", "") + "." + cd.StreamExtension);
                    if (File.Exists(noSpacePath))
                    {
                        streamPath = noSpacePath;
                    }
                    else if (Directory.Exists(dirPath))
                    {
                        string normBase = baseName.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                        string[] allExt = Directory.GetFiles(dirPath, "*." + cd.StreamExtension);
                        for (int k = 0; k < allExt.Length; k++)
                        {
                            string fn = Path.GetFileNameWithoutExtension(allExt[k]).Replace(" ", "").Replace("_", "").ToLowerInvariant();
                            if (fn == normBase)
                            {
                                streamPath = allExt[k];
                                break;
                            }
                        }
                    }
                }
                if (!File.Exists(streamPath)) continue;

                string posLabel = !string.IsNullOrEmpty(cd.PositionLabel) ? cd.PositionLabel
                    : (!string.IsNullOrEmpty(defaultPositionLabel) && c == 0 ? defaultPositionLabel
                    : (c == 0 ? "Inside" : (c == 1 ? "Outside" : ("Cam " + (c + 1)))));
                SlitCameraStream stream = SlitScanEngine.LoadNativeLynxImageStream(
                    streamPath,
                    cd.CameraName,
                    posLabel,
                    cd.SensorHeight,
                    cd.SensorMode,
                    cd.SampleRateHz,
                    (long)gunStartMasterTicks,
                    cd.CalibratedLaneBoundaries);

                if (stream != null)
                {
                    stream.CameraId = cd.StreamExtension;
                    stream.ReverseLaneOrder = cd.ReverseLaneOrder;
                    if (!string.IsNullOrEmpty(cd.SerialNumber) && stream.Profile != null)
                    {
                        stream.Profile.SerialNumber = cd.SerialNumber;
                    }
                    doc.Cameras.Add(stream);
                }
            }

            // Configure document lane count & perspective lane boundaries from primary camera
            int maxCompetitorLane = 8;
            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                if (doc.Competitors[i].Lane > maxCompetitorLane)
                {
                    maxCompetitorLane = doc.Competitors[i].Lane;
                }
            }

            if (doc.Cameras.Count > 0 && doc.Cameras[0].CalibratedLaneBoundaries != null && doc.Cameras[0].CalibratedLaneBoundaries.Length >= 2)
            {
                doc.CalibratedLaneBoundaries = doc.Cameras[0].CalibratedLaneBoundaries;
                doc.LaneCount = doc.CalibratedLaneBoundaries.Length - 1;
                doc.LaneTopRatio = doc.CalibratedLaneBoundaries[0];
                doc.LaneBottomRatio = doc.CalibratedLaneBoundaries[doc.CalibratedLaneBoundaries.Length - 1];
            }
            else
            {
                doc.LaneCount = Math.Max(8, maxCompetitorLane);
            }

            // Position each competitor's vertical evaluation marker on their calibrated lane (and refine on torso contrast)
            SlitCameraStream primaryCam = doc.Cameras.Count > 0 ? doc.Cameras[0] : null;
            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                CompetitorEntry comp = doc.Competitors[i];
                int effLane = comp.Lane > 0 ? comp.Lane : ((i % Math.Max(1, doc.LaneCount)) + 1);
                float laneCenter = doc.LaneToCenterRatio(effLane);
                if (primaryCam != null && comp.HasValidNumericTime && comp.Lane > 0)
                {
                    float bandHalf = 0.35f / Math.Max(4, doc.LaneCount);
                    comp.EvaluatedVerticalRatio = SlitScanEngine.DetectBestVerticalTorsoRatio(
                        primaryCam,
                        comp.RawTimeSeconds,
                        Math.Max(0.03f, laneCenter - bandHalf),
                        Math.Min(0.97f, laneCenter + bandHalf),
                        laneCenter);
                }
                else
                {
                    comp.EvaluatedVerticalRatio = laneCenter;
                }
            }

            // 8. Load companion .spl split markers if present
            string splPath = Path.Combine(dirPath, baseName + ".spl");
            if (File.Exists(splPath))
            {
                LoadSplitFile(splPath, doc);
            }

            // 9. Check for matching .lif file in the same directory to populate wind reading if present
            string lifCodePath = Path.Combine(dirPath, string.Format(CultureInfo.InvariantCulture, "{0:000}-{1}-{2:00}.lif", doc.EventNumber, doc.RoundNumber, doc.HeatNumber));
            if (File.Exists(lifCodePath))
            {
                try
                {
                    string[] lifLines = File.ReadAllLines(lifCodePath, Encoding.Default);
                    if (lifLines.Length > 0)
                    {
                        string[] hParts = SplitCsvLine(lifLines[0]);
                        if (hParts.Length > 4 && !string.IsNullOrEmpty(hParts[4].Trim()))
                        {
                            double wVal;
                            if (double.TryParse(hParts[4].Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out wVal))
                            {
                                doc.WindReading = wVal;
                                doc.HasWindReading = true;
                            }
                        }
                    }
                }
                catch { }
            }

            // Fallback to synthesized strip only if no .i01/.i02 existed
            if (doc.Cameras.Count == 0)
            {
                SlitScanEngine.BuildSynthesizedCameraStrips(doc);
            }

            doc.RecalculatePlaces();
            return doc;
        }

        /// <summary>
        /// Saves official results to a standard comma-delimited .lif or .csv file.
        /// </summary>
        public static void ExportResultsLifOrCsv(RaceEventDocument doc, string outputPath)
        {
            doc.RecalculatePlaces();
            StringBuilder sb = new StringBuilder();

            string windStr = doc.HasWindReading ? doc.WindReading.ToString("0.0", CultureInfo.InvariantCulture) : "";
            string windUnitStr = doc.WindUnit == WindSpeedUnit.MetersPerSecond ? "m/s"
                               : doc.WindUnit == WindSpeedUnit.MilesPerHour ? "mph" : "fps";

            // Header row: Event#, Round#, Heat#, EventName, Wind, WindUnit, ..., Distance, GunTimeOfDay
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},,,,{6:0},{7:HH:mm:ss.ffff}",
                doc.EventNumber, doc.RoundNumber, doc.HeatNumber,
                EscapeCsv(doc.EventName), windStr, windUnitStr,
                doc.DistanceMeters, doc.GunTimeOfDay);
            sb.AppendLine();

            List<CompetitorEntry> sorted = new List<CompetitorEntry>(doc.Competitors);
            sorted.Sort(delegate(CompetitorEntry a, CompetitorEntry b)
            {
                if (a.HasValidNumericTime && b.HasValidNumericTime) return a.RawTimeSeconds.CompareTo(b.RawTimeSeconds);
                if (a.HasValidNumericTime) return -1;
                if (b.HasValidNumericTime) return 1;
                return a.Lane.CompareTo(b.Lane);
            });

            for (int i = 0; i < sorted.Count; i++)
            {
                CompetitorEntry c = sorted[i];
                string placeStr = c.GetPlaceDisplay();
                string timeStr = c.GetFormattedOfficialTime(doc.Precision);
                string deltaStr = doc.FormatDeltaForCompetitor(c).TrimStart('+');
                string reacStr = !double.IsNaN(c.ReactionTimeSeconds) && c.ReactionTimeSeconds > 0
                    ? c.ReactionTimeSeconds.ToString("0.000", CultureInfo.InvariantCulture) : "";
                string todStr = doc.GunTimeOfDay.ToString("HH:mm:ss.ff", CultureInfo.InvariantCulture);
                string speedStr = c.HasValidNumericTime && doc.DistanceMeters > 0
                    ? (doc.DistanceMeters / c.RawTimeSeconds).ToString("0.00", CultureInfo.InvariantCulture) : "";

                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},,{10},,,,{8},{8},{11},",
                    placeStr,
                    EscapeCsv(c.BibId),
                    c.Lane > 0 ? c.Lane.ToString(CultureInfo.InvariantCulture) : "",
                    EscapeCsv(c.LastName),
                    EscapeCsv(c.FirstName),
                    EscapeCsv(c.Affiliation),
                    timeStr,
                    EscapeCsv(c.License),
                    deltaStr,
                    reacStr,
                    todStr,
                    EscapeCsv(speedStr));
                sb.AppendLine();
            }

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Saves an OptiFinish native clean-room race package (.optr + camera .png files).
        /// </summary>
        public static void SaveNativeRacePackage(RaceEventDocument doc, string filePath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# OptiFinish Studio Clean-Room Race Package v1.0");
            sb.AppendLine("EVENT_NUMBER=" + doc.EventNumber.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("ROUND_NUMBER=" + doc.RoundNumber.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("HEAT_NUMBER=" + doc.HeatNumber.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("EVENT_NAME=" + doc.EventName);
            sb.AppendLine("DISTANCE_METERS=" + doc.DistanceMeters.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("COMPETITION=" + doc.CompetitionName);
            sb.AppendLine("WIND_READING=" + doc.WindReading.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("HAS_WIND=" + (doc.HasWindReading ? "1" : "0"));
            sb.AppendLine("PRECISION=" + ((int)doc.Precision).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("LANE_COUNT=" + doc.LaneCount.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("HAS_START=" + (doc.HasStartSignal ? "1" : "0"));
            sb.AppendLine("START_OFFSET=" + doc.StartOffsetSeconds.ToString("0.0000", CultureInfo.InvariantCulture));
            sb.AppendLine("SHOW_EVENT_OVERLAY=" + (doc.ShowEventNameOverlay ? "1" : "0"));

            string baseDir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(baseDir)) baseDir = ".";
            string baseName = Path.GetFileNameWithoutExtension(filePath);

            for (int i = 0; i < doc.Cameras.Count; i++)
            {
                SlitCameraStream cam = doc.Cameras[i];
                string pngName = string.Format(CultureInfo.InvariantCulture, "{0}_cam{1}.png", baseName, i + 1);
                string pngPath = Path.Combine(baseDir, pngName);
                if (cam.RawStripBitmap != null)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        cam.RawStripBitmap.Save(ms, ImageFormat.Png);
                        File.WriteAllBytes(pngPath, ms.ToArray());
                    }
                }
                sb.AppendFormat(CultureInfo.InvariantCulture, "CAMERA={0}|{1}|{2}|{3}|{4:0.0000}|{5}",
                    cam.CameraId, cam.DisplayName, cam.SampleRateHz, (int)cam.Orientation, cam.CaptureStartSeconds, pngName);
                sb.AppendLine();

                if (cam.ActiveCrop != null)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "CAMERA_CROP={0}|{1}|{2:0.0000}|{3:0.0000}",
                        cam.ActiveCrop.StartCol, cam.ActiveCrop.EndCol, cam.ActiveCrop.TopRatio, cam.ActiveCrop.BottomRatio);
                    sb.AppendLine();
                }

                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "CAMERA_ADJ={0}|{1}|{2:0.000}|{3:0.000}|{4}|{5}|{6}|{7}|{8}|{9}",
                    cam.BrightnessOffset, cam.ContrastOffset, cam.GammaValue, cam.VerticalStretch,
                    cam.BlackLevel, cam.WhiteBalanceRed, cam.WhiteBalanceGreen, cam.WhiteBalanceBlue,
                    cam.EnhanceEnabled ? 1 : 0, cam.ReverseLaneOrder ? 1 : 0);
                sb.AppendLine();

                if (cam.Profile != null && !string.IsNullOrEmpty(cam.Profile.SerialNumber))
                {
                    sb.AppendLine("CAMERA_SN=" + cam.Profile.SerialNumber);
                }
            }

            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                CompetitorEntry c = doc.Competitors[i];
                string rawStr = double.IsNaN(c.RawTimeSeconds) ? "NaN" : c.RawTimeSeconds.ToString("0.00000", CultureInfo.InvariantCulture);
                string evStr = double.IsNaN(c.EvaluatedTimeSeconds) ? "NaN" : c.EvaluatedTimeSeconds.ToString("0.00000", CultureInfo.InvariantCulture);
                int precOv = c.PrecisionOverride.HasValue ? (int)c.PrecisionOverride.Value : -1;
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "COMPETITOR={0}|{1}|{2}|{3}|{4}|{5}|{6:0.0000}|{7}|{8:0.0000}|{9}|{10}|{11}|{12}",
                    c.Lane, c.BibId, c.LastName, c.FirstName, c.Affiliation,
                    rawStr,
                    double.IsNaN(c.ReactionTimeSeconds) ? -1.0 : c.ReactionTimeSeconds,
                    (int)c.Status,
                    c.EvaluatedVerticalRatio,
                    c.HasImageEvaluation ? 1 : 0,
                    evStr,
                    c.EvaluatedCameraIndex,
                    precOv);
                sb.AppendLine();
            }

            for (int i = 0; i < doc.Overlays.Count; i++)
            {
                ImageOverlayItem ov = doc.Overlays[i];
                if (ov == null) continue;
                string safeText = (ov.Text ?? "").Replace("\r", "").Replace("\n", "\\n").Replace("|", "/");
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "OVERLAY={0}|{1:0.00000}|{2:0.0000}|{3}|{4}|{5}|{6:0.0}|{7}|{8}|{9}|{10}",
                    ov.GroupIndex, ov.TimeSeconds, ov.VerticalRatio, ov.OverlayColor.ToArgb(),
                    ov.Visible ? 1 : 0, ov.FontName ?? "Segoe UI", ov.FontSizePt,
                    ov.FontBold ? 1 : 0, ov.ShowLine ? 1 : 0, ov.ShowSplits ? 1 : 0, safeText);
                sb.AppendLine();
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Loads an OptiFinish native clean-room race package (.optr + camera .png files),
        /// restoring ActiveCrop, image adjustments, overlays, and competitor evaluations.
        /// </summary>
        public static RaceEventDocument LoadNativeRacePackage(string filePath)
        {
            RaceEventDocument doc = new RaceEventDocument
            {
                SourceFilePath = filePath
            };
            doc.Competitors.Clear();
            doc.Cameras.Clear();
            doc.Overlays.Clear();

            string baseDir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(baseDir)) baseDir = ".";

            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            SlitCameraStream lastCam = null;

            for (int li = 0; li < lines.Length; li++)
            {
                string raw = (lines[li] ?? "").Trim();
                if (raw.Length == 0 || raw.StartsWith("#")) continue;
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                string key = raw.Substring(0, eq).Trim().ToUpperInvariant();
                string val = raw.Substring(eq + 1);

                if (key == "EVENT_NUMBER") { int n; if (int.TryParse(val, out n)) doc.EventNumber = n; }
                else if (key == "ROUND_NUMBER") { int n; if (int.TryParse(val, out n)) doc.RoundNumber = n; }
                else if (key == "HEAT_NUMBER") { int n; if (int.TryParse(val, out n)) doc.HeatNumber = n; }
                else if (key == "EVENT_NAME") { doc.EventName = val; }
                else if (key == "DISTANCE_METERS") { double d; if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) doc.DistanceMeters = d; }
                else if (key == "COMPETITION") { doc.CompetitionName = val; }
                else if (key == "WIND_READING") { double w; if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out w)) doc.WindReading = w; }
                else if (key == "HAS_WIND") { doc.HasWindReading = (val == "1"); }
                else if (key == "PRECISION") { int p; if (int.TryParse(val, out p) && Enum.IsDefined(typeof(TimePrecision), p)) doc.Precision = (TimePrecision)p; }
                else if (key == "LANE_COUNT") { int lc; if (int.TryParse(val, out lc) && lc > 0) doc.LaneCount = lc; }
                else if (key == "HAS_START") { doc.HasStartSignal = (val == "1"); }
                else if (key == "START_OFFSET") { double so; if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out so)) doc.StartOffsetSeconds = so; }
                else if (key == "SHOW_EVENT_OVERLAY") { doc.ShowEventNameOverlay = (val == "1"); }
                else if (key == "CAMERA")
                {
                    string[] parts = val.Split('|');
                    if (parts.Length >= 6)
                    {
                        int rate = 2000;
                        int.TryParse(parts[2], out rate);
                        int orient = 0;
                        int.TryParse(parts[3], out orient);
                        double capStart = 0;
                        double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out capStart);
                        string pngPath = Path.IsPathRooted(parts[5]) ? parts[5] : Path.Combine(baseDir, parts[5]);
                        Bitmap rawBmp = null;
                        if (File.Exists(pngPath))
                        {
                            using (FileStream fs = new FileStream(pngPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (Image img = Image.FromStream(fs))
                            {
                                rawBmp = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb);
                                using (Graphics g = Graphics.FromImage(rawBmp))
                                {
                                    g.DrawImage(img, 0, 0, img.Width, img.Height);
                                }
                            }
                        }
                        if (rawBmp == null)
                        {
                            rawBmp = new Bitmap(2000, 480, PixelFormat.Format24bppRgb);
                        }
                        lastCam = new SlitCameraStream
                        {
                            CameraId = parts[0],
                            DisplayName = parts[1],
                            SampleRateHz = Math.Max(100, rate),
                            Orientation = Enum.IsDefined(typeof(StripOrientation), orient) ? (StripOrientation)orient : StripOrientation.LeftToRight,
                            CaptureStartSeconds = capStart,
                            FrameHeightPx = rawBmp.Height,
                            RawStripBitmap = rawBmp,
                            SourceFilePath = pngPath,
                            IsEnabled = true
                        };
                        doc.Cameras.Add(lastCam);
                    }
                }
                else if (key == "CAMERA_CROP" && lastCam != null)
                {
                    string[] parts = val.Split('|');
                    if (parts.Length >= 4)
                    {
                        int sc, ec;
                        float tr, br;
                        if (int.TryParse(parts[0], out sc) && int.TryParse(parts[1], out ec) &&
                            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out tr) &&
                            float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out br))
                        {
                            lastCam.ActiveCrop = new CropWindow(sc, ec, tr, br);
                        }
                    }
                }
                else if (key == "CAMERA_ADJ" && lastCam != null)
                {
                    string[] parts = val.Split('|');
                    if (parts.Length >= 4)
                    {
                        int iv;
                        double dv;
                        if (int.TryParse(parts[0], out iv)) lastCam.BrightnessOffset = iv;
                        if (int.TryParse(parts[1], out iv)) lastCam.ContrastOffset = iv;
                        if (double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) lastCam.GammaValue = dv;
                        if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) lastCam.VerticalStretch = dv;
                        if (parts.Length >= 5 && int.TryParse(parts[4], out iv)) lastCam.BlackLevel = iv;
                        if (parts.Length >= 6 && int.TryParse(parts[5], out iv)) lastCam.WhiteBalanceRed = iv;
                        if (parts.Length >= 7 && int.TryParse(parts[6], out iv)) lastCam.WhiteBalanceGreen = iv;
                        if (parts.Length >= 8 && int.TryParse(parts[7], out iv)) lastCam.WhiteBalanceBlue = iv;
                        if (parts.Length >= 9) lastCam.EnhanceEnabled = (parts[8] == "1");
                        if (parts.Length >= 10) lastCam.ReverseLaneOrder = (parts[9] == "1");
                    }
                }
                else if (key == "CAMERA_SN" && lastCam != null)
                {
                    if (lastCam.Profile == null) lastCam.Profile = new CameraSettingsProfile();
                    lastCam.Profile.SerialNumber = val.Trim();
                }
                else if (key == "COMPETITOR")
                {
                    string[] parts = val.Split('|');
                    if (parts.Length >= 9)
                    {
                        int lane = 1, st = 0;
                        double rawT = double.NaN, reac = -1.0;
                        float vr = 0.5f;
                        int.TryParse(parts[0], out lane);
                        if (!string.Equals(parts[5], "NaN", StringComparison.OrdinalIgnoreCase))
                        {
                            double parsedRaw;
                            if (double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out parsedRaw))
                            {
                                bool isLegacySentinel = (Math.Abs(parsedRaw + 1.0) < 1e-5) && (parts.Length < 10 || parts[9] != "1");
                                if (!isLegacySentinel) rawT = parsedRaw;
                            }
                        }
                        double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out reac);
                        int.TryParse(parts[7], out st);
                        float.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out vr);

                        CompetitorEntry c = new CompetitorEntry
                        {
                            Lane = lane,
                            BibId = parts[1],
                            LastName = parts[2],
                            FirstName = parts[3],
                            Affiliation = parts[4],
                            RawTimeSeconds = rawT,
                            ReactionTimeSeconds = reac >= 0 ? reac : double.NaN,
                            Status = Enum.IsDefined(typeof(CompetitorStatus), st) ? (CompetitorStatus)st : CompetitorStatus.Valid,
                            EvaluatedVerticalRatio = vr
                        };
                        if (parts.Length >= 12)
                        {
                            double evT = double.NaN;
                            int evCam = 0;
                            c.HasImageEvaluation = (parts[9] == "1");
                            if (!string.Equals(parts[10], "NaN", StringComparison.OrdinalIgnoreCase) &&
                                double.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out evT))
                            {
                                bool isLegacySentinel = (Math.Abs(evT + 1.0) < 1e-5) && !c.HasImageEvaluation;
                                if (!isLegacySentinel) c.EvaluatedTimeSeconds = evT;
                            }
                            if (int.TryParse(parts[11], out evCam)) c.EvaluatedCameraIndex = evCam;
                        }
                        else if (!double.IsNaN(c.RawTimeSeconds))
                        {
                            c.HasImageEvaluation = true;
                            c.EvaluatedTimeSeconds = c.RawTimeSeconds;
                        }
                        if (parts.Length >= 13)
                        {
                            int po;
                            if (int.TryParse(parts[12], out po) && po >= 0 && Enum.IsDefined(typeof(TimePrecision), po))
                            {
                                c.PrecisionOverride = (TimePrecision)po;
                            }
                        }
                        doc.Competitors.Add(c);
                    }
                }
                else if (key == "OVERLAY")
                {
                    string[] parts = val.Split(new char[] { '|' }, 11);
                    if (parts.Length >= 11)
                    {
                        int grp = 0, argb = Color.Yellow.ToArgb();
                        double ts = 0;
                        float vr = 0.5f, fsz = 10f;
                        int.TryParse(parts[0], out grp);
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out ts);
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vr);
                        int.TryParse(parts[3], out argb);
                        float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out fsz);
                        doc.Overlays.Add(new ImageOverlayItem
                        {
                            GroupIndex = Math.Max(0, Math.Min(8, grp)),
                            TimeSeconds = ts,
                            VerticalRatio = vr,
                            OverlayColor = Color.FromArgb(argb),
                            Visible = (parts[4] == "1"),
                            FontName = string.IsNullOrEmpty(parts[5]) ? "Segoe UI" : parts[5],
                            FontSizePt = fsz > 1f ? fsz : 10f,
                            FontBold = (parts[7] == "1"),
                            ShowLine = (parts[8] == "1"),
                            ShowSplits = (parts[9] == "1"),
                            Text = parts[10].Replace("\\n", "\n")
                        });
                    }
                }
            }

            if (doc.Cameras.Count == 0)
            {
                SlitScanEngine.BuildSynthesizedCameraStrips(doc);
            }
            else
            {
                for (int i = 0; i < doc.Cameras.Count; i++)
                {
                    SlitScanEngine.ComputeMotionEnergyProfile(doc.Cameras[i]);
                    SlitScanEngine.UpdateDisplayBitmap(doc.Cameras[i]);
                }
            }
            doc.RecalculatePlaces();
            return doc;
        }

        public static RaceEventDocument CreateSample100mHurdlesWomen()
        {
            RaceEventDocument doc = new RaceEventDocument
            {
                EventNumber = 17,
                RoundNumber = 2,
                HeatNumber = 1,
                EventName = "100 Meter Hurdles Women",
                DistanceMeters = 100.0,
                CompetitionName = "National Track & Field Championships",
                SponsorName = "OptiFinish Studio",
                WindReading = 1.2,
                HasWindReading = true,
                WindUnit = WindSpeedUnit.MetersPerSecond,
                WindMode = WindWindowMode.Auto13s_Hurdles,
                LaneCount = 8,
                Precision = TimePrecision.Hundredths
            };

            AddSampleRunner(doc, 6, "643", "Vaughn", "Angela", "University of Texas", 12.8432, 0.142);
            AddSampleRunner(doc, 7, "391", "Lawson", "Tonya", "Unattached", 12.8818, 0.151);
            AddSampleRunner(doc, 4, "161", "Dickey", "Cheryl", "Nike Athletics", 12.9144, 0.138);
            AddSampleRunner(doc, 5, "61",  "Brewer", "Elisha", "Univ of Arkansas", 12.9502, 0.164);
            AddSampleRunner(doc, 3, "348", "Jolivet-Outing", "Rosa", "Texas A&M Univ", 13.0218, 0.149);
            AddSampleRunner(doc, 8, "53",  "Bowles", "Dawn", "Asics Track Club", 13.0890, 0.157);
            AddSampleRunner(doc, 2, "413", "Lott", "Tiffany", "Unattached", 13.1405, 0.168);
            AddSampleRunner(doc, 1, "436", "McCray", "Yolanda", "Unattached", 13.2188, 0.173);

            doc.RecalculatePlaces();
            SlitScanEngine.BuildSynthesizedCameraStrips(doc);
            return doc;
        }

        public static RaceEventDocument CreateSample100mMenDeadHeat()
        {
            RaceEventDocument doc = new RaceEventDocument
            {
                EventNumber = 1,
                RoundNumber = 3,
                HeatNumber = 1,
                EventName = "100 Meter Dash Men — Final",
                DistanceMeters = 100.0,
                CompetitionName = "World Athletics Continental Tour",
                SponsorName = "OptiFinish Studio",
                WindReading = 0.9,
                HasWindReading = true,
                WindUnit = WindSpeedUnit.MetersPerSecond,
                WindMode = WindWindowMode.Auto10s_100m,
                LaneCount = 8,
                Precision = TimePrecision.Hundredths
            };

            // Notice 1st and 2nd both round up to 9.90s at 1/100s precision, separated by 0.0036s at 1/1000s!
            AddSampleRunner(doc, 4, "564", "Crawford", "Shawn", "Clemson", 9.8912, 0.134);
            AddSampleRunner(doc, 5, "775", "Trammell", "Terrence", "South Carolina", 9.8948, 0.141);
            AddSampleRunner(doc, 3, "548", "Myles-Mills", "Leonard", "Brigham Young", 9.9621, 0.148);
            AddSampleRunner(doc, 6, "881", "Hooker", "JaWarren", "Washington", 10.0134, 0.152);
            AddSampleRunner(doc, 2, "584", "Carroll", "Daymon", "Florida", 10.0580, 0.161);
            AddSampleRunner(doc, 7, "836", "Wade", "Larry", "Texas A&M", 10.0915, 0.145);
            AddSampleRunner(doc, 8, "715", "Campbell", "Milton", "North Carolina", 10.1422, 0.167);
            AddSampleRunner(doc, 1, "621", "Darling", "Dennis", "Houston", 10.1940, 0.159);

            doc.RecalculatePlaces();
            SlitScanEngine.BuildSynthesizedCameraStrips(doc);
            return doc;
        }

        public static RaceEventDocument CreateSample1500mDistanceRun()
        {
            RaceEventDocument doc = new RaceEventDocument
            {
                EventNumber = 4,
                RoundNumber = 1,
                HeatNumber = 1,
                EventName = "1500 Meter Run Men",
                DistanceMeters = 1500.0,
                CompetitionName = "Golden Grand Prix Athletics",
                SponsorName = "OptiFinish Studio",
                WindReading = 0.0,
                HasWindReading = false,
                LaneCount = 8,
                TotalLaps = 4,
                LapsPerSplit = 1,
                MinValidSplitSeconds = 50.0,
                MaxValidSplitSeconds = 75.0,
                Precision = TimePrecision.Hundredths
            };

            AddSampleRunnerWithSplits(doc, 4, "25", "El Guerrouj", "Hicham", "Morocco", 209.914, new double[] { 54.20, 110.45, 165.80, 209.914 });
            AddSampleRunnerWithSplits(doc, 3, "29", "Sullivan", "Kevin", "Canada", 211.816, new double[] { 54.65, 111.10, 166.90, 211.816 });
            AddSampleRunnerWithSplits(doc, 6, "26", "Lagat", "Bernard", "Kenya", 213.132, new double[] { 54.90, 111.40, 167.45, 213.132 });
            AddSampleRunnerWithSplits(doc, 5, "35", "Kaouch", "Adil", "Morocco", 213.394, new double[] { 55.10, 111.75, 167.80, 213.394 });
            AddSampleRunnerWithSplits(doc, 2, "28", "Webb", "Alan", "U.S.A.", 213.428, new double[] { 55.40, 112.05, 168.10, 213.428 });
            AddSampleRunnerWithSplits(doc, 7, "39", "de Souza", "Hudson", "Brazil", 214.385, new double[] { 55.60, 112.50, 168.90, 214.385 });
            AddSampleRunnerWithSplits(doc, 1, "33", "Hood", "Graham", "Canada", 214.612, new double[] { 55.85, 112.80, 169.20, 214.612 });
            AddSampleRunnerWithSplits(doc, 8, "31", "Berryhill", "Bryan", "U.S.A.", 215.004, new double[] { 56.05, 113.10, 169.60, 215.004 });

            CompetitorEntry dnf = new CompetitorEntry
            {
                Lane = 8,
                BibId = "27",
                LastName = "Pyrah",
                FirstName = "Jason",
                Affiliation = "U.S.A.",
                Status = CompetitorStatus.DNF
            };
            doc.Competitors.Add(dnf);

            doc.RecalculatePlaces();
            SlitScanEngine.BuildSynthesizedCameraStrips(doc);
            return doc;
        }

        private static void AddSampleRunner(
            RaceEventDocument doc,
            int lane,
            string bib,
            string lastName,
            string firstName,
            string affiliation,
            double rawSec,
            double reactionSec)
        {
            CompetitorEntry c = new CompetitorEntry
            {
                Lane = lane,
                BibId = bib,
                LastName = lastName,
                FirstName = firstName,
                Affiliation = affiliation,
                RawTimeSeconds = rawSec,
                ReactionTimeSeconds = reactionSec,
                Status = CompetitorStatus.Valid,
                HasImageEvaluation = true,
                EvaluatedTimeSeconds = rawSec,
                EvaluatedVerticalRatio = doc.LaneToCenterRatio(lane),
                EvaluatedCameraIndex = 0
            };
            doc.Competitors.Add(c);
        }

        private static void AddSampleRunnerWithSplits(
            RaceEventDocument doc,
            int lane,
            string bib,
            string lastName,
            string firstName,
            string affiliation,
            double rawSec,
            double[] cumulativeSplits)
        {
            CompetitorEntry c = new CompetitorEntry
            {
                Lane = lane,
                BibId = bib,
                LastName = lastName,
                FirstName = firstName,
                Affiliation = affiliation,
                RawTimeSeconds = rawSec,
                Status = CompetitorStatus.Valid,
                HasImageEvaluation = true,
                EvaluatedTimeSeconds = rawSec,
                EvaluatedVerticalRatio = doc.LaneToCenterRatio(lane),
                EvaluatedCameraIndex = 0
            };

            double prev = 0.0;
            for (int i = 0; i < cumulativeSplits.Length; i++)
            {
                double lap = cumulativeSplits[i] - prev;
                c.Splits.Add(new LapSplitRecord(i + 1, cumulativeSplits[i], lap, SplitFlag.Accepted));
                prev = cumulativeSplits[i];
            }

            doc.Competitors.Add(c);
        }

        public static double ParseFormattedTime(string text)
        {
            if (string.IsNullOrEmpty(text)) return double.NaN;
            string s = text.Trim().Replace(',', '.');

            string[] colonParts = s.Split(':');
            if (colonParts.Length == 1)
            {
                double v;
                if (double.TryParse(colonParts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
                return double.NaN;
            }
            if (colonParts.Length == 2)
            {
                int mins;
                double secs;
                if (int.TryParse(colonParts[0], out mins) &&
                    double.TryParse(colonParts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out secs))
                {
                    return mins * 60.0 + secs;
                }
            }
            if (colonParts.Length == 3)
            {
                int hrs, mins;
                double secs;
                if (int.TryParse(colonParts[0], out hrs) &&
                    int.TryParse(colonParts[1], out mins) &&
                    double.TryParse(colonParts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out secs))
                {
                    return hrs * 3600.0 + mins * 60.0 + secs;
                }
            }
            return double.NaN;
        }

        private static double InferDistanceFromEventName(string name)
        {
            if (string.IsNullOrEmpty(name)) return 100.0;
            string lower = name.ToLowerInvariant();
            if (lower.Contains("mile")) return 1609.34;
            if (lower.Contains("10000") || lower.Contains("10,000")) return 10000.0;
            if (lower.Contains("5000") || lower.Contains("5,000")) return 5000.0;
            if (lower.Contains("3000") || lower.Contains("3,000")) return 3000.0;
            if (lower.Contains("1500") || lower.Contains("1,500")) return 1500.0;
            if (lower.Contains("800")) return 800.0;
            if (lower.Contains("400")) return 400.0;
            if (lower.Contains("200")) return 200.0;
            if (lower.Contains("110")) return 110.0;
            if (lower.Contains("60")) return 60.0;
            return 100.0;
        }

        private static string[] SplitCsvLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (ch == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(ch);
                }
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.IndexOf(',') >= 0 || val.IndexOf('"') >= 0)
            {
                return "\"" + val.Replace("\"", "\"\"") + "\"";
            }
            return val;
        }

        public static void ExportSplitTimesCsv(RaceEventDocument doc, string outputPath)
        {
            if (doc == null || string.IsNullOrEmpty(outputPath)) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Place,Lane,Id,Name,SplitIndex,CumTime,LapTime,Flag");
            for (int i = 0; i < doc.Competitors.Count; i++)
            {
                CompetitorEntry c = doc.Competitors[i];
                string fullName = ((c.FirstName ?? "") + " " + (c.LastName ?? "")).Trim();
                if (c.Splits.Count == 0)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2},{3},0,{4},,\r\n",
                        c.GetPlaceDisplay(), c.Lane, EscapeCsv(c.BibId), EscapeCsv(fullName),
                        c.GetFormattedOfficialTime(doc.Precision));
                }
                else
                {
                    for (int s = 0; s < c.Splits.Count; s++)
                    {
                        LapSplitRecord sp = c.Splits[s];
                        sb.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6},{7}\r\n",
                            c.GetPlaceDisplay(), c.Lane, EscapeCsv(c.BibId), EscapeCsv(fullName),
                            sp.LapNumber,
                            CompetitorEntry.FormatSeconds(sp.CumulativeSeconds, doc.Precision),
                            CompetitorEntry.FormatSeconds(sp.LapSeconds, doc.Precision),
                            sp.Flag);
                    }
                }
            }
            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        public static int ImportReacTimeData(RaceEventDocument doc, string filePath)
        {
            if (doc == null || string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return 0;
            int updated = 0;
            string[] lines = File.ReadAllLines(filePath, Encoding.Default);
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (string.IsNullOrEmpty(ln) || ln.StartsWith(";")) continue;
                string[] parts = SplitCsvLine(ln);
                if (parts.Length < 2) continue;

                int lane;
                double reac;
                if (int.TryParse(parts[0].Trim(), out lane) &&
                    double.TryParse(parts[parts.Length - 1].Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out reac) &&
                    reac > 0.0 && reac < 2.0)
                {
                    for (int c = 0; c < doc.Competitors.Count; c++)
                    {
                        if (doc.Competitors[c].Lane == lane)
                        {
                            doc.Competitors[c].ReactionTimeSeconds = reac;
                            updated++;
                            break;
                        }
                    }
                }
            }
            return updated;
        }

        public static string BackupEventsToDirectory(IEnumerable<RaceEventDocument> events, string backupDir)
        {
            if (string.IsNullOrEmpty(backupDir))
            {
                backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backup");
            }
            Directory.CreateDirectory(backupDir);
            int count = 0;
            foreach (RaceEventDocument doc in events)
            {
                if (doc == null) continue;
                string safe = string.Format(CultureInfo.InvariantCulture, "{0:000}-{1}-{2:00}_{3}",
                    doc.EventNumber, doc.RoundNumber, doc.HeatNumber,
                    DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
                SaveNativeRacePackage(doc, Path.Combine(backupDir, safe + ".optr"));
                ExportResultsLifOrCsv(doc, Path.Combine(backupDir, string.Format(CultureInfo.InvariantCulture, "{0:000}-{1}-{2:00}.lif", doc.EventNumber, doc.RoundNumber, doc.HeatNumber)));
                count++;
            }
            return backupDir + " (" + count + " event(s))";
        }
    }
}
