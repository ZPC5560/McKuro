#!/usr/bin/env python3
"""CHANGELOG.md 是 McKuro 发布说明的**唯一数据源**。

本工具负责让三个消费端始终同源:
  * GitHub Release 正文  -- CI 在 tag 发布时用 `extract` 取本版小节并写入 Release
  * 官网「更新日志」区块  -- website-src/build.py import 本模块的 to_html() 在构建时注入
  * 仓库首页/人工阅读     -- 就是 CHANGELOG.md 本身

小节契约(改格式先改这里):
  ## [v1.4.0] - 2026-10-04        <- 版本锚点行,`^## [vX.Y.Z] - YYYY-MM-DD`
  ### Added / Changed / Fixed ...   <- 分组标题(也允许中文组名)
  - 条目文本(允许 **粗体**、`代码`、[链接](url)、行内 HTML 不允许)

双语契约(1.3.8.1 起,见 CHANGELOG.md 顶部):
  中文正文照上面的契约写在小节开头;英文正文整块放在
  `<!--en-->` 与 `<!--/en-->` 两个标记之间,自带一套 `### Added/...` 分组。
  文件顶部用 `<!--bilingual-from: X.Y.Z-->` 声明「从哪一版开始必须双语」:
  check 对所有 >= 该版本的版本强制要求非空英文块,更旧的版本允许只有中文
  (但一旦补了英文块,它也会被校验完整性)。没有该声明时按「最新版本必须双语」。

用法:
  python tools/changelog.py extract <version> [-o out.md] [--lang zh|en]
      # 取某版正文(不含版本标题行);默认 --lang zh,行为与历史上完全一致
      # --lang en 取英文正文,英文缺失时回退中文并在 stderr 告警(不报错)
  python tools/changelog.py has-lang <version> <lang>
      # 该版本是否有该语言正文(有=0,无=1,版本不存在=2);CI 用它决定是否拼双语
  python tools/changelog.py versions                        # 列出全部版本号(新→旧)
  python tools/changelog.py check                            # 校验结构 + 双语,失败退出码 1
"""
import argparse
import html as _html
import os
import re
import sys

SECTION_RE = re.compile(r"^## \[(v?[0-9][^\]]*)\](?:\s*-\s*(\S.*?))?\s*$")
GROUP_RE = re.compile(r"^###\s+(.+)$")

# 双语块的边界标记:各占一行。大小写不敏感,方便手写。
EN_OPEN_RE = re.compile(r"^<!--\s*en\s*-->$", re.IGNORECASE)
EN_CLOSE_RE = re.compile(r"^<!--\s*/en\s*-->$", re.IGNORECASE)
# 「从哪一版开始必须双语」的声明(写在文件顶部,任何 `## [` 小节之前)。
# 必须独占一行,正文里提到它不算声明。
BILINGUAL_FROM_LINE_RE = re.compile(
    r"^<!--\s*bilingual-from\s*:\s*v?([0-9][0-9.]*)\s*-->\s*$", re.IGNORECASE)

# 分组英文名 → 中文显示名(站点中文渲染用;GitHub 侧保持原文)
GROUP_LABELS = {
    "added": "新增",
    "changed": "变更",
    "deprecated": "渐弃",
    "removed": "移除",
    "fixed": "修复",
    "security": "安全",
}

LANGS = ("zh", "en")


def repo_root():
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def default_path():
    return os.path.join(repo_root(), "CHANGELOG.md")


def split_sections(text):
    """返回 [(version, date, body_lines)];按文件顺序(约定新→旧)。

    围栏代码块(```)里的内容不算小节:文件顶部的双语写法说明就用一个 markdown 围栏
    展示模板,里面的 `## [1.3.8.1]` 示例不能被当成真的版本小节。
    """
    out = []
    cur = None
    fenced = False
    for line in text.splitlines():
        if line.lstrip().startswith("```"):
            fenced = not fenced
            if cur is not None:
                cur[2].append(line)
            continue
        if not fenced:
            m = SECTION_RE.match(line)
            if m:
                if cur is not None:
                    out.append(cur)
                cur = (m.group(1), (m.group(2) or "").strip(), [])
                continue
        if cur is not None:
            cur[2].append(line)
    if cur is not None:
        out.append(cur)
    return out


def parse(text):
    """返回 [(version, date, body)];body 已去首尾空行。"""
    return [(v, d, "\n".join(lines).strip()) for (v, d, lines) in split_sections(text)]


def split_zh_en(body):
    """把一小节正文拆成 (中文正文, 英文正文, 是否有英文标记, 错误列表)。

    没有 `<!--en-->` 标记时「是否有英文标记」为 False,英文正文为空串,
    中文正文就是整块(与历史行为逐字节一致)。
    """
    lines = body.splitlines()
    open_at = None
    close_at = None
    errs = []
    for i, line in enumerate(lines):
        s = line.strip()
        if open_at is None:
            if EN_OPEN_RE.match(s):
                open_at = i
            elif EN_CLOSE_RE.match(s):
                errs.append("`<!--/en-->` 之前没有对应的 `<!--en-->`")
            continue
        if close_at is not None:
            if EN_OPEN_RE.match(s):
                errs.append("`<!--en-->` 出现了第二次(每节只能有一个英文块)")
            continue
        if EN_CLOSE_RE.match(s):
            close_at = i
        elif EN_OPEN_RE.match(s):
            errs.append("`<!--en-->` 出现了第二次(每节只能有一个英文块)")
    if open_at is None:
        return body.strip(), "", False, errs
    if close_at is None:
        errs.append("英文块缺少结尾标记 `<!--/en-->`")
    zh = "\n".join(lines[:open_at]).strip()
    end = close_at if close_at is not None else len(lines)
    en = "\n".join(lines[open_at + 1:end]).strip()
    return zh, en, True, errs


def parse_bilingual(text):
    """返回 [(version, date, zh_body, en_body)];没有英文块时 en_body 为空串。"""
    out = []
    for v, d, body in parse(text):
        zh, en, _has, _errs = split_zh_en(body)
        out.append((v, d, zh, en))
    return out


def _find(text, version):
    """按版本号找 (version, date, zh, en);版本号前导 v 可有可无。找不到返回 None。"""
    want = version.lstrip("v")
    for v, d, zh, en in parse_bilingual(text):
        if v.lstrip("v") == want:
            return v, d, zh, en
    return None


def extract(text, version, lang="zh"):
    """取指定版本正文;找不到返回 None。版本号前导 v 可有可无(与 tag 名互容)。

    lang="zh"(默认)与历史行为完全一致;lang="en" 取英文块,缺失时回退中文并告警。
    """
    hit = _find(text, version)
    if hit is None:
        return None
    v, _d, zh, en = hit
    if lang == "zh":
        return zh
    if en:
        return en
    print(f"warning: {v} 没有英文正文(--lang {lang}),已回退中文", file=sys.stderr)
    return zh


def has_lang(text, version, lang):
    """该版本是否真的有该语言正文。版本不存在返回 None。"""
    hit = _find(text, version)
    if hit is None:
        return None
    _v, _d, zh, en = hit
    return bool(zh if lang == "zh" else en)


def bilingual_floor(text):
    """该文件的「必须双语」起始版本号;没有声明时退化为最新版本号。

    返回 (版本号, 是否显式声明);没有小节时返回 (None, False)。
    """
    sections = split_sections(text)
    declared = bilingual_from(text)
    if declared is not None:
        return declared, True
    if sections:
        return sections[0][0].lstrip("v"), False
    return None, False


def bilingual_from(text):
    """文件顶部声明的「必须双语」起始版本号;没声明返回 None。

    声明必须**独占一行**(`<!--bilingual-from: 1.3.8.1-->`),这样正文里顺口提到这条
    注释、或在说明段落里贴出它,都不会被当成真的声明。
    """
    # 只看第一个真的 `## [` 小节之前的部分(围栏代码块里的示例不算),避免版本正文
    # 里引用这条注释被误读。
    fenced = False
    for line in text.splitlines():
        if line.lstrip().startswith("```"):
            fenced = not fenced
            continue
        if not fenced and SECTION_RE.match(line):
            break
        if fenced:
            continue
        m = BILINGUAL_FROM_LINE_RE.match(line.strip())
        if m:
            return m.group(1)
    return None


def _inline_md(s):
    """markdown 行内 → HTML(先整体转义,再还原受支持的标记,杜绝注入)。"""
    esc = _html.escape(s, quote=False)
    esc = re.sub(r"\[([^\]]+)\]\((https?://[^)\s]+)\)",
                 r'<a href="\2" target="_blank" rel="noopener">\1</a>', esc)
    esc = re.sub(r"`([^`]+)`", r"<code>\1</code>", esc)
    esc = re.sub(r"\*\*([^*]+)\*\*", r"<b>\1</b>", esc)
    # 版本正文里的 commit 短链等尾部括号内容原样保留
    return esc


def _group_label(title):
    key = title.strip().lower()
    return GROUP_LABELS.get(key, title.strip())


def _group_items(body):
    """正文 → [(title, [items])],保持出现顺序;中文/英文正文共用同一套规则。"""
    groups = []  # [(title, [items])]
    current = None
    for line in body.splitlines():
        gm = GROUP_RE.match(line)
        if gm:
            current = (gm.group(1).strip(), [])
            groups.append(current)
            continue
        st = line.strip()
        if not st or st.startswith("<!--") or st.startswith("---"):
            continue
        if st.startswith("- ") or st.startswith("* "):
            if current is None:
                current = ("", [])
                groups.append(current)
            current[1].append(st[2:].strip())
        elif current is not None and current[0] == "" and current[1] == []:
            # 组前说明行:作为引言条目
            current[1].append(st)
    return groups


def _render_groups(groups, lang="zh"):
    """[(title, items)] → HTML 片段。中文渲染时把 Added/Fixed 译成中文组名。"""
    inner = []
    for title, items in groups:
        if not items:
            continue
        if not title:
            label = ""
        elif lang == "zh":
            label = _group_label(title)
        else:
            label = title
        if label:
            inner.append(f'<h4 class="cl__group">{_html.escape(label)}</h4>')
        inner.append('<ul class="cl__list">'
                     + "".join(f"<li>{_inline_md(it)}</li>" for it in items)
                     + "</ul>")
    return inner


def to_html(text, open_recent=2, lang="zh", with_en=False):
    """渲染成站点的 <details> 时间线(最新 open_recent 版默认展开)。

    lang="zh"(默认)渲染中文正文,lang="en" 渲染英文正文(缺失回退中文)。
    with_en=True 时额外把英文正文也渲染进同一个 <details>(包在 `.cl__zh` / `.cl__en`
    里)—— 官网用它:有 JS 时右上角切换语言,没 JS 时每个英文块自己是一个可展开的
    `<details>`,所以两种状态下英文都读得到。with_en=False 时输出与历史版本逐字节一致。

    每个小节带 `id="rel-<版本>"` 锚点:官网「更新日志」上方的版本导航栏
    (build.py 生成)靠它做页内跳转;id 对版本号做最小转义,保证永远是合法锚点。
    """
    parts = []
    for idx, (v, d, zh, en) in enumerate(parse_bilingual(text)):
        anchor = "rel-" + re.sub(r"[^A-Za-z0-9._-]", "-", v)
        primary_en = lang == "en" and bool(en)
        head = (f'<summary><span class="cl__ver mono">{_html.escape(v)}</span>'
                f'<span class="cl__date mono">{_html.escape(d)}</span></summary>')
        open_attr = " open" if idx < open_recent else ""
        has_en = ' data-has-en="1"' if en else ""
        if not with_en:
            body = en if primary_en else zh
            inner = _render_groups(_group_items(body), "en" if primary_en else "zh")
        elif primary_en:
            inner = [f'<div class="cl__en__body" lang="en">'
                     + "".join(_render_groups(_group_items(en), "en")) + "</div>"]
        else:
            inner = [f'<div class="cl__zh">'
                     + "".join(_render_groups(_group_items(zh), "zh")) + "</div>"]
            if en:
                inner.append(
                    '<details class="cl__en" lang="en">'
                    '<summary class="cl__en__sum">English</summary>'
                    '<div class="cl__en__body">'
                    + "".join(_render_groups(_group_items(en), "en"))
                    + "</div></details>")
        parts.append(f'<details class="cl" id="{anchor}"{open_attr} data-ver="{_html.escape(v)}"{has_en}>'
                     + head + f'<div class="cl__body">{"".join(inner)}</div></details>')
    return "\n".join(parts)


def _ver_key(s):
    return tuple(int(x) for x in re.findall(r"\d+", s))


def _en_block_errors(en):
    """英文块自身的完整性:必须有分组标题、必须有条目、条目必须真的是英文。

    不禁止英文块里出现中文 —— 本仓的英文条目经常引用中文界面文案(如
    "the 「预下载」 button"),禁掉只会逼人写错。只要求条目里至少有一个英文单词,
    用来挡住「把中文原文整块贴进英文块」这种最常见的漏译。
    """
    errs = []
    groups = _group_items(en)
    if not any(title for title, _items in groups):
        errs.append("英文块必须用 `### Added` 这类分组标题组织")
    items = [it for _title, its in groups for it in its]
    if not items:
        errs.append("英文块里没有任何 `- ` 条目")
    elif not re.search(r"[A-Za-z]{2,}", " ".join(items)):
        errs.append("英文块里没有任何英文单词(是不是把中文原文贴进去了?)")
    return errs


def check(text):
    """结构 + 双语校验;返回错误列表(空 = 通过)。"""
    errs = []
    raw = split_sections(text)
    if not raw:
        errs.append("没有任何 `## [vX.Y.Z]` 小节")
    floor, declared = bilingual_floor(text)
    floor_key = _ver_key(floor) if floor else None
    seen = set()
    for v, d, lines in raw:
        zh, en, has_marker, serrs = split_zh_en("\n".join(lines).strip())
        errs.extend(f"{v}: {e}" for e in serrs)
        if v in seen:
            errs.append(f"版本 {v} 重复")
        seen.add(v)
        if not d:
            errs.append(f"{v} 缺日期(`## [{v}] - YYYY-MM-DD`)")
        if not zh:
            errs.append(f"{v} 正文为空")
        if has_marker and not en:
            # 有人开了英文块却没写内容:历史版本也照样报错,半个双语比单语更糟
            errs.append(f"{v}: 英文块是空的(`<!--en-->` 与 `<!--/en-->` 之间没有内容)")
        elif en:
            errs.extend(f"{v}: {e}" for e in _en_block_errors(en))
        elif floor_key is not None and _ver_key(v) >= floor_key:
            errs.append(
                f"{v} 缺英文正文:自 {floor} 起每个版本都必须中英双语"
                "(英文块用 `<!--en-->` … `<!--/en-->` 包住)")
    # 版本号应从新到旧(semver 比较,允许 4 段)
    vers = [v for v, _d, _lines in raw]
    for a, b in zip(vers, vers[1:]):
        if _ver_key(a) < _ver_key(b):
            errs.append(f"顺序错误:{a} 在 {b} 之前(应新→旧)")
    return errs


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--file", default=default_path(), help="CHANGELOG 路径(默认仓库根 CHANGELOG.md)")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p_ex = sub.add_parser("extract", help="输出指定版本正文")
    p_ex.add_argument("version")
    p_ex.add_argument("-o", "--out", help="写入文件(默认 stdout)")
    p_ex.add_argument("--lang", default="zh", choices=LANGS,
                      help="zh(默认,中文正文)或 en(英文正文,缺失回退中文)")

    p_hl = sub.add_parser("has-lang", help="该版本是否有该语言正文(退出码 0/1,版本不存在 2)")
    p_hl.add_argument("version")
    p_hl.add_argument("lang", nargs="?", default="en", choices=LANGS)

    sub.add_parser("versions", help="列出版本号(新→旧)")
    sub.add_parser("check", help="校验结构 + 双语")

    args = ap.parse_args()
    with open(args.file, encoding="utf-8") as f:
        text = f.read()

    if args.cmd == "extract":
        body = extract(text, args.version, lang=args.lang)
        if body is None:
            print(f"not found: {args.version}", file=sys.stderr)
            sys.exit(1)
        if args.out:
            with open(args.out, "w", encoding="utf-8", newline="\n") as _f:
                _f.write(body + "\n")
        else:
            print(body)
    elif args.cmd == "has-lang":
        got = has_lang(text, args.version, args.lang)
        if got is None:
            print(f"not found: {args.version}", file=sys.stderr)
            sys.exit(2)
        sys.exit(0 if got else 1)
    elif args.cmd == "versions":
        for v, _d, _b in parse(text):
            print(v)
    elif args.cmd == "check":
        errs = check(text)
        for e in errs:
            print("error: " + e, file=sys.stderr)
        sys.exit(1 if errs else 0)


if __name__ == "__main__":
    main()
