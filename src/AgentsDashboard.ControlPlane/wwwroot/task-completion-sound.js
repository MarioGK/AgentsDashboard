window.agentsDashboard = window.agentsDashboard || {};

(function () {
    const soundEngineVersion = "1.3.0";

    const soundProfiles = {
        modern: {
            label: "Modern",
            description: "Bright layered arpeggios with soft attack for a contemporary finish.",
            tones: {
                succeeded: [
                    { frequency: 587.33, duration: 0.09, delay: 0, gain: 0.16, attack: 0.005, decay: 0.09, type: "sine" },
                    { frequency: 739.99, duration: 0.08, delay: 0.06, gain: 0.14, attack: 0.004, decay: 0.08, type: "triangle" },
                    { frequency: 880.0, duration: 0.11, delay: 0.12, gain: 0.16, attack: 0.004, decay: 0.11, type: "sine" },
                    { frequency: 1046.50, duration: 0.09, delay: 0.22, gain: 0.13, attack: 0.004, decay: 0.08, type: "triangle" },
                ],
                failed: [
                    { frequency: 196.0, duration: 0.11, delay: 0, gain: 0.12, attack: 0.01, decay: 0.14, type: "triangle" },
                    { frequency: 155.56, duration: 0.11, delay: 0.08, gain: 0.1, attack: 0.01, decay: 0.13, type: "square" },
                    { frequency: 130.81, duration: 0.12, delay: 0.16, gain: 0.08, attack: 0.01, decay: 0.13, type: "sawtooth" },
                ],
                cancelled: [
                    { frequency: 523.25, duration: 0.07, delay: 0, gain: 0.09, attack: 0.007, decay: 0.08, type: "triangle" },
                    { frequency: 493.88, duration: 0.07, delay: 0.07, gain: 0.08, attack: 0.007, decay: 0.07, type: "triangle" },
                    { frequency: 440.0, duration: 0.08, delay: 0.14, gain: 0.07, attack: 0.007, decay: 0.07, type: "sine" },
                ],
            },
        },
        ambient: {
            label: "Ambient",
            description: "Softer, rounded tones that linger for a calmer finish.",
            tones: {
                succeeded: [
                    { frequency: 698.46, duration: 0.08, delay: 0, gain: 0.12, attack: 0.006, decay: 0.08, type: "triangle" },
                    { frequency: 783.99, duration: 0.08, delay: 0.07, gain: 0.12, attack: 0.006, decay: 0.08, type: "triangle" },
                    { frequency: 1046.50, duration: 0.09, delay: 0.14, gain: 0.11, attack: 0.006, decay: 0.09, type: "triangle" },
                    { frequency: 1174.66, duration: 0.09, delay: 0.22, gain: 0.1, attack: 0.006, decay: 0.09, type: "triangle" },
                ],
                failed: [
                    { frequency: 174.61, duration: 0.11, delay: 0, gain: 0.1, attack: 0.01, decay: 0.13, type: "triangle" },
                    { frequency: 130.81, duration: 0.11, delay: 0.09, gain: 0.09, attack: 0.01, decay: 0.13, type: "triangle" },
                ],
                cancelled: [
                    { frequency: 415.30, duration: 0.08, delay: 0, gain: 0.07, attack: 0.01, decay: 0.08, type: "sine" },
                    { frequency: 349.23, duration: 0.08, delay: 0.08, gain: 0.06, attack: 0.01, decay: 0.08, type: "sine" },
                ],
            },
        },
        clean: {
            label: "Clean",
            description: "Tight, focused tones with immediate attack for clear status changes.",
            tones: {
                succeeded: [
                    { frequency: 659.25, duration: 0.06, delay: 0, gain: 0.14, attack: 0.003, decay: 0.06, type: "sine" },
                    { frequency: 783.99, duration: 0.06, delay: 0.06, gain: 0.1, attack: 0.003, decay: 0.06, type: "sine" },
                    { frequency: 1046.50, duration: 0.07, delay: 0.12, gain: 0.08, attack: 0.003, decay: 0.06, type: "triangle" },
                ],
                failed: [
                    { frequency: 233.08, duration: 0.08, delay: 0, gain: 0.1, attack: 0.006, decay: 0.08, type: "square" },
                    { frequency: 246.94, duration: 0.07, delay: 0.07, gain: 0.09, attack: 0.007, decay: 0.07, type: "square" },
                    { frequency: 293.66, duration: 0.07, delay: 0.13, gain: 0.08, attack: 0.007, decay: 0.07, type: "triangle" },
                ],
                cancelled: [
                    { frequency: 466.16, duration: 0.05, delay: 0, gain: 0.08, attack: 0.005, decay: 0.06, type: "sine" },
                    { frequency: 440.0, duration: 0.05, delay: 0.06, gain: 0.07, attack: 0.005, decay: 0.06, type: "sine" },
                ],
            },
        },
        sparkle: {
            label: "Sparkle",
            description: "A crisp notification style with playful, bright harmonic motion.",
            tones: {
                succeeded: [
                    { frequency: 554.37, duration: 0.06, delay: 0, gain: 0.13, attack: 0.004, decay: 0.06, type: "triangle" },
                    { frequency: 698.46, duration: 0.06, delay: 0.06, gain: 0.11, attack: 0.004, decay: 0.06, type: "triangle" },
                    { frequency: 880.00, duration: 0.07, delay: 0.12, gain: 0.11, attack: 0.004, decay: 0.07, type: "triangle" },
                    { frequency: 1046.50, duration: 0.07, delay: 0.18, gain: 0.09, attack: 0.004, decay: 0.07, type: "triangle" },
                ],
                failed: [
                    { frequency: 261.63, duration: 0.08, delay: 0, gain: 0.1, attack: 0.007, decay: 0.08, type: "sawtooth" },
                    { frequency: 329.63, duration: 0.08, delay: 0.08, gain: 0.08, attack: 0.007, decay: 0.08, type: "triangle" },
                    { frequency: 311.13, duration: 0.08, delay: 0.16, gain: 0.07, attack: 0.007, decay: 0.08, type: "triangle" },
                ],
                cancelled: [
                    { frequency: 415.30, duration: 0.06, delay: 0, gain: 0.07, attack: 0.006, decay: 0.06, type: "sine" },
                    { frequency: 392.0, duration: 0.06, delay: 0.07, gain: 0.06, attack: 0.006, decay: 0.06, type: "sine" },
                    { frequency: 349.23, duration: 0.06, delay: 0.14, gain: 0.06, attack: 0.006, decay: 0.06, type: "triangle" },
                ],
            },
        },
        mixkit: {
            label: "Mixkit Minimal",
            description: "Minimal, modern notification tones from your selected Mixkit sounds.",
            tones: {
                succeeded: [
                    { url: "/sounds/mixkit-message-pop-alert-2354.mp3" }
                ],
                failed: [
                    { url: "/sounds/mixkit-digital-quick-tone-2866.mp3" },
                    { url: "/sounds/mixkit-double-beep-tone-alert-2868.mp3" },
                    { url: "/sounds/mixkit-elevator-tone-2863.mp3" }
                ],
                cancelled: [
                    { url: "/sounds/mixkit-double-beep-tone-alert-2868.mp3" }
                ],
            },
        },
    };

    const defaultSettings = {
        enabled: true,
        volume: 0.65,
        selectedProfile: "mixkit",
        version: soundEngineVersion,
        playSucceeded: true,
        playFailed: true,
        playCancelled: true
    };

    const storageKey = "agentsDashboard.runCompletionAudioSettings";
    let settings = { ...defaultSettings };
    let settingsInfo;
    let settingsLoaded = false;
    const fileToneCursor = {
        succeeded: 0,
        failed: 0,
        cancelled: 0
    };

    let audioContext;

    async function getAudioContextAsync() {
        if (audioContext) {
            return ensureAudioContext(audioContext);
        }

        const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
        if (!AudioContextCtor) {
            return null;
        }

        audioContext = new AudioContextCtor();
        return ensureAudioContext(audioContext);
    }

    async function ensureAudioContext(context) {
        if (context.state === "suspended") {
            try {
                await context.resume();
            } catch {
                return context.state === "running" ? context : null;
            }
        }

        return context.state === "running" ? context : null;
    }

    function clamp(value, min, max) {
        return Math.max(min, Math.min(max, value));
    }

    function isAudioTone(tone) {
        return typeof tone?.url === "string" && tone.url.trim().length > 0;
    }

    function nextAudioTone(state, toneSequence) {
        const count = Number.isInteger(fileToneCursor[state]) ? fileToneCursor[state] : 0;
        const index = count % toneSequence.length;
        fileToneCursor[state] = count + 1;
        return toneSequence[index] ?? null;
    }

    async function waitMs(ms) {
        return new Promise((resolve) => setTimeout(resolve, ms));
    }

    async function playAudioTone(tone, stateVolume) {
        if (!isAudioTone(tone)) {
            return false;
        }

        const delayMs = Math.max(0, Number(tone.delay ?? 0) * 1000);
        if (delayMs > 0) {
            await waitMs(delayMs);
        }

        const audio = new Audio(tone.url);
        audio.volume = clamp(Number(stateVolume ?? 0.65), 0, 1);
        await audio.play();
        return true;
    }

    function normalizeState(state) {
        return String(state ?? "").toLowerCase();
    }

    function normalizeProfile(profile) {
        const normalized = String(profile ?? "").toLowerCase();
        return soundProfiles[normalized] ? normalized : null;
    }

    function loadSettingsInfo() {
        if (settingsInfo) {
            return settingsInfo;
        }

        settingsInfo = {
            version: soundEngineVersion,
            profiles: Object.entries(soundProfiles).map(([id, profile]) => ({
                id,
                label: profile.label || id,
                description: profile.description || ""
            }))
        };

        return settingsInfo;
    }

    function isEnabledState(key) {
        switch (key) {
            case "succeeded":
                return settings.playSucceeded;
            case "failed":
                return settings.playFailed;
            case "cancelled":
                return settings.playCancelled;
            default:
                return false;
        }
    }

    function loadSettings() {
        if (settingsLoaded) {
            return settings;
        }

        try {
            const serialized = localStorage.getItem(storageKey);
            if (!serialized) {
                settingsLoaded = true;
                return settings;
            }

            const loaded = JSON.parse(serialized);
            const normalizedProfile = normalizeProfile(loaded?.selectedProfile);
            const loadedVolume = Number.isFinite(Number(loaded?.volume)) ? clamp(Number(loaded.volume), 0, 1) : defaultSettings.volume;
            settings = {
                ...defaultSettings,
                ...loaded,
                version: soundEngineVersion,
                volume: loadedVolume,
                selectedProfile: normalizedProfile || defaultSettings.selectedProfile
            };
        } catch {
            settings = { ...defaultSettings };
        }

        settings.volume = clamp(settings.volume, 0, 1);
        settingsLoaded = true;
        return settings;
    }

    function saveSettings(nextSettings) {
        const normalizedProfile = normalizeProfile(nextSettings?.selectedProfile);
        settings = {
            ...defaultSettings,
            ...settings,
            ...nextSettings,
            volume: clamp(Number(nextSettings?.volume ?? settings.volume), 0, 1),
            selectedProfile: normalizedProfile || settings.selectedProfile,
            version: soundEngineVersion
        };

        localStorage.setItem(storageKey, JSON.stringify(settings));
        return settings;
    }

    function withSettings(newSettings) {
        settings = { ...settings, ...newSettings };
        return settings;
    }

    function playTone(context, tone, startTime, masterVolume) {
        const oscillator = context.createOscillator();
        const gainNode = context.createGain();

        oscillator.type = tone.type || "sine";
        oscillator.frequency.setValueAtTime(tone.frequency, startTime);
        oscillator.connect(gainNode);
        gainNode.connect(context.destination);

        const attack = clamp(tone.attack ?? 0.008, 0.001, 0.05);
        const decay = clamp(tone.decay ?? 0.12, 0.02, 0.3);
        const peak = clamp((tone.gain ?? 0.12) * masterVolume, 0.001, 0.4);
        const end = startTime + (tone.duration ?? 0.12);

        gainNode.gain.setValueAtTime(0.0001, startTime);
        gainNode.gain.exponentialRampToValueAtTime(peak, startTime + attack);
        gainNode.gain.exponentialRampToValueAtTime(0.0001, end + decay);

        oscillator.start(startTime);
        oscillator.stop(end + decay + 0.02);
    }

    async function playSoundSequence(state, customSettings) {
        const normalized = normalizeState(state);
        const resolvedSettings = customSettings ? { ...settings, ...customSettings } : settings;

        if (!resolvedSettings.enabled || !isEnabledState(normalized)) {
            return;
        }

        const profileName = normalizeProfile(resolvedSettings.selectedProfile) || "modern";
        const profile = soundProfiles[profileName] || soundProfiles.modern;
        const toneSequence = profile.tones[normalized];
        if (!toneSequence || toneSequence.length === 0) {
            return;
        }

        const volume = clamp(Number(resolvedSettings.volume ?? resolvedSettings.level ?? 0.65), 0, 1);
        if (toneSequence.some(isAudioTone)) {
            const selectedTone = nextAudioTone(normalized, [...toneSequence]);
            if (selectedTone) {
                try {
                    await playAudioTone(selectedTone, volume);
                    return;
                } catch {
                }
            }
        }

        const context = await getAudioContextAsync();
        if (!context) {
            return;
        }

        const now = context.currentTime;
        for (const tone of toneSequence) {
            if (isAudioTone(tone)) {
                continue;
            }

            const startTime = now + (tone.delay ?? 0);
            playTone(context, tone, startTime, volume);
        }
    }

    window.agentsDashboard.playRunCompletedSound = async function (runState) {
        try {
            await playSoundSequence(runState, loadSettings());
        } catch {
        }
    };

    window.agentsDashboard.testRunCompletionSound = async function (runState, runSettings) {
        try {
            const previewSettings = runSettings ? { ...loadSettings(), ...runSettings } : loadSettings();
            await playSoundSequence(runState, previewSettings);
            return true;
        } catch {
            return false;
        }
    };

    window.agentsDashboard.getRunCompletionAudioSettings = function () {
        return loadSettings();
    };

    window.agentsDashboard.setRunCompletionAudioSettings = function (newSettings) {
        try {
            return saveSettings(newSettings || {});
        } catch {
            return loadSettings();
        }
    };

    window.agentsDashboard.getRunCompletionAudioInfo = function () {
        return loadSettingsInfo();
    };
})();
