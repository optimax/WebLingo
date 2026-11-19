// Installs WebLingo for the whole machine, or updates it: wasp, weblingo and the standard modules, copied from a published
// folder into C:\Program Files\WebLingo by std:fs, where every WASP site's web.config names wasp. The application pools of the IIS
// sites whose wasp runs from there are stopped while their files are replaced, then started again. Run it as administrator,
// from this folder, once WebLingo/publish.ps1 has published WebLingo into WebLingo\publish\WebLingo, with that folder's
// weblingo.exe, not the installed one, which cannot replace itself while it runs:
//
//   ..\WebLingo\publish\WebLingo\weblingo.exe run install-weblingo.wl
//
// source=<folder> installs another published folder, a relative path being from the current folder; target=<folder>
// installs elsewhere, where web.config's processPath must then point. It may be run again: it copies the files anew, over
// the old ones, and keeps any the new build lacks.
param source = "..\\WebLingo\\publish\\WebLingo";
param target = "C:\\Program Files\\WebLingo";

import { writeLn } from "std:system";
import com from "windows:com";
import fs from "std:fs";
import { requireAdministrator, runningFrom } from "./windows.wl";
import { copyIntoInstallation } from "./iis.wl";

await requireAdministrator();

// The folders' full paths, a relative one from the current folder.
var files = com.create("Scripting.FileSystemObject");
var published = files.GetAbsolutePathName(source);
var installed = files.GetAbsolutePathName(target);
foreach (var program in ["wasp.exe", "weblingo.exe"]) {
    if (!(await fs.exists($"{published}\\{program}"))) {
        throw new Error($"{published} has no {program}: publish WebLingo there first, with WebLingo/publish.ps1.");
    }
}
foreach (var path in runningFrom(installed)) {
    if (path.toLower().endsWith("\\weblingo.exe")) {
        throw new Error($"weblingo runs from {installed}: run this with {published}\\weblingo.exe, as the installed one cannot replace itself.");
    }
}

await copyIntoInstallation(published, installed);
writeLn($"WebLingo installed in {installed}, from {published}");
