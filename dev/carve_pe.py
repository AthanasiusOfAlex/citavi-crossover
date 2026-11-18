import sys, struct, os
src, out = sys.argv[1], sys.argv[2]
d = open(src, 'rb').read()
pos = 0; n = 0
while True:
    i = d.find(b'MZ', pos)
    if i < 0: break
    pos = i + 2
    try:
        e = struct.unpack_from('<I', d, i + 0x3c)[0]
        if e > 0x1000 or d[i+e:i+e+4] != b'PE\0\0': continue
        nsec = struct.unpack_from('<H', d, i+e+6)[0]
        optsz = struct.unpack_from('<H', d, i+e+20)[0]
        sec = i + e + 24 + optsz
        end = 0
        for s in range(nsec):
            rawsz, rawptr = struct.unpack_from('<II', d, sec + s*40 + 16)
            end = max(end, rawptr + rawsz)
        magic = struct.unpack_from('<H', d, i+e+24)[0]
        ddoff = i+e+24 + (96 if magic == 0x10b else 112)
        clr_rva, clr_sz = struct.unpack_from('<II', d, ddoff + 14*8)
        if i == 0: continue
        name = os.path.join(out, f'pe_{n}_{i:x}{"_managed" if clr_rva else ""}.dll')
        open(name, 'wb').write(d[i:i+end]); n += 1
        print(name, end, 'managed' if clr_rva else 'native')
    except struct.error:
        pass
