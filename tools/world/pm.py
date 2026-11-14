import struct, gzip, json, math, sys, requests, io
B="https://overturemaps-extras-us-west-2.s3.amazonaws.com/tiles/2026-10-21.0/"
S=requests.Session()
def rng(name,off,ln):
    r=S.get(B+name+".pmtiles",headers={"Range":f"bytes={off}-{off+ln-1}"},timeout=60); r.raise_for_status(); return r.content
def header(name):
    h=rng(name,0,127); assert h[:7]==b"PMTiles" and h[7]==3
    f=struct.unpack("<QQQQQQQQQQQ",h[8:96])
    d=dict(zip("root_off root_len meta_off meta_len leaf_off leaf_len data_off data_len n_addr n_ent n_cont".split(),f))
    d["clustered"],d["icomp"],d["tcomp"],d["ttype"],d["minz"],d["maxz"]=h[96:102]
    return d
def varint(b,p):
    r=0;s=0
    while True:
        c=b[p];p+=1;r|=(c&127)<<s;s+=7
        if c<128:return r,p
def directory(b):
    b=gzip.decompress(b);p=0;n,p=varint(b,p);ids=[];last=0
    for i in range(n):
        v,p=varint(b,p);last+=v;ids.append(last)
    runs=[];lens=[];offs=[]
    for i in range(n): v,p=varint(b,p);runs.append(v)
    for i in range(n): v,p=varint(b,p);lens.append(v)
    for i in range(n):
        v,p=varint(b,p)
        offs.append(offs[i-1]+lens[i-1] if v==0 and i>0 else v-1)
    return ids,runs,lens,offs
def tileid(z,x,y):
    acc=sum(4**i for i in range(z)); n=1<<z; d=0; s=n>>1; rx=ry=0
    while s>0:
        rx=1 if x&s else 0; ry=1 if y&s else 0
        d+=s*s*((3*rx)^ry)
        if ry==0:
            if rx==1: x=s-1-x if False else (n-1-x); y=n-1-y
            x,y=y,x
        s>>=1
    return acc+d
def tileid2(z,x,y):
    acc=sum(4**i for i in range(z)); n=1<<z; d=0; s=n//2
    while s>0:
        rx=1 if (x&s)>0 else 0; ry=1 if (y&s)>0 else 0
        d+=s*s*((3*rx)^ry)
        if ry==0:
            if rx==1: x=s-1-x; y=s-1-y
            x,y=y,x
        s//=2
    return acc+d
import bisect
_dc={}
def gettile(name,h,z,x,y):
    tid=tileid2(z,x,y); off,ln=h["root_off"],h["root_len"]
    for depth in range(4):
        k=(name,off)
        if k not in _dc: _dc[k]=directory(rng(name,off,ln))
        ids,runs,lens,offs=_dc[k]
        i=bisect.bisect_right(ids,tid)-1
        if i<0: return None
        if runs[i]==0: off,ln=h["leaf_off"]+offs[i],lens[i]; continue
        if tid-ids[i]<runs[i]: return gzip.decompress(rng(name,h["data_off"]+offs[i],lens[i]))
        return None
def ll2t(lat,lon,z):
    n=1<<z; x=int((lon+180)/360*n); y=int((1-math.asinh(math.tan(math.radians(lat)))/math.pi)/2*n); return x,y
# MVT
def pb(b):
    p=0
    while p<len(b):
        k,p=varint(b,p); f,w=k>>3,k&7
        if w==0: v,p=varint(b,p)
        elif w==2: l,p=varint(b,p); v=b[p:p+l]; p+=l
        elif w==5: v=b[p:p+4]; p+=4
        elif w==1: v=b[p:p+8]; p+=8
        yield f,w,v
def packed(b):
    p=0;o=[]
    while p<len(b): v,p=varint(b,p); o.append(v)
    return o
def val(b):
    for f,w,v in pb(b):
        if f==1: return v.decode()
        if f==2: return struct.unpack("<f",v)[0]
        if f==3: return struct.unpack("<d",v)[0]
        if f in(4,5): return v
        if f==6: return (v>>1)^-(v&1)
        if f==7: return bool(v)
def mvt(b):
    L={}
    for f,w,v in pb(b):
        if f!=3: continue
        name=None;keys=[];vals=[];feats=[];ext=4096
        for f2,w2,v2 in pb(v):
            if f2==1: name=v2.decode()
            elif f2==2: feats.append(v2)
            elif f2==3: keys.append(v2.decode())
            elif f2==4: vals.append(val(v2))
            elif f2==5: ext=v2
        out=[]
        for fb in feats:
            tags=[];gt=0;geom=[]
            for f3,w3,v3 in pb(fb):
                if f3==2: tags=packed(v3)
                elif f3==3: gt=v3
                elif f3==4: geom=packed(v3)
            props={keys[tags[i]]:vals[tags[i+1]] for i in range(0,len(tags),2)}
            parts=[];x=y=0;i=0;cur=None
            while i<len(geom):
                c=geom[i]&7;n=geom[i]>>3;i+=1
                if c==1 or c==2:
                    for _ in range(n):
                        x+=(geom[i]>>1)^-(geom[i]&1); y+=(geom[i+1]>>1)^-(geom[i+1]&1); i+=2
                        if c==1: cur=[(x,y)]; parts.append(cur)
                        else: cur.append((x,y))
                elif c==7: pass
            out.append((gt,props,parts))
        L[name]=(ext,out)
    return L
if __name__=="__main__":
    for name in sys.argv[1:]:
        h=header(name); print(name,h)
        m=json.loads(gzip.decompress(rng(name,h["meta_off"],h["meta_len"])))
        for l in m.get("vector_layers",[]): print("  layer",l["id"],l.get("minzoom"),l.get("maxzoom"),list(l.get("fields",{}).keys())[:60])
