using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace SentriPet
{
    static class Net
    {
        /// <summary>Simple blocking HTTP request. Throws on network errors; returns body text and status.</summary>
        public static string Request(string method, string url, IDictionary<string, string> headers, string body, int timeoutMs, out int status)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = string.IsNullOrEmpty(method) ? "GET" : method.ToUpperInvariant();
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = AppInfo.Name + "/" + AppInfo.Version;
            req.Accept = "application/json";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (headers != null)
            {
                foreach (var kv in headers)
                {
                    string k = kv.Key, v = kv.Value ?? "";
                    if (string.Equals(k, "User-Agent", StringComparison.OrdinalIgnoreCase)) req.UserAgent = v;
                    else if (string.Equals(k, "Accept", StringComparison.OrdinalIgnoreCase)) req.Accept = v;
                    else if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase)) req.ContentType = v;
                    else req.Headers[k] = v;
                }
            }
            if (body != null && req.Method != "GET")
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                if (req.ContentType == null) req.ContentType = "application/json";
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            }
            HttpWebResponse resp;
            try { resp = (HttpWebResponse)req.GetResponse(); }
            catch (WebException wex)
            {
                resp = wex.Response as HttpWebResponse;
                if (resp == null) throw;
            }
            using (resp)
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                status = (int)resp.StatusCode;
                return sr.ReadToEnd();
            }
        }

        /// <summary>
        /// Runs a command and returns stdout. Kills it after the timeout. Scripts go through the system shell:
        /// cmd.exe for .cmd/.bat on Windows, /bin/sh for .sh (or "shell": true) on macOS and Linux, PowerShell for .ps1.
        /// </summary>
        public static string RunCommand(string file, IList<string> args, bool viaShell, int timeoutMs, out int exitCode, out string stderr)
        {
            var psi = new ProcessStartInfo();
            args = args ?? new string[0];
            string ext = (Path.GetExtension(file) ?? "").ToLowerInvariant();
            if (Os.Windows && (viaShell || ext == ".cmd" || ext == ".bat"))
            {
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.Arguments = "/d /s /c \"" + Quote(file) + (args.Count > 0 ? " " + JoinQuoted(args) : "") + "\"";
            }
            else if (!Os.Windows && viaShell)
            {
                // one shell command line: the program and its arguments, quoted for sh
                var line = new StringBuilder(ShQuote(file));
                foreach (var a in args) line.Append(' ').Append(ShQuote(a));
                SetArgs(psi, "/bin/sh", new[] { "-c", line.ToString() });
            }
            else if (!Os.Windows && ext == ".sh")
            {
                var all = new List<string> { file };
                all.AddRange(args);
                SetArgs(psi, "/bin/sh", all);
            }
            else if (ext == ".ps1")
            {
                var all = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", file };
                all.AddRange(args);
                SetArgs(psi, Os.Windows ? "powershell.exe" : "pwsh", all);
            }
            else
            {
                SetArgs(psi, file, args);
            }
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            var outSb = new StringBuilder();
            var errSb = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                var outDone = new ManualResetEvent(false);
                var errDone = new ManualResetEvent(false);
                p.OutputDataReceived += (s, e) => { if (e.Data == null) outDone.Set(); else outSb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data == null) errDone.Set(); else errSb.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException(L.T("指令執行逾時"));
                }
                outDone.WaitOne(2000);
                errDone.WaitOne(2000);
                exitCode = p.ExitCode;
            }
            stderr = errSb.ToString();
            return outSb.ToString();
        }

        /// <summary>Program and arguments, passed exactly (ArgumentList on .NET 10, Windows quoting on .NET Framework).</summary>
        static void SetArgs(ProcessStartInfo psi, string file, IList<string> args)
        {
            psi.FileName = file;
#if NET
            foreach (var a in args) psi.ArgumentList.Add(a);
#else
            psi.Arguments = JoinQuoted(args);
#endif
        }

        static string JoinQuoted(IList<string> args)
        {
            var sb = new StringBuilder();
            foreach (var a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Quote(a));
            }
            return sb.ToString();
        }

        static string Quote(string a)
        {
            if (string.IsNullOrEmpty(a)) return "\"\"";
            if (a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            return "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>'single quotes' for /bin/sh (a quote inside becomes '\'').</summary>
        static string ShQuote(string a)
        {
            if (!string.IsNullOrEmpty(a) && a.IndexOfAny(" \t\n'\"\\$`;&|<>()*?[]{}!#~".ToCharArray()) < 0) return a;
            return "'" + (a ?? "").Replace("'", "'\\''") + "'";
        }
    }
}
