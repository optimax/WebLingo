// Lets Explorer run a WebLingo script when it is double-clicked: .wl files, for this Windows account, run by weblingo in a
// console that waits for a key once the script has ended, so that what it wrote stays to be read; "Edit" on their
// right-click menu, which opens one in Notepad; and Explorer's preview of one as text. It needs no administrator,
// changing only this account's file types, HKEY_CURRENT_USER\Software\Classes. Run it once WebLingo is installed:
//
//   weblingo add-wl-file-type.wl
//
// folder=<folder> names WebLingo's folder, where it is neither C:\Program Files\WebLingo nor
// C:\Program Files (x86)\WebLingo; remove=yes takes the file type away again. It may be run again: it writes the same.
// A script double-clicked runs as any program does, one downloaded from the internet included: run only those you trust.
param folder = "";
param remove = "no";

import { writeLn } from "std:system";
import com from "windows:com";
import fs from "std:fs";
import { programFiles } from "./windows.wl";

const classes = "HKCU\\Software\\Classes\\";
const type = "WebLingo.Script";
var shell = com.create("WScript.Shell");

if (remove == "yes") {
    // Deepest first, since a key with keys under it is not removed; what is not there is passed over.
    foreach (var key in [$"{type}\\shell\\open\\command", $"{type}\\shell\\open", $"{type}\\shell\\edit\\command",
        $"{type}\\shell\\edit", $"{type}\\shell", $"{type}\\DefaultIcon", type]) {
        try {
            shell.RegDelete($"{classes}{key}\\");
        } catch (ComError e) {
        }
    }
    // .wl itself, unless another program keeps its own keys under it: then only its link to this file type.
    try {
        shell.RegDelete($"{classes}.wl\\");
    } catch (ComError e) {
        shell.RegWrite($"{classes}.wl\\", "", "REG_SZ");
    }
    writeLn(".wl files are no longer WebLingo's for this account: double-clicking one asks what to open it with.");
} else {
    // The weblingo installed: in the folder given, or in WebLingo's for x64 or for x86.
    var folders = folder != "" ? [folder] : [$"{programFiles("x64")}\\WebLingo", $"{programFiles("x86")}\\WebLingo"];
    var weblingo = null;
    foreach (var candidate in folders) {
        if (weblingo == null && (await fs.exists($"{candidate}\\weblingo.exe"))) {
            weblingo = $"{candidate}\\weblingo.exe";
        }
    }
    if (weblingo == null) {
        throw new Error($"weblingo.exe is not in {" or ".join(folders)}: install WebLingo first, or name its folder, folder=<folder>.");
    }

    shell.RegWrite($"{classes}.wl\\", type, "REG_SZ");
    shell.RegWrite($"{classes}.wl\\PerceivedType", "text", "REG_SZ");
    shell.RegWrite($"{classes}.wl\\Content Type", "text/plain", "REG_SZ");
    shell.RegWrite($"{classes}{type}\\", "WebLingo script", "REG_SZ");
    shell.RegWrite($"{classes}{type}\\DefaultIcon\\", $"\"{weblingo}\",0", "REG_SZ");
    // Run by cmd.exe, which waits for a key after weblingo has ended, whatever its exit code.
    shell.RegWrite($"{classes}{type}\\shell\\open\\command\\", $"cmd.exe /c \"\"{weblingo}\" \"%1\" %* & pause\"", "REG_SZ");
    shell.RegWrite($"{classes}{type}\\shell\\edit\\command\\", "notepad.exe \"%1\"", "REG_SZ");
    writeLn($".wl files are WebLingo's for this account: double-clicking one runs it with {weblingo}; Edit opens it in Notepad.");
}
