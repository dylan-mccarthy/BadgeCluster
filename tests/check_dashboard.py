import json
import uuid
from pathlib import Path
from playwright.sync_api import sync_playwright

OUT = Path(__file__).resolve().parents[1] / 'artifacts'
OUT.mkdir(exist_ok=True)
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=['--no-sandbox'])
    page = browser.new_page(viewport={'width': 1360, 'height': 1100}, device_scale_factor=1)
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.goto('http://127.0.0.1:5080')
    baseline = page.request.get('http://127.0.0.1:5080/api/v1/cluster').json()
    page.locator('#status').filter(has_text='Cluster connected').wait_for()
    if baseline['samples'] == 0:
        assert page.locator('#pi').inner_text() == '—'
    page.screenshot(path=str(OUT / 'dashboard-before.png'), full_page=True)
    result = page.request.post('http://127.0.0.1:5080/api/v1/contributions', data=dict(
        protocolVersion=1, nodeId='node-browser-test', sessionId='session-' + uuid.uuid4().hex,
        sequence=1, samples=1000000, inside=785398))
    assert result.status == 200, result.text()
    page.reload()
    page.get_by_text('Cluster connected · latest accepted totals').wait_for()
    samples = baseline['samples'] + 1000000
    inside = baseline['inside'] + 785398
    assert page.locator('#pi').inner_text() == format(4 * inside / samples, '.6f')
    assert page.locator('#samples').inner_text() == format(samples, ',')
    page.screenshot(path=str(OUT / 'dashboard-desktop.png'), full_page=True)
    page.set_viewport_size({'width': 390, 'height': 844})
    page.screenshot(path=str(OUT / 'dashboard-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
    # Re-open against a broken API to verify cached totals stay visible as stale.
    page.route('**/api/v1/cluster', lambda route: route.fulfill(status=503, body='{}'))
    page.evaluate('document.dispatchEvent(new Event("visibilitychange"))')
    page.get_by_text('Connection interrupted · displayed values may be stale').wait_for()
    assert page.locator('#samples').inner_text() == format(samples, ',')
    assert page.locator('#status').get_attribute('class') == 'stale'
    assert not errors, errors
    print('Dashboard browser checks passed: empty/live snapshots, approximation, mobile overflow, stale cache; no JavaScript errors.')
    browser.close()
