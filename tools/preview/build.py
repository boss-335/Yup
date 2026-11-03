#!/usr/bin/env python3
"""Builds docs/preview/roster.html from data/characters.json, data/visuals.json and data/moves.json.
Usage (from the repo root): python3 tools/preview/build.py
The page is self-contained (only Google Fonts online; falls back to system fonts offline)."""
import json, pathlib

root = pathlib.Path(__file__).resolve().parents[2]
data = root / "data"
chars = json.loads((data / "characters.json").read_text(encoding="utf-8"))
vis = json.loads((data / "visuals.json").read_text(encoding="utf-8"))
moves = json.loads((data / "moves.json").read_text(encoding="utf-8"))
html = (root / "tools/preview/roster.template.html").read_text(encoding="utf-8")
st, tiers = vis["stage"], vis["tiers"]
for key, val in {
    "/*BG_TOP*/": st["background_top"], "/*BG_BOTTOM*/": st["background_bottom"], "/*FLOOR*/": st["floor"],
    "/*SPOT*/": st["spotlight"], "/*CURTAIN*/": st["curtain"], "/*PERFECT*/": tiers["perfect_color"], "/*SOUR*/": tiers["sour_color"],
    "/*CHARACTERS*/": json.dumps(chars, ensure_ascii=False), "/*VISUALS*/": json.dumps(vis, ensure_ascii=False),
    "/*MOVES*/": json.dumps(moves, ensure_ascii=False),
}.items():
    assert key in html, key
    html = html.replace(key, val)
out = root / "docs/preview/roster.html"
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(html, encoding="utf-8")
print(f"wrote {out.relative_to(root)} ({len(html)//1024} KB)")
