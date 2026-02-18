const viewportListeners = new Map();
const composerBridges = new Map();
const composerImagePasteBridges = new Map();

let viewportCounter = 0;
let composerCounter = 0;
let composerImagePasteCounter = 0;

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

export function registerComposerImagePasteBridge(elementId, dotNetRef, bridgeKey) {
    if (!elementId || !dotNetRef) {
        return null;
    }

    const element = document.getElementById(elementId);
    if (!element) {
        return null;
    }

    const id = `composer-image-paste-${++composerImagePasteCounter}`;
    const handler = async event => {
        const clipboard = event.clipboardData;
        if (!clipboard?.items) {
            return;
        }

        const files = [];
        for (const item of clipboard.items) {
            if (item.kind !== "file") {
                continue;
            }

            if (!item.type || !item.type.startsWith("image/")) {
                continue;
            }

            const file = item.getAsFile();
            if (file) {
                files.push(file);
            }
        }

        if (files.length === 0) {
            return;
        }

        event.preventDefault();

        const images = [];
        for (const file of files) {
            try {
                images.push(await readClipboardImage(file));
            } catch {
            }
        }

        if (images.length === 0) {
            return;
        }

        await dotNetRef.invokeMethodAsync("OnComposerImagesPastedFromJs", bridgeKey || "", images);
    };

    element.addEventListener("paste", handler);
    composerImagePasteBridges.set(id, { element, handler });
    return id;
}

export function unregisterComposerImagePasteBridge(id) {
    const entry = composerImagePasteBridges.get(id);
    if (!entry) {
        return;
    }

    entry.element.removeEventListener("paste", entry.handler);
    composerImagePasteBridges.delete(id);
}

async function readClipboardImage(file) {
    const dataUrl = await readFileAsDataUrl(file);
    const dimensions = await readImageDimensions(dataUrl);
    return {
        id: createImageId(),
        fileName: file.name || "pasted-image",
        mimeType: file.type || "image/png",
        sizeBytes: file.size || 0,
        dataUrl,
        width: dimensions.width,
        height: dimensions.height
    };
}

function readFileAsDataUrl(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result?.toString() || "");
        reader.onerror = () => reject(reader.error || new Error("Failed to read clipboard image"));
        reader.readAsDataURL(file);
    });
}

function readImageDimensions(dataUrl) {
    return new Promise(resolve => {
        const image = new Image();
        image.onload = () => resolve({
            width: image.naturalWidth || null,
            height: image.naturalHeight || null
        });
        image.onerror = () => resolve({ width: null, height: null });
        image.src = dataUrl;
    });
}

function createImageId() {
    if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
        return crypto.randomUUID();
    }

    return `${Date.now()}-${Math.floor(Math.random() * 1000000)}`;
}
