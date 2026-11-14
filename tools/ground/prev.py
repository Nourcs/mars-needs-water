import sys, numpy as np
from PIL import Image
names=["grass","drygrass","forest","farm","sand","rock","snow","paved","dirt","mud"]
d=sys.argv[1]; S=int(sys.argv[2]); crop=int(sys.argv[3])
cells=[]
L=np.array([-0.5,0.5,0.7]); L/=np.linalg.norm(L)
for k in names:
    a=np.asarray(Image.open(f'{d}/{k}_a.png')).astype(float)/255; n=np.asarray(Image.open(f'{d}/{k}_n.png')).astype(float)/255
    nx=n[...,0]*2-1; ny=n[...,1]*2-1; nz=np.sqrt(np.clip(1-nx*nx-ny*ny,0,1))
    sh=np.clip(nx*L[0]+ny*L[1]+nz*L[2],0,1)*0.8+0.3
    img=np.clip(a[...,:3]*n[...,2:3]*sh[...,None],0,1)
    img=np.tile(img,(2,2,1))[:crop,:crop]
    cells.append(np.asarray(Image.fromarray((img*255).astype(np.uint8)).resize((S,S),Image.LANCZOS)))
rows=[np.concatenate(cells[i:i+5],1) for i in (0,5)]
Image.fromarray(np.concatenate(rows,0)).save(sys.argv[4])
