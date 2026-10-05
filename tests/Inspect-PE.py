import hashlib
import json
import pathlib
import re
import struct
import sys

records = []
for arg in sys.argv[1:]:
    path = pathlib.Path(arg)
    data = path.read_bytes()
    pe = struct.unpack_from("<I", data, 60)[0]
    if data[pe:pe + 4] != b"PE\x00\x00":
        raise ValueError("INVALID_PE")
    machine, sections = struct.unpack_from("<HH", data, pe + 4)
    optional_size = struct.unpack_from("<H", data, pe + 20)[0]
    optional = pe + 24
    magic = struct.unpack_from("<H", data, optional)[0]
    directories = optional + (112 if magic == 523 else 96)
    headers = []
    for index in range(sections):
        offset = optional + optional_size + 40 * index
        virtual_size, rva, raw_size, raw = struct.unpack_from("<IIII", data, offset + 8)
        headers.append((rva, max(virtual_size, raw_size), raw))

    def offset_for(rva):
        for start, size, offset in headers:
            if start <= rva < start + size:
                return offset + rva - start
        return rva

    def string_at(offset):
        return data[offset:data.index(b"\x00", offset)].decode("ascii")

    imports = []
    import_rva, _ = struct.unpack_from("<II", data, directories + 8)
    if import_rva:
        descriptor = offset_for(import_rva)
        while True:
            values = struct.unpack_from("<IIIII", data, descriptor)
            if not any(values):
                break
            imports.append(string_at(offset_for(values[3])))
            descriptor += 20
    strings = re.findall(rb"[\x20-\x7e]{6,}", data)
    decoded = [item.decode("ascii") for item in strings]
    decoded += re.findall(r"[\x20-\x7e]{6,}", data.decode("utf-16le", errors="ignore"))
    private_markers = [item for item in decoded if re.search(r"[A-Za-z]:\\Users\\|BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY|gh[pousr]_[A-Za-z0-9]{30,}", item, re.I)]
    records.append({"file": path.name, "sha256": hashlib.sha256(data).hexdigest(), "machine": hex(machine), "managed": bool(struct.unpack_from("<I", data, directories + 14 * 8)[0]), "imports": sorted(set(imports)), "private_marker_count": len(private_markers)})
output = pathlib.Path(__file__).resolve().parent / "artifacts" / "pe-tests.json"
output.write_text(json.dumps(records, indent=2), encoding="utf-8")
raise SystemExit(1 if any(r["private_marker_count"] for r in records) else 0)
