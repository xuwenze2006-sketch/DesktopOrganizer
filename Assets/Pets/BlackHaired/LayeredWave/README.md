# 黑发伙伴招手源稿

这里保存可重复制作的 RGBA 图层及 `rig.json`。坐标均采用 256 × 256 原画画布。
程序只加载导出的 `wave-layered.png` 与 `manifest.json`，不在运行时计算骨架或变形。

头部、表情、裤腿、下垂手臂和完整动作袖子沿用同一人物原画。补画只用于张开手掌和
躯干遮挡区域；来源详见上级 `CHARACTER-SOURCES.txt`。`actions.png` 是保留的原素材，
`actions-clean.png` 是清理白边后的动作图。图层内的隐藏连接区域已补全。

`sleeve.png` 必须保持整条袖子的连续纹理。肩、肘、腕控制一条有圆滑弯折的曲带，
逆映射到原袖子纹理；内肘压皱只改变现有衣料颜色。不要再拆成旋转的两块袖子或添加
关节圆盖。头与躯干采用刚性侧倾，裤腿采用固定脚底的水平剪切，不按姿势重新缩放体型。
手型在掌面侧转时切换，手腕绑定位置保持一致；脸部表情只覆盖原坐标中的眼、嘴区域。

从项目根目录运行（Python 需要 Pillow、NumPy）：

```powershell
python scripts/pet-animation/test_black_wave.py
python scripts/pet-animation/build_black_wave.py
```

默认只输出到被 Git 忽略的 `artifacts/black-layered-wave`：46 帧 PNG、2.3 秒的浅/深底
GIF、接触表、骨架坐标及图集。图集使用直接 RGBA 写入，避免重复预乘破坏边缘。
完成视觉检查后，用 `--install` 更新招手图集与 manifest；其它动作只改用清理白边的图集。
重新制作 `actions-clean.png` 或补画图层时，可复用脚本的 `clean_rgba_edges`，其余内部
像素应保留。修改后仍须构建和测试，实际桌面试用须遵守项目的运行验证授权规则。

离线检查覆盖肢段控制长度、挥动时身体跟随、连续袖形和连接处透明裂口。WPF 测试覆盖
头身比例、手掌运动突变、首尾站姿、动作排队以及原生/放大/带场景透明渲染。
这些检查不能代替对动作自然程度的观看和判断，也不能证明已经达到手绘动漫质量。
