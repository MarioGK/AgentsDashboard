const terminals = new Map();

export function initTerminal(elementId, sessionId, dotNetRef) {
    const container = document.getElementById(elementId);
    if (!container) return;

    const term = new Terminal({
        cursorBlink: true,
        cursorStyle: 'block',
        fontSize: 14,
        fontFamily: "'Cascadia Code', 'Fira Code', 'JetBrains Mono', 'Consolas', monospace",
        theme: {
            background: '#1e1e1e',
            foreground: '#d4d4d4',
            cursor: '#d4d4d4',
            selectionBackground: '#264f78',
            black: '#1e1e1e',
            red: '#f44747',
            green: '#6a9955',
            yellow: '#dcdcaa',
            blue: '#569cd6',
            magenta: '#c586c0',
            cyan: '#4ec9b0',
            white: '#d4d4d4',
            brightBlack: '#808080',
            brightRed: '#f44747',
            brightGreen: '#6a9955',
            brightYellow: '#dcdcaa',
            brightBlue: '#9cdcfe',
            brightMagenta: '#c586c0',
            brightCyan: '#4ec9b0',
            brightWhite: '#ffffff',
        },
        scrollback: 10000,
        convertEol: true,
        allowProposedApi: true,
    });

    const fitAddon = new FitAddon.FitAddon();
    term.loadAddon(fitAddon);

    term.open(container);

    try {
        fitAddon.fit();
    } catch { }

    term.onData((data) => {
        const encoded = btoa(unescape(encodeURIComponent(data)));
        dotNetRef.invokeMethodAsync('OnTerminalData', sessionId, encoded);
    });

    term.onResize(({ cols, rows }) => {
        dotNetRef.invokeMethodAsync('OnTerminalResize', sessionId, cols, rows);
    });

    const resizeObserver = new ResizeObserver(() => {
        try {
            fitAddon.fit();
        } catch { }
    });
    resizeObserver.observe(container);

    terminals.set(elementId, { term, fitAddon, resizeObserver, sessionId });

    term.write('\x1b[1;34mConnecting to terminal session...\x1b[0m\r\n');
}

export function writeTerminal(elementId, base64Data) {
    const entry = terminals.get(elementId);
    if (!entry) return;

    try {
        const decoded = decodeURIComponent(escape(atob(base64Data)));
        entry.term.write(decoded);
    } catch {
        const bytes = Uint8Array.from(atob(base64Data), c => c.charCodeAt(0));
        entry.term.write(bytes);
    }
}

export function resizeTerminal(elementId) {
    const entry = terminals.get(elementId);
    if (!entry) return;

    try {
        entry.fitAddon.fit();
    } catch { }
}

export function focusTerminal(elementId) {
    const entry = terminals.get(elementId);
    if (!entry) return;

    entry.term.focus();
}

export function disposeTerminal(elementId) {
    const entry = terminals.get(elementId);
    if (!entry) return;

    try {
        entry.resizeObserver.disconnect();
    } catch { }

    try {
        entry.term.dispose();
    } catch { }

    terminals.delete(elementId);
}
