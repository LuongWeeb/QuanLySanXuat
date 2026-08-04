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
});
