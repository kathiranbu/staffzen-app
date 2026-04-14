// ============================================================
// firebase-messaging-sw.js  — Firebase Background Push Handler
// Served at root: https://yourdomain/firebase-messaging-sw.js
// ============================================================

importScripts('https://www.gstatic.com/firebasejs/10.7.1/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/10.7.1/firebase-messaging-compat.js');

// ─────────────────────────────────────────────────────────────────────────────
// Firebase Web App config
// project_id and messagingSenderId come from your service account JSON.
// apiKey and appId: Firebase Console → Project Settings → Your Apps → Web App → SDK setup
// ─────────────────────────────────────────────────────────────────────────────
const firebaseConfig = {
    apiKey:            "PASTE_YOUR_WEB_APP_API_KEY_HERE",           // Firebase Console → Project Settings → Web App
    authDomain:        "apm-staffzen-22.firebaseapp.com",
    projectId:         "apm-staffzen-22",
    storageBucket:     "apm-staffzen-22.firebasestorage.app",
    messagingSenderId: "108515079438874773809",
    appId:             "PASTE_YOUR_WEB_APP_APP_ID_HERE"             // Firebase Console → Project Settings → Web App
};

firebase.initializeApp(firebaseConfig);
const messaging = firebase.messaging();

// Handle push messages received when the browser tab is NOT focused / closed
messaging.onBackgroundMessage(function (payload) {
    console.log('[SW] Background push received:', payload);

    const title = payload.notification?.title || payload.data?.title || 'StaffZen';
    const body  = payload.notification?.body  || payload.data?.body  || '';

    self.registration.showNotification(title, {
        body,
        icon:  '/favicon.png',
        badge: '/favicon.png',
        tag:   'staffzen-push',
        data:  { url: '/' }
    });
});

// User clicks the notification → focus or open the app
self.addEventListener('notificationclick', function (event) {
    event.notification.close();
    const url = event.notification.data?.url || '/';
    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (list) {
            for (const client of list) {
                if (client.url.includes(self.location.origin) && 'focus' in client)
                    return client.focus();
            }
            if (clients.openWindow) return clients.openWindow(url);
        })
    );
});
