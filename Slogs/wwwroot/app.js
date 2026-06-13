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
