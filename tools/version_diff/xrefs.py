# 사용: python -I xrefs.py <GameAssembly.dll> <script.json> <클래스 전체 이름>...
#   예: python -I xrefs.py "<게임>\GameAssembly.dll" il2cpp_dump/script.json ManagerWebRequest
# 누가 이 클래스를 부르는가: 클래스 메서드로의 직접 call/jmp(E8/E9 rel32)와, 그 클래스의 TypeInfo/MethodInfo
# 메타데이터를 rip 상대로 읽는 함수(= 그 클래스를 쓰는 코드)를 실행 섹션(.text + il2cpp) 전체에서 찾는다.
# 같은 클래스 안 호출은 뺀다. 한계: 가상 호출·인터페이스 호출·리플렉션·씬에 붙은 MonoBehaviour 메시지는 안 보임
# (= "0건"이 곧 "안 불림"은 아님). ICF로 접힌 주소는 공유 메서드 전부의 호출로 잡힘(예: 빈 .ctor).
# 2026-11-15: 레거시 ManagerWebRequest 호출자가 전부 구버전 RhythmGame.* 매니저임을 확인할 때 씀.
import sys, json, bisect
import numpy as np, pefile

dll, sj = sys.argv[1], sys.argv[2]
classes = sys.argv[3:]
with open(sj, encoding="utf-8") as f:
    d = json.load(f)
names = {}
for m in d["ScriptMethod"]:
    names.setdefault(m["Address"], []).append(m["Name"])
starts = np.array(sorted(names), dtype=np.int64)

def owner(rva):
    i = bisect.bisect_right(starts, rva) - 1
    if i < 0: return "?"
    v = names[int(starts[i])]
    return v[0]

def cls_match(n):
    return any(n.startswith(c + "$$") for c in classes)

targets = {a: n for a, ns in names.items() for n in ns if cls_match(n)}
meta = {}
for key in ("ScriptMetadataMethod", "ScriptMetadata"):
    for m in d.get(key, []):
        n = m["Name"]
        if any(c in n for c in classes):
            meta[m["Address"]] = n

pe = pefile.PE(dll, fast_load=True)
img = np.frombuffer(pe.get_memory_mapped_image(), dtype=np.uint8)
ex = [s for s in pe.sections if s.Characteristics & 0x20000000]  # .text + il2cpp
t0 = min(s.VirtualAddress for s in ex); t1 = max(s.VirtualAddress + s.Misc_VirtualSize for s in ex)
pe.close()
seg = img[t0:t1]

def rel32_at(idx):  # idx: positions in seg of the 4-byte field
    b = seg
    return (b[idx].astype(np.int64) | (b[idx+1].astype(np.int64) << 8) | (b[idx+2].astype(np.int64) << 16) | (b[idx+3].astype(np.int64) << 24)).astype(np.int32).astype(np.int64)

# 1) E8/E9 rel32
pos = np.nonzero((seg[:-5] == 0xE8) | (seg[:-5] == 0xE9))[0]
dest = pos + 5 + rel32_at(pos + 1) + t0
tset = np.array(sorted(targets), dtype=np.int64)
hit = np.isin(dest, tset)
callers = {}
for p, dst in zip(pos[hit], dest[hit]):
    callers.setdefault(targets[int(dst)], set()).add(owner(int(p) + t0))

# 2) rip 상대 [rip+disp32] -> meta (명령 끝 = disp 위치+4, 즉값 없는 경우만 근사)
# 후보 줄이기: ModRM rip 형식(mod=00, rm=101) 바이트 바로 뒤가 disp
modrm = seg[:-5]
cand = np.nonzero((modrm & 0xC7) == 0x05)[0] + 1
dst2 = cand + 4 + rel32_at(cand) + t0
mset = np.array(sorted(meta), dtype=np.int64)
hit2 = np.isin(dst2, mset)
users = {}
for p, dst in zip(cand[hit2], dst2[hit2]):
    users.setdefault(meta[int(dst)], set()).add(owner(int(p) + t0))

own = lambda n: cls_match(n) or any(c in n for c in classes)
print("== 직접 호출 (대상 메서드 <- 호출자, 같은 클래스 안 호출 제외)")
for t in sorted(callers):
    ext = sorted(x for x in callers[t] if not own(x))
    print(f"{t}: 외부 {len(ext)}  {ext[:12]}")
print("== 메타데이터 참조 (TypeInfo/MethodInfo <- 쓰는 함수, 같은 클래스 제외)")
for t in sorted(users):
    ext = sorted(x for x in users[t] if not own(x))
    print(f"{t}: 외부 {len(ext)}  {ext[:15]}")
