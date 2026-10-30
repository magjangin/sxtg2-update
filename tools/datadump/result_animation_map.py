"""ResultAnimationMap entries (track ID -> result viewer) from the contents bundle.
Usage: python -I result_animation_map.py "{game}/.../StreamingAssets/aa/StandaloneWindows64/contents_assets_all_*.bundle"
Reads the game files with UnityPy; nothing from the game is stored in this repository.
"""
import struct, sys
import UnityPy

env = UnityPy.load(sys.argv[1])
objs = {}
for f in env.files.values():
    pass
for o in env.objects:
    objs[(o.assets_file.name, o.path_id)] = o

def find(pid, af):
    return objs.get((af, pid))

def go_name_of_component(o):
    raw = o.get_raw_data()
    fid, gpid = struct.unpack_from('<iq', raw, 0)
    g = find(gpid, o.assets_file.name)
    return g.read().m_Name if g is not None else f'?{fid}:{gpid}'

for o in env.objects:
    if o.type.name != 'MonoBehaviour':
        continue
    raw = o.get_raw_data()
    try:
        n = struct.unpack_from('<i', raw, 28)[0]
        name = raw[32:32 + n].decode('utf-8')
    except Exception:
        continue
    if not name.startswith('ResultAnimation') and 'Result' not in name:
        continue
    pos = (32 + n + 3) & ~3
    try:
        count = struct.unpack_from('<i', raw, pos)[0]
    except Exception:
        continue
    if not (0 < count < 500):
        continue
    pos += 4
    entries = []
    ok = True
    for _ in range(count):
        ln = struct.unpack_from('<i', raw, pos)[0]
        if ln < 0 or ln > 200: ok = False; break
        sid = raw[pos + 4:pos + 4 + ln].decode('utf-8', 'replace')
        pos = (pos + 4 + ln + 3) & ~3
        fid, pid = struct.unpack_from('<iq', raw, pos); pos += 12
        v = find(pid, o.assets_file.name) if fid == 0 else None
        vname = go_name_of_component(v) if v is not None else f'ext{fid}:{pid}'
        entries.append((sid, vname))
    if ok:
        print('==', name, count)
        for e in entries:
            print('   ', e)
