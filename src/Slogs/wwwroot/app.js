(() => {
    if (window.__slogsCardClickBound) {
        return;
    }

    window.__slogsCardClickBound = true;

    const interactiveSelector = [
        "a",
        "button",
        "input",
        "textarea",
        "select",
        "label",
        "summary",
        "[role='button']",
        "[contenteditable='true']",
        "[data-card-click-ignore]"
    ].join(",");

    const getElement = (target) => target instanceof Element ? target : target?.parentElement;

    const isInteractiveTarget = (target) => {
        const element = getElement(target);
        return Boolean(element?.closest(interactiveSelector));
    };

    const navigateToCard = (card) => {
        const href = card?.getAttribute("data-card-click-href");
        if (!href) {
            return;
        }

        const destination = new URL(href, window.location.href);
        if (destination.origin === window.location.origin && window.Blazor?.navigateTo) {
            window.Blazor.navigateTo(`${destination.pathname}${destination.search}${destination.hash}`);
            return;
        }

        window.location.assign(destination.href);
    };

    document.addEventListener("click", (event) => {
        if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
            return;
        }

        if (window.getSelection()?.toString()) {
            return;
        }

        const element = getElement(event.target);
        const card = element?.closest(".clickable-post-card[data-card-click-href]");
        if (!card || isInteractiveTarget(event.target)) {
            return;
        }

        navigateToCard(card);
    });

    document.addEventListener("keydown", (event) => {
        if (event.defaultPrevented || (event.key !== "Enter" && event.key !== " ")) {
            return;
        }

        const element = getElement(event.target);
        const card = element?.closest(".clickable-post-card[data-card-click-href]");
        if (!card || isInteractiveTarget(event.target)) {
            return;
        }

        event.preventDefault();
        navigateToCard(card);
    });
})();

window.slogsMarkdownEditor = (() => {
    const vditorVersion = "3.11.2";
    const vditorCdn = `https://unpkg.com/vditor@${vditorVersion}`;
    const editors = new Map();
    let loadPromise;

    const loadVditor = () => {
        if (window.Vditor) {
            return Promise.resolve();
        }

        if (loadPromise) {
            return loadPromise;
        }

        loadPromise = new Promise((resolve, reject) => {
            const stylesheetId = "slogs-vditor-css";
            if (!document.getElementById(stylesheetId)) {
                const link = document.createElement("link");
                link.id = stylesheetId;
                link.rel = "stylesheet";
                link.href = `${vditorCdn}/dist/index.css`;
                document.head.appendChild(link);
            }

            const script = document.createElement("script");
            script.src = `${vditorCdn}/dist/index.min.js`;
            script.async = true;
            script.onload = () => resolve();
            script.onerror = () => reject(new Error("Vditor failed to load"));
            document.head.appendChild(script);
        });

        return loadPromise;
    };

    const createFallbackEditor = (host, dotNetReference, value, options) => {
        const textarea = document.createElement("textarea");
        textarea.className = "slogs-markdown-editor__fallback";
        textarea.placeholder = options.placeholder ?? "";
        textarea.value = value ?? "";
        textarea.style.minHeight = `${options.minHeight ?? 360}px`;
        textarea.addEventListener("input", () => {
            dotNetReference.invokeMethodAsync("OnMarkdownChanged", textarea.value);
        });
        host.replaceChildren(textarea);
        return { fallback: textarea };
    };

    const init = async (id, dotNetReference, value, options = {}) => {
        const host = document.getElementById(id);
        if (!host) {
            return;
        }

        dispose(id);

        try {
            await loadVditor();
        } catch {
            editors.set(id, createFallbackEditor(host, dotNetReference, value, options));
            return;
        }

        let editor;
        editor = new window.Vditor(id, {
            mode: "ir",
            value: value ?? "",
            cdn: vditorCdn,
            lang: "ko_KR",
            theme: "classic",
            icon: "material",
            minHeight: options.minHeight ?? 360,
            cache: {
                enable: false
            },
            counter: {
                enable: true,
                type: "markdown"
            },
            placeholder: options.placeholder ?? "",
            toolbar: [
                "headings",
                "bold",
                "italic",
                "strike",
                "|",
                "quote",
                "list",
                "ordered-list",
                "check",
                "|",
                "code",
                "inline-code",
                "link",
                "table",
                "|",
                "undo",
                "redo"
            ],
            input: (markdown) => {
                dotNetReference.invokeMethodAsync("OnMarkdownChanged", markdown);
            },
            after: () => {
                dotNetReference.invokeMethodAsync("OnMarkdownChanged", editor.getValue());
            }
        });

        editors.set(id, { editor });
    };

    const setValue = (id, value) => {
        const instance = editors.get(id);
        const nextValue = value ?? "";

        if (instance?.editor && instance.editor.getValue() !== nextValue) {
            instance.editor.setValue(nextValue);
        }

        if (instance?.fallback && instance.fallback.value !== nextValue) {
            instance.fallback.value = nextValue;
        }
    };

    const dispose = (id) => {
        const instance = editors.get(id);
        if (instance?.editor) {
            instance.editor.destroy();
        }
        editors.delete(id);
    };

    return {
        init,
        setValue,
        dispose
    };
})();
