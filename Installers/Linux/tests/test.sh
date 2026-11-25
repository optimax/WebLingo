#!/usr/bin/env bash
# Tries the x64 packages package.sh made into <folder> on this machine, Linux on x64, as CI's ubuntu-latest runs it:
# the archive installed by install.sh as root, its programs run, a script by its name, and removed; installed again in
# folders of the account's own, without root; and the Debian package installed by apt, run, and removed. Needs sudo.
#   Installers/Linux/tests/test.sh <folder>
set -euo pipefail
packages=$(cd "$1" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
archive=$(ls "$packages"/weblingo-*-linux-x64.tar.gz)
deb=$(ls "$packages"/weblingo_*_amd64.deb)
version=$(basename "$archive" | sed -E 's/^weblingo-(.*)-linux-x64\.tar\.gz$/\1/')

fail() { echo "FAILED: $*" >&2; exit 1; }

# A script run by its name, its first line #!/usr/bin/env weblingo, importing standard modules and given a parameter.
cat > "$work/hello.wlg" <<'EOF'
#!/usr/bin/env weblingo
param name = "world";
import { writeLn } from "std:console";
import math from "std:math";
import { DateTime } from "std:datetime";
writeLn($"Hello, {name}: {math.sqrt(16.0)}, {DateTime.utc(2026, 11, 24).toZone("Europe/Warsaw"):yyyy-MM-dd HH:mm}");
EOF
chmod +x "$work/hello.wlg"
cat > "$work/page.wasp" <<'EOF'
<p><%= 6 * 7 %></p>
EOF

# What weblingo and wasp, linked from the folder $1, must do, a script by its name found on a PATH with $1 first.
check() {
    local bin=$1
    [ "$("$bin/weblingo" --version | head -n 1 | cut -d' ' -f1-2)" = "WebLingo $version" ] || fail "weblingo --version"
    [ "$("$bin/wasp" --version | head -n 1 | cut -d' ' -f1-2)" = "WASP $version" ] || fail "wasp --version"
    [ "$("$bin/weblingo" "$work/hello.wlg" name=Linux)" = "Hello, Linux: 4.0, 2026-11-24 01:00" ] || fail "weblingo hello.wlg"
    [ "$(cd "$work" && PATH="$bin:$PATH" ./hello.wlg)" = "Hello, world: 4.0, 2026-11-24 01:00" ] || fail "./hello.wlg by its name"
    [ "$("$bin/wasp" run "$work/page.wasp")" = "<p>42</p>" ] || fail "wasp run page.wasp"
}

tar -xzf "$archive" -C "$work"
folder=$work/weblingo-$version-linux-x64

echo "The archive, installed as root:"
sudo "$folder/install.sh"
check /usr/local/bin
[ "$(command -v weblingo)" = /usr/local/bin/weblingo ] || fail "weblingo is not /usr/local/bin's"
# Refused: a folder holding something else.
mkdir -p "$work/other" && touch "$work/other/mine"
if "$folder/install.sh" --prefix "$work/other" --bin "$work/otherbin" 2> /dev/null; then fail "a folder not WebLingo's was taken"; fi
# Installed again over itself, then removed.
sudo "$folder/install.sh"
sudo /opt/weblingo/uninstall.sh
[ ! -e /opt/weblingo ] || fail "/opt/weblingo is still there"
[ ! -e /usr/local/bin/weblingo ] && [ ! -L /usr/local/bin/weblingo ] || fail "/usr/local/bin/weblingo is still there"

echo "The archive, in folders of the account's own:"
"$folder/install.sh" --prefix "$work/home/weblingo" --bin "$work/home/bin"
check "$work/home/bin"
"$work/home/weblingo/uninstall.sh"
[ ! -e "$work/home/weblingo" ] && [ ! -L "$work/home/bin/weblingo" ] || fail "the account's installation is still there"

echo "The Debian package:"
sudo apt-get install -y "$deb" > /dev/null
check /usr/bin
[ "$(command -v weblingo)" = /usr/bin/weblingo ] || fail "weblingo is not /usr/bin's"
sudo apt-get remove -y weblingo > /dev/null
[ ! -e /opt/weblingo/weblingo ] || fail "the package's files are still there"

echo "Every installer of $version for Linux on x64 installed, ran and was removed."
