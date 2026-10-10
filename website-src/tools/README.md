# website-src 构建说明

`build.py` 把本目录的片段拼成 `../website/`（GitHub Pages 直接发布的那份）。

```
html/*.html 按文件名顺序拼接 → website/index.html
css/*.css   按文件名顺序拼接 → website/site.css
js/*.js     逐个复制          → website/
live2d/     整个目录复制      → website/live2d/   （首屏角色资源，见下）
vendor/     仅 three.module.min.js → website/vendor/
```

用法：`python build.py`（需 `fonttools==4.66.1` 与 `brotli`，见仓库 CI）。
字体源缺失时构建会在写任何输出前中止，避免半成品覆盖可用站点。

## 首屏角色（心 · 双形态）

首屏右侧的角色由 `js/hero-figure.js` 驱动，运行时是 `live2d/puppetloom-web.js`，
资源由 **`tools/optimize-live2d.mjs` 离线生成并入库**——原始展示包（约 14 MB，
含两份运行时副本与未压缩 JSON）不入库。

```bash
# 需要 sharp；在携带它的目录里跑（如 E:\donet\.site-build）
cd <带 sharp 的目录>
node McKuro/website-src/tools/optimize-live2d.mjs <原始展示包目录> McKuro/website-src/live2d
```

优化做的事，以及为什么可以无损：

| 项 | 原始 | 产物 | 说明 |
| --- | --- | --- | --- |
| 运行时 JS | 292 KB × 2 | 292 KB × 1 | 两形态文件 sha256 相同，去重；脚本会断言这一点 |
| `project.json`（每形态） | 4.76 / 5.17 MB | 1.20 / 1.32 MB | 去掉 70% 缩进空白 + 浮点保留 5 位 |
| 纹理（每形态） | 2.02 / 2.05 MB | 0.93 / 0.93 MB | 无损 WebP |

- **浮点 5 位**：坐标是 0–1 归一化值，5 位小数在运行时绘制的 1024px 画布上约
  0.01px，不可见。
- **无损 WebP**：实测与原 PNG 的差异只出现在完全透明的像素上（可见像素差异为 0），
  所以不会在两个形态之间产生发丝抖动。
- GitHub Pages 会对 `.json` 自动 gzip，所以线上每形态约 1.1 MB（实测）。

产物里的 `manifest.json` 记录了两个形态的 revision（r33 / r35）、源指纹与源文件
大小，便于回溯是哪一版素材。
