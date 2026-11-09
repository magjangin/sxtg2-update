"""모드가 훅하는 함수의 ICF(동일 코드 접기) 개수를 새 빌드의 script.json으로 다시 센다.

런타임 ICF 가드(PatchResilience)는 1~3바이트 빈 스텁만 잡는다. 업데이트로 훅 대상이 내용 있는 다른 함수와
같은 주소를 쓰게 되면 패치가 엉뚱한 클래스의 this로 실행되므로, 업데이트 직후 이 스크립트로 확인한다.

사용:
  python -I icf_check.py <script.json> <HOOK_TABLE.md>
  (script.json = 새 GameAssembly.dll + global-metadata.dat로 Il2CppDumper를 다시 돌린 출력)

출력: 표에 적힌 ICF 개수(없으면 1)와 다른 대상, 이름으로 못 찾은 대상, RVA가 바뀐 대상.
종료 코드: ICF가 늘어난 대상이 있으면 1.
"""
import json
import re
import sys
from collections import Counter, defaultdict

ROW = re.compile(r"^\| `[^`]+` \| `([^`]+)` \| `([^`]+)` \| [^|]+\| `0x([0-9A-Fa-f]+)`(?: \(ICF×(\d+)\))? \|")


def il2cpp_type_name(interop: str) -> str:
    """Il2CppInterop 이름(__c__DisplayClass34_0, _WaitUpdateCheck_d__6, __c)을 Il2CppDumper 이름(<>c__…, <…>d__6, <>c)으로."""
    parts = []
    for p in interop.split("."):
        p = re.sub(r"^__c__DisplayClass", "<>c__DisplayClass", p)
        p = re.sub(r"^_(\w+?)_d__(\d+)$", r"<\1>d__\2", p)
        if p == "__c":
            p = "<>c"
        parts.append(p)
    return ".".join(parts)


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    with open(sys.argv[1], encoding="utf-8") as f:
        methods = json.load(f)["ScriptMethod"]
    by_address = Counter(m["Address"] for m in methods)
    by_name = defaultdict(list)
    for m in methods:
        by_name[m["Name"]].append(m["Address"])

    rows = []
    with open(sys.argv[2], encoding="utf-8-sig") as f:
        for line in f:
            m = ROW.match(line.strip())
            if m:
                rows.append((m.group(1), m.group(2), int(m.group(3), 16), int(m.group(4) or 1)))

    worse = missing = moved = 0
    for type_name, method, old_rva, expected_icf in rows:
        bare = re.sub(r"\(.*\)$", "", method)  # 오버로드 표기 "UpdateUserData(Users, Action)" → 이름만
        suffix = f"{il2cpp_type_name(type_name)}$${bare}"
        addrs = sorted({a for n, al in by_name.items() if n == suffix or n.endswith("." + suffix) for a in al})
        if bare != method and old_rva in addrs:
            addrs = [old_rva]  # 같은 빌드면 표의 RVA로 오버로드를 고름(새 빌드면 모든 오버로드를 함께 봄)
        label = f"{type_name}.{method}"
        if not addrs:
            missing += 1
            print(f"[못 찾음] {label} (이름이 바뀌었거나 컴파일러 생성 이름의 순번이 밀림 — 모드는 실행 중 동적으로 찾음)")
            continue
        counts = [by_address[a] for a in addrs]
        if len(addrs) == 1 and addrs[0] != old_rva:
            moved += 1
        if max(counts) > expected_icf:
            worse += 1
            print(f"[ICF 증가] {label}: " + ", ".join(f"0x{a:X} ICF×{c}" for a, c in zip(addrs, counts))
                  + f" (표: ICF×{expected_icf}) → 이 주소를 패치하면 다른 함수도 같이 걸림. 패치 대상을 바꿀 것")
        elif max(counts) < expected_icf:
            print(f"[ICF 감소] {label}: " + ", ".join(f"0x{a:X} ICF×{c}" for a, c in zip(addrs, counts))
                  + f" (표: ICF×{expected_icf}) — 표를 고칠 것")

    print(f"\n대상 {len(rows)}개: ICF 증가 {worse}, 이름 못 찾음 {missing}, RVA 바뀜 {moved}")
    return 1 if worse else 0


if __name__ == "__main__":
    sys.exit(main())
