"""Which data files contain each DLC Steam AppID (int32 little-endian). Edit `ids` for new DLCs.
Usage: python -I dlc_ids.py "{game}/Sixtar Gate STARTRAIL_Data"
Reads the game files with UnityPy; nothing from the game is stored in this repository.
"""
import os, struct, sys
root = sys.argv[1]
ids = [1986060, 2148660, 2197460, 2447170, 2648850, 2648860, 2712520, 2766750, 2909210, 3913740, 4785690]
pats = {i: struct.pack('<i', i) for i in ids}
found = {i: [] for i in ids}
for dirpath, _, files in os.walk(root):
    if 'StreamingAssets' in dirpath:
        continue
    for f in files:
        p = os.path.join(dirpath, f)
        if os.path.getsize(p) > 400_000_000:
            continue
        data = open(p, 'rb').read()
        for i, pat in pats.items():
            if pat in data:
                found[i].append(os.path.relpath(p, root))
for i in ids:
    print(i, found[i][:4])
