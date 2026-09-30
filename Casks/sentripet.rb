cask "sentripet" do
  arch arm: "arm64", intel: "x64"

  version "2.4.0"
  sha256 arm:   "7a8fe100b6a4d37c3cf64efa4e293766f9ef725f28db484b73286f8b026bdb78",
         intel: "8abfac6fe1f98272c7b2b67377dd6c97dc47959a770f36932d9ca6c92e5214cc"

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
