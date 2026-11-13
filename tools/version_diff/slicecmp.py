# 업데이트 전 GameAssembly에서 잘라 둔 slices/*.dll의 함수 바이트를
# 현재(업데이트 후) GameAssembly.dll 같은 RVA의 바이트와 비교한다.
import json, sys, os, glob
import pefile

slice_dir, game_dll = sys.argv[1], sys.argv[2]
g = pefile.PE(game_dll, fast_load=True)
gdata = g.get_memory_mapped_image()
g.close()

total = same = diff = missing = 0
diffs = []
for path in sorted(glob.glob(os.path.join(slice_dir, "*.dll"))):
    names = path + ".names.json"
    if not os.path.exists(names):
        continue
    with open(names, encoding="utf-8") as nf:
        meta = json.load(nf)
    s = pefile.PE(path, fast_load=True)
    sbase = s.OPTIONAL_HEADER.ImageBase
    sdata = s.get_memory_mapped_image()
    s.close()
    for f in meta["functions"]:
        va = int(f["va"], 16); rva = int(f["rva"], 16); size = int(f["size"], 16)
        off = va - sbase  # 슬라이스 안의 RVA
        old = sdata[off:off + size]
        new = gdata[rva:rva + size]
        total += 1
        if len(old) != size or not any(old):
            missing += 1
            continue
        if old == new:
            same += 1
        else:
            diff += 1
            ndiff = sum(1 for a, b in zip(old, new) if a != b)
            diffs.append((os.path.basename(path), f["rva"], f["size"], ndiff, f["name"]))

print(f"functions={total} same={same} diff={diff} unreadable={missing}")
seen = set()
for d in diffs:
    key = (d[1], d[4])
    if key in seen:
        continue
    seen.add(key)
    print(f"{d[1]:>10} size={d[2]:>7} bytesDiff={d[3]:>5}  {d[4]}  [{d[0]}]")
