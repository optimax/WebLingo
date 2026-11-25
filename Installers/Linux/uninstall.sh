#!/bin/sh
# Removes WebLingo that install.sh installed: the links to it that install.sh made, then this folder, everything in it.
# Run it as install.sh was run, as root for /opt/weblingo:
#   sudo /opt/weblingo/uninstall.sh
set -eu
prefix=$(cd "$(dirname "$0")" && pwd)
if [ ! -x "$prefix/weblingo" ]; then
    echo "uninstall.sh: $prefix is not a folder WebLingo is installed in." >&2
    exit 1
fi
bin=/usr/local/bin
if [ -f "$prefix/links" ]; then
    bin=$(cat "$prefix/links")
fi
for name in weblingo wasp; do
    if [ "$(readlink "$bin/$name" || true)" = "$prefix/$name" ]; then
        rm -f "$bin/$name"
    fi
done
rm -rf "$prefix"
echo "Removed WebLingo from $prefix."
