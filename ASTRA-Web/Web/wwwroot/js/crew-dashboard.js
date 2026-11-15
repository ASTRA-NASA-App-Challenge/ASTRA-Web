(() => {
    const root = document.querySelector('[data-health-dashboard]');
    if (!root) return;
    const records = root.querySelector('.health-records');
    const tablePanel = records.querySelector('.health-table-scroll');
    let tableAnimation;
    records.dataset.animated = 'true';
    records.querySelector('summary').addEventListener('click', event => {
        event.preventDefault();
        const opening = !records.open || records.classList.contains('is-closing');
        const height = records.open ? tablePanel.getBoundingClientRect().height : 0;
        const margin = records.open ? getComputedStyle(tablePanel).marginTop : '0px';
        const opacity = records.open ? getComputedStyle(tablePanel).opacity : '0';
        tableAnimation?.cancel();
        records.open = true;
        records.classList.toggle('is-closing', !opening);
        const targetHeight = Math.min(tablePanel.scrollHeight, parseFloat(getComputedStyle(tablePanel).maxHeight));
        tableAnimation = tablePanel.animate([
            { height: `${height}px`, marginTop: margin, opacity },
            { height: opening ? `${targetHeight}px` : '0px', marginTop: opening ? '1rem' : '0px', opacity: opening ? 1 : 0 }
        ], { duration: 320, easing: 'ease-in-out', fill: 'both' });
        tableAnimation.onfinish = () => {
            records.open = opening;
            records.classList.remove('is-closing');
            tableAnimation.cancel();
            tableAnimation = null;
        };
    });
    let data = JSON.parse(document.getElementById('health-history-data').textContent);
    const chart = root.querySelector('[data-history-chart]');
    const selector = root.querySelector('[data-history-select]');
    const note = root.querySelector('[data-chart-note]');
    const ns = 'http://www.w3.org/2000/svg';
    const el = (name, attrs, text) => { const node = document.createElementNS(ns, name); Object.entries(attrs).forEach(([key, value]) => node.setAttribute(key, value)); if (text) node.textContent = text; return node; };
    let activePopup;
    let activePoint;
    const closePopup = () => {
        if (activePopup && activePoint) {
            const popup = document.getElementById(activePoint.getAttribute('aria-describedby'));
            if (popup) {
                const leaving = popup.cloneNode(true);
                leaving.removeAttribute('id');
                leaving.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'));
                leaving.setAttribute('aria-hidden', 'true');
                leaving.inert = true;
                leaving.style.pointerEvents = 'none';
                const opacity = getComputedStyle(popup).opacity;
                popup.parentElement.append(leaving);
                const fade = leaving.animate([{opacity, scale:1}, {opacity:0, scale:.97}], {duration:180, easing:'ease-out', fill:'forwards'});
                fade.onfinish = () => leaving.remove();
            }
            activePopup.dispose();
        }
        activePoint?.setAttribute('aria-expanded', 'false');
        activePopup = null;
        activePoint = null;
    };
    document.addEventListener('click', event => {
        if (!event.target.closest('[data-history-chart] circle, .health-point-popup')) closePopup();
    });
    document.addEventListener('keydown', event => { if (event.key === 'Escape') closePopup(); });
    const render = () => {
        closePopup();
        chart.replaceChildren();
        const rows = data[selector.value].filter(x => x.value !== null && Number.isFinite(x.value));
        const unit = {pulse:'bpm', temperature:'°C', humidity:'%'}[selector.value];
        const table = root.querySelector('[data-history-table]');
        const showSignal = selector.value === 'pulse';
        root.querySelector('[data-history-signal-heading]').hidden = !showSignal;
        root.querySelector('[data-history-value-heading]').textContent = selector.selectedOptions[0].textContent;
        table.replaceChildren();
        const recorded = [...data[selector.value]].sort((a,b) => new Date(b.at) - new Date(a.at));
        recorded.forEach(row => {
            const tr = document.createElement('tr');
            const time = document.createElement('td');
            time.textContent = new Date(row.at).toLocaleString('en-GB', {timeZone:'UTC', month:'short', day:'numeric', hour:'2-digit', minute:'2-digit'});
            const value = document.createElement('td');
            value.textContent = row.value === null || !Number.isFinite(row.value) ? 'Not measured' : `${row.value} ${unit}`;
            tr.append(time, value);
            if (showSignal) {
                const signal = document.createElement('td');
                signal.textContent = row.quality || 'Unknown';
                tr.append(signal);
            }
            const statusCell = document.createElement('td');
            const status = document.createElement('span');
            status.className = 'health-history-status state-'+row.state;
            status.textContent = row.status;
            statusCell.append(status);
            tr.append(statusCell);
            table.append(tr);
        });
        if (!recorded.length) {
            const tr = document.createElement('tr');
            const cell = document.createElement('td');
            cell.colSpan = showSignal ? 4 : 3;
            cell.textContent = 'No measurements in this period.';
            tr.append(cell);
            table.append(tr);
        }
        if (!rows.length) { chart.textContent = 'Not measured — no history in the selected period.'; note.textContent = ''; root.querySelector('[data-history-summary]')?.remove(); return; }
        const values = rows.map(x => x.value), low = Math.min(...values), high = Math.max(...values);
        const padding = Math.max((high-low)*.15, 1), min = low-padding, max = high+padding;
        const first = new Date(rows[0].at).getTime(), last = new Date(rows.at(-1).at).getTime();
        const x = row => last===first ? 350 : 50+(new Date(row.at).getTime()-first)/(last-first)*600;
        const y = row => 200-(row.value-min)/(max-min)*175;
        const svg = el('svg',{viewBox:'0 0 700 240', role:'group', 'aria-label':selector.selectedOptions[0].textContent+' measurement history'});
        for(let i=0;i<4;i++) { const yy=25+i*175/3; svg.append(el('line',{x1:50,x2:650,y1:yy,y2:yy,stroke:'rgba(145,221,255,.12)'})); svg.append(el('text',{x:42,y:yy+4,'text-anchor':'end',fill:'#9fb4c4','font-size':14},(max-i*(max-min)/3).toFixed(1))); }
        svg.append(el('polyline',{points:rows.map(r=>`${x(r)},${y(r)}`).join(' '),fill:'none',stroke:'#91ddff','stroke-width':2}));
        rows.forEach(row => {
            const timestamp = new Date(row.at).toLocaleString('en-GB', {timeZone:'UTC'});
            const label = timestamp+' UTC: '+row.value+' '+unit+(row.quality ? ' · '+row.quality+' signal' : '');
            const dot = el('circle', {cx:x(row), cy:y(row), r:6, fill:({good:'#86e3b3',watch:'#c3e874',caution:'#f6d567',high:'#ff9c52',critical:'#ff536b',unknown:'#93a9ba'})[row.state] || '#93a9ba', tabindex:0, role:'button', 'aria-label':label, 'aria-expanded':'false'});
            const open = () => {
                if (activePoint === dot) { closePopup(); return; }
                closePopup();
                activePoint = dot;
                const details = document.createElement('dl');
                details.className = 'health-point-details';
                const field = (name, value) => {
                    const group = document.createElement('div');
                    const term = document.createElement('dt');
                    const description = document.createElement('dd');
                    term.textContent = name;
                    description.textContent = value;
                    group.append(term, description);
                    details.append(group);
                };
                field('Value', `${row.value} ${unit}`);
                field('Status', row.status);
                field('Recorded (UTC)', timestamp);
                if (row.quality) field('Pulse sensor quality', row.quality);
                activePopup = new bootstrap.Popover(dot, {
                    container:chart, trigger:'manual', placement:'top', customClass:'health-point-popup',
                    title:selector.selectedOptions[0].textContent,
                    content:details, html:true
                });
                dot.setAttribute('aria-expanded','true');
                activePopup.show();
            };
            dot.addEventListener('click', open);
            dot.addEventListener('keydown', event => {
                if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); open(); }
            });
            svg.append(dot);
        });
        const date = at=>new Date(at).toLocaleString('en-GB',{timeZone:'UTC',month:'short',day:'numeric',hour:'2-digit',minute:'2-digit'});
        svg.append(el('text',{x:50,y:225,fill:'#9fb4c4','font-size':14},date(rows[0].at)),el('text',{x:650,y:225,'text-anchor':'end',fill:'#9fb4c4','font-size':14},date(rows.at(-1).at)));
        chart.append(svg);
        svg.animate([{opacity:0, transform:"translateY(6px)"}, {opacity:1, transform:"translateY(0)"}], {duration:260, easing:"ease-out"});
        if (records.open) table.animate([{opacity:.4}, {opacity:1}], {duration:220, easing:"ease-out"});
        note.textContent='All times UTC · Select a point for details.';
        let summary = root.querySelector('[data-history-summary]');
        if (!summary) { summary=document.createElement('p'); summary.dataset.historySummary=''; summary.className='health-summary'; chart.after(summary); }
        
        summary.textContent=`Average ${(values.reduce((a,b)=>a+b,0)/values.length).toFixed(1)} ${unit} · Lowest ${low} · Highest ${high}`;
        if (selector.value==='pulse' && rows.some(x=>x.quality!=='good')) summary.textContent+=' · Includes low-quality readings (grey).';
    };
    selector.addEventListener('change', render); render();
    const periodNav = root.querySelector('.health-period');
    const statistics = periodNav.closest('.astra-dashboard__card--stats');
    const periodStatus = root.querySelector('[data-history-period-status]');
    let periodRequest;
    let periodRequestId = 0;
    let changingPeriod = false;
    periodNav.addEventListener('click', async event => {
        const link = event.target.closest('a');
        if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        if (link.getAttribute('aria-current') === 'page' && !changingPeriod) return;
        periodRequest?.abort();
        const requestId = ++periodRequestId;
        const controller = new AbortController();
        periodRequest = controller;
        const timeout = setTimeout(() => controller.abort(), 35000);
        changingPeriod = true;
        closePopup();
        statistics.setAttribute('aria-busy', 'true');
        statistics.classList.add('is-loading');
        chart.inert = true;
        periodStatus.textContent = 'Loading history…';
        try {
            // Reuse the existing authorized page route for every viewer role.
            // Parse its data only; never replace the document or execute returned scripts.
            const response = await fetch(link.href, {signal:controller.signal, cache:'no-store', credentials:'same-origin'});
            if (requestId !== periodRequestId) return;
            if (response.redirected) { location.assign(response.url); return; }
            if (!response.ok) throw new Error('History unavailable');
            const page = new DOMParser().parseFromString(await response.text(), 'text/html');
            const payload = page.getElementById('health-history-data');
            if (!payload) throw new Error('History unavailable');
            const next = JSON.parse(payload.textContent);
            if (next.error || !['pulse','temperature','humidity'].every(key => Array.isArray(next[key]))) throw new Error('History unavailable');
            if (requestId !== periodRequestId) return;
            data = next;
            document.getElementById('health-history-data').textContent = payload.textContent;
            root.querySelector('[data-history-period-label]').textContent = page.querySelector('[data-history-period-label]').textContent;
            periodNav.querySelectorAll('a').forEach(item => {
                if (item === link) item.setAttribute('aria-current', 'page');
                else item.removeAttribute('aria-current');
            });
            root.querySelector('[data-dashboard-refresh]').href = link.href;
            history.replaceState(null, '', link.href);
            render();
            periodStatus.textContent = '';
        } catch {
            if (requestId === periodRequestId) periodStatus.textContent = 'History could not be loaded. Your current view is unchanged. Try again.';
        } finally {
            clearTimeout(timeout);
            if (requestId === periodRequestId) {
                changingPeriod = false;
                periodRequest = null;
                statistics.removeAttribute('aria-busy');
                statistics.classList.remove('is-loading');
                chart.inert = false;
            }
        }
    });

    const countdown = root.querySelector('[data-check-countdown]');
    const tick = () => {
        const due = Date.parse(countdown.dataset.due);
        if (!Number.isFinite(due)) return;
        const remaining = due-Date.now();
        countdown.textContent=remaining<=0 ? 'Check-in overdue — take a new device reading.' : `Due in ${Math.floor(remaining/3600000)}h ${Math.floor(remaining/60000)%60}m ${Math.floor(remaining/1000)%60}s`;
    };
    tick(); setInterval(tick,1000);
    const photo = root.querySelector('[data-health-photo]');
    const photoError = () => { photo.hidden=true; root.querySelector('[data-photo-error]').hidden=false; };
    if(photo) { photo.addEventListener('error',photoError); if(photo.complete && !photo.naturalWidth) photoError(); }
    // Refresh only when the member is viewing the page and no modal is open.
    setInterval(()=>{if(!document.hidden && !document.querySelector('.modal.show') && !activePopup && !changingPeriod) location.reload();},60000);
})();

