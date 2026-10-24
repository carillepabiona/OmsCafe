/* theme.js — put in wwwroot/js/theme.js and load it in <head> (no defer),
   so the theme is applied before the page paints (no white flash).
   Uses the same storage key as your landing page: "verso-theme". */
(function () {
    var KEY = "verso-theme";
    var root = document.documentElement;
    var mq = window.matchMedia("(prefers-color-scheme: dark)");

    function saved() {
        try {
            var t = localStorage.getItem(KEY);
            if (t === "light" || t === "dark") return t;
        } catch (e) { }
        return null;
    }

    function apply(theme) {
        root.setAttribute("data-theme", theme);
        root.setAttribute("data-bs-theme", theme); // Bootstrap 5.3 dark mode
        var label = theme === "dark" ? "Switch to light mode" : "Switch to dark mode";
        document.querySelectorAll("[data-theme-toggle]").forEach(function (b) {
            b.setAttribute("aria-label", label);
            b.setAttribute("title", label);
        });
    }

    // 1) Apply immediately (saved choice, else the OS preference)
    apply(saved() || (mq.matches ? "dark" : "light"));

    // 2) Refresh button labels once the buttons exist
    document.addEventListener("DOMContentLoaded", function () {
        apply(root.getAttribute("data-theme"));
    });

    // 3) Toggle on click (works for any element with data-theme-toggle)
    document.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-theme-toggle]");
        if (!btn) return;
        var next = root.getAttribute("data-theme") === "dark" ? "light" : "dark";
        root.classList.add("theme-anim");
        apply(next);
        try { localStorage.setItem(KEY, next); } catch (e2) { }
        setTimeout(function () { root.classList.remove("theme-anim"); }, 350);
    });

    // 4) Follow OS changes only if the user hasn't picked a theme
    var onChange = function (e) { if (!saved()) apply(e.matches ? "dark" : "light"); };
    if (mq.addEventListener) mq.addEventListener("change", onChange);
})();