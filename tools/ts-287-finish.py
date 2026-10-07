"""Temporary validation finalizer. Never advances a branch or merges a PR."""
from pathlib import Path, PureWindowsPath
import base64
import io
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile

ROOT = Path.cwd()
EVIDENCE = ROOT / '.godot/ts-287-checks'
REPO = 'JonathanGenier/Trackstorm'
BASE = '73c6811b56cc8ae3b05229d66b7ffdfcb0ab2911'
PREVIOUS = 'd3c1adb12af6a622bfb60fe3d89344fc56286592'
PREVIOUS_RUN = 'https://github.com/JonathanGenier/Trackstorm/actions/runs/37674293176'
TEMP = ['tools/ts-287-apply.py', 'tools/ts-287-finish.py', '.github/workflows/ts-287-maintenance.yml']


def git(*args):
    return subprocess.check_output(['git', *args], text=True, encoding='utf-8').strip()


def write(path, text):
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text.rstrip() + '\n', encoding='utf-8', newline='\n')


def previous():
    # Only validation-driver metadata changed after the completed full gate.
    subprocess.run(['git', 'diff', '--exit-code', PREVIOUS, 'HEAD', '--', 'tools/agent-checks.ps1', 'tools/check-fast.ps1', 'tools/check-agent-environment.ps1', 'tools/test-agent-tools.ps1', 'tools/ts-287-apply.py', 'code', 'check.ps1', 'Trackstorm.Client.csproj'], check=True)
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, req, fp, code, msg, headers, newurl):
            return None
    request = urllib.request.Request(f'https://api.github.com/repos/{REPO}/actions/artifacts/11505992292/zip', headers={'Authorization': 'Bearer ' + os.environ['GH_TOKEN'], 'Accept': 'application/vnd.github+json'})
    try:
        response = urllib.request.build_opener(NoRedirect).open(request, timeout=60)
        payload = response.read()
    except urllib.error.HTTPError as error:
        if error.code != 302:
            raise
        location = error.headers['Location']
        parsed = urllib.parse.urlparse(location)
        if parsed.scheme != 'https' or not any(parsed.hostname.endswith(suffix) for suffix in ('.blob.core.windows.net', '.actions.githubusercontent.com', '.githubusercontent.com')):
            raise RuntimeError('Unexpected artifact download origin.')
        # Do not forward GitHub credentials to the signed storage URL.
        with urllib.request.urlopen(location, timeout=60) as response:
            payload = response.read()
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(payload)) as archive:
        records = json.loads(archive.read('results.json').decode('utf-8-sig'))
        for record in records:
            record['Run'] = PREVIOUS_RUN
            record['SourceHead'] = PREVIOUS
            for key in ('StdoutLog', 'StderrLog'):
                basename = PureWindowsPath(record[key]).name
                target = EVIDENCE / 'previous' / basename
                target.parent.mkdir(exist_ok=True)
                target.write_bytes(archive.read(basename))
                record[key] = str(target)
    if not any(r['Name'] == 'check.ps1' and r['Status'] == 'PASS' for r in records):
        raise RuntimeError('Prior comprehensive gate did not pass.')
    write(EVIDENCE / 'results.json', json.dumps(records, indent=2))
    print('Imported actual prior-run evidence. No checks were relabeled or rerun by this import.')


def publish():
    records = json.loads((EVIDENCE / 'results.json').read_text(encoding='utf-8-sig'))
    auto = json.loads((ROOT / '.godot/ts-287-auto/results.json').read_text(encoding='utf-8-sig'))
    auto_failures = [r for r in auto['Checks'] if r['Status'] != 'PASS']
    if len(auto_failures) != 1 or auto_failures[0]['Name'] != 'check-car-articulation.ps1':
        raise RuntimeError('Automatic batch has an unexpected failure; inspect it before delivery.')
    car = next(r for r in records if r['Name'] == 'check-car-articulation.ps1 -NoBuild')
    baseline = next(r for r in records if r['Name'] == 'Unchanged main check-car-articulation.ps1')
    for record in (car, baseline, auto_failures[0]):
        if record['Status'] != 'FAIL':
            raise RuntimeError('Baseline comparison did not reproduce the strict failure.')
        text = re.sub(r'\x1b\[[0-9;]*m', '', Path(record['StdoutLog']).read_text(encoding='utf-8-sig'))
        for expected in ('Car articulation passed: 203 assertions.', 'WARNING: 24 ObjectDB instances were leaked', 'ERROR: 3 resources still in use'):
            if expected not in text:
                raise RuntimeError(f'Baseline diagnostic differs: {expected}')
    allowed = {'check-car-articulation.ps1 -NoBuild', 'check-fast.ps1 automatic runtime batch', 'Unchanged main check-car-articulation.ps1'}
    if any(r['Status'] != 'PASS' and r['Name'] not in allowed for r in records):
        raise RuntimeError('An additional check failed; no completion claim is allowed.')
    head = git('rev-parse', 'HEAD')
    rows = '\n'.join(f"| `{r['Name']}` | {r['Status']} | {r['DurationSeconds']} | [Run]({r['Run']}) |" for r in records)
    report = f'''# TS-287 — Implementation-agent efficiency

## Implemented

Automatic checks combine committed Story changes with staged, unstaged and non-ignored untracked files, retaining deletions and both sides of renames. Documentation-only guards now precede every native route and are tested against the feature catalog. Plan/explain/JSON mode executes no checks. Runtime batches prepare current Debug artifacts once, use supported build-skip parameters and retain mixed-configuration soak setup. Complete logs, compact output, actual command/exit/timing records, dirty-source fingerprints and explicit pending/unexecuted work improve handoff evidence. Standalone wrappers still build by default.

Read-only preflight inspects Git/.NET/Godot and explicitly identifies remaining native/media/device prerequisites. Workflow references now separate implementation verification from delivery administration, defer to Jira model recommendations, treat ratings as advisory and do not repeat the full gate solely because an unchanged result was accepted. Item, vehicle and tuning details move into focused references with shared contracts and existing incoming anchors retained. CI adds NuGet fallback cache restore without removing ordinary restore/audit, tests, native assertions or CI gates.

## Actual verification

Windows GitHub Actions, pinned Godot 4.7.2 .NET. The full repository gate and targeted regressions passed in [the first validation run]({PREVIOUS_RUN}) on `{PREVIOUS}` plus the deterministic maintenance patch. Only temporary validation-driver metadata changed afterward; a Git comparison confirmed the tested implementation and patch driver were unchanged. The full gate was not redundantly repeated. Fresh build/import and remaining native operational checks were executed in the subsequent run. The delivered report and removal of temporary validation files happen after those executions; independent delivered-commit CI remains required.

Current validation-driver source: `{head}`. Normal Story version is `0.2.34`, with both export fields `0.2.34.0`, relative to main `{BASE}`.

| Command / check | Actual outcome | Seconds | Evidence |
| --- | --- | --- | --- |
{rows}

## Confirmed baseline failure — not a passing native gate

The Story's `check-car-articulation.ps1 -NoBuild`, the automatic runtime batch, and **the unmodified script in a separately built/imported worktree at main `{BASE}`** all reported **203 passing articulation assertions**, then **24 leaked ObjectDB instances and 3 resources still in use at exit**. Each strict command remains FAIL. No warning or error filter was weakened and no production/fixture code was changed to hide this. The automatic runner correctly stopped, retained the failed result, and listed unexecuted checks; the remaining wrapper checks were then exercised independently.

This pre-existing native shutdown failure is a merge-readiness limitation, not evidence that TS-287 changed vehicle behavior. Baseline remediation was not silently added to this repository-maintenance Story.

## Coverage and limitations

Targeted regressions cover the feature documents, dirty-only Git work, committed/staged/unstaged/untracked changes, ignored files, deletion, rename, Unicode paths, source invalidation, plan-only operation, literal parameter/switch binding, nonzero exits, missing commands, warning-triggered failures, timeouts and unavailable required runtime. Expected negative fixtures are distinguished from the containing suite's passing result.

No production gameplay, physics, protocol, assets or release policy changed. Native commands establish operational/wrapper evidence, not human gameplay acceptance. No physical-device, authenticated EOS/Internet, remote multiplayer, sustained-performance or measured Codex-credit saving claim is made. Local Codex speed, model, MCP and account settings were not accessed or modified.

**Astra Review: N/A** — independent Astra critique was unavailable in this ChatGPT environment. The missing critique and baseline native failure remain explicit. Human acceptance, independent delivered-commit CI and current-main readiness are still required. **No merge was performed.**
'''
    write('docs/verification/ts-287.md', report)
    index_path = Path('docs/verification/README.md')
    index = index_path.read_text(encoding='utf-8-sig')
    marker = '| Evidence | Reports |\n| --- | --- |'
    if '(ts-287.md)' not in index:
        if index.count(marker) != 1:
            raise RuntimeError('Unexpected verification index structure.')
        index = index.replace(marker, marker + '\n| Implementation-agent tooling and context routing | [TS-287 verification evidence](ts-287.md) |')
        write(index_path, index)
    for path in TEMP:
        Path(path).unlink(missing_ok=True)
    changed = set(subprocess.check_output(['git', 'diff', '--name-only', '-z', 'HEAD', '--']).decode().split('\0'))
    changed.update(subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', '-z']).decode().split('\0'))
    changed.discard('')
    permitted = set(TEMP) | {'AGENTS.md', 'Directory.Build.props', 'export_presets.cfg', '.github/workflows/ci.yml', '.github/workflows/extended-ci.yml', 'tools/fast-check-routes.ps1', 'tools/test-workflow-tools.ps1', 'docs/critique.md', 'docs/verification/ts-287.md', 'docs/verification/README.md', 'check-car-articulation.ps1', 'check-developer-options.ps1', 'check-menu.ps1', 'check-settings.ps1', 'check-vehicle.ps1', 'check-input.ps1', 'check-gdunit.ps1'}
    for path in changed:
        if path not in permitted and not path.startswith(('docs/workflow', 'docs/features/')):
            raise RuntimeError(f'Unexpected modified path: {path}')
    subprocess.run(['git', 'diff', '--check'], check=True)
    def api(path, body):
        request = urllib.request.Request(f'https://api.github.com/repos/{REPO}/{path}', data=json.dumps(body).encode(), method='POST', headers={'Authorization': 'Bearer ' + os.environ['GH_TOKEN'], 'Accept': 'application/vnd.github+json', 'Content-Type': 'application/json'})
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)
    entries = []
    for name in sorted(changed):
        path = Path(name)
        sha = api('git/blobs', {'encoding': 'base64', 'content': base64.b64encode(path.read_bytes()).decode()})['sha'] if path.exists() else None
        entries.append({'path': name, 'mode': '100644', 'type': 'blob', 'sha': sha})
    tree = api('git/trees', {'base_tree': git('rev-parse', 'HEAD^{tree}'), 'tree': entries})['sha']
    commit = api('git/commits', {'message': 'TS-287: Deliver verified efficiency fixes with explicit native baseline limitation', 'tree': tree, 'parents': [head]})['sha']
    print('PROPOSED_COMMIT=' + commit)
    print('PROPOSED_TREE=' + tree)
    print('REF_NOT_UPDATED. Native baseline and missing Astra evidence remain merge-readiness limitations.')


if __name__ == '__main__':
    previous() if sys.argv[1] == 'previous' else publish()
