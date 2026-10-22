// NeedleBase web: tiny, dependency-free helpers (CSP-safe: no inline script).
// Double-submit guard: once a form really submits (validation passed), the clicked submit button is
// disabled, marked aria-busy and shows its data-pending-text. Back/forward cache restores it.
(function () {
  "use strict";
  document.addEventListener("submit", function (e) {
    if (e.defaultPrevented) return; // client validation blocked it
    var form = e.target;
    if (form.dataset.nbSubmitting === "1") { e.preventDefault(); return; }
    form.dataset.nbSubmitting = "1";
    var btn = e.submitter || form.querySelector("button[type=submit]");
    if (!btn) return;
    // Defer disabling so the button's own name/value still goes with this submission.
    setTimeout(function () {
      btn.setAttribute("aria-busy", "true");
      btn.disabled = true;
      if (btn.dataset.pendingText) { btn.dataset.nbLabel = btn.textContent; btn.textContent = btn.dataset.pendingText; }
    }, 0);
  });
  window.addEventListener("pageshow", function () {
    document.querySelectorAll("form[data-nb-submitting]").forEach(function (f) { delete f.dataset.nbSubmitting; });
    document.querySelectorAll("button[aria-busy=true]").forEach(function (b) {
      b.disabled = false; b.removeAttribute("aria-busy");
      if (b.dataset.nbLabel) b.textContent = b.dataset.nbLabel;
    });
  });
})();
