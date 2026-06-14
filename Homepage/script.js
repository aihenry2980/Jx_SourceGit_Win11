(function () {
  const releaseUrl = "https://github.com/aihenry2980/Jx_SourceGit_Win11/releases/latest";

  function getSessionId() {
    const key = "jx_sourcegit_home_session";
    const value = `${Date.now()}-${Math.random().toString(16).slice(2)}`;

    try {
      const existing = window.sessionStorage.getItem(key);
      if (existing) {
        return existing;
      }

      window.sessionStorage.setItem(key, value);
    } catch {
      return value;
    }

    return value;
  }

  function track(eventName, extra) {
    const config = window.JX_SOURCEGIT_ANALYTICS || {};
    const endpoint = config.googleAppsScriptUrl;
    if (!endpoint || endpoint.includes("PASTE_YOUR")) {
      return;
    }

    const payload = {
      event: eventName,
      project: "Jx SourceGit Win11",
      page: window.location.pathname,
      title: document.title,
      url: window.location.href,
      referrer: document.referrer,
      language: navigator.language,
      userAgent: navigator.userAgent,
      screen: `${window.screen.width}x${window.screen.height}`,
      sessionId: getSessionId(),
      timestamp: new Date().toISOString(),
      ...extra
    };

    try {
      navigator.sendBeacon?.(endpoint, new Blob([JSON.stringify(payload)], { type: "text/plain" })) ||
        fetch(endpoint, {
          method: "POST",
          mode: "no-cors",
          keepalive: true,
          headers: { "Content-Type": "text/plain" },
          body: JSON.stringify(payload)
        });
    } catch {
      // Analytics must never block navigation.
    }
  }

  window.addEventListener("DOMContentLoaded", () => {
    track("page_view");

    document.querySelectorAll("[data-track-download]").forEach((link) => {
      link.addEventListener("click", () => {
        track("download_click", {
          targetUrl: link.href || releaseUrl
        });
      });
    });
  });
})();
