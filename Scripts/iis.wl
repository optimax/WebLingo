// IIS, as the setup scripts reach it: its configuration, applicationHost.config, through its own COM objects
// (Microsoft.ApplicationHost.WritableAdminManager), and its application pools' states through appcmd, which IIS installs.
// Both need an administrator.
import com from "windows:com";
import { run, runOrStop, expand } from "./windows.wl";

const appcmd = "%SystemRoot%\\System32\\inetsrv\\appcmd";
const admin = com.create("Microsoft.ApplicationHost.WritableAdminManager");
admin.CommitPath = "MACHINE/WEBROOT/APPHOST";

// A section of IIS's configuration: "system.applicationHost/sites", "system.applicationHost/applicationPools".
export function section(name) {
    return admin.GetAdminSection(name, "MACHINE/WEBROOT/APPHOST");
}

// Writes what this script changed in IIS's configuration.
export function commit() {
    admin.CommitChanges();
}

// The element of a collection whose property is that text, its case aside, as IIS compares names; null for none.
export function find(collection, property, text) {
    for (var i = 0; i < collection.Count; i++) {
        var element = collection.Item(i);
        var value = element.GetPropertyByName(property).Value;
        if (value is String && value.toLower() == text.toLower()) {
            return element;
        }
    }
    return null;
}

// A website: its name, its root application's pool, and its root's folder, expanded, null where it has none.
export class Site {
    constructor(name, pool, folder) {
        this.name = name;
        this.pool = pool;
        this.folder = folder;
    }
}

// Every website IIS has.
export function sites() {
    var found = [];
    var all = section("system.applicationHost/sites").Collection;
    for (var i = 0; i < all.Count; i++) {
        var site = all.Item(i);
        var application = find(site.Collection, "path", "/");
        if (application == null) {
            continue;
        }
        var root = find(application.Collection, "path", "/");
        var folder = root == null ? null : expand(root.GetPropertyByName("physicalPath").Value);
        found.push(new Site(site.GetPropertyByName("name").Value, application.GetPropertyByName("applicationPool").Value, folder));
    }
    return found;
}

// An application pool's state, "Started", "Starting", "Stopping" or "Stopped"; null where there is no such pool.
export async function poolState(name) {
    var ran = await run($"{appcmd} list apppool \"{name}\" /text:state");
    var state = ran.output.trim();
    return ran.code == 0 && state != "" ? state : null;
}

// Waits, up to two minutes, until a pool is in that state: appcmd only asks IIS to start or stop one, and returns at once.
export async function waitForPool(name, state) {
    for (var i = 0; i < 240; i++) {
        if ((await poolState(name)) == state) {
            return;
        }
        await delay(500);
    }
    throw new Error($"The application pool {name} is not {state} after two minutes.");
}

// Stops a pool, and waits until it has stopped; whether it was running, so that it can be started again.
export async function stopPool(name) {
    var state = await poolState(name);
    if (state == "Starting") {
        await waitForPool(name, "Started");
        state = "Started";
    }
    if (state == "Started") {
        await runOrStop($"{appcmd} stop apppool \"{name}\"");
    }
    await waitForPool(name, "Stopped");
    return state == "Started";
}

// Starts a pool, and waits until it has started.
export async function startPool(name) {
    var state = await poolState(name);
    if (state == "Stopping") {
        await waitForPool(name, "Stopped");
        state = "Stopped";
    }
    if (state == "Stopped") {
        await runOrStop($"{appcmd} start apppool \"{name}\"");
    }
    await waitForPool(name, "Started");
}

// Starts a website, unless it is started.
export async function startSite(name) {
    if ((await runOrStop($"{appcmd} list site \"{name}\" /text:state")).trim() != "Started") {
        await runOrStop($"{appcmd} start site \"{name}\"");
    }
}
