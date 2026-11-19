// Lets Explorer run a WebLingo script when it is double-clicked: .wl files, for this Windows account, run by weblingo,
// `weblingo script.wl`, in a console window of its own, which weblingo keeps open until a key is pressed once the script
// has ended, so that what it wrote stays to be read; "Edit" on their right-click menu, which opens one in Notepad; and
// Explorer's preview of one as text. It needs no administrator, changing only this account's file types,
// HKEY_CURRENT_USER\Software\Classes. Run it once WebLingo is installed:
//
//   weblingo add-wl-file-type.wl
//
// folder=<folder> names WebLingo's folder, where it is neither C:\Program Files\WebLingo nor
// C:\Program Files (x86)\WebLingo; remove=yes takes the file type away again. It may be run again: it writes the same.
// At the first double-click, Windows asks once which program to open .wl files with, since only the user may choose
// that, and its list may leave WebLingo out: choose "Choose an app on your PC" (on Windows 10, "More apps", then "Look
// for another app on this PC"), then C:\Program Files\WebLingo\weblingo.exe, and "Always"; Windows then opens them with
// Applications\weblingo.exe, which this registers with the command above. A script double-clicked runs as any program
// does, one downloaded from the internet included: run only those you trust.
param folder = "";
param remove = "no";

import { writeLn } from "std:system";
import com from "windows:com";
import fs from "std:fs";
import { programFiles } from "./windows.wl";

const classes = "HKCU\\Software\\Classes\\";
const type = "WebLingo.Script";
// weblingo as an application, which Windows's "How do you want to open this file?" offers by its name.
const application = "Applications\\weblingo.exe";
var shell = com.create("WScript.Shell");

// Removes a key, its keys first, a key with keys under it not being removed; what is not there is passed over.
function removeKey(key, children) {
    foreach (var child in children) {
        removeKey($"{key}\\{child}", []);
    }
    try {
        shell.RegDelete($"{classes}{key}\\");
    } catch (ComError e) {
    }
}

if (remove == "yes") {
    removeKey($"{type}\\shell\\open", ["command"]);
    removeKey($"{type}\\shell\\edit", ["command"]);
    removeKey($"{type}\\shell", []);
    removeKey(type, ["DefaultIcon"]);
    removeKey($"{application}\\shell\\open", ["command"]);
    removeKey($"{application}\\shell", []);
    removeKey(application, ["SupportedTypes"]);
    try {
        shell.RegDelete($"{classes}.wl\\OpenWithProgids\\{type}");
    } catch (ComError e) {
    }
    removeKey(".wl", ["OpenWithProgids"]);
    // .wl itself, where another program keeps keys of its own under it: only its link to this file type.
    try {
        if (shell.RegRead($"{classes}.wl\\") == type) {
            shell.RegWrite($"{classes}.wl\\", "", "REG_SZ");
        }
    } catch (ComError e) {
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
    var run = $"\"{weblingo}\" \"%1\" %*";

    shell.RegWrite($"{classes}.wl\\", type, "REG_SZ");
    shell.RegWrite($"{classes}.wl\\OpenWithProgids\\{type}", "", "REG_SZ");
    shell.RegWrite($"{classes}.wl\\PerceivedType", "text", "REG_SZ");
    shell.RegWrite($"{classes}.wl\\Content Type", "text/plain", "REG_SZ");
    shell.RegWrite($"{classes}{type}\\", "WebLingo script", "REG_SZ");
    shell.RegWrite($"{classes}{type}\\DefaultIcon\\", $"\"{weblingo}\",0", "REG_SZ");
    shell.RegWrite($"{classes}{type}\\shell\\open\\FriendlyAppName", "WebLingo", "REG_SZ");
    shell.RegWrite($"{classes}{type}\\shell\\open\\command\\", run, "REG_SZ");
    shell.RegWrite($"{classes}{type}\\shell\\edit\\command\\", "notepad.exe \"%1\"", "REG_SZ");
    shell.RegWrite($"{classes}{application}\\FriendlyAppName", "WebLingo", "REG_SZ");
    shell.RegWrite($"{classes}{application}\\SupportedTypes\\.wl", "", "REG_SZ");
    shell.RegWrite($"{classes}{application}\\shell\\open\\command\\", run, "REG_SZ");
    writeLn($".wl files are WebLingo's for this account: double-clicking one runs it with {weblingo}; Edit opens it in Notepad.");
}
