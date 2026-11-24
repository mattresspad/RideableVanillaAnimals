"""Audit stable pack payloads and build a deterministic, folder-wrapped player ZIP."""
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED
import hashlib, json
root = Path(__file__).resolve().parents[1]
pack = root / "mods/Rideable Vanilla Animals"
expected = json.loads((root / "evidence/historical/package-audit.json").read_text(encoding="utf-8"))
for name, digest in expected["files"].items():
    assert hashlib.sha256((root / "mods" / name).read_bytes()).hexdigest() == digest, "Original payload changed: " + name
fit = json.loads((pack / "transport.json").read_bytes())
ids = [i for m in fit["mounts"] for i in m["animalRecordIds"]]
assert fit["schemaVersion"] == 3 and fit["packId"] == "rideable-vanilla-animals"
assert len(ids) == len(set(ids)) == 43 and len(fit["mounts"]) == 22
out = root / "dist"
out.mkdir(exist_ok=True)
archive = out / "Rideable-Vanilla-Animals-0.1.0-playtest.1.zip"
files = {p.relative_to(root / "mods").as_posix(): p.read_bytes() for p in pack.rglob("*") if p.is_file()}
assert set(files) == set(expected["files"]), "Unexpected player files"
files["Rideable Vanilla Animals/CC0-1.0.txt"] = (root / "CC0-1.0.txt").read_bytes()
with ZipFile(archive, "w", compression=ZIP_DEFLATED) as z:
    for name, data in sorted(files.items()):
        info = ZipInfo(name, (2026, 1, 1, 0, 0, 0)); info.compress_type = ZIP_DEFLATED; info.external_attr = 0o644 << 16
        z.writestr(info, data)
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
(out / "SHA256SUMS.txt").write_text(digest + "  " + archive.name + "\n", encoding="utf-8")
print(f"PASS: 43 unique records, 22 fits, unchanged four-file runtime payload. {digest}  {archive.name}")
