# 사용: python -I capstone_lengths.py <GameAssembly.dll> <script.json> <출력.txt> [최대 개수]
# 컴파일러 생성 람다(이름에 "b__")·상태머신 MoveNext의 앞 192바이트를 capstone으로 해독해 명령 길이를 적는다.
# lambdascan이 X64Decoder 길이와 대조한다(--lengths 출력.txt).
import sys, json
import pefile, capstone
dll, sj, out = sys.argv[1], sys.argv[2], sys.argv[3]
limit = int(sys.argv[4]) if len(sys.argv) > 4 else 4000
d = json.load(open(sj, encoding="utf-8"))
rvas = sorted({m["Address"] for m in d["ScriptMethod"] if "b__" in m["Name"] or "MoveNext" in m["Name"]})
pe = pefile.PE(dll, fast_load=True)
img = pe.get_memory_mapped_image()
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
step = max(1, len(rvas) // limit)
n = 0
with open(out, "w") as f:
    for rva in rvas[::step]:
        code = img[rva:rva + 192]
        lens = []
        for ins in md.disasm(code, rva):
            if ins.bytes[0] == 0xCC: break
            lens.append(ins.size)
        if lens:
            f.write("0x%X %s\n" % (rva, " ".join(map(str, lens))))
            n += 1
print(n, "functions,", len(rvas), "candidates")
