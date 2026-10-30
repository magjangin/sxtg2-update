"""Which GameObjects (full hierarchy path) reference sprites whose name starts with a prefix.
Usage: python -I sprite_users.py "{game}/.../sharedassets0.assets" [prefix=button01]
Reads the game files with UnityPy; nothing from the game is stored in this repository.
"""
import struct, sys
import UnityPy

env = UnityPy.load(sys.argv[1])
prefix = sys.argv[2] if len(sys.argv) > 2 else 'button01'
objs = {o.path_id: o for o in env.objects}
sprites = {}
for o in env.objects:
    if o.type.name == 'Sprite':
        n = o.read().m_Name
        if n.startswith(prefix):
            sprites[o.path_id] = n

# Transform parent chain for a GameObject -> full hierarchy path.
go_transform = {}
for o in env.objects:
    if o.type.name in ('Transform', 'RectTransform'):
        t = o.read()
        go_transform[t.m_GameObject.path_id] = t

def path_of(go_pid, depth=0):
    o = objs.get(go_pid)
    if o is None or depth > 30: return '?'
    name = o.read().m_Name
    t = go_transform.get(go_pid)
    if t is None or t.m_Father.path_id == 0: return name
    father = objs.get(t.m_Father.path_id)
    if father is None: return name
    return path_of(father.read().m_GameObject.path_id, depth + 1) + '/' + name

hits = {}
for o in env.objects:
    if o.type.name != 'MonoBehaviour': continue
    raw = o.get_raw_data()
    for pid, n in sprites.items():
        if struct.pack('<iq', 0, pid) in raw:
            gpid = struct.unpack_from('<q', raw, 4)[0]
            hits.setdefault(n, set()).add(path_of(gpid))
for n, paths in sorted(hits.items()):
    print(n, sorted(paths)[:4])
print('unreferenced:', sorted(set(sprites.values()) - set(hits)))
