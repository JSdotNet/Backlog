import * as os from 'node:os';
import * as vscode from 'vscode';

import { InboxItem, SyncClient, SyncResult } from './sync-client';

const pairCommand = 'backlog.pair';

function cloudUrl(): string | undefined {
    const configured = vscode.workspace.getConfiguration('backlog').get<string>('cloudUrl', '').trim();
    if (configured.length === 0) {
        vscode.window.showWarningMessage(
            'Backlog: set "backlog.cloudUrl" to the cloud resource URL shown in the Aspire dashboard.');
        return undefined;
    }
    return configured.replace(/\/+$/, '');
}

/** Says what went wrong with an inbox call. Both pairing outcomes offer the
 * pair command, so a 401 is never reported as "cloud sync unavailable". */
function reportFailure(result: Exclude<SyncResult<unknown>, { status: 'ok' }>, failurePrefix: string): void {
    const pair = 'Pair with Desktop';
    const offerPairing = (message: string) => {
        vscode.window.showWarningMessage(message, pair).then(choice => {
            if (choice === pair) {
                vscode.commands.executeCommand(pairCommand);
            }
        });
    };

    switch (result.status) {
        case 'notPaired':
            offerPairing('Backlog: this editor is not paired yet. Pair it with a code from the desktop\'s Devices settings.');
            return;
        case 'credentialRejected':
            offerPairing('Backlog: the sync service no longer accepts this editor\'s pairing. Pair it again.');
            return;
        case 'unavailable':
            vscode.window.showWarningMessage(`Backlog: ${failurePrefix} (${result.reason}).`);
            return;
    }
}

class InboxProvider implements vscode.TreeDataProvider<InboxItem> {
    private readonly changed = new vscode.EventEmitter<void>();
    readonly onDidChangeTreeData = this.changed.event;

    constructor(private readonly client: SyncClient) {}

    refresh(): void {
        this.changed.fire();
    }

    getTreeItem(item: InboxItem): vscode.TreeItem {
        const node = new vscode.TreeItem(item.title, vscode.TreeItemCollapsibleState.None);
        node.description = item.source;
        node.tooltip = new vscode.MarkdownString(`**${item.title}**\n\nCaptured ${item.capturedAt}`);
        return node;
    }

    async getChildren(): Promise<InboxItem[]> {
        const url = cloudUrl();
        if (!url) {
            return [];
        }

        const result = await this.client.listInbox(url);
        if (result.status === 'ok') {
            return result.value;
        }
        reportFailure(result, 'cloud sync unavailable');
        return [];
    }
}

async function capture(client: SyncClient): Promise<boolean> {
    const url = cloudUrl();
    if (!url) {
        return false;
    }

    // Asked before the title box, so nobody types a title that has nowhere to go.
    if (!(await client.isPaired())) {
        reportFailure({ status: 'notPaired' }, 'capture failed');
        return false;
    }

    const selection = vscode.window.activeTextEditor?.document.getText(
        vscode.window.activeTextEditor.selection);

    const title = await vscode.window.showInputBox({
        prompt: 'Capture to Backlog inbox',
        value: selection?.trim().split('\n')[0] ?? ''
    });

    if (!title) {
        return false;
    }

    const result = await client.capture(url, title);
    if (result.status === 'ok') {
        vscode.window.showInformationMessage(`Backlog: captured "${title}".`);
        return true;
    }
    reportFailure(result, 'capture failed');
    return false;
}

async function pair(client: SyncClient): Promise<boolean> {
    const url = cloudUrl();
    if (!url) {
        return false;
    }

    const code = await vscode.window.showInputBox({
        prompt: 'Pairing code from the desktop app (Settings > Devices > Let another device in)',
        ignoreFocusOut: true
    });

    if (!code) {
        return false;
    }

    const result = await client.pair(url, code, `${vscode.env.appName} on ${os.hostname()}`);
    if (result.status === 'paired') {
        vscode.window.showInformationMessage('Backlog: this editor is paired with your desktop.');
        return true;
    }
    vscode.window.showErrorMessage(`Backlog: pairing failed (${result.reason}).`);
    return false;
}

export function activate(context: vscode.ExtensionContext): void {
    // The credential lives in the editor's secret storage (the OS credential
    // store), never in settings or globalState.
    const client = new SyncClient({
        fetch: (url, init) => fetch(url, init),
        secrets: context.secrets
    });
    const provider = new InboxProvider(client);

    context.subscriptions.push(
        vscode.window.registerTreeDataProvider('backlogInbox', provider),
        vscode.commands.registerCommand('backlog.refreshInbox', () => provider.refresh()),
        vscode.commands.registerCommand('backlog.capture', async () => {
            if (await capture(client)) {
                provider.refresh();
            }
        }),
        vscode.commands.registerCommand(pairCommand, async () => {
            if (await pair(client)) {
                provider.refresh();
            }
        }),
        vscode.commands.registerCommand('backlog.unpair', async () => {
            await client.unpair();
            provider.refresh();
            vscode.window.showInformationMessage('Backlog: this editor\'s pairing is forgotten.');
        })
    );
}

export function deactivate(): void {
    // no-op
}
