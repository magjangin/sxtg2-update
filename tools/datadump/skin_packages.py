"""GearPackage / NotePackage prefab names.
Usage: python -I skin_packages.py "{game}/Sixtar Gate STARTRAIL_Data/sharedassets0.assets"
Reads the game files with UnityPy; nothing from the game is stored in this repository.
"""
import struct, sys
import UnityPy

env = UnityPy.load(sys.argv[1])
objs = {o.path_id: o for o in env.objects}

def go_name(pid):
    o = objs.get(pid)
    if o is None: return None
    if o.type.name == 'GameObject':
        return o.read().m_Name
    if o.type.name == 'MonoBehaviour':
        raw = o.get_raw_data()
        fid, gpid = struct.unpack_from('<iq', raw, 0)  # m_GameObject PPtr
        return go_name(gpid)
    return None

for o in env.objects:
    if o.type.name != 'MonoBehaviour': continue
    raw = o.get_raw_data()
    # MonoBehaviour: m_GameObject(12) m_Enabled(1+3 pad) m_Script(12) m_Name(string)
    try:
        n = struct.unpack_from('<i', raw, 28)[0]
        name = raw[32:32 + n].decode('utf-8')
    except Exception:
        continue
    if name not in ('NotePackage', 'GearPackage'): continue
    pos = (32 + n + 3) & ~3
    count = struct.unpack_from('<i', raw, pos)[0]; pos += 4
    names = []
    for _ in range(count):
        fid, pid = struct.unpack_from('<iq', raw, pos); pos += 12
        names.append(go_name(pid) if fid == 0 else f'<ext {fid}:{pid}>')
    print(name, count, names)
