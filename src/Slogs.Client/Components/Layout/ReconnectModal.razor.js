const reconnectStatus = document.getElementById("components-reconnect-modal");
const stateClasses = [
    "components-reconnect-show",
    "components-reconnect-retrying",
    "components-reconnect-failed",
    "components-reconnect-rejected",
    "components-reconnect-paused",
    "components-reconnect-resume-failed"
];

reconnectStatus.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);

const retryButton = document.getElementById("components-reconnect-button");
retryButton.addEventListener("click", retry);

const resumeButton = document.getElementById("components-resume-button");
resumeButton.addEventListener("click", resume);

function setConnectionState(state) {
    reconnectStatus.dataset.state = state;

    if (state === "connected") {
        reconnectStatus.classList.remove(...stateClasses);
    }
}

function handleReconnectStateChanged(event) {
    const state = event.detail.state;

    if (state === "show") {
        setConnectionState("connecting");
    } else if (state === "hide") {
        setConnectionState("connected");
        document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    } else if (state === "failed") {
        setConnectionState("failed");
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    } else if (state === "rejected") {
        setConnectionState("failed");
        location.reload();
    }
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    setConnectionState("retrying");

    try {
        const successful = await Blazor.reconnect();
        if (!successful) {
            const resumeSuccessful = await Blazor.resumeCircuit();
            if (!resumeSuccessful) {
                location.reload();
            } else {
                setConnectionState("connected");
            }
        } else {
            setConnectionState("connected");
        }
    } catch {
        setConnectionState("failed");
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    }
}

async function resume() {
    setConnectionState("paused");

    try {
        const successful = await Blazor.resumeCircuit();
        if (!successful) {
            location.reload();
        } else {
            setConnectionState("connected");
        }
    } catch {
        reconnectStatus.classList.replace("components-reconnect-paused", "components-reconnect-resume-failed");
        setConnectionState("resume-failed");
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") {
        await retry();
    }
}
