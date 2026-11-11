// What schedule-run-report.mjs decides, kept free of Node imports so a test can drive it with
// plain strings (local ADR 0051 §1).
//
// A run a Claude Desktop schedule starts opens with `<scheduled-task name="<taskId>" file="…">`;
// the name is the task's id in the scheduler tool, and the id Backlog keys the schedule on.

export const DEFAULT_PORT = 5757;
export const ROUTE = "/schedule-runs";

// The task id the prompt's opening `<scheduled-task name="…">` tag carries, or null when the
// prompt does not open with one — a prompt that merely mentions the tag is no scheduled run.
export function scheduledTaskName(prompt) {
    if (typeof prompt !== "string") return null;
    const match = prompt.trimStart().match(/^<scheduled-task\b[^>]*?\bname="([^"]+)"/);
    return match ? match[1] : null;
}

// The text of one transcript entry's message content: a string, or the text blocks of an array.
function textOf(content) {
    if (typeof content === "string") return content;
    if (!Array.isArray(content)) return null;
    if (content.some((block) => block && block.type === "tool_result")) return null;
    const text = content
        .filter((block) => block && block.type === "text" && typeof block.text === "string")
        .map((block) => block.text)
        .join("\n");
    return text || null;
}

// The session's first prompt, from transcript entries read in order: the first queued prompt
// (Claude Desktop writes it as a `queue-operation` `enqueue` before SessionStart fires) or the
// first user message that is neither meta nor a tool result. Null while there is none.
export function firstPrompt(entries) {
    for (const entry of entries) {
        if (!entry || typeof entry !== "object") continue;
        if (entry.type === "queue-operation" && entry.operation === "enqueue" && typeof entry.content === "string") {
            return entry.content;
        }
        if (entry.type === "user" && !entry.isMeta && !entry.isSidechain) {
            const text = textOf(entry.message && entry.message.content);
            if (text !== null) return text;
        }
    }
    return null;
}

// The session's last assistant message, from transcript entries read in order: the text blocks
// of the last assistant message that has any, joined across the entries that share its id.
export function lastAssistantText(entries) {
    let id;
    let parts = [];
    for (const entry of entries) {
        if (!entry || entry.type !== "assistant" || entry.isSidechain || !entry.message) continue;
        const text = textOf(entry.message.content);
        if (text === null) continue;
        const messageId = entry.message.id;
        if (messageId && messageId === id) parts.push(text);
        else {
            id = messageId;
            parts = [text];
        }
    }
    return parts.length ? parts.join("\n") : null;
}

// What the session is told at its start: look its schedule up once and report `started`.
export function startContext(scheduleId, sessionId) {
    return [
        `This session is a run of the scheduled task \`${scheduleId}\`. Before any other work, report its start to Backlog once:`,
        `1. Look up this task's own entry once with the scheduler tool (\`list_scheduled_tasks\`, the entry whose taskId is \`${scheduleId}\`): its title and its cronExpression.`,
        `2. Call the backlog MCP server's \`report_schedule_run\` with scheduleId \`${scheduleId}\`, sessionId \`${sessionId}\`, event \`started\`, title the entry's title, cron its cronExpression, and repository the owner/name it runs in — the part of its title before \` · \`, else this folder's git remote.`,
        "If the scheduler tool or report_schedule_run is not available or refuses, go on with the run without retrying. Do not report the finish yourself: a hook reports it when this session stops.",
    ].join("\n");
}

const isHeading = (line) => /^\s{0,3}#{1,6}\s/.test(line);
const isSeparator = (line) => /^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$/.test(line);
const cellsOf = (line) =>
    line
        .trim()
        .replace(/^\|/, "")
        .replace(/\|$/, "")
        .split("|")
        .map((cell) => cell.trim());

// A line that only labels what follows, such as `**Notes:**`, is no verdict.
const isLabel = (text) => /:\**$/.test(text);

// The report's verdict line: its bold sentence, else its first line of prose.
export function verdictLine(message) {
    const lines = String(message).split(/\r?\n/);
    for (const line of lines) {
        const bold = line.trim().match(/^\*\*([^*]+)\*\*/);
        if (bold && !isLabel(bold[1].trim())) return bold[1].trim();
    }
    for (const line of lines) {
        const text = line.trim();
        if (!text || isHeading(line) || text.startsWith("|") || /^[-*_]{3,}$/.test(text) || isLabel(text)) continue;
        return text.replace(/^(?:[-*+]|\d+\.)\s+/, "").replaceAll("**", "");
    }
    return null;
}

// True when a person prompted the session after its scheduled prompt: a later user message of
// text that is no tool result, no meta entry and no tag the host wraps its own notices in. The
// run's report is its last message before that; a reply to it is no new finish.
export function promptedAfterStart(entries) {
    let seen = 0;
    for (const entry of entries) {
        if (!entry || entry.type !== "user" || entry.isMeta || entry.isSidechain) continue;
        const text = textOf(entry.message && entry.message.content);
        if (text === null) continue;
        seen += 1;
        if (seen > 1 && !text.trimStart().startsWith("<")) return true;
    }
    return false;
}

// The rows under the report's *Needs you* heading — every `### Needs you…` section at the level
// of the first, a table's header and separator rows dropped — each as its cells joined by ` · `,
// or a bullet's text. A deeper one is a nested report's own, which a bundle's top section
// already gathers.
export function needsYouRows(message) {
    const rows = [];
    let inSection = false;
    let level = null;
    let table = [];
    const flush = () => {
        // A table's first row is its header when the separator follows it.
        const body = table.length > 1 && isSeparator(table[1]) ? table.slice(2) : table.filter((row) => !isSeparator(row));
        for (const row of body) {
            const cells = cellsOf(row).filter((cell) => cell && cell !== "—" && cell !== "-");
            if (cells.length) rows.push(cells.join(" · "));
        }
        table = [];
    };
    for (const line of String(message).split(/\r?\n/)) {
        if (isHeading(line)) {
            flush();
            const needsYou = line.match(/^\s{0,3}(#{1,6})\s+\*{0,2}Needs you\b/i);
            if (needsYou && level === null) level = needsYou[1].length;
            inSection = Boolean(needsYou) && needsYou[1].length === level;
            continue;
        }
        if (!inSection) continue;
        const text = line.trim();
        if (text.startsWith("|")) {
            table.push(text);
            continue;
        }
        flush();
        const bullet = text.match(/^(?:[-*+]|\d+\.)\s+(.+)$/);
        if (bullet) rows.push(bullet[1].trim());
    }
    flush();
    return rows;
}

// The finish report a final message makes: `skipped` when it opens with `Skipped:`, else
// `finished` with its verdict line and its Needs-you rows (none sent as an empty list, so a
// later report clears an earlier one's).
export function finishReport(scheduleId, sessionId, message) {
    const text = typeof message === "string" ? message.trim() : "";
    if (text.startsWith("Skipped:")) {
        return { scheduleId, sessionId, event: "skipped", verdict: text.split(/\r?\n/)[0].trim() };
    }
    return {
        scheduleId,
        sessionId,
        event: "finished",
        verdict: text ? verdictLine(text) : null,
        needsYou: text ? needsYouRows(text) : [],
    };
}

// Where the app keeps settings.json: .NET's LocalApplicationData, then the Backlog folder —
// the same place telemetry-forwarder.mjs reads.
export function settingsPath(env, platform, home) {
    const sep = platform === "win32" ? "\\" : "/";
    const join = (...parts) => parts.map((part, i) => (i === 0 ? part.replace(/[\\/]+$/, "") : part)).join(sep);
    const localData =
        platform === "win32"
            ? env.LOCALAPPDATA || join(home, "AppData", "Local")
            : platform === "darwin"
              ? join(home, "Library", "Application Support")
              : env.XDG_DATA_HOME || join(home, ".local", "share");
    return join(localData, "Backlog", "settings.json");
}

// BACKLOG_MCP_PORT / BACKLOG_MCP_TOKEN first, then the app's settings.json text (null when it
// could not be read), then the app's default port.
export function endpointFrom(env, settingsText) {
    let port = Number(env.BACKLOG_MCP_PORT) || null;
    let token = env.BACKLOG_MCP_TOKEN || null;
    if ((!port || !token) && settingsText) {
        try {
            const mcp = JSON.parse(settingsText).mcpServer || {};
            port ||= Number(mcp.port) || null;
            token ||= mcp.token || null;
        } catch {
            // A half-written settings.json reads as none.
        }
    }
    return { url: `http://127.0.0.1:${port || DEFAULT_PORT}${ROUTE}`, token };
}
