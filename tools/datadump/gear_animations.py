"""GearObject.animationConnector items (state / animator object / animation name / lane / judge) of a gear prefab.
Usage: python -I gear_animations.py "{game}/Sixtar Gate STARTRAIL_Data/sharedassets0.assets" <GearName> [GearName ...]
Reads the game files with UnityPy; nothing from the game is stored in this repository.
GearAnimationState: 1 GateOpen, 2 PressLane, 3 ReleaseLane, 4 NoteJudged (lane ignored by NoteJudegdModule), 5 ScoreUpdate.
LaneIndex: 0 LL, 1 L, 2 R, 3 RR, 4 Gate, 5 LT, 6 RT. EJudge: 0 BlueStar, 1 WhiteStar, 2 YellowStar, 3 RedStar.
"""
import struct, sys
import UnityPy

env = UnityPy.load(sys.argv[1])
objs = {o.path_id: o for o in env.objects}
STATE = {0: 'None', 1: 'GateOpen', 2: 'PressLane', 3: 'ReleaseLane', 4: 'NoteJudged', 5: 'ScoreUpdate'}
LANE = ['LL', 'L', 'R', 'RR', 'Gate', 'LT', 'RT']

trans = {}
for o in env.objects:
    if o.type.name in ('Transform', 'RectTransform'):
        t = o.read()
        trans[t.m_GameObject.path_id] = t
parent_of = {}
for gpid, t in trans.items():
    for c in t.m_Children:
        co = objs.get(c.path_id)
        if co is not None:
            parent_of[co.read().m_GameObject.path_id] = gpid

def go_path(gpid):
    parts = []
    while gpid in objs:
        parts.append(objs[gpid].read().m_Name)
        if gpid not in parent_of: break
        gpid = parent_of[gpid]
    return '/'.join(reversed(parts))

def descendants(gpid):
    yield gpid
    t = trans.get(gpid)
    if t is None: return
    for c in t.m_Children:
        co = objs.get(c.path_id)
        if co is not None:
            yield from descendants(co.read().m_GameObject.path_id)

def rstr(raw, pos):
    n = struct.unpack_from('<i', raw, pos)[0]
    s = raw[pos + 4:pos + 4 + n].decode('utf-8')
    return s, (pos + 4 + n + 3) & ~3

def owner_go(pid):
    o = objs.get(pid)
    if o is None: return None
    if o.type.name == 'GameObject': return pid
    return struct.unpack_from('<iq', o.get_raw_data(), 0)[1]

roots = {o.path_id: o.read().m_Name for o in env.objects if o.type.name == 'GameObject'}
for want in sys.argv[2:]:
    for gpid, name in roots.items():
        if name != want or gpid in parent_of: continue
        for d in descendants(gpid):
            for c in objs[d].read().m_Components:
                co = objs.get(c.path_id)
                if co is None or co.type.name != 'MonoBehaviour': continue
                raw = co.get_raw_data()
                # MonoScripts live in another file, so recognise GearObject by its first field instead:
                # List<GearAnimationItem> whose items parse as (state 0..5, PPtr, string, lane 0..6, judge 0..3).
                try:
                    _, pos = rstr(raw, 28)
                    count = struct.unpack_from('<i', raw, pos)[0]; pos += 4
                    if not 0 < count < 300: continue
                    rows = []
                    for _ in range(count):
                        state = struct.unpack_from('<i', raw, pos)[0]; pos += 4
                        fid, apid = struct.unpack_from('<iq', raw, pos); pos += 12
                        anim, pos = rstr(raw, pos)
                        lane, judge = struct.unpack_from('<ii', raw, pos); pos += 8
                        if not (0 <= state <= 5 and 0 <= judge <= 3 and -1 <= lane <= 6): raise ValueError
                        rows.append((state, apid, anim, lane, judge))
                except Exception:
                    continue
                print(f'== {want} (on {go_path(d)}) items={count}')
                for state, apid, anim, lane, judge in rows:
                    ag = owner_go(apid)
                    print(f'  {STATE.get(state, state):11} lane={LANE[lane] if 0 <= lane < 7 else lane:4} judge={judge} '
                          f'anim={anim!r} animator={go_path(ag) if ag else apid}')
