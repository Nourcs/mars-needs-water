
import sys, struct, glob, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont
d=sys.argv[1]; out=sys.argv[2]; az=float(sys.argv[3]) if len(sys.argv)>3 else 28; only=sys.argv[4:] 
files=sorted(glob.glob(d+"/*.lm"))
if only: files=[f for f in files if os.path.basename(f)[:2] in only]
W=H=420; cols=6 if len(files)>12 else max(1,min(4,len(files)))
if only: W=H=900; cols=min(2,len(files))
rows=(len(files)+cols-1)//cols
sheet=Image.new("RGB",(cols*W,rows*H),(196,214,232))
L=np.array([0.45,0.8,-0.4]); L/=np.linalg.norm(L)
for n,f in enumerate(files):
    b=open(f,"rb").read(); yaw,nt=struct.unpack_from("<fi",b,0)
    a=np.frombuffer(b,dtype=np.dtype([("p","<f4",(9,)),("c","<u4")]),count=nt,offset=8)
    P=a["p"].reshape(-1,3,3).astype(float).copy(); C=a["c"]; P[:,:,1]=np.maximum(P[:,:,1],0)
    # camera: looks at the front of the model (which faces bearing yaw), a bit to the side, 22 degrees down
    th=math.radians(yaw+az); el=math.radians(18)
    fwd=np.array([-math.sin(th)*math.cos(el),-math.sin(el),-math.cos(th)*math.cos(el)])
    right=np.cross([0,1,0],fwd); right/=np.linalg.norm(right); up=np.cross(fwd,right)
    X=P@right; Y=P@up; Z=P@fwd
    nrm=np.cross(P[:,1]-P[:,0],P[:,2]-P[:,0]); ln=np.linalg.norm(nrm,axis=1); ln[ln==0]=1; nrm/=ln[:,None]
    flip=(nrm@fwd)>0; nrm[flip]*=-1
    sh=0.55+0.5*np.clip(nrm@L,0,1)
    x0,x1,y0,y1=X.min(),X.max(),Y.min(),Y.max()
    s=min((W-30)/max(x1-x0,1e-3),(H-50)/max(y1-y0,1e-3))
    SS=2; im=Image.new("RGB",(W*SS,H*SS),(196,214,232)); dr=ImageDraw.Draw(im)
    order=np.argsort(-Z.mean(axis=1))
    cx=(x0+x1)/2
    for t in order:
        c=int(C[t]); r,g,bl=(c>>16)&255,(c>>8)&255,c&255; k=sh[t]
        pts=[((W/2+(X[t,j]-cx)*s)*SS,(H-28-(Y[t,j]-y0)*s)*SS) for j in range(3)]
        dr.polygon(pts,fill=(min(255,int(r*k)),min(255,int(g*k)),min(255,int(bl*k))))
    im=im.resize((W,H),Image.LANCZOS); dr=ImageDraw.Draw(im)
    dr.text((8,H-20),open(f[:-3]+".txt").read(),fill=(20,30,40))
    sheet.paste(im,((n%cols)*W,(n//cols)*H))
sheet.save(out)
