# WebLingo for Linux

WebLingo is a scripting language. This archive holds `weblingo`, which runs WebLingo's scripts, `.wlg` files, `wasp`,
which serves WASP's sites, WebLingo's web pages, and their standard modules, with .NET's runtime, so that nothing of
.NET's needs installing. **install.sh** installs them for the whole machine.

It comes in two archives, and two Debian packages, named for the processors they run on:

- **`weblingo-…-linux-x64.tar.gz`** and **`weblingo_…_amd64.deb`** are for Intel's and AMD's 64-bit processors, which
  most servers and PCs have.
- **`weblingo-…-linux-arm64.tar.gz`** and **`weblingo_…_arm64.deb`** are for 64-bit ARM, such as a Raspberry Pi 4 or 5
  with a 64-bit system, or an ARM server.

They need a Linux whose C library is glibc, as Debian's, Ubuntu's, Fedora's and most others are (not Alpine's), with ICU
and OpenSSL, which most have installed; on one without them, install `libicu` and `libssl` from its packages.

## Installing from the archive

```sh
tar -xzf weblingo-0.21.0-linux-x64.tar.gz
cd weblingo-0.21.0-linux-x64
sudo ./install.sh
```

`install.sh` copies the folder `WebLingo`, beside it, to `/opt/weblingo`, replacing an installation there, and links
`/usr/local/bin/weblingo` and `/usr/local/bin/wasp` to it. It refuses a folder that holds anything but WebLingo, and a
`weblingo` or a `wasp` already in `/usr/local/bin` that is not its own link. Once it reports what it installed, you can
delete the extracted folder.

Without `sudo`, for your account alone, give it folders of your own, the second one on your `PATH`:

```sh
./install.sh --prefix ~/.local/weblingo --bin ~/.local/bin
```

## Installing the Debian package

On Debian, Ubuntu and the distributions made from them:

```sh
sudo apt install ./weblingo_0.21.0_amd64.deb
```

It installs the same files in `/opt/weblingo`, links `/usr/bin/weblingo` and `/usr/bin/wasp` to them, and installs the
libraries they need, if they are not there. Install it, or the archive, not both.

## Running a script

`weblingo --version` shows the installed version, `weblingo script.wlg` runs a script, and `weblingo check script.wlg`
checks it. A script whose first line is

```text
#!/usr/bin/env weblingo
```

runs by its name once it is executable, as a Python script does:

```sh
chmod +x hello.wlg
./hello.wlg
```

`wasp` serves a site to nginx or Apache httpd through FastCGI, `wasp fastcgi`, as the WASP User Manual's section on
nginx and Apache says.

## Updating

Install the newer archive's or package's the same way: `install.sh` replaces the installation in its folder, and
`apt` the package. Stop the sites `wasp` serves first, and restart them after.

## Removing it

Installed from the archive:

```sh
sudo /opt/weblingo/uninstall.sh
```

removes the links `install.sh` made and the folder `/opt/weblingo`, with everything in it; for an installation in folders
of your own, run `uninstall.sh` in the folder it installed. Installed from the package:

```sh
sudo apt remove weblingo
```
