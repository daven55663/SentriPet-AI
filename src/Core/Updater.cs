using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentriPet
{
    /// <summary>A published release, as GitHub's releases/latest describes it.</summary>
    class Release
    {
        public string Tag, Version, Name, Notes, Page;
        public DateTime? Published;
        public readonly List<ReleaseAsset> Assets = new List<ReleaseAsset>();
    }

    /// <summary>A file of a release, with the SHA-256 GitHub computed when it was uploaded.</summary>
    class ReleaseAsset
    {
        public string Name, Url, Sha256;
        public long Size;
    }

    /// <summary>How this copy was installed — and so how it is brought up to date.</summary>
    enum InstallKind
    {
        /// <summary>install.cmd's folder or an unzipped copy on Windows: SentriPet replaces its own files.</summary>
        SelfUpdate,
        Scoop,
        Winget,
        Homebrew,
        /// <summary>A copy installed by hand on macOS or Linux: the release page is opened.</summary>
        Download,
        /// <summary>A build from the source (bin/Release): never replaced.</summary>
        Development,
    }

    class UpdateException : Exception
    {
        public UpdateException(string message) : base(message) { }
    }

    /// <summary>
    /// New versions (#39): once a day SentriPet asks GitHub for the latest release (only that request — no usage data,
    /// nothing about the user) and, when it is newer, offers it. A copy installed by install.cmd or unzipped on Windows
    /// downloads the release's zip, checks its size and its SHA-256 against what GitHub published, replaces its own files
    /// (the running program is renamed first, which Windows allows) and starts again; Scoop, winget and Homebrew copies
    /// let their package manager do it; anything else opens the release page.
    /// </summary>
    static class Updater
    {
        public const string Repository = "daven55663/SentriPet-AI";
        public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";
        public const string WingetId = "daven55663.SentriPet";
        /// <summary>How often to look.</summary>
        public static readonly TimeSpan Every = TimeSpan.FromHours(24);

        public static string ExeName { get { return Os.Windows ? "SentriPet.exe" : "SentriPet"; } }

        /// <summary>The folder downloads are unpacked in (removed at the next start).</summary>
        public static string WorkDir { get { return Path.Combine(Path.GetTempPath(), "SentriPet-update"); } }

        // ------------------------------------------------------------------ is there a newer one?

        /// <summary>"v2.7.0", "2.7", "2.7.0-beta" → 2.7.0.0; null when it isn't a version.</summary>
        public static Version ParseVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) s = s.Substring(0, cut);
            if (s.IndexOf('.') < 0) s += ".0";
            Version v;
            if (!Version.TryParse(s, out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        public static bool IsNewer(string candidate, string current)
        {
            var a = ParseVersion(candidate);
            var b = ParseVersion(current);
            return a != null && b != null && a > b;
        }

        /// <summary>Time for the daily look (also when the clock was turned back).</summary>
        public static bool Due(AppSettings s, DateTime nowUtc)
        {
            if (!s.CheckUpdates) return false;
            if (!s.LastUpdateCheck.HasValue) return true;
            var last = s.LastUpdateCheck.Value;
            return nowUtc - last >= Every || last > nowUtc + TimeSpan.FromMinutes(5);
        }

        /// <summary>Whether to show a release: newer than this copy and — unless asked for — not skipped.</summary>
        public static bool Offer(Release r, AppSettings s, bool asked)
        {
            if (r == null || !IsNewer(r.Version, AppInfo.Version)) return false;
            return asked || !string.Equals(r.Version, s.SkippedVersion, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The JSON of releases/latest; null when it isn't one.</summary>
        public static Release Parse(string json)
        {
            var o = Json.Obj(Json.TryParse(json ?? ""));
            if (o == null) return null;
            var r = new Release
            {
                Tag = Json.Str(Json.Get(o, "tag_name")),
                Name = Json.Str(Json.Get(o, "name")),
                Notes = Json.Str(Json.Get(o, "body")) ?? "",
                Page = Json.Str(Json.Get(o, "html_url")),
            };
            if (r.Tag == null || ParseVersion(r.Tag) == null) return null;
            if (Json.Bool(Json.Get(o, "draft")) == true || Json.Bool(Json.Get(o, "prerelease")) == true) return null;
            r.Version = r.Tag.TrimStart('v', 'V');
            if (r.Page == null || !r.Page.StartsWith("https://github.com/" + Repository + "/", StringComparison.OrdinalIgnoreCase))
                r.Page = "https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(r.Tag);
            DateTime published;
            if (DateTime.TryParse(Json.Str(Json.Get(o, "published_at")) ?? "", System.Globalization.CultureInfo.InvariantCulture,
                                  System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out published))
                r.Published = DateTime.SpecifyKind(published, DateTimeKind.Utc);
            var assets = Json.Arr(Json.Get(o, "assets"));
            if (assets != null)
                foreach (var x in assets)
                {
                    var a = Json.Obj(x);
                    if (a == null) continue;
                    string name = Json.Str(Json.Get(a, "name")), url = Json.Str(Json.Get(a, "browser_download_url")), digest = Json.Str(Json.Get(a, "digest"));
                    if (name == null || url == null) continue;
                    r.Assets.Add(new ReleaseAsset
                    {
                        Name = name,
                        Url = url,
                        Size = (long)(Json.Num(Json.Get(a, "size")) ?? 0),
                        Sha256 = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest.Substring(7).ToLowerInvariant() : null,
                    });
                }
            return r;
        }

        /// <summary>Asks GitHub for the latest release (blocking; throws when it can't be reached).</summary>
        public static Release Fetch(string url = null, int timeoutMs = 15000)
        {
            int status;
            var headers = new Dictionary<string, string> { { "Accept", "application/vnd.github+json" }, { "X-GitHub-Api-Version", "2022-11-28" } };
            string body = Net.Request("GET", url ?? LatestUrl, headers, null, timeoutMs, out status);
            if (status == 404) return null;   // no release yet
            if (status != 200) throw new UpdateException(L.F("GitHub 回應 HTTP {0}", status));
            var r = Parse(body);
            if (r == null) throw new UpdateException(L.T("GitHub 的回應看不懂"));
            return r;
        }

        /// <summary>The package for this system: win-x64, osx-arm64, osx-x64 or linux-x64.</summary>
        public static string Rid
        {
            get
            {
                if (Os.Windows) return "win-x64";
                if (Os.Mac) return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64";
                return "linux-x64";
            }
        }

        public static ReleaseAsset AssetFor(Release r, string rid)
        {
            if (r == null) return null;
            string ext = rid.StartsWith("linux") ? ".tar.gz" : ".zip";
            return r.Assets.FirstOrDefault(a => a.Name.Equals("SentriPet-" + r.Version + "-" + rid + ext, StringComparison.OrdinalIgnoreCase))
                ?? r.Assets.FirstOrDefault(a => a.Name.EndsWith("-" + rid + ext, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------ the release notes, readable

        /// <summary>
        /// What changed: the "changes" section of the release notes (up to the next heading) as plain text, or the
        /// whole notes when there is no such section.
        /// </summary>
        public static string Changes(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes)) return "";
            var lines = notes.Replace("\r\n", "\n").Split('\n');
            int from = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string h = lines[i].Replace("\u2060", "").Trim();   // (Korean text can carry word joiners)
                if (h.StartsWith("## ") && Regex.IsMatch(h, "變化|变化|變更|更新內容|What's new|Changes|変更|변경", RegexOptions.IgnoreCase)) { from = i + 1; break; }   // i18n-ignore
            }
            var keep = new List<string>();
            for (int i = Math.Max(0, from); i < lines.Length; i++)
            {
                if (from >= 0 && lines[i].TrimStart().StartsWith("## ")) break;
                keep.Add(lines[i]);
            }
            return Plain(string.Join("\n", keep)).Trim();
        }

        /// <summary>Markdown, read as text: no emphasis marks, links as their text, headings and list marks kept simple.</summary>
        public static string Plain(string md)
        {
            var sb = new StringBuilder();
            foreach (var raw in (md ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (Regex.IsMatch(line.Trim(), @"^\|?\s*:?-{2,}")) continue;   // a table's rule
                line = Regex.Replace(line, @"!\[[^\]]*\]\([^)]*\)", "");                       // pictures
                line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]*\)", "$1");                    // links
                line = Regex.Replace(line, @"(\*\*|__)(.+?)\1", "$2");
                line = Regex.Replace(line, @"`([^`]*)`", "$1");
                line = Regex.Replace(line, @"^\s*#{1,6}\s+", "");
                line = Regex.Replace(line, @"^(\s*)[-*]\s+", "$1• ");
                line = Regex.Replace(line, @"<[^>]+>", "");
                sb.Append(line).Append('\n');
            }
            return Regex.Replace(sb.ToString(), "\n{3,}", "\n\n");
        }

        // ------------------------------------------------------------------ how this copy is updated

        /// <summary>From where the program runs (and, on macOS, whether Homebrew installed it).</summary>
        public static InstallKind KindOf(string exe, bool windows, bool mac, Func<string, bool> folderExists)
        {
            if (string.IsNullOrEmpty(exe)) return InstallKind.Download;
            string p = exe.Replace('\\', '/').ToLowerInvariant();
            if (p.Contains("/bin/release/") || p.Contains("/bin/debug/") || p.Contains("/obj/")) return InstallKind.Development;
            if (windows)
            {
                if (p.Contains("/scoop/apps/sentripet/") || Regex.IsMatch(p, @"/apps/sentripet/(current|\d[^/]*)/sentripet\.exe$")) return InstallKind.Scoop;
                if (p.Contains("/microsoft/winget/packages/")) return InstallKind.Winget;
                return InstallKind.SelfUpdate;
            }
            if (mac && p.Contains(".app/contents/macos/") && folderExists != null &&
                (folderExists("/opt/homebrew/Caskroom/sentripet") || folderExists("/usr/local/Caskroom/sentripet")))
                return InstallKind.Homebrew;
            return InstallKind.Download;
        }

        public static InstallKind Kind { get { return KindOf(Environment.ProcessPath, Os.Windows, Os.Mac, Directory.Exists); } }

        /// <summary>Under Scoop the program may run from its version's folder; "current" is the one that moves on.</summary>
        public static string ScoopCurrent(string exe)
        {
            return Regex.Replace(exe, @"([\\/]apps[\\/]sentripet[\\/])[^\\/]+([\\/][^\\/]+)$", "$1current$2", RegexOptions.IgnoreCase);
        }

        // ------------------------------------------------------------------ download, check, unpack, replace

        /// <summary>Downloads a release file into a folder; progress(bytes so far, total).</summary>
        public static async Task<string> Download(ReleaseAsset a, string folder, Action<long, long> progress, CancellationToken cancel)
        {
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, Path.GetFileName(a.Name));
            string part = file + ".part";
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.Version);
                using (var resp = await http.GetAsync(a.Url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) throw new UpdateException(L.F("下載失敗（HTTP {0}）", (int)resp.StatusCode));
                    long total = resp.Content.Headers.ContentLength ?? a.Size;
                    using (var src = await resp.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
                    using (var dst = File.Create(part))
                    {
                        var buf = new byte[81920];
                        long got = 0;
                        int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, cancel).ConfigureAwait(false)) > 0)
                        {
                            dst.Write(buf, 0, n);
                            got += n;
                            if (a.Size > 0 && got > a.Size) throw new UpdateException(L.T("下載的檔案比 GitHub 公布的大，已經停止更新"));
                            if (progress != null) progress(got, total);
                        }
                    }
                }
            }
            File.Move(part, file, true);
            return file;
        }

        /// <summary>Null when the file is the one GitHub published (its size and SHA-256), otherwise what is wrong.</summary>
        public static string Problem(string file, ReleaseAsset a)
        {
            var info = new FileInfo(file);
            if (!info.Exists) return L.T("下載的檔案不見了");
            if (a.Size > 0 && info.Length != a.Size) return L.F("下載的檔案大小不對（{0} 位元組，應該是 {1}）", info.Length, a.Size);
            if (string.IsNullOrEmpty(a.Sha256)) return L.T("GitHub 沒有公布這個檔案的 SHA-256，無法確認它沒有被竄改");
            string hash;
            using (var s = File.OpenRead(file)) hash = Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
            return hash == a.Sha256 ? null : L.T("下載的檔案和 GitHub 公布的 SHA-256 不符，已經停止更新");
        }

        /// <summary>Unpacks the zip into folder/files; returns the folder that holds the program.</summary>
        public static string Unpack(string zip, string folder)
        {
            string to = Path.Combine(folder, "files");
            if (Directory.Exists(to)) Directory.Delete(to, true);
            ZipFile.ExtractToDirectory(zip, to);   // (refuses entries that point outside the folder)
            string exe = Directory.GetFiles(to, ExeName, SearchOption.AllDirectories).OrderBy(f => f.Length).FirstOrDefault();
            if (exe == null) throw new UpdateException(L.T("下載的檔案裡沒有 SentriPet 程式"));
            return Path.GetDirectoryName(exe);
        }

        /// <summary>
        /// Puts the new files where this copy is: the running program is renamed to .old first (Windows lets a running
        /// program be renamed, not overwritten), then every file is copied over. When something fails, the old program
        /// is put back.
        /// </summary>
        public static void Replace(string from, string exe)
        {
            string dir = Path.GetDirectoryName(exe), old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                {
                    string dest = Path.Combine(dir, Path.GetRelativePath(from, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(f, dest, true);
                }
                if (!File.Exists(exe)) throw new UpdateException(L.T("下載的檔案裡沒有 SentriPet 程式"));
            }
            catch
            {
                try
                {
                    if (File.Exists(exe)) File.Delete(exe);
                    File.Move(old, exe);
                }
                catch (Exception ex) { Log.Error("update: putting the old program back", ex); }
                throw;
            }
        }

        /// <summary>At start: removes what the last update left (the old program, the download).</summary>
        public static void CleanUp(string exe)
        {
            try { if (!string.IsNullOrEmpty(exe) && File.Exists(exe + ".old")) File.Delete(exe + ".old"); } catch { }
            try { if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, true); } catch { }
        }

        /// <summary>
        /// Downloads the release for this system, checks it, unpacks it and puts it where exe is; returns how to start
        /// the new program. step(what is happening, how far 0–1 or -1) is called from a worker thread.
        /// </summary>
        public static async Task<ProcessStartInfo> InstallOver(Release r, string exe, Action<string, double> step, CancellationToken cancel, params string[] extra)
        {
            var asset = AssetFor(r, Rid);
            if (asset == null) throw new UpdateException(L.T("這個版本沒有這台電腦用的安裝檔"));
            string folder = Path.Combine(WorkDir, r.Version);
            step(L.T("下載中…"), 0);
            string file = await Download(asset, folder, (got, total) =>
                step(L.F("下載中… {0} / {1} MB", (got / 1048576.0).ToString("0.0"), (total / 1048576.0).ToString("0.0")), total > 0 ? (double)got / total : 0), cancel).ConfigureAwait(false);
            step(L.T("確認檔案沒有被竄改…"), -1);
            string problem = Problem(file, asset);
            if (problem != null) throw new UpdateException(problem);
            cancel.ThrowIfCancellationRequested();
            step(L.T("安裝中…"), -1);
            Replace(Unpack(file, folder), exe);
            Log.Info("update: " + AppInfo.Version + " replaced by " + r.Version + " in " + Path.GetDirectoryName(exe));
            return Restart(exe, Environment.ProcessId, extra);
        }

        /// <summary>The new program, started after this one has quit (it waits for this process).</summary>
        public static ProcessStartInfo Restart(string exe, int pid, params string[] extra)
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
            psi.ArgumentList.Add("--updated-from");
            psi.ArgumentList.Add(AppInfo.Version);
            psi.ArgumentList.Add("--wait-pid");
            psi.ArgumentList.Add(pid.ToString());
            if (AppPaths.Dev) psi.ArgumentList.Add("--dev");
            if (extra != null) foreach (var a in extra) psi.ArgumentList.Add(a);
            return psi;
        }

        /// <summary>--wait-pid: the copy being replaced is still quitting.</summary>
        public static void WaitFor(string pid, int timeoutMs)
        {
            int id;
            if (!int.TryParse(pid, out id)) return;
            try
            {
                using (var p = Process.GetProcessById(id)) p.WaitForExit(timeoutMs);
            }
            catch (ArgumentException) { }   // already gone
            catch (Exception ex) { Log.Warn("wait for " + pid + ": " + ex.Message); }
        }

        // ------------------------------------------------------------------ package managers

        static string Ps(string s) { return "'" + (s ?? "").Replace("'", "''") + "'"; }
        static string Sh(string s) { return "'" + (s ?? "").Replace("'", "'\\''") + "'"; }

        /// <summary>
        /// The update through Scoop, winget or Homebrew, run after this copy has quit: Windows in a console window the
        /// user can watch (the package manager may ask something), Homebrew quietly into a log file. Afterwards the
        /// program is started again (the old one, when the update didn't happen).
        /// </summary>
        public static ProcessStartInfo PackageUpdate(InstallKind kind, int pid, string exe, string brew = null, string log = null)
        {
            if (kind == InstallKind.Scoop || kind == InstallKind.Winget)
            {
                string start = kind == InstallKind.Scoop ? ScoopCurrent(exe) : exe;
                var sb = new StringBuilder();
                sb.Append("$Host.UI.RawUI.WindowTitle = 'SentriPet update'; ");
                sb.Append("while (Get-Process -Id " + pid + " -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }; ");
                if (kind == InstallKind.Scoop) sb.Append("scoop update; scoop update sentripet; ");
                else sb.Append("winget upgrade --id " + WingetId + " --exact --source winget; ");
                sb.Append("if (-not (Get-Process -Name SentriPet -ErrorAction SilentlyContinue)) { Start-Process -FilePath " + Ps(start) + " }; ");
                sb.Append("Start-Sleep -Seconds 6");
                var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = false };
                foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", sb.ToString() }) psi.ArgumentList.Add(a);
                return psi;
            }
            if (kind == InstallKind.Homebrew)
            {
                // …/SentriPet.app/Contents/MacOS/SentriPet → …/SentriPet.app
                int end = exe.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
                string app = end >= 0 ? exe.Substring(0, end + 4) : Path.GetDirectoryName(exe);
                string script = "while kill -0 " + pid + " 2>/dev/null; do sleep 0.3; done; " +
                                Sh(brew) + " upgrade --cask sentripet >> " + Sh(log) + " 2>&1; " +
                                "/usr/bin/open " + Sh(app);
                var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(script);
                return psi;
            }
            return null;
        }

        /// <summary>Homebrew's program (Apple silicon or Intel), null when there is none.</summary>
        public static string Brew
        {
            get
            {
                foreach (var b in new[] { "/opt/homebrew/bin/brew", "/usr/local/bin/brew" }) if (File.Exists(b)) return b;
                return null;
            }
        }
    }
}
