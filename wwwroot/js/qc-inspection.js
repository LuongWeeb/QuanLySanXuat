(() => {
    const parse = value => {
        const normalized = value.trim().replace(",", ".");
        return normalized === "" ? Number.NaN : Number(normalized);
    };

    document.querySelectorAll(".qc-measurement").forEach(row => {
        const input = row.querySelector(".qc-value");
        const status = row.querySelector(".qc-line-status");
        if (!input || !status) return;
        const min = parse(row.dataset.min ?? "");
        const max = parse(row.dataset.max ?? "");
        const update = () => {
            const value = parse(input.value);
            if (Number.isNaN(value)) {
                status.textContent = "Chưa nhập";
                status.className = "status-pill qc-line-status is-muted";
                return;
            }
            const ok = (Number.isNaN(min) || value >= min) &&
                (Number.isNaN(max) || value <= max);
            status.textContent = ok ? "PASS" : "FAIL";
            status.className = `status-pill qc-line-status ${ok ? "is-active" : "is-muted"}`;
        };
        input.addEventListener("input", update);
        update();
    });
})();
