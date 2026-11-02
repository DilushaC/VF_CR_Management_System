/*
 * session-guard.js
 * Centralized handling for server-side session timeouts.
 *
 * SessionCheckAttribute (server) returns, for AJAX requests whose session
 * has expired:
 *   HTTP 401
 *   { sessionExpired: true, redirectUrl: "/User/Login" }
 *
 * This file makes sure EVERY fetch() and EVERY $.ajax() call in the app
 * reacts to that the same way, without each call needing its own code:
 *   - fetch(): automatically tags every request as AJAX
 *              (X-Requested-With header), and auto-detects a 401
 *              sessionExpired response before the caller's own .then()
 *              ever sees it.
 *   - $.ajax(): a global ajaxError handler catches the same 401 shape.
 *
 * Include this ONE file in _Layout.cshtml, after jQuery, before any
 * page-specific script:
 *     <script src="~/js/session-guard.js"></script>
 */
(function () {
    "use strict";

    var LOGIN_URL_FALLBACK = "/User/Login";
    var sessionExpiredShown = false;

    function showSessionExpired(redirectUrl) {
        // Prevent multiple fetches/ajax calls failing at once from stacking
        // several identical SweetAlerts on top of each other.
        if (sessionExpiredShown) { return; }
        sessionExpiredShown = true;

        var goToLogin = function () {
            window.location.href = redirectUrl || LOGIN_URL_FALLBACK;
        };

        if (window.Swal) {
            Swal.fire({
                title: "Session expired",
                text: "You've been signed out due to inactivity. Please log in again.",
                icon: "warning",
                confirmButtonText: "Go to Login",
                allowOutsideClick: false,
                allowEscapeKey: false,
                customClass: { confirmButton: "btn btn-primary" },
                buttonsStyling: false
            }).then(goToLogin);
        } else {
            alert("Your session has expired. Please log in again.");
            goToLogin();
        }
    }

    // ---------- fetch() wrapper ----------
    if (window.fetch) {
        var originalFetch = window.fetch;

        window.fetch = function (input, init) {
            init = init || {};
            init.headers = new Headers(init.headers || {});
            if (!init.headers.has("X-Requested-With")) {
                init.headers.set("X-Requested-With", "XMLHttpRequest");
            }

            return originalFetch(input, init).then(function (response) {
                if (response.status === 401) {
                    // Clone so the caller can still read the body if they
                    // really want to (we don't consume their response).
                    var cloned = response.clone();
                    cloned.json().then(function (data) {
                        if (data && data.sessionExpired) {
                            showSessionExpired(data.redirectUrl);
                        }
                    }).catch(function () {
                        // 401 without the expected JSON shape — ignore here,
                        // let the caller's own error handling deal with it.
                    });
                }
                return response;
            });
        };
    }

    // ---------- jQuery AJAX global handler ----------
    if (window.jQuery) {
        jQuery(document).ajaxError(function (event, xhr) {
            if (xhr.status === 401 && xhr.responseJSON && xhr.responseJSON.sessionExpired) {
                showSessionExpired(xhr.responseJSON.redirectUrl);
            }
        });
    }
})();
