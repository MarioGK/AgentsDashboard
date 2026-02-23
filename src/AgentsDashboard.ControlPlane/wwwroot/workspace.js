const viewportListeners = new Map();
const composerBridges = new Map();
const composerImagePasteBridges = new Map();
const chatAutoScrollControllers = new Map();

let viewportCounter = 0;
let composerCounter = 0;
let composerImagePasteCounter = 0;
let chatAutoScrollCounter = 0;

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

export function autoSizeTextarea(elementId, maxHeightPx) {
    if (!elementId) {
        return;
    }

    const element = document.getElementById(elementId);
    if (!element) {
        return;
    }

    const maxHeight = Number.isFinite(maxHeightPx) && maxHeightPx > 0 ? maxHeightPx : 240;
    element.style.height = "0px";
    const desiredHeight = Math.min(element.scrollHeight, maxHeight);
    element.style.height = `${desiredHeight}px`;
    element.style.overflowY = element.scrollHeight > maxHeight ? "auto" : "hidden";
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
        if (event.isComposing) {
            return;
        }

        if (event.key === "Tab" || event.key === "ArrowRight") {
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

            return;
        }

        if (event.key === "Enter" && !event.shiftKey) {
            const submitted = await dotNetRef.invokeMethodAsync("TrySubmitComposerFromJs");
            if (submitted) {
                event.preventDefault();
            }
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

export function registerChatAutoScroll(elementId, dotNetRef) {
    if (!elementId || !dotNetRef) {
        return null;
    }

    const element = document.getElementById(elementId);
    if (!element) {
        return null;
    }

    const id = `chat-autoscroll-${++chatAutoScrollCounter}`;
    const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const state = {
        sticky: true,
        pending: 0,
        animationFrame: 0,
        suppressScrollEvent: false
    };

    const atBottomThreshold = 72;

    const cancelAnimation = () => {
        if (state.animationFrame !== 0) {
            window.cancelAnimationFrame(state.animationFrame);
            state.animationFrame = 0;
        }
    };

    const distanceFromBottom = () => Math.max(0, element.scrollHeight - element.clientHeight - element.scrollTop);
    const isNearBottom = () => distanceFromBottom() <= atBottomThreshold;

    const notify = () => {
        dotNetRef
            .invokeMethodAsync("OnChatAutoScrollStateChanged", state.sticky, state.pending)
            .catch(() => {});
    };

    const setSticky = sticky => {
        if (state.sticky === sticky) {
            return;
        }

        state.sticky = sticky;
        notify();
    };

    const setPending = pending => {
        if (state.pending === pending) {
            return;
        }

        state.pending = pending;
        notify();
    };

    const easeOutCubic = value => 1 - Math.pow(1 - value, 3);

    const animateToBottom = force => {
        const target = Math.max(0, element.scrollHeight - element.clientHeight);
        const start = element.scrollTop;
        const distance = target - start;
        if (Math.abs(distance) < 2) {
            element.scrollTop = target;
            return;
        }

        if (prefersReducedMotion) {
            element.scrollTop = target;
            return;
        }

        cancelAnimation();
        const duration = force
            ? Math.min(220, Math.max(140, Math.abs(distance) * 0.12))
            : Math.min(180, Math.max(120, Math.abs(distance) * 0.1));
        const startTime = performance.now();
        state.suppressScrollEvent = true;

        const step = now => {
            const elapsed = now - startTime;
            const progress = Math.min(1, elapsed / duration);
            const eased = easeOutCubic(progress);
            element.scrollTop = start + (distance * eased);
            if (progress >= 1) {
                state.animationFrame = 0;
                state.suppressScrollEvent = false;
                return;
            }

            state.animationFrame = window.requestAnimationFrame(step);
        };

        state.animationFrame = window.requestAnimationFrame(step);
    };

    const onContentChanged = () => {
        if (state.sticky || isNearBottom()) {
            setSticky(true);
            setPending(0);
            animateToBottom(false);
            return;
        }

        setPending(state.pending + 1);
    };

    const onScroll = () => {
        if (state.suppressScrollEvent) {
            return;
        }

        if (isNearBottom()) {
            setSticky(true);
            setPending(0);
            return;
        }

        setSticky(false);
    };

    const mutationObserver = new MutationObserver(onContentChanged);
    mutationObserver.observe(element, { childList: true, subtree: true, characterData: true });

    const resizeObserver = new ResizeObserver(onContentChanged);
    resizeObserver.observe(element);

    element.addEventListener("scroll", onScroll, { passive: true });

    const jumpToLatest = () => {
        setSticky(true);
        setPending(0);
        animateToBottom(true);
    };

    const controller = {
        element,
        mutationObserver,
        resizeObserver,
        onScroll,
        jumpToLatest,
        dispose: () => {
            cancelAnimation();
            element.removeEventListener("scroll", onScroll);
            mutationObserver.disconnect();
            resizeObserver.disconnect();
        }
    };

    chatAutoScrollControllers.set(id, controller);
    jumpToLatest();
    notify();

    return id;
}

export function unregisterChatAutoScroll(id) {
    const controller = chatAutoScrollControllers.get(id);
    if (!controller) {
        return;
    }

    controller.dispose();
    chatAutoScrollControllers.delete(id);
}

export function scrollChatToLatest(id) {
    const controller = chatAutoScrollControllers.get(id);
    if (!controller) {
        return;
    }

    controller.jumpToLatest();
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
