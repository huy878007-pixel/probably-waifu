"""Exercise manual review in an isolated headless Chrome; no personal browser state."""
import json
from pathlib import Path
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT.parents[1] / 'PortraitMaterialPolish' / 'artifacts'
OUT.mkdir(parents=True, exist_ok=True)


def main():
    errors, checks = [], []
    with sync_playwright() as p:
        browser = p.chromium.launch(channel='chrome', headless=True)
        page = browser.new_page(viewport={'width': 1700, 'height': 1000}, accept_downloads=True)
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.goto(ROOT.as_uri() + '/index.html#eleanor')
        page.wait_for_function('!!window.__WB__')
        before = page.evaluate('JSON.stringify(window.__WB__.map)')
        page.locator('#btnReview').click()
        assert page.locator('#humanReview').is_visible()
        assert page.locator('#auditCharacter').input_value() == 'eleanor'
        checks.append('opens current character without changing mapping')
        # First dialogue, after the entry slot.
        page.locator('#auditItem').select_option('1')
        item = page.locator('#auditItem').input_value()
        key = page.locator('#auditContext code').inner_text()
        candidate = page.locator('#auditGallery [data-portrait]').last.get_attribute('data-portrait')
        page.locator('#auditGallery [data-portrait]').last.click()
        assert page.evaluate('JSON.stringify(window.__WB__.map)') == before
        assert page.locator('#auditItem').input_value() == item
        checks.append('candidate preview never assigns or advances')
        page.locator('#auditApply').click()
        saved = page.evaluate('window.__WB__.map')
        assert saved['eleanor']['lines'][key] == candidate
        assert len(saved['eleanor']['lines']) == 1
        assert page.locator('#auditItem').input_value() == item
        checks.append('confirmation assigns exactly one line without jumping')
        assert page.locator('#auditComparison figure').count() == 3
        assert page.locator('#auditComparison figure').first.locator('img').get_attribute('src').startswith('../packs/core/')
        checks.append('runtime source is actual package image, separate from candidates')
        page.locator('#auditBackground').select_option('light')
        assert page.locator('.audit-canvas.light').count() == 3
        page.locator('#auditStatus').select_option('fix')
        page.locator('#auditIssues input[value="2"]').check()
        page.locator('#auditNote').fill('左侧袖口裁断，需要保留原衣服。')
        with page.expect_download() as d:
            page.locator('#auditExport').click()
        report_path = OUT / 'review-export-test.json'
        d.value.save_as(report_path)
        report = json.loads(report_path.read_text(encoding='utf-8'))
        rec = next(r for r in report['records'].values() if r['kind']=='line')
        assert rec['note'].startswith('左侧袖口') and rec['issues'] == ['裁切边缘']
        assert rec['scene'] == page.locator('#auditScene').input_value()
        assert len(report['explicitEdits']) == 1 and report['explicitEdits'][0]['portrait'] == candidate
        checks.append('issues and notes export with exact scene and line')
        # End-of-dialogue expectation must not change game mappings.
        end = page.locator('#auditItem option').filter(has_text='对话结束').get_attribute('value')
        page.locator('#auditItem').select_option(end)
        existing = page.evaluate('JSON.stringify(window.__WB__.map)')
        page.locator('#auditGallery [data-portrait]').first.click()
        page.locator('#auditApply').click()
        assert page.evaluate('JSON.stringify(window.__WB__.map)') == existing
        assert any(r.get('expectedPortrait') for r in page.evaluate("JSON.parse(localStorage.getItem('dsh-portrait-review-v1'))").values())
        checks.append('end expectation recorded without changing runtime semantics')
        # An entry is its own slot, not a bulk dialogue operation.
        page.locator('#auditItem').select_option('0')
        page.locator('#auditGallery [data-portrait]').last.click()
        page.locator('#auditApply').click()
        assert len(page.evaluate('window.__WB__.map.eleanor.lines')) == 1
        checks.append('entry slot changes do not overwrite dialogue lines')
        page.locator('#auditItem').select_option('1')
        assert page.locator('#auditNote').input_value().startswith('左侧袖口')
        page.screenshot(path=str(OUT / 'workbench-review-desktop.png'))
        # Closing the dialog with Escape must not trigger the editor's clear shortcut.
        page.locator('#auditItem').focus()
        page.keyboard.press('Escape')
        assert not page.locator('#humanReview').is_visible()
        page.locator('#btnUndo').click()
        assert page.evaluate('window.__WB__.map.eleanor.lines')[key] == candidate
        checks.append('existing undo remains available')
        page.reload()
        page.locator('#btnReview').click()
        page.locator('#auditItem').select_option('1')
        assert page.locator('#auditNote').input_value().startswith('左侧袖口')
        assert page.evaluate('window.__WB__.map.eleanor.lines')[key] == candidate
        checks.append('mapping and independent review persist on reload')
        page.once('dialog', lambda dialog: dialog.accept())
        page.locator('#auditImport').set_input_files(str(report_path))
        assert '已合并' in page.locator('#auditMessage').inner_text()
        checks.append('review import roundtrip does not modify mappings')
        page.locator('#auditCharacter').select_option('Mentor')
        page.locator('#auditItem').select_option('1')
        page.locator('#auditGallery [data-portrait]').first.click()
        page.locator('#auditApply').click()
        assert 'Mentor' in page.evaluate('window.__WB__.map')
        checks.append('cross-character review assigns the intended character')
        page.set_viewport_size({'width': 760, 'height': 1000})
        assert page.locator('#humanReview').evaluate('(e)=>e.scrollWidth <= e.clientWidth + 2')
        page.screenshot(path=str(OUT / 'workbench-review-mobile.png'))
        checks.append('narrow viewport has no horizontal dialog overflow')
        # Shared dialogue keys must remain scoped to this scene, not applied globally.
        page.set_viewport_size({'width':1700,'height':1000})
        shared = page.evaluate('''()=> {for(const c of __WB__.D.characters)for(const e of c.events)for(const l of e.lines)
            if(!e.foreign && l.spk==='char' && (c.sharedKeys||{})[l.key]) return {ck:c.key,ev:e.id,key:l.key};}''')
        if shared:
            page.locator('#auditCharacter').select_option(shared['ck'])
            page.locator('#auditScene').select_option(shared['ev'])
            item_index = page.evaluate('''(q)=> {const c=__WB__.D.characters.find(c=>c.key===q.ck),e=c.events.find(e=>e.id===q.ev);
                return __WB__.logic.entryList(__WB__.map,c.key,e).length+e.lines.filter(l=>l.spk==='char').findIndex(l=>l.key===q.key);}''',shared)
            page.locator('#auditItem').select_option(str(item_index))
            page.locator('#auditGallery [data-portrait]').last.click()
            page.locator('#auditApply').click()
            m=page.evaluate('window.__WB__.map')
            assert m[shared['ck']]['contexts'][shared['ev']][shared['key']]
            assert shared['key'] not in m[shared['ck']]['lines']
            checks.append('reused dialogue keys are scoped to the selected scene')
        protected=browser.new_page()
        protected.on('pageerror',lambda e: errors.append(str(e)))
        protected.add_init_script("localStorage.setItem('dsh-portrait-review-v1','broken-json');")
        protected.goto(ROOT.as_uri()+'/index.html#eleanor')
        protected.locator('#btnReview').click()
        assert '保护' in protected.locator('#auditSave').inner_text()
        protected.locator('#auditNote').fill('不能覆盖损坏数据')
        assert protected.evaluate("localStorage.getItem('dsh-portrait-review-v1')")=='broken-json'
        checks.append('invalid review storage is protected against overwrite')
        protected.close()
        assert page.evaluate('WB_SOURCE.newSourceLines') == 34
        assert page.evaluate("__WB__.D.characters.flatMap(c=>c.events).filter(e=>e.id.startsWith('source|')).length") == 4
        checks.append('current-source supplement exposes 34 missing line references in 4 scenes')
        preserved = page.evaluate('''()=> {const D=__WB__.D,L=__WB__.logic,key='dialog_inspection_routine_1';
            const map={lou:{lines:{[key]:'lou-choice.png'},events:{}},leroy:{lines:{[key]:'leroy-choice.png'},events:{}}};
            const r=L.migrateMap(D,map);return r.map.lou.lines[key]==='lou-choice.png' && r.map.leroy.lines[key]==='leroy-choice.png';}''')
        assert preserved
        checks.append('same locale key across different characters preserves both manual choices')
        before_gap = page.evaluate('JSON.stringify(__WB__.map)')
        page.locator('#auditNextGap').click()
        assert '待核项' in page.locator('#auditMessage').inner_text()
        assert page.evaluate('JSON.stringify(__WB__.map)') == before_gap
        checks.append('next-gap navigation changes focus only, never portrait assignments')
        assert not errors, errors
        browser.close()
    (OUT / 'workbench-review-test.json').write_text(json.dumps({'passed':True,'checks':checks,'pageErrors':errors},ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'passed':True,'checks':checks},ensure_ascii=False))


if __name__ == '__main__':
    main()
