import json
from pathlib import Path

root = Path.cwd()
output = root / 'docs/verification/ts-275-evidence'
output.mkdir(exist_ok=True)
ablation = []
for label in ('inner-baseline', 'tight-bounds', 'geometric-slide'):
    trace = json.loads((root / '.godot/tunnel-scrape-checks' / label / 'fall-25-1.20--1.json').read_text())
    interval = trace[4:55]
    ablation.append(dict(label=label, frames=[4,54], meanObserveMs=sum(t['observeMs'] for t in interval)/len(interval),
                         meanSweeps=sum(t['sweeps'] for t in interval)/len(interval), endPosition=trace[-1]['position']))
(output/'investigation-ablation.json').write_text(json.dumps(ablation, indent=2)+'\n')
folder = root/'.godot/tunnel-scrape-checks/final-idle'
summary = json.loads((folder/'summary.json').read_text())
network = [t for t in summary if t['name'].startswith('network')]
contacts = []
for case in network:
    contacts.extend(t for t in json.loads((folder/(case['name']+'.json')).read_text()) if t['contacts'])
probes = json.loads((folder/'query-comparison.json').read_text())
original = sum(p['originalMs'] for p in probes)
bounded = sum(p['boundedMs'] for p in probes)
metrics = dict(cases=len(summary), hostedCases=len(network), ticksPerCase=420,
               maxCaseMeanMs=max(c['mean'] for c in network), maxCaseP95Ms=max(c['p95'] for c in network),
               maxCaseP99Ms=max(c['p99'] for c in network), maxContactMs=max(c['maximum'] for c in network),
               maxStepM=max(c['peakStep'] for c in summary), contactFrames=len(contacts),
               contactFramesOver16ms=sum(c['observeMs']>16.667 for c in contacts),
               contactFramesOver16msAfterSubtractingGc=sum(c['observeMs']-c['gcPauseMs']>16.667 for c in contacts),
               maxContactExcludingRecordedGcMs=max(c['observeMs']-c['gcPauseMs'] for c in contacts),
               queryOriginalMs=original, queryBoundedMs=bounded, queryRatio=bounded/original)
(output/'final-metrics.json').write_text(json.dumps(metrics, indent=2)+'\n')
print(json.dumps(ablation, indent=2))
print(json.dumps(metrics, indent=2))
