#!/usr/bin/env bash
# Makes WebLingo's installers for Linux, for x64 and for arm64, each self-contained, with .NET's runtime, so that a
# machine needs nothing of .NET's installed:
#   weblingo-<version>-linux-<architecture>.tar.gz, a folder holding install.sh, which installs WebLingo in /opt/weblingo
#   and links /usr/local/bin/weblingo and /usr/local/bin/wasp to it, run as sudo ./install.sh, its folder WebLingo,
#   what it installs, and readme.md, a human's steps, this folder's;
#   weblingo_<version>_<amd64 or arm64>.deb, the same files in /opt/weblingo, /usr/bin/weblingo and /usr/bin/wasp linked
#   to them, which sudo apt install ./weblingo_<version>_amd64.deb installs, and sudo apt remove weblingo removes.
# What each installs is wasp's publish, which holds weblingo's too (Documents/implementation.md, section 3): weblingo,
# the scripts' runner, wasp, WASP's, and the standard modules, without what only Windows runs, weblingow and windows:com.
# Run it on Linux, with the .NET 10 SDK, GNU tar and, for the .deb, dpkg-deb, from any folder:
#   Installers/Linux/package.sh [--architecture x64|arm64] [--destination <folder>]
# --architecture makes one architecture's alone; the packages go to the repository's Installers folder by default, which
# git ignores them in.
set -euo pipefail
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repository=$(cd "$here/../.." && pwd)
solution=$repository/WebLingo
destination=$repository/Installers
architectures=(x64 arm64)
usage() { echo "usage: package.sh [--architecture x64|arm64] [--destination <folder>]" >&2; exit 2; }
while [ $# -gt 0 ]; do
  case $1 in
    --architecture)
      [ $# -ge 2 ] || usage
      case $2 in x64|arm64) architectures=("$2") ;; *) usage ;; esac
      shift 2 ;;
    --destination)
      [ $# -ge 2 ] || usage
      destination=$2
      shift 2 ;;
    *) usage ;;
  esac
done
command -v dotnet > /dev/null || { echo "package.sh: the .NET 10 SDK's dotnet is not on the PATH." >&2; exit 1; }
command -v dpkg-deb > /dev/null || { echo "package.sh: dpkg-deb, which makes the .deb, is not installed." >&2; exit 1; }
# The solution's one version, which every assembly carries (Directory.Build.props).
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$solution/Directory.Build.props" | head -n 1)
[ -n "$version" ] || { echo "package.sh: no <Version> in Directory.Build.props." >&2; exit 1; }
mkdir -p "$destination"
destination=$(cd "$destination" && pwd)
staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT

# wasp's publish for an architecture, keeping only what runs (Runtime.pubxml: no symbols, nor SqlClient's messages in
# other languages), and only what runs on Linux: weblingow is weblingo as a Windows application, its program left out, its
# assembly kept, since wasp's dependencies name it; windows:com is Windows's own (specification, section 3.10). Its files
# readable by every account, its programs run by every account.
publish() {
  local architecture=$1 folder=$2
  dotnet publish "$solution/src/WebLingo.Wasp.Cli/WebLingo.Wasp.Cli.csproj" -c Release -r "linux-$architecture" \
    --self-contained true -o "$folder" "-p:PublishProfileFullPath=$solution/Runtime.pubxml"
  rm -f "$folder/weblingow"
  rm -rf "$folder/windows"
  chmod -R u=rwX,go=rX "$folder"
  chmod 755 "$folder/weblingo" "$folder/wasp"
  [ ! -e "$folder/createdump" ] || chmod 755 "$folder/createdump"
}

for architecture in "${architectures[@]}"; do
  published=$staging/published-$architecture
  publish "$architecture" "$published"

  # The archive: install.sh and readme.md beside the folder WebLingo, which holds uninstall.sh too.
  name=weblingo-$version-linux-$architecture
  root=$staging/$name
  mkdir -p "$root"
  cp -R "$published" "$root/WebLingo"
  cp "$here/install.sh" "$root/install.sh"
  cp "$here/uninstall.sh" "$root/WebLingo/uninstall.sh"
  cp "$here/readme.md" "$root/readme.md"
  # The MIT License, which every copy of WebLingo carries, installed with it (specification, section 12), and the
  # licenses and notices of the packages it ships, which their licenses ask every copy to carry.
  cp "$repository/LICENSE" "$root/WebLingo/LICENSE"
  cp "$repository/THIRD_PARTY_NOTICES.md" "$root/WebLingo/THIRD_PARTY_NOTICES.md"
  chmod 755 "$root/install.sh" "$root/WebLingo/uninstall.sh"
  chmod 644 "$root/readme.md" "$root/WebLingo/LICENSE" "$root/WebLingo/THIRD_PARTY_NOTICES.md"
  tar --owner=0 --group=0 --numeric-owner -czf "$destination/$name.tar.gz" -C "$staging" "$name"
  echo "Made $destination/$name.tar.gz, $(du -h "$destination/$name.tar.gz" | cut -f1)"

  # The Debian package: the same files, as root's, in /opt/weblingo, the programs linked from /usr/bin.
  case $architecture in x64) debian=amd64 ;; arm64) debian=arm64 ;; esac
  package=$staging/deb-$architecture
  mkdir -p "$package/DEBIAN" "$package/opt" "$package/usr/bin"
  cp -R "$published" "$package/opt/weblingo"
  install -m 644 "$repository/LICENSE" "$package/opt/weblingo/LICENSE"
  install -m 644 "$repository/THIRD_PARTY_NOTICES.md" "$package/opt/weblingo/THIRD_PARTY_NOTICES.md"
  ln -s /opt/weblingo/weblingo "$package/usr/bin/weblingo"
  ln -s /opt/weblingo/wasp "$package/usr/bin/wasp"
  # What a self-contained .NET program needs of the system (Microsoft's ".NET dependencies" for Debian and Ubuntu): ICU
  # and OpenSSL by the names their releases give them, and tzdata, the time zones std:datetime reads.
  cat > "$package/DEBIAN/control" <<EOF
Package: weblingo
Version: $version
Architecture: $debian
Maintainer: The WebLingo project
Installed-Size: $(du -sk "$package/opt" | cut -f1)
Depends: libc6, libgcc-s1 | libgcc1, libstdc++6, zlib1g, libgssapi-krb5-2, libssl3t64 | libssl3 | libssl1.1, libicu78 | libicu77 | libicu76 | libicu75 | libicu74 | libicu73 | libicu72 | libicu71 | libicu70 | libicu69 | libicu68 | libicu67 | libicu66 | libicu63, tzdata
Section: interpreters
Priority: optional
Description: WebLingo, a server-side scripting language, and WASP
 weblingo runs WebLingo's scripts, as weblingo script.wlg, or by their names where
 their first line is #!/usr/bin/env weblingo; wasp serves WASP's sites, through
 FastCGI behind nginx or Apache httpd. With the standard modules and .NET's
 runtime, which nothing else needs installed.
EOF
  chmod 644 "$package/DEBIAN/control"
  deb=$destination/weblingo_${version}_$debian.deb
  dpkg-deb --root-owner-group --build "$package" "$deb" > /dev/null
  echo "Made $deb, $(du -h "$deb" | cut -f1)"
done
