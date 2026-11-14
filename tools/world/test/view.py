import sys, struct
from PIL import Image, ImageDraw
p=sys.argv[1]; n=int(sys.argv[2]); size=float(sys.argv[3])
im=Image.frombytes("RGBA",(n,n),open(p,"rb").read()).convert("RGB")
im.save(p+".tex.png")
try:
    d=open(p+".mesh","rb").read()
except: sys.exit()
K=2; big=im.resize((n*K,n*K)); dr=ImageDraw.Draw(big); o=0
cols=[(200,120,90),(40,40,45),(30,110,40)]
for mi in range(3):
    nv=struct.unpack_from("<i",d,o)[0]; o+=4; P=struct.unpack_from("<%df"%(nv*3),d,o); o+=nv*12
    ni=struct.unpack_from("<i",d,o)[0]; o+=4; I=struct.unpack_from("<%di"%ni,d,o); o+=ni*4
    pass
    for t in range(0,ni,3):
        pts=[((P[I[t+k]*3]/size+0.5)*n*K,(0.5-P[I[t+k]*3+2]/size)*n*K) for k in range(3)]
        dr.polygon(pts,fill=cols[mi])
big.save(p+".top.png")
