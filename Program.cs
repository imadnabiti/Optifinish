using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using OptiFinish.Core;
using OptiFinish.UI;

[assembly: AssemblyTitle("OptiFinish Studio")]
[assembly: AssemblyProduct("OptiFinish Studio")]
[assembly: AssemblyDescription("Clean-Room Slit-Scan Photo-Finish Timing & Athletics Evaluation Suite")]
[assembly: AssemblyCompany("OptiFinish Open Timing Systems")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Clean-Room Implementation")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace OptiFinish
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                string icoPath = Path.Combine(baseDir, "OptiFinish.ico");
                if (!File.Exists(icoPath))
                {
                    AppIconGenerator.GenerateIconFile(icoPath);
                }
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args != null && args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                return RunSelfTest(baseDir);
            }

            Application.Run(new MainWindow());
            return 0;
        }

        private static int RunSelfTest(string baseDir)
        {
            string sampleDir = Path.Combine(baseDir, "SampleData");
            Directory.CreateDirectory(sampleDir);
            string reportPath = Path.Combine(sampleDir, "SelfTestReport.txt");

            StringBuilder report = new StringBuilder();
            try
            {
                report.AppendLine("=== OptiFinish Studio Clean-Room Verification Report ===");
                report.AppendLine("Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            RaceEventDocument doc = MeetDatabaseService.CreateSample100mHurdlesWomen();

            report.AppendLine("Loaded Event: " + doc.EventName + " | Cameras: " + doc.Cameras.Count + " | Competitors: " + doc.Competitors.Count);
            foreach (CompetitorEntry c in doc.Competitors)
            {
                report.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  Place {0,2} | Lane {1} | Bib #{2,-4} | {3,-18} | Raw={4:0.0000}s | Official(0.01)={5} | Official(0.001)={6} | Speed={7}",
                    c.GetPlaceDisplay(),
                    c.Lane,
                    c.BibId,
                    c.LastName + ", " + c.FirstName,
                    c.RawTimeSeconds,
                    c.GetFormattedOfficialTime(TimePrecision.Hundredths),
                    c.GetFormattedOfficialTime(TimePrecision.Thousandths),
                    c.GetFormattedSpeed(doc.DistanceMeters, doc.SpeedUnit)));
            }

            string pngPath = Path.Combine(sampleDir, "100m_Hurdles_Annotated_PhotoFinish.png");
            using (Bitmap annotated = SlitScanEngine.RenderAnnotatedExportBitmap(doc, 0))
            {
                annotated.Save(pngPath, ImageFormat.Png);
            }

            string lifPath = Path.Combine(sampleDir, "004-1-01-OptiFinish.lif");
            MeetDatabaseService.ExportResultsLifOrCsv(doc, lifPath);

            string optrPath = Path.Combine(sampleDir, "100m_Hurdles_Women.optr");
            CropWindow testCrop = new CropWindow(180, 620, 0.15f, 0.75f);
            doc.Cameras[0].ActiveCrop = testCrop;
            doc.Cameras[0].BrightnessOffset = 12;
            doc.Overlays.Add(new ImageOverlayItem
            {
                GroupIndex = 2,
                TimeSeconds = doc.Cameras[0].ColumnToTimeSeconds(360),
                VerticalRatio = 0.45f,
                Text = "Test Crop Overlay",
                OverlayColor = Color.Yellow,
                Visible = true
            });
            MeetDatabaseService.SaveNativeRacePackage(doc, optrPath);

            // Verify crop + SaveBitmapToFile (.jpg, .png, .tga) + PrintDialogSettings + .optr reload
            using (Bitmap directCrop = SlitScanEngine.RenderDirectExportBitmap(doc, 0, testCrop, true))
            {
                string cropJpg = Path.Combine(sampleDir, "TestCrop_Direct.jpg");
                string cropPng = Path.Combine(sampleDir, "TestCrop_Direct.png");
                string cropTga = Path.Combine(sampleDir, "TestCrop_Direct.tga");
                SlitScanEngine.SaveBitmapToFile(directCrop, cropJpg, 92);
                SlitScanEngine.SaveBitmapToFile(directCrop, cropPng, 92);
                SlitScanEngine.SaveBitmapToFile(directCrop, cropTga, 92);
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Cropped Direct Export: {0}x{1}px (jpg={2}B, png={3}B, tga={4}B)",
                    directCrop.Width, directCrop.Height,
                    new FileInfo(cropJpg).Length, new FileInfo(cropPng).Length, new FileInfo(cropTga).Length));
            }

            PrintDialogSettings pCfg = new PrintDialogSettings
            {
                Zoom = 2,
                Smooth = true,
                Dpi = 600,
                LaneBarsOn = true,
                LimitResOn = true,
                LimitResValue = 800,
                PrintResultsOn = true,
                MaxResults = 4,
                ResultsRange = "1-3"
            };
            using (Bitmap annotCrop = SlitScanEngine.RenderAnnotatedExportBitmap(doc, 0, testCrop, pCfg))
            {
                string annotCropPng = Path.Combine(sampleDir, "TestCrop_Annotated.png");
                string annotCropPdf = Path.Combine(sampleDir, "TestCrop_Annotated.pdf");
                SlitScanEngine.SaveBitmapToFile(annotCrop, annotCropPng, 92);
                SlitScanEngine.SaveBitmapToFile(annotCrop, annotCropPdf, 92);
                byte[] pdfHead = File.ReadAllBytes(annotCropPdf);
                bool validPdf = pdfHead.Length > 100 && pdfHead[0] == (byte)'%' && pdfHead[1] == (byte)'P' && pdfHead[2] == (byte)'D' && pdfHead[3] == (byte)'F';
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Cropped Annotated Print/Bitmap Export (Zoom2, 600DPI, LimitRes800, Results 1-3): {0}x{1}px (png={2}B, pdf={3}B ValidPdf={4})",
                    annotCrop.Width, annotCrop.Height, new FileInfo(annotCropPng).Length, pdfHead.Length, validPdf));
            }

            using (RaceEventDocument reloaded = MeetDatabaseService.LoadNativeRacePackage(optrPath))
            {
                CropWindow rc = reloaded.Cameras.Count > 0 ? reloaded.Cameras[0].ActiveCrop : null;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    ".optr Round-Trip: Cams={0}, Competitors={1}, Overlays={2}, Crop=[{3}..{4}, {5:0.00}..{6:0.00}], Brightness={7}",
                    reloaded.Cameras.Count, reloaded.Competitors.Count, reloaded.Overlays.Count,
                    rc != null ? rc.StartCol : -1, rc != null ? rc.EndCol : -1,
                    rc != null ? rc.TopRatio : -1f, rc != null ? rc.BottomRatio : -1f,
                    reloaded.Cameras.Count > 0 ? reloaded.Cameras[0].BrightnessOffset : 0));
            }
            doc.Cameras[0].ActiveCrop = null;
            doc.Cameras[0].BrightnessOffset = 0;

            // Dead-heat preset test
            RaceEventDocument deadHeatDoc = MeetDatabaseService.CreateSample100mMenDeadHeat();
            string deadHeatPng = Path.Combine(sampleDir, "100m_Men_DeadHeat_PhotoFinish.png");
            using (Bitmap annotatedDh = SlitScanEngine.RenderAnnotatedExportBitmap(deadHeatDoc, 0))
            {
                annotatedDh.Save(deadHeatPng, ImageFormat.Png);
            }
            deadHeatDoc.Dispose();
            doc.Dispose();

            // Native C:\Lynx binary .evn + .i01/.i02/.spl verification
            List<string> nativeEvnFiles = MeetDatabaseService.DiscoverNativeLynxEventFiles(Path.GetFullPath(Path.Combine(baseDir, "..")));
            report.AppendLine();
            report.AppendLine("=== Native C:\\Lynx .evn + .i01/.i02/.spl Decoder Verification (" + nativeEvnFiles.Count + " files) ===");
            for (int i = 0; i < nativeEvnFiles.Count; i++)
            {
                string evnPath = nativeEvnFiles[i];
                using (RaceEventDocument nativeDoc = MeetDatabaseService.LoadNativeLynxEventFile(evnPath))
                {
                    report.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  [{0}] {1} -> Event='{2}' ({3}), Competitors={4}, Cameras={5}, Splits={6}",
                        i + 1,
                        Path.GetFileName(evnPath),
                        nativeDoc.EventName,
                        nativeDoc.GetEventCodeString(),
                        nativeDoc.Competitors.Count,
                        nativeDoc.Cameras.Count,
                        nativeDoc.SplitMarkerSeconds.Count));

                    for (int c = 0; c < nativeDoc.Cameras.Count; c++)
                    {
                        SlitCameraStream cam = nativeDoc.Cameras[c];
                        report.AppendLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "      Cam {0}: '{1}' | {2} | Span={3:0.0000}s..{4:0.0000}s",
                            c + 1,
                            cam.DisplayName,
                            cam.NativeFormatSummary,
                            cam.ColumnToTimeSeconds(0),
                            cam.ColumnToTimeSeconds(Math.Max(0, cam.TotalColumns - 1))));
                    }

                    if (nativeDoc.Competitors.Count > 0 && nativeDoc.Competitors[0].HasValidNumericTime)
                    {
                        CompetitorEntry w = nativeDoc.Competitors[0];
                        report.AppendLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "      Winner: #{0} {1}, {2} (Lane {3}) -> Raw={4:0.0000}s, Official={5}",
                            w.BibId, w.LastName, w.FirstName, w.Lane, w.RawTimeSeconds,
                            w.GetFormattedOfficialTime(TimePrecision.Hundredths)));
                    }

                    if (evnPath.IndexOf("punto zero", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        evnPath.IndexOf("puntozero", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        report.AppendLine("      >>> punto zero verification:");
                        report.AppendLine("          Cameras count: " + nativeDoc.Cameras.Count);
                        for (int c = 0; c < nativeDoc.Cameras.Count; c++)
                        {
                            SlitCameraStream cam = nativeDoc.Cameras[c];
                            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                "          Cam {0}: IsVideo={1}, Res={2}x{3}, Frames={4}, Rate={5} Hz/fps",
                                c + 1, cam.IsVideo, cam.IsVideo ? cam.VideoFrameWidth : cam.TotalColumns,
                                cam.FrameHeightPx, cam.TotalVideoFrames, cam.SampleRateHz));
                        }
                        if (nativeDoc.Cameras.Count > 1 && nativeDoc.Cameras[1].IsVideo)
                        {
                            SlitCameraStream vCam = nativeDoc.Cameras[1];
                            using (Bitmap f0 = vCam.GetVideoFrame(0))
                            {
                                if (f0 != null)
                                {
                                    string pzFramePng = Path.Combine(sampleDir, "Native_PuntoZero_IdentiLynx_Frame0.png");
                                    f0.Save(pzFramePng, ImageFormat.Png);
                                    report.AppendLine("          Exported Frame 0 -> " + Path.GetFileName(pzFramePng) + " (" + f0.Width + "x" + f0.Height + " px)");
                                }
                            }
                            int fIdx;
                            using (Bitmap fWin = vCam.GetVideoFrameForTime(-0.0003, out fIdx))
                            {
                                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                    "          Synchronized Hairline Time -0.0003s -> Video Frame {0} (t = {1:0.000}s)",
                                    fIdx, vCam.VideoFrameTimestamps[fIdx]));
                            }
                        }
                        string pzStripPng = Path.Combine(sampleDir, "Native_PuntoZero_PhotoFinish.png");
                        using (Bitmap nativeBmp = SlitScanEngine.RenderAnnotatedExportBitmap(nativeDoc, 0))
                        {
                            nativeBmp.Save(pzStripPng, ImageFormat.Png);
                            report.AppendLine("          Exported Photo-Finish -> " + Path.GetFileName(pzStripPng));
                        }
                    }

                    if (evnPath.EndsWith("Sprint.evn", StringComparison.OrdinalIgnoreCase) && nativeDoc.Cameras.Count > 0)
                    {
                        string nativeSprintPng = Path.Combine(sampleDir, "Native_Lynx_Sprint_evn_PhotoFinish.png");
                        using (Bitmap nativeBmp = SlitScanEngine.RenderAnnotatedExportBitmap(nativeDoc, 0))
                        {
                            nativeBmp.Save(nativeSprintPng, ImageFormat.Png);
                        }
                    }
                    else if (evnPath.IndexOf("110 Meter Hurdles", StringComparison.OrdinalIgnoreCase) >= 0 && nativeDoc.Cameras.Count > 0)
                    {
                        string nativeHurdlesPng = Path.Combine(sampleDir, "Native_Lynx_110mHurdles_v3_PhotoFinish.png");
                        using (Bitmap nativeBmp = SlitScanEngine.RenderAnnotatedExportBitmap(nativeDoc, 0))
                        {
                            nativeBmp.Save(nativeHurdlesPng, ImageFormat.Png);
                        }
                    }
                }
            }

            // LynxConfigService & Autosave verification
            report.AppendLine();
            report.AppendLine("=== LynxConfigService & Path Management Verification ===");
            LynxConfigService cfg = LynxConfigService.Instance;
            report.AppendLine("  DataInDir (Database .evt/.ppl): " + cfg.DataInDir);
            report.AppendLine("  DataOutDir (Results .lif): " + cfg.DataOutDir);
            report.AppendLine("  EventDir (Events .evn/.i01): " + cfg.EventDir);
            report.AppendLine("  BackupDir (Autosave backup): " + cfg.BackupDir);
            report.AppendLine("  AutoSave: " + cfg.AutoSave + ", Interval: " + cfg.AutoSaveIntervalSeconds + "s");
            report.AppendLine("  LayoutSelection: " + cfg.LayoutSelection);

            using (RaceEventDocument testDoc = MeetDatabaseService.CreateSample100mHurdlesWomen())
            {
                string autoReport;
                bool autoOk = cfg.PerformAutosave(testDoc, out autoReport);
                report.AppendLine("  Autosave Test Execution: Success=" + autoOk + " (" + autoReport + ")");
            }

                // Instantiate MainWindow and verify control hierarchy initializes cleanly
                using (MainWindow win = new MainWindow())
                {
                    win.CreateControl();
                    report.AppendLine("MainWindow initialized: Title='" + win.Text + "', Size=" + win.Size.Width + "x" + win.Size.Height);
                }

                File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                return 0;
            }
            catch (Exception ex)
            {
                report.AppendLine("ERROR IN SELF-TEST: " + ex.ToString());
                File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                return 1;
            }
        }
    }
}
