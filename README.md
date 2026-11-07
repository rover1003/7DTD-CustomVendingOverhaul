# CustomVendingOverhaul for 7 Days to Die V3.4 (b10)

《七日杀》玩家自动售货机全面优化与修复模组（Harmony 纯代码补丁，无任何资源侵入）。

---

## 🌟 解决的原版痛点与核心特性

原版（Vanilla）售货机系统在底层存在严重的机制限制与逻辑死锁，本模组通过 Cecil IL 精确逆向并修复了全部底层通道：

### 1. 解锁无限租赁售货机（Unlimited Renting）
* **原版限制**：原版底层拦截 `checkAlreadyRentingVM`，每个玩家在全世界同一时刻只能租赁 1 台商人售货机。
* **模组优化**：彻底解除该限制，玩家可以在所有商人的售货机上同时租赁，打造自己的跨商人连锁小卖部！

### 2. 激活自购售货机每日自动收购（Bought Vending Machine Auto-Buy）
* **原版缺陷**：原版 `TileEntityVendingMachine.TryAutoBuy` 仅在商人处租赁机（`Rentable == true`）的窗口打开时触发；玩家从商人处辛苦攒钱买回家放置的自购售货机（`Rentable == false`），原版逻辑直接跳过，**在家里放一辈子也永远卖不出去一件货**！
* **模组优化**：完美挂载补丁，当玩家打开自己放置的售货机时，自动补齐原版的每日结算流程，家里售货机正式具备自动卖货能力！

### 3. 每日 100% 稳定收购 3~5 组物品（Guaranteed 100% Daily Sales）
* **原版机制**：原版每次结算有 33% 概率直接判定失败，且要求机器内物品必须大于 5 组才允许收购。
* **模组优化**：
  * 移除失败概率与保底累计器，只要到了第二天打开售货机，**100% 成功结算**；
  * 每次随机收购 **3 至 5 组物品**（若机器内不足 3 组，则全额收购合格商品）；
  * 移除“必须大于 5 组”的苛刻门槛；
  * 智能避开恶意溢价（`Markup > 0`）商品，平价商品快速回款；
  * 公道结算公爵赌场币（Casino Coins），打开售货机直接取钱！

### 4. 每日计时器严格锚定（Anti-Exploit）
* **原版 Bug**：原版使用相对顺延累加，导致玩家连续反复开关售货机界面会疯狂重置重复卖货。
* **模组防刷**：结算后严格将下一次收购时间锚定在 `当前世界时间 + 24000 tick`（整 1 游戏天）。同一天内无论开关多少次界面绝不会重复收购；若离开数天归来，也规范结算 1 批，安全稳健。

---

## 📥 安装说明

1. 确认游戏已安装 `0_TFP_Harmony`（游戏自带或主流运行库）。
2. 将 `CustomVendingOverhaul` 文件夹放入你的 7 Days to Die `Mods/` 目录：
   ```text
   7 Days To Die/
     Mods/
       0_TFP_Harmony/
       CustomVendingOverhaul/
         ModInfo.xml
         CustomVendingOverhaul.dll
   ```
3. 启动游戏时请**关闭 EAC（反作弊）**（所有 Harmony DLL Mod 的通用要求）。

---

## 🛠️ 开源协议
MIT License. 100% 原生 C# Harmony 代码注入，无任何版权资产侵权。
