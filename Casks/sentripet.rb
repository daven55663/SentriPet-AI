cask "sentripet" do
  arch arm: "arm64", intel: "x64"

  version "2.3.1"
  sha256 arm:   "b4f301a453b37a5efc311daa122d186afd457a1ca27b305123590bec5ed8010b",
         intel: "a74a20e84a89803e09102ae4c37cb23fa44b1297202503801970e7636aae8235"

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
