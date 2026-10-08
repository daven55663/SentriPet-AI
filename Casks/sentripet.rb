cask "sentripet" do
  arch arm: "arm64", intel: "x64"

  version "2.5.0"
  sha256 arm:   "56d7fb62fcea6c17382c5fe2da0a0f2dc25059e3719a71231738b3d04adb3730",
         intel: "707b34c7a43a71c49cba5e3043a2e09fd81c267dbe4cfca7487bffe6e61102fd"

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
