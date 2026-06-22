(() => {
    if (window.__slogsInteractivityBound) {
        return;
    }

    window.__slogsInteractivityBound = true;

    const pendingClass = "slogs-interactivity-pending";
    const restoringClass = "slogs-interactivity-restoring";
    const readyClass = "slogs-interactivity-ready";
    const guardedSelector = [
        "button",
        "form",
        "input",
        "textarea",
        "select",
        "[role='button']",
        "[contenteditable='true']"
    ].join(",");

    const getElement = (target) => target instanceof Element ? target : target?.parentElement;

    const isWaiting = () => document.body?.classList.contains(pendingClass)
        || document.body?.classList.contains(restoringClass);

    const isReconnectControl = (element) => Boolean(element?.closest("#components-reconnect-modal"));

    const findGuardedElement = (target) => getElement(target)?.closest(guardedSelector);

    const blockIfWaiting = (event) => {
        if (!isWaiting()) {
            return;
        }

        const guardedElement = findGuardedElement(event.target);
        if (!guardedElement || isReconnectControl(guardedElement)) {
            return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
    };

    document.addEventListener("pointerdown", blockIfWaiting, true);
    document.addEventListener("click", blockIfWaiting, true);
    document.addEventListener("submit", blockIfWaiting, true);
    document.addEventListener("keydown", (event) => {
        if (event.key === "Tab" || event.key === "Escape") {
            return;
        }

        blockIfWaiting(event);
    }, true);

    window.slogsInteractivity = {
        markReady: () => {
            document.body?.classList.remove(pendingClass, restoringClass);
            document.body?.classList.add(readyClass);
        },
        markConnecting: () => {
            document.body?.classList.remove(readyClass);
            document.body?.classList.add(restoringClass);
        }
    };
})();

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

(() => {
    if (window.__slogsAccountMenuBound) {
        return;
    }

    window.__slogsAccountMenuBound = true;

    const getElement = (target) => target instanceof Element ? target : target?.parentElement;

    const closeAccountMenus = (exceptMenu) => {
        document.querySelectorAll(".slogs-account-menu[open]").forEach((menu) => {
            if (menu !== exceptMenu) {
                menu.removeAttribute("open");
            }
        });
    };

    document.addEventListener("click", (event) => {
        const element = getElement(event.target);
        const selectedMenuItem = element?.closest(".slogs-account-menu [role='menuitem']");
        if (selectedMenuItem) {
            closeAccountMenus();
            return;
        }

        const accountMenu = element?.closest(".slogs-account-menu");
        if (accountMenu) {
            closeAccountMenus(accountMenu);
            return;
        }

        closeAccountMenus();
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            closeAccountMenus();
        }
    });

    window.slogsAccountMenu = {
        closeAll: () => closeAccountMenus()
    };
})();

window.slogsMobileHeader = (() => {
    const collapsedClass = "slogs-mobile-header-collapsed";
    const mediaQuery = window.matchMedia("(max-width: 1279px)");
    let initialized = false;
    let lastScrollY = 0;
    let ticking = false;

    const getScrollY = () => Math.max(0, window.scrollY || window.pageYOffset || 0);

    const isMobileMenuOpen = () => Boolean(document.querySelector(".slogs-mobile-menu-drawer.is-open"));

    const setCollapsed = (collapsed) => {
        document.body.classList.toggle(collapsedClass, collapsed);
    };

    const update = () => {
        ticking = false;
        const header = document.querySelector("[data-slogs-mobile-header]");
        const currentY = getScrollY();

        if (!header || !mediaQuery.matches || isMobileMenuOpen()) {
            setCollapsed(false);
            lastScrollY = currentY;
            return;
        }

        const delta = currentY - lastScrollY;
        if (currentY < 64) {
            setCollapsed(false);
        } else if (delta > 8) {
            setCollapsed(true);
        } else if (delta < -8) {
            setCollapsed(false);
        }

        lastScrollY = currentY;
    };

    const requestUpdate = () => {
        if (ticking) {
            return;
        }

        ticking = true;
        window.requestAnimationFrame(update);
    };

    const init = () => {
        if (initialized) {
            requestUpdate();
            return;
        }

        initialized = true;
        lastScrollY = getScrollY();
        window.addEventListener("scroll", requestUpdate, { passive: true });
        window.addEventListener("resize", requestUpdate);
        mediaQuery.addEventListener?.("change", requestUpdate);
        requestUpdate();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init, { once: true });
    } else {
        init();
    }

    return {
        init
    };
})();

window.slogsInfiniteScroll = (() => {
    const registrations = new WeakMap();

    const isNearViewport = (element) => {
        if (!element?.getBoundingClientRect) {
            return false;
        }

        const rect = element.getBoundingClientRect();
        return rect.top <= window.innerHeight + 900 && rect.bottom >= -900;
    };

    const unobserve = (element) => {
        const registration = registrations.get(element);
        registration?.observer?.disconnect();
        if (registration?.scrollListener) {
            window.removeEventListener("scroll", registration.scrollListener);
            window.removeEventListener("resize", registration.scrollListener);
        }

        registrations.delete(element);
    };

    const observe = (element, dotNetReference) => {
        if (!element || !dotNetReference) {
            return;
        }

        unobserve(element);

        let pending = false;
        const trigger = () => {
            if (pending || !isNearViewport(element)) {
                return;
            }

            pending = true;
            let loadedMore = false;
            dotNetReference.invokeMethodAsync("LoadMorePostsAsync")
                .then((result) => {
                    loadedMore = result === true;
                })
                .catch(() => {
                    loadedMore = false;
                })
                .finally(() => {
                    pending = false;
                    if (loadedMore && isNearViewport(element)) {
                        window.setTimeout(trigger, 120);
                    }
                });
        };

        if (window.IntersectionObserver) {
            const observer = new IntersectionObserver((entries) => {
                if (entries.some((entry) => entry.isIntersecting)) {
                    trigger();
                }
            }, {
                rootMargin: "900px 0px",
                threshold: 0
            });

            observer.observe(element);
            registrations.set(element, { observer });
            trigger();
            return;
        }

        const scrollListener = () => trigger();
        window.addEventListener("scroll", scrollListener, { passive: true });
        window.addEventListener("resize", scrollListener);
        registrations.set(element, { scrollListener });
        trigger();
    };

    return {
        observe,
        unobserve
    };
})();

window.slogsVditorRuntime = (() => {
    const version = "3.11.2";
    const cdn = `https://unpkg.com/vditor@${version}`;
    let loadPromise;
    const localizedCodeCopyControls = new WeakSet();
    const chromeTranslations = new Map([
        ["复制到公众号", "WeChat으로 복사"],
        ["复制到知乎", "Zhihu로 복사"]
    ]);

    const ensureStylesheet = () => {
        const stylesheetId = "slogs-vditor-css";
        if (document.getElementById(stylesheetId)) {
            return;
        }

        const link = document.createElement("link");
        link.id = stylesheetId;
        link.rel = "stylesheet";
        link.href = `${cdn}/dist/index.css`;
        document.head.appendChild(link);
    };

    const load = () => {
        ensureStylesheet();

        if (window.Vditor) {
            return Promise.resolve();
        }

        if (loadPromise) {
            return loadPromise;
        }

        loadPromise = new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = `${cdn}/dist/index.min.js`;
            script.async = true;
            script.onload = () => resolve();
            script.onerror = () => reject(new Error("Vditor failed to load"));
            document.head.appendChild(script);
        });

        return loadPromise;
    };

    const getCopyText = (copy) => {
        const textarea = copy.querySelector("textarea");
        if (textarea?.value) {
            return textarea.value;
        }

        const code = copy.closest("pre")?.querySelector("code")
            ?? copy.parentElement?.querySelector("code");

        return code?.textContent ?? "";
    };

    const writeClipboardText = async (text) => {
        if (navigator.clipboard?.writeText) {
            await navigator.clipboard.writeText(text);
            return;
        }

        const fallback = document.createElement("textarea");
        fallback.value = text;
        fallback.setAttribute("readonly", "");
        fallback.style.position = "fixed";
        fallback.style.left = "-9999px";
        fallback.style.top = "0";
        document.body.appendChild(fallback);

        try {
            fallback.select();
            if (!document.execCommand("copy")) {
                throw new Error("copy command failed");
            }
        } finally {
            fallback.remove();
        }
    };

    const shouldResetCopyLabel = (label) => !label || /复制|已复制|copy|copied/i.test(label);

    const localizeVditorChrome = (host) => {
        if (!host?.querySelectorAll) {
            return;
        }

        for (const element of host.querySelectorAll("[aria-label], [title]")) {
            const ariaLabel = element.getAttribute("aria-label");
            const title = element.getAttribute("title");

            if (chromeTranslations.has(ariaLabel)) {
                element.setAttribute("aria-label", chromeTranslations.get(ariaLabel));
            }

            if (chromeTranslations.has(title)) {
                element.setAttribute("title", chromeTranslations.get(title));
            }
        }
    };

    const localizeCodeCopyControls = (host) => {
        if (!host?.querySelectorAll) {
            return;
        }

        localizeVditorChrome(host);

        for (const copy of host.querySelectorAll(".vditor-copy")) {
            const control = copy.querySelector("span");
            if (!control) {
                continue;
            }

            control.removeAttribute("onmouseover");
            control.removeAttribute("onclick");

            if (shouldResetCopyLabel(control.getAttribute("aria-label") ?? "")) {
                control.setAttribute("aria-label", "복사");
            }

            if (localizedCodeCopyControls.has(control)) {
                continue;
            }

            localizedCodeCopyControls.add(control);

            const resetLabel = () => control.setAttribute("aria-label", "복사");
            control.addEventListener("mouseover", resetLabel);
            control.addEventListener("focus", resetLabel);
            control.addEventListener("click", async (event) => {
                event.preventDefault();
                event.stopPropagation();

                try {
                    await writeClipboardText(getCopyText(copy));
                    control.setAttribute("aria-label", "복사됨");
                } catch {
                    control.setAttribute("aria-label", "복사 실패");
                }
            });
        }
    };

    const observeCodeCopyControls = (host) => {
        localizeCodeCopyControls(host);

        if (!host || !window.MutationObserver) {
            return null;
        }

        const observer = new MutationObserver(() => localizeCodeCopyControls(host));
        observer.observe(host, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ["aria-label", "onclick", "onmouseover"]
        });

        return observer;
    };

    return {
        cdn,
        load,
        localizeCodeCopyControls,
        localizeVditorChrome,
        observeCodeCopyControls
    };
})();

window.slogsMarkdownEditor = (() => {
    const editors = new Map();
    const maxImageBytes = 5 * 1024 * 1024;
    const imageTypes = new Set(["image/png", "image/jpeg", "image/gif", "image/webp"]);

    const isSupportedImage = (file) => file && imageTypes.has(file.type) && file.size > 0 && file.size <= maxImageBytes;

    const getImageFiles = (itemsOrFiles) => Array.from(itemsOrFiles ?? [])
        .map((item) => typeof item.getAsFile === "function" ? item.getAsFile() : item)
        .filter(isSupportedImage);

    const markdownEscape = (value) => (value ?? "image")
        .replace(/[[\]\\]/g, "\\$&")
        .replace(/\r?\n/g, " ")
        .trim() || "image";

    const uploadImage = async (file) => {
        const formData = new FormData();
        formData.append("image", file, file.name || "pasted-image.png");

        const response = await fetch("/editor/images", {
            method: "POST",
            body: formData,
            credentials: "same-origin"
        });

        if (!response.ok) {
            let error = "이미지 삽입에 실패했습니다.";
            try {
                const body = await response.json();
                error = body.error || error;
            } catch {
            }

            throw new Error(error);
        }

        return await response.json();
    };

    const makeImageMarkdown = (uploadResult, file) => {
        const altText = markdownEscape(uploadResult.altText || file.name?.replace(/\.[^.]+$/, "") || "image");
        return `![${altText}](${uploadResult.url})`;
    };

    const setStatus = (host, message, kind = "info") => {
        let status = host.querySelector("[data-editor-image-status]");
        if (!status) {
            status = document.createElement("div");
            status.setAttribute("data-editor-image-status", "");
            status.setAttribute("aria-live", "polite");
            status.className = "slogs-markdown-editor__image-status";
            host.appendChild(status);
        }

        status.textContent = message;
        status.dataset.kind = kind;
        window.clearTimeout(status.__slogsStatusTimer);
        status.__slogsStatusTimer = window.setTimeout(() => {
            status.textContent = "";
            delete status.dataset.kind;
        }, kind === "error" ? 2600 : 1400);
    };

    const setDropActive = (host, active) => {
        host.classList.toggle("is-image-dragging", active);
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

    const insertIntoFallback = (textarea, markdown) => {
        const start = textarea.selectionStart ?? textarea.value.length;
        const end = textarea.selectionEnd ?? start;
        const prefix = textarea.value.slice(0, start);
        const suffix = textarea.value.slice(end);
        const needsLeadingBreak = prefix.length > 0 && !prefix.endsWith("\n");
        const needsTrailingBreak = suffix.length > 0 && !suffix.startsWith("\n");
        const snippet = `${needsLeadingBreak ? "\n\n" : ""}${markdown}${needsTrailingBreak ? "\n\n" : ""}`;
        textarea.value = `${prefix}${snippet}${suffix}`;
        const cursor = start + snippet.length;
        textarea.setSelectionRange(cursor, cursor);
        textarea.dispatchEvent(new Event("input", { bubbles: true }));
        textarea.focus();
    };

    const placeCaretFromPoint = (container, clientX, clientY) => {
        if (!Number.isFinite(clientX) || !Number.isFinite(clientY)) {
            return;
        }

        const range = document.caretRangeFromPoint
            ? document.caretRangeFromPoint(clientX, clientY)
            : document.caretPositionFromPoint
                ? (() => {
                    const position = document.caretPositionFromPoint(clientX, clientY);
                    if (!position) {
                        return null;
                    }

                    const nextRange = document.createRange();
                    nextRange.setStart(position.offsetNode, position.offset);
                    return nextRange;
                })()
                : null;

        if (!range || !container.contains(range.startContainer)) {
            return;
        }

        const selection = window.getSelection();
        selection?.removeAllRanges();
        selection?.addRange(range);
    };

    const insertIntoEditor = (editor, eventTarget, markdown, pointerEvent) => {
        if (pointerEvent) {
            placeCaretFromPoint(eventTarget, pointerEvent.clientX, pointerEvent.clientY);
        }

        editor.focus();
        const formatted = `\n\n${markdown}\n\n`;
        editor.insertValue(formatted);
    };

    const createImageInsertionHandlers = (host, eventTarget, getTarget, notifyChanged) => {
        let dragDepth = 0;
        let isUploading = false;

        const insertFiles = async (files, sourceEvent) => {
            if (isUploading || files.length === 0) {
                return;
            }

            isUploading = true;
            setStatus(host, "이미지 삽입 중...");

            try {
                for (const file of files) {
                    const uploadResult = await uploadImage(file);
                    const markdown = makeImageMarkdown(uploadResult, file);
                    const target = getTarget();

                    if (target?.editor) {
                        insertIntoEditor(target.editor, eventTarget, markdown, sourceEvent);
                    } else if (target?.fallback) {
                        insertIntoFallback(target.fallback, markdown);
                    }
                }

                notifyChanged();
                setStatus(host, "이미지 삽입 완료");
            } catch (error) {
                setStatus(host, error.message || "이미지 삽입에 실패했습니다.", "error");
            } finally {
                isUploading = false;
                dragDepth = 0;
                setDropActive(host, false);
            }
        };

        const paste = (event) => {
            const files = getImageFiles(event.clipboardData?.items);
            if (files.length === 0) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            void insertFiles(files, event);
        };

        const dragEnter = (event) => {
            if (getImageFiles(event.dataTransfer?.items).length === 0) {
                return;
            }

            dragDepth += 1;
            setDropActive(host, true);
        };

        const dragOver = (event) => {
            if (getImageFiles(event.dataTransfer?.items).length === 0) {
                return;
            }

            event.preventDefault();
            event.dataTransfer.dropEffect = "copy";
            setDropActive(host, true);
        };

        const dragLeave = () => {
            dragDepth = Math.max(0, dragDepth - 1);
            if (dragDepth === 0) {
                setDropActive(host, false);
            }
        };

        const drop = (event) => {
            const files = getImageFiles(event.dataTransfer?.files);
            if (files.length === 0) {
                setDropActive(host, false);
                dragDepth = 0;
                return;
            }

            event.preventDefault();
            event.stopPropagation();
            void insertFiles(files, event);
        };

        const listeners = [
            { eventName: "paste", listener: paste },
            { eventName: "dragenter", listener: dragEnter },
            { eventName: "dragover", listener: dragOver },
            { eventName: "dragleave", listener: dragLeave },
            { eventName: "drop", listener: drop }
        ];

        for (const { eventName, listener } of listeners) {
            eventTarget.addEventListener(eventName, listener, true);
        }

        return listeners;
    };

    const createChangeNotifier = (id, editor, dotNetReference, initialValue) => {
        let lastValue = initialValue ?? "";
        let timer;

        const notify = (markdown) => {
            const nextValue = markdown ?? "";
            if (nextValue === lastValue) {
                return;
            }

            lastValue = nextValue;
            window.clearTimeout(timer);
            timer = window.setTimeout(() => {
                const liveInstance = editors.get(id);
                if (liveInstance?.editor !== editor) {
                    return;
                }

                dotNetReference.invokeMethodAsync("OnMarkdownChanged", nextValue).catch(() => {
                    // The Blazor circuit can be gone during navigation.
                });
            }, 20);
        };

        const readAndNotify = () => {
            try {
                notify(editor.getValue());
            } catch {
                // Vditor can briefly report no current mode while reconciling IR state.
            }
        };

        return { notify, readAndNotify };
    };

    const init = async (id, dotNetReference, value, options = {}) => {
        const host = document.getElementById(id);
        if (!host) {
            return;
        }

        dispose(id);

        try {
            await window.slogsVditorRuntime.load();
        } catch {
            const fallbackInstance = createFallbackEditor(host, dotNetReference, value, options);
            const imageListeners = createImageInsertionHandlers(
                host,
                fallbackInstance.fallback,
                () => fallbackInstance,
                () => fallbackInstance.fallback.dispatchEvent(new Event("input", { bubbles: true })));
            editors.set(id, { ...fallbackInstance, imageEventTarget: fallbackInstance.fallback, imageListeners });
            return;
        }

        let editor;
        let changeNotifier;
        editor = new window.Vditor(id, {
            mode: "ir",
            value: value ?? "",
            cdn: window.slogsVditorRuntime.cdn,
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
                changeNotifier?.notify(markdown);
                window.setTimeout(() => window.slogsVditorRuntime.localizeCodeCopyControls(host), 0);
            },
            after: () => {
                changeNotifier?.readAndNotify();
                window.slogsVditorRuntime.localizeCodeCopyControls(host);
            }
        });

        changeNotifier = createChangeNotifier(id, editor, dotNetReference, value);
        const eventTarget = host.querySelector(".vditor-content") ?? host;
        const copyLocalizationObserver = window.slogsVditorRuntime.observeCodeCopyControls(host);
        const eventNames = ["input", "keyup", "paste", "compositionend"];
        const listeners = eventNames.map((eventName) => {
            const listener = () => changeNotifier.readAndNotify();
            eventTarget.addEventListener(eventName, listener, true);
            return { eventName, listener };
        });
        const imageListeners = createImageInsertionHandlers(
            host,
            eventTarget,
            () => editors.get(id),
            () => changeNotifier.readAndNotify());

        editors.set(id, { editor, eventTarget, listeners, imageEventTarget: eventTarget, imageListeners, copyLocalizationObserver });
    };

    const setValue = (id, value) => {
        const instance = editors.get(id);
        const nextValue = value ?? "";

        if (instance?.editor) {
            const applyEditorValue = () => {
                const liveInstance = editors.get(id);
                if (liveInstance?.editor !== instance.editor) {
                    return true;
                }

                try {
                    instance.editor.setValue(nextValue);
                    return true;
                } catch {
                    return false;
                }
            };

            try {
                if (instance.editor.getValue() === nextValue) {
                    return;
                }
            } catch {
                // Vditor can briefly report no current mode while it is reconciling IR state.
            }

            if (!applyEditorValue()) {
                window.setTimeout(applyEditorValue, 100);
            }
        }

        if (instance?.fallback && instance.fallback.value !== nextValue) {
            instance.fallback.value = nextValue;
        }
    };

    const dispose = (id) => {
        const instance = editors.get(id);
        if (instance?.eventTarget && instance.listeners) {
            for (const { eventName, listener } of instance.listeners) {
                instance.eventTarget.removeEventListener(eventName, listener, true);
            }
        }

        if (instance?.imageEventTarget && instance.imageListeners) {
            for (const { eventName, listener } of instance.imageListeners) {
                instance.imageEventTarget.removeEventListener(eventName, listener, true);
            }
        }

        instance?.copyLocalizationObserver?.disconnect();

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

window.slogsAuthApi = (() => {
    const sendJson = async (url, payload = {}, method = "POST") => {
        const response = await fetch(url, {
            method,
            headers: {
                "Content-Type": "application/json"
            },
            credentials: "same-origin",
            body: JSON.stringify(payload)
        });

        let body = null;
        try {
            body = await response.json();
        } catch {
        }

        return {
            Ok: response.ok,
            Status: response.status,
            Error: body?.error ?? null,
            ReturnUrl: body?.returnUrl ?? null,
            User: body?.user ?? null
        };
    };

    return {
        login: (request) => sendJson("/api/auth/login", request),
        register: (request) => sendJson("/api/auth/register", request),
        profile: (request) => sendJson("/api/auth/profile", request, "PUT"),
        enterAdminMode: () => sendJson("/api/auth/admin-mode/enter"),
        exitAdminMode: () => sendJson("/api/auth/admin-mode/exit"),
        logout: () => sendJson("/api/auth/logout")
    };
})();

window.slogsMarkdownPreview = (() => {
    const renderCounters = new Map();

    const render = async (id, value, fallbackHtml) => {
        const host = document.getElementById(id);
        if (!host) {
            return;
        }

        const markdown = value ?? "";
        const shell = host.closest("[data-markdown-preview-shell]");
        const token = (renderCounters.get(id) ?? 0) + 1;
        renderCounters.set(id, token);
        host.setAttribute("data-card-click-ignore", "");

        if (!markdown.trim()) {
            host.classList.remove("vditor-reset");
            host.innerHTML = typeof fallbackHtml === "string"
                ? fallbackHtml
                : '<p class="text-slate-500">내용이 없습니다.</p>';
            shell?.classList.add("is-rendered");
            return;
        }

        try {
            await window.slogsVditorRuntime.load();
            if (renderCounters.get(id) !== token) {
                return;
            }

            host.innerHTML = "";
            host.classList.add("vditor-reset");
            await window.Vditor.preview(host, markdown, {
                cdn: window.slogsVditorRuntime.cdn,
                lang: "ko_KR",
                mode: "light",
                anchor: 0,
                speech: {
                    enable: false
                },
                hljs: {
                    enable: true,
                    lineNumber: false,
                    style: "github"
                },
                theme: {
                    current: "light"
                }
            });
            window.slogsVditorRuntime.localizeCodeCopyControls(host);
            shell?.classList.add("is-rendered");
        } catch {
            host.classList.remove("vditor-reset");
            host.innerHTML = typeof fallbackHtml === "string" ? fallbackHtml : "";
            shell?.classList.add("is-rendered");
        }
    };

    return {
        render
    };
})();
