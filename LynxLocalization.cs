using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace OptiFinish.Core
{
    /// <summary>
    /// Embedded multilingual localization manager powered by the extracted FinishLynx 17-language
    /// LangPack.dat resource plus optional external .lng override files.
    /// </summary>
    public static class Loc
    {
        private static readonly object SyncLock = new object();
        private static readonly List<string> _languages = new List<string>();
        private static readonly Dictionary<string, Dictionary<string, string>> _packs =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, string> _englishBase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _activePack = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;
        private static string _currentLanguage = "English (US)";

        public static event EventHandler LanguageChanged;

        static Loc()
        {
            EnsureLoaded();
        }

        public static string CurrentLanguage
        {
            get { EnsureLoaded(); return _currentLanguage; }
        }

        public static IList<string> AvailableLanguages
        {
            get
            {
                EnsureLoaded();
                return _languages.AsReadOnly();
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (SyncLock)
            {
                if (_loaded) return;
                _loaded = true;
                try
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    using (Stream st = asm.GetManifestResourceStream("OptiFinish.LangPack.dat"))
                    {
                        if (st != null)
                        {
                            using (StreamReader sr = new StreamReader(st, Encoding.UTF8))
                            {
                                ParseLangPack(sr);
                            }
                        }
                    }
                }
                catch { }

                if (_packs.Count == 0)
                {
                    string diskPack = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LangPack.dat");
                    if (File.Exists(diskPack))
                    {
                        try
                        {
                            using (StreamReader sr = new StreamReader(diskPack, Encoding.UTF8))
                            {
                                ParseLangPack(sr);
                            }
                        }
                        catch { }
                    }
                }

                LoadExternalLngFolder(AppDomain.CurrentDomain.BaseDirectory);
                LoadExternalLngFolder(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages"));

                Dictionary<string, string> en;
                if (_packs.TryGetValue("English (US)", out en)) _englishBase = en;
                SetLanguage(LynxConfigService.Instance.Language);
            }
        }

        private static void ParseLangPack(TextReader reader)
        {
            string line;
            string currentSection = null;
            Dictionary<string, string> currentDict = null;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    if (!_packs.TryGetValue(currentSection, out currentDict))
                    {
                        currentDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        _packs[currentSection] = currentDict;
                        if (!_languages.Contains(currentSection)) _languages.Add(currentSection);
                    }
                    continue;
                }
                if (currentDict == null) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq);
                string v = line.Substring(eq + 1);
                currentDict[k] = v;
            }
        }

        private static void LoadExternalLngFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            try
            {
                string[] files = Directory.GetFiles(dir, "*.lng");
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        string[] lines = File.ReadAllLines(files[i], Encoding.Default);
                        if (lines.Length == 0) continue;
                        string header = lines[0].Trim();
                        string langName = !string.IsNullOrEmpty(header)
                            ? header.Split(',')[0].Trim()
                            : Path.GetFileNameWithoutExtension(files[i]);
                        if (string.IsNullOrEmpty(langName)) continue;

                        Dictionary<string, string> dict;
                        if (!_packs.TryGetValue(langName, out dict))
                        {
                            dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            _packs[langName] = dict;
                            _languages.Add(langName);
                        }
                        for (int l = 1; l < lines.Length; l++)
                        {
                            string ln = lines[l];
                            if (!ln.StartsWith("Strings\\")) continue;
                            int eq = ln.IndexOf('=');
                            if (eq <= 0) continue;
                            string key = ln.Substring(0, eq).Split(':')[0];
                            string val = ln.Substring(eq + 1).Replace("FinishLynx", "OptiFinish");
                            if (key.StartsWith("Strings\\MenuBar\\") && val.Contains(","))
                            {
                                int lc = val.LastIndexOf(',');
                                if (val.Length - lc <= 5) val = val.Substring(0, lc);
                            }
                            if (!string.IsNullOrEmpty(val)) dict[key] = val;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static void SetLanguage(string languageName)
        {
            EnsureLoaded();
            string resolved = ResolveLanguageName(languageName);
            _currentLanguage = resolved;
            Dictionary<string, string> p;
            _activePack = _packs.TryGetValue(resolved, out p) ? p : _englishBase;
            if (LanguageChanged != null) LanguageChanged(null, EventArgs.Empty);
        }

        private static string ResolveLanguageName(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested)) return "English (US)";
            if (_packs.ContainsKey(requested)) return requested;

            string low = requested.Trim().ToLowerInvariant();
            for (int i = 0; i < _languages.Count; i++)
            {
                if (_languages[i].ToLowerInvariant().StartsWith(low)) return _languages[i];
            }
            if (low.Contains("ital")) return FindExisting("Italiano (IT)");
            if (low.Contains("deut") || low.Contains("germ")) return FindExisting("Deutsch (DE)");
            if (low.Contains("fran") || low.Contains("fren")) return FindExisting("Français (FR)");
            if (low.Contains("espa") || low.Contains("span")) return FindExisting("Español (ES)");
            if (low.Contains("port")) return FindExisting("Português (BR)");
            if (low.Contains("pol")) return FindExisting("Polski (PL)");
            if (low.Contains("sven") || low.Contains("swed")) return FindExisting("Svenska (SE)");
            if (low.Contains("suom") || low.Contains("finn")) return FindExisting("Suomi (FI)");
            if (low.Contains("magy") || low.Contains("hung")) return FindExisting("Magyar (HU)");
            if (low.Contains("russ") || low.Contains("русс")) return FindExisting("Русский (RU)");
            if (low.Contains("arab")) return FindExisting("العربية (SA)");
            if (low.Contains("kor")) return FindExisting("한국어 (KR)");
            if (low.Contains("trad")) return FindExisting("中文 (繁體)");
            if (low.Contains("zh") || low.Contains("chin")) return FindExisting("中文 (简体)");
            if (low.Contains("verilynx")) return FindExisting("English (VeriLynx)");
            return FindExisting("English (US)");
        }

        private static string FindExisting(string candidate)
        {
            return _packs.ContainsKey(candidate) ? candidate : (_languages.Count > 0 ? _languages[0] : "English (US)");
        }

        public static string Get(string fullKey, string fallback)
        {
            EnsureLoaded();
            string v;
            if (_activePack != null && _activePack.TryGetValue(fullKey, out v) && !string.IsNullOrEmpty(v)) return v;
            if (_englishBase != null && _englishBase.TryGetValue(fullKey, out v) && !string.IsNullOrEmpty(v)) return v;
            return fallback;
        }

        public static string GetClean(string fullKey, string fallback)
        {
            return Get(fullKey, fallback).Replace("&", "");
        }

        public static string Menu(string subKey, string fallback)
        {
            return Get("Strings\\MenuBar\\" + subKey, fallback);
        }

        public static string Opt(string subKey, string fallback)
        {
            return Get("Strings\\Options\\" + subKey, fallback);
        }

        public static string Evt(string subKey, string fallback)
        {
            return Get("Strings\\Event\\" + subKey, fallback);
        }

        public static string Hw(string subKey, string fallback)
        {
            return Get("Strings\\Hardware\\" + subKey, fallback);
        }

        public static string Dlg(string subKey, string fallback)
        {
            return Get("Strings\\Dialog\\" + subKey, fallback);
        }

        public static string FieldDisplayName(string fieldKey, string fallback)
        {
            switch (fieldKey)
            {
                case "Place": return Evt("Results\\Fields\\Place", fallback);
                case "Id": return Evt("Results\\Fields\\Id", fallback);
                case "Lane": return Evt("Results\\Fields\\Lane", fallback);
                case "FirstName": return Evt("Results\\Fields\\FirstName", fallback);
                case "LastName": return Evt("Results\\Fields\\LastName", fallback);
                case "Affiliation": return Evt("Results\\Fields\\Affiliation", fallback);
                case "License": return Evt("Results\\Fields\\License", fallback);
                case "Time": return Evt("Results\\Fields\\Time", fallback);
                case "Delta": return Evt("Results\\Fields\\DeltaTime", fallback);
                case "ReacTime": return Evt("Results\\Fields\\ReacTime", fallback);
                case "CumSplit": return Evt("Results\\Fields\\CumSplitTime", fallback);
                case "LastSplit": return Evt("Results\\Fields\\LastSplitTime", fallback);
                case "BestSplit": return Evt("Results\\Fields\\BestSplitTime", fallback);
                case "Laps": return Evt("Results\\Fields\\LapsToGo", fallback);
                case "Speed": return Evt("Results\\Fields\\Speed", fallback);
                case "Pace": return Evt("Results\\Fields\\Pace", fallback);
                case "User1": return Evt("Results\\Fields\\User1", fallback);
                case "User2": return Evt("Results\\Fields\\User2", fallback);
                case "User3": return Evt("Results\\Fields\\User3", fallback);
                case "Delta2": return Evt("Results\\Fields\\DeltaTime2", fallback);
                case "Delta3": return Evt("Results\\Fields\\DeltaTime3", fallback);
                default: return fallback;
            }
        }
    }
}
