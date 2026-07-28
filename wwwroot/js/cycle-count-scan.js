(() => {
    const input = document.querySelector("#barcode-input");
    const status = document.querySelector("#scan-status");
    const video = document.querySelector("#barcode-camera");
    const startButton = document.querySelector("#start-camera");
    const stopButton = document.querySelector("#stop-camera");
    const rows = [...document.querySelectorAll(".count-row")];
    if (!input || !status) return;

    let stream;
    let scanning = false;
    let detector;
    let selectedLocation = "";
    const normalize = value => value.trim().toUpperCase();

    const announce = message => {
        status.textContent = message;
    };

    const clearHighlight = () => {
        rows.forEach(row => row.classList.remove("table-warning"));
    };

    const processCode = rawCode => {
        const code = normalize(rawCode);
        if (!code) return;
        clearHighlight();
        const locationMatches = rows.filter(row =>
            normalize(row.dataset.locationCode ?? "") === code);
        if (locationMatches.length > 0) {
            selectedLocation = code;
            locationMatches.forEach(row => row.classList.add("table-warning"));
            locationMatches[0].scrollIntoView({ behavior: "smooth", block: "center" });
            locationMatches[0].querySelector(".counted-qty")?.focus();
            announce(`Đã chọn vị trí ${code}, có ${locationMatches.length} dòng.`);
            return;
        }

        const lotMatches = rows.filter(row =>
            normalize(row.dataset.lotNo ?? "") === code &&
            (!selectedLocation ||
                normalize(row.dataset.locationCode ?? "") === selectedLocation));
        if (lotMatches.length > 1 && !selectedLocation) {
            announce(`Lô ${code} có ở nhiều vị trí. Hãy quét vị trí trước.`);
            return;
        }
        const lotRow = lotMatches[0];
        if (lotRow) {
            lotRow.classList.add("table-warning");
            const quantity = lotRow.querySelector(".counted-qty");
            quantity.value = (Number(quantity.value || 0) + 1).toString();
            lotRow.scrollIntoView({ behavior: "smooth", block: "center" });
            quantity.focus();
            announce(`Đã cộng 1 cho lô ${code}.`);
            return;
        }
        announce(`Không tìm thấy mã ${code} trong đợt kiểm kê.`);
    };

    input.addEventListener("keydown", event => {
        if (event.key !== "Enter") return;
        event.preventDefault();
        processCode(input.value);
        input.value = "";
    });

    const stopCamera = () => {
        scanning = false;
        stream?.getTracks().forEach(track => track.stop());
        stream = undefined;
        video?.classList.add("d-none");
        if (startButton) startButton.disabled = false;
        if (stopButton) stopButton.disabled = true;
        input.focus();
    };

    const detectFrame = async () => {
        if (!scanning || !detector || !video) return;
        try {
            const barcodes = await detector.detect(video);
            if (barcodes.length > 0) {
                processCode(barcodes[0].rawValue);
                stopCamera();
                return;
            }
        } catch {
            announce("Camera không thể đọc mã. Hãy dùng ô quét thủ công.");
            stopCamera();
            return;
        }
        requestAnimationFrame(detectFrame);
    };

    startButton?.addEventListener("click", async () => {
        if (!("BarcodeDetector" in window) || !navigator.mediaDevices?.getUserMedia) {
            announce("Trình duyệt không hỗ trợ quét camera; hãy dùng máy quét hoặc nhập mã.");
            return;
        }
        try {
            detector = new BarcodeDetector({
                formats: ["qr_code", "code_128", "code_39", "ean_13"]
            });
            stream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: { ideal: "environment" } },
                audio: false
            });
            video.srcObject = stream;
            await video.play();
            video.classList.remove("d-none");
            scanning = true;
            startButton.disabled = true;
            stopButton.disabled = false;
            announce("Đưa mã vào giữa khung hình.");
            requestAnimationFrame(detectFrame);
        } catch {
            announce("Không thể mở camera. Kiểm tra quyền truy cập hoặc dùng ô quét.");
            stopCamera();
        }
    });

    stopButton?.addEventListener("click", stopCamera);
    window.addEventListener("pagehide", stopCamera);
    input.focus();
})();
