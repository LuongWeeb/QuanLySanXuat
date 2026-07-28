(() => {
    const tableBody = document.querySelector("#qc-items tbody");
    const template = document.querySelector("#qc-item-template");
    const addButton = document.querySelector("#add-qc-item");
    if (!tableBody || !template || !addButton) return;

    const reindex = () => {
        [...tableBody.rows].forEach((row, index) => {
            row.querySelectorAll("[name]").forEach(field => {
                field.name = field.name.replace(/Items\[\d+\]/, `Items[${index}]`);
            });
        });
    };

    const bindRemove = () => {
        tableBody.querySelectorAll(".remove-qc-item").forEach(button => {
            button.onclick = () => {
                if (tableBody.rows.length > 1) {
                    button.closest("tr")?.remove();
                    reindex();
                }
            };
        });
    };

    addButton.addEventListener("click", () => {
        const index = tableBody.rows.length;
        tableBody.insertAdjacentHTML(
            "beforeend",
            template.innerHTML.replaceAll("__index__", index.toString()));
        reindex();
        bindRemove();
    });
    bindRemove();
})();
