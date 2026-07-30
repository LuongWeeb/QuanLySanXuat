"use strict";

(() => {
    const requiredAssets = Object.freeze([
        Object.freeze({
            cdnUrl: "https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js",
            localUrl: "/lib/microsoft-signalr/8.0.0/signalr.min.js",
            integrity: "sha384-/taWmisziXYpcfnYsumSUmNaiMvG/fF/OJOUCLnqCIYTrpOZy7WbFF6FfIxwOrfL"
        }),
        Object.freeze({
            cdnUrl: "https://cdn.jsdelivr.net/npm/chart.js@4.4.9/dist/chart.umd.min.js",
            localUrl: "/lib/chart.js/4.4.9/chart.umd.min.js",
            integrity: "sha384-b0GXujLkk9eYYSmcSfoyZbfyElGAQnDyY0skCHSG6w3JgTMFnz11ggrTAr7seu9f"
        })
    ]);

    const appendScript = (source, integrity) => new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = source;
        script.async = true;
        if (integrity) {
            script.integrity = integrity;
            script.crossOrigin = "anonymous";
        }
        script.onload = resolve;
        script.onerror = () => {
            script.remove();
            reject(new Error(`Unable to load script: ${source}`));
        };
        document.head.append(script);
    });

    const loadScript = async asset => {
        try {
            await appendScript(asset.cdnUrl, asset.integrity);
        } catch {
            await appendScript(asset.localUrl);
        }
    };

    let initializationPromise;
    const start = initializeDashboard => {
        if (typeof initializeDashboard !== "function") {
            return Promise.reject(new TypeError("A dashboard initializer is required."));
        }

        initializationPromise ??=
            Promise.all(requiredAssets.map(loadScript))
            .then(initializeDashboard);
        return initializationPromise;
    };

    window.factoryDashboardLoader = Object.freeze({ start });
})();
