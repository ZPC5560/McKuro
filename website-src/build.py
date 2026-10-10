"""Build the McKuro static site from the partials in this directory.

Layout (all paths relative to this file, so a fresh clone works):
  html/*.html   concatenated in filename order -> <repo>/website/index.html
  css/*.css     concatenated in filename order -> <repo>/website/site.css
  js/*          copied as-is to <repo>/website/
  assets/       fonts + icons live in <repo>/website/; only the generated
                font subsets and the sprite are rewritten here
  ../CHANGELOG.md  rendered into the <!--CHANGELOG--> placeholder, so the
                site's release notes and the GitHub Release body stay one source

Icon SVGs come from src/icons (Phosphor, MIT). Font sources come from
font-src/ and are subset to exactly the glyphs the page renders.

Usage:  python build.py [--font-src DIR]

If a font source is missing the build stops before touching the output, so an
incomplete checkout can never half-overwrite a working site.
"""
import argparse
import os
import re
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)                    # .../McKuro
OUT = os.path.join(REPO, "website")
ICONS = os.path.join(HERE, "src", "icons")

# share the CHANGELOG parser with tools/changelog.py (single contract, one impl)
sys.path.insert(0, os.path.join(REPO, "tools"))
import changelog as cl  # noqa: E402

# CJK + latin ranges the subsets declare (browsers pick a face by unicode-range)
CJK = "U+2000-206F,U+3000-303F,U+3400-4DBF,U+4E00-9FFF,U+FF00-FFEF"
LAT = ("U+0000-00FF,U+0131,U+0152-0153,U+2000-206F,U+20AC,U+2122,U+2191,"
       "U+2193,U+2212,U+2215,U+FEFF,U+FFFD")


def read(p):
    with open(p, encoding="utf-8") as f:
        return f.read()


def write(p, s):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(s)


def concat(sub, ext):
    d = os.path.join(HERE, sub)
    names = sorted(n for n in os.listdir(d) if n.endswith(ext))
    if not names:
        raise SystemExit(f"no {ext} files in {d}")
    return "".join(read(os.path.join(d, n)) for n in names)


def sprite():
    """Inline every icon as a <symbol> so the page needs no icon CDN."""
    out = []
    for n in sorted(os.listdir(ICONS)):
        if not n.endswith(".svg"):
            continue
        svg = read(os.path.join(ICONS, n)).strip()
        vb = re.search(r'viewBox="([^"]+)"', svg).group(1)
        inner = re.sub(r"^.*?<svg[^>]*>|</svg>\s*$", "", svg, flags=re.S)
        out.append(f'<symbol id="i-{n[:-4]}" viewBox="{vb}" fill="currentColor">{inner}</symbol>')
    return ('<svg class="sr" aria-hidden="true" focusable="false" xmlns="http://www.w3.org/2000/svg">'
            "<defs>" + "".join(out) + "</defs></svg>")


def changelog_section():
    """Render the repo CHANGELOG.md into the release-notes section markup.

    Verified before anything is written: if the file is missing or its newest
    version cannot be parsed, the build stops rather than shipping an empty log.
    """
    path = os.path.join(REPO, "CHANGELOG.md")
    if not os.path.isfile(path):
        raise SystemExit(f"missing {path} (the site renders its changelog from it)")
    text = read(path)
    errs = cl.check(text)
    if errs:
        raise SystemExit("CHANGELOG.md failed validation:\n  " + "\n  ".join(errs))
    return cl.to_html(text, open_recent=2)


def subset(py, src, dst, chars):
    """Subset one font to `chars`; raises if fontTools is unavailable."""
    tmp = os.path.join(HERE, ".charset.tmp")
    write(tmp, "".join(sorted(chars)))
    try:
        r = subprocess.run(
            [py, "-m", "fontTools.subset", src, f"--text-file={tmp}",
             "--flavor=woff2", f"--output-file={dst}", "--layout-features=*",
             "--no-hinting", "--desubroutinize"],
            capture_output=True, text=True)
    finally:
        if os.path.exists(tmp):
            os.remove(tmp)
    if r.returncode != 0:
        raise SystemExit(f"fontTools.subset failed for {os.path.basename(src)}:\n{r.stderr[-800:]}")
    print(f"  {os.path.basename(dst):30s} {os.path.getsize(src)/1024:7.0f} KB -> "
          f"{os.path.getsize(dst)/1024:6.1f} KB")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--font-src", default=os.path.join(HERE, "font-src"),
                    help="directory holding the full Noto Sans/Serif SC and Geist woff2 files")
    ap.add_argument("--python", default=sys.executable)
    args = ap.parse_args()

    html = concat("html", ".html").replace("<!--SPRITE-->", sprite())
    if "<!--CHANGELOG-->" not in html:
        raise SystemExit("html partials must keep the <!--CHANGELOG--> placeholder")
    html = html.replace("<!--CHANGELOG-->", changelog_section())
    css = concat("css", ".css")
    js_names = sorted(n for n in os.listdir(os.path.join(HERE, "js")) if n.endswith(".js"))
    js = "".join(read(os.path.join(HERE, "js", n)) for n in js_names)

    used = set(re.findall(r'href="#i-([a-z0-9-]+)"', html))
    have = {n[:-4] for n in os.listdir(ICONS) if n.endswith(".svg")}
    if used - have:
        raise SystemExit(f"markup references missing icons: {sorted(used - have)}")

    # ---- verify inputs before writing anything ----
    fs = args.font_src
    need = {
        f"noto-sans-sc-{w}": os.path.join(fs, f"noto-sans-sc-{w}.woff2") for w in (400, 500, 700)
    }
    need.update({
        "noto-serif-sc-600": os.path.join(fs, "noto-serif-sc-600.woff2"),
        "noto-serif-sc-latin-600": os.path.join(fs, "noto-serif-sc-latin-600.woff2"),
        "geist-latin-400": os.path.join(fs, "geist-latin-400.woff2"),
        "geist-latin-500": os.path.join(fs, "geist-latin-500.woff2"),
        "geist-latin-600": os.path.join(fs, "geist-latin-600.woff2"),
        "geist-latin-700": os.path.join(fs, "geist-latin-700.woff2"),
        "geistmono-latin-400": os.path.join(fs, "geistmono-latin-400.woff2"),
        "geistmono-latin-500": os.path.join(fs, "geistmono-latin-500.woff2"),
    })
    missing = [p for p in need.values() if not os.path.isfile(p)]
    if missing:
        raise SystemExit("missing font sources (output left untouched):\n  " + "\n  ".join(missing))

    vendored = os.path.join(HERE, "vendor")
    # Only the bare three.js build is still needed: the hero backdrop (hero3d.js) uses
    # core THREE only, and the character no longer goes through GLTFLoader.
    required = ["three.module.min.js"]
    missing_vendor = [os.path.join(vendored, r) for r in required
                      if not os.path.isfile(os.path.join(vendored, r))]
    if missing_vendor:
        raise SystemExit("missing vendored libs:\n  " + "\n  ".join(missing_vendor))

    # The hero character (心, both forms) is pre-optimised by tools/optimize-live2d.mjs and
    # committed under live2d/. Verify it is complete before anything is written: a hero with
    # a missing project.json would silently render as an empty margin.
    live2d_src = os.path.join(HERE, "live2d")
    needed_l2d = [
        "puppetloom-web.js", "manifest.json",
        os.path.join("form1", "project.json"), os.path.join("form2", "project.json"),
    ]
    missing_l2d = [p for p in needed_l2d if not os.path.isfile(os.path.join(live2d_src, p))]
    if missing_l2d:
        raise SystemExit(
            "missing optimised Live2D assets (output left untouched):\n  "
            + "\n  ".join(missing_l2d)
            + "\nRun tools/optimize-live2d.mjs against the original showcase package."
        )

    # ---- write page + scripts ----
    write(os.path.join(OUT, "index.html"), html)
    write(os.path.join(OUT, "site.css"), css)
    for n in js_names:
        shutil.copy2(os.path.join(HERE, "js", n), os.path.join(OUT, n))

    # vendor/ is copied wholesale: three.module.min.js is imported by name from the
    # importmap in the page head, so the path has to stay ./vendor/three.module.min.js.
    vd = os.path.join(OUT, "vendor")
    if os.path.isdir(vd):
        shutil.rmtree(vd)
    os.makedirs(vd, exist_ok=True)
    for r in required:
        shutil.copy2(os.path.join(vendored, r), os.path.join(vd, r))

    # ---- the hero character: rebuilt from scratch so a removed asset cannot linger ----
    l2d_out = os.path.join(OUT, "live2d")
    if os.path.isdir(l2d_out):
        shutil.rmtree(l2d_out)
    shutil.copytree(live2d_src, l2d_out)

    # ---- fonts: sans covers the whole page, serif only the headings ----
    SAFE = set("0123456789%·—–、。，：；！？（）《》「」“”‘’…+/-=#@&*[]{}<>|\\~^$ "
               "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ")
    sans_chars = set(html + js) | SAFE
    serif_src = " ".join(
        re.sub(r"<[^>]+>", "", m)
        for m in re.findall(r"<(?:h1|h2|h3)[^>]*>(.*?)</(?:h1|h2|h3)>", html, re.S)
    )
    serif_chars = set(serif_src) | set("0123456789·，。、：“”「」")

    fd = os.path.join(OUT, "fonts")
    os.makedirs(fd, exist_ok=True)
    print("fonts:")
    for w in (400, 500, 700):
        subset(args.python, need[f"noto-sans-sc-{w}"], os.path.join(fd, f"noto-sans-sc-{w}.woff2"), sans_chars)
    subset(args.python, need["noto-serif-sc-600"], os.path.join(fd, "noto-serif-sc-600.woff2"), serif_chars)
    for key in ("noto-serif-sc-latin-600", "geist-latin-400", "geist-latin-500",
                "geist-latin-600", "geist-latin-700", "geistmono-latin-400", "geistmono-latin-500"):
        shutil.copy2(need[key], os.path.join(fd, os.path.basename(need[key])))

    faces = [("Noto Sans SC", w, f"noto-sans-sc-{w}.woff2", CJK) for w in (400, 500, 700)]
    faces += [("Noto Serif SC", 600, "noto-serif-sc-600.woff2", CJK),
              ("Noto Serif SC", 600, "noto-serif-sc-latin-600.woff2", LAT)]
    faces += [("Geist", w, f"geist-latin-{w}.woff2", LAT) for w in (400, 500, 600, 700)]
    faces += [("Geist Mono", w, f"geistmono-latin-{w}.woff2", LAT) for w in (400, 500)]
    write(os.path.join(OUT, "fonts.css"),
          "/* Generated by website-src/build.py. Self-hosted, subset to this page's glyphs. */\n"
          + "\n".join(
              f"@font-face{{font-family:'{f}';font-style:normal;font-weight:{w};font-display:swap;"
              f"src:url(fonts/{n}) format('woff2');unicode-range:{r};}}"
              for f, w, n, r in faces) + "\n")

    total = sum(os.path.getsize(os.path.join(r, f))
                for r, _, files in os.walk(OUT) for f in files)
    print(f"output {OUT}  ({total/1024:.0f} KB, {len(used)} icons used)")


if __name__ == "__main__":
    main()
