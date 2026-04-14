// ═══════════════════════════════════════════════════════════════════════════
//  maps.js  —  All Google Maps + GPS tracking logic for ApmStaffZen
//
//  Contains three namespaces:
//    window.locMap        — Locations / Geofence management page map
//    window.llMap         — Live Locations admin map (real-time employee pins)
//    window.liveLocation  — GPS sender (runs on employee's browser while clocked in)
// ═══════════════════════════════════════════════════════════════════════════

// ───────────────────────────────────────────────────────────────────────────
//  SHARED UTILITY
//  Queues callbacks until Google Maps SDK finishes loading asynchronously.
// ───────────────────────────────────────────────────────────────────────────
function waitForGoogleMaps(cb) {
    if (window._googleMapsReady && window.google && window.google.maps) {
        cb();
    } else {
        window.onGoogleMapsReady(cb);
    }
}


// ═══════════════════════════════════════════════════════════════════════════
//  LOCATIONS MAP  (locMap)
//  Used on the Locations management page to show/add/edit geofenced locations.
// ═══════════════════════════════════════════════════════════════════════════
window.locMap = (function () {

    let map        = null;
    let markers    = {};
    let circles    = {};
    let infoWindow = null;
    let mapType    = 'roadmap';

    function makeIcon() {
        return {
            path: google.maps.SymbolPath.CIRCLE,
            scale: 18,
            fillColor: '#ff7a00',
            fillOpacity: 1,
            strokeColor: '#fff',
            strokeWeight: 3,
            labelOrigin: new google.maps.Point(0, 0),
        };
    }

    function makeLabel(name) {
        return {
            text: (name && name.length > 0) ? name[0].toUpperCase() : '?',
            color: '#fff',
            fontWeight: '700',
            fontSize: '13px',
        };
    }

    return {

        init(containerId, locations) {
            waitForGoogleMaps(() => {
                const el = document.getElementById(containerId);
                if (!el) return;
                markers = {};
                circles = {};

                map = new google.maps.Map(el, {
                    center: { lat: 20, lng: 0 },
                    zoom: 2,
                    mapTypeId: mapType,
                    mapTypeControl: false,
                    streetViewControl: false,
                    fullscreenControl: false,
                });

                infoWindow = new google.maps.InfoWindow();

                if (locations && locations.length > 0) {
                    const bounds = new google.maps.LatLngBounds();
                    locations.forEach(loc => {
                        if (!loc.lat || !loc.lng) return;
                        const pos    = { lat: loc.lat, lng: loc.lng };
                        const marker = new google.maps.Marker({
                            position: pos, map,
                            icon: makeIcon(), label: makeLabel(loc.name), title: loc.name,
                        });
                        marker.addListener('click', () => {
                            infoWindow.setContent(
                                '<div style="font-family:sans-serif;padding:4px">' +
                                '<b style="font-size:14px">'  + loc.name + '</b>' +
                                '<div style="font-size:12px;color:#6b7280;margin-top:3px">' +
                                loc.radius + 'm radius</div></div>');
                            infoWindow.open(map, marker);
                        });
                        markers[loc.id] = marker;
                        if (loc.radius) {
                            circles[loc.id] = new google.maps.Circle({
                                map, center: pos, radius: loc.radius,
                                strokeColor: '#ff7a00', strokeOpacity: 0.8, strokeWeight: 2,
                                fillColor: '#ff7a00', fillOpacity: 0.1,
                            });
                        }
                        bounds.extend(pos);
                    });
                    if (locations.length === 1) {
                        map.setCenter({ lat: locations[0].lat, lng: locations[0].lng });
                        map.setZoom(14);
                    } else {
                        map.fitBounds(bounds, { top: 50, right: 50, bottom: 50, left: 50 });
                    }
                }
            });
        },

        flyTo(lat, lng) {
            if (!map) return;
            map.panTo({ lat, lng });
            map.setZoom(15);
            setTimeout(() => {
                for (const id in markers) {
                    const m = markers[id];
                    const p = m.getPosition();
                    if (Math.abs(p.lat() - lat) < 0.001 && Math.abs(p.lng() - lng) < 0.001) {
                        google.maps.event.trigger(m, 'click');
                        break;
                    }
                }
            }, 400);
        },

        addMarker(id, name, lat, lng, radius) {
            if (!map) return;
            if (markers[id]) { markers[id].setMap(null); delete markers[id]; }
            if (circles[id]) { circles[id].setMap(null); delete circles[id]; }
            const pos    = { lat, lng };
            const marker = new google.maps.Marker({
                position: pos, map,
                icon: makeIcon(), label: makeLabel(name), title: name,
            });
            marker.addListener('click', () => {
                infoWindow.setContent(
                    '<div style="font-family:sans-serif;padding:4px">' +
                    '<b style="font-size:14px">' + name + '</b>' +
                    '<div style="font-size:12px;color:#6b7280;margin-top:3px">' +
                    radius + 'm radius</div></div>');
                infoWindow.open(map, marker);
            });
            markers[id] = marker;
            if (radius) {
                circles[id] = new google.maps.Circle({
                    map, center: pos, radius,
                    strokeColor: '#ff7a00', strokeOpacity: 0.8, strokeWeight: 2,
                    fillColor: '#ff7a00', fillOpacity: 0.1,
                });
            }
            map.panTo(pos);
            map.setZoom(15);
        },

        removeMarker(id) {
            if (markers[id]) { markers[id].setMap(null); delete markers[id]; }
            if (circles[id]) { circles[id].setMap(null); delete circles[id]; }
        },

        setStyle(satellite) {
            mapType = satellite ? 'satellite' : 'roadmap';
            if (map) map.setMapTypeId(mapType);
        },

        showPendingPin(lat, lng, name) {
            if (!map) return;
            if (window._pendingMarker) { window._pendingMarker.setMap(null); window._pendingMarker = null; }
            window._pendingMarker = new google.maps.Marker({
                position: { lat, lng }, map, title: name,
                icon: {
                    path: google.maps.SymbolPath.CIRCLE, scale: 18,
                    fillColor: '#ff7a00', fillOpacity: 1,
                    strokeColor: '#fff', strokeWeight: 3,
                    labelOrigin: new google.maps.Point(0, 0),
                },
                label: { text: name[0] ? name[0].toUpperCase() : '?', color: '#fff', fontWeight: '700', fontSize: '13px' },
            });
            if (window._pendingCircle) { window._pendingCircle.setMap(null); }
            window._pendingCircle = new google.maps.Circle({
                map, center: { lat, lng }, radius: 300,
                strokeColor: '#ff7a00', strokeOpacity: 1, strokeWeight: 2.5,
                fillColor: '#ff7a00', fillOpacity: 0.12,
            });
            map.panTo({ lat, lng });
            map.setZoom(15);
        },

        clearPendingPin() {
            if (window._pendingMarker) { window._pendingMarker.setMap(null); window._pendingMarker = null; }
            if (window._pendingCircle) { window._pendingCircle.setMap(null); window._pendingCircle = null; }
        },

        updateCircle(lat, lng, radius) {
            if (!map) return;
            const target = window._pendingCircle || window._editingCircle;
            if (target) {
                target.setRadius(radius);
                const bounds = target.getBounds();
                if (bounds) map.fitBounds(bounds, { top: 80, right: 80, bottom: 80, left: 80 });
            }
        },

        initPlacesSearch(inputId, dotNetMethodName, dotNetObjRef) {
            waitForGoogleMaps(() => {
                const input = document.getElementById(inputId);
                if (!input) return;
                const ac = new google.maps.places.Autocomplete(input, {
                    fields: ['name', 'formatted_address', 'geometry'],
                });

                ac.addListener('place_changed', () => {
                    const place = ac.getPlace();
                    if (!place.geometry) return;
                    dotNetObjRef.invokeMethodAsync(
                        dotNetMethodName,
                        place.name || '',
                        place.formatted_address || '',
                        place.geometry.location.lat(),
                        place.geometry.location.lng()
                    );
                });

                function buildMissingItem(pac) {
                    var old = pac.querySelector('.pac-item-missing');
                    if (old) old.remove();

                    var item = document.createElement('div');
                    item.className = 'pac-item pac-item-missing';
                    item.style.cssText = [
                        'cursor:pointer', 'padding:10px 16px',
                        'display:flex', 'align-items:center', 'gap:8px',
                        'color:#ff7a00', 'font-weight:600', 'font-size:13px',
                        'font-family:inherit', 'border-top:1px solid #f0f0f0',
                        'background:#fff', 'box-sizing:border-box'
                    ].join(';');

                    var icon = document.createElement('span');
                    icon.textContent = '+';
                    icon.style.cssText = 'font-size:16px;line-height:1;color:#ff7a00;font-weight:700;';

                    var text = document.createElement('span');
                    text.textContent = 'Add missing location';

                    item.appendChild(icon);
                    item.appendChild(text);

                    item.addEventListener('mouseenter', () => { item.style.background = '#fff7ed'; });
                    item.addEventListener('mouseleave', () => { item.style.background = '#fff'; });
                    item.addEventListener('mousedown', (e) => {
                        e.preventDefault();
                        e.stopPropagation();
                        pac.style.display = 'none';
                        input.blur();
                        if (dotNetObjRef) {
                            dotNetObjRef.invokeMethodAsync('OpenAddMissingFlow')
                                .catch(err => console.warn('OpenAddMissingFlow:', err));
                        }
                    });

                    pac.appendChild(item);
                }

                var bodyObserver = new MutationObserver(() => {
                    var pac = document.querySelector('.pac-container');
                    if (!pac) return;
                    bodyObserver.disconnect();

                    new MutationObserver(() => {
                        if (pac.style.display !== 'none' && pac.children.length > 0) {
                            var last = pac.lastElementChild;
                            if (!last || !last.classList.contains('pac-item-missing'))
                                buildMissingItem(pac);
                        }
                    }).observe(pac, { childList: true });

                    new MutationObserver(() => {
                        if (pac.style.display !== 'none' && pac.children.length > 1) {
                            var last = pac.lastElementChild;
                            if (!last || !last.classList.contains('pac-item-missing'))
                                buildMissingItem(pac);
                        }
                    }).observe(pac, { attributes: true, attributeFilter: ['style'] });
                });
                bodyObserver.observe(document.body, { childList: true, subtree: false });
            });
        },

        startMissingPinDrag(dotNetRef) {
            window._missingDotNet = dotNetRef;
            if (map && map.getZoom() < 5) map.setZoom(5);
        },

        cancelMissingPin() {
            if (window._missingMarker) { window._missingMarker.setMap(null); window._missingMarker = null; }
            if (window._missingMapClick) {
                google.maps.event.removeListener(window._missingMapClick);
                window._missingMapClick = null;
            }
            window._missingDotNet = null;
        },

        getMissingPinPosition() {
            if (!map) return [0, 0];
            const c = map.getCenter();
            return [c.lat(), c.lng()];
        },

        reverseGeocodeForPanel(lat, lng) {
            if (!window._missingDotNet || !window.google) return;
            new google.maps.Geocoder().geocode({ location: { lat, lng } }, (results, status) => {
                if (status !== 'OK' || !results || !results[0]) return;
                const comps = results[0].address_components;
                let street = '', city = '', country = '', postal = '';
                for (const c of comps) {
                    const t = c.types;
                    if (t.includes('route'))                         street  = c.long_name;
                    if (t.includes('locality') ||
                        t.includes('administrative_area_level_2'))  city    = c.long_name;
                    if (t.includes('country'))                       country = c.long_name;
                    if (t.includes('postal_code'))                   postal  = c.long_name;
                }
                if (!street && results[0].formatted_address)
                    street = results[0].formatted_address.split(',')[0].trim();
                if (window._missingDotNet)
                    window._missingDotNet.invokeMethodAsync(
                        'OnMissingAddressResolved', street, city, country, postal
                    ).catch(e => console.warn('OnMissingAddressResolved failed', e));
            });
        },
    };
})();


// ═══════════════════════════════════════════════════════════════════════════
//  LIVE LOCATIONS MAP  (llMap)
//  Used on the Live Locations admin page.
//  Shows real-time employee pins, routes (polylines), and geofence circles.
// ═══════════════════════════════════════════════════════════════════════════
window.llMap = (function () {

    let map           = null;
    let memberMarkers = {};
    let routePolyline = null;
    let routeDots     = [];
    let infoWindow    = null;
    let dotNetRef     = null;
    let mapTypeId     = 'roadmap';

    function memberIcon(isSelected) {
        return {
            path: google.maps.SymbolPath.CIRCLE,
            scale: 20,
            fillColor: isSelected ? '#ff7a00' : '#22c55e',
            fillOpacity: 1,
            strokeColor: '#fff',
            strokeWeight: 3,
            labelOrigin: new google.maps.Point(0, 0),
        };
    }

    function memberLabel(name) {
        return {
            text: (name && name.length > 0) ? name[0].toUpperCase() : '?',
            color: '#fff',
            fontWeight: '700',
            fontSize: '14px',
        };
    }

    function placeMarker(m) {
        if (!m.lat || !m.lng) return;
        const marker = new google.maps.Marker({
            position: { lat: m.lat, lng: m.lng },
            map,
            icon:   memberIcon(false),
            label:  memberLabel(m.name),
            title:  m.name,
            zIndex: 10,
        });
        marker.addListener('click', () => {
            if (dotNetRef) dotNetRef.invokeMethodAsync('OnMapMemberClicked', m.id).catch(() => {});
        });
        memberMarkers[m.id] = marker;
    }

    return {

        init(containerId, members, dotNet) {
            dotNetRef = dotNet || null;
            waitForGoogleMaps(() => {
                const el = document.getElementById(containerId);
                if (!el) return;
                memberMarkers = {};

                map = new google.maps.Map(el, {
                    center:            { lat: 13.0827, lng: 80.2707 },
                    zoom:              13,
                    mapTypeId:         mapTypeId,
                    mapTypeControl:    false,
                    streetViewControl: false,
                    fullscreenControl: false,
                    styles: [
                        { featureType: 'poi', elementType: 'labels', stylers: [{ visibility: 'off' }] },
                    ],
                });

                infoWindow = new google.maps.InfoWindow();

                if (members && members.length > 0) {
                    const bounds = new google.maps.LatLngBounds();
                    members.forEach(m => {
                        placeMarker(m);
                        if (m.lat && m.lng) bounds.extend({ lat: m.lat, lng: m.lng });
                    });
                    if (members.length === 1) {
                        map.setCenter({ lat: members[0].lat, lng: members[0].lng });
                        map.setZoom(15);
                    } else if (members.length > 1) {
                        map.fitBounds(bounds, { top: 60, right: 60, bottom: 80, left: 60 });
                    }
                }
            });
        },

        // Refresh all pins after LoadLiveMembers() re-fetches from API
        updateMarkers(members) {
            if (!map) return;
            const incomingIds = new Set((members || []).map(m => String(m.id)));

            // Remove pins for members no longer clocked in
            for (const id in memberMarkers) {
                if (!incomingIds.has(String(id))) {
                    memberMarkers[id].setMap(null);
                    delete memberMarkers[id];
                }
            }

            // Move existing pins or add new ones
            (members || []).forEach(m => {
                if (!m.lat || !m.lng) return;
                if (memberMarkers[m.id]) {
                    memberMarkers[m.id].setPosition({ lat: m.lat, lng: m.lng });
                } else {
                    placeMarker(m);
                }
            });
        },

        flyTo(lat, lng) {
            if (!map) return;
            map.panTo({ lat, lng });
            map.setZoom(16);
        },

        selectMember(id) {
            for (const mid in memberMarkers) {
                const sel = String(mid) === String(id);
                memberMarkers[mid].setIcon(memberIcon(sel));
                memberMarkers[mid].setZIndex(sel ? 20 : 10);
            }
        },

        // Draw orange polyline + dots for a member's route
        drawRoute(points) {
            if (!map) return;
            this.clearRoute();
            if (!points || points.length === 0) return;

            routePolyline = new google.maps.Polyline({
                path:          points.map(p => ({ lat: p.lat, lng: p.lng })),
                geodesic:      true,
                strokeColor:   '#ff7a00',
                strokeOpacity: 0.85,
                strokeWeight:  3,
                map,
            });

            points.forEach((p, i) => {
                const isEndpoint = (i === 0 || i === points.length - 1);
                const dot = new google.maps.Marker({
                    position: { lat: p.lat, lng: p.lng },
                    map,
                    icon: {
                        path:        google.maps.SymbolPath.CIRCLE,
                        scale:       isEndpoint ? 6 : 4,
                        fillColor:   i === points.length - 1 ? '#ff7a00' : '#ffffff',
                        fillOpacity: 1,
                        strokeColor: '#ff7a00',
                        strokeWeight: 2,
                    },
                    zIndex: 5,
                    title: p.recordedAt ? new Date(p.recordedAt).toLocaleTimeString() : '',
                });
                routeDots.push(dot);
            });

            const bounds = new google.maps.LatLngBounds();
            points.forEach(p => bounds.extend({ lat: p.lat, lng: p.lng }));
            map.fitBounds(bounds, { top: 60, right: 80, bottom: 60, left: 80 });
        },

        // Alias so any existing Blazor calls to showRoute still work
        showRoute(points) { this.drawRoute(points); },

        clearRoute() {
            if (routePolyline) { routePolyline.setMap(null); routePolyline = null; }
            routeDots.forEach(d => d.setMap(null));
            routeDots = [];
        },

        setStyle(satellite) {
            mapTypeId = satellite ? 'satellite' : 'roadmap';
            if (map) map.setMapTypeId(mapTypeId);
        },

        showGeofences(locations) {
            if (!map) return;
            this.hideGeofences();
            window._gfCircles = (locations || []).map(loc => new google.maps.Circle({
                map,
                center:        { lat: loc.lat, lng: loc.lng },
                radius:        loc.radius,
                strokeColor:   '#6366f1',
                strokeOpacity: 0.7,
                strokeWeight:  2,
                fillColor:     '#6366f1',
                fillOpacity:   0.08,
            }));
        },

        hideGeofences() {
            if (window._gfCircles) {
                window._gfCircles.forEach(c => c.setMap(null));
                window._gfCircles = [];
            }
        },

        updateMember(id, lat, lng, name) {
            if (!map) return;
            if (memberMarkers[id]) {
                memberMarkers[id].setPosition({ lat, lng });
            } else {
                placeMarker({ id, name, lat, lng });
            }
        },
    };
})();


// ═══════════════════════════════════════════════════════════════════════════
//  LIVE LOCATION SENDER  (liveLocation)
//
//  Runs on the EMPLOYEE's browser (mobile or desktop).
//  Reads GPS from navigator.geolocation and POSTs to the API
//  every 60 seconds while the employee is clocked in.
//
//  Blazor calls:
//    After clock-in:  liveLocation.start(employeeId, "https://your-api.com")
//    After clock-out: liveLocation.stop()
// ═══════════════════════════════════════════════════════════════════════════
window.liveLocation = (function () {

    let _empId     = null;
    let _apiBase   = null;
    let _timerId   = null;   // 60-second heartbeat interval
    let _watchId   = null;   // watchPosition handle for continuous GPS
    let _lastLat   = null;
    let _lastLng   = null;
    let _isRunning = false;

    // Skip sending a new point if the employee moved less than this many metres
    const MIN_MOVE_METRES = 10;

    function distanceMetres(lat1, lng1, lat2, lng2) {
        const R    = 6371000;
        const dLat = (lat2 - lat1) * Math.PI / 180;
        const dLng = (lng2 - lng1) * Math.PI / 180;
        const a    = Math.sin(dLat / 2) ** 2 +
                     Math.cos(lat1 * Math.PI / 180) *
                     Math.cos(lat2 * Math.PI / 180) *
                     Math.sin(dLng / 2) ** 2;
        return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    }

    function sendPoint(position) {
        const lat = position.coords.latitude;
        const lng = position.coords.longitude;
        const acc = position.coords.accuracy;
        const spd = position.coords.speed;

        // Skip if the employee has barely moved
        if (_lastLat !== null && _lastLng !== null) {
            const dist = distanceMetres(_lastLat, _lastLng, lat, lng);
            if (dist < MIN_MOVE_METRES) {
                console.debug('[GPS] Skipped — moved only ' + dist.toFixed(1) + 'm');
                return;
            }
        }

        _lastLat = lat;
        _lastLng = lng;

        fetch(_apiBase + '/api/LiveLocation/update', {
            method:  'POST',
            headers: {
                'Content-Type': 'application/json',
                'ngrok-skip-browser-warning': 'true',
            },
            body:    JSON.stringify({
                employeeId: _empId,
                latitude:   lat,
                longitude:  lng,
                accuracy:   acc ?? null,
                speed:      spd ?? null,
            }),
        })
        .then(r => r.json())
        .then(data => {
            if (data.stored) {
                console.debug('[GPS] Stored — lat:' + lat.toFixed(5) + ' lng:' + lng.toFixed(5));
            } else {
                console.debug('[GPS] Server skipped — reason:', data.reason);
            }
        })
        .catch(err => console.warn('[GPS] Send failed:', err));
    }

    function onPosition(pos)  { sendPoint(pos); }
    function onError(err) {
        const msgs = {
            1: 'Permission denied — employee must allow location access.',
            2: 'Position unavailable — GPS signal lost.',
            3: 'Timeout — GPS took too long to respond.',
        };
        console.warn('[GPS]', msgs[err.code] || err.message);
    }

    return {

        // Call this right after a successful clock-in
        start(employeeId, apiBaseUrl) {
            if (_isRunning) {
                console.warn('[GPS] Already running — call stop() before start().');
                return;
            }
            if (!navigator.geolocation) {
                console.warn('[GPS] Geolocation not supported by this browser.');
                return;
            }

            _empId = employeeId;

            // ── Critical mobile fix ──────────────────────────────────────────
            // Blazor passes the ApiSettings.BaseUrl which is "https://localhost:7058".
            // On a desktop browser "localhost" = the development laptop → correct.
            // On a mobile phone "localhost" = the phone itself → wrong, nothing there.
            //
            // Fix: when we detect "localhost" or "127.0.0.1" in the API URL, replace
            // that hostname with window.location.hostname — the actual IP/hostname
            // the phone used to reach the Blazor app (e.g. "192.168.1.45").
            // The API port stays unchanged. This makes the GPS fetch go to the
            // laptop's real LAN address instead of the phone's own loopback.
            if (apiBaseUrl.includes('localhost') || apiBaseUrl.includes('127.0.0.1')) {
                try {
                    const parsed = new URL(apiBaseUrl);
                    _apiBase = parsed.protocol + '//' + window.location.hostname + ':' + parsed.port;
                    console.info('[GPS] Mobile fix — API rewritten: localhost → ' + _apiBase);
                } catch (e) {
                    _apiBase = apiBaseUrl; // fallback: use as-is
                }
            } else {
                _apiBase = apiBaseUrl;
            }

            _isRunning = true;
            _lastLat   = null;
            _lastLng   = null;

            console.info('[GPS] Started tracking for employee', employeeId, '→', _apiBase);

            // watchPosition gives us a continuous GPS stream from the hardware chip.
            // enableHighAccuracy:true requests the GPS chip on phones (not IP/Wi-Fi).
            // maximumAge:0 forces a fresh reading every time (no stale cached positions).
            _watchId = navigator.geolocation.watchPosition(onPosition, onError, {
                enableHighAccuracy: true,   // use GPS chip, not IP/Wi-Fi
                timeout:            20000,  // wait up to 20s for GPS lock
                maximumAge:         0,      // never use a cached position
            });

            // Heartbeat every 60 s — keeps "last seen" timestamp fresh
            // even if the employee is not moving.
            _timerId = setInterval(() => {
                navigator.geolocation.getCurrentPosition(onPosition, onError, {
                    enableHighAccuracy: true,
                    timeout:            20000,
                    maximumAge:         0,
                });
            }, 60000);
        },

        // Call this right after a successful clock-out
        stop() {
            if (_timerId)  { clearInterval(_timerId); _timerId = null; }
            if (_watchId)  { navigator.geolocation.clearWatch(_watchId); _watchId = null; }
            _isRunning = false;
            _lastLat   = null;
            _lastLng   = null;
            console.info('[GPS] Tracking stopped.');
        },

        isRunning() { return _isRunning; },
    };
})();


// ═══════════════════════════════════════════════════════════════════════════
//  MISC UTILITIES
// ═══════════════════════════════════════════════════════════════════════════

// Scroll the active cell to the centre of each time-picker column
window.scrollPickerColumns = function () {
    document.querySelectorAll('.tep-tp-col').forEach(col => {
        const sel = col.querySelector('.tep-tp-cell--sel');
        if (sel) {
            const offset = sel.offsetTop - (col.clientHeight / 2) + (sel.clientHeight / 2);
            col.scrollTo({ top: Math.max(0, offset), behavior: 'instant' });
        }
    });
};

// Select all text in an input (called on focus to mimic Jibble-style highlight)
window.selectInputText = function (inputId) {
    setTimeout(() => {
        const el = document.getElementById(inputId);
        if (el) { el.focus(); el.select(); }
    }, 20);
};
