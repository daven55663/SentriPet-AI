#!/bin/sh
# Installs SentriPet for the current user (no root needed) and starts it:
#   ~/.local/share/sentripet          the program
#   ~/.local/share/applications       a menu entry
# SentriPet itself adds ~/.config/autostart/sentripet.desktop (turn it off in the menu or the settings).
# To remove: rm -rf ~/.local/share/sentripet ~/.local/share/applications/sentripet.desktop ~/.config/autostart/sentripet.desktop
set -e
here="$(cd "$(dirname "$0")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
dest="$data/sentripet"

pkill -x SentriPet 2>/dev/null || true
mkdir -p "$dest"
cp -R "$here"/. "$dest"/
chmod +x "$dest/SentriPet"

mkdir -p "$data/applications"
cat > "$data/applications/sentripet.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=SentriPet
Comment=AI usage desk pet
Exec="$dest/SentriPet"
Icon=$dest/sentripet.png
Terminal=false
Categories=Utility;
EOF

nohup "$dest/SentriPet" >/dev/null 2>&1 &
echo "Installed to $dest"
