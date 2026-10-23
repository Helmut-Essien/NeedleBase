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

// Mobile navigation drawer (< 768px). Without JS the toggle is a #nb-drawer link and CSS :target opens it.
// With JS: open/close via toggle (click, Enter or Space), close button, scrim, Escape or choosing a link;
// focus moves in, is trapped (the rest of the page is inert) and returns to the toggle; scroll is locked
// (body pinned, so iOS Safari can't scroll the page behind). Crossing to >= 768px closes it.
(function () {
  "use strict";
  var drawer = document.getElementById("nb-drawer");
  var toggle = document.querySelector("[data-nb-drawer-open]");
  if (!drawer || !toggle) return;
  var root = document.documentElement, body = document.body, scrollY = 0;
  var outside = document.querySelectorAll("[data-nb-drawer-outside]");
  var mobile = window.matchMedia("(max-width: 767.98px)");
  function focusables() {
    return Array.prototype.filter.call(drawer.querySelectorAll("a[href], button:not([disabled]), input:not([disabled]), select, textarea, [tabindex]:not([tabindex='-1'])"),
      function (el) { return el.getClientRects().length > 0; });
  }
  function isOpen() { return root.classList.contains("nb-drawer-open"); }
  function setInert(on) { outside.forEach(function (el) { if (on) el.setAttribute("inert", ""); else el.removeAttribute("inert"); }); }
  function open() {
    if (isOpen() || !mobile.matches) return;
    if (location.hash === "#nb-drawer") history.replaceState(history.state, "", location.pathname + location.search);
    scrollY = window.scrollY;
    body.style.position = "fixed"; body.style.top = -scrollY + "px"; body.style.width = "100%";
    root.classList.add("nb-drawer-open");
    toggle.setAttribute("aria-expanded", "true");
    toggle.setAttribute("aria-label", "Close menu");
    drawer.setAttribute("role", "dialog");
    drawer.setAttribute("aria-modal", "true");
    setInert(true);
    var f = focusables();
    if (!drawer.hasAttribute("tabindex")) drawer.setAttribute("tabindex", "-1");
    (f[0] || drawer).focus({ preventScroll: true });
  }
  function close(restore) {
    if (!isOpen()) return;
    root.classList.remove("nb-drawer-open");
    body.style.position = ""; body.style.top = ""; body.style.width = "";
    window.scrollTo(0, scrollY);
    toggle.setAttribute("aria-expanded", "false");
    toggle.setAttribute("aria-label", "Open menu");
    drawer.removeAttribute("role");
    drawer.removeAttribute("aria-modal");
    setInert(false);
    if (restore !== false) toggle.focus({ preventScroll: true });
  }
  toggle.addEventListener("click", function (e) { e.preventDefault(); open(); });
  // The toggle and close controls are links (for the no-JS fallback) with role=button, so Space must work too.
  document.addEventListener("keydown", function (e) {
    var t = e.target;
    if ((e.key === " " || e.key === "Spacebar") && t.getAttribute && t.getAttribute("role") === "button" && t.tagName === "A") { e.preventDefault(); t.click(); }
  });
  document.querySelectorAll("[data-nb-drawer-close]").forEach(function (el) {
    el.addEventListener("click", function (e) { e.preventDefault(); close(); });
  });
  drawer.addEventListener("click", function (e) {
    if (e.target.closest("nav a") && isOpen()) close(false); // navigating away
  });
  document.addEventListener("keydown", function (e) {
    if (!isOpen()) return;
    if (e.key === "Escape") { e.preventDefault(); close(); return; }
    if (e.key !== "Tab") return;
    var f = focusables();
    if (f.length < 2) { e.preventDefault(); (f[0] || drawer).focus(); return; }
    var first = f[0], last = f[f.length - 1];
    if (!drawer.contains(document.activeElement)) { e.preventDefault(); first.focus(); }
    else if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  });
  var onChange = function () { if (!mobile.matches) close(false); };
  if (mobile.addEventListener) mobile.addEventListener("change", onChange); else mobile.addListener(onChange);
  if (location.hash === "#nb-drawer" && mobile.matches) open();
  window.addEventListener("pageshow", function (e) { if (e.persisted) close(false); });
  window.addEventListener("pagehide", function () { close(false); });
})();
