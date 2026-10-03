import json
from pathlib import Path

root = Path.cwd()
folder = root/'.godot/ts-275/idle-playtest'
summary = []
def distribution(values):
    values = sorted(values)
    return dict(samples=len(values), mean=sum(values)/len(values), p95=values[int((len(values)-1)*.95)],
                p99=values[int((len(values)-1)*.99)], maximum=values[-1]) if values else None
for path in sorted(folder.glob('approach-*.json')):
    trace = json.loads(path.read_text())
    contacts = [t for t in trace if t['contacts']]
    falling = sum(t['contacts']>0 and trace[i-1]['velocity'][1] < -.5 for i,t in enumerate(trace) if i)
    summary.append(dict(name=path.stem, frames=len(trace), contacts=len(contacts), fallingContacts=falling,
                        finalPosition=trace[-1]['position'], finalHp=trace[-1]['hp'],
                        observationMs=distribution([t['observationMs'] for t in trace]),
                        contactObservationMs=distribution([t['observationMs'] for t in contacts]),
                        sampledRenderIntervalMs=distribution([t['renderFrameMs'] for t in trace]),
                        sampledRenderIntervalAfterFirst5TicksMs=distribution([t['renderFrameMs'] for t in trace[5:]])))
output=root/'docs/verification/ts-275-evidence/playtest-metrics.json'
output.write_text(json.dumps(summary, indent=2)+'\n')
for t in summary:
    if t['name'].endswith(('-fall','-press')):
        print(t['name'], 'contact/fall',t['contacts'],t['fallingContacts'],
              'observe',t['observationMs'], 'render',t['sampledRenderIntervalMs'])
