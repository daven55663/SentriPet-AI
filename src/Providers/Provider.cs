using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace SentriPet
{
    /// <summary>
    /// A source of usage data for one AI tool. Detect() must be cheap; Fetch() runs on a worker thread.
    /// </summary>
    abstract class Provider : IDisposable
    {
        public string Id;
        public string Name;
        public string Mascot = "antenna";   // sparkle | prompt | goggles | llama | antenna | cat
        public Color Color = Colors.SlateGray;
        public bool BuiltIn = true;
        public string Origin;               // plugin file for custom providers

        public virtual int IntervalSeconds { get { return 30; } }

        public abstract Detection Detect();

        /// <param name="force">true when the user pressed "refresh now".</param>
        public abstract Snapshot Fetch(bool force, AppSettings settings);

        public virtual void Dispose() { }
    }

    /// <summary>Watches a folder for writes so the pet can show "working" while an AI is busy.</summary>
    class ActivityWatcher : IDisposable
    {
        readonly string dir;
        readonly string filter;
        FileSystemWatcher watcher;
        DateTime lastTry = DateTime.MinValue;
        public DateTime LastChangeUtc = DateTime.MinValue;
        public event Action<string> Changed;

        public ActivityWatcher(string dir, string filter)
        {
            this.dir = dir;
            this.filter = filter;
            Ensure();
        }

        /// <summary>(Re)creates the watcher if the folder appeared later or the watcher died.</summary>
        public void Ensure()
        {
            if (watcher != null) return;
            if ((DateTime.UtcNow - lastTry).TotalSeconds < 60) return;
            lastTry = DateTime.UtcNow;
            try
            {
                if (!Directory.Exists(dir)) return;
                var w = new FileSystemWatcher(dir, filter);
                w.IncludeSubdirectories = true;
                w.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                w.InternalBufferSize = 32 * 1024;
                w.Changed += OnEvent;
                w.Created += OnEvent;
                w.Renamed += (s, e) => OnEvent(s, e);
                w.Error += (s, e) =>
                {
                    try { w.Dispose(); } catch { }
                    watcher = null;
                };
                w.EnableRaisingEvents = true;
                watcher = w;
            }
            catch (Exception ex)
            {
                Log.Warn("watcher failed for " + dir + ": " + ex.Message);
            }
        }

        void OnEvent(object sender, FileSystemEventArgs e)
        {
            LastChangeUtc = DateTime.UtcNow;
            var h = Changed;
            if (h != null) h(e.FullPath);
        }

        public bool ActiveWithin(int seconds)
        {
            return (DateTime.UtcNow - LastChangeUtc).TotalSeconds < seconds;
        }

        public void Dispose()
        {
            if (watcher != null) { try { watcher.Dispose(); } catch { } watcher = null; }
        }
    }

    /// <summary>AI tools we can recognise but have no usage source for (yet). Plugins can add one.</summary>
    class CatalogEntry
    {
        public string Id;
        public string Name;
        public string[] Paths = new string[0];
        public string[] Commands = new string[0];
        public string[] EditorExtensions = new string[0];
        public string[] MsixPackages = new string[0];

        public Detection Detect()
        {
            var d = new Detection { UsageSupported = false };
            foreach (var p in Paths) if (AppPaths.Glob(p).Count > 0) { d.Evidence.Add("資料夾 " + AppPaths.ShortPath(AppPaths.Expand(p))); break; }
            foreach (var c in Commands) if (AppPaths.Which(c) != null) { d.Evidence.Add(c + " 指令"); break; }
            foreach (var e in EditorExtensions) if (AppPaths.EditorExtensions(e).Count > 0) { d.Evidence.Add("編輯器擴充"); break; }
            foreach (var m in MsixPackages) if (AppPaths.MsixInstalled(m)) { d.Evidence.Add("已安裝 App"); break; }
            d.Installed = d.Evidence.Count > 0;
            d.Hint = "目前沒有公開的用量來源，可在外掛資料夾放一個 JSON 設定檔接上";
            return d;
        }

        public static readonly List<CatalogEntry> All = new List<CatalogEntry>
        {
            new CatalogEntry { Id = "gemini", Name = "Gemini CLI", Paths = new[] { "~/.gemini" }, Commands = new[] { "gemini" } },
            new CatalogEntry { Id = "chatgpt", Name = "ChatGPT 桌面版", MsixPackages = new[] { "OpenAI.ChatGPT-Desktop", "OpenAI.ChatGPT" } },
            new CatalogEntry { Id = "cursor", Name = "Cursor", Paths = new[] { "%APPDATA%/Cursor", "%LOCALAPPDATA%/Programs/cursor" } },
            new CatalogEntry { Id = "windsurf", Name = "Windsurf", Paths = new[] { "%APPDATA%/Windsurf", "~/.codeium/windsurf" } },
            new CatalogEntry { Id = "kiro", Name = "Kiro", Paths = new[] { "~/.kiro", "%APPDATA%/Kiro" } },
            new CatalogEntry { Id = "trae", Name = "Trae", Paths = new[] { "%APPDATA%/Trae" } },
            new CatalogEntry { Id = "qwen", Name = "Qwen Code", Paths = new[] { "~/.qwen" }, Commands = new[] { "qwen" } },
            new CatalogEntry { Id = "amp", Name = "Amp", Paths = new[] { "~/.config/amp" }, Commands = new[] { "amp" } },
            new CatalogEntry { Id = "aider", Name = "Aider", Paths = new[] { "~/.aider*" }, Commands = new[] { "aider" } },
            new CatalogEntry { Id = "continue", Name = "Continue", Paths = new[] { "~/.continue" }, EditorExtensions = new[] { "continue.continue" } },
            new CatalogEntry { Id = "cline", Name = "Cline", EditorExtensions = new[] { "saoudrizwan.claude-dev" } },
            new CatalogEntry { Id = "roo", Name = "Roo Code", EditorExtensions = new[] { "rooveterinaryinc.roo-cline" } },
            new CatalogEntry { Id = "kilo", Name = "Kilo Code", EditorExtensions = new[] { "kilocode.kilo-code" } },
            new CatalogEntry { Id = "lmstudio", Name = "LM Studio", Paths = new[] { "~/.lmstudio", "%LOCALAPPDATA%/Programs/LM Studio" } },
            new CatalogEntry { Id = "perplexity", Name = "Perplexity", MsixPackages = new[] { "AI.Perplexity.Perplexity" }, Paths = new[] { "%LOCALAPPDATA%/Programs/perplexity" } },
            new CatalogEntry { Id = "zed", Name = "Zed", Paths = new[] { "%LOCALAPPDATA%/Zed" } },
        };
    }

    static class ProviderRegistry
    {
        public static List<Provider> CreateAll()
        {
            var list = new List<Provider>
            {
                new ClaudeProvider(),
                new CodexProvider(),
                new CopilotProvider(),
                new OllamaProvider(),
            };
            foreach (var cp in CustomProvider.LoadAll())
            {
                list.RemoveAll(p => p.Id == cp.Id);   // a plugin may override a built-in
                list.Add(cp);
            }
            return list;
        }
    }
}
