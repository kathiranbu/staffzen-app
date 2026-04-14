// wwwroot/js/faceRecognition.js
// ─────────────────────────────────────────────────────────────────────────────
// Facial recognition helpers using @vladmandic/face-api (face-api.js).
// ─────────────────────────────────────────────────────────────────────────────

const FACE_MODEL_URL = 'https://cdn.jsdelivr.net/npm/@vladmandic/face-api/model';
let _modelsLoaded = false;
let _modelsLoadingPromise = null;

async function ensureModelsLoaded() {
    if (_modelsLoaded) return;
    if (_modelsLoadingPromise) return _modelsLoadingPromise;
    _modelsLoadingPromise = Promise.all([
        faceapi.nets.ssdMobilenetv1.loadFromUri(FACE_MODEL_URL),
        faceapi.nets.tinyFaceDetector.loadFromUri(FACE_MODEL_URL),
        faceapi.nets.faceLandmark68Net.loadFromUri(FACE_MODEL_URL),
        faceapi.nets.faceRecognitionNet.loadFromUri(FACE_MODEL_URL)
    ]).then(() => {
        _modelsLoaded = true;
        _modelsLoadingPromise = null;
        console.log('[FaceRecognition] Models loaded.');
    });
    return _modelsLoadingPromise;
}

window.preloadFaceModels = function () {
    ensureModelsLoaded().catch(err => console.warn('[FaceRecognition] Preload failed:', err));
};

window.areFaceModelsLoaded = function () { return _modelsLoaded; };

// ── Descriptor cache ───────────────────────────────────────────────────────
// Pre-parse enrolled descriptors once per session so loops never call
// JSON.parse + Float32Array construction on every frame.
const _descriptorCache = new Map(); // employeeId -> Float32Array

function getDescriptor(emp) {
    if (!emp.descriptor) return null;
    if (_descriptorCache.has(emp.employeeId)) return _descriptorCache.get(emp.employeeId);
    try {
        const arr = new Float32Array(JSON.parse(emp.descriptor));
        _descriptorCache.set(emp.employeeId, arr);
        return arr;
    } catch { return null; }
}

window.clearDescriptorCache = function () { _descriptorCache.clear(); };

// ── Head-pose yaw estimator (landmarks-based) ─────────────────────────────
// Returns estimated yaw in degrees (0 = frontal). Frames with yaw > 28-30
// produce poor descriptors and are skipped in all loops.
function estimateYaw(landmarks) {
    try {
        const pts       = landmarks.positions;
        const leftEyeX  = (pts[36].x + pts[39].x) / 2;
        const rightEyeX = (pts[42].x + pts[45].x) / 2;
        const noseTip   = pts[30].x;
        const eyeMidX   = (leftEyeX + rightEyeX) / 2;
        const eyeSpan   = rightEyeX - leftEyeX;
        if (eyeSpan < 1) return 0;
        return Math.abs((noseTip - eyeMidX) / eyeSpan) * 60;
    } catch { return 0; }
}

// ── Camera control ─────────────────────────────────────────────────────────
window.startCamera = async function (videoElementId) {
    try {
        const video  = document.getElementById(videoElementId);
        const stream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } }
        });
        video.srcObject = stream;
        await new Promise(resolve => { video.onloadedmetadata = resolve; });
        await video.play();
        return true;
    } catch (err) {
        console.error('[FaceRecognition] startCamera failed:', err);
        return false;
    }
};

window.stopCamera = function (videoElementId) {
    const video = document.getElementById(videoElementId);
    if (video && video.srcObject) {
        video.srcObject.getTracks().forEach(t => t.stop());
        video.srcObject = null;
    }
};

// ── Live detection loop state ──────────────────────────────────────────────
let _liveLoopActive = false;
let _liveCanvas     = null;

// ── ENROLLMENT LOOP ────────────────────────────────────────────────────────
// requiredHits 8->5: faster enrollment, same quality (minConfidence raised to
// compensate). Frame cadence: setTimeout(80ms) instead of rAF prevents a
// backlog of async detections queuing up at 60fps.
window.startLiveFaceDetection = async function (videoElementId, canvasElementId, dotNetRef, requiredHits = 5) {
    _liveLoopActive = false;
    await ensureModelsLoaded();

    const video  = document.getElementById(videoElementId);
    const canvas = document.getElementById(canvasElementId);
    if (!video || !canvas) { console.error('[FaceRecognition] startLiveFaceDetection: elements not found'); return; }

    _liveCanvas = canvas;
    const ctx   = canvas.getContext('2d');
    let hits     = 0;
    let captured = false;
    _liveLoopActive = true;

    const notifyProgress = async (pct) => {
        try { await dotNetRef.invokeMethodAsync('OnEnrollProgress', pct); } catch {}
    };

    const loop = async () => {
        if (!_liveLoopActive) return;

        if (canvas.width !== video.videoWidth || canvas.height !== video.videoHeight) {
            canvas.width  = video.videoWidth  || canvas.offsetWidth  || 320;
            canvas.height = video.videoHeight || canvas.offsetHeight || 240;
        }
        ctx.clearRect(0, 0, canvas.width, canvas.height);

        try {
            // minConfidence 0.65->0.72: only sharp, well-lit frames enrolled
            const detections = await faceapi
                .detectAllFaces(video, new faceapi.SsdMobilenetv1Options({ minConfidence: 0.72 }))
                .withFaceLandmarks()
                .withFaceDescriptors();

            if (detections.length > 1) {
                hits = 0;
                for (const det of detections) {
                    const b = det.detection.box;
                    const cx2 = b.x + b.width / 2, cy2 = b.y + b.height / 2;
                    const r2  = Math.max(b.width, b.height) * 0.62;
                    ctx.beginPath(); ctx.arc(cx2, cy2, r2, 0, 2 * Math.PI);
                    ctx.strokeStyle = '#f59e0b'; ctx.lineWidth = 3;
                    ctx.shadowColor = '#f59e0b'; ctx.shadowBlur = 12; ctx.stroke(); ctx.shadowBlur = 0;
                }
                ctx.font = 'bold 14px -apple-system, sans-serif'; ctx.fillStyle = '#f59e0b';
                ctx.textAlign = 'center'; ctx.shadowColor = 'rgba(0,0,0,0.9)'; ctx.shadowBlur = 5;
                ctx.fillText('Only one person should be in frame', canvas.width / 2, canvas.height - 14);
                ctx.shadowBlur = 0;
                await notifyProgress(0);
                if (_liveLoopActive) setTimeout(loop, 80);
                return;
            }

            const detection = detections.length === 1 ? detections[0] : null;

            if (detection) {
                // Skip tilted frames (yaw > 28 deg) — poor descriptor quality
                if (estimateYaw(detection.landmarks) > 28) {
                    const gcx = canvas.width / 2, gcy = canvas.height / 2;
                    const gr  = Math.min(canvas.width, canvas.height) * 0.36;
                    ctx.font = '13px -apple-system, sans-serif'; ctx.fillStyle = 'rgba(255,200,0,0.9)';
                    ctx.textAlign = 'center';
                    ctx.fillText('Face the camera directly', gcx, gcy + gr + 22);
                    if (_liveLoopActive) setTimeout(loop, 80);
                    return;
                }

                const box      = detection.detection.box;
                const cx       = box.x + box.width  / 2;
                const cy       = box.y + box.height / 2;
                const r        = Math.max(box.width, box.height) * 0.62;
                const progress = Math.min(hits / requiredHits, 1);
                const pct      = Math.round(progress * 100);
                const arcColor = `hsl(${Math.round(progress * 120)}, 90%, 55%)`;

                ctx.beginPath(); ctx.arc(cx, cy, r + 8, 0, 2 * Math.PI);
                ctx.strokeStyle = 'rgba(255,255,255,0.12)'; ctx.lineWidth = 12; ctx.stroke();

                if (progress > 0) {
                    ctx.beginPath();
                    ctx.arc(cx, cy, r + 8, -Math.PI / 2, -Math.PI / 2 + progress * 2 * Math.PI);
                    ctx.strokeStyle = arcColor; ctx.lineWidth = 6;
                    ctx.shadowColor = arcColor; ctx.shadowBlur = 14; ctx.stroke(); ctx.shadowBlur = 0;
                }

                ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI);
                ctx.strokeStyle = progress > 0.5 ? '#00e676' : 'rgba(255,255,255,0.6)';
                ctx.lineWidth = 2.5; ctx.stroke();

                if (pct > 0) {
                    ctx.font = `bold ${Math.round(r * 0.28)}px -apple-system, sans-serif`;
                    ctx.fillStyle = arcColor; ctx.textAlign = 'center';
                    ctx.shadowColor = 'rgba(0,0,0,0.8)'; ctx.shadowBlur = 6;
                    ctx.fillText(`${pct}%`, cx, cy - r - 18); ctx.shadowBlur = 0;
                }
                if (pct < 100) {
                    ctx.font = `${Math.round(r * 0.18)}px -apple-system, sans-serif`;
                    ctx.fillStyle = 'rgba(255,255,255,0.85)'; ctx.textAlign = 'center';
                    ctx.fillText('Hold still', cx, cy + r + 22);
                }

                hits++;
                await notifyProgress(Math.min(pct, 99));

                if (hits >= requiredHits && !captured) {
                    captured = true; _liveLoopActive = false;
                    ctx.fillStyle = 'rgba(0,230,118,0.35)'; ctx.fillRect(0, 0, canvas.width, canvas.height);
                    ctx.beginPath(); ctx.arc(cx, cy, r + 8, 0, 2 * Math.PI);
                    ctx.strokeStyle = '#00e676'; ctx.lineWidth = 6;
                    ctx.shadowColor = '#00e676'; ctx.shadowBlur = 18; ctx.stroke(); ctx.shadowBlur = 0;
                    await notifyProgress(100);

                    const descriptorJson = JSON.stringify(Array.from(detection.descriptor));
                    let enrollPhotoBase64 = '';
                    try {
                        const snap = document.createElement('canvas');
                        snap.width  = video.videoWidth  || 640;
                        snap.height = video.videoHeight || 480;
                        snap.getContext('2d').drawImage(video, 0, 0, snap.width, snap.height);
                        enrollPhotoBase64 = snap.toDataURL('image/jpeg', 0.85).split(',')[1] ?? '';
                    } catch {}
                    try { await dotNetRef.invokeMethodAsync('OnFaceCaptured', descriptorJson, enrollPhotoBase64); } catch {}
                    return;
                }
            } else {
                hits = Math.max(0, hits - 1);
                if (hits > 0) await notifyProgress(Math.round((hits / requiredHits) * 100));
                const cx2 = canvas.width / 2, cy2 = canvas.height / 2;
                const r2  = Math.min(canvas.width, canvas.height) * 0.38;
                ctx.beginPath(); ctx.arc(cx2, cy2, r2, 0, 2 * Math.PI);
                ctx.strokeStyle = 'rgba(255,255,255,0.3)'; ctx.lineWidth = 2;
                ctx.setLineDash([8, 6]); ctx.stroke(); ctx.setLineDash([]);
                ctx.font = '14px -apple-system, sans-serif'; ctx.fillStyle = 'rgba(255,255,255,0.7)';
                ctx.textAlign = 'center';
                ctx.fillText('Position your face here', cx2, cy2 + r2 + 22);
            }
        } catch {}

        if (_liveLoopActive) setTimeout(loop, 80);
    };

    setTimeout(loop, 80);
};

window.stopLiveFaceDetection = function (canvasElementId) {
    _liveLoopActive = false;
    const canvas = canvasElementId ? document.getElementById(canvasElementId) : _liveCanvas;
    if (canvas) canvas.getContext('2d').clearRect(0, 0, canvas.width, canvas.height);
};

// ── Manual capture fallback ────────────────────────────────────────────────
window.captureFaceDescriptor = async function (videoElementId) {
    try {
        await ensureModelsLoaded();
        const video = document.getElementById(videoElementId);
        const detection = await faceapi
            .detectSingleFace(video, new faceapi.SsdMobilenetv1Options({ minConfidence: 0.55 }))
            .withFaceLandmarks().withFaceDescriptor();
        if (!detection) return null;
        return JSON.stringify(Array.from(detection.descriptor));
    } catch (err) { console.error('[FaceRecognition] captureFaceDescriptor error:', err); return null; }
};

// ── Audit photo capture ────────────────────────────────────────────────────
window.captureAuditPhoto = function (videoElementId) {
    const video  = document.getElementById(videoElementId);
    const canvas = document.createElement('canvas');
    canvas.width  = video.videoWidth  || 320;
    canvas.height = video.videoHeight || 240;
    canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
    return canvas.toDataURL('image/jpeg', 0.85).split(',')[1];
};

// ── VERIFICATION LOOP (legacy URL-based) ──────────────────────────────────
window.startLiveFaceVerification = async function (videoElementId, canvasElementId, descriptorApiUrl, dotNetRef) {
    await ensureModelsLoaded();
    let storedDescriptor = null;
    try {
        const res = await fetch(descriptorApiUrl, { headers: { 'ngrok-skip-browser-warning': 'true' } });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const json = await res.json();
        if (json.descriptor) storedDescriptor = new Float32Array(JSON.parse(json.descriptor));
    } catch (e) { console.error('[FaceRecognition] Could not fetch descriptor:', e); }
    if (!storedDescriptor) {
        try { await dotNetRef.invokeMethodAsync('OnFaceVerified', false, 1.0); } catch {}
        return;
    }
    window.startLiveFaceVerificationWithDescriptor(
        videoElementId, canvasElementId,
        JSON.stringify(Array.from(storedDescriptor)), dotNetRef
    );
};

// ── Kiosk: match against multiple employees (no overlay) ──────────────────
window.matchFaceKiosk = async function (videoElementId, enrolledEmployees, threshold = 0.38) {
    try {
        await ensureModelsLoaded();
        const video = document.getElementById(videoElementId);
        const detection = await faceapi
            .detectSingleFace(video, new faceapi.TinyFaceDetectorOptions({ inputSize: 320, scoreThreshold: 0.5 }))
            .withFaceLandmarks().withFaceDescriptor();
        if (!detection) return { matched: false, employeeId: null, fullName: null, distance: 1.0 };
        let best = { employeeId: null, fullName: null, distance: 1.0 }, second = 1.0;
        for (const emp of enrolledEmployees) {
            const stored = getDescriptor(emp);
            if (!stored) continue;
            const d = faceapi.euclideanDistance(detection.descriptor, stored);
            if (d < best.distance) { second = best.distance; best = { employeeId: emp.employeeId, fullName: emp.fullName, distance: d }; }
            else if (d < second)   { second = d; }
        }
        const margin  = enrolledEmployees.length > 1 ? (second - best.distance) / second : 1;
        const isMatch = best.distance < threshold && margin >= 0.15;
        return { matched: isMatch, employeeId: isMatch ? best.employeeId : null, fullName: isMatch ? best.fullName : null, distance: Math.round(best.distance * 1000) / 1000 };
    } catch { return { matched: false, employeeId: null, fullName: null, distance: 1.0 }; }
};

// ── VERIFICATION LOOP with descriptor passed directly ─────────────────────
// Changes vs previous version:
//   REQUIRED_HITS     12 -> 6   (~2x faster confirmation)
//   FRAME_INTERVAL    rAF -> 80ms timer (prevents async detection queue)
//   MISMATCH handling hard reset -> soft decay by MISMATCH_DECAY=2 per frame
//   NO_FACE_TIMEOUT   120 -> 80 (~8s give-up)
//   OVERALL_TIMEOUT   40s -> 30s
//   NOTIFY_THROTTLE   500ms -> 200ms (more responsive progress bar)
//   minConfidence     0.60 -> 0.65
//   Landmark yaw check: skips frames where head is turned > 30 degrees
window.startLiveFaceVerificationWithDescriptor = async function (videoElementId, canvasElementId, descriptorJson, dotNetRef) {
    await ensureModelsLoaded();

    let storedDescriptor = null;
    try {
        storedDescriptor = new Float32Array(JSON.parse(descriptorJson));
    } catch (e) {
        console.error('[FaceRec] Failed to parse descriptor:', e);
        try { await dotNetRef.invokeMethodAsync('OnFaceStatus', 'no_face', 1.0); } catch {}
        return;
    }

    const video  = document.getElementById(videoElementId);
    const canvas = document.getElementById(canvasElementId);
    if (!video || !canvas) return;

    const ctx = canvas.getContext('2d');

    let matchHits      = 0;
    let mismatchFrames = 0;
    let noFaceFrames   = 0;
    let verified       = false;
    let lastStatus     = '';
    let lastNotifyTime = 0;

    const REQUIRED_HITS        = 6;
    const MISMATCH_BADGE_AFTER = 4;
    const MISMATCH_DECAY       = 2;
    const NO_FACE_TIMEOUT      = 80;
    const OVERALL_TIMEOUT_MS   = 30000;
    const THRESHOLD            = 0.38;
    const NOTIFY_THROTTLE_MS   = 200;
    const FRAME_INTERVAL_MS    = 80;

    _liveLoopActive = true;

    const _overallTimer = setTimeout(async () => {
        if (_liveLoopActive && !verified) {
            _liveLoopActive = false;
            try { await dotNetRef.invokeMethodAsync('OnFaceStatus', 'no_face', 1.0); } catch {}
        }
    }, OVERALL_TIMEOUT_MS);

    const notifyStatus = async (status, dist) => {
        if (status === lastStatus) return;
        const now = Date.now();
        if (now - lastNotifyTime < NOTIFY_THROTTLE_MS) return;
        lastNotifyTime = now; lastStatus = status;
        try { await dotNetRef.invokeMethodAsync('OnFaceStatus', status, dist); } catch {}
    };

    const loop = async () => {
        if (!_liveLoopActive || verified) return;

        if (canvas.width !== video.videoWidth || canvas.height !== video.videoHeight) {
            canvas.width  = video.videoWidth  || canvas.offsetWidth  || 320;
            canvas.height = video.videoHeight || canvas.offsetHeight || 240;
        }
        ctx.clearRect(0, 0, canvas.width, canvas.height);

        try {
            const detections = await faceapi
                .detectAllFaces(video, new faceapi.SsdMobilenetv1Options({ minConfidence: 0.65 }))
                .withFaceLandmarks()
                .withFaceDescriptors();

            if (detections.length > 1) {
                matchHits = 0; mismatchFrames = 0;
                for (const det of detections) {
                    const b = det.detection.box;
                    const cx2 = b.x + b.width / 2, cy2 = b.y + b.height / 2;
                    const r2  = Math.max(b.width, b.height) * 0.60;
                    ctx.beginPath(); ctx.arc(cx2, cy2, r2, 0, 2 * Math.PI);
                    ctx.strokeStyle = '#f59e0b'; ctx.lineWidth = 3.5;
                    ctx.shadowColor = '#f59e0b'; ctx.shadowBlur = 12; ctx.stroke(); ctx.shadowBlur = 0;
                }
                ctx.font = 'bold 14px -apple-system, sans-serif'; ctx.fillStyle = '#f59e0b';
                ctx.textAlign = 'center'; ctx.shadowColor = 'rgba(0,0,0,0.9)'; ctx.shadowBlur = 5;
                ctx.fillText('Only one person allowed in frame', canvas.width / 2, canvas.height - 14);
                ctx.shadowBlur = 0;
                await notifyStatus('multiple_faces', 1.0);
                if (_liveLoopActive) setTimeout(loop, FRAME_INTERVAL_MS);
                return;
            }

            const detection = detections.length === 1 ? detections[0] : null;

            if (detection) {
                noFaceFrames = 0;

                // Reject frames where head is significantly turned
                if (estimateYaw(detection.landmarks) > 30) {
                    const gcx = canvas.width / 2, gcy = canvas.height / 2;
                    const gr  = Math.min(canvas.width, canvas.height) * 0.36;
                    ctx.beginPath(); ctx.arc(gcx, gcy, gr, 0, 2 * Math.PI);
                    ctx.strokeStyle = 'rgba(255,200,0,0.5)'; ctx.lineWidth = 2;
                    ctx.setLineDash([8, 6]); ctx.stroke(); ctx.setLineDash([]);
                    ctx.font = '13px -apple-system, sans-serif'; ctx.fillStyle = 'rgba(255,200,0,0.9)';
                    ctx.textAlign = 'center';
                    ctx.fillText('Face the camera directly', gcx, gcy + gr + 22);
                    if (_liveLoopActive) setTimeout(loop, FRAME_INTERVAL_MS);
                    return;
                }

                const box      = detection.detection.box;
                const cx       = box.x + box.width  / 2;
                const cy       = box.y + box.height / 2;
                const r        = Math.max(box.width, box.height) * 0.60;
                const distance = faceapi.euclideanDistance(detection.descriptor, storedDescriptor);
                const isMatch  = distance < THRESHOLD;

                if (isMatch) {
                    mismatchFrames = 0;
                    matchHits++;

                    ctx.beginPath(); ctx.arc(cx, cy, r + 12, 0, 2 * Math.PI);
                    ctx.strokeStyle = 'rgba(34,197,94,0.20)'; ctx.lineWidth = 12; ctx.stroke();
                    ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI);
                    ctx.strokeStyle = '#22c55e'; ctx.lineWidth = 3.5;
                    ctx.shadowColor = '#22c55e'; ctx.shadowBlur = 14; ctx.stroke(); ctx.shadowBlur = 0;
                    ctx.fillStyle = 'rgba(34,197,94,0.08)';
                    ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI); ctx.fill();

                    const prog = Math.min(matchHits / REQUIRED_HITS, 1);
                    ctx.beginPath();
                    ctx.arc(cx, cy, r + 7, -Math.PI / 2, -Math.PI / 2 + prog * 2 * Math.PI);
                    ctx.strokeStyle = '#ffffff'; ctx.lineWidth = 3;
                    ctx.shadowColor = 'rgba(255,255,255,0.6)'; ctx.shadowBlur = 6; ctx.stroke(); ctx.shadowBlur = 0;

                    const pct = Math.round(prog * 100);
                    ctx.font = 'bold 13px -apple-system, sans-serif'; ctx.fillStyle = '#22c55e';
                    ctx.textAlign = 'center'; ctx.shadowColor = 'rgba(0,0,0,0.85)'; ctx.shadowBlur = 5;
                    ctx.fillText('Verifying\u2026 ' + pct + '%', cx, cy + r + 22); ctx.shadowBlur = 0;

                    await notifyStatus('scanning', distance);

                    if (matchHits >= REQUIRED_HITS) {
                        verified = true; _liveLoopActive = false;
                        ctx.fillStyle = 'rgba(34,197,94,0.28)'; ctx.fillRect(0, 0, canvas.width, canvas.height);
                        ctx.font = 'bold ' + Math.round(r * 0.7) + 'px -apple-system, sans-serif';
                        ctx.fillStyle = '#22c55e'; ctx.textAlign = 'center';
                        ctx.shadowColor = 'rgba(0,0,0,0.6)'; ctx.shadowBlur = 8;
                        ctx.fillText('\u2713', cx, cy + Math.round(r * 0.25)); ctx.shadowBlur = 0;

                        let selfieBase64 = null;
                        try {
                            const snap = document.createElement('canvas');
                            snap.width  = video.videoWidth  || 320;
                            snap.height = video.videoHeight || 240;
                            snap.getContext('2d').drawImage(video, 0, 0, snap.width, snap.height);
                            selfieBase64 = snap.toDataURL('image/jpeg', 0.82);
                        } catch {}

                        clearTimeout(_overallTimer);
                        try { await dotNetRef.invokeMethodAsync('OnFaceVerifiedWithSelfie', Math.round(distance * 1000) / 1000, selfieBase64 ?? ''); } catch {}
                        return;
                    }

                } else {
                    // Soft-decay instead of hard reset: one bad frame loses 2 hits,
                    // an imposter can never reach REQUIRED_HITS from zero.
                    matchHits = Math.max(0, matchHits - MISMATCH_DECAY);
                    mismatchFrames++;

                    ctx.beginPath(); ctx.arc(cx, cy, r + 12, 0, 2 * Math.PI);
                    ctx.strokeStyle = 'rgba(239,68,68,0.22)'; ctx.lineWidth = 12; ctx.stroke();
                    ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI);
                    ctx.strokeStyle = '#ef4444'; ctx.lineWidth = 3.5;
                    ctx.shadowColor = '#ef4444'; ctx.shadowBlur = 14; ctx.stroke(); ctx.shadowBlur = 0;
                    ctx.fillStyle = 'rgba(239,68,68,0.09)';
                    ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI); ctx.fill();

                    if (mismatchFrames >= MISMATCH_BADGE_AFTER) {
                        ctx.font = 'bold ' + Math.round(r * 0.38) + 'px -apple-system, sans-serif';
                        ctx.fillStyle = '#ef4444'; ctx.textAlign = 'center';
                        ctx.shadowColor = 'rgba(0,0,0,0.8)'; ctx.shadowBlur = 6;
                        ctx.fillText('\u2717', cx, cy + Math.round(r * 0.14)); ctx.shadowBlur = 0;
                        ctx.font = 'bold 13px -apple-system, sans-serif'; ctx.fillStyle = '#ef4444';
                        ctx.textAlign = 'center'; ctx.shadowColor = 'rgba(0,0,0,0.9)'; ctx.shadowBlur = 5;
                        ctx.fillText('Face mismatch', cx, cy + r + 22); ctx.shadowBlur = 0;
                        await notifyStatus('mismatch', Math.round(distance * 1000) / 1000);
                    }
                }

            } else {
                matchHits = 0; mismatchFrames = 0; noFaceFrames++;
                const cx2 = canvas.width / 2, cy2 = canvas.height / 2;
                const r2  = Math.min(canvas.width, canvas.height) * 0.36;
                ctx.beginPath(); ctx.arc(cx2, cy2, r2, 0, 2 * Math.PI);
                ctx.strokeStyle = 'rgba(255,255,255,0.28)'; ctx.lineWidth = 2;
                ctx.setLineDash([10, 7]); ctx.stroke(); ctx.setLineDash([]);
                ctx.font = '13px -apple-system, sans-serif'; ctx.fillStyle = 'rgba(255,255,255,0.65)';
                ctx.textAlign = 'center';
                ctx.fillText('Position your face here', cx2, cy2 + r2 + 22);
                await notifyStatus('no_face_scanning', 1.0);
                if (noFaceFrames >= NO_FACE_TIMEOUT) {
                    _liveLoopActive = false; clearTimeout(_overallTimer);
                    try { await dotNetRef.invokeMethodAsync('OnFaceStatus', 'no_face', 1.0); } catch {}
                    return;
                }
            }
        } catch {}

        if (_liveLoopActive) setTimeout(loop, FRAME_INTERVAL_MS);
    };

    setTimeout(loop, FRAME_INTERVAL_MS);
};

// ── Kiosk: match + draw green/red circle overlay on canvas ────────────────
// Speed: TinyFaceDetector (3-5x faster than SsdMobilenetv1 for snapshots),
//        descriptor cache (no JSON.parse per call).
// Accuracy: guide-circle position check, landmark yaw check,
//           margin check (best must be >= 15% better than second-best).
window.matchFaceKioskWithOverlay = async function (videoElementId, canvasElementId, enrolledEmployees, threshold = 0.38) {
    try {
        await ensureModelsLoaded();

        const video  = document.getElementById(videoElementId);
        const canvas = document.getElementById(canvasElementId);
        if (!video || !canvas) return { Matched: false, EmployeeId: null, FullName: null, Distance: 1.0, MultipleFaces: false };

        canvas.width  = video.videoWidth  || canvas.offsetWidth  || 360;
        canvas.height = video.videoHeight || canvas.offsetHeight || 270;

        const ctx = canvas.getContext('2d');
        ctx.clearRect(0, 0, canvas.width, canvas.height);

        // TinyFaceDetector: much faster for single-shot snapshot matching
        const detections = await faceapi
            .detectAllFaces(video, new faceapi.TinyFaceDetectorOptions({ inputSize: 320, scoreThreshold: 0.55 }))
            .withFaceLandmarks()
            .withFaceDescriptors();

        if (detections.length === 0) {
            const cx = canvas.width / 2, cy = canvas.height / 2;
            const r  = Math.min(canvas.width, canvas.height) * 0.36;
            ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI);
            ctx.strokeStyle = 'rgba(255,255,255,0.35)'; ctx.lineWidth = 2;
            ctx.setLineDash([8, 6]); ctx.stroke(); ctx.setLineDash([]);
            ctx.font = '14px -apple-system, sans-serif'; ctx.fillStyle = 'rgba(255,255,255,0.65)';
            ctx.textAlign = 'center'; ctx.fillText('No face detected', cx, cy + r + 22);
            return { Matched: false, EmployeeId: null, FullName: null, Distance: 1.0, MultipleFaces: false };
        }

        if (detections.length > 1) {
            for (const det of detections) {
                const b = det.detection.box;
                const cx2 = b.x + b.width / 2, cy2 = b.y + b.height / 2;
                const r2  = Math.max(b.width, b.height) * 0.62;
                ctx.beginPath(); ctx.arc(cx2, cy2, r2, 0, 2 * Math.PI);
                ctx.strokeStyle = '#f59e0b'; ctx.lineWidth = 4;
                ctx.shadowColor = '#f59e0b'; ctx.shadowBlur = 16; ctx.stroke(); ctx.shadowBlur = 0;
                ctx.fillStyle = 'rgba(245,158,11,0.10)';
                ctx.beginPath(); ctx.arc(cx2, cy2, r2, 0, 2 * Math.PI); ctx.fill();
            }
            ctx.font = 'bold 15px -apple-system, sans-serif'; ctx.fillStyle = '#f59e0b';
            ctx.textAlign = 'center'; ctx.shadowColor = 'rgba(0,0,0,0.9)'; ctx.shadowBlur = 6;
            ctx.fillText('\u26a0 Only one person allowed', canvas.width / 2, canvas.height - 16); ctx.shadowBlur = 0;
            return { Matched: false, EmployeeId: null, FullName: null, Distance: 1.0, MultipleFaces: true };
        }

        const detection = detections[0];

        // ── Guide-circle position check ────────────────────────────────────
        {
            const _b   = detection.detection.box;
            const _fcx = _b.x + _b.width  / 2;
            const _fcy = _b.y + _b.height / 2;
            const _gcx = canvas.width  / 2;
            const _gcy = canvas.height / 2;
            const _gr  = Math.min(canvas.width, canvas.height) * 0.36;
            if (Math.sqrt((_fcx - _gcx) ** 2 + (_fcy - _gcy) ** 2) > _gr * 1.4) {
                ctx.beginPath(); ctx.arc(_gcx, _gcy, _gr, 0, 2 * Math.PI);
                ctx.strokeStyle = 'rgba(255,255,255,0.35)'; ctx.lineWidth = 2;
                ctx.setLineDash([8, 6]); ctx.stroke(); ctx.setLineDash([]);
                ctx.font = '14px -apple-system, sans-serif'; ctx.fillStyle = 'rgba(255,255,255,0.65)';
                ctx.textAlign = 'center'; ctx.fillText('Move closer to the camera', _gcx, _gcy + _gr + 22);
                return { Matched: false, EmployeeId: null, FullName: null, Distance: 1.0, MultipleFaces: false };
            }
        }

        // ── Landmark yaw check ─────────────────────────────────────────────
        if (estimateYaw(detection.landmarks) > 30) {
            const gcx = canvas.width / 2, gcy = canvas.height / 2;
            const gr  = Math.min(canvas.width, canvas.height) * 0.36;
            ctx.beginPath(); ctx.arc(gcx, gcy, gr, 0, 2 * Math.PI);
            ctx.strokeStyle = 'rgba(255,200,0,0.5)'; ctx.lineWidth = 2;
            ctx.setLineDash([8, 6]); ctx.stroke(); ctx.setLineDash([]);
            ctx.font = '14px -apple-system, sans-serif'; ctx.fillStyle = 'rgba(255,200,0,0.9)';
            ctx.textAlign = 'center'; ctx.fillText('Face the camera directly', gcx, gcy + gr + 22);
            return { Matched: false, EmployeeId: null, FullName: null, Distance: 1.0, MultipleFaces: false };
        }

        // ── Match with margin check ────────────────────────────────────────
        let best   = { employeeId: null, fullName: null, distance: 1.0 };
        let second = 1.0;
        for (const emp of enrolledEmployees) {
            const stored = getDescriptor(emp);
            if (!stored) continue;
            const d = faceapi.euclideanDistance(detection.descriptor, stored);
            if (d < best.distance) { second = best.distance; best = { employeeId: emp.employeeId, fullName: emp.fullName, distance: d }; }
            else if (d < second)   { second = d; }
        }

        // Best match must be clearly better than second-best (>=15% margin).
        const margin  = enrolledEmployees.length > 1 ? (second - best.distance) / second : 1;
        const isMatch = best.distance < threshold && margin >= 0.15;

        const box = detection.detection.box;
        const cx  = box.x + box.width  / 2;
        const cy  = box.y + box.height / 2;
        const r   = Math.max(box.width, box.height) * 0.62;

        if (isMatch) {
            ctx.beginPath(); ctx.arc(cx, cy, r + 10, 0, 2 * Math.PI);
            ctx.strokeStyle = 'rgba(34,197,94,0.25)'; ctx.lineWidth = 10; ctx.stroke();
            ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI);
            ctx.strokeStyle = '#22c55e'; ctx.lineWidth = 4;
            ctx.shadowColor = '#22c55e'; ctx.shadowBlur = 16; ctx.stroke(); ctx.shadowBlur = 0;
            ctx.fillStyle = 'rgba(34,197,94,0.12)';
            ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI); ctx.fill();
            ctx.font = `bold ${Math.round(r * 0.35)}px -apple-system, sans-serif`;
            ctx.fillStyle = '#22c55e'; ctx.textAlign = 'center';
            ctx.shadowColor = 'rgba(0,0,0,0.8)'; ctx.shadowBlur = 6;
            ctx.fillText('\u2713', cx, cy - r - 10); ctx.shadowBlur = 0;
            ctx.font = 'bold 14px -apple-system, sans-serif'; ctx.fillStyle = '#22c55e';
            ctx.textAlign = 'center'; ctx.shadowColor = 'rgba(0,0,0,0.9)'; ctx.shadowBlur = 5;
            ctx.fillText(best.fullName || '', cx, cy + r + 22); ctx.shadowBlur = 0;
        } else {
            ctx.beginPath(); ctx.arc(cx, cy, r + 10, 0, 2 * Math.PI);
            ctx.strokeStyle = 'rgba(239,68,68,0.25)'; ctx.lineWidth = 10; ctx.stroke();
            ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI);
            ctx.strokeStyle = '#ef4444'; ctx.lineWidth = 4;
            ctx.shadowColor = '#ef4444'; ctx.shadowBlur = 16; ctx.stroke(); ctx.shadowBlur = 0;
            ctx.fillStyle = 'rgba(239,68,68,0.10)';
            ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI); ctx.fill();
            ctx.font = `bold ${Math.round(r * 0.35)}px -apple-system, sans-serif`;
            ctx.fillStyle = '#ef4444'; ctx.textAlign = 'center';
            ctx.shadowColor = 'rgba(0,0,0,0.8)'; ctx.shadowBlur = 6;
            ctx.fillText('\u2717', cx, cy - r - 10); ctx.shadowBlur = 0;
            ctx.font = 'bold 14px -apple-system, sans-serif'; ctx.fillStyle = '#ef4444';
            ctx.textAlign = 'center'; ctx.shadowColor = 'rgba(0,0,0,0.9)'; ctx.shadowBlur = 5;
            ctx.fillText('Face mismatch', cx, cy + r + 22); ctx.shadowBlur = 0;
        }

        return {
            Matched:       isMatch,
            EmployeeId:    isMatch ? best.employeeId : null,
            FullName:      isMatch ? best.fullName    : null,
            Distance:      Math.round(best.distance * 1000) / 1000,
            MultipleFaces: false
        };
    } catch (err) {
        console.error('[FaceRecognition] matchFaceKioskWithOverlay error:', err);
        return { Matched: false, EmployeeId: null, FullName: null, Distance: 1.0, MultipleFaces: false };
    }
};

// ── Clear kiosk canvas ─────────────────────────────────────────────────────
window.clearKioskCanvas = function (canvasElementId) {
    const canvas = document.getElementById(canvasElementId);
    if (canvas) canvas.getContext('2d').clearRect(0, 0, canvas.width, canvas.height);
};

// ── Draw mismatch overlay after Blazor re-render blanks the canvas ─────────
window.drawMismatchOverlay = function (canvasElementId) {
    const canvas = document.getElementById(canvasElementId);
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    const w = canvas.width || canvas.offsetWidth || 360;
    const h = canvas.height || canvas.offsetHeight || 270;
    const cx = w / 2, cy = h / 2, r = Math.min(w, h) * 0.36;
    ctx.clearRect(0, 0, w, h);
    ctx.beginPath(); ctx.arc(cx, cy, r + 10, 0, 2 * Math.PI);
    ctx.strokeStyle = 'rgba(239,68,68,0.3)'; ctx.lineWidth = 14; ctx.stroke();
    ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI);
    ctx.strokeStyle = '#ef4444'; ctx.lineWidth = 4;
    ctx.shadowColor = '#ef4444'; ctx.shadowBlur = 18; ctx.stroke(); ctx.shadowBlur = 0;
    ctx.fillStyle = 'rgba(239,68,68,0.12)';
    ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI); ctx.fill();
    ctx.font = `bold ${Math.round(r * 0.38)}px -apple-system, sans-serif`;
    ctx.fillStyle = '#ef4444'; ctx.textAlign = 'center';
    ctx.shadowColor = 'rgba(0,0,0,0.85)'; ctx.shadowBlur = 6;
    ctx.fillText('\u2717', cx, cy - r - 10); ctx.shadowBlur = 0;
    ctx.font = 'bold 15px -apple-system, sans-serif'; ctx.fillStyle = '#ef4444';
    ctx.textAlign = 'center'; ctx.shadowColor = 'rgba(0,0,0,0.85)'; ctx.shadowBlur = 5;
    ctx.fillText('Face mismatch', cx, cy + r + 26); ctx.shadowBlur = 0;
};

// ── Selfie-only capture loop ───────────────────────────────────────────────
// requiredHits 6->4, TinyFaceDetector (faster), 80ms cadence.
let _selfieLoopActive = false;

window.startSelfieCaptureLoop = async function (videoElementId, canvasElementId, dotNetRef, requiredHits = 4) {
    _selfieLoopActive = false;
    await ensureModelsLoaded();

    const video  = document.getElementById(videoElementId);
    const canvas = document.getElementById(canvasElementId);
    if (!video || !canvas) { console.error('[Selfie] startSelfieCaptureLoop: elements not found'); return; }

    const ctx = canvas.getContext('2d');
    let hits     = 0;
    let captured = false;
    _selfieLoopActive = true;

    const loop = async () => {
        if (!_selfieLoopActive) return;

        if (canvas.width !== video.videoWidth || canvas.height !== video.videoHeight) {
            canvas.width  = video.videoWidth  || canvas.offsetWidth  || 320;
            canvas.height = video.videoHeight || canvas.offsetHeight || 240;
        }
        ctx.clearRect(0, 0, canvas.width, canvas.height);

        try {
            const detection = await faceapi
                .detectSingleFace(video, new faceapi.TinyFaceDetectorOptions({ inputSize: 224, scoreThreshold: 0.45 }))
                .withFaceLandmarks();

            if (detection) {
                const box  = detection.detection.box;
                const cx   = box.x + box.width  / 2;
                const cy   = box.y + box.height / 2;
                const r    = Math.max(box.width, box.height) * 0.62;
                const prog = Math.min(hits / requiredHits, 1);
                const pct  = Math.round(prog * 100);
                const arcColor = `hsl(${Math.round(prog * 120)}, 90%, 55%)`;

                ctx.beginPath(); ctx.arc(cx, cy, r + 8, 0, 2 * Math.PI);
                ctx.strokeStyle = 'rgba(255,255,255,0.12)'; ctx.lineWidth = 12; ctx.stroke();

                if (prog > 0) {
                    ctx.beginPath();
                    ctx.arc(cx, cy, r + 8, -Math.PI / 2, -Math.PI / 2 + prog * 2 * Math.PI);
                    ctx.strokeStyle = arcColor; ctx.lineWidth = 6;
                    ctx.shadowColor = arcColor; ctx.shadowBlur = 14; ctx.stroke(); ctx.shadowBlur = 0;
                }
                ctx.beginPath(); ctx.arc(cx, cy, r, 0, 2 * Math.PI);
                ctx.strokeStyle = prog > 0.5 ? '#00e676' : 'rgba(255,255,255,0.6)';
                ctx.lineWidth = 2.5; ctx.stroke();

                if (pct > 0) {
                    ctx.font = `bold ${Math.round(r * 0.28)}px -apple-system, sans-serif`;
                    ctx.fillStyle = arcColor; ctx.textAlign = 'center';
                    ctx.shadowColor = 'rgba(0,0,0,0.8)'; ctx.shadowBlur = 6;
                    ctx.fillText(pct + '%', cx, cy - r - 18); ctx.shadowBlur = 0;
                }
                if (pct < 100) {
                    ctx.font = `${Math.round(r * 0.18)}px -apple-system, sans-serif`;
                    ctx.fillStyle = 'rgba(255,255,255,0.85)'; ctx.textAlign = 'center';
                    ctx.fillText('Hold still', cx, cy + r + 22);
                }

                hits++;
                if (hits >= requiredHits && !captured) {
                    captured = true; _selfieLoopActive = false;
                    ctx.fillStyle = 'rgba(0,230,118,0.35)'; ctx.fillRect(0, 0, canvas.width, canvas.height);
                    ctx.beginPath(); ctx.arc(cx, cy, r + 8, 0, 2 * Math.PI);
                    ctx.strokeStyle = '#00e676'; ctx.lineWidth = 6;
                    ctx.shadowColor = '#00e676'; ctx.shadowBlur = 18; ctx.stroke(); ctx.shadowBlur = 0;

                    let selfieBase64 = '';
                    try {
                        const snap = document.createElement('canvas');
                        snap.width  = video.videoWidth  || 320;
                        snap.height = video.videoHeight || 240;
                        snap.getContext('2d').drawImage(video, 0, 0, snap.width, snap.height);
                        selfieBase64 = snap.toDataURL('image/jpeg', 0.85);
                    } catch {}
                    try { await dotNetRef.invokeMethodAsync('OnSelfieCaptureDone', selfieBase64); } catch {}
                    return;
                }
            } else {
                hits = 0;
                const cx2 = canvas.width / 2, cy2 = canvas.height / 2;
                const r2  = Math.min(canvas.width, canvas.height) * 0.36;
                ctx.beginPath(); ctx.arc(cx2, cy2, r2, 0, 2 * Math.PI);
                ctx.strokeStyle = 'rgba(255,255,255,0.28)'; ctx.lineWidth = 2;
                ctx.setLineDash([10, 7]); ctx.stroke(); ctx.setLineDash([]);
                ctx.font = '13px -apple-system, sans-serif'; ctx.fillStyle = 'rgba(255,255,255,0.65)';
                ctx.textAlign = 'center'; ctx.fillText('Position your face here', cx2, cy2 + r2 + 22);
            }
        } catch {}

        if (_selfieLoopActive) setTimeout(loop, 80);
    };

    setTimeout(loop, 80);
};

window.stopSelfieCaptureLoop = function () { _selfieLoopActive = false; };
