(function () {
    function syncToggleState(toggleButton, passwordInput, visible) {
        const showLabel = toggleButton.getAttribute('data-show-label') || 'Show password';
        const hideLabel = toggleButton.getAttribute('data-hide-label') || 'Hide password';

        passwordInput.type = visible ? 'text' : 'password';
        toggleButton.classList.toggle('nb-password-toggle--visible', visible);
        toggleButton.setAttribute('aria-label', visible ? hideLabel : showLabel);
        toggleButton.setAttribute('aria-pressed', visible ? 'true' : 'false');
    }

    function togglePassword(toggleButton) {
        const inputId = toggleButton.getAttribute('data-input-id');
        const passwordInput = inputId ? document.getElementById(inputId) : null;
        if (!passwordInput) {
            return;
        }

        const showing = passwordInput.type === 'password';
        syncToggleState(toggleButton, passwordInput, showing);
    }

    function remaskAll(root) {
        root.querySelectorAll('.nb-password-toggle').forEach(function (button) {
            const inputId = button.getAttribute('data-input-id');
            const passwordInput = inputId ? document.getElementById(inputId) : null;
            if (!passwordInput) {
                return;
            }

            syncToggleState(button, passwordInput, false);
        });
    }

    function bindToggles(root) {
        root.querySelectorAll('.nb-password-toggle').forEach(function (button) {
            if (button.dataset.nbPasswordToggleBound === '1') {
                return;
            }

            button.dataset.nbPasswordToggleBound = '1';
            button.addEventListener('click', function () {
                togglePassword(button);
            });
        });
    }

    function bindFormRemask(root) {
        root.querySelectorAll('form.nb-auth-form').forEach(function (form) {
            if (form.dataset.nbPasswordToggleSubmitBound === '1') {
                return;
            }

            form.dataset.nbPasswordToggleSubmitBound = '1';
            form.addEventListener('submit', function () {
                remaskAll(document);
            });
        });
    }

    function init() {
        bindToggles(document);
        bindFormRemask(document);

        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'hidden') {
                remaskAll(document);
            }
        });

        window.addEventListener('pageshow', function (event) {
            if (event.persisted) {
                remaskAll(document);
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
