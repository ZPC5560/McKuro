# tools/ — 首页舞蹈动画管线（已从首屏撤下，管线保留供参考）

> **状态**：首屏右侧现在由「心」的双形态 Live2D 角色占据（`hero-figure.js` +
> `website-src/live2d/`），原先的 3D 跳舞模型不再出现在页面上。本目录保留这条
> 管线与它的产物说明，供将来重新使用；`website-src/models/xin-yuehu.glb` 仍是
> 它的输入模型。

官网首页心月狐模型(`models/xin-yuehu.glb`)里烘焙了一段 27.5s 循环舞蹈,
由 MMD 动作 `玉兰开花三月三_动作.vmd`(bilibili 鲸落璃沙郊,目标模型
"Firefly R18")重定向到模型的 Biped 骨架生成。本目录是可复现管线。

## 链路

```
FBX(带 506 骨蒙皮) ──┐
                      ├→ s5.glb(带骨架 GLB,quantize+webp,3.9MB)
原始 PNG 纹理 ────────┘
VMD ──→ retarget.js ──→ dance-tracks.json(50 条四元数轨 + 1 条 vec3 轨 __root_position,26.7k 关键帧)
s5.glb + dance-tracks.json ──→ inject-dance.js ──→ models/xin-yuehu.glb(4.1MB,含动画)
```

转换与优化依赖 `.site-build`(FBXLoader/GLTFExporter 跑在浏览器里,
gltf-transform/sharp 跑在 Node),脚本见 `E:\donet\.site-build\gltf\`:
`convert-skinned.html`(FBX→蒙皮 GLB)、`restore-textures.js`(回写原始 PNG,
避免 canvas 预乘 alpha 弄黑头发)、`weld/simplify/webp`(跳过 quantize——
它会破坏蒙皮 JOINTS_0)。

## 各脚本

- `vmd.js` — VMD 二进制解析(Shift-JIS 骨名,全角数字归一化)+ 四元数工具。
- `analyze-center.js` — 分析 センター 位移、搜索姿态匹配的循环接缝
  (当前选段 645→1470 帧)。注意:早期版本的本脚本插值分支返回常量单位四元数,
  姿态距离的旋转项恒为 0,该选段实际只由位置项决定;已改用 `vmd.js` 的
  `sampleTrack`(含 slerp)修复,接缝的具体姿态差需重跑本脚本验证后再引用。
- `retarget.js` — 核心:VMD→Biped 重定向。命令行
  `node retarget.js [model.glb] [segA] [segB] [out.json] [vmd]`(各项均有默认值)。
  启动期集中断言关键骨轨(センター/腰/下半身/四肢主干)与 GLB 节点齐备,
  缺失即报错退出;手指等可选骨轨缺失仅提示 NOTE。公式
  `Rloc(b) = Rw_bind(parent)⁻¹ · C·q_vmd(b)·C⁻¹ · Rw_bind(b)`,
  C = mirror-Z(位置 (x,y,-z),四元数 (-x,-y,z,w)):MMD 前向 -Z 对上模型前向
  +Z 并翻转手性。腰→Bip001Pelvis,下半身折叠进大腿链;MMD 单位换算 S 与落地方
  偏移 dy 由"重心高度↔腿部 FK 落地"的 Theil-Sen 回归自动标定(S≈0.074)。
  センター xz 慢漂移被扣除,使舞蹈原位循环。
- `inject-dance.js` — 把动画作为 glTF animation 注入 GLB
  (在 `.site-build` 下运行,需 @gltf-transform/core)。

## 已知取舍

- 表情/口型不动画(VMD 的 31818 条 morph 帧无目标,面部保持静态)。
- 头发/裙摆/尾巴骨骼保持绑定位姿,随身体刚体跟随(无物理)。
- 足 IK 轨被跳过——腿部 FK 是烘焙真值,直接回放即贴合地面
  (实测脚底 -2~3cm,在此显示尺寸下不可见)。
- mp3 与横屏镜头 VMD 未使用(官网不需要自动播放音乐/运镜)。
