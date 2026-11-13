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

    def logo(svg):
        return f'<span class="dlplat__logo" aria-hidden="true">{svg}</span>'

    def card(os_id, arch, title, sub, primary, alts, logoSvg):
        altlinks = "".join(f"<li>{a}</li>" for a in alts)
        return (f'<article class="dlplat" id="dl-{os_id}" data-os="{os_id}"'
                + (f' data-arch="{arch}"' if arch else "") + ">"
                + f'<header class="dlplat__head"><h3 class="dlplat__h">{logo(logoSvg)}{title}</h3>'
                + f'<p class="dlplat__sub">{sub}</p></header>'
                + f'<div class="dlplat__body">{primary}'
                + (f'<ul class="dlplat__alts">{altlinks}</ul>' if altlinks else "")
                + "</div>"
                + '<p class="dlplat__reco" data-reco hidden>为你推荐</p>'
                + "</article>")

    # Platform brand marks, embedded directly (they are logos, not the site's
    # monochrome icon set): the four-pane Windows flag, Apple, and Tux.
    win_logo = ('<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">'
                '<path fill="#F25022" d="M0 0h11.4v11.4H0z"/>'
                '<path fill="#7FBA00" d="M12.6 0H24v11.4H12.6z"/>'
                '<path fill="#00A4EF" d="M0 12.6h11.4V24H0z"/>'
                '<path fill="#FFB900" d="M12.6 12.6H24V24H12.6z"/></svg>')
    mac_logo = ('<svg viewBox="0 0 24 24" fill="#333" xmlns="http://www.w3.org/2000/svg">'
                '<path d="M12.152 6.896c-.948 0-2.415-1.078-3.96-1.04-2.04.027-3.91 1.183-4.961 '
                '3.014-2.117 3.675-.546 9.103 1.519 12.09 1.013 1.454 2.208 3.09 3.792 3.039 '
                '1.52-.065 2.09-.987 3.935-.987 1.831 0 2.35.987 3.96.948 1.637-.026 2.676-1.48 '
                '3.676-2.948 1.156-1.688 1.636-3.325 1.662-3.415-.039-.013-3.182-1.221-3.22-4.857 '
                '-.026-3.04 2.48-4.494 2.597-4.559-1.429-2.09-3.623-2.324-4.39-2.376-2-.156-3.675 '
                '1.09-4.61 1.09zM15.53 3.83c.843-1.012 1.4-2.427 1.245-3.83-1.207.052-2.662.805-3.532 '
                '1.818-.78.896-1.454 2.338-1.273 3.714 1.338.104 2.715-.688 3.559-1.701"/></svg>')
    lin_logo = ('<svg viewBox="0 0 24 24" fill="#333" xmlns="http://www.w3.org/2000/svg">'
                '<path d="M12.504 0c-.155 0-.315.008-.48.021-4.226.333-3.105 4.807-3.17 6.298-.076 '
                '1.092-.3 1.953-1.05 3.02-.885 1.051-2.127 2.75-2.716 4.521-.278.832-.41 1.684-.287 '
                '2.489a.424.424 0 00-.11.135c-.26.268-.45.6-.663.839-.199.199-.485.267-.797.4-.313.136-.658.269-.864.68-.09.189-.136.394-.132.602 0 .199.027.4.055.536.058.399.116.728.04.97-.249.68-.28 1.145-.106 1.484.174.334.535.47.94.601.81.2 1.91.135 2.774.6.926.466 1.866.67 2.616.47.526-.116.97-.464 1.208-.946.587-.003 1.23-.269 2.26-.334.699-.058 1.574.267 2.577.2.025.134.063.198.114.333l.003.003c.391.778 1.113 1.132 1.884 1.071.771-.06 1.592-.536 2.257-1.306.631-.765 1.683-1.084 2.378-1.503.348-.199.629-.469.649-.853.023-.4-.2-.811-.714-1.376v-.097l-.003-.003c-.17-.2-.25-.535-.338-.926-.085-.401-.182-.786-.492-1.046h-.003c-.059-.054-.123-.067-.188-.135a.357.357 0 00-.19-.064c.431-1.278.264-2.55-.173-3.694-.533-1.41-1.465-2.638-2.175-3.483-.796-1.005-1.576-1.957-1.56-3.368.026-2.152.236-6.133-3.544-6.139zm.529 3.405h.013c.213 0 .396.062.584.198.19.135.33.332.438.533.105.259.158.459.166.724 0-.02.006-.04.006-.06v.105a.086.086 0 01-.004-.021l-.004-.024a1.807 1.807 0 01-.15.706.953.953 0 01-.213.335.71.71 0 00-.088-.042c-.104-.045-.198-.064-.284-.133a1.312 1.312 0 00-.22-.066c.05-.06.146-.133.183-.198.053-.128.082-.264.088-.402v-.02a1.21 1.21 0 00-.061-.4c-.045-.134-.101-.2-.183-.333-.084-.066-.167-.132-.267-.132h-.016c-.093 0-.176.03-.262.132a.8.8 0 00-.205.334 1.18 1.18 0 00-.09.4v.019c.002.089.008.179.02.267-.193-.067-.438-.135-.607-.202a1.635 1.635 0 01-.018-.2v-.02a1.772 1.772 0 01.15-.768c.082-.22.232-.406.43-.533a.985.985 0 01.594-.2zm-2.962.059h.036c.142 0 .27.048.399.135.146.129.264.288.344.465.09.199.14.4.153.667v.004c.007.134.006.2-.002.266v.08c-.03.007-.056.018-.083.024-.152.055-.274.135-.393.2.012-.09.013-.18.003-.267v-.015c-.012-.133-.04-.2-.082-.333a.613.613 0 00-.166-.267.248.248 0 00-.183-.064h-.021c-.071.006-.13.04-.186.132a.552.552 0 00-.12.27.944.944 0 00-.023.33v.015c.012.135.037.2.08.334.046.134.098.2.166.268.01.009.02.018.034.024-.07.057-.117.07-.176.136a.304.304 0 01-.131.068 2.62 2.62 0 01-.275-.402 1.772 1.772 0 01-.155-.667 1.759 1.759 0 01.08-.668 1.43 1.43 0 01.283-.535c.128-.133.26-.2.418-.2zm1.37 1.706c.332 0 .733.065 1.216.399.293.2.523.269 1.052.468h.003c.255.136.405.266.478.399v-.131a.571.571 0 01.016.47c-.123.31-.516.643-1.063.842v.002c-.268.135-.501.333-.775.465-.276.135-.588.292-1.012.267a1.139 1.139 0 01-.448-.067 3.566 3.566 0 01-.322-.198c-.195-.135-.363-.332-.612-.465v-.005h-.005c-.4-.246-.616-.512-.686-.71-.07-.268-.005-.47.193-.6.224-.135.38-.271.483-.336.104-.074.143-.102.176-.131h.002v-.003c.169-.202.436-.47.839-.601.139-.036.294-.065.466-.065zm2.8 2.142c.358 1.417 1.196 3.475 1.735 4.473.286.534.855 1.659 1.102 3.024.156-.005.33.018.513.064.646-1.671-.546-3.467-1.089-3.966-.22-.2-.232-.335-.123-.335.59.534 1.365 1.572 1.646 2.757.13.535.16 1.104.021 1.67.067.028.135.06.205.067 1.032.534 1.413.938 1.23 1.537v-.043c-.06-.003-.12 0-.18 0h-.016c.151-.467-.182-.825-1.065-1.224-.915-.4-1.646-.336-1.77.465-.008.043-.013.066-.018.135-.068.023-.139.053-.209.064-.43.268-.662.669-.793 1.187-.13.533-.17 1.156-.205 1.869v.003c-.02.334-.17.838-.319 1.35-1.5 1.072-3.58 1.538-5.348.334a2.645 2.645 0 00-.402-.533 1.45 1.45 0 00-.275-.333c.182 0 .338-.03.465-.067a.615.615 0 00.314-.334c.108-.267 0-.697-.345-1.163-.345-.467-.931-.995-1.788-1.521-.63-.4-.986-.87-1.15-1.396-.165-.534-.143-1.085-.015-1.645.245-1.07.873-2.11 1.274-2.763.107-.065.037.135-.408.974-.396.751-1.14 2.497-.122 3.854a8.123 8.123 0 01.647-2.876c.564-1.278 1.743-3.504 1.836-5.268.048.036.217.135.289.202.218.133.38.333.59.465.21.201.477.335.876.335.039.003.075.006.11.006.412 0 .73-.134.997-.268.29-.134.52-.334.74-.4h.005c.467-.135.835-.402 1.044-.7zm2.185 8.958c.037.6.343 1.245.882 1.377.588.134 1.434-.333 1.791-.765l.211-.01c.315-.007.577.01.847.268l.003.003c.208.199.305.53.391.876.085.4.154.78.409 1.066.486.527.645.906.636 1.14l.003-.007v.018l-.003-.012c-.015.262-.185.396-.498.595-.63.401-1.746.712-2.457 1.57-.618.737-1.37 1.14-2.036 1.191-.664.053-1.237-.2-1.574-.898l-.005-.003c-.21-.4-.12-1.025.056-1.69.176-.668.428-1.344.463-1.897.037-.714.076-1.335.195-1.814.12-.465.308-.797.641-.984l.045-.022zm-10.814.049h.01c.053 0 .105.005.157.014.376.055.706.333 1.023.752l.91 1.664.003.003c.243.533.754 1.064 1.189 1.637.434.598.77 1.131.729 1.57v.006c-.057.744-.48 1.148-1.125 1.294-.645.135-1.52.002-2.395-.464-.968-.536-2.118-.469-2.857-.602-.369-.066-.61-.2-.723-.4-.11-.2-.113-.602.123-1.23v-.004l.002-.003c.117-.334.03-.752-.027-1.118-.055-.401-.083-.71.043-.94.16-.334.396-.4.69-.533.294-.135.64-.202.915-.47h.002v-.002c.256-.268.445-.601.668-.838.19-.201.38-.336.663-.336zm7.159-9.074c-.435.201-.945.535-1.488.535-.542 0-.97-.267-1.28-.466-.154-.134-.28-.268-.373-.335-.164-.134-.144-.333-.074-.333.109.016.129.134.199.2.096.066.215.2.36.333.292.2.68.467 1.167.467.485 0 1.053-.267 1.398-.466.195-.135.445-.334.648-.467.156-.136.149-.267.279-.267.128.016.034.134-.147.332a8.097 8.097 0 01-.69.468zm-1.082-1.583V5.64c-.006-.02.013-.042.029-.05.074-.043.18-.027.26.004.063 0 .16.067.15.135-.006.049-.085.066-.135.066-.055 0-.092-.043-.141-.068-.052-.018-.146-.008-.163-.065zm-.551 0c-.02.058-.113.049-.166.066-.047.025-.086.068-.14.068-.05 0-.13-.02-.136-.068-.01-.066.088-.133.15-.133.08-.031.184-.047.259-.005.019.009.036.03.03.05v.02h.003z"/></svg>')

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
        card("windows", "x64", "Windows", "x64 · 安装包与绿色包都内置自动更新", win_ins, [win_zip], win_logo),
        card("macos", "arm64", "macOS · Apple 芯片", "arm64 · 内置视频播放与 Live2D", mac_dmg_a, mac_alt_a, mac_logo),
        card("macos", "x64", "macOS · Intel", "x64 · 视频回退封面，Live2D 未实机验证", mac_dmg_x, mac_alt_x, mac_logo),
        card("linux", "x64", "Linux", "x64 · 视频需系统 libmpv，Live2D 未实机验证", lin_tar, lin_alts, lin_logo),
    ]
    return ('<div class="dlplat__grid" id="dl-grid">'
            + "".join(cards) + "</div>")


def changelog_nav():
    """Render the multi-level version nav for the 更新日志 section.

    Tree: major.minor group (collapsible <details>) -> minor subheading -> patch
    version links, each linking to the `id="rel-<ver>"` anchor that
    tools/changelog.py to_html() emits. Every minor version gets its own level:
    a minor that is itself a release (1.3.7) is a clickable subheading with its
    4-part patches beneath; one that is not (1.3.8) is a plain label. The newest
    group is expanded by default. Without JS a link is a plain in-page jump;
    site.js opens the target entry and the group under the reading position.
    """
    from collections import OrderedDict

    sections = cl.parse(read(os.path.join(REPO, "CHANGELOG.md")))
    releases = {v for v, _d, _body in sections}
    groups = OrderedDict()          # "1.3" -> OrderedDict "1.3.8" -> [versions]
    for v, _d, _body in sections:
        parts = v.split(".")
        key = ".".join(parts[:2])
        sub = ".".join(parts[:3])
        groups.setdefault(key, OrderedDict()).setdefault(sub, []).append(v)

    def link(v):
        anchor = "rel-" + re.sub(r"[^A-Za-z0-9._-]", "-", v)
        return (f'<a class="clnav__link" href="#{anchor}">'
                f'<span class="mono">{_html.escape(v)}</span></a>')

    def sublink(v):
        anchor = "rel-" + re.sub(r"[^A-Za-z0-9._-]", "-", v)
        return (f'<a class="clnav__sublink" href="#{anchor}">'
                f'<span class="mono">{_html.escape(v)}</span></a>')

    def subblock(sub, vers):
        """One minor level: subheading (link when it is a release) + patches."""
        if sub in releases:
            head = sublink(sub)
            rest = [v for v in vers if v != sub]
        else:
            head = f'<h4 class="clnav__sub">{_html.escape(sub)}</h4>'
            rest = vers
        return (f'<div class="clnav__subgrp">{head}'
                + "".join(link(v) for v in rest) + "</div>")

    out = []
    for gi, (gkey, subs) in enumerate(groups.items()):
        inner = "".join(subblock(s, vs) for s, vs in subs.items())
        count = sum(len(vs) for vs in subs.values())
        open_attr = " open" if gi == 0 else ""
        out.append(
            f'<details class="clnav__grp"{open_attr} data-grp="{gkey}">'
            f'<summary class="clnav__grphead">'
            f'<span class="clnav__gkey mono">{_html.escape(gkey)}</span>'
            f'<span class="clnav__gcount">{count} 个版本</span></summary>'
            f'<div class="clnav__grpbody">{inner}</div></details>')
    return ('<nav class="clnav" aria-label="更新日志版本目录">'
            '<div class="clnav__scroll">' + "".join(out) + "</div></nav>")


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
