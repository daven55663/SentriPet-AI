cask "sentripet" do
  arch arm: "arm64", intel: "x64"

  version "2.5.1"
  sha256 arm:   "29343c9ab6c291582e333bba65ba6407a85b0095fadf9fd07735c234bccd4dd6",
         intel: "66872858b961dcfaf1d61c4f0b52e9db879c13c8bdee381e7af4363f003a02e1"

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
