(() => {
    const banner = document.querySelector('[data-demo-status]');
    if (!banner) return;
    const title = banner.querySelector('[data-demo-title]');
    const detail = banner.querySelector('[data-demo-detail]');
    const progress = banner.querySelector('[data-demo-progress]');
    const refresh = banner.querySelector('[data-demo-refresh]');
    refresh.addEventListener('click', () => window.location.reload());
    const labels = {
        waiting_for_pins: 'Waiting for demo PIN registration',
        preparing: 'Preparing demo data',
        verifying: 'Verifying demo data and photos',
        complete: 'Demo data is ready',
        retrying: 'Demo preparation paused — retrying automatically',
        skipped: 'Demo preparation skipped: a demo PIN was retired',
        disabled: 'Automatic demo preparation is disabled'
    };
    const poll = async () => {
        try {
            const response = await fetch(banner.dataset.statusUrl, { cache: 'no-store', headers: { Accept: 'application/json' }, signal: AbortSignal.timeout(10000) });
            if (response.status === 404 || response.status === 401 || response.status === 403 || response.redirected) {
                banner.hidden = true;
                return;
            }
            if (!response.ok) throw new Error('Monitoring unavailable');
            const status = await response.json();
            if (!(status.state in labels)) throw new Error('Unknown preparation status');
            if (status.state === 'complete') {
                banner.hidden = true;
                return;
            }
            banner.hidden = false;
            banner.dataset.state = status.state;
            title.textContent = labels[status.state];
            const counts = `${status.measurements}/${status.expectedMeasurements} pulse readings · ${status.environmentReadings}/${status.expectedEnvironmentReadings} cabin readings · ${status.uploadedPhotos}/${status.expectedPhotos} photos`;
            detail.textContent = status.state === 'waiting_for_pins'
                ? `${status.registeredPins}/${status.expectedPins} PINs registered. Preparation starts automatically.`
                : status.state === 'complete' ? `${counts}. Six-hour check-ins across 31 days.` : `${counts}. Histories may be incomplete until preparation finishes.`;
            progress.max = status.expectedMeasurements + status.expectedEnvironmentReadings + status.expectedPhotos;
            progress.value = status.measurements + status.environmentReadings + status.uploadedPhotos;
            progress.hidden = status.state === 'disabled' || status.state === 'skipped';
            refresh.hidden = status.state !== 'complete';
            if (status.state === 'complete' || status.state === 'skipped' || status.state === 'disabled') return;
        } catch {
            banner.hidden = false;
            banner.dataset.state = 'unavailable';
            title.textContent = 'Monitoring connection unavailable';
            detail.textContent = 'Waiting to reconnect and check demo preparation.';
            progress.hidden = true;
            refresh.hidden = true;
        }
        window.setTimeout(poll, 3000);
    };
    poll();
})();
