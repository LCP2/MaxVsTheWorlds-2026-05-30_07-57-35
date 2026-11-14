"""MV-1376: % of pixels whose every channel differs by <=2 levels between two PNGs, on an optional crop.
Usage: diff.py a.png b.png <crop 'x,y,w,h' | full>"""
import sys
import numpy as np
from PIL import Image

a, b, crop = sys.argv[1], sys.argv[2], sys.argv[3]
A = np.asarray(Image.open(a).convert("RGB"), dtype=np.int16)
B = np.asarray(Image.open(b).convert("RGB"), dtype=np.int16)
if A.shape != B.shape:
    print(f"FAIL: size mismatch {A.shape} vs {B.shape}")
    sys.exit(5)
if crop != "full":
    x, y, w, h = (int(v) for v in crop.split(","))
    A, B = A[y:y + h, x:x + w], B[y:y + h, x:x + w]
    if A.size == 0:
        print("FAIL: empty crop")
        sys.exit(5)
d = np.abs(A - B).max(axis=2)
pct = 100.0 * float((d <= 2).sum()) / d.size
print(f"RESULT: within2={pct:.3f}% maxdiff={int(d.max())} pixels={d.size} crop={crop}")
