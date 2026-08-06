import * as vscode from 'vscode';

interface InboxItem {
    id: string;
    title: string;
    source: string;
    capturedAt: string;
}

function cloudUrl(): string {
    return vscode.workspace.getConfiguration('backlog').get<string>('cloudUrl', 'http://localhost:15310');
}

class InboxProvider implements vscode.TreeDataProvider<InboxItem> {
    private readonly changed = new vscode.EventEmitter<void>();
    readonly onDidChangeTreeData = this.changed.event;

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
        try {
            const response = await fetch(`${cloudUrl()}/api/sync/inbox`);
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }
            return (await response.json()) as InboxItem[];
        } catch (error) {
            vscode.window.showWarningMessage(`Backlog: cloud sync unavailable (${error}).`);
            return [];
        }
    }
}

async function capture(): Promise<void> {
    const selection = vscode.window.activeTextEditor?.document.getText(
        vscode.window.activeTextEditor.selection);

    const title = await vscode.window.showInputBox({
        prompt: 'Capture to Backlog inbox',
        value: selection?.trim().split('\n')[0] ?? ''
    });

    if (!title) {
        return;
    }

    try {
        const response = await fetch(`${cloudUrl()}/api/sync/inbox`, {
            method: 'POST',
            headers: { 'content-type': 'application/json' },
            body: JSON.stringify({ title, source: 'vscode' })
        });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
        vscode.window.showInformationMessage(`Backlog: captured "${title}".`);
    } catch (error) {
        vscode.window.showErrorMessage(`Backlog: capture failed (${error}).`);
    }
}

export function activate(context: vscode.ExtensionContext): void {
    const provider = new InboxProvider();

    context.subscriptions.push(
        vscode.window.registerTreeDataProvider('backlogInbox', provider),
        vscode.commands.registerCommand('backlog.refreshInbox', () => provider.refresh()),
        vscode.commands.registerCommand('backlog.capture', async () => {
            await capture();
            provider.refresh();
        })
    );
}

export function deactivate(): void {
    // no-op
}
