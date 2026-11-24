"""Scan E:\\Sounds and write a CSV of every audio file (WAV header read for length/rate/channels)."""
import csv, os, struct, sys

root, out = sys.argv[1], sys.argv[2]

def wav_info(path):
    try:
        with open(path, 'rb') as f:
            if f.read(4) != b'RIFF': return None
            f.read(4)
            if f.read(4) != b'WAVE': return None
            rate = ch = bits = byterate = None
            while True:
                hdr = f.read(8)
                if len(hdr) < 8: return None
                cid, size = hdr[:4], struct.unpack('<I', hdr[4:])[0]
                if cid == b'fmt ':
                    d = f.read(size)
                    _, ch, rate, byterate, _, bits = struct.unpack('<HHIIHH', d[:16])
                elif cid == b'data':
                    if not byterate: return None
                    return round(size / byterate, 2), rate, ch, bits
                else:
                    f.seek(size + (size & 1), 1)
    except OSError:
        return None

rows = []
for dp, _, files in os.walk(root):
    for fn in files:
        ext = os.path.splitext(fn)[1].lower().lstrip('.')
        if ext not in ('wav', 'mp3'): continue
        p = os.path.join(dp, fn)
        rel = os.path.relpath(p, root).replace('\\', '/')
        parts = rel.split('/')
        group, pack = parts[0], parts[1] if len(parts) > 2 else ''
        info = wav_info(p) if ext == 'wav' else None
        dur, rate, ch, bits = info if info else ('', '', '', '')
        rows.append([group, pack, rel, ext, dur, rate, ch, bits, os.path.getsize(p)])

rows.sort(key=lambda r: r[2].lower())
with open(out, 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f)
    w.writerow(['group', 'pack', 'path', 'format', 'seconds', 'sample_rate', 'channels', 'bits', 'bytes'])
    w.writerows(rows)
print(len(rows), 'audio files')
