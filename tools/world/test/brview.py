import sys
from PIL import Image, ImageDraw
rows=[list(map(float,l.split())) for l in open(sys.argv[1])]
x0=min(r[0] for r in rows); x1=max(r[0] for r in rows); y0=min(r[1] for r in rows); y1=max(r[1] for r in rows)
W=1500; k=(W-40)/max(x1-x0,y1-y0,1e-6); H=int((y1-y0)*k)+40
im=Image.new("RGB",(W,H+330),(235,238,240)); d=ImageDraw.Draw(im)
prev=None
for r in rows:
    p=(20+(r[0]-x0)*k,20+(r[1]-y0)*k); c=r[2]-r[3]
    col=(int(min(255,c*20)),int(max(0,180-c*10)),60)
    if prev and not r[5]: d.line([prev,p],fill=col,width=3)
    prev=p
# side view: height against easting
d.line([(20,H+320),(W-20,H+320)],fill=(120,120,120))
for r in rows:
    px=20+(r[0]-x0)*k
    d.point((px,H+320-r[2]*12),fill=(200,40,40)); d.point((px,H+320-r[3]*12),fill=(60,120,60))
im.save(sys.argv[2])
