// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Disable the submit button and show a spinner while a form is being submitted,
// so users get feedback and cannot double-submit (e.g. double goods receipt/issue).
document.addEventListener("submit", event => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || form.hasAttribute("data-no-loading-state")) return;
    if (form.dataset.submitting === "true") {
        event.preventDefault();
        return;
    }
    if (typeof form.checkValidity === "function" && !form.checkValidity()) return;

    form.dataset.submitting = "true";
    const submitter = event.submitter instanceof HTMLButtonElement
        ? event.submitter
        : form.querySelector("button[type='submit']");
    if (submitter) {
        submitter.classList.add("is-submitting");
        submitter.setAttribute("aria-busy", "true");
        submitter.disabled = true;
    }
});

// After a failed submit round-trip, move focus to the first invalid field so
// keyboard/screen-reader users land directly on the problem instead of the page top.
document.addEventListener("DOMContentLoaded", () => {
    const firstInvalid = document.querySelector(".input-validation-error");
    if (firstInvalid) {
        firstInvalid.focus({ preventScroll: false });
        firstInvalid.scrollIntoView({ behavior: "smooth", block: "center" });
    }

    // Collapsible Sidebar Navigation Accordion
    const titles = document.querySelectorAll(".side-nav .nav-section-title");
    titles.forEach(title => {
        const links = [];
        let next = title.nextElementSibling;
        while (next && !next.classList.contains("nav-section-title")) {
            if (next.tagName === "A" || next.querySelector("a")) {
                links.push(next);
            }
            next = next.nextElementSibling;
        }

        if (links.length === 0) return;

        title.setAttribute("role", "button");
        title.setAttribute("tabindex", "0");
        title.setAttribute("aria-expanded", "true");
        title.setAttribute("title", "Bấm để ẩn/hiện danh mục");

        const toggleSection = () => {
            const isCollapsed = title.classList.toggle("is-collapsed");
            title.setAttribute("aria-expanded", (!isCollapsed).toString());
            links.forEach(link => {
                link.style.display = isCollapsed ? "none" : "";
            });
        };

        title.addEventListener("click", toggleSection);
        title.addEventListener("keydown", (e) => {
            if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                toggleSection();
            }
        });
    });
});
