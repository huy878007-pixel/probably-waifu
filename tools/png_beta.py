
raise SystemExit("Disabled: V3 content must preserve original HJP; PNG beta conversion is not a release path.")
import json,sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0,r'D:\Document\bim\portrait-pack')
from hjp import Hjp,L_COLOR
root=Path(r'D:\AI\PortraitRuntime\beauty\artifacts\v3-content')
layout=json.loads((root/'layout_profiles.json').read_text(encoding='utf-8'))
for f in (root/'packs').glob('*/pack.json'):
 d=json.loads(f.read_text(encoding='utf-8')); replace={};d['assetDimensions']={}
 for container in f.parent.glob('hjp/*.hjp'):
  h=Hjp(container)
  for e in h.meta['expressions']:
   eid=e['id'];rgb,_=h.image(eid,L_COLOR);alpha=h.silhouette(eid)
   rgba=np.dstack([rgb,alpha])
   path='v3/'+container.stem+'/'+str(eid)+'.png';out=f.parent/path;out.parent.mkdir(parents=True,exist_ok=True)
   Image.fromarray(rgba).save(out,compress_level=6)
   old='hjp/'+container.name+'#'+str(eid);replace[old]=path
   d['assetDimensions'][path]=[rgba.shape[1],rgba.shape[0]]
   entry=layout['Assets'].get(d['id']+'|'+old)
   if entry:layout['Assets'][d['id']+'|'+path]=entry
 def remap(x):
  if isinstance(x,dict):return {k:remap(v) for k,v in x.items()}
  if isinstance(x,list):return [remap(v) for v in x]
  return replace.get(x,x) if isinstance(x,str) else x
 d=remap(d)
 f.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
 # Containers remain in the source delivery directory. Only standard PNG resources ship in the native Mod.
 for container in f.parent.glob('hjp/*.hjp'):container.unlink()
 print(d['id'],len(replace),flush=True)
(root/'layout_profiles.json').write_text(json.dumps(layout,ensure_ascii=False,indent=2),encoding='utf-8')
