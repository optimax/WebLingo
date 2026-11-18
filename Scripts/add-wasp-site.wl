// Adds a WASP site to IIS, with WebLingo installed in C:\Program Files\WebLingo (install-weblingo.wl): an application pool
// and a website, both named name, whose folder holds the site's pages beside its web.config, served on https://<host>/,
// HTTPS's standard port, 443, for that name alone (SNI), with a self-signed certificate this machine trusts, the name
// in the hosts file; and the pool given write access to the folders outside the site that writable lists, separated by
// semicolons, such as a SQLite database's. Run it as administrator:
//
//   weblingo run add-wasp-site.wl name=wasp.example2 folder=C:\WWW\wasp.example2\site host=example2.wasp.localhost
//       writable=C:\WWW\wasp.example2\database
//
// It may be run again: it keeps what is there, and adds what is missing.
param name;
param folder;
param host;
param writable = "";

import { writeLn } from "std:system";
import fs from "std:fs";
import { Regex } from "std:regex";
import { requireAdministrator, runOrStop, expand, makeCertificate, trust, boundCertificate } from "./windows.wl";
import { section, commit, find, startPool, startSite } from "./iis.wl";

await requireAdministrator();
if (!(await fs.exists($"{folder}\\web.config"))) {
    throw new Error($"{folder} has no web.config: give the folder that holds the site's pages and web.config.");
}
if (!(await fs.exists("C:\\Program Files\\WebLingo\\wasp.exe"))) {
    throw new Error("WebLingo is not installed in C:\\Program Files\\WebLingo: install it first, with install-weblingo.wl.");
}
var writables = writable == "" ? [] : writable.split(";");
foreach (var other in writables) {
    if (!(await fs.exists(other))) {
        throw new Error($"{other} is not there.");
    }
}

// The application pool: no managed code, the ASP.NET Core Module running wasp in a process of its own.
var pools = section("system.applicationHost/applicationPools").Collection;
var pool = find(pools, "name", name);
if (pool == null) {
    pool = pools.CreateNewElement("add");
    pool.GetPropertyByName("name").Value = name;
    pool.GetPropertyByName("managedRuntimeVersion").Value = "";
    pools.AddElement(pool, -1);
    writeLn($"Application pool {name} added");
} else if (pool.GetPropertyByName("managedRuntimeVersion").Value != "") {
    pool.GetPropertyByName("managedRuntimeVersion").Value = "";
    writeLn($"Application pool {name} set to no managed code");
}
commit();

// The folders outside the site that the pool's account, IIS AppPool\<name>, changes: SQLite writes its journal beside
// its database while it changes it.
foreach (var other in writables) {
    await runOrStop($"icacls \"{other}\" /grant \"IIS AppPool\\{name}:(OI)(CI)M\"");
    writeLn($"IIS AppPool\\{name} may write to {other}");
}

// The site's certificate: the one http.sys gives its name already, or a new one; trusted by this machine.
var thumbprint = await boundCertificate(host);
if (thumbprint == null) {
    thumbprint = makeCertificate(host, true).thumbprint;
    writeLn($"Certificate made for {host}, {thumbprint}");
}
if (await trust(thumbprint)) {
    writeLn("Certificate trusted by this machine");
}

// The website, its folder the site's own files, beside its web.config, with HTTPS on port 443 for its name alone.
var bindingInformation = $"*:443:{host}";
var all = section("system.applicationHost/sites").Collection;
var site = find(all, "name", name);
var adding = site == null;
if (adding) {
    var id = 1;
    for (var i = 0; i < all.Count; i++) {
        var taken = all.Item(i).GetPropertyByName("id").Value;
        if (taken >= id) {
            id = taken + 1;
        }
    }
    site = all.CreateNewElement("site");
    site.GetPropertyByName("name").Value = name;
    site.GetPropertyByName("id").Value = id;
    var application = site.Collection.CreateNewElement("application");
    application.GetPropertyByName("path").Value = "/";
    application.GetPropertyByName("applicationPool").Value = name;
    var root = application.Collection.CreateNewElement("virtualDirectory");
    root.GetPropertyByName("path").Value = "/";
    root.GetPropertyByName("physicalPath").Value = folder;
    application.Collection.AddElement(root, -1);
    site.Collection.AddElement(application, -1);
}
var bindings = site.GetElementByName("bindings").Collection;
var bound = false;
for (var i = 0; i < bindings.Count; i++) {
    var binding = bindings.Item(i);
    if (binding.GetPropertyByName("protocol").Value == "https"
        && binding.GetPropertyByName("bindingInformation").Value.toLower() == bindingInformation.toLower()) {
        bound = true;
    }
}
if (!bound) {
    var binding = bindings.CreateNewElement("binding");
    binding.GetPropertyByName("protocol").Value = "https";
    binding.GetPropertyByName("bindingInformation").Value = bindingInformation;
    binding.GetPropertyByName("sslFlags").Value = 1; // by its name, SNI
    bindings.AddElement(binding, -1);
}
if (adding) {
    all.AddElement(site, -1);
    writeLn($"Website {name} added, from {folder}");
} else if (!bound) {
    writeLn($"Website {name} bound to https://{host}/");
}
commit();

// The certificate, in http.sys, for the name on port 443: IIS's own application id, as IIS gives it.
if ((await boundCertificate(host)) == null) {
    await runOrStop($"netsh http add sslcert hostnameport={host}:443 certhash={thumbprint} appid={{4dc3e181-e14b-4a21-b022-59fc669b0914}} certstorename=MY");
    writeLn($"https://{host}/ given its certificate");
}

// The name, for this machine's own resolver too: browsers take every *.localhost name for this machine on their own, but
// Windows does not, so that other programs, curl and PowerShell among them, find it here.
var hosts = expand("%SystemRoot%\\System32\\drivers\\etc\\hosts");
var text = await fs.readText(hosts);
var entry = new Regex($"^\\s*127\\.0\\.0\\.1\\s+{host.replace(".", "\\.")}\\s*(#.*)?$", "i");
if (!text.split("\n").any(line => entry.test(line.trim()))) {
    var line = $"127.0.0.1 {host}\r\n";
    if (text.length > 0 && !text.endsWith("\n")) {
        line = "\r\n" + line;
    }
    await fs.appendText(hosts, line, "us-ascii");
    writeLn($"{host} added to {hosts}");
}

await startPool(name);
await startSite(name);
writeLn($"{name} is served on https://{host}/");
