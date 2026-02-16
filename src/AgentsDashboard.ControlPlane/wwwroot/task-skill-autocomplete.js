window.agentsDashboardSkillAutocomplete = window.agentsDashboardSkillAutocomplete || {};

(function () {
    let providerRegistered = false;
    let providerDisposable = null;
    const editorBindings = new Map();

    function getEditor(editorId) {
        if (!window.blazorMonaco?.editor?.getEditor) {
            return null;
        }

        try {
            return window.blazorMonaco.editor.getEditor(editorId, true);
        } catch {
            return null;
        }
    }

    function normalizeTrigger(trigger) {
        return String(trigger ?? "")
            .trim()
            .replace(/^\/+/, "")
            .toLowerCase();
    }

    function normalizeSkills(skills) {
        if (!Array.isArray(skills)) {
            return [];
        }

        return skills
            .map((skill) => {
                const trigger = normalizeTrigger(skill?.trigger);
                return {
                    trigger: trigger,
                    content: String(skill?.content ?? ""),
                    description: String(skill?.description ?? ""),
                    scopeLabel: String(skill?.scopeLabel ?? ""),
                    scopePriority: Number.isFinite(Number(skill?.scopePriority))
                        ? Number(skill.scopePriority)
                        : 99,
                    enabled: Boolean(skill?.enabled ?? true)
                };
            })
            .filter((skill) => skill.trigger.length > 0 && skill.content.length > 0 && skill.enabled);
    }

    function ensureProvider() {
        if (providerRegistered) {
            return true;
        }

        if (!window.monaco?.languages?.registerCompletionItemProvider) {
            return false;
        }

        providerDisposable = window.monaco.languages.registerCompletionItemProvider("markdown", {
            triggerCharacters: ["/"],
            provideCompletionItems: (model, position) => {
                const modelUri = model?.uri?.toString();
                if (!modelUri) {
                    return { suggestions: [] };
                }

                const binding = Array.from(editorBindings.values())
                    .find((entry) => entry.modelUri === modelUri);
                if (!binding || !Array.isArray(binding.skills) || binding.skills.length === 0) {
                    return { suggestions: [] };
                }

                const lineText = model.getLineContent(position.lineNumber);
                const beforeCursor = lineText.slice(0, position.column - 1);
                const match = /(^|\s)\/([a-z0-9-]*)$/i.exec(beforeCursor);
                if (!match) {
                    return { suggestions: [] };
                }

                const partial = (match[2] ?? "").toLowerCase();
                const slashIndex = beforeCursor.length - partial.length - 1;
                if (slashIndex < 0) {
                    return { suggestions: [] };
                }

                const startColumn = slashIndex + 1;
                const endColumn = position.column;
                const range = new window.monaco.Range(
                    position.lineNumber,
                    startColumn,
                    position.lineNumber,
                    endColumn
                );

                const suggestions = binding.skills
                    .filter((skill) => skill.trigger.startsWith(partial))
                    .sort((left, right) => {
                        if (left.scopePriority !== right.scopePriority) {
                            return left.scopePriority - right.scopePriority;
                        }

                        return left.trigger.localeCompare(right.trigger);
                    })
                    .map((skill, index) => ({
                        label: `/${skill.trigger}`,
                        kind: window.monaco.languages.CompletionItemKind.Snippet,
                        insertText: skill.content,
                        range: range,
                        detail: skill.scopeLabel,
                        documentation: skill.description || undefined,
                        sortText: `${String(skill.scopePriority).padStart(2, "0")}-${skill.trigger}-${String(index).padStart(4, "0")}`
                    }));

                return { suggestions: suggestions };
            }
        });

        providerRegistered = true;
        return true;
    }

    window.agentsDashboardSkillAutocomplete.bindTaskPromptEditor = function (editorId, skills) {
        const editor = getEditor(editorId);
        if (!editor) {
            return false;
        }

        if (!ensureProvider()) {
            return false;
        }

        const model = editor.getModel();
        if (!model?.uri) {
            return false;
        }

        editorBindings.set(editorId, {
            modelUri: model.uri.toString(),
            skills: normalizeSkills(skills)
        });

        return true;
    };

    window.agentsDashboardSkillAutocomplete.updateEditorSkills = function (editorId, skills) {
        if (!editorBindings.has(editorId)) {
            return window.agentsDashboardSkillAutocomplete.bindTaskPromptEditor(editorId, skills);
        }

        const binding = editorBindings.get(editorId);
        binding.skills = normalizeSkills(skills);
        editorBindings.set(editorId, binding);
        return true;
    };

    window.agentsDashboardSkillAutocomplete.unbindTaskPromptEditor = function (editorId) {
        editorBindings.delete(editorId);
    };

    window.agentsDashboardSkillAutocomplete.dispose = function () {
        editorBindings.clear();
        if (providerDisposable?.dispose) {
            providerDisposable.dispose();
        }

        providerDisposable = null;
        providerRegistered = false;
    };
})();
