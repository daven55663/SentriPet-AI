cask "sentripet" do
  arch arm: "arm64", intel: "x64"

  version "2.7.0"
  sha256 arm:   "8b53a6b4ec4920552c3d257e75d49dd593a4dec16331604f938068efcfa89910",
         intel: "e856ed3799fae8d951df25c79a10b2eb04a59719b2bebf00e4e2d5b4d12fcc08"

  url "https://github.com/daven55663/SentriPet-AI/releases/download/v#{version}/SentriPet-#{version}-osx-#{arch}.zip"
  name "SentriPet"
  desc "Desktop pet that shows how much of your AI plan is left"
  homepage "https://github.com/daven55663/SentriPet-AI"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on macos: :monterey

  app "SentriPet.app"

  # SentriPet is not signed with an Apple Developer ID (it is signed ad hoc), so macOS would refuse to open it
  # after the download; installing it through this tap is the approval (no quarantine flag to remove: fine too)
  postflight_steps do
    run "/usr/bin/xattr", args: ["-dr", "com.apple.quarantine", "{{appdir}}/SentriPet.app"], must_succeed: false
  end

  uninstall quit: "com.sentripet.app"

  zap trash: [
    "~/Library/Application Support/SentriPet",
    "~/Library/LaunchAgents/com.sentripet.app.plist",
  ]
end
