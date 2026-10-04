#!/usr/bin/env python3
"""CHANGELOG.md 是 McKuro 发布说明的**唯一数据源**。

本工具负责让三个消费端始终同源:
  * GitHub Release 正文  -- CI 在 tag 发布时 `extract <版本>` 生成本版小节并写入 Release
  * 官网「更新日志」区块  -- website-src/build.py import 本模块的 to_html() 在构建时注入
  * 仓库首页/人工阅读     -- 就是 CHANGELOG.md 本身

小节契约(改格式先改这里):
  ## [v1.4.0] - 2026-10-04        <- 版本锚点行,`^## [vX.Y.Z] - YYYY-MM-DD`
  ### Added / Changed / Fixed ...   <- 分组标题
  - 条目文本(允许 **粗体**、`代码`、[链接](url)、行内 HTML 不允许)

用法:
  python tools/changelog.py extract <version> [-o out.md]   # 取某版正文(不含版本标题行)
  python tools/changelog.py versions                        # 列出全部版本号(新→旧)
  python tools/changelog.py check                            # 校验结构契约,失败退出码 1
"""
import argparse
import html as _html
import os
import re
import sys

SECTION_RE = re.compile(r"^## \[(v?[0-9][^\]]*)\](?:\s*-\s*(\S.*?))?\s*$")
GROUP_RE = re.compile(r"^###\s+(.+)$")

# 分组英文名 → 中文显示名(站点渲染用;GitHub 侧保持原文)
GROUP_LABELS = {
    "added": "新增",
    "changed": "变更",
    "deprecated": "渐弃",
    "removed": "移除",
    "fixed": "修复",
    "security": "安全",
}


def repo_root():
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def default_path():
    return os.path.join(repo_root(), "CHANGELOG.md")


def split_sections(text):
    """返回 [(version, date, body_lines)];按文件顺序(约定新→旧)。"""
    out = []
    cur = None
    for line in text.splitlines():
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


def extract(text, version):
    """取指定版本的正文;找不到返回 None。版本号前导 v 可有可无(与 tag 名互容)。"""
    want = version.lstrip("v")
    for v, _d, body in parse(text):
        if v.lstrip("v") == want:
            return body
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


def to_html(text, open_recent=2):
    """渲染成站点的 <details> 时间线(最新 open_recent 版默认展开)。"""
    parts = []
    for idx, (v, d, body) in enumerate(parse(text)):
        groups = []  # [(title, [items])] 保持出现顺序
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
        head = (f'<summary><span class="cl__ver mono">{_html.escape(v)}</span>'
                f'<span class="cl__date mono">{_html.escape(d)}</span></summary>')
        inner = []
        for title, items in groups:
            if not items:
                continue
            label = _group_label(title) if title else ""
            if label:
                inner.append(f'<h4 class="cl__group">{_html.escape(label)}</h4>')
            inner.append('<ul class="cl__list">'
                         + "".join(f"<li>{_inline_md(it)}</li>" for it in items)
                         + "</ul>")
        open_attr = " open" if idx < open_recent else ""
        parts.append(f'<details class="cl"{open_attr} data-ver="{_html.escape(v)}">'
                     + head + f'<div class="cl__body">{"".join(inner)}</div></details>')
    return "\n".join(parts)


def check(text):
    """结构校验;返回错误列表(空 = 通过)。"""
    errs = []
    sections = parse(text)
    if not sections:
        errs.append("没有任何 `## [vX.Y.Z]` 小节")
    seen = set()
    for v, d, body in sections:
        if v in seen:
            errs.append(f"版本 {v} 重复")
        seen.add(v)
        if not d:
            errs.append(f"{v} 缺日期(`## [{v}] - YYYY-MM-DD`)")
        if not body:
            errs.append(f"{v} 正文为空")
    # 版本号应从新到旧(semver 比较,允许 4 段)
    def key(s):
        return tuple(int(x) for x in re.findall(r"\d+", s))
    vers = [v for v, _d, _b in sections]
    for a, b in zip(vers, vers[1:]):
        if key(a) < key(b):
            errs.append(f"顺序错误:{a} 在 {b} 之前(应新→旧)")
    return errs


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--file", default=default_path(), help="CHANGELOG 路径(默认仓库根 CHANGELOG.md)")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p_ex = sub.add_parser("extract", help="输出指定版本正文")
    p_ex.add_argument("version")
    p_ex.add_argument("-o", "--out", help="写入文件(默认 stdout)")

    sub.add_parser("versions", help="列出版本号(新→旧)")
    sub.add_parser("check", help="校验结构")

    args = ap.parse_args()
    with open(args.file, encoding="utf-8") as f:
        text = f.read()

    if args.cmd == "extract":
        body = extract(text, args.version)
        if body is None:
            print(f"not found: {args.version}", file=sys.stderr)
            sys.exit(1)
        if args.out:
            with open(args.out, "w", encoding="utf-8", newline="\n") as _f:
                _f.write(body + "\n")
        else:
            print(body)
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
