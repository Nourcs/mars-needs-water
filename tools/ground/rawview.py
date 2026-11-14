import sys, numpy as np
from PIL import Image
n=int(sys.argv[2]); a=np.fromfile(sys.argv[1],np.uint8).reshape(n,n,4)
pal=np.array([[95,125,51],[139,123,81],[40,70,30],[200,190,60],[230,210,150],[120,110,100],[255,255,255],[60,60,60],[150,110,70],[40,40,90]],np.uint8)
k=pal[np.minimum(a[...,3],9)]
Image.fromarray(np.concatenate([a[...,:3],k],1)).save(sys.argv[3])
