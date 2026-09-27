#!/usr/bin/env python3
"""Writes the package-manager manifests for a released version:

    python3 packaging/update-manifests.py 2.1.0

- bucket/sentripet.json        Scoop  (scoop bucket add sentripet https://github.com/daven55663/SentriPet-AI)
- Casks/sentripet.rb           Homebrew (brew tap daven55663/sentripet https://github.com/daven55663/SentriPet-AI)
- packaging/winget/*.yaml      winget (submitted to microsoft/winget-pkgs by hand)

The SHA-256 of each package comes from the published GitHub release (gh CLI), so run it after the release is out;
the Release workflow does this and commits the result.
"""
import hashlib
import json
import os
import subprocess
import sys
import urllib.request

REPO = 'daven55663/SentriPet-AI'
HOME = 'https://github.com/' + REPO
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WINGET_ID = 'daven55663.SentriPet'
SUMMARY = 'A desktop pet that shows how much of your AI plan is left (Claude, Codex, Copilot…) and nudges you to use it before it resets.'


def release(version):
    out = subprocess.run(['gh', 'api', 'repos/%s/releases/tags/v%s' % (REPO, version)],
                         capture_output=True, text=True, encoding='utf-8', check=True).stdout
    rel = json.loads(out)
    sha = {}
    for a in rel['assets']:
        digest = a.get('digest') or ''
        if digest.startswith('sha256:'):
            sha[a['name']] = digest[7:]
        else:   # older releases have no digest: hash the download
            with urllib.request.urlopen(a['browser_download_url']) as r:
                sha[a['name']] = hashlib.sha256(r.read()).hexdigest()
    return sha, (rel.get('published_at') or '')[:10]


def url(version, rid, ext):
    return '%s/releases/download/v%s/SentriPet-%s-%s.%s' % (HOME, version, version, rid, ext)


def write(path, text):
    path = os.path.join(ROOT, path)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    print('wrote', os.path.relpath(path, ROOT))


def scoop(version, sha):
    m = {
        'version': version,
        'description': SUMMARY,
        'homepage': HOME,
        'license': 'MIT',
        'architecture': {'64bit': {'url': url(version, 'win-x64', 'zip'), 'hash': sha['SentriPet-%s-win-x64.zip' % version]}},
        # no shim: a GUI program started through a shim closes with the terminal; SentriPet adds its own
        # Start menu shortcut (needed for its notifications) the first time it runs
        'post_install': ['Start-Process "$dir\\SentriPet.exe"'],
        'notes': [
            'SentriPet is running: it adds itself to the Start menu and starts when you sign in (turn that off in its right-click menu).',
            'Quit it (right-click > Quit) before "scoop update sentripet" or "scoop uninstall sentripet".',
        ],
        'checkver': 'github',
        'autoupdate': {'architecture': {'64bit': {'url': url('$version', 'win-x64', 'zip')}}},
    }
    write('bucket/sentripet.json', json.dumps(m, indent=4, ensure_ascii=False) + '\n')


def cask(version, sha):
    write('Casks/sentripet.rb', '''cask "sentripet" do
  arch arm: "arm64", intel: "x64"

  version "%(v)s"
  sha256 arm:   "%(arm)s",
         intel: "%(intel)s"

  url "%(home)s/releases/download/v#{version}/SentriPet-#{version}-osx-#{arch}.zip"
  name "SentriPet"
  desc "Desktop pet that shows how much of your AI plan is left"
  homepage "%(home)s"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on macos: ">= :monterey"

  app "SentriPet.app"

  # SentriPet is not signed with an Apple Developer ID (it is signed ad hoc), so macOS would refuse to open it
  # after the download; installing it through this tap is the approval
  postflight do
    system_command "/usr/bin/xattr", args: ["-dr", "com.apple.quarantine", "#{appdir}/SentriPet.app"]
  end

  uninstall quit: "com.sentripet.app"

  zap trash: [
    "~/Library/Application Support/SentriPet",
    "~/Library/LaunchAgents/com.sentripet.app.plist",
  ]
end
''' % {'v': version, 'home': HOME,
       'arm': sha['SentriPet-%s-osx-arm64.zip' % version], 'intel': sha['SentriPet-%s-osx-x64.zip' % version]})


def winget(version, sha, date):
    head = '# yaml-language-server: $schema=https://aka.ms/winget-manifest.%s.1.10.0.schema.json\n\n'
    base = 'PackageIdentifier: %s\nPackageVersion: %s\n' % (WINGET_ID, version)
    tail = 'ManifestVersion: 1.10.0\n'
    d = 'packaging/winget/'
    write(d + WINGET_ID + '.yaml', head % 'version' + base + 'DefaultLocale: en-US\nManifestType: version\n' + tail)
    write(d + WINGET_ID + '.installer.yaml', head % 'installer' + base + '''InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
- RelativeFilePath: SentriPet.exe
  PortableCommandAlias: sentripet
MinimumOSVersion: 10.0.17763.0
ReleaseDate: %s
Installers:
- Architecture: x64
  InstallerUrl: %s
  InstallerSha256: %s
ManifestType: installer
''' % (date, url(version, 'win-x64', 'zip'), sha['SentriPet-%s-win-x64.zip' % version].upper()) + tail)
    write(d + WINGET_ID + '.locale.en-US.yaml', head % 'defaultLocale' + base + '''PackageLocale: en-US
Publisher: daven55663
PublisherUrl: https://github.com/daven55663
PublisherSupportUrl: %(home)s/issues
PackageName: SentriPet
PackageUrl: %(home)s
License: MIT
LicenseUrl: %(home)s/blob/main/LICENSE
ShortDescription: %(summary)s
Description: |-
  SentriPet sits on your desktop and shows the quota left on your AI plans: Claude, Codex, Copilot, Ollama,
  and anything else through JSON plugins. It reads the numbers the tools keep on your computer (never your
  sign-in credentials), warns you when a quota runs low, and nudges you when a weekly quota is about to reset
  with plenty left. Eight looks, five languages (English, 繁體中文, 简体中文, 日本語, 한국어).
Moniker: sentripet
Tags:
- ai
- claude
- codex
- copilot
- desktop-pet
- usage
- widget
ReleaseNotesUrl: %(home)s/releases/tag/v%(v)s
ManifestType: defaultLocale
''' % {'home': HOME, 'summary': SUMMARY, 'v': version} + tail)
    write(d + WINGET_ID + '.locale.zh-TW.yaml', head % 'locale' + base + '''PackageLocale: zh-TW
ShortDescription: 放在桌面上的 AI 用量桌寵：Claude、Codex、Copilot 還剩多少額度一眼就知道，額度快重置還沒用完時會提醒你。
Description: |-
  SentriPet 放在桌面上，顯示各個 AI 方案還剩多少額度：Claude、Codex、Copilot、Ollama，也能用 JSON 外掛接上其他服務。
  只讀取這些工具存在本機的用量數字（不讀取登入憑證）；額度快用完時提醒，每週額度快重置卻還剩很多時催你用掉。
  8 種造型，支援繁體中文、简体中文、English、日本語、한국어。
ManifestType: locale
''' + tail)


def main():
    if len(sys.argv) != 2:
        sys.exit('usage: update-manifests.py <version>')
    version = sys.argv[1].lstrip('v')
    sha, date = release(version)
    scoop(version, sha)
    cask(version, sha)
    winget(version, sha, date)


if __name__ == '__main__':
    main()
