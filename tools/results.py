"""Collect completed v0.22.1 roster observations without copying raw saves or machine paths."""
import argparse,csv,json,hashlib
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('trial',type=Path);p.add_argument('--out',type=Path,default=Path('evidence/v0221'));a=p.parse_args()
groups=['packs-and-goats','dogs-raptors-and-crabs','spiders-and-gorillos','large-animals','cleanser','leviathan','trader']
rows=[]
for group in groups+['small']:
 folder=a.trial/group;log=(folder/'client.log').read_bytes().decode('utf-8',errors='replace')
 assert 'ROSTER PASS all ' in log and 'ROSTER FAIL ' not in log, group
 for line in (folder/'rides.jsonl').read_text(encoding='utf-8').splitlines():
  r=json.loads(line);r.pop('slot',None);r['case']=group
  assert r['maxPelvis']<.08 and (r['maxSaddle'] is None or r['maxSaddle']<.08)
  assert r['frames']>0 and r['turnFrames']>=7 and r['turnDegrees']>=72
  rows.append(r)
log=(a.trial/'wild/client.log').read_bytes().decode('utf-8',errors='replace')
assert 'ROSTER PASS' in log and 'ROSTER FAIL' not in log
primary=[r for r in rows if r['case']!='small']
root=Path(__file__).resolve().parents[1]
fit=json.loads((root/'mods/Rideable Vanilla Animals/transport.json').read_bytes())
ids={i for m in fit['mounts'] for i in m['animalRecordIds']}
assert len(primary)==len({r['id'] for r in primary})==43 and {r['id'] for r in primary}==ids
installed=a.trial/'install/mods/Rideable Vanilla Animals'
for file in (root/'mods/Rideable Vanilla Animals').rglob('*'):
 if file.is_file():assert file.read_bytes()==(installed/file.relative_to(root/'mods/Rideable Vanilla Animals')).read_bytes()
summary=dict(game='0.22.1+9ee4074bbc6fd7fdeb26108a04afc9f90cb1dc01',transport='0.3.0-playtest.6',workers=4,primaryRides=43,youngerRides=3,distinctFits=len({r['fit'] for r in primary}),posedFrames=sum(r['frames'] for r in rows),turnFrames=sum(r['turnFrames'] for r in rows),maxPelvis=max(r['maxPelvis'] for r in rows),maxSaddle=max(r['maxSaddle'] or 0 for r in rows),unownedRefused=True,saveReloadRemount=sum(r['remounted'] for r in rows),minimumHeading=min(r['turnDegrees'] for r in primary),maximumHeading=max(r['turnDegrees'] for r in primary))
a.out.mkdir(parents=True,exist_ok=True)
(a.out/'rides.json').write_text(json.dumps(rows,indent=2)+'\n',encoding='utf-8')
(a.out/'summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
keys=['id','name','fit','case','scale','turnDegrees','frames','turnFrames','maxPelvis','maxSaddle','bag','remounted','footDistance']
with (a.out/'roster.csv').open('w',encoding='utf-8',newline='') as f:
 w=csv.DictWriter(f,fieldnames=keys);w.writeheader();w.writerows({k:r[k] for k in keys} for r in primary)
print(json.dumps(summary,indent=2))
