cask "sentripet" do
  arch arm: "arm64", intel: "x64"

  version "2.7.1"
  sha256 arm:   "1139e4d3897110aa222a0e43afb92bc55100901c3f8e74d57b7e3124c89d0689",
         intel: "1f19eaa40bad6a3dfdbb873cf430ae4130ff14dfbe1ca0fd417420faee6ceaec"

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
