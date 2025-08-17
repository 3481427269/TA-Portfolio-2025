# TA-Portfolio-2025
2025年

仿terrian自带刷草工具的Mesh刷草工具 ： 支持Gpu instancing + 顶点着色器风场 + 一键烘培
![Result]("./image/LastResult.gif")

挂载脚本GrassPainV4，设置相关参数点击paingrass即可在编辑模式进行绘制。
![GrassTool]("./image/GrassTool.gif")

点击烘焙可以将数据烘培到指定asset中。
![Bake]("./image/Bake.gif")

在挂载了RuntimeGrassRenderer脚本的mesh上，指定GrassDataAsset，调节Height，可以在运行模式下看到asset中的草 + 视锥剔除 + 距离移除 + 顶点着色器单人交互。
![Interaction]("./image/Interaction.gif")

运行结果：
![01 image]("./image/01.png")

性能测试：

处理器：13th Gen Intel(R) Core(TM) i5-13500H (2.60 GHz)；
机带RAM：16.0 GB；
集成显卡：128MB；

5w规模简单模型草（单株顶点数13）帧频稳定在170左右。
20w草帧频在48帧左右。

一些注意事项：
由于风场效果是在特定shader中,基于实例plane大小的世界坐标实现，所以如果场景中看不到实例的风场效果多半是因为Mesh的size太大。（后续考虑将风场效果迁移到computeshader）。
另外不同草地需要使用不同的材质，否则后用草地更改的材质数据会覆盖前者。
交互如果不明显，可以调节RuntimeGrassRenderer中的push radius。
旧工程残留 JobSystem 剔除逻辑，所以需配置相关包才能正常运行，或者可以自行删除RuntimeGrassRenderer脚本中的Job system相关内容。
目前工具一次仅支持使用一种mesh + material进行刷草操作（会改进）。
视锥剔除和交互效果仅仅在RuntimeGrassRenderer脚本中实现。
