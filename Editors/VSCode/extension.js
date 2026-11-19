const vscode = require('vscode');

function activate(context) {
    context.subscriptions.push(
        vscode.debug.registerDebugConfigurationProvider('weblingo', {
            resolveDebugConfiguration(_folder, configuration) {
                if (configuration.type || configuration.request || configuration.name) return configuration;
                const editor = vscode.window.activeTextEditor;
                if (!editor || editor.document.languageId !== 'weblingo' || editor.document.uri.scheme !== 'file') {
                    return undefined;
                }
                return {
                    type: 'weblingo',
                    request: 'launch',
                    name: 'Debug WebLingo Script',
                    program: editor.document.uri.fsPath
                };
            }
        }),
        vscode.debug.registerDebugAdapterDescriptorFactory('weblingo', {
            createDebugAdapterDescriptor(session) {
                if (!vscode.workspace.isTrusted) {
                    throw new Error('Trust this workspace to debug local WebLingo scripts.');
                }
                const configuration = vscode.workspace.getConfiguration('weblingo', session.workspaceFolder?.uri);
                const executable = configuration.get('debuggerPath') || 'weblingo';
                return new vscode.DebugAdapterExecutable(executable, ['--debug-adapter']);
            }
        })
    );
}

module.exports = { activate };
