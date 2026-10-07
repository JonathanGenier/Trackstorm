"""Temporary TS-287 editing/validation driver; excluded from the delivered tree."""
from pathlib import Path
import base64
import json
import os
import re
import subprocess
import sys
import urllib.request

TEMP = ('tools/ts-287-apply.py', '.github/workflows/ts-287-maintenance.yml')


def read(name):
    return Path(name).read_text(encoding='utf-8-sig')


def write(name, text):
    p = Path(name)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text.rstrip() + '\n', encoding='utf-8', newline='\n')


def replace_once(text, old, new):
    if text.count(old) != 1:
        raise RuntimeError(f'Expected exactly one patch location: {old[:100]!r}')
    return text.replace(old, new, 1)


def headings(text):
    offset, fence = 0, None
    result = []
    for line in text.splitlines(keepends=True):
        fm = re.match(r'^\s*(`{3,}|~{3,})', line)
        if fm:
            mark = fm.group(1)
            if fence is None:
                fence = mark
            elif mark[0] == fence[0] and len(mark) >= len(fence):
                fence = None
        elif fence is None:
            match = re.match(r'^(#{1,6}) (.+?)\s*$', line)
            if match:
                result.append((len(match[1]), match[2], offset))
        offset += len(line)
    return result


def slug(text):
    return re.sub(r'[^\w\s-]', '', text.lower()).replace(' ', '-')


def split_sections(name, mapping):
    original = read(name)
    top = [(title, start) for level, title, start in headings(original) if level == 2]
    actual = {title for title, _ in top}
    if set(mapping) - actual:
        raise RuntimeError(f'Missing sections in {name}: {set(mapping) - actual}')
    pieces = [original[:top[0][1]]]
    children = {}
    for index, (title, start) in enumerate(top):
        end = top[index + 1][1] if index + 1 < len(top) else len(original)
        section = original[start:end]
        target = mapping.get(title)
        if not target:
            pieces.append(section)
            continue
        children.setdefault(target, []).append(section)
        anchors = ''.join(f'<a id="{slug(t)}"></a>\n' for level, t, _ in headings(section) if level > 2)
        pieces.append(f'## {title}\n\n{anchors}[Read this section]({target}#{slug(title)}).\n\n')
    central = Path(name)
    for target, sections in children.items():
        body = ''.join(sections)
        redirected = body.replace('](#', f']({central.name}#')
        title = target.removesuffix('.md').replace('-', ' ').title()
        prefix = f'# {title}\n\nRead the [shared system contract]({central.name}) first. This reference contains only the selected subsystem; preserve its shared ownership and integration requirements.\n\n'
        write(central.parent / target, prefix + redirected)
    write(name, ''.join(pieces))
    print(f'CONTEXT {name}: {len(original.encode())} -> {len(read(name).encode())} bytes; {len(children)} focused references')


def apply():
    routes = read('tools/fast-check-routes.ps1')
    routes = replace_once(routes, '[string[]]$Paths\n', '[AllowEmptyCollection()][string[]]$Paths,\n        [switch]$IncludeFeatureDocHints\n')
    routes = replace_once(routes, '    $coreTests = $false', '    $workflowTests = $false\n    $coreTests = $false')
    start = routes.index('    $hasProductionChanges = ')
    end = routes.index('    foreach ($path in $normalized)', start)
    block = routes[start:end].replace('$normalized | Where-Object', '$productionPaths | Where-Object')
    routes = routes[:start] + "    $productionPaths = @($normalized | Where-Object { $_ -notmatch '\\.md$' })\n" + block + '    $hasProductionChanges = $hasProductionChanges -or $IncludeFeatureDocHints\n\n' + routes[end:]
    guard = """        # Feature-document-only edits stay cheap. Feature docs act as route hints only
        # when the same change also contains production/runtime files.
        if ($path -match '^docs/features/' -and -not $hasProductionChanges) {
            continue
        }
"""
    routes = replace_once(routes, guard, '')
    routes = replace_once(routes, '    foreach ($path in $normalized) {', """    # Focused feature references retain the owning document's integration hints.
    $normalized += @($normalized | Where-Object { $_ -match '^docs/features/(items|vehicles|developer-options)-.+\\.md$' } | ForEach-Object {
        if ($_ -match '^docs/features/(items|vehicles|developer-options)-') { "docs/features/$($Matches[1]).md" }
    })
    foreach ($path in $normalized) {
        # This guard must precede every feature route, including water and surfaces.
        if ($path -match '\\.md$' -and -not $hasProductionChanges) { continue }
        if ($path -match '^tools/.*\\.ps1$' -or $path -eq 'check.ps1' -or $path -match '^\\.github/workflows/') {
            $workflowTests = $true
        }
""")
    routes = replace_once(routes, '        CoreTests = $coreTests', '        HasProductionChanges = [bool]$hasProductionChanges\n        WorkflowTests = $workflowTests\n        CoreTests = $coreTests')
    write('tools/fast-check-routes.ps1', routes)
    tests = read('tools/test-workflow-tools.ps1')
    anchor = '    Copy-Item "$PSScriptRoot/fast-check-routes.ps1" (Join-Path $toolsDir "fast-check-routes.ps1")'
    tests = replace_once(tests, anchor, anchor + '\n    Copy-Item "$PSScriptRoot/agent-checks.ps1" (Join-Path $toolsDir "agent-checks.ps1")')
    write('tools/test-workflow-tools.ps1', tests + '\n& "$PSScriptRoot/test-agent-tools.ps1"\n')
    wrappers = ['check-car-articulation.ps1', 'check-developer-options.ps1', 'check-menu.ps1', 'check-settings.ps1', 'check-vehicle.ps1', 'check-input.ps1', 'check-gdunit.ps1']
    for name in wrappers:
        before = read(name)
        if '$NoBuild' in before:
            raise RuntimeError(f'{name} already has a build contract; inspect rather than overwrite')
        if name == 'check-car-articulation.ps1':
            after = replace_once(before, '[switch]$Visual)', '[switch]$Visual, [switch]$NoBuild)')
        else:
            endparam = before.index('\n)')
            after = before[:endparam].rstrip() + ',\n    [switch]$NoBuild' + before[endparam:]
        match = re.search(r'(?m)^dotnet build [^\n]+\nif \(\$LASTEXITCODE -ne 0\) \{[^\n]+\}', after)
        if not match:
            raise RuntimeError(f'Unexpected build/exit structure in {name}')
        old = match.group()
        new = 'if (-not $NoBuild) {\n' + '\n'.join('    ' + line for line in old.splitlines()) + '\n}'
        after = replace_once(after, old, new)
        write(name, after)
    for name in ('.github/workflows/ci.yml', '.github/workflows/extended-ci.yml'):
        text = read(name)
        key = "          key: trackstorm-nuget-${{ runner.os }}-net10-${{ hashFiles('**/*.csproj', 'Directory.Build.props') }}"
        write(name, replace_once(text, key, key + '\n          restore-keys: |\n            trackstorm-nuget-${{ runner.os }}-net10-'))
    workflow = read('docs/workflow.md')
    start = workflow.index('Use the lowest trusted implementation-agent level')
    end = workflow.index('A normal implementation prompt should be close to:', start)
    workflow = workflow[:start] + 'Read the assigned Jira Story/Bug\'s `Recommended Model: <Model> — <Reasoning level>` before implementation. Preserve that recommendation; do not independently replace it using an older generic model matrix. The planner chooses the lowest-cost trusted model likely to succeed in one cycle. Light-tier models are not used for substantive work. A materially changed, explicitly approved scope requires an explained reassessment and a Jira update before a different recommendation is issued. If the current model is not identifiable, report that limitation rather than claiming enforcement.\n\n' + workflow[end:]
    start = workflow.index('When an engineering quality score is used,')
    end = workflow.index('The repository may already have created', start)
    workflow = workflow[:start] + '''Engineering and Astra scores are advisory, with targets of **6.0** and **8.0** respectively. A below-target score requires an explicit warning, not automatic rejection or a polish round. Optional cleanup alone never invalidates acceptable implementation.

The final PR verdict is exactly **PASS**, **BLOCKED**, or **INVALID**. PASS means acceptable implementation and every merge gate satisfied. BLOCKED means merge-readiness is incomplete (for example pending/approval-required CI, baseline/infra failure, missing verification/Astra evidence, or unresolved routine main/version maintenance). INVALID requires a concrete implementation defect: incorrect behavior, acceptance-criteria violation, regression, harmful scope change, unsafe architecture/ownership, or a material coverage gap. CI exposing a real code/test regression is INVALID, not merely BLOCKED. Always inspect current main before the verdict and apply safe synchronization/version corrections rather than only reporting that the branch is behind.

''' + workflow[end:]
    old = 'After explicit human acceptance of an applicable critique, perform final verification without new implementation changes.'
    new = 'After explicit human acceptance of a critique, confirm that the previously verified source, build configuration and relevant environment still match. Acceptance alone does not require a duplicate full local suite on an unchanged result. Evidence is not transferable across changed inputs: substantive corrections, relevant main integration changes, or changed build/runtime inputs require applicable re-verification and the required comprehensive final gate. CI remains independent.'
    workflow = replace_once(workflow, old, new)
    workflow = workflow.replace('Stories with a meaningful runtime/player/operational surface require this critique. Pure repository/process/tooling Stories may omit Astra critique when operational verification plus independent engineering review fully cover the changed behavior and no distinct experiential judgment exists; record the exemption in the Story verification report.', 'Every completed Story receives Astra runtime/experiential or feature-specific operational critique after final verification. Record missing or unavailable critique as N/A with the limitation; never manufacture a score or silently infer an exemption.')
    workflow = workflow.replace(' For an exempt pure tooling/process Story, proceed after successful operational verification and independent engineering review.', '')
    workflow = workflow.replace('Before final delivery, update its title/body', 'The delivery reviewer, not a fresh substantive implementation-agent run, should update its title/body before final delivery')
    workflow = workflow.replace('It compares the Story branch with current `main`, routes changes', 'It combines the committed Story difference with staged, unstaged and non-ignored untracked paths (including both sides of renames), then routes changes')
    workflow = workflow.replace('The router prints required playtest/manual scenarios', 'The router records required playtest/manual scenarios')
    workflow += '''
## Targeted-tool operation

Use `tools/check-fast.ps1 -Plan -Explain` to inspect routes without executing checks; add `-Json` for machine-readable output. Plan-only success is never test evidence. `tools/check-agent-environment.ps1` performs read-only prerequisite inspection; it neither installs tools nor disables package auditing. Missing native/media/device capabilities remain explicit.

Execution writes complete per-command stdout/stderr logs and `results.json` below `.godot/fast-checks/<run>/`. It records actual commands, parameters, exit codes, timing, source HEAD/dirty fingerprint, pending manual/runtime/extended work and checks not executed after failure. `-DetailedOutput` restores verbose console output. Store custom evidence outside the repository or in a Git-ignored directory. Do not include credentials in command arguments or committed evidence. An empty plan is not a pass; a completed targeted plan is not final Story verification.

For a runtime batch the router builds the current Debug solution once, then passes each supported build-skip switch. The mixed Debug/Release network-soak harness retains its own setup; GdUnit regression also retains its independent import behavior. Standalone harnesses still build by default. Source changes during execution invalidate the batch; do not edit the checkout or change SDK/native configuration while checks run. No build or verification success is reused from a previous invocation.

Use the structured records to summarize actual execution in the Story report. Human observations, authenticated/remote multiplayer, physical devices, experiential judgments and Astra results remain separately attributed; no script infers them from a passing build.

## Automated PR approval

A PR created by the repository's `GITHUB_TOKEN` workflow can have CI awaiting workflow approval. Inspect the actual run status. An authorized human approves required workflows through GitHub; do not create dummy commits, weaken security, or claim that approval-required CI has passed. Mechanical PR/version/index maintenance belongs to delivery review, not another substantive implementation-agent run.
'''
    write('docs/workflow.md', workflow)
    split_sections('docs/workflow.md', {
        'Story-only Git delivery': 'workflow-delivery.md',
        'Critique and final PR': 'workflow-delivery.md',
        'Automated PR approval': 'workflow-delivery.md',
        'Verification': 'workflow-verification.md',
        'Targeted-tool operation': 'workflow-verification.md',
    })
    common = read('docs/workflow.md')
    common = common.replace('This document owns assignment interpretation, Git delivery, feature-document maintenance and completion verification.', 'Read this shared assignment/scope contract first. Implementation also reads [verification](workflow-verification.md); delivery review/admin reads [delivery](workflow-delivery.md). These documents divide stage-specific detail without reducing any completion gate. Implementation agents do not perform PR prose, version or index-only maintenance; delivery review owns those mechanical corrections.')
    write('docs/workflow.md', common)
    critique = read('docs/critique.md')
    critique = critique.replace('Every completed Jira Story with a meaningful runtime/player/operational surface receives a Story critique', 'Every completed Jira Story receives a runtime/experiential or feature-specific operational Story critique')
    critique = critique.replace('- Pure repository/process/tooling Stories may omit Astra critique when there is no distinct experiential surface to evaluate and operational verification plus independent engineering review fully cover the changed behavior. Record that exemption in the Story verification report.', '- Tooling/process Stories are evaluated through their actual operational result. Unavailable Astra execution is reported as N/A and a missing gate, never a static-code score or an assumed exemption.')
    critique = critique.replace('FAIL', 'BELOW TARGET').replace('PASS', 'AT OR ABOVE TARGET')
    critique = critique.replace('Solid but below the Trackstorm quality gate', 'Solid but below the Trackstorm target')
    critique = critique.replace('still requires meaningful experiential/runtime refinement before acceptance', 'may benefit from experiential/runtime refinement; the human decides acceptance')
    critique = critique.replace('8.0 is passing.', '8.0 meets the target.')
    critique = critique.replace('The score advises the human;', 'Scores are advisory: a score below 8.0 alone never blocks or invalidates delivery and does not authorize another polish round. The score advises the human;')
    write('docs/critique.md', critique)
    agents = read('AGENTS.md')
    agents = replace_once(agents, 'Read [workflow](docs/workflow.md) for assignment scope, Jira authority and approved requirement changes, branch selection, verification, independent engineering review and delivery.', 'Read the shared [workflow](docs/workflow.md) for scope and authority. During implementation also read [verification](docs/workflow-verification.md); load [delivery](docs/workflow-delivery.md) for delivery administration or PR review, not every coding iteration.')
    write('AGENTS.md', agents)
    split_sections('docs/features/items.md', {
        'Sustained Machine Gun resource': 'items-machine-gun.md', 'Persistent Oil': 'items-oil.md',
        'Sustained Nitro resource': 'items-nitro.md', 'Magnetic Proxy Mine': 'items-proxy-mine.md',
        'Arcing Salvo': 'items-salvo.md', 'Vehicle rack presentation': 'items-rack.md',
        'Shield rear armor and persistent health': 'items-shield.md', 'Movable Shield world walls': 'items-shield.md',
    })
    split_sections('docs/features/vehicles.md', {
        'Terrain handling profiles': 'vehicles-ground-handling.md',
        'Health, Collision Damage and Combat Hooks': 'vehicles-collision.md',
        'Terrain landing recovery': 'vehicles-collision.md',
        'Current vehicle presentation': 'vehicles-presentation.md',
        'Player-controlled airborne rotation': 'vehicles-air-control.md',
        'Material identity observations': 'vehicles-ground-handling.md',
        'Nitro speed recovery': 'vehicles-ground-handling.md',
        'Interactive handling verification': 'vehicles-ground-handling.md',
        'Dirt cornering and delayed crash recovery': 'vehicles-ground-handling.md',
        'Deliberate surface braking and stationary handbrake': 'vehicles-ground-handling.md',
    })
    dev = read('docs/features/developer-options.md')
    catalog_started = False
    catalog = {}
    for level, title, _ in headings(dev):
        if level != 2:
            continue
        if title == 'Editable control-to-runtime mapping':
            catalog_started = True
        if catalog_started:
            catalog[title] = 'developer-options-catalog.md'
    split_sections('docs/features/developer-options.md', catalog)
    write('docs/features/developer-options.md', read('docs/features/developer-options.md') + '\nThe tuning catalog is a reference, not mandatory background for ordinary UI work. Locate the affected heading or stable key and read that section plus the shared authority/persistence contract.\n')
    index = read('docs/features/README.md')
    misplaced = re.search(r'^\| \[Post-match Application Flow\].*$', index, re.M)
    if misplaced:
        row = misplaced.group()
        index = index[:misplaced.start()] + index[misplaced.end():]
        index = index.replace('\n## Specialized routes', '\n' + row + '\n\n## Specialized routes', 1)
    write('docs/features/README.md', index)
    print('Applied bounded tooling, workflow, cache and content-routing changes. No production code changed.')


def git(*args):
    return subprocess.check_output(['git', *args], text=True, encoding='utf-8').strip()


def publish():
    # Only proposes a commit object. The reviewer alone advances the Story ref with a lease.
    head = git('rev-parse', 'HEAD')
    records = json.loads(read('.godot/ts-287-checks/results.json'))
    if any(r['Status'] != 'PASS' for r in records):
        raise RuntimeError('Required validation failed; refusing a proposed completion commit.')
    run_url = f"https://github.com/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']}"
    rows = '\n'.join(f"| `{r['Name']}` | {r['Status']} | {r['DurationSeconds']} |" for r in records)
    write('docs/verification/ts-287.md', f'''# TS-287 — Implementation-agent efficiency

## Implemented

Automatic checks include committed, staged, unstaged and non-ignored new paths; deletion/rename routing is retained. Documentation-only routing is guarded before native selection and tested across the feature catalog. Plan/explain/JSON mode executes no checks. Runtime batches build current Debug artifacts once and use supported build-skip parameters, with mixed-configuration soak setup retained. Full logs, structured actual results, pending/unexecuted gates and dirty-source fingerprints replace verbose success output. Standalone wrappers continue building by default.

Read-only preflight identifies available Git/.NET/Godot prerequisites and reports what it did not verify. Stage-specific workflow references align Jira model recommendations, advisory ratings, review verdicts and unchanged-evidence reuse. Large item, vehicle and tuning references are split without removing their shared contracts or historical incoming anchors. CI cache fallback reuses NuGet packages across version-only key changes; restore/audit and all CI gates remain enabled.

## Actual verification

Execution environment: Windows GitHub Actions. Source: `{head}` plus the exact maintenance patch applied in this run. Verification/report text is finalized after execution; independent CI must validate the delivered commit. [Execution logs]({run_url}).

| Command / check | Actual outcome | Seconds |
| --- | --- | --- |
{rows}

The targeted regression suite covers all feature documents, dirty-only Git work, committed/staged/unstaged/untracked changes, ignored files, deletion, rename, Unicode paths, source invalidation, plan-only operation, parameter quoting/switch binding, explicit nonzero exits, missing commands, warning-triggered failures, timeouts and unavailable required runtime. Negative fixtures intentionally fail; only their expected classifications make the containing suite pass.

## Limits and unverified areas

No production gameplay, networking protocol, physics or asset behavior changed. Native smoke coverage records invocation/integration evidence, not a new human gameplay acceptance. No physical device, authenticated EOS, remote multiplayer, performance benchmark or Codex-credit saving percentage is claimed. Local Codex speed mode, models, MCP configuration and account settings were not accessed or changed.

Astra Review: N/A — no independent Astra critique was available in this ChatGPT environment. This remains explicit rather than a manufactured score. Human review/acceptance, independent delivered-commit CI and current-main/version readiness remain required. No merge was performed.
''')
    index = read('docs/verification/README.md')
    if '(ts-287.md)' not in index:
        index = replace_once(index, '| Evidence | Reports |\n| --- | --- |', '| Evidence | Reports |\n| --- | --- |\n| Implementation-agent tooling and context routing | [TS-287 verification evidence](ts-287.md) |')
        write('docs/verification/README.md', index)
    for name in TEMP:
        Path(name).unlink(missing_ok=True)
    changed = set(subprocess.check_output(['git', 'diff', '--name-only', '-z', 'HEAD', '--']).decode().split('\0'))
    changed.update(subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', '-z']).decode().split('\0'))
    changed.discard('')
    wrappers = {'check-car-articulation.ps1', 'check-developer-options.ps1', 'check-menu.ps1', 'check-settings.ps1', 'check-vehicle.ps1', 'check-input.ps1', 'check-gdunit.ps1'}
    for name in changed:
        allowed = name in TEMP or name in wrappers or name in {'AGENTS.md', 'Directory.Build.props', 'export_presets.cfg', '.github/workflows/ci.yml', '.github/workflows/extended-ci.yml', 'tools/fast-check-routes.ps1', 'tools/test-workflow-tools.ps1', 'docs/critique.md', 'docs/verification/ts-287.md', 'docs/verification/README.md'} or name.startswith('docs/workflow') or name.startswith('docs/features/')
        if not allowed:
            raise RuntimeError(f'Unexpected edited path; refusing publication: {name}')
    subprocess.run(['git', 'diff', '--check'], check=True)
    repo = os.environ['GITHUB_REPOSITORY']
    def api(path, body):
        request = urllib.request.Request(f'https://api.github.com/repos/{repo}/{path}', data=json.dumps(body).encode(), method='POST', headers={'Authorization': 'Bearer ' + os.environ['GH_TOKEN'], 'Accept': 'application/vnd.github+json', 'Content-Type': 'application/json'})
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)
    entries = []
    for name in sorted(changed):
        file = Path(name)
        if file.exists():
            blob = api('git/blobs', {'encoding': 'base64', 'content': base64.b64encode(file.read_bytes()).decode()})['sha']
            entries.append({'path': name, 'mode': '100644', 'type': 'blob', 'sha': blob})
        else:
            entries.append({'path': name, 'mode': '100644', 'type': 'blob', 'sha': None})
    tree = api('git/trees', {'base_tree': git('rev-parse', 'HEAD^{tree}'), 'tree': entries})['sha']
    commit = api('git/commits', {'message': 'TS-287: Reduce verification and context overhead without removing coverage', 'tree': tree, 'parents': [head]})['sha']
    print('PROPOSED_COMMIT=' + commit)
    print('PROPOSED_TREE=' + tree)
    print('REF_NOT_UPDATED; reviewer must inspect and advance ts-287-jg with an expected-head lease.')


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == 'publish':
        publish()
    else:
        apply()
