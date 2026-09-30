using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentriPet
{
    /// <summary>
    /// Another Claude Code or Codex account (#26): its own settings folder — Claude Code's CLAUDE_CONFIG_DIR or Codex's
    /// CODEX_HOME — shown as a pet of its own. SentriPet never reads the sign-in inside; Codex answers for the folder's
    /// account through its own app-server, Claude Code through the status line connected in that folder.
    /// </summary>
    class Account
    {
        public string Id;                // claude-2, codex-2… (the provider id)
        public string Kind;              // claude or codex
        public string Folder;            // as the user picked it; ~ is fine
        public string Name;
        public string Color;             // #RRGGBB, or null for the default of the kind
        public bool StatusLine;          // Claude: SentriPet is the status line in this folder's settings.json
        public string StatusLineChain;   // …and the user's own status line there (JSON), still shown

        public string Home { get { return AppPaths.Expand(Folder); } }

        public Dictionary<string, object> ToJson()
        {
            var o = new Dictionary<string, object> { { "id", Id }, { "kind", Kind }, { "folder", Folder }, { "name", Name } };
            if (Color != null) o["color"] = Color;
            if (StatusLine) o["statusLine"] = true;
            if (StatusLineChain != null) o["statusLineChain"] = StatusLineChain;
            return o;
        }

        public static Account FromJson(object x)
        {
            var o = Json.Obj(x);
            if (o == null) return null;
            var a = new Account
            {
                Id = Json.Str(Json.Get(o, "id")),
                Kind = Json.Str(Json.Get(o, "kind")),
                Folder = Json.Str(Json.Get(o, "folder")),
                Name = Json.Str(Json.Get(o, "name")),
                Color = Json.Str(Json.Get(o, "color")),
                StatusLine = Json.Bool(Json.Get(o, "statusLine")) ?? false,
                StatusLineChain = Json.Str(Json.Get(o, "statusLineChain")),
            };
            if (string.IsNullOrEmpty(a.Id) || (a.Kind != "claude" && a.Kind != "codex") || string.IsNullOrEmpty(a.Folder)) return null;
            if (string.IsNullOrEmpty(a.Name)) a.Name = a.Id;
            return a;
        }
    }

    static class AccountSetup
    {
        /// <summary>Colours to tell the accounts apart (the first ones are the defaults for the second, third… account).</summary>
        public static readonly string[] Colors = { "#E0A458", "#4FB5A5", "#A78BFA", "#F472B6", "#60A5FA", "#F87171", "#84CC16", "#94A3B8" };

        /// <summary>The folder the default account uses (the one SentriPet always shows).</summary>
        public static string DefaultFolder(string kind)
        {
            string env = Environment.GetEnvironmentVariable(kind == "claude" ? "CLAUDE_CONFIG_DIR" : "CODEX_HOME");
            return !string.IsNullOrEmpty(env) ? env : Path.Combine(AppPaths.Home, kind == "claude" ? ".claude" : ".codex");
        }

        static string Normal(string folder)
        {
            try { return Path.GetFullPath(AppPaths.Expand(folder)).TrimEnd('/', '\\'); }
            catch { return folder ?? ""; }
        }

        public static bool SameFolder(string a, string b)
        {
            return string.Equals(Normal(a), Normal(b), Os.Linux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Why this folder can't be added (in the current language), or null.</summary>
        public static string Problem(AppSettings s, string kind, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(AppPaths.Expand(folder))) return L.T("找不到這個資料夾");
            if (SameFolder(folder, DefaultFolder(kind))) return L.T("這是預設的資料夾，桌面上已經有這隻了");
            if (s.Accounts.Any(a => a.Kind == kind && SameFolder(a.Folder, folder))) return L.T("這個資料夾已經加過了");
            return null;
        }

        /// <summary>Adds an account: the next free id (claude-2, claude-3…), "Claude 2" and a colour of its own.</summary>
        public static Account Add(AppSettings s, string kind, string folder)
        {
            int n = 2;
            while (s.Accounts.Any(a => a.Id == kind + "-" + n)) n++;
            var used = s.Accounts.Select(a => a.Color).ToList();
            var a2 = new Account
            {
                Id = kind + "-" + n,
                Kind = kind,
                Folder = folder,
                Name = (kind == "claude" ? "Claude" : "Codex") + " " + n,
                Color = Colors.FirstOrDefault(c => !used.Contains(c)) ?? Colors[0],
            };
            s.Accounts.Add(a2);
            return a2;
        }

        public static Account Find(AppSettings s, string id) { return s.Accounts.FirstOrDefault(a => a.Id == id); }
    }
}
