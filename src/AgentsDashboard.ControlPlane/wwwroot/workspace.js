const viewportListeners = new Map();
const composerBridges = new Map();

let viewportCounter = 0;
let composerCounter = 0;

export function getViewportHeight() {
    return window.innerHeight || document.documentElement.clientHeight || 0;
}

export function getInputSelection(elementId) {
    if (!elementId) {
        return [0, 0];
    }

    const element = document.getElementById(elementId);
    if (!element) {
        return [0, 0];
    }

    const start = typeof element.selectionStart === "number" ? element.selectionStart : 0;
    const end = typeof element.selectionEnd === "number" ? element.selectionEnd : start;
    return [start, end];
}

export function registerViewportListener(dotNetRef) {
    if (!dotNetRef) {
        return null;
    }

    const id = `viewport-${++viewportCounter}`;
    const entry = {
        frame: 0,
        handler: null
    };

    entry.handler = () => {
        if (entry.frame !== 0) {
            window.cancelAnimationFrame(entry.frame);
        }

        entry.frame = window.requestAnimationFrame(() => {
            entry.frame = 0;
            dotNetRef.invokeMethodAsync("OnWorkspaceViewportChanged", getViewportHeight());
        });
    };

    window.addEventListener("resize", entry.handler);
    viewportListeners.set(id, entry);

    return id;
}

export function unregisterViewportListener(id) {
    const entry = viewportListeners.get(id);
    if (!entry) {
        return;
    }

    window.removeEventListener("resize", entry.handler);

    if (entry.frame !== 0) {
        window.cancelAnimationFrame(entry.frame);
    }

    viewportListeners.delete(id);
}

export function registerComposerKeyBridge(elementId, dotNetRef) {
    if (!elementId || !dotNetRef) {
        return null;
    }

    const element = document.getElementById(elementId);
    if (!element) {
        return null;
    }

    const id = `composer-${++composerCounter}`;
    const handler = async event => {
        if (event.key !== "Tab" && event.key !== "ArrowRight") {
            return;
        }

        const selectionStart = typeof element.selectionStart === "number" ? element.selectionStart : element.value.length;
        const selectionEnd = typeof element.selectionEnd === "number" ? element.selectionEnd : element.value.length;

        const accepted = await dotNetRef.invokeMethodAsync(
            "TryAcceptGhostSuggestionFromJs",
            event.key,
            selectionStart,
            selectionEnd);

        if (accepted) {
            event.preventDefault();
        }
    };

    element.addEventListener("keydown", handler);
    composerBridges.set(id, { element, handler });

    return id;
}

export function unregisterComposerKeyBridge(id) {
    const entry = composerBridges.get(id);
    if (!entry) {
        return;
    }

    entry.element.removeEventListener("keydown", entry.handler);
    composerBridges.delete(id);
}
