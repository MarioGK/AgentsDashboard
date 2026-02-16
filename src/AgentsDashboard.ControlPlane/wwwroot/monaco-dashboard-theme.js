(function () {
    window.agentsDashboardMonacoThemeInitialized = false;

    window.agentsDashboardInitializeMonacoTheme = () => {
        if (window.agentsDashboardMonacoThemeInitialized) {
            return;
        }

        if (typeof window.require !== "function") {
            return;
        }

        window.require(["vs/editor/editor.main"], () => {
            if (window.agentsDashboardMonacoThemeInitialized || window.monaco?.editor?.defineTheme === undefined) {
                return;
            }

            window.monaco.editor.defineTheme("agents-dashboard-dark", {
                base: "vs-dark",
                inherit: true,
                rules: [
                    { token: "comment", foreground: "5F6F88", fontStyle: "italic" },
                    { token: "string", foreground: "6FE7B8" },
                    { token: "keyword", foreground: "9A8CFF" },
                    { token: "number", foreground: "9CCBFF" },
                    { token: "type", foreground: "4CE0C4" }
                ],
                colors: {
                    "editor.background": "#07090D",
                    "editor.foreground": "#E1E8F2",
                    "editorLineNumber.foreground": "#5F6F88",
                    "editor.selectionBackground": "#1F3755",
                    "editor.selectionHighlightBackground": "#263B5D66",
                    "editor.lineHighlightBackground": "#0F1623",
                    "editorCursor.foreground": "#7CC4FF",
                    "editorWhitespace.foreground": "#2E3D54",
                    "editorIndentGuide.background": "#1D2A3C",
                    "editorIndentGuide.activeBackground": "#38506D",
                    "editorGutter.background": "#07090D",
                    "editorLineNumber.activeForeground": "#8BD0FF",
                    "scrollbar.shadow": "#07090D",
                    "scrollbarSlider.background": "#2A3950",
                    "scrollbarSlider.hoverBackground": "#3E5B86",
                    "scrollbarSlider.activeBackground": "#6694CC"
                }
            });

            window.agentsDashboardMonacoThemeInitialized = true;
        });
    };

    window.agentsDashboardInitializeMonacoTheme();
})();
