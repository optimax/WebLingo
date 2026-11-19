// Installs WebLingo for the whole machine from its installer's zip (Installers/Windows/readme.md): weblingo and the
// standard modules, the folder WebLingo beside this script, copied by std:fs into WebLingo's folder, which is then put
// on the machine's PATH, so that consoles started after run weblingo by its name, and further scripts with it. Its
// folder is C:\Program Files\WebLingo, or, for the zips for x86, 32-bit programs, on 64-bit Windows,
// C:\Program Files (x86)\WebLingo. Run it as administrator, from the unzipped folder, with the weblingo the zip holds:
//
//   WebLingo\weblingo.exe run install.wl
//
// target=<folder> installs elsewhere; source=<folder> installs another folder, a relative path being from the current
// folder. It may be run again, from a newer zip, to update WebLingo: it copies the files anew, over the old ones, and
// keeps any the new zip lacks, wasp among them; it puts the folder on the PATH once, after any other there, so that a
// machine with WebLingo for x64 and for x86 runs the one installed first by its name.
param source = "WebLingo";
param target = "";

import { writeLn } from "std:system";
import com from "windows:com";
import fs from "std:fs";
import { requireAdministrator, runningFrom, copyFolder, addToMachinePath, architectureOf, programFiles } from "./windows.wl";

await requireAdministrator();

// The folders' full paths, a relative one from the current folder; WebLingo's folder for the zip's architecture, unless
// target names another.
var files = com.create("Scripting.FileSystemObject");
var zipped = files.GetAbsolutePathName(source);
if (!(await fs.exists($"{zipped}\\weblingo.exe"))) {
    throw new Error($"{zipped} has no weblingo.exe: run this from the unzipped folder, which holds the folder WebLingo.");
}
var installed = files.GetAbsolutePathName(target == "" ? $"{programFiles(await architectureOf(zipped, "weblingo"))}\\WebLingo" : target);
if (zipped.toLower() == installed.toLower()) {
    throw new Error($"{zipped} is where WebLingo is installed: run this from the unzipped folder instead.");
}

// A program running from the installation keeps its files in use, as IIS's sites' wasp does.
var running = runningFrom(installed);
if (running.length > 0) {
    throw new Error($"Programs run from {installed}, {running}: stop them, then run this again.");
}

await copyFolder(zipped, installed);
writeLn($"WebLingo installed in {installed}");
if (addToMachinePath(installed)) {
    writeLn($"{installed} added to the machine's PATH");
} else {
    writeLn($"{installed} is on the machine's PATH already");
}
writeLn("Open a new console, and run: weblingo --version");
