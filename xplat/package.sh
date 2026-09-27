#!/usr/bin/env bash
# Builds a self-contained package of the cross-platform desk pet (no .NET needed on the target machine):
#   xplat/package.sh win-x64 | win-arm64 | osx-arm64 | osx-x64 | linux-x64 | linux-arm64
# Output in dist/: SentriPet-<version>-<rid>.zip (Windows, macOS .app) or .tar.gz (Linux).
# macOS packages must be built on a Mac (the .app is signed ad hoc with codesign).
set -euo pipefail

rid="${1:?usage: package.sh <runtime id>}"
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/dist"
ver="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/xplat/Directory.Build.props" | head -n 1)"
name="SentriPet-$ver-$rid"
pub="$out/publish-$rid"

rm -rf "$pub"
mkdir -p "$out"
dotnet publish "$root/xplat/SentriPet.Desktop/SentriPet.Desktop.csproj" -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "$pub"
cp -R "$root/examples" "$pub/examples"
cp "$root/LICENSE" "$pub/LICENSE.txt" 2>/dev/null || true

case "$rid" in
  win-*)
    rm -f "$out/$name.zip"
    if command -v 7z >/dev/null; then (cd "$pub" && 7z a -tzip -bso0 "$out/$name.zip" .)
    else
      src="$pub"; dst="$out/$name.zip"
      if command -v cygpath >/dev/null; then src="$(cygpath -w "$pub")"; dst="$(cygpath -w "$out/$name.zip")"; fi
      powershell -NoProfile -Command "Compress-Archive -Path '$src\\*' -DestinationPath '$dst' -Force"
    fi
    echo "$out/$name.zip"
    ;;

  osx-*)
    app="$out/$rid/SentriPet.app"
    rm -rf "$out/$rid"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp -R "$pub/." "$app/Contents/MacOS/"
    # icon: the 256 px PNG scaled into an iconset
    set_dir="$out/$rid/SentriPet.iconset"
    mkdir -p "$set_dir"
    for s in 16 32 128 256; do
      sips -z $s $s "$root/assets/app.png" --out "$set_dir/icon_${s}x${s}.png" >/dev/null
      sips -z $((s * 2)) $((s * 2)) "$root/assets/app.png" --out "$set_dir/icon_${s}x${s}@2x.png" >/dev/null
    done
    iconutil -c icns "$set_dir" -o "$app/Contents/Resources/SentriPet.icns"
    rm -rf "$set_dir"
    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>SentriPet</string>
  <key>CFBundleDisplayName</key><string>SentriPet</string>
  <key>CFBundleIdentifier</key><string>com.sentripet.app</string>
  <key>CFBundleVersion</key><string>$ver</string>
  <key>CFBundleShortVersionString</key><string>$ver</string>
  <key>CFBundleExecutable</key><string>SentriPet</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>SentriPet</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
    chmod +x "$app/Contents/MacOS/SentriPet"
    # no Apple developer certificate: sign ad hoc (Apple silicon refuses unsigned code)
    codesign --force --deep --sign - "$app"
    rm -f "$out/$name.zip"
    ditto -c -k --keepParent "$app" "$out/$name.zip"
    echo "$out/$name.zip"
    ;;

  linux-*)
    dir="$out/$name"
    rm -rf "$dir"
    cp -R "$pub" "$dir"
    cp "$root/assets/app.png" "$dir/sentripet.png"
    cp "$root/xplat/install-linux.sh" "$dir/install.sh"
    chmod +x "$dir/SentriPet" "$dir/install.sh"
    tar -czf "$out/$name.tar.gz" -C "$out" "$name"
    echo "$out/$name.tar.gz"
    ;;

  *) echo "unknown runtime id: $rid" >&2; exit 1 ;;
esac
