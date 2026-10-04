"""Audit canonical Shield naming while verifying immutable historical evidence."""
from pathlib import Path
import hashlib
import json
import re
import struct
import subprocess

root = Path(__file__).resolve().parents[3]
git = ['git', '-c', 'safe.directory=' + root.as_posix(), '-C', str(root)]
baseline = subprocess.check_output(git + ['rev-parse', 'origin/main']).decode().strip()
rows = subprocess.check_output(git + ['ls-tree', '-r', baseline, '--', 'docs/verification']).decode().splitlines()
historical = {}
for row in rows:
    metadata, name = row.split('\t', 1)
    if Path(name).suffix in {'.log', '.txt', '.json', '.csv', '.trx', '.xml'}:
        historical[name] = metadata.split()[2]
quoted = {}
for row in rows:
    _, name = row.split('\t', 1)
    if name.endswith('.md'):
        original = subprocess.check_output(git + ['show', baseline + ':' + name]).decode('utf-8-sig')
        quoted[name] = re.findall(r'```[\s\S]*?```|`[^`\n]+`', original)
names = set(subprocess.check_output(git + ['ls-files', '-z']).decode().split('\0'))
names.update(subprocess.check_output(git + ['ls-files', '--others', '--exclude-standard', '-z']).decode().split('\0'))
legacy = re.search(r'"items\.([a-z]+)_', (root / 'code/Core/Development/ConfigurationKeyMigration.cs').read_text()).group(1)
pattern = re.compile(legacy.encode(), re.I)
boundaries = {'code/Core/Development/ConfigurationKeyMigration.cs', 'code/Core/Events/EventCodec.cs',
              'code/Tests/Development/ShieldConfigurationMigrationTests.cs', 'code/Tests/ShieldJournalMigrationTests.cs'}
remaining = {}
excluded = []
quoted_exclusions = {}
historical_links = []
for name in sorted(names):
    path = root / name
    if not name or not path.is_file():
        continue
    data = path.read_bytes()
    if name in historical:
        blob = hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()
        assert blob == historical[name], 'Historical raw evidence differs from main: ' + name
        excluded.append(name)
        continue
    if name.endswith('.md'):
        for target in re.findall(r'\[[^\]]*\]\(([^)]+)\)', data.decode('utf-8-sig')):
            if re.match(r'[a-z]+://', target):
                continue
            linked = (path.parent / target.split('#', 1)[0].strip('<>')).resolve()
            if linked.is_relative_to(root):
                relative = linked.relative_to(root).as_posix()
                if relative in historical:
                    assert linked.is_file(), (name, target)
                    historical_links.append({'source': name, 'target': target})
    assert not pattern.search(name.encode()), name
    for span in quoted.get(name, []):
        encoded = span.encode()
        if pattern.search(encoded) and encoded in data:
            quoted_exclusions[name] = quoted_exclusions.get(name, 0) + 1
            data = data.replace(encoded, b'')
    # References to immutable evidence keep their historical paths, including
    # restoration manifests. This is not a runtime alias or an active identifier.
    for old in historical if name.startswith('docs/') else ():
        if not pattern.search(old.encode()):
            continue
        for ref in {old, old.removeprefix('docs/verification/'), Path(old).name}:
            data = data.replace(ref.encode(), b'')
    matches = list(pattern.finditer(data))
    if matches:
        assert name in boundaries, name
        remaining[name] = len(matches)
original = json.loads((root / 'docs/verification/ts-220-evidence/rename-audit.json').read_text())
renamed = [n for n in original['renamed_paths'] if (root / n).is_file()]
geometry = {}
for name in ('Shield.glb', 'ShieldRack.glb'):
    new = 'assets/items/shield/' + name
    old = 'assets/items/' + legacy + '/' + name.replace('Shield', legacy.title())
    old_data = subprocess.check_output(git + ['show', baseline + ':' + old])
    updated = (root / new).read_bytes()
    def binary(data):
        return data[20 + struct.unpack_from('<I', data, 12)[0]:]
    assert binary(old_data) == binary(updated), new
    geometry[new] = hashlib.sha256(binary(updated)).hexdigest()
manifest = json.loads((root / 'assets/items/shield/sources.json').read_text())
for name, expected in manifest['sha256'].items():
    path = root / 'assets/items/shield' / ('source/' + name if name.endswith('.blend') else name)
    assert hashlib.sha256(path.read_bytes()).hexdigest() == expected, name
result = {'baseline_commit': baseline, 'result': 'PASS',
          'scope': 'Canonical implementation/current documentation; exact historical raw bytes and original quoted commands/symbols are verified provenance exceptions',
          'maintained_files_scanned': sum(bool(n) and (root / n).is_file() for n in names),
          'immutable_historical_raw_files_verified_and_excluded': excluded,
          'historical_quoted_spans_verified_and_excluded': quoted_exclusions,
          'immutable_historical_evidence_links_checked': len(historical_links),
          'remaining_boundary_or_boundary_test_files': remaining,
          'renamed_paths': renamed, 'unchanged_glb_binary_chunks': geometry, 'manifest_hashes': 'PASS'}
(root / 'docs/verification/ts-220-evidence/rename-audit.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps({'result': 'PASS', 'baseline': baseline, 'immutable_raw_files': len(excluded),
                  'canonical_renamed_paths': len(renamed), 'boundary_files': remaining}))
