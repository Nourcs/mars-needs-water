# Top-down imitation of EarthGround.shader on one square, to check borders and repeats.
import sys, numpy as np
from PIL import Image
raw, n, size, u0, v0, span, out = sys.argv[1], int(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4]), float(sys.argv[5]), float(sys.argv[6]), sys.argv[7]
M = np.fromfile(raw, np.uint8).reshape(n, n, 4)
names = ["grass","drygrass","forest","farm","sand","rock","snow","paved","dirt","mud"]
rep = [1.6,1.8,2.4,2.4,2.6,4.5,5.0,3.0,2.2,2.4]; rot=[1]*10
means = {}
for l in open('out/means.txt'): p=l.split(); means[p[0]] = np.array([float(x) for x in p[2:5]])
A = [np.asarray(Image.open('out/%s_a.png'%k)).astype(np.float32)/255 for k in names]
NM = [np.asarray(Image.open('out/%s_n.png'%k)).astype(np.float32)/255 for k in names]
lin = lambda c: np.where(c<=0.04045, c/12.92, ((c+0.055)/1.055)**2.4)
srgb = lambda c: np.where(c<=0.0031308, c*12.92, 1.055*np.power(np.maximum(c,0),1/2.4)-0.055)
R = 1000; px = span/R
ys, xs = np.mgrid[0:R, 0:R].astype(np.float64)
wx = xs*px + u0*size; wz = -(ys*px + v0*size)          # world metres
U = u0 + xs*px/size; Vv = v0 + ys*px/size
def h(ix, iy, s):
    x = (ix.astype(np.int64)*0x8da6b343 ^ iy.astype(np.int64)*0xd8163841 ^ s*0xcb1ab31f) & 0xffffffff
    x ^= x>>15; x = (x*0x2c1b3c6d)&0xffffffff; x ^= x>>12; x=(x*0x297a2d39)&0xffffffff; x ^= x>>15
    return (x & 0xffff)/65535.0
def vn(p, s):
    i = np.floor(p).astype(np.int64); f = p-i; f=f*f*(3-2*f)
    a=h(i[0],i[1],s); b=h(i[0]+1,i[1],s); c=h(i[0],i[1]+1,s); d=h(i[0]+1,i[1]+1,s)
    return (a+(b-a)*f[0]) + ((c+(d-c)*f[0]) - (a+(b-a)*f[0]))*f[1]
W = np.stack([wx, wz])
def hexs(k):
    st = W/rep[k]; q = st*3.4641016
    sk = np.stack([q[0]-0.57735027*q[1], 1.15470054*q[1]]); base=np.floor(sk).astype(np.int64); t=sk-base
    tz = 1-t[0]-t[1]; s=(tz<0).astype(np.int64); s2=2*s-1
    w=[-tz*s2, s-t[1]*s2, s-t[0]*s2]
    vs=[(base[0]+s,base[1]+s),(base[0]+s,base[1]+1-s),(base[0]+1-s,base[1]+s)]
    acc=np.zeros((R,R,4)); g=[]
    samples=[]
    for i,(vx,vy) in enumerate(vs):
        ox=h(vx,vy,k*131+1); oy=h(vx,vy,k*977+2); ang=h(vx,vy,77)*6.2831853*rot[k]
        c,sn=np.cos(ang),np.sin(ang)
        uu=c*st[0]-sn*st[1]+ox; vv=sn*st[0]+c*st[1]+oy
        ix=(np.floor(uu*512)%512).astype(int); iy=(np.floor(vv*512)%512).astype(int)
        smp=A[k][iy,ix]; samples.append(smp)
        g.append(w[i]**3*(smp[...,3]+0.3))
    G=sum(g)+1e-4
    return sum(samples[i]*(g[i]/G)[...,None] for i in range(3))
wob = np.stack([vn(W*0.9,11)-0.5, vn(W*0.9,13)-0.5]) + 0.5*np.stack([vn(W*3.1,17)-0.5, vn(W*3.1,19)-0.5])
kc=M[np.clip((Vv*n).astype(int),0,n-1),np.clip((U*n).astype(int),0,n-1),3]
P = np.stack([U*n-0.5, Vv*n-0.5]) + wob*1.6*(kc!=7)
b = np.floor(P).astype(int); f = P-b
def K(dx,dy): return M[np.clip(b[1]+dy,0,n-1), np.clip(b[0]+dx,0,n-1), 3].astype(int)
def C(dx,dy): return M[np.clip(b[1]+dy,0,n-1), np.clip(b[0]+dx,0,n-1), :3].astype(np.float32)/255
ks=[K(0,0),K(1,0),K(0,1),K(1,1)]; ws=[(1-f[0])*(1-f[1]), f[0]*(1-f[1]), (1-f[0])*f[1], f[0]*f[1]]
mapc = lin(sum(C(*d)*w[...,None] for d,w in zip([(0,0),(1,0),(0,1),(1,1)],ws)))
cache={}
lw=[]; la=[]
for j in range(4):
    a=np.zeros((R,R,4))
    for k in np.unique(ks[j]):
        if k not in cache: cache[k]=hexs(k)
        m=ks[j]==k; a[m]=cache[k][m]
    la.append(a); lw.append(a[...,3]+ws[j]*1.6)
hm=np.max(lw,0)
sumA=0; sumW=0
for j in range(4):
    bw=np.maximum(lw[j]-hm+0.35,0)
    mean=np.stack([means[names[k]] for k in range(10)])[np.minimum(ks[j],9)]
    ratio=np.clip(mapc/np.maximum(mean,0.003),0.25,4)
    sumA=sumA+lin(la[j][...,:3])*ratio*bw[...,None]; sumW=sumW+bw
alb=sumA/np.maximum(sumW,1e-4)[...,None]
alb*= (0.9+0.2*(vn(W/23,3)*0.6+vn(W/7.3,5)*0.4))[...,None]
Image.fromarray((np.clip(srgb(alb*1.3),0,1)*255).astype(np.uint8)).save(out)
