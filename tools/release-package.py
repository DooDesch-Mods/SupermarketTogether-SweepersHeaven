"""Validate release metadata and package only the plugin and public documentation."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import xml.etree.ElementTree as ET
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("version")
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
projects = list(root.glob("*.csproj"))
if len(projects) != 1:
    raise RuntimeError("Expected one mod project at the repository root.")
name = projects[0].stem
version = args.version.removeprefix("v")
if not re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", version):
    raise RuntimeError(f"Invalid release version: {args.version}")
if ET.parse(projects[0]).findtext(".//Version") != version:
    raise RuntimeError("The tag and project version differ.")
manifest_path = root / "thunderstore/manifest.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
if manifest["name"] != name or manifest["version_number"] != version:
    raise RuntimeError("The manifest, project and tag disagree.")
if len(manifest["description"]) > 250:
    raise RuntimeError("Thunderstore descriptions must not exceed 250 characters.")
icon = (root / "thunderstore/icon.png").read_bytes()
if icon[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", icon[16:24]) != (256, 256):
    raise RuntimeError("Thunderstore requires a 256 x 256 PNG icon.")
readme = (root / "README.md").read_text(encoding="utf-8")
support = f"> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/{name.lower()}](https://support.doodesch.de/{name.lower()})."
if readme.splitlines().count(support) != 1 or support not in "\n".join(readme.splitlines()[:6]):
    raise RuntimeError("The public README must contain exactly one support line near the top.")
changelog = (root / "CHANGELOG.md").read_text(encoding="utf-8")
section = re.search(r"^## \["+re.escape(version)+r"\][^\n]*\n(.*?)(?=^## |\Z)", changelog, re.M | re.S)
if not section or not section.group(1).strip():
    raise RuntimeError(f"No release notes found for {version}.")
dll = root / f"bin/Release/netstandard2.1/{name}.dll"
files = {dll.name: dll, "README.md": root / "README.md", "CHANGELOG.md": root / "CHANGELOG.md",
    "LICENSE.md": root / "LICENSE.md", "manifest.json": manifest_path, "icon.png": root / "thunderstore/icon.png"}
dist = root / "dist"
dist.mkdir(exist_ok=True)
output = dist / f"{name}-{version}.zip"
with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
    for entry, source in files.items():
        archive.write(source, entry)
with zipfile.ZipFile(output) as archive:
    if archive.testzip() is not None or set(archive.namelist()) != set(files):
        raise RuntimeError("The release archive inventory is invalid.")
    for entry, source in files.items():
        if archive.read(entry) != source.read_bytes():
            raise RuntimeError(f"Release archive readback differs: {entry}")
(dist / "release-notes.md").write_text(section.group(1).strip()+"\n", encoding="utf-8")
receipt = {"mod": name, "version": version, "dllSha256": hashlib.sha256(dll.read_bytes()).hexdigest(),
    "zipSha256": hashlib.sha256(output.read_bytes()).hexdigest(), "entries": list(files)}
(dist / "package-check.json").write_text(json.dumps(receipt, indent=2)+"\n", encoding="utf-8")
print(json.dumps(receipt, indent=2))
