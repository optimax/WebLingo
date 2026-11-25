#!/bin/sh
# Installs WebLingo from the folder WebLingo beside this script: its files in /opt/weblingo, replacing an installation
# there, and weblingo and wasp linked from /usr/local/bin, so that a script runs as weblingo script.wlg, or by its name
# where its first line is #!/usr/bin/env weblingo and it is executable. Run it as root, from the folder its archive made:
#   sudo ./install.sh
# --prefix names another folder for WebLingo's files, and --bin another for its links, such as $HOME/.local/weblingo and
# $HOME/.local/bin, which need no sudo. uninstall.sh, in the folder it installs, removes it.
set -eu
prefix=/opt/weblingo
bin=/usr/local/bin
usage() {
    echo "usage: install.sh [--prefix <folder>] [--bin <folder>]" >&2
    exit 2
}
while [ $# -gt 0 ]; do
    case $1 in
        --prefix) [ $# -ge 2 ] || usage; prefix=$2; shift 2 ;;
        --bin) [ $# -ge 2 ] || usage; bin=$2; shift 2 ;;
        *) usage ;;
    esac
done
here=$(cd "$(dirname "$0")" && pwd)
source=$here/WebLingo
if [ ! -x "$source/weblingo" ]; then
    echo "install.sh: $source/weblingo is not there: run install.sh from the folder its archive made." >&2
    exit 1
fi
case $prefix in /*) ;; *) prefix=$(pwd)/$prefix ;; esac
case $bin in /*) ;; *) bin=$(pwd)/$bin ;; esac
prefix=${prefix%/}
bin=${bin%/}

# An earlier installation is replaced, never a folder that holds anything else, nor a program of another's.
if [ -d "$prefix" ] && [ -n "$(ls -A "$prefix")" ] && [ ! -f "$prefix/uninstall.sh" ]; then
    echo "install.sh: $prefix holds files that are not WebLingo's; give another folder with --prefix." >&2
    exit 1
fi
for name in weblingo wasp; do
    if [ -e "$bin/$name" ] || [ -L "$bin/$name" ]; then
        if [ "$(readlink "$bin/$name" || true)" != "$prefix/$name" ]; then
            echo "install.sh: $bin/$name is there already, and is not WebLingo's link to $prefix/$name." >&2
            exit 1
        fi
    fi
done

# Copied beside the folder first, then put in its place, so that a failed copy leaves the earlier installation whole.
mkdir -p "$(dirname "$prefix")" "$bin"
copy=$prefix.installing
rm -rf "$copy"
cp -R "$source" "$copy"
printf '%s\n' "$bin" > "$copy/links"
if [ -d "$prefix" ]; then
    rm -rf "$prefix.replaced"
    mv "$prefix" "$prefix.replaced"
    mv "$copy" "$prefix"
    rm -rf "$prefix.replaced"
else
    mv "$copy" "$prefix"
fi
for name in weblingo wasp; do
    ln -sfn "$prefix/$name" "$bin/$name"
done
echo "Installed $("$prefix/weblingo" --version | head -n 1) in $prefix: $bin/weblingo and $bin/wasp run it."
case :$PATH: in
    *:"$bin":*) ;;
    *) echo "$bin is not on your PATH: add it, or run $bin/weblingo." ;;
esac
