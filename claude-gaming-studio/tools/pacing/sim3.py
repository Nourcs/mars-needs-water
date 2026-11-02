"""Mars Needs Water pacing simulator (reference player), Economy GDD.
10 Hz, discrete player actions. Modes: eager = reference player (launch when full
or when the launch affords the next item); full = launch only when the pool is full
from the hand pump on; lazy = launch 20 s after the pool fills.
Usage: python3 sim3.py [mode=eager|full|lazy] [item=price ...]"""
import sys
DT=0.1
LADDER=[["bucket",8],["barrel_100l",220],["drum_200l",260],["hand_pump",380],
        ["electric_motor",1100],["electric_pump_2",15000],["ibc_tank",40000]]
INSTALL={"bucket":0,"barrel_100l":8,"drum_200l":10,"hand_pump":25,"electric_motor":15,"electric_pump_2":25,"ibc_tank":25}
CAP={"barrel_100l":100,"drum_200l":200,"ibc_tank":1200}
mode="eager"
for a in sys.argv[1:]:
    k,v=a.split("=")
    if k=="mode": mode=v
    else:
        for r in LADDER:
            if r[0]==k: r[1]=float(v)
CPL=10; LEVER=3; CYCLE=15; COUNT=3; TERMINAL=2; DELIV=4; UNBOX=6
SPOON=(0.015,1.25); BUCKET=10; TRIP_PUDDLE=5; TRIP_POND=20
HAND=1.25*0.7; ELEC=8
puddle=40.0; pond=20000.0; POND0=pond
t=0.0; cr=0.0; pool=0.0; cap=10.0; owned={"spoon","jerrycan_10l"}; nxt=0
pad_free_at=0.0; pending=[]  # (liftoff_time, litres)
busy=0.0; act=None; log=[]; marks=[1,2.5,5,10,20,30]; mi=0
last_launch=-1e9; full_since=None; LAZY=20; times=[]; buy_t=0.0
def has(x): return x in owned
def nextitem(): return LADDER[nxt] if nxt<len(LADDER) else None
def tool():
    return "bucket" if has("bucket") else "spoon"
def stage():
    if has("electric_motor"): return "electric"
    if has("hand_pump"): return "hand"
    return "tool"
def pumps_rate():
    if has("electric_pump_2"): return 2*ELEC
    if has("electric_motor"): return ELEC
    return 0
def want_launch():
    if pool<=1e-9 or t<pad_free_at: return False
    it=nextitem()
    global full_since
    if pool>=cap-1e-9:
        if full_since is None: full_since=t
    else: full_since=None
    if mode=="lazy" and stage()!="tool": return full_since is not None and t-full_since>=LAZY
    if mode=="full" and stage()!="tool": return pool>=cap-1e-9
    if pool>=cap-1e-9: return True
    return it is not None and cr+pool*CPL>=it[1]
while t<2400:
    t+=DT
    # liftoffs
    for p in list(pending):
        if t>=p[0]: cr+=p[1]*CPL; pending.remove(p)
    # electric pumps run regardless of player
    r=pumps_rate()
    if r:
        a=min(r*DT,cap-pool,pond); pool+=a; pond-=a
    d=100*(POND0-pond)/POND0
    while mi<len(marks) and d>=marks[mi]: log.append((t,f"pond {marks[mi]}%","")); mi+=1
    if busy>0:
        busy-=DT
        if act=="crank":
            a=min(HAND*DT,cap-pool,pond); pool+=a; pond-=a
        if busy>0: continue
        # finish action
        if act and act[0]=="trip":
            _,src,vol=act
            pool+=vol
        act=None
    it=nextitem()
    if it and cr>=it[1] and (it[0]!="hand_pump" or has("drum_200l")):
        cr-=it[1]; owned.add(it[0]); log.append((t,"buy "+it[0],f"{d:.2f}%"))
        times.append(t-buy_t); buy_t=t
        if it[0] in CAP: cap=CAP[it[0]]
        busy=TERMINAL+DELIV+UNBOX+INSTALL[it[0]]; nxt+=1; act=("buying",); continue
    if want_launch():
        pending.append((t+COUNT,pool)); pool=0; pad_free_at=t+CYCLE; last_launch=t; full_since=None
        busy=LEVER; act=("lever",); continue
    if stage()=="tool":
        vol,tt=SPOON if tool()=="spoon" else (BUCKET,None)
        if tool()=="bucket": tt=TRIP_PUDDLE if puddle>0 else TRIP_POND
        space=cap-pool
        if space<=1e-9: continue  # wait for pad
        take=min(vol,space)
        if puddle>0: g=min(take,puddle); puddle-=g
        else: g=min(take,pond); pond-=g
        if puddle<=0 and not any(l[1]=="puddle dry" for l in log): log.append((t,"puddle dry",""))
        busy=tt; act=("trip","",g)
    elif stage()=="hand":
        if cap-pool>1e-9: busy=1.0; act="crank"
    # electric: player idle near pad
if "--gaps" not in sys.argv:
    for tt,e,x in log: print(f"{int(tt)//60}:{int(tt)%60:02d}  {e} {x}")
    print("step times:",[round(x) for x in times])
