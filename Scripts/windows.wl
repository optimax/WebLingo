// What the setup scripts share, through Windows's COM objects: running a program and waiting for it, the processes
// running, and a certificate, made and trusted. The COM objects a script makes are listed in weblingo.config, beside it.
import com from "windows:com";
import fs from "std:fs";
import { delay } from "std:system";
import { DateTime } from "std:datetime";
import { Regex } from "std:regex";

const shell = com.create("WScript.Shell");

// A folder copied whole, through std:fs, without COM: its folders made, and its files copied as they are, over any of
// the same names; files the target has that the source lacks are kept.
export async function copyFolder(source, target) {
    await fs.makeFolder(target);
    foreach (var name in await fs.list(source)) {
        var from = $"{source}\\{name}";
        var to = $"{target}\\{name}";
        if (await fs.isFolder(from)) {
            await copyFolder(from, to);
        } else {
            await fs.copy(from, to);
        }
    }
}

// What a program gave: its exit code, and what it wrote, its errors among it.
export class Ran {
    constructor(code, output) {
        this.code = code;
        this.output = output;
    }
}

// Runs a command line through cmd.exe, which waits for it, and gives its exit code and what it wrote.
export async function run(command) {
    var process = shell.Exec($"cmd.exe /d /s /c \"{command} 2>&1\"");
    var output = process.StdOut.ReadAll();
    while (process.Status == 0) {
        await delay(20);
    }
    return new Ran(process.ExitCode, output);
}

// Runs a command line, as run does, and stops the script where it fails, with what it wrote.
export async function runOrStop(command) {
    var ran = await run(command);
    if (ran.code != 0) {
        throw new Error($"{command}: exit code {ran.code}. {ran.output.trim()}");
    }
    return ran.output;
}

// Stops the script unless it runs as an administrator: fltmc lists the file system's filters only for one.
export async function requireAdministrator() {
    if ((await run("fltmc")).code != 0) {
        throw new Error("This script changes the machine: run it from a console run as administrator.");
    }
}

// An environment variable of this process, expanded as Windows expands it: "%TEMP%".
export function expand(text) {
    return shell.ExpandEnvironmentStrings(text);
}

// Puts a folder on the machine's PATH, once, at its end, keeping the other entries as they are, their %names%
// unexpanded, as WScript.Shell keeps the machine's environment, the registry's: whether it added it, consoles started
// after then finding its programs by their names. An entry is compared as Windows compares them, its case and a last
// backslash aside.
export function addToMachinePath(folder) {
    var environment = shell.Environment("SYSTEM");
    var path = environment["Path"];
    if (path.split(";").any(entry => pathEntry(entry) == pathEntry(folder))) {
        return false;
    }
    while (path.endsWith(";")) {
        path = path.slice(0, path.length - 1);
    }
    environment["Path"] = $"{path};{folder}";
    return true;
}

// A PATH entry as Windows compares them.
function pathEntry(entry) {
    var text = entry.trim().toLower();
    return text.endsWith("\\") ? text.slice(0, text.length - 1) : text;
}

// The paths of the programs running from a folder, through WMI, which shows every process's path to an administrator.
export function runningFrom(folder) {
    var wmi = com.create("WbemScripting.SWbemLocator").ConnectServer(".", "root\\cimv2");
    var prefix = folder.toLower();
    if (!prefix.endsWith("\\")) {
        prefix += "\\";
    }
    var found = [];
    foreach (var process in wmi.ExecQuery("SELECT ExecutablePath FROM Win32_Process")) {
        var path = process.ExecutablePath;
        if (path != null && path.toLower().startsWith(prefix)) {
            found.push(path);
        }
    }
    return found;
}

// A certificate for TLS, made and installed for a host name: self-signed, for that name only, not a CA, signed with
// SHA-256, valid for two years, its private key RSA's, 2,048 bits, not exportable, in the machine's personal store
// (Cert:\LocalMachine\My) when machine is true, else the user's. It gives the certificate's SHA-1 thumbprint, in
// capitals, as Windows names it, and the certificate itself, in Base64.
export class Certificate {
    constructor(thumbprint, base64) {
        this.thumbprint = thumbprint;
        this.base64 = base64;
    }
}

export function makeCertificate(hostName, machine) {
    var key = com.create("X509Enrollment.CX509PrivateKey");
    key.ProviderName = "Microsoft Enhanced RSA and AES Cryptographic Provider";
    key.MachineContext = machine;
    key.Length = 2048;
    key.KeySpec = 1; // AT_KEYEXCHANGE, which TLS's key exchange needs
    key.ExportPolicy = 0; // not exportable
    key.Create();

    var request = com.create("X509Enrollment.CX509CertificateRequestCertificate");
    request.InitializeFromPrivateKey(machine ? 2 : 1, key, ""); // ContextMachine, ContextUser
    var subject = com.create("X509Enrollment.CX500DistinguishedName");
    subject.Encode($"CN={hostName}", 0);
    request.Subject = subject;
    request.Issuer = subject;
    request.NotBefore = DateTime.now().addDays(-1);
    request.NotAfter = DateTime.now().addYears(2);

    var serverAuthentication = com.create("X509Enrollment.CObjectId");
    serverAuthentication.InitializeFromValue("1.3.6.1.5.5.7.3.1");
    var purposes = com.create("X509Enrollment.CObjectIds");
    purposes.Add(serverAuthentication);
    var enhancedUsage = com.create("X509Enrollment.CX509ExtensionEnhancedKeyUsage");
    enhancedUsage.InitializeEncode(purposes);

    var name = com.create("X509Enrollment.CAlternativeName");
    name.InitializeFromString(3, hostName); // a DNS name
    var names = com.create("X509Enrollment.CAlternativeNames");
    names.Add(name);
    var alternativeNames = com.create("X509Enrollment.CX509ExtensionAlternativeNames");
    alternativeNames.InitializeEncode(names);

    var notACa = com.create("X509Enrollment.CX509ExtensionBasicConstraints");
    notACa.InitializeEncode(false, -1);
    notACa.Critical = true;

    var usage = com.create("X509Enrollment.CX509ExtensionKeyUsage");
    usage.InitializeEncode(0x80 + 0x20); // digital signature, key encipherment
    usage.Critical = true;

    foreach (var extension in [enhancedUsage, alternativeNames, notACa, usage]) {
        request.X509Extensions.Add(extension);
    }
    var sha256 = com.create("X509Enrollment.CObjectId");
    sha256.InitializeFromAlgorithmName(1, 0, 0, "SHA256"); // a hash algorithm, by its name
    request.HashAlgorithm = sha256;
    request.Encode();

    var enrollment = com.create("X509Enrollment.CX509Enrollment");
    enrollment.InitializeFromRequest(request);
    enrollment.CertificateFriendlyName = $"{hostName} (WebLingo, this machine only)";
    var base64 = enrollment.CreateRequest(1);
    enrollment.InstallResponse(2, base64, 1, ""); // its own issuer, so untrusted until trust() adds it to the roots

    var hash = com.create("X509Enrollment.CCertPropertySHA1Hash");
    hash.InitializeFromCertificate(machine, 1, base64);
    var thumbprint = hash.SHA1Hash(12).replace(" ", "").trim().toUpper(); // in hexadecimal digits
    return new Certificate(thumbprint, base64);
}

// Adds a certificate of the machine's personal store (Cert:\LocalMachine\My), without its private key, to its trusted
// roots (Cert:\LocalMachine\Root), so that this machine's browsers accept it, unless it is there; whether it added it.
// certutil copies it out to a file in the temporary folder, removed after, and adds that.
export async function trust(thumbprint) {
    if ((await run($"certutil -store Root {thumbprint}")).code == 0) {
        return false;
    }
    var file = expand($"%TEMP%\\{thumbprint}.cer");
    try {
        await runOrStop($"certutil -store My {thumbprint} \"{file}\"");
        await runOrStop($"certutil -addstore Root \"{file}\"");
    } finally {
        if (await fs.exists(file)) {
            await fs.remove(file);
        }
    }
    return true;
}

// The thumbprint of the certificate http.sys gives a host name on HTTPS's port, 443, by SNI; null where it gives none.
export async function boundCertificate(hostName) {
    var ran = await run($"netsh http show sslcert hostnameport={hostName}:443");
    var hash = new Regex("\\b[0-9a-fA-F]{40}\\b").match(ran.output);
    return ran.code == 0 && hash != null ? hash[0].toUpper() : null;
}
