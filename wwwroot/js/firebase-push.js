// ============================================================
// firebase-push.js — Firebase Foreground Push + Token Manager
// Loaded in App.razor as a module script
// ============================================================

window.FirebasePush = (function () {

    // ─────────────────────────────────────────────────────────────────────────
    // Firebase Web App config — same values as firebase-messaging-sw.js
    // apiKey  → Firebase Console → Project Settings → Your Apps → Web App
    // appId   → Firebase Console → Project Settings → Your Apps → Web App
    // vapidKey→ Firebase Console → Project Settings → Cloud Messaging tab
    //           → Web Push certificates → Key pair (public key)
    // ─────────────────────────────────────────────────────────────────────────
    const FIREBASE_CONFIG = {
        apiKey:            "PASTE_YOUR_WEB_APP_API_KEY_HERE",       // Firebase Console → Web App
        authDomain:        "apm-staffzen-22.firebaseapp.com",
        projectId:         "apm-staffzen-22",
        storageBucket:     "apm-staffzen-22.firebasestorage.app",
        messagingSenderId: "108515079438874773809",
        appId:             "PASTE_YOUR_WEB_APP_APP_ID_HERE"         // Firebase Console → Web App
    };

    const VAPID_KEY = "PASTE_YOUR_VAPID_PUBLIC_KEY_HERE";           // Firebase Console → Cloud Messaging → Web Push certificates

    // ─────────────────────────────────────────────────────────────────────────

    let _app       = null;
    let _messaging = null;
    let _dotNet    = null;

    // Called from AccountSettings.razor OnAfterRenderAsync
    function init(dotNetRef) {
        _dotNet = dotNetRef;
        if (_app) { _requestPermission(); return; }   // already initialised

        // Register the service worker first (must resolve before getToken)
        _registerServiceWorker().then(() => {
            // Dynamically load Firebase ESM modules from CDN
            Promise.all([
                import('https://www.gstatic.com/firebasejs/10.7.1/firebase-app.js'),
                import('https://www.gstatic.com/firebasejs/10.7.1/firebase-messaging.js')
            ]).then(([{ initializeApp }, { getMessaging, getToken, onMessage }]) => {

                _app       = initializeApp(FIREBASE_CONFIG);
                _messaging = getMessaging(_app);

                // Foreground message handler (tab is open and focused)
                onMessage(_messaging, (payload) => {
                    const title = payload.notification?.title || payload.data?.title || 'StaffZen';
                    const body  = payload.notification?.body  || payload.data?.body  || '';

                    // Show native browser notification even when tab is open
                    if (Notification.permission === 'granted') {
                        new Notification(title, { body, icon: '/favicon.png', tag: 'staffzen-push' });
                    }

                    // Tell Blazor so it can show an in-app toast if desired
                    if (_dotNet) _dotNet.invokeMethodAsync('OnPushReceived', title, body).catch(() => {});
                });

                _requestPermission(getToken);

            }).catch(err => console.error('[FCM] SDK import failed:', err));
        });
    }

    function _registerServiceWorker() {
        if (!('serviceWorker' in navigator)) return Promise.resolve();
        return navigator.serviceWorker
            .register('/firebase-messaging-sw.js')
            .then(reg => {
                console.log('[FCM] Service worker registered, scope:', reg.scope);
                return reg;
            })
            .catch(err => console.error('[FCM] Service worker registration failed:', err));
    }

    function _requestPermission(getToken) {
        if (!('Notification' in window)) {
            console.warn('[FCM] Browser does not support notifications.');
            return;
        }

        Notification.requestPermission().then(permission => {
            if (permission === 'granted') {
                _getAndSaveToken(getToken);
            } else {
                console.warn('[FCM] Permission denied.');
                if (_dotNet) _dotNet.invokeMethodAsync('OnPushPermissionDenied').catch(() => {});
            }
        });
    }

    function _getAndSaveToken(getToken) {
        // If called from _requestPermission after init, getToken is already imported
        // If called standalone, re-import
        const doGet = getToken
            ? Promise.resolve(getToken)
            : import('https://www.gstatic.com/firebasejs/10.7.1/firebase-messaging.js').then(m => m.getToken);

        doGet.then(getTokenFn => {
            return navigator.serviceWorker.ready.then(swReg => {
                return getTokenFn(_messaging, { vapidKey: VAPID_KEY, serviceWorkerRegistration: swReg });
            });
        }).then(token => {
            if (token) {
                console.log('[FCM] Token obtained successfully.');
                if (_dotNet) _dotNet.invokeMethodAsync('OnFcmTokenReceived', token).catch(() => {});
            } else {
                console.warn('[FCM] No token — ensure VAPID key is correct and SW is registered.');
            }
        }).catch(err => console.error('[FCM] getToken error:', err));
    }

    return { init };

})();

// ── Time picker column auto-scroll ──────────────────────────────────────────
window.scrollPickerColumns = function () {
    document.querySelectorAll('.tep-tp-col').forEach(function (col) {
        var sel = col.querySelector('.tep-tp-cell--sel');
        if (sel) {
            var offset = sel.offsetTop - (col.clientHeight / 2) + (sel.clientHeight / 2);
            col.scrollTo({ top: Math.max(0, offset), behavior: 'instant' });
        }
    });
};

// Select all text in an input by id (called on focus to mimic Jibble blue highlight)
window.selectInputText = function (inputId) {
    setTimeout(function () {
        var el = document.getElementById(inputId);
        if (el) { el.focus(); el.select(); }
    }, 20);
};
