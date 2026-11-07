#!/usr/bin/env python3
"""Release helpers, used by release.sh.

  package   build the plugin zip: the DLL, meta.json (version and timestamp filled in), thumb.png
  notes     print the English CHANGELOG section of a version (release notes)
  manifest  add / replace a version in manifest.json (the Jellyfin plugin repository)
"""
import argparse
import datetime
import hashlib
import json
import pathlib
import re
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent


def short(version: str) -> str:
    """1.5.0.0 -> 1.5.0 (the CHANGELOG headings)."""
    parts = version.split(".")
    return ".".join(parts[:3]) if len(parts) == 4 and parts[3] == "0" else version


def changelog_section(version: str) -> str:
    text = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
    english = text.split("## Magyar")[0]
    match = re.search(r"^### \[" + re.escape(short(version)) + r"\][^\n]*\n(.*?)(?=^### \[|^---|\Z)",
                      english, re.S | re.M)
    return match.group(1).strip() if match else ""


def now() -> str:
    return datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.0000000Z")


def package(args):
    meta = json.loads((ROOT / "meta.json").read_text(encoding="utf-8"))
    meta["version"] = args.version
    meta["timestamp"] = now()
    meta["changelog"] = changelog_section(args.version)
    with zipfile.ZipFile(args.out, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(args.dll, "Jellyfin.Plugin.IndexJelly.dll")
        z.writestr("meta.json", json.dumps(meta, indent=2, ensure_ascii=False))
        z.write(ROOT / "assets" / "thumb.png", "thumb.png")


def notes(args):
    print(changelog_section(args.version) or f"IndexJelly {args.version}")


def manifest(args):
    path = ROOT / "manifest.json"
    repo = json.loads(path.read_text(encoding="utf-8"))
    meta = json.loads((ROOT / "meta.json").read_text(encoding="utf-8"))
    plugin = repo[0]
    entry = {
        "version": args.version,
        "changelog": changelog_section(args.version),
        "targetAbi": meta["targetAbi"],
        "sourceUrl": args.url,
        "checksum": args.checksum or hashlib.md5(pathlib.Path(args.zip).read_bytes()).hexdigest(),
        "timestamp": now(),
    }
    plugin["versions"] = [entry] + [v for v in plugin.get("versions", []) if v["version"] != args.version]
    path.write_text(json.dumps(repo, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main():
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="cmd", required=True)
    a = sub.add_parser("package"); a.add_argument("--version", required=True); a.add_argument("--dll", required=True); a.add_argument("--out", required=True)
    b = sub.add_parser("notes"); b.add_argument("--version", required=True)
    c = sub.add_parser("manifest"); c.add_argument("--version", required=True); c.add_argument("--checksum"); c.add_argument("--zip"); c.add_argument("--url", required=True)
    args = p.parse_args()
    if args.cmd == "manifest" and not (args.checksum or args.zip):
        p.error("manifest needs --checksum or --zip")
    {"package": package, "notes": notes, "manifest": manifest}[args.cmd](args)


if __name__ == "__main__":
    main()
