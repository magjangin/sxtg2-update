# 사용: python -I dis.py <GameAssembly.dll> <script.json> <이름 부분문자열 또는 0xRVA> [...]
# 함수 하나를 ret/다음 함수 시작까지 디스어셈블하고 call 대상 이름을 붙인다.
import sys, json, bisect, re, pickle, os
import pefile, capstone

dll, sj = sys.argv[1], sys.argv[2]
cache = sj + ".addr.pickle"
if os.path.exists(cache):
    with open(cache, "rb") as f:
        names, meta = pickle.load(f)
else:
    with open(sj, encoding="utf-8") as f:
        d = json.load(f)
    names = {}
    for m in d["ScriptMethod"]:
        names.setdefault(m["Address"], []).append(m["Name"])
    meta = {}
    for m in d.get("ScriptMetadataMethod", []):
        meta[m["Address"]] = m["Name"]
    for m in d.get("ScriptMetadata", []):
        meta[m["Address"]] = m["Name"]
    with open(cache, "wb") as f:
        pickle.dump((names, meta), f)
starts = sorted(names)

pe = pefile.PE(dll, fast_load=True)
img = pe.get_memory_mapped_image()
base = pe.OPTIONAL_HEADER.ImageBase
pe.close()
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)

def nm(rva):
    v = names.get(rva)
    if v: return v[0] + (f" (+{len(v)-1} ICF)" if len(v) > 1 else "")
    return meta.get(rva, None)

for q in sys.argv[3:]:
    if q.startswith("0x"):
        targets = [int(q, 16)]
    else:
        targets = [a for a, n in names.items() if any(q in x for x in n)]
    for rva in targets:
        i = bisect.bisect_right(starts, rva)
        end = starts[i] if i < len(starts) else rva + 0x400
        end = min(end, rva + 0x1200)
        print(f"===== 0x{rva:X} {nm(rva)}  (size 0x{end-rva:X})")
        for ins in md.disasm(img[rva:end], base + rva):
            s = f"  {ins.address-base:08X}: {ins.mnemonic} {ins.op_str}"
            m = re.search(r"0x([0-9a-f]+)", ins.op_str)
            if "rip" not in ins.op_str and m and ins.mnemonic.startswith(("call", "j")):
                n = nm(int(m.group(1), 16) - base)
                if n: s += f"    ; {n}"
            elif "rip" in ins.op_str:
                mm = re.search(r"rip ([+-]) (0x[0-9a-f]+)", ins.op_str)
                if mm:
                    off = int(mm.group(2), 16) * (1 if mm.group(1) == "+" else -1)
                    t2 = ins.address + ins.size - base + off
                    if meta.get(t2): s += f"    ; {meta[t2]}"
            print(s)
