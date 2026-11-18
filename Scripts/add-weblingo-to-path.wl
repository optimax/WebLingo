// Puts WebLingo's folder, C:\Program Files\WebLingo, on the machine's PATH, so that weblingo and wasp run by their names in
// every console started after. It adds the folder once, at the end, keeping the other entries as they are, their %names%
// unexpanded. Run it as administrator:
//
//   C:\Program Files\WebLingo\weblingo.exe run add-weblingo-to-path.wl
//
// folder=<folder> adds another. It may be run again: it does nothing then.
param folder = "C:\\Program Files\\WebLingo";

import { writeLn } from "std:io";
import com from "windows:com";
import fs from "std:fs";
import { requireAdministrator } from "./windows.wl";

await requireAdministrator();
if (!(await fs.exists($"{folder}\\weblingo.exe"))) {
    throw new Error($"{folder} has no weblingo.exe: install WebLingo first, with install-weblingo.wl.");
}

// A PATH entry as Windows compares them: its case and a last backslash aside.
function normal(entry) {
    var text = entry.trim().toLower();
    return text.endsWith("\\") ? text.slice(0, text.length - 1) : text;
}

// The machine's environment, as WScript.Shell keeps it: the registry's, its values unexpanded.
var environment = com.create("WScript.Shell").Environment("SYSTEM");
var path = environment["Path"];
if (path.split(";").any(entry => normal(entry) == normal(folder))) {
    writeLn($"{folder} is on the machine's PATH already");
} else {
    while (path.endsWith(";")) {
        path = path.slice(0, path.length - 1);
    }
    environment["Path"] = $"{path};{folder}";
    writeLn($"{folder} added to the machine's PATH: consoles started from now on find weblingo and wasp by their names");
}
