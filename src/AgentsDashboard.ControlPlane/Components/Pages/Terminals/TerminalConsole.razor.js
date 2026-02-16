const terminals = new Map();
const xtermScriptUrl = '/lib/xterm/xterm.min.js';
const fitAddonScriptUrl = '/lib/xterm/addon-fit.min.js';
const xtermEsmUrl = 'https://cdn.jsdelivr.net/npm/@xterm/xterm@5.5.0/+esm';
const fitAddonEsmUrl = 'https://cdn.jsdelivr.net/npm/@xterm/addon-fit@0.10.0/+esm';
let xtermLoadPromise;
let esmTerminalCtor;
let esmFitAddonCtor;

function resolveTerminalCtor() {
    if (typeof esmTerminalCtor === 'function') return esmTerminalCtor;
    if (typeof globalThis.Terminal === 'function') return globalThis.Terminal;
    if (typeof globalThis.Xterm?.Terminal === 'function') return globalThis.Xterm.Terminal;
    return null;
}

function resolveFitAddonCtor() {
    if (typeof esmFitAddonCtor === 'function') return esmFitAddonCtor;
    if (typeof globalThis.FitAddon?.FitAddon === 'function') return globalThis.FitAddon.FitAddon;
    if (typeof globalThis.FitAddon === 'function') return globalThis.FitAddon;
    return null;
}

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const existing = Array.from(document.querySelectorAll('script')).find((s) => s.src === src);
        if (existing) {
            resolve();
            return;
        }
        const script = document.createElement('script');
        script.src = src;
        script.async = true;
        script.onload = () => resolve();
        script.onerror = () => reject(new Error(`Failed to load script: ${src}`));
        document.head.appendChild(script);
    });
}

async function ensureXtermLoaded() {
    if (resolveTerminalCtor() && resolveFitAddonCtor()) return;

    xtermLoadPromise ??= (async () => {
        await loadScript(xtermScriptUrl);
        await loadScript(fitAddonScriptUrl);

        if (resolveTerminalCtor() && resolveFitAddonCtor()) return;

        const [xtermModule, fitAddonModule] = await Promise.all([
            import(xtermEsmUrl),
            import(fitAddonEsmUrl)
        ]);

        esmTerminalCtor = xtermModule.Terminal ?? xtermModule.default?.Terminal ?? xtermModule.default;
        esmFitAddonCtor = fitAddonModule.FitAddon ?? fitAddonModule.default?.FitAddon ?? fitAddonModule.default;
    })();

    await xtermLoadPromise;
}

export async function initTerminal(elementId, sessionId, dotNetRef) {
    const container = document.getElementById(elementId);
    if (!container) return;

    await ensureXtermLoaded();

    const TerminalCtor = resolveTerminalCtor();
    const FitAddonCtor = resolveFitAddonCtor();
    if (!TerminalCtor) throw new Error('xterm.js failed to load: Terminal constructor is unavailable.');
    if (!FitAddonCtor) throw new Error('xterm.js fit addon failed to load: FitAddon constructor is unavailable.');

    const term = new TerminalCtor({
        cursorBlink: true,
        cursorStyle: 'block',
        fontSize: 14,
        fontFamily: "'Cascadia Code', 'Fira Code', 'JetBrains Mono', 'Consolas', monospace",
        theme: {
            background: '#07090d',
            foreground: '#e6edf8',
            cursor: '#7cc4ff',
            cursorAccent: '#050607',
            selectionBackground: '#2f81f755',
            selectionInactiveBackground: '#2f81f733',
            black: '#0c1117',
            red: '#ff6b6b',
            green: '#38d39f',
            yellow: '#ffd166',
            blue: '#6bc1ff',
            magenta: '#c084fc',
            cyan: '#2dd4bf',
            white: '#d9e4f5',
            brightBlack: '#4b5b73',
            brightRed: '#ff8b8b',
            brightGreen: '#5cf2b5',
            brightYellow: '#ffe08a',
            brightBlue: '#93d7ff',
            brightMagenta: '#d8b4fe',
            brightCyan: '#67e8f9',
            brightWhite: '#ffffff',
        },
        scrollback: 10000,
        convertEol: true,
        allowProposedApi: true,
    });

    const fitAddon = new FitAddonCtor();
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
