"""Hierarchy of root GameObjects (with sprites their components reference) inside a bundle or assets file.
Usage: python -I prefab_tree.py <bundle or .assets> <RootName> [RootName ...]
Reads the game files with UnityPy; nothing from the game is stored in this repository.
"""
import struct, sys
import UnityPy

env = UnityPy.load(sys.argv[1])
objs = {o.path_id: o for o in env.objects}
roots = set(sys.argv[2:])

trans = {}
for o in env.objects:
    if o.type.name in ('Transform', 'RectTransform'):
        t = o.read()
        trans[t.m_GameObject.path_id] = t

def children(go_pid):
    t = trans.get(go_pid)
    if t is None: return []
    out = []
    for c in t.m_Children:
        co = objs.get(c.path_id)
        if co is None: continue
        out.append(co.read().m_GameObject.path_id)
    return out

sprite_names = {o.path_id: o.read().m_Name for o in env.objects if o.type.name == 'Sprite'}
clip_names = {o.path_id: o.read().m_Name for o in env.objects if o.type.name in ('AnimationClip', 'AudioClip', 'VideoClip')}

def comps_sprites(go_pid):
    found = set()
    go = objs[go_pid].read()
    for c in go.m_Components:
        co = objs.get(c.path_id)
        if co is None or co.type.name != 'MonoBehaviour': continue
        raw = co.get_raw_data()
        for i in range(0, len(raw) - 12, 4):
            fid, pid = struct.unpack_from('<iq', raw, i)
            if fid == 0 and pid in sprite_names: found.add(sprite_names[pid])
    return found

def walk(go_pid, depth=0, out=None):
    name = objs[go_pid].read().m_Name
    s = comps_sprites(go_pid)
    print('  ' * depth + name + (('  [' + ', '.join(sorted(s)) + ']') if s else ''))
    if depth < 6:
        for c in children(go_pid):
            walk(c, depth + 1)

for o in env.objects:
    if o.type.name == 'GameObject':
        g = o.read()
        if g.m_Name in roots and trans.get(o.path_id) is not None and trans[o.path_id].m_Father.path_id == 0:
            print('=====', g.m_Name)
            walk(o.path_id)
print('clips:', sorted(n for n in clip_names.values() if any(k in n.lower() for k in ('zantetsu', 'christmas', 'xmas', 'result'))))
