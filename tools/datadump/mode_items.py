"""Mode Select entries (ModeItemController: modeName, targetSceneName, isDevelop) for every ModeSelect_* scene.
Usage: python -I mode_items.py "{game}/Sixtar Gate STARTRAIL_Data"
Reads the game files with UnityPy; nothing from the game is stored in this repository.
"""
import struct, os, sys
import UnityPy

data = sys.argv[1]
gg = UnityPy.load(os.path.join(data, 'globalgamemanagers'))
scenes = []
for o in gg.objects:
    if o.type.name == 'BuildSettings':
        scenes = list(o.read().scenes)
names = {i: s.split('/')[-1].replace('.unity', '') for i, s in enumerate(scenes)}
print('scenes:', ', '.join(f'{i}:{n}' for i, n in names.items()))

TARGETS = {'ModeItemController', 'MusicSelectItemController', 'PlayStyleSelectItemController', 'StandbyController'}

def rd_str(raw, i):
    l = struct.unpack_from('<i', raw, i)[0]
    s = raw[i + 4:i + 4 + l].decode('utf-8', 'replace')
    return s, (i + 4 + l + 3) & ~3

def script_class(env_objs, fid, pid, af):
    # MonoScript lives in globalgamemanagers.assets or the same file; resolve via externals.
    try:
        ptr = af.externals[fid - 1] if fid else None
    except Exception:
        return None
    return None

for i, s in names.items():
    if 'ModeSelect' not in s:
        continue
    env = UnityPy.load(os.path.join(data, f'level{i}'))
    objs = {o.path_id: o for o in env.objects}
    print('=====', s)
    for o in env.objects:
        if o.type.name != 'MonoBehaviour':
            continue
        try:
            mb = o.read(check_read=False)
            sc = mb.m_Script.read()
            cls = sc.m_ClassName
        except Exception:
            continue
        if cls not in TARGETS:
            continue
        raw = o.get_raw_data()
        if len(raw) < 48: continue
        p = 28
        _, p = rd_str(raw, p)                       # m_Name
        cnt = struct.unpack_from('<i', raw, p)[0]   # ModeSelectViewers list
        p += 4 + 12 * cnt
        mode, p = rd_str(raw, p)
        target, p = rd_str(raw, p)
        dev = raw[p]
        go = objs.get(struct.unpack_from('<q', raw, 4)[0])
        gname = go.read().m_Name if go else '?'
        print(f'  [{cls}] {gname}: mode={mode!r} -> scene={target!r} develop={dev}')
