// Small progressive-enhancement helpers for the static-SSR Aspire Shop frontend.
//
// Everything here uses event delegation on `document`, so it keeps working across Blazor's
// enhanced navigation (which morphs the DOM in place without re-running page scripts).

(function () {
    "use strict";

    function effectiveTheme() {
        var explicit = document.documentElement.getAttribute("data-theme");
        if (explicit === "light" || explicit === "dark") {
            return explicit;
        }
        return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }

    function toggleTheme() {
        var next = effectiveTheme() === "dark" ? "light" : "dark";
        document.documentElement.setAttribute("data-theme", next);
        try {
            localStorage.setItem("aspireshop-theme", next);
        } catch (e) {
            /* localStorage unavailable */
        }
    }

    document.addEventListener("click", function (event) {
        // Theme toggle.
        var themeToggle = event.target.closest("[data-theme-toggle]");
        if (themeToggle) {
            event.preventDefault();
            toggleTheme();
            return;
        }

        // Open a quick-look modal.
        var opener = event.target.closest("[data-quickview]");
        if (opener) {
            var dialog = document.getElementById(opener.getAttribute("data-quickview"));
            if (dialog && typeof dialog.showModal === "function" && !dialog.open) {
                event.preventDefault();
                dialog.showModal();
            }
            return;
        }

        // Close button inside a modal.
        var closer = event.target.closest("[data-quickview-close]");
        if (closer) {
            var owning = closer.closest("dialog");
            if (owning) {
                owning.close();
            }
            return;
        }

        // Light-dismiss: a click that lands on the dialog element itself is a backdrop click.
        if (event.target instanceof HTMLDialogElement && event.target.classList.contains("quickview")) {
            event.target.close();
        }
    });

    // Preserve scroll position across the enhanced page refresh that follows cart mutations
    // (add-to-cart, quantity changes, clear). Those forms are marked data-preserve-scroll. Without
    // this, the post-mutation refresh can jump the page back to the top, which feels like a reload.
    var pendingScroll = null;

    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (form instanceof HTMLFormElement && form.hasAttribute("data-preserve-scroll")) {
            pendingScroll = window.scrollY;
        }
    }, true);

    function restoreScroll() {
        if (pendingScroll !== null) {
            var y = pendingScroll;
            pendingScroll = null;
            window.scrollTo(0, y);
        }
    }

    if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
        window.Blazor.addEventListener("enhancedload", restoreScroll);
    }
})();
