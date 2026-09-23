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
            req.UserAgent = "SentriPet/" + App.Version;
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

        /// <summary>Runs a command and returns stdout. Kills it after the timeout.</summary>
        public static string RunCommand(string file, IList<string> args, bool viaShell, int timeoutMs, out int exitCode, out string stderr)
        {
            var psi = new ProcessStartInfo();
            var argText = new StringBuilder();
            foreach (var a in args ?? new string[0])
            {
                if (argText.Length > 0) argText.Append(' ');
                argText.Append(Quote(a));
            }
            if (viaShell || file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.Arguments = "/d /s /c \"" + Quote(file) + (argText.Length > 0 ? " " + argText : "") + "\"";
            }
            else if (file.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(file) + (argText.Length > 0 ? " " + argText : "");
            }
            else
            {
                psi.FileName = file;
                psi.Arguments = argText.ToString();
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
                    throw new TimeoutException("指令執行逾時");
                }
                outDone.WaitOne(2000);
                errDone.WaitOne(2000);
                exitCode = p.ExitCode;
            }
            stderr = errSb.ToString();
            return outSb.ToString();
        }

        static string Quote(string a)
        {
            if (string.IsNullOrEmpty(a)) return "\"\"";
            if (a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            return "\"" + a.Replace("\"", "\\\"") + "\"";
        }
    }
}
