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

Usage:  python build.py [--font-src DIR] [--out DIR]

If a font source is missing the build stops before touching the output, so an
incomplete checkout can never half-overwrite a working site.
"""
import argparse
import html as _html
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
    # with_en=True: every release ships both languages in the same markup, so site.js
    # only switches which one is visible (no extra request, and a visitor with JS off
    # can still expand the English block of any single release).
    return cl.to_html(text, open_recent=2, with_en=True)


def latest_release():
    """(version, date) of the newest CHANGELOG entry, e.g. ('1.3.6.2', '2026-10-18').

    The page bakes a version into three places as the no-JS / offline fallback for
    the live value fetched from the GitHub API. Those literals drifted two releases
    behind because nobody remembers to edit them, so they are derived here instead:
    CHANGELOG.md is already the single source of truth for releases, and the build
    already refuses to run without a valid newest section.
    """
    sections = cl.parse(read(os.path.join(REPO, "CHANGELOG.md")))
    version, date = sections[0][0], sections[0][1]
    if not version or not date:
        raise SystemExit("newest CHANGELOG section needs a version and a date")
    return version.lstrip("v"), date


def stamp_release(html, version, date):
    """Replace the baked-in fallback version/date markers with the real release.

    Markers are explicit (`<!--VER-->` / `<!--DATE-->`) rather than a regex over any
    `vX.Y.Z`-looking string, so a version number that appears in prose (a changelog
    entry, a download filename) can never be rewritten by accident. `<!--VER-RAW-->`
    is the same version without the leading `v` (asset file names use it).
    """
    for marker in ("<!--VER-->", "<!--DATE-->", "<!--VER-RAW-->"):
        if marker not in html:
            raise SystemExit(f"html partials must keep the {marker} marker")
    return (html.replace("<!--VER-->", "v" + version)
                .replace("<!--VER-RAW-->", version)
                .replace("<!--DATE-->", date))


def _release_url(version, asset):
    """GitHub release 直链(assets 由 CI 按 .github/workflows/build-and-test.yml 的
    固定命名上传,这里按同一套约定构造,版本号永远来自 CHANGELOG)。"""
    return (f"https://github.com/ZPC5560/McKuro/releases/download/"
            f"v{version}/{asset.format(ver=version)}")


def download_section():
    """Render the per-platform download cards with direct asset links.

    The release asset names follow the CI workflow's fixed naming (setup / zip /
    dmg / app.zip / tar.gz / deb / rpm), so they can be derived from the CHANGELOG
    version at build time — no second place to keep in sync. Every link is a direct
    GitHub release download; site.js only picks which platform is *recommended*
    (badge + top CTA), it never builds URLs.
    """
    version, _date = latest_release()
    ver = version.lstrip("v")

    def btn(href, label, tag, cls="btn--primary"):
        return (f'<a class="btn {cls}" href="{href}">'
                f'<span>{label}</span>'
                f'<span class="btn__end">{tag}</span></a>')

    def alt(href, label):
        return f'<a class="dlplat__altlink" href="{href}">{label}</a>'

    def card(os_id, arch, title, sub, primary, alts):
        altlinks = "".join(f"<li>{a}</li>" for a in alts)
        return (f'<article class="dlplat" id="dl-{os_id}" data-os="{os_id}"'
                + (f' data-arch="{arch}"' if arch else "") + ">"
                + f'<header class="dlplat__head"><h3 class="dlplat__h">{title}</h3>'
                + f'<p class="dlplat__sub">{sub}</p></header>'
                + f'<div class="dlplat__body">{primary}'
                + (f'<ul class="dlplat__alts">{altlinks}</ul>' if altlinks else "")
                + "</div>"
                + '<p class="dlplat__reco" data-reco hidden>为你推荐</p>'
                + "</article>")

    win_ins = btn(_release_url(ver, "McKuro-setup-{ver}.exe"), "安装包", ".exe")
    win_zip = alt(_release_url(ver, "McKuro-win-x64-{ver}.zip"), "绿色包 · 免安装（zip）")
    mac_dmg_a = btn(_release_url(ver, "McKuro-osx-arm64-{ver}.dmg"), "安装包（拖拽）", ".dmg")
    mac_alt_a = [alt(_release_url(ver, "McKuro-osx-arm64-{ver}.app.zip"), "绿色包 · .app.zip"),
                 alt(_release_url(ver, "McKuro-osx-arm64-{ver}.zip"), "绿色包 · zip")]
    mac_dmg_x = btn(_release_url(ver, "McKuro-osx-x64-{ver}.dmg"), "安装包（拖拽）", ".dmg")
    mac_alt_x = [alt(_release_url(ver, "McKuro-osx-x64-{ver}.app.zip"), "绿色包 · .app.zip"),
                 alt(_release_url(ver, "McKuro-osx-x64-{ver}.zip"), "绿色包 · zip")]
    lin_tar = btn(_release_url(ver, "McKuro-linux-x64-{ver}.tar.gz"), "通用压缩包", ".tar.gz")
    lin_alts = [alt(_release_url(ver, "mckuro_{ver}_amd64.deb"), "Debian / Ubuntu · .deb"),
                alt(_release_url(ver, "mckuro-{ver}-1.x86_64.rpm"), "Fedora / openSUSE · .rpm")]

    cards = [
        card("windows", "x64", "Windows", "x64 · 安装包与绿色包都内置自动更新", win_ins, [win_zip]),
        card("macos", "arm64", "macOS · Apple 芯片", "arm64 · 内置视频播放与 Live2D", mac_dmg_a, mac_alt_a),
        card("macos", "x64", "macOS · Intel", "x64 · 视频回退封面，Live2D 未实机验证", mac_dmg_x, mac_alt_x),
        card("linux", "x64", "Linux", "x64 · 视频需系统 libmpv，Live2D 未实机验证", lin_tar, lin_alts),
    ]
    return ('<div class="dlplat__grid" id="dl-grid">'
            + "".join(cards) + "</div>")


def changelog_nav():
    """Render the version nav bar for the 更新日志 section.

    One chip per release, linking to the `id="rel-<ver>"` anchor that
    tools/changelog.py to_html() now emits. Clicking it with JS open the entry too
    (site.js); without JS it is a plain in-page jump to the entry's summary.
    """
    sections = cl.parse(read(os.path.join(REPO, "CHANGELOG.md")))
    chips = []
    for v, _d, _body in sections:
        anchor = "rel-" + re.sub(r"[^A-Za-z0-9._-]", "-", v)
        chips.append(f'<a class="clnav__link" href="#{anchor}">'
                     f'<span class="mono">{_html.escape(v)}</span></a>')
    # The sticky bar lives on the outer element; the horizontal chip scroll goes on an
    # inner one. A sticky element that is itself a scroll container never sticks.
    return ('<nav class="clnav" aria-label="更新日志版本目录">'
            '<div class="clnav__scroll">' + "".join(chips) + "</div></nav>")


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
    ap.add_argument("--out", default=OUT,
                    help="output directory; defaults to <repo>/website. Point it elsewhere "
                         "to preview a change without touching the committed site.")
    args = ap.parse_args()
    out = os.path.abspath(args.out)

    html = concat("html", ".html").replace("<!--SPRITE-->", sprite())
    for ph in ("<!--CHANGELOG-->", "<!--DOWNLOADS-->", "<!--CLNAV-->"):
        if ph not in html:
            raise SystemExit(f"html partials must keep the {ph} placeholder")
    version, date = latest_release()
    html = stamp_release(html, version, date)
    html = (html.replace("<!--CHANGELOG-->", changelog_section())
                .replace("<!--DOWNLOADS-->", download_section())
                .replace("<!--CLNAV-->", changelog_nav()))
    print(f"release stamped: v{version} ({date})")
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
    # assets/ (app icon, social card) is hand-made and lives only in the committed site,
    # so it is not generated. A preview build into another directory still needs it,
    # otherwise the page shows broken icons and a layout bug cannot be told from a
    # missing file.
    assets_src = os.path.join(OUT, "assets")
    if out != OUT and os.path.isdir(assets_src):
        shutil.copytree(assets_src, os.path.join(out, "assets"), dirs_exist_ok=True)

    write(os.path.join(out, "index.html"), html)
    write(os.path.join(out, "site.css"), css)
    for n in js_names:
        shutil.copy2(os.path.join(HERE, "js", n), os.path.join(out, n))

    # vendor/ is copied wholesale: three.module.min.js is imported by name from the
    # importmap in the page head, so the path has to stay ./vendor/three.module.min.js.
    vd = os.path.join(out, "vendor")
    if os.path.isdir(vd):
        shutil.rmtree(vd)
    os.makedirs(vd, exist_ok=True)
    for r in required:
        shutil.copy2(os.path.join(vendored, r), os.path.join(vd, r))

    # ---- the hero character: rebuilt from scratch so a removed asset cannot linger ----
    l2d_out = os.path.join(out, "live2d")
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

    fd = os.path.join(out, "fonts")
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
    write(os.path.join(out, "fonts.css"),
          "/* Generated by website-src/build.py. Self-hosted, subset to this page's glyphs. */\n"
          + "\n".join(
              f"@font-face{{font-family:'{f}';font-style:normal;font-weight:{w};font-display:swap;"
              f"src:url(fonts/{n}) format('woff2');unicode-range:{r};}}"
              for f, w, n, r in faces) + "\n")

    total = sum(os.path.getsize(os.path.join(r, f))
                for r, _, files in os.walk(out) for f in files)
    print(f"output {out}  ({total/1024:.0f} KB, {len(used)} icons used)")


if __name__ == "__main__":
    main()
