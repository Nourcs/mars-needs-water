import pyarrow.parquet as pq, struct, sys
f=pq.ParquetFile("div/division.parquet")
print(f.schema_arrow.names)
out=[]
for rg in range(f.num_row_groups):
    t=f.read_row_group(rg,columns=["names","subtype","class","population","country","region","geometry"]).to_pylist()
    for r in t:
        if r["subtype"]!="locality": continue
        n=(r["names"] or {}).get("primary")
        if not n or "|" in n or "\n" in n: continue
        g=r["geometry"]
        if not g or len(g)<21: continue
        bo="<" if g[0]==1 else ">"
        typ=struct.unpack_from(bo+"I",g,1)[0]
        if typ&0xff!=1: continue
        lon,lat=struct.unpack_from(bo+"dd",g,5)
        pop=r["population"] or 0
        cls=r["class"] or ""
        c=(r["names"] or {}).get("common") or []
        if isinstance(c,dict): c=list(c.items())
        en=""
        for k,v in c:
            if k=="en": en=v or ""
        if en==n or "|" in en or "\n" in en: en=""
        out.append((pop,cls,n,(r["region"] or r["country"] or ""),lat,lon,en))
print(len(out))
import collections; print(collections.Counter(o[1] for o in out))
keep=[o for o in out if o[0]>=2000 or o[1] in("city","town")]
keep.sort(key=lambda o:-o[0]); print(len(keep))
open("places.txt","w",encoding="utf8").write("\n".join("%s|%s|%.4f|%.4f|%d|%s"%(o[2],o[3],o[4],o[5],o[0],o[6]) for o in keep)+"\n")
print(keep[:5],keep[-3:])
