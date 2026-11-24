const vscode = require('vscode');
const fs = require('fs');
const path = require('path');

// Each debugger: its type, the language of the files it runs, the name of a launch it makes for the file open in the
// editor, the setting that names its executable and the executable without it, and what it debugs.
const debuggers = [
    { type: 'weblingo', language: 'weblingo', name: 'Debug WebLingo Script', setting: 'debuggerPath', executable: 'weblingo',
      what: 'local WebLingo scripts' },
    { type: 'wasp', language: 'wasp', name: 'Debug WASP Page', setting: 'waspDebuggerPath', executable: 'wasp',
      what: 'local WASP pages' }
];

// A page's site: the nearest folder above it, within its workspace folder, that has a site's configuration, wasp.config or
// web.config; none, and the adapter takes the page's own folder, as wasp run does.
function siteRoot(page, folder) {
    if (!folder) return undefined;
    const top = path.resolve(folder.uri.fsPath);
    for (let at = path.dirname(path.resolve(page)); ; at = path.dirname(at)) {
        if (fs.existsSync(path.join(at, 'wasp.config')) || fs.existsSync(path.join(at, 'web.config'))) return at;
        if (at === top || path.dirname(at) === at || !at.startsWith(top)) return undefined;
    }
}

function activate(context) {
    for (const debug of debuggers) {
        context.subscriptions.push(
            vscode.debug.registerDebugConfigurationProvider(debug.type, {
                resolveDebugConfiguration(folder, configuration) {
                    if (configuration.type || configuration.request || configuration.name) return configuration;
                    const editor = vscode.window.activeTextEditor;
                    if (!editor || editor.document.languageId !== debug.language || editor.document.uri.scheme !== 'file') {
                        return undefined;
                    }
                    const program = editor.document.uri.fsPath;
                    const launch = { type: debug.type, request: 'launch', name: debug.name, program };
                    if (debug.type === 'wasp') {
                        const root = siteRoot(program, vscode.workspace.getWorkspaceFolder(editor.document.uri) ?? folder);
                        if (root) launch.root = root;
                    }
                    return launch;
                }
            }),
            vscode.debug.registerDebugAdapterDescriptorFactory(debug.type, {
                createDebugAdapterDescriptor(session) {
                    if (!vscode.workspace.isTrusted) {
                        throw new Error(`Trust this workspace to debug ${debug.what}.`);
                    }
                    const configuration = vscode.workspace.getConfiguration('weblingo', session.workspaceFolder?.uri);
                    const executable = configuration.get(debug.setting) || debug.executable;
                    return new vscode.DebugAdapterExecutable(executable, ['--debug-adapter']);
                }
            })
        );
    }
}

module.exports = { activate };
