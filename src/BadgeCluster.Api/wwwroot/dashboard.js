'use strict';
const byId = id => document.getElementById(id);
const number = new Intl.NumberFormat();
let stopped = false;
let timer;
async function refresh() {
  if (stopped || document.hidden) return;
  try {
    const response = await fetch('/api/v1/cluster', {cache: 'no-store', signal: AbortSignal.timeout(5000)});
    if (!response.ok) throw new Error('unavailable');
    const value = await response.json();
    if (!Number.isSafeInteger(value.samples) || !Number.isSafeInteger(value.inside) ||
        value.inside < 0 || value.inside > value.samples || !Number.isInteger(value.activeNodes) || value.activeNodes < 0)
      throw new Error('invalid snapshot');
    byId('pi').textContent = value.samples ? (4 * value.inside / value.samples).toFixed(6) : '—';
    byId('samples').textContent = number.format(value.samples);
    byId('active').textContent = number.format(value.activeNodes);
    const updated = value.updatedAt ? new Date(value.updatedAt) : null;
    const stale = updated && (Number.isNaN(updated.getTime()) || Date.now() - updated.getTime() >= 300000);
    byId('status').textContent = stale ? 'No recent contributions · snapshot is stale' :
      value.samples ? 'Cluster connected · latest accepted totals' : 'Cluster connected · waiting for contributions';
    byId('status').classList.toggle('stale', Boolean(stale));
    byId('updated').textContent = updated && !Number.isNaN(updated.getTime()) ? 'Last contribution: ' + updated.toLocaleString() : 'No contributions yet';
  } catch {
    byId('status').textContent = 'Connection interrupted · displayed values may be stale';
    byId('status').classList.add('stale');
  } finally {
    if (!stopped) timer = setTimeout(refresh, 15000);
  }
}
document.addEventListener('visibilitychange', () => {clearTimeout(timer); if (!document.hidden) refresh();});
window.addEventListener('pagehide', () => {stopped = true; clearTimeout(timer);});
window.addEventListener('pageshow', event => {if (event.persisted) {stopped = false; refresh();}});
refresh();
